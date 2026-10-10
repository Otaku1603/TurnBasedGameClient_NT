using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Network
{
    /// <summary>
    /// WebSocket 传输层（协议 v2：二进制帧，帧内 = 完整 GameMessage protobuf，无长度前缀）。
    /// 职责仅限：建链、收发、断链通知——不做任何业务判断（推送/请求竞态由上层处理）。
    /// 后台线程收发，入站帧经 ConcurrentQueue 由主线程泵取（与旧 TCP 网络层同模式）。
    /// </summary>
    public sealed class WsTransport : IDisposable
    {
        private const int ReceiveBufferSize = 64 * 1024;

        private readonly ConcurrentQueue<byte[]> _incoming = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private volatile bool _running;
        /** Closed 事件只发一次（Close/ReceiveLoop/send 失败多条路径汇聚到 NotifyClosed）。 */
        private bool _closedNotified;

        /// <summary>连接已建立（可能在后台线程触发，订阅方须线程安全）。</summary>
        public event Action Opened;

        /// <summary>连接断开（可能在后台线程触发）。reason 为可读说明。</summary>
        public event Action<string> Closed;

        public bool IsOpen => _ws != null && _ws.State == WebSocketState.Open;

        public async Task ConnectAsync(string url)
        {
            DisposeSocket();
            _closedNotified = false;
            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();

            // 自签证书（wss 开发环境）：Mono/编辑器下走全局回调旁路
            BypassCertificateHandler.InstallOnce();

            await _ws.ConnectAsync(new Uri(url), _cts.Token).ConfigureAwait(false);

            _running = true;
            _ = Task.Run(() => ReceiveLoop(_ws, _cts.Token));
            UnityEngine.Debug.Log($"[WsTransport#{GetHashCode():x8}] opened {url}");
            Opened?.Invoke();
        }

        /// <summary>发送一帧（fire-and-forget）。未连接时静默丢弃，由上层状态机处理重试。</summary>
        public async void Send(byte[] frame)
        {
            if (!IsOpen || frame == null)
            {
                return;
            }

            // ClientWebSocket 不允许并发 send，串行化
            try
            {
                await _sendLock.WaitAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return; // 已 Dispose
            }

            try
            {
                var ws = _ws;
                var token = _cts != null ? _cts.Token : CancellationToken.None;
                if (ws == null)
                {
                    return;
                }

                await ws.SendAsync(
                    new ArraySegment<byte>(frame),
                    WebSocketMessageType.Binary,
                    true,
                    token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // 关连接竞态下 _ws/_cts 可能已被释放；这里必须无条件吞掉——
                // async void 中逃逸的异常会成为未处理异常（旧写法只在 _running 时捕获）。
                if (_running)
                {
                    NotifyClosed("send failed: " + e.Message);
                }
            }
            finally
            {
                try
                {
                    _sendLock.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Dispose 与在途 send 竞争，忽略
                }
            }
        }

        /// <summary>主线程泵：取走一条已收到的原始帧。</summary>
        public bool TryDequeue(out byte[] frame) => _incoming.TryDequeue(out frame);

        public void Close(string reason = "client close")
        {
            NotifyClosed(reason);
            _running = false;
            try
            {
                _cts?.Cancel();
            }
            catch
            {
                // ignore
            }

            DisposeSocket();
        }

        private async void ReceiveLoop(ClientWebSocket ws, CancellationToken token)
        {
            var buffer = new byte[ReceiveBufferSize];
            var frameBuilder = new System.IO.MemoryStream(ReceiveBufferSize);

            while (_running && ws.State == WebSocketState.Open)
            {
                try
                {
                    frameBuilder.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            throw new System.IO.IOException("server close frame");
                        }

                        frameBuilder.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    _incoming.Enqueue(frameBuilder.ToArray());
                }
                catch (Exception e)
                {
                    if (_running)
                    {
                        NotifyClosed("receive failed: " + e.Message);
                    }

                    break;
                }
            }
        }

        private void NotifyClosed(string reason)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            if (!_closedNotified)
            {
                _closedNotified = true;
                UnityEngine.Debug.Log($"[WsTransport#{GetHashCode():x8}] closed: {reason}");
                Closed?.Invoke(reason);
            }
        }

        private void DisposeSocket()
        {
            try
            {
                _ws?.Dispose();
            }
            catch
            {
                // ignore
            }

            _ws = null;
            try
            {
                _cts?.Dispose();
            }
            catch
            {
                // ignore
            }

            _cts = null;
        }

        public void Dispose()
        {
            Close("disposed");
            _sendLock.Dispose();
        }
    }
}
