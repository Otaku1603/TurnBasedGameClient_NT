using System;
using Game.Network;
using GameClient.Proto;
using UnityEngine;

namespace Game.App
{
    /// <summary>
    /// 断线恢复（M4）：ConnState.Reconnected（WS 重连+LOGIN 成功）→ 自动 BATTLE_REJOIN。
    /// - 战斗中恢复：REJOIN 响应成功 → 转 BattleStart 上下文，通知战斗场景重挂盘面；
    ///   失败（战斗已结束/不存在）→ 什么都不做（BATTLE_END 推送或回大厅兜底）。
    /// - 服务端语义：REJOIN 幂等可重发（HANDOVER §3）。
    /// </summary>
    public sealed class ReconnectRecovery : IDisposable
    {
        private readonly ConnectionStateMachine _connState;
        private readonly BattleService _battles;

        /// <summary>战斗中重连且盘面已恢复（BattleScene 订阅：重挂 Sim + 刷新 HUD）。</summary>
        public event Action<BattleStartResponse> BattleRecovered;

        public ReconnectRecovery(ConnectionStateMachine connState, BattleService battles)
        {
            _connState = connState;
            _battles = battles;
            _connState.Reconnected += OnReconnected;
            _battles.BattleRejoinResponse += OnRejoinResponse;
        }

        private void OnReconnected()
        {
            Debug.Log("[Recovery] reconnected, probing active battle...");
            _battles.Rejoin();
        }

        private void OnRejoinResponse(BattleRejoinResponse resp)
        {
            if (!resp.Success)
            {
                Debug.Log("[Recovery] no active battle after reconnect");
                return;
            }

            // 盘面完整性守卫：不带双方数据的 success 包（如跨节点转发途中的占位回包）
            // 不得覆盖 CurrentBattle——用 null 玩家构造盘面会在战斗场景初始化时 NPE
            if (resp.Player1 == null || resp.Player2 == null)
            {
                Debug.LogWarning("[Recovery] rejoin response without snapshot, awaiting authoritative push");
                return;
            }

            var start = new BattleStartResponse
            {
                BattleId = resp.BattleId,
                Player1 = resp.Player1,
                Player2 = resp.Player2,
                CurrentActorUserId = resp.CurrentActorUserId,
                CurrentRound = resp.CurrentRound,
            };
            _battles.CurrentBattle = start;
            Debug.Log($"[Recovery] battle recovered: {resp.BattleId} round={resp.CurrentRound}");
            BattleRecovered?.Invoke(start);
        }

        public void Dispose()
        {
            _connState.Reconnected -= OnReconnected;
            _battles.BattleRejoinResponse -= OnRejoinResponse;
        }
    }
}
