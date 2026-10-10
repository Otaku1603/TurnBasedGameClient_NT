using GameClient.Proto;
using Google.Protobuf;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using TurnBasedGame.Core;
using UnityEngine;

namespace TurnBasedGame.Network
{
    /// <summary>
    /// 网络底层管理器（单例）。
    /// 核心职责：
    /// 1. 维护 TCP 长连接（Socket）。
    /// 2. 解决 Unity 主线程与 Socket 接收线程的同步问题（使用并发队列）。
    /// 3. 处理 TCP 粘包/拆包（Length-Prefix 协议）。
    /// </summary>
    public class NetworkManager
    {
        private static NetworkManager _instance;
        public static NetworkManager Instance => _instance ??= new NetworkManager();

        private TcpClient _client;
        private Stream _stream;
        private Thread _receiveThread;
        private volatile bool _isRunning;

        /// <summary>
        /// 消息缓冲区（线程安全队列）。
        /// Socket 接收线程（后台）把消息塞进去，Unity 主线程（Update）取出来处理。
        /// 必须这么做，因为 Unity 的组件（如 UI Text）不能在后台线程操作。
        /// </summary>
        private readonly ConcurrentQueue<GameMessage> _messageQueue = new ConcurrentQueue<GameMessage>();

        public event Action<GameMessage> OnMessageReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;

        public bool IsConnected => _client != null && _client.Connected;

        /// <summary>
        /// 初始化 Socket 并连接服务器。
        /// 关键配置：
        /// - NoDelay = true: 禁用 Nagle 算法，回合制指令（如出牌）需要立即发送，不能等凑满包。
        /// - 开启后台线程 ReceiveLoop: 防止 Socket.Read 阻塞卡死 Unity 的主界面。
        /// </summary>
        public void Connect()
        {
            if (IsConnected) return;

            try
            {
                _client = new TcpClient();
                _client.ReceiveBufferSize = 8192;
                _client.SendBufferSize = 8192;
                _client.NoDelay = true; // 禁用 Nagle 算法，确保出牌指令立即发送，不凑包，降低操作延迟

                // 建立基础连接
                _client.Connect(AppConfig.SERVER_IP, AppConfig.TcpPort);

                Debug.Log($"[Network] Connecting to {AppConfig.SERVER_IP}:{AppConfig.TcpPort}...");

                // 获取基础流
                NetworkStream netStream = _client.GetStream();

                if (AppConfig.USE_SSL)
                {
                    Debug.Log("[Network] SSL Enabled. Starting Handshake...");
                    // 包装 SSL 流
                    SslStream sslStream = new SslStream(
                        netStream,
                        false,
                        (sender, certificate, chain, sslPolicyErrors) => true // 忽略证书错误
                    );

                    // SSL 握手
                    sslStream.AuthenticateAsClient("localhost");
                    _stream = sslStream; // 使用加密流

                    Debug.Log("[Network] <color=green>SSL Handshake Success!</color>");
                }
                else
                {
                    Debug.LogWarning("[Network] SSL Disabled. Using plain socket.");
                    _stream = netStream; // 直接使用原始流
                }

                _isRunning = true;
                // 创建后台线程处理消息接收，避免阻塞主线程
                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.IsBackground = true;
                _receiveThread.Start();

                Debug.Log("[Network] Socket Thread Started.");

                OnConnected?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Network] Connection failed: {e.Message}");
                Disconnect();
            }
        }

        /// <summary>
        /// 处理SSL证书验证
        /// </summary>
        private bool ValidateCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // 开发或测试阶段：直接返回 true，允许自签名证书
            // 生产环境：应该检查 sslPolicyErrors == SslPolicyErrors.None
            return true;
        }

        /// <summary>
        /// 发送消息（封包逻辑）。
        /// 协议格式：[Varint32 长度] + [Protobuf 数据体]。
        /// 先写长度是为了让接收端知道这条消息有多长，解决 TCP 粘包问题。
        /// </summary>
        /// <param name="msg">Protobuf 生成的消息对象</param>
        public void Send(GameMessage msg)
        {
            if (!IsConnected)
            {
                Debug.LogError("[Network] Cannot send: Not connected.");
                return;
            }

            try
            {
                byte[] bodyBytes = msg.ToByteArray();
                int length = bodyBytes.Length;

                using (var ms = new MemoryStream(length + 5))
                {
                    // 写入Varint32格式的长度前缀
                    WriteVarint32(ms, length);
                    ms.Write(bodyBytes, 0, length);

                    byte[] packet = ms.ToArray();
                    _stream.Write(packet, 0, packet.Length);
                    _stream.Flush();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Network] Send Error: {e.Message}");
                Disconnect();
            }
        }

        // 发送心跳包，维持TCP连接活性
        public void SendHeartbeat()
        {
            if (!IsConnected) return;

            var msg = new GameMessage
            {
                Type = MessageType.Heartbeat,
                Token = Services.AuthService.Instance.CurrentToken ?? "",
                Heartbeat = new Heartbeat
                {
                    ClientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            };

            Send(msg);
        }

        // 写入Varint32格式的整数到流中
        private void WriteVarint32(Stream stream, int value)
        {
            while (true)
            {
                if ((value & ~0x7F) == 0)
                {
                    stream.WriteByte((byte)value);
                    return;
                }
                else
                {
                    stream.WriteByte((byte)((value & 0x7F) | 0x80));
                    value >>= 7;
                }
            }
        }

        /// <summary>
        /// 接收循环，运行在独立线程中
        /// 采用分包协议：先读长度，再读对应长度的消息体
        /// </summary>
        private void ReceiveLoop()
        {
            while (_isRunning && IsConnected)
            {
                try
                {
                    // 1. 先读头部长度（Varint32），确定消息体有多大
                    int bodyLength = ReadVarint32(_stream);
                    if (bodyLength < 0) throw new IOException("End of stream (Length)");

                    // 2. 循环读取消息体
                    // TCP 是流式的，Read(8192) 可能只收到 100 字节，必须循环读满 bodyLength 为止。
                    byte[] body = new byte[bodyLength];
                    int totalRead = 0;
                    while (totalRead < bodyLength)
                    {
                        int read = _stream.Read(body, totalRead, bodyLength - totalRead);
                        if (read <= 0) throw new IOException("End of stream (Body)");
                        totalRead += read;
                    }

                    // 3. 反序列化，放入队列等待主线程处理
                    GameMessage msg = GameMessage.Parser.ParseFrom(body);
                    _messageQueue.Enqueue(msg);
                }
                catch (Exception e)
                {
                    // 只在运行中时报告错误，避免断开连接时的正常异常
                    if (_isRunning)
                    {
                        Debug.LogWarning($"[Network] Receive Error: {e.Message}");
                        _isRunning = false;
                    }
                    break;
                }
            }

            // 循环结束，清理资源
            if (!_isRunning)
            {
                CloseSocket();
            }
        }

        /// <summary>
        /// 读取Varint32格式的整数
        /// </summary>
        private int ReadVarint32(Stream stream)
        {
            int result = 0;
            int shift = 0;
            int b;
            do
            {
                if (shift >= 32) throw new FormatException("Varint too long");
                b = stream.ReadByte();
                if (b == -1) return -1; // EOF

                result |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);

            return result;
        }

        // 断开连接并清理资源
        public void Disconnect()
        {
            Debug.Log("[Network] Disconnecting...");
            _isRunning = false;
            CloseSocket();
            OnDisconnected?.Invoke();
        }

        // 安全关闭Socket资源
        private void CloseSocket()
        {
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            _client = null;
        }

        public void Update()
        {
            // 将后台线程接收到的消息分发给主线程，防止 UI 操作报错
            while (_messageQueue.TryDequeue(out GameMessage msg))
            {
                OnMessageReceived?.Invoke(msg);
            }

            // 断线检测补充：如果运行标志为false但client还存在，则触发断开
            if (!_isRunning && _client != null)
            {
                Disconnect();
            }
        }
    }
}