using System;
using System.Threading;
using System.Threading.Tasks;
using GameClient.Proto;
using UnityEngine;

namespace Game.Network
{
    /// <summary>
    /// 连接级状态机：Disconnected → Connecting → Authenticating → Authenticated。
    /// - 断线自动重连（指数退避 1s→2s→4s→…上限 15s，循环）。
    /// - 重连成功自动重放 WS LOGIN（token 复用）；LOGIN 失败（token 失效）触发 OnAuthFailed，
    ///   停止自动重连，由上层引导重新 REST 登录。
    /// - 业务级状态（Matching/InBattle）由上层维护；本机只负责“已认证”这一层。
    /// - OnReconnected：重连并重新认证成功后触发，上层据此发 BATTLE_REJOIN 恢复盘面。
    /// </summary>
    public sealed class ConnectionStateMachine : IDisposable
    {
        public enum State { Disconnected, Connecting, Authenticating, Authenticated, WaitingReconnect }

        private readonly GameConnection _connection;
        private readonly NetworkConfig _config;

        private string _token;
        private int _reconnectAttempt;
        private bool _wasAuthenticated;
        private bool _authFailed;
        // 可能被后台线程触发，统一用 TickCount64（无 Unity API 限制）
        private long _nextReconnectTick = -1;
        private State _state = State.Disconnected;
        private Task<GameMessage> _loginTask;
        // 构造发生在主线程（Awake）；后台线程触发的回调需要用它把续跑切回主线程
        private readonly SynchronizationContext _mainThreadContext;

        public State CurrentState => _state;
        public bool IsAuthenticated => _state == State.Authenticated;
        public bool AuthFailed => _authFailed;

        /// <summary>WS LOGIN 成功（首次或重连后）。</summary>
        public event Action Authenticated;

        /// <summary>断线重连并重新认证成功（区别于首次），上层据此发 BATTLE_REJOIN。</summary>
        public event Action Reconnected;

        /// <summary>token 失效（LOGIN 被拒），自动重连已停止。</summary>
        public event Action<string> OnAuthFailed;

        public ConnectionStateMachine(GameConnection connection, NetworkConfig config)
        {
            _connection = connection;
            _config = config;
            _mainThreadContext = SynchronizationContext.Current;
            _connection.Opened += OnTransportOpened;
            _connection.Closed += OnTransportClosed;
            _connection.InboundSilence += OnInboundSilence;
        }

        /// <summary>发起连接并认证（token 来自 REST 登录）。</summary>
        public void Connect(string token)
        {
            _token = token;
            _authFailed = false;
            _wasAuthenticated = false;
            _reconnectAttempt = 0;
            _nextReconnectTick = -1;
            Transition(State.Connecting);
            _ = ConnectInternalAsync();
        }

        /// <summary>主动断开（不自动重连）。</summary>
        public void Disconnect()
        {
            Transition(State.Disconnected);
            _nextReconnectTick = -1;
            _connection.Close("user disconnect");
        }

        /// <summary>主线程泵（转发 GameConnection.Update；重连计时也在此驱动）。</summary>
        public void Update()
        {
            _connection.Update();
            if (_state == State.WaitingReconnect && _nextReconnectTick > 0 && Clock.Millis() >= _nextReconnectTick)
            {
                _nextReconnectTick = -1;
                Transition(State.Connecting);
                _ = ConnectInternalAsync();
            }
        }

        private async Task ConnectInternalAsync()
        {
            try
            {
                await _connection.ConnectAsync(_config.WsUrl).ConfigureAwait(true);
                // Opened 事件由 transport 触发 → OnTransportOpened
            }
            catch (Exception e)
            {
                ScheduleReconnect("connect failed: " + e.Message);
            }
        }

        private void OnTransportOpened()
        {
            Transition(State.Authenticating);
            var login = new GameMessage
            {
                Type = MessageType.Login,
                Token = _token ?? string.Empty,
            };
            _loginTask = _connection.RequestAsync(login, 8000);
            // 续跑切回主线程（构造时捕获的 UnitySynchronizationContext）
            _loginTask.ContinueWith(t => _mainThreadContext.Post(_ => HandleLoginResult(t), null));
        }

        private void HandleLoginResult(Task<GameMessage> t)
        {
            if (t.IsFaulted || t.IsCanceled)
            {
                // 超时/断链：走重连退避
                ScheduleReconnect("login " + (t.Exception?.GetBaseException().Message ?? "canceled"));
                return;
            }

            var msg = t.Result;
            if (msg.Type == MessageType.LoginResponse && msg.LoginResponse.Success)
            {
                bool first = !_wasAuthenticated;
                _wasAuthenticated = true;
                _reconnectAttempt = 0;
                Transition(State.Authenticated);
                if (first)
                {
                    Authenticated?.Invoke();
                }
                else
                {
                    Reconnected?.Invoke();
                }

                return;
            }

            // LOGIN 被拒：token 失效或服务端拒绝——不自动重连
            var reason = msg.Type == MessageType.Error
                ? $"{msg.ErrorResponse.Code}: {msg.ErrorResponse.Message}"
                : msg.Type == MessageType.LoginResponse ? msg.LoginResponse.Message : $"unexpected {msg.Type}";
            _authFailed = true;
            Transition(State.Disconnected);
            OnAuthFailed?.Invoke(reason);
        }

        private void OnTransportClosed(string reason)
        {
            if (_state == State.Disconnected)
            {
                return; // 主动断开或已终态
            }

            ScheduleReconnect("closed: " + reason);
        }

        private void OnInboundSilence()
        {
            if (_state == State.Authenticated || _state == State.Authenticating)
            {
                _connection.Close("inbound silence 30s"); // Closed 事件将触发重连
            }
        }

        private void ScheduleReconnect(string reason)
        {
            if (_authFailed || string.IsNullOrEmpty(_token))
            {
                Transition(State.Disconnected);
                return;
            }

            _reconnectAttempt++;
            var delayMs = Math.Min(1000L << Math.Min(_reconnectAttempt - 1, 4), 15000L);
            Debug.Log($"[ConnState] reconnect #{_reconnectAttempt} in {delayMs / 1000f:0}s ({reason})");
            Transition(State.WaitingReconnect);
            _nextReconnectTick = Clock.Millis() + delayMs;
        }

        private void Transition(State next)
        {
            if (_state != next)
            {
                _state = next;
            }
        }

        public void Dispose()
        {
            _connection.Opened -= OnTransportOpened;
            _connection.Closed -= OnTransportClosed;
            _connection.InboundSilence -= OnInboundSilence;
        }
    }
}
