using System;
using System.Collections.Generic;
using System.Linq;
using Game.Network;
using GameClient.Proto;
using UnityEngine;

namespace Game.App
{
    /// <summary>
    /// 匹配 + 战斗指令（协议 v2）：
    /// - 请求侧：MATCH_REQUEST / MATCH_CANCEL / BATTLE_READY / BATTLE_ACTION / BATTLE_SURRENDER / BATTLE_REJOIN。
    ///   服务端对成功操作**不回直接响应帧**，结果一律经推送（MATCH_SUCCESS/BATTLE_START/UPDATE/END/REJOIN_RESPONSE），
    ///   因此这里全部 fire-and-forget（服务端对重复指令安全，客户端按推送驱动状态）。
    /// - 推送侧：经 PushEventBus 订阅后转强类型事件。
    /// - 服务端权威：客户端不预判伤害，UPDATE 的 HP/description 是唯一事实（HANDOVER §6.2）。
    /// - 发送节流：战斗操作 30 次/10s、匹配 10 次/min（RateLimiter.Shared）。
    /// </summary>
    public sealed class BattleService : IDisposable
    {
        private readonly GameConnection _connection;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        public event Action<MatchSuccessResponse> MatchSuccess;
        public event Action<BattleStartResponse> BattleStart;
        public event Action<BattleUpdateResponse> BattleUpdate;
        public event Action<BattleEndResponse> BattleEnd;
        public event Action<BattleRejoinResponse> BattleRejoinResponse;

        /// <summary>最近一次 BATTLE_START/REJOIN 的盘面（进战斗场景的上下文）。M3 起由快照系统管理。</summary>
        public BattleStartResponse CurrentBattle { get; set; }

        /// <summary>最近一次 BATTLE_UPDATE（回合循环/测试用）。</summary>
        public BattleUpdateResponse LastUpdate { get; private set; }

        public string CurrentBattleId => CurrentBattle?.BattleId;

        /// <summary>本机玩家的最新盘面数据（Start 或 Update 中较新者）。</summary>
        public BattlePlayerData SelfPlayerData
        {
            get
            {
                if (LastUpdate != null)
                {
                    return PickSelf(LastUpdate.Player1, LastUpdate.Player2);
                }

                if (CurrentBattle != null)
                {
                    return PickSelf(CurrentBattle.Player1, CurrentBattle.Player2);
                }

                return null;
            }
        }

        public long SelfUserId { get; set; }

        public BattleService(GameConnection connection, PushEventBus pushBus)
        {
            _connection = connection;
            _subscriptions.Add(pushBus.Subscribe(MessageType.MatchSuccess, m => MatchSuccess?.Invoke(m.MatchSuccessResponse)));
            _subscriptions.Add(pushBus.Subscribe(MessageType.BattleStart, m => OnStart(m.BattleStartResponse)));
            _subscriptions.Add(pushBus.Subscribe(MessageType.BattleUpdate, m =>
            {
                LastUpdate = m.BattleUpdateResponse;
                BattleUpdate?.Invoke(LastUpdate);
            }));
            _subscriptions.Add(pushBus.Subscribe(MessageType.BattleEnd, m => BattleEnd?.Invoke(m.BattleEndResponse)));
            _subscriptions.Add(pushBus.Subscribe(MessageType.BattleRejoinResponse, m => BattleRejoinResponse?.Invoke(m.BattleRejoinResponse)));
        }

        private void OnStart(BattleStartResponse start)
        {
            CurrentBattle = start;
            LastUpdate = null;
            BattleStart?.Invoke(start);
        }

        // ---------- 请求（fire-and-forget；结果经推送） ----------

        /// <summary>
        /// 请求匹配。
        ///
        /// @return false = 被限流拦截或未连接（调用方应复位匹配 UI，不进入等待态）
        /// </summary>
        public bool RequestMatch()
        {
            if (!Throttle(RateLimiter.MatchRequest))
            {
                return false;
            }

            return Send(new GameMessage { Type = MessageType.MatchRequest });
        }

        public void CancelMatch()
        {
            Send(new GameMessage { Type = MessageType.MatchCancel });
        }

        public void Ready(string battleId)
        {
            Send(new GameMessage
            {
                Type = MessageType.BattleReady,
                BattleReadyRequest = new BattleReadyRequest { BattleId = battleId },
            });
        }

        /// <summary>战斗操作（USE_SKILL/DEFEND/USE_ITEM；paramId=技能ID/道具ID，防御填 0）。</summary>
        public void Action(string battleId, ActionType actionType, int paramId)
        {
            if (!Throttle(RateLimiter.BattleAction))
            {
                return;
            }

            Send(new GameMessage
            {
                Type = MessageType.BattleAction,
                BattleActionRequest = new BattleActionRequest
                {
                    BattleId = battleId,
                    ActionType = actionType,
                    ParamId = paramId,
                },
            });
        }

        public void Surrender(string battleId)
        {
            Send(new GameMessage
            {
                Type = MessageType.BattleSurrender,
                BattleSurrenderRequest = new BattleSurrenderRequest { BattleId = battleId },
            });
        }

        /// <summary>
        /// 断线重连恢复盘面（登录后探测与重连共用；结果经 BATTLE_REJOIN_RESPONSE 推送）。
        /// </summary>
        public void Rejoin()
        {
            Send(new GameMessage { Type = MessageType.BattleRejoin });
        }

        // ---------- 可用性计算（服务端权威数据的纯函数） ----------

        /// <summary>可用技能 = ownedSkillIds − cooldowns.keys（HANDOVER §6.2）。</summary>
        public static List<int> UsableSkillIds(BattlePlayerData self)
        {
            if (self == null)
            {
                return new List<int>();
            }

            return self.OwnedSkillIds.Where(id => !self.Cooldowns.ContainsKey(id)).ToList();
        }

        private bool Send(GameMessage msg)
        {
            if (!_connection.IsOpen)
            {
                Debug.LogWarning("[BattleService] not connected, drop " + msg.Type);
                return false;
            }

            _connection.Send(msg);
            return true;
        }

        /// <summary>占用一个限流名额。false = 窗口已满，应拦截不发（避免服务端 RATE_LIMITED）。</summary>
        private bool Throttle(string key)
        {
            if (!RateLimiter.Shared.TryAcquire(key))
            {
                Debug.LogWarning("[BattleService] rate limited, dropped: " + key);
                return false;
            }

            return true;
        }

        private BattlePlayerData PickSelf(BattlePlayerData p1, BattlePlayerData p2)
        {
            if (SelfUserId == 0)
            {
                return null;
            }

            return p1 != null && p1.UserId == SelfUserId ? p1 : p2;
        }

        public void Dispose()
        {
            foreach (var sub in _subscriptions)
            {
                sub.Dispose();
            }

            _subscriptions.Clear();
        }
    }
}
