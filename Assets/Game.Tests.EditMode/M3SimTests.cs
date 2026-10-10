using System.Collections;
using System.Threading.Tasks;
using Game.App;
using Game.Network;
using Game.Sim;
using GameClient.Proto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode
{
    /// <summary>M3 验收：Sim 世界快照→ECS→门控/倒计时，对接真实战斗流（无 UI）。</summary>
    public class M3SimTests
    {
        [Test]
        public void SimWorld_StartUpdateGate_Flow()
        {
            using (var sim = new BattleSimWorld())
            {
                long selfId = 100;
                long enemyId = 200;

                var start = new BattleStartResponse
                {
                    BattleId = "b-sim",
                    CurrentRound = 1,
                    CurrentActorUserId = selfId,
                    Player1 = MakePlayer(selfId, 100, 100, 7, 9),
                    Player2 = MakePlayer(enemyId, 80, 80, 7),
                };
                sim.ApplyStart(start, selfId);
                sim.Tick(); // TurnGateSystem 写回 IsMyTurn

                var ui = sim.ReadUiState();
                Assert.IsTrue(ui.IsMyTurn, "self should act first");
                Assert.AreEqual(100, ui.SelfHp);
                Assert.AreEqual(80, ui.EnemyHp);
                Assert.Greater(ui.TurnRemaining, 29f);

                // 打一回合：服务端 UPDATE 权威覆盖 + 回合指针切换
                var update = new BattleUpdateResponse
                {
                    BattleId = "b-sim",
                    CurrentRound = 1,
                    NextActorUserId = enemyId,
                    Player1 = MakePlayer(selfId, 100, 100, 7, 2),
                    Player2 = MakePlayer(enemyId, 60, 80, 7),
                };
                sim.ApplyUpdate(update);
                sim.Tick();

                ui = sim.ReadUiState();
                Assert.IsFalse(ui.IsMyTurn, "after self act, opponent turn");
                Assert.AreEqual(100, ui.SelfHp);
                Assert.AreEqual(60, ui.EnemyHp);

                // CD 权威覆盖（skill 7 进入 2 回合 CD）
                var cds = sim.GetCooldowns();
                Assert.AreEqual(1, cds.Count);
                Assert.AreEqual(2, cds[0].Remain);

                // 结束标记
                sim.MarkEnded();
                Assert.IsTrue(sim.ReadUiState().Ended);
            }
        }

        private static BattlePlayerData MakePlayer(long userId, int hp, int maxHp, int skillId, int cdRemain = 0)
        {
            var p = new BattlePlayerData
            {
                UserId = userId,
                CurrentHp = hp,
                MaxHp = maxHp,
                IsAlive = hp > 0,
            };
            p.OwnedSkillIds.Add(skillId);
            if (cdRemain > 0)
            {
                p.Cooldowns[skillId] = cdRemain;
            }

            return p;
        }
    }
}
