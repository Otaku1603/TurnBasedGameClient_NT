using System;
using System.Collections.Concurrent;
using System.Threading;
using GameClient.Proto;

namespace Game.Network
{
    /// <summary>
    /// 推送事件总线：requestId=0 的服务端推送按 MessageType 分发。
    /// 解耦 Network 层与 Sim/UI 层——任何模块都只订阅事件，不直接触碰传输。
    /// </summary>
    public sealed class PushEventBus
    {
        private readonly ConcurrentDictionary<MessageType, ConcurrentDictionary<int, Action<GameMessage>>> _subscribers
            = new ConcurrentDictionary<MessageType, ConcurrentDictionary<int, Action<GameMessage>>>();
        private int _nextSubscriptionId;

        /// <summary>所有推送（含已按 type 分发的），调试/日志用。</summary>
        public event Action<GameMessage> AnyPush;

        /// <summary>订阅某类推送，返回取消订阅句柄。</summary>
        public IDisposable Subscribe(MessageType type, Action<GameMessage> handler)
        {
            int id = Interlocked.Increment(ref _nextSubscriptionId);
            var map = _subscribers.GetOrAdd(type, _ => new ConcurrentDictionary<int, Action<GameMessage>>());
            map[id] = handler;
            return new Unsubscriber(this, type, id);
        }

        public void Publish(GameMessage msg)
        {
            AnyPush?.Invoke(msg);
            if (_subscribers.TryGetValue(msg.Type, out var map))
            {
                foreach (var handler in map.Values)
                {
                    try
                    {
                        handler(msg);
                    }
                    catch (Exception e)
                    {
                        // 单个订阅者异常不得影响分发链
                        UnityEngine.Debug.LogWarning($"[PushEventBus] handler error for {msg.Type}: {e.Message}");
                    }
                }
            }
        }

        private sealed class Unsubscriber : IDisposable
        {
            private PushEventBus _bus;
            private readonly MessageType _type;
            private readonly int _id;

            public Unsubscriber(PushEventBus bus, MessageType type, int id)
            {
                _bus = bus;
                _type = type;
                _id = id;
            }

            public void Dispose()
            {
                if (_bus != null && _bus._subscribers.TryGetValue(_type, out var map))
                {
                    map.TryRemove(_id, out _);
                }

                _bus = null;
            }
        }
    }
}
