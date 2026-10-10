using Unity.Entities;

namespace Game.Sim
{
    /// <summary>
    /// 战斗 Sim 组件（服务端权威快照的客户端投影，表现最小重演）：
    /// BattleController 喂 BATTLE_START/BATTLE_UPDATE → 组件状态；
    /// UI 每帧从 UiState（由系统写回的 singleton）渲染，禁止直接读网络消息。
    /// </summary>

    /// <summary>回合门控 singleton：谁行动 / 本机是否可操作 / 回合剩余秒数。</summary>
    public struct BattleRound : IComponentData
    {
        public int CurrentRound;
        public long CurrentActorUserId;
        public long SelfUserId;
        /// <summary>本回合操作倒计时（秒，本地计时；服务端 30s 超时判负）。</summary>
        public float TurnRemaining;
        /// <summary>TurnGateSystem 每帧写回：1=轮到本机。</summary>
        public byte IsMyTurn;
        /// <summary>战斗是否已结束（BATTLE_END 后置 1）。</summary>
        public byte Ended;
    }

    /// <summary>玩家生命值。</summary>
    public struct PlayerVitals : IComponentData
    {
        public long UserId;
        public int Hp;
        public int MaxHp;
        public byte IsSelf;
        public byte IsAlive;
    }

    /// <summary>本机技能冷却（每技能一实体；服务端每 UPDATE 权威下发）。</summary>
    public struct SkillCooldown : IComponentData
    {
        public int SkillId;
        public int Remain;
    }

    /// <summary>UI 每帧读取的快照（struct 拷贝，无 GC）。</summary>
    public struct BattleUiState
    {
        public int CurrentRound;
        public bool IsMyTurn;
        public float TurnRemaining;
        public bool Ended;
        public int SelfHp;
        public int SelfMaxHp;
        public int EnemyHp;
        public int EnemyMaxHp;
    }
}
