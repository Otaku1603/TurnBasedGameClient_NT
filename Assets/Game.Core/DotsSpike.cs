using Unity.Burst;
using Unity.Entities;

namespace Game.Core
{
    /// <summary>M0 DOTS spike：验证 2022.3.62f3 + Entities 1.3.15 + Burst 全链路（建实体 / ISystem / 源生成）。</summary>
    public struct SpikeHp : IComponentData
    {
        public int Value;
    }

    /// <summary>Unmanaged ISystem：Burst 编译路径冒烟。仅 M0 验证用，M3 起由真实 Sim System 取代。</summary>
    [BurstCompile]
    public partial struct SpikeDoubleSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var hp in SystemAPI.Query<RefRW<SpikeHp>>())
            {
                hp.ValueRW.Value *= 2;
            }
        }
    }
}
