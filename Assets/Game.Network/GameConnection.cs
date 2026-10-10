using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using GameClient.Proto;
using Google.Protobuf;
using UnityEngine;

namespace Game.Network
{
    /// <summary>
    /// 游戏连接（协议 v2 信封语义）：
    /// - version=2；requestId 自增并维护 pending 表（超时即失败；服务端 C2 至多一次，重复指令安全，上层可重试）。
    /// - requestId=0 的入站帧 → PushEventBus；有 requestId 的 → 完成 pending（迟到/无主帧仍进总线兜底）。
    /// - 心跳 5s（含 RTT 采样）；入站静默 30s 判定连接丢失。
    /// - 必须由主线程调用 Update()（如 GameEntry）。
    /// </summary>
    public sealed class GameConnection : IDisposable
    {
        public const int ProtocolVersion = 2;
        private const int HeartbeatIntervalMs = 5000;
        private const int DefaultRequestTimeoutMs = 8000;
        private const int InboundSilenceTimeoutMs = 30000;

        private readonly WsTransport _transport;
        private readonly ConcurrentDictionary<long, PendingRequest> _pending
            = new ConcurrentDictionary<long, PendingRequest>();
        private long _nextRequestId;

        private float _lastInboundTime;
        private float _nextHeartbeatTime;
        private float _lastRttMs = -1f;

        /// <summary>连接建立（wss/ws 握手完成）。</summary>
        public event Action Opened;
        /// <summary>连接断开（任何原因）。reason 可读。</summary>
        public event Action<string> Closed;
        /// <summary>入站静默超时（30s 无任何服务端消息），上层应触发重连流程。</summary>
        public event Action InboundSilence;

        /// <summary>最近一次心跳 RTT（毫秒），-1 表示尚无样本。</summary>
        public float LastRttMs => _lastRttMs;

        public bool IsOpen => _transport.IsOpen;

        public GameConnection(WsTransport transport)
        {
            _transport = transport;
            _transport.Opened += () => Opened?.Invoke();
            _transport.Closed += reason =>
            {
                FailAllPending("connection closed");
                Closed?.Invoke(reason);
            };
        }

        public Task ConnectAsync(string url) => _transport.ConnectAsync(url);

        public void Close(string reason = "client close") => _transport.Close(reason);

        /// <summary>发送（自动填充 version=2 与 requestId），返回本次 requestId。</summary>
        public long Send(GameMessage msg)
        {
            if (msg.Version == 0)
            {
                msg.Version = ProtocolVersion;
            }

            if (msg.RequestId == 0 && IsRequestIdNeeded(msg.Type))
            {
                msg.RequestId = Interlocked.Increment(ref _nextRequestId);
            }

            Debug.Log($"[GameConnection#{GetHashCode():x8}] outbound type={msg.Type} reqId={msg.RequestId}");
            _transport.Send(msg.ToByteArray());
            return msg.RequestId;
        }

        /// <summary>发送并等待对应响应帧（requestId 关联）。超时抛 TimeoutException。</summary>
        public async Task<GameMessage> RequestAsync(GameMessage msg, int timeoutMs = DefaultRequestTimeoutMs)
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("not connected");
            }

            var requestId = Send(msg);
            var pending = new PendingRequest(timeoutMs, msg.Type);
            _pending[requestId] = pending;

            try
            {
                var completed = await pending.Task.ConfigureAwait(true);
                return completed;
            }
            finally
            {
                _pending.TryRemove(requestId, out _);
            }
        }

        /// <summary>主线程泵：入站分发 + pending 超时 + 心跳 + 静默检测。</summary>
        public void Update()
        {
            var now = Time.unscaledTime;

            while (_transport.TryDequeue(out var frame))
            {
                GameMessage msg;
                try
                {
                    msg = GameMessage.Parser.ParseFrom(frame);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GameConnection] bad frame: {e.Message}");
                    continue;
                }

                _lastInboundTime = now;
                HandleInbound(msg);
            }

            SweepPendingTimeouts();
            MaybeSendHeartbeat(now);
            if (_transport.IsOpen && _lastInboundTime > 0f && now - _lastInboundTime > InboundSilenceTimeoutMs / 1000f)
            {
                _lastInboundTime = now; // 防重复触发
                InboundSilence?.Invoke();
            }
        }

        private void HandleInbound(GameMessage msg)
        {
            Debug.Log($"[GameConnection#{GetHashCode():x8}] inbound type={msg.Type} reqId={msg.RequestId}");

            // ERROR 帧优先按 ErrorResponse.requestId 关联回 pending（proto 字段 3）
            if (msg.Type == MessageType.Error && msg.ErrorResponse != null && msg.ErrorResponse.RequestId != 0)
            {
                if (_pending.TryRemove(msg.ErrorResponse.RequestId, out var errorPending))
                {
                    errorPending.TryComplete(msg);
                    return;
                }
            }

            if (msg.RequestId != 0 && _pending.TryRemove(msg.RequestId, out var pending))
            {
                pending.TryComplete(msg);
                return;
            }

            // 推送（requestId=0）与无主迟到消息：一律进总线
            PushEventBusInstance.Publish(msg);
        }

        private void SweepPendingTimeouts()
        {
            foreach (var kv in _pending)
            {
                if (kv.Value.IsExpired())
                {
                    _pending.TryRemove(kv.Key, out _);
                    kv.Value.TryFail(new TimeoutException($"request {kv.Key} timed out (type={kv.Value.RequestType})"));
                }
            }
        }

        private void MaybeSendHeartbeat(float now)
        {
            if (!IsOpen || now < _nextHeartbeatTime)
            {
                return;
            }

            _nextHeartbeatTime = now + HeartbeatIntervalMs / 1000f;
            var sentAt = Time.unscaledTime;
            var heartbeat = new GameMessage
            {
                Type = MessageType.Heartbeat,
                Heartbeat = new Heartbeat { ClientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
            };
            _ = RequestAsync(heartbeat, HeartbeatIntervalMs).ContinueWith(t =>
            {
                // 响应帧 type 仍为 HEARTBEAT（双向），响应体为 HeartbeatResponse
                if (t.Status == TaskStatus.RanToCompletion &&
                    t.Result.Type == MessageType.Heartbeat && t.Result.HeartbeatResponse != null)
                {
                    _lastRttMs = (Time.unscaledTime - sentAt) * 1000f;
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static bool IsRequestIdNeeded(MessageType type)
        {
            // 客户端主动请求均需关联 ID；服务端推送类型客户端不该发送，但兜底不加
            switch (type)
            {
                case MessageType.Heartbeat:
                case MessageType.Login:
                case MessageType.MatchRequest:
                case MessageType.MatchCancel:
                case MessageType.BattleReady:
                case MessageType.BattleAction:
                case MessageType.BattleSurrender:
                case MessageType.BattleRejoin:
                    return true;
                default:
                    return false;
            }
        }

        private void FailAllPending(string reason)
        {
            foreach (var kv in _pending)
            {
                kv.Value.TryFail(new InvalidOperationException(reason));
            }

            _pending.Clear();
        }

        /// <summary>总线由 App 层装配注入（避免构造链耦合）。</summary>
        public PushEventBus PushEventBusInstance { get; set; } = new PushEventBus();

        public void Dispose()
        {
            _transport.Dispose();
            FailAllPending("disposed");
        }

        private sealed class PendingRequest
        {
            // 可能从 WS 后台线程发起请求，禁用 Unity API，统一用 TickCount64 计时
            private readonly long _expiresAtTick = Clock.Millis() + DefaultRequestTimeoutMs;
            private readonly TaskCompletionSource<GameMessage> _tcs
                = new TaskCompletionSource<GameMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<GameMessage> Task => _tcs.Task;
            public MessageType RequestType { get; }

            public PendingRequest(int timeoutMs, MessageType requestType)
            {
                _expiresAtTick = Clock.Millis() + timeoutMs;
                RequestType = requestType;
            }

            public bool IsExpired() => Clock.Millis() >= _expiresAtTick;

            public void TryComplete(GameMessage msg) => _tcs.TrySetResult(msg);

            public void TryFail(Exception e) => _tcs.TrySetException(e);
        }
    }
}
