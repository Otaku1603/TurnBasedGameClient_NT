using Unity.Burst;
using Unity.Entities;

namespace Game.Sim
{
    /// <summary>
    /// 回合倒计时（Burst ISystem，每帧）：TurnRemaining -= deltaTime。
    /// 服务端 30s 超时判负，此倒计时仅为 UI 展示；到 0 不触发本地判负（等 END 推送）。
    /// </summary>
    [BurstCompile]
    public partial struct TurnCountdownSystem : ISystem
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
            if (round.Ended == 0 && round.TurnRemaining > 0f)
            {
                round.TurnRemaining -= SystemAPI.Time.DeltaTime;
                SystemAPI.SetSingleton(round);
            }
        }
    }
}
