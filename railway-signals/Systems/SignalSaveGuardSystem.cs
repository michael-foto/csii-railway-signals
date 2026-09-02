using Game;
using Game.Common;
using RailwaySignals.Signalling;
using Unity.Collections;
using Unity.Entities;

namespace RailwaySignals.Systems
{
    /// <summary>
    /// Keeps signal objects out of the save file. Signals are derived from the track network and
    /// are replanned on load, so persisting them only bloats the save and leaves objects behind
    /// whose prefab is gone once the mod is uninstalled.
    /// <para>
    /// SerializerSystem takes every entity with a PrefabRef but skips anything carrying Deleted, so
    /// the parts wear Deleted for the length of the Serialize phase and have it taken off again
    /// afterwards. That phase runs as one synchronous pass over seven systems, none of which act on
    /// Deleted, and the entities keep their culling index and mesh batches, which destroying and
    /// recreating them would forfeit. Registered both before and after SerializerSystem: the first
    /// pass hides, the second restores.
    /// </para>
    /// </summary>
    public partial class SignalSaveGuardSystem : GameSystemBase
    {
        private EntityQuery m_PartQuery;

        private NativeList<Entity> m_Hidden;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PartQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<RailwaySignalPart>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
            m_Hidden = new NativeList<Entity>(256, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            m_Hidden.Dispose();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (m_Hidden.Length == 0)
            {
                NativeArray<Entity> parts = m_PartQuery.ToEntityArray(Allocator.Temp);
                m_Hidden.AddRange(parts);
                EntityManager.AddComponent<Deleted>(parts);
                parts.Dispose();
                return;
            }
            // Only the parts hidden above. A rebuild earlier in the frame can have marked others
            // Deleted for real, and those have to stay that way.
            EntityManager.RemoveComponent<Deleted>(m_Hidden.AsArray());
            m_Hidden.Clear();
        }
    }
}
