using Unity.Burst;
using Unity.Entities;

namespace Game.Sim
{
    /// <summary>
    /// 回合门控（Burst ISystem，每帧）：写 IsMyTurn（本机是否行动方）。
    /// 服务端权威——本系统仅做表现层门控，非法操作仍由服务端拒绝。
    /// </summary>
    [BurstCompile]
    public partial struct TurnGateSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BattleRound>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var round = SystemAPI.GetSingleton<BattleRound>();
            round.IsMyTurn =
                round.Ended == 0 && round.CurrentActorUserId == round.SelfUserId
                    ? (byte)1
                    : (byte)0;
            SystemAPI.SetSingleton(round);
        }
    }
}
