using Game.Core;
using NUnit.Framework;
using Unity.Entities;

namespace Game.Tests.EditMode
{
    /// <summary>M0 DOTS spike 验收：无渲染环境下建实体、跑 Burst ISystem、断言确定性结果。</summary>
    public class DotsSpikeTests
    {
        [Test]
        public void SpikeDoubleSystem_DoublesHp()
        {
            using (var world = new World("SpikeTestWorld"))
            {
                var handle = world.CreateSystem<SpikeDoubleSystem>();
                world.GetOrCreateSystemManaged<SimulationSystemGroup>().AddSystemToUpdateList(handle);

                var entity = world.EntityManager.CreateEntity(typeof(SpikeHp));
                world.EntityManager.SetComponentData(entity, new SpikeHp { Value = 21 });

                world.Update();

                Assert.AreEqual(42, world.EntityManager.GetComponentData<SpikeHp>(entity).Value);
            }
        }
    }
}
