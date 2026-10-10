using System.Collections.Generic;
using Unity.Entities;
using GameClient.Proto;

namespace Game.Sim
{
    /// <summary>
    /// 战斗 Sim 世界封装（Entities 1.3）：
    /// - ApplyStart/ApplyUpdate：网络快照 → 组件状态（服务端权威，直接覆盖）；
    /// - Tick：驱动 TurnGate/TurnCountdown（BattleController 每帧调用，取代 MonoBehaviour.Update）；
    /// - ReadUiState / UsableSkillIds：UI 渲染查询（struct 拷贝，无 GC 分配）。
    /// 架构红线：BattleController/HUD 只读 UiState，不得直接消费网络消息。
    /// </summary>
    public sealed class BattleSimWorld : System.IDisposable
    {
        /// <summary>服务端每回合操作时限（秒），仅用于倒计时显示。</summary>
        public const float TurnTimeLimit = 30f;

        private readonly World _world;
        private readonly EntityManager _em;
        private Entity _roundEntity;
        private Entity _selfEntity;
        private Entity _enemyEntity;

        public BattleSimWorld()
        {
            _world = new World("BattleSim");
            _em = _world.EntityManager;
            _roundEntity = _em.CreateEntity(typeof(BattleRound));
            _em.SetComponentData(_roundEntity, new BattleRound
            {
                CurrentRound = 0,
                CurrentActorUserId = 0,
                SelfUserId = 0,
                TurnRemaining = 0f,
                IsMyTurn = 0,
                Ended = 0,
            });
            foreach (var sys in new[]
                     {
                         _world.CreateSystem<TurnGateSystem>(),
                         _world.CreateSystem<TurnCountdownSystem>(),
                     })
            {
                _world.GetOrCreateSystemManaged<SimulationSystemGroup>().AddSystemToUpdateList(sys);
            }
        }

        /// <summary>BATTLE_START / BATTLE_REJOIN：建玩家实体 + 初始盘面。</summary>
        public void ApplyStart(BattleStartResponse start, long selfUserId)
        {
            _em.DestroyEntity(_selfEntity);
            _em.DestroyEntity(_enemyEntity);
            _selfEntity = CreatePlayer(start.Player1, selfUserId);
            _enemyEntity = CreatePlayer(start.Player2, selfUserId);

            // 本机技能 CD 实体（Remain 以 start 中 cooldowns 为准）
            var cdQuery = _em.CreateEntityQuery(typeof(SkillCooldown));
            _em.DestroyEntity(cdQuery);
            cdQuery.Dispose();
            var selfData = start.Player1.UserId == selfUserId ? start.Player1 : start.Player2;
            SelfSnapshot = selfData;
            foreach (var skillId in selfData.OwnedSkillIds)
            {
                var e = _em.CreateEntity(typeof(SkillCooldown));
                _em.SetComponentData(e, new SkillCooldown
                {
                    SkillId = skillId,
                    Remain = selfData.Cooldowns.TryGetValue(skillId, out var remain) ? remain : 0,
                });
            }

            var round = _em.GetComponentData<BattleRound>(_roundEntity);
            round.CurrentRound = start.CurrentRound;
            round.CurrentActorUserId = start.CurrentActorUserId;
            round.SelfUserId = selfUserId;
            round.TurnRemaining = TurnTimeLimit;
            round.Ended = 0;
            _em.SetComponentData(_roundEntity, round);
        }

        /// <summary>BATTLE_UPDATE：覆盖双方 HP / 本机技能 CD / 回合指针。</summary>
        public void ApplyUpdate(BattleUpdateResponse update)
        {
            OverwritePlayer(_selfEntity, update.Player1.UserId == _em.GetComponentData<BattleRound>(_roundEntity).SelfUserId
                ? update.Player1
                : update.Player2);
            OverwritePlayer(_enemyEntity, update.Player1.UserId == _em.GetComponentData<BattleRound>(_roundEntity).SelfUserId
                ? update.Player2
                : update.Player1);

            var round = _em.GetComponentData<BattleRound>(_roundEntity);
            round.CurrentRound = update.CurrentRound;
            round.CurrentActorUserId = update.NextActorUserId;
            round.TurnRemaining = TurnTimeLimit;
            _em.SetComponentData(_roundEntity, round);

            // 本机技能 CD 权威覆盖
            var selfUserId = _em.GetComponentData<BattleRound>(_roundEntity).SelfUserId;
            var selfData = update.Player1.UserId == selfUserId ? update.Player1 : update.Player2;
            SelfSnapshot = selfData;
            var cdQuery = _em.CreateEntityQuery(typeof(SkillCooldown));
            var cds = cdQuery.ToComponentDataArray<SkillCooldown>(Unity.Collections.Allocator.Temp);
            var entities = cdQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < cds.Length; i++)
            {
                var cd = cds[i];
                cd.Remain = selfData.Cooldowns.TryGetValue(cd.SkillId, out var remain) ? remain : 0;
                _em.SetComponentData(entities[i], cd);
            }

            cds.Dispose();
            entities.Dispose();
            cdQuery.Dispose();
        }

        public void MarkEnded()
        {
            var round = _em.GetComponentData<BattleRound>(_roundEntity);
            round.Ended = 1;
            _em.SetComponentData(_roundEntity, round);
        }

        /// <summary>每帧驱动（TurnGate/TurnCountdown），由 BattleController.Update 调用。</summary>
        public void Tick()
        {
            _world.Update();
        }

        /// <summary>本机技能 CD 列表（UI 技能按钮渲染用）。</summary>
        public List<(int SkillId, int Remain)> GetCooldowns()
        {
            var result = new List<(int, int)>();
            var cdQuery = _em.CreateEntityQuery(typeof(SkillCooldown));
            var cds = cdQuery.ToComponentDataArray<SkillCooldown>(Unity.Collections.Allocator.Temp);
            foreach (var cd in cds)
            {
                result.Add((cd.SkillId, cd.Remain));
            }

            cds.Dispose();
            cdQuery.Dispose();
            return result;
        }

        /// <summary>本机玩家最新盘面（ownedSkillIds 来源；网络快照数据不进 Sim）。</summary>
        public BattlePlayerData SelfSnapshot { get; private set; }

        /// <summary>UI 渲染快照（struct 拷贝）。</summary>
        public BattleUiState ReadUiState()
        {
            var round = _em.GetComponentData<BattleRound>(_roundEntity);
            var state = new BattleUiState
            {
                CurrentRound = round.CurrentRound,
                IsMyTurn = round.IsMyTurn == 1,
                TurnRemaining = round.TurnRemaining,
                Ended = round.Ended == 1,
            };

            if (_selfEntity != Entity.Null && _em.HasComponent<PlayerVitals>(_selfEntity))
            {
                var self = _em.GetComponentData<PlayerVitals>(_selfEntity);
                state.SelfHp = self.Hp;
                state.SelfMaxHp = self.MaxHp;
            }

            if (_enemyEntity != Entity.Null && _em.HasComponent<PlayerVitals>(_enemyEntity))
            {
                var enemy = _em.GetComponentData<PlayerVitals>(_enemyEntity);
                state.EnemyHp = enemy.Hp;
                state.EnemyMaxHp = enemy.MaxHp;
            }

            return state;
        }

        /// <summary>可用技能 = ownedSkillIds − cooldowns.keys（服务端权威数据）。</summary>
        public List<int> UsableSkillIds(BattlePlayerData self)
        {
            var result = new List<int>();
            if (self == null)
            {
                return result;
            }

            foreach (var id in self.OwnedSkillIds)
            {
                if (!self.Cooldowns.ContainsKey(id))
                {
                    result.Add(id);
                }
            }

            return result;
        }

        private Entity CreatePlayer(BattlePlayerData data, long selfUserId)
        {
            var e = _em.CreateEntity(typeof(PlayerVitals));
            _em.SetComponentData(e, new PlayerVitals
            {
                UserId = data.UserId,
                Hp = data.CurrentHp,
                MaxHp = data.MaxHp,
                IsSelf = data.UserId == selfUserId ? (byte)1 : (byte)0,
                IsAlive = data.IsAlive ? (byte)1 : (byte)0,
            });
            return e;
        }

        private void OverwritePlayer(Entity e, BattlePlayerData data)
        {
            if (e == Entity.Null || !_em.HasComponent<PlayerVitals>(e))
            {
                return;
            }

            var vitals = _em.GetComponentData<PlayerVitals>(e);
            vitals.Hp = data.CurrentHp;
            vitals.MaxHp = data.MaxHp;
            vitals.IsAlive = data.IsAlive ? (byte)1 : (byte)0;
            _em.SetComponentData(e, vitals);
        }

        public void Dispose()
        {
            _world.Dispose();
        }
    }
}
