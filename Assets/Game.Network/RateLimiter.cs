using System;
using System.Collections.Generic;

namespace Game.Network
{
    /// <summary>
    /// 滑动窗口限流器（客户端发送节流，与服务端风控阈值对齐）：
    /// 战斗操作 30 次/10s、匹配 10 次/min、聊天 8 条/10s。
    /// TryAcquire 失败时应直接拦截不发（避免服务端 RATE_LIMITED code=7）。
    /// </summary>
    public sealed class RateLimiter
    {
        public static readonly RateLimiter Shared = new RateLimiter()
            .Register(BattleAction, 30, TimeSpan.FromSeconds(10))
            .Register(MatchRequest, 10, TimeSpan.FromMinutes(1))
            .Register(ChatMessage, 8, TimeSpan.FromSeconds(10));

        public const string BattleAction = "battleAction";
        public const string MatchRequest = "matchRequest";
        public const string ChatMessage = "chatMessage";

        private readonly Dictionary<string, Window> _windows = new Dictionary<string, Window>();

        public RateLimiter Register(string key, int limit, TimeSpan span)
        {
            _windows[key] = new Window(limit, span);
            return this;
        }

        /// <summary>尝试占用一个名额。false = 窗口已满，应拦截。</summary>
        public bool TryAcquire(string key, long nowUnixMs = 0)
        {
            if (!_windows.TryGetValue(key, out var window))
            {
                return true; // 未注册的 key 不限制
            }

            var now = nowUnixMs > 0 ? nowUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return window.TryAcquire(now);
        }

        /// <summary>窗口剩余可用名额（UI 提示用）。</summary>
        public int Remaining(string key, long nowUnixMs = 0)
        {
            if (!_windows.TryGetValue(key, out var window))
            {
                return int.MaxValue;
            }

            var now = nowUnixMs > 0 ? nowUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return window.Remaining(now);
        }

        private sealed class Window
        {
            private readonly int _limit;
            private readonly long _spanMs;
            private readonly Queue<long> _stamps = new Queue<long>();

            public Window(int limit, TimeSpan span)
            {
                _limit = limit;
                _spanMs = (long)span.TotalMilliseconds;
            }

            public bool TryAcquire(long nowMs)
            {
                Evict(nowMs);
                if (_stamps.Count >= _limit)
                {
                    return false;
                }

                _stamps.Enqueue(nowMs);
                return true;
            }

            public int Remaining(long nowMs)
            {
                Evict(nowMs);
                return _limit - _stamps.Count;
            }

            private void Evict(long nowMs)
            {
                while (_stamps.Count > 0 && nowMs - _stamps.Peek() >= _spanMs)
                {
                    _stamps.Dequeue();
                }
            }
        }
    }
}
