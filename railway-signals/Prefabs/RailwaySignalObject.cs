using System;
using System.Collections.Generic;
using Game.Prefabs;
using RailwaySignals.Signalling;
using Unity.Entities;

namespace RailwaySignals.Prefabs
{
    /// <summary>
    /// Adds a <see cref="RailwaySignalObjectData"/> tag to the prefabs used by the mod.
    /// Lets the mod query only its own entities, rather than all prefabs in the game
    /// </summary>
    [ComponentMenu("Objects/", new Type[] { typeof(StaticObjectPrefab) })]
    public class RailwaySignalObject : ComponentBase
    {
        public SignalAsset m_Asset;

        public override void GetPrefabComponents(HashSet<ComponentType> components)
        {
            components.Add(ComponentType.ReadWrite<RailwaySignalObjectData>());
        }

        public override void GetArchetypeComponents(HashSet<ComponentType> components)
        {
        }

        public override void LateInitialize(EntityManager entityManager, Entity entity)
        {
            base.LateInitialize(entityManager, entity);
            entityManager.SetComponentData(entity, new RailwaySignalObjectData { m_Asset = m_Asset });
        }
    }

    /// <summary>Sits on the prefab entity, not the instance.</summary>
    public struct RailwaySignalObjectData : IComponentData, IQueryTypeParameter
    {
        public SignalAsset m_Asset;
    }
}
