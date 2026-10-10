using System;
using System.Collections;
using System.Threading.Tasks;
using Game.App;
using Game.Network;
using GameClient.Proto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// M2 验收（HANDOVER §6.2 B0）：双账号注册 → 匹配 → READY → BATTLE_START →
    /// 按回合 USE_SKILL/DEFEND 互打 → BATTLE_END 全链路，直连真实双 VM 集群。
    /// 战斗请求为 fire-and-forget（服务端以推送驱动），泵循环持续驱动两条连接。
    /// </summary>
    public class M2BattleFlowSmokeTests
    {
        private const string ServerHost = "10.244.216.57";

        [UnityTest]
        public IEnumerator RealServer_TwoPlayerFullBattle()
        {
            if (!IsServerReachable())
            {
                Assert.Ignore("server unreachable, skip");
            }

            Task<bool> task = RunFullBattleAsync();
            yield return new WaitUntil(() => task.IsCompleted);
            Assert.IsTrue(task.Result, "full battle flow failed");
        }

        private static bool IsServerReachable()
        {
            try
            {
                using (var c = new System.Net.Sockets.TcpClient())
                {
                    var connect = c.ConnectAsync(ServerHost, 80);
                    return connect.Wait(3000);
                }
            }
            catch
            {
                return false;
            }
        }

        private sealed class PlayerCtx : IDisposable
        {
            public WsTransport Transport;
            public GameConnection Connection;
            public ConnectionStateMachine ConnState;
            public RestClient Rest;
            public AuthService Auth;
            public BattleService Battles;
            public GameDataService GameData;
            public TaskCompletionSource<bool> AuthTcs =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public long UserId;
            public string BattleId;
            public MatchSuccessResponse Match;
            public BattleStartResponse Start;
            public BattleEndResponse End;

            public void Wire()
            {
                ConnState.Authenticated += () => AuthTcs.TrySetResult(true);
                Battles.MatchSuccess += m => { if (Match == null) { Match = m; BattleId = m.BattleId; } };
                Battles.BattleStart += s => { if (Start == null) { Start = s; BattleId = s.BattleId; } };
                Battles.BattleEnd += e => { if (End == null) End = e; };
            }

            public void Pump() => ConnState.Update();

            public void Dispose()
            {
                ConnState?.Dispose();
                Battles?.Dispose();
                Connection?.Dispose();
            }
        }

        private static async Task<PlayerCtx> CreatePlayerAsync(string username, string password, string nickname)
        {
            var ctx = new PlayerCtx();
            var config = new NetworkConfig { Host = ServerHost, UseSsl = false };
            var pushBus = new PushEventBus();
            ctx.Transport = new WsTransport();
            ctx.Connection = new GameConnection(ctx.Transport) { PushEventBusInstance = pushBus };
            ctx.ConnState = new ConnectionStateMachine(ctx.Connection, config);
            ctx.Rest = new RestClient(config);
            ctx.Auth = new AuthService(ctx.Rest, ctx.ConnState);
            ctx.Battles = new BattleService(ctx.Connection, pushBus);
            ctx.GameData = new GameDataService(ctx.Rest);
            ctx.Wire();

            var register = await ctx.Auth.RegisterAsync(username, password, nickname).ConfigureAwait(true);
            Assert.IsTrue(register.IsOk, $"register failed: {register.Code} {register.Message}");
            ctx.UserId = ctx.Auth.UserId;

            var deadline = Clock.Millis() + 10000;
            while (!ctx.AuthTcs.Task.IsCompleted && Clock.Millis() < deadline)
            {
                ctx.Pump();
                await Task.Delay(50).ConfigureAwait(true);
            }

            Assert.IsTrue(ctx.AuthTcs.Task.IsCompleted && ctx.AuthTcs.Task.Result, "ws login failed");
            await ctx.GameData.LoadAllAsync().ConfigureAwait(true);
            return ctx;
        }

        private static async Task<bool> RunFullBattleAsync()
        {
            var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using (var p1 = await CreatePlayerAsync($"m2a_{stamp}", "test123456", $"P1{stamp % 1000:000}").ConfigureAwait(true))
            using (var p2 = await CreatePlayerAsync($"m2b_{stamp}", "test123456", $"P2{stamp % 1000:000}").ConfigureAwait(true))
            {
                var overallDeadline = Clock.Millis() + 180000;

                // 1. 双方入匹配队列（推送 MATCH_SUCCESS）
                Debug.Log("[M2] step1: both request match");
                p1.Battles.RequestMatch();
                p2.Battles.RequestMatch();

                while ((p1.Match == null || p2.Match == null) && Clock.Millis() < overallDeadline)
                {
                    p1.Pump();
                    p2.Pump();
                    await Task.Delay(100).ConfigureAwait(true);
                }

                if (p1.Match == null || p2.Match == null)
                {
                    Debug.LogError($"[M2] match timeout p1={p1.Match != null} p2={p2.Match != null}");
                    return false;
                }

                Debug.Log($"[M2] matched battleId={p1.BattleId} vs {p1.Match.Opponent?.Nickname}");

                // 2. 双方 READY（就绪后推送 BATTLE_START）
                p1.Battles.Ready(p1.BattleId);
                p2.Battles.Ready(p2.BattleId);
                Debug.Log("[M2] step2: both ready");

                while ((p1.Start == null || p2.Start == null) && Clock.Millis() < overallDeadline)
                {
                    p1.Pump();
                    p2.Pump();
                    await Task.Delay(100).ConfigureAwait(true);
                }

                if (p1.Start == null || p2.Start == null)
                {
                    Debug.LogError("[M2] battle start timeout");
                    return false;
                }

                Debug.Log($"[M2] step3: battle started, first actor={p1.Start.CurrentActorUserId}");

                // 3. 回合循环：当前行动者出招，直到 BATTLE_END
                long lastActionMs = 0;
                while (p1.End == null && Clock.Millis() < overallDeadline)
                {
                    p1.Pump();
                    p2.Pump();

                    var update = p1.Battles.LastUpdate;
                    long actor = update?.NextActorUserId ?? p1.Start.CurrentActorUserId;

                    // 节流出招（>400ms 间隔），服务端权威决定回合归属
                    if ((actor == p1.UserId || actor == p2.UserId) && Clock.Millis() - lastActionMs > 400)
                    {
                        var ctx = actor == p1.UserId ? p1 : p2;
                        var self = ctx.Battles.SelfPlayerData;
                        var usable = BattleService.UsableSkillIds(self);
                        if (usable.Count > 0)
                        {
                            ctx.Battles.Action(ctx.BattleId, ActionType.UseSkill, usable[0]);
                        }
                        else
                        {
                            ctx.Battles.Action(ctx.BattleId, ActionType.Defend, 0);
                        }

                        lastActionMs = Clock.Millis();
                    }

                    await Task.Delay(50).ConfigureAwait(true);
                }

                if (p1.End == null)
                {
                    Debug.LogError("[M2] battle loop timeout");
                    return false;
                }

                var end = p1.End;
                bool winnerValid = end.WinnerId == p1.UserId || end.WinnerId == p2.UserId;
                Debug.Log($"[M2] battle ended: winner={end.WinnerId} reason={end.EndReason} rounds={end.TotalRounds}");
                return winnerValid;
            }
        }
    }
}
