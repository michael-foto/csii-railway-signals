using System;
using Game;
using Game.Common;
using Game.Prefabs;
using RailwaySignals.Prefabs;
using RailwaySignals.Signalling;
using Unity.Collections;
using Unity.Entities;

namespace RailwaySignals.Systems
{
    /// <summary>
    /// Finds the object prefabs the mod's own signal assets are built from. Each one carries a
    /// <see cref="RailwaySignalObject"/> component naming which piece it is, so the whole set comes
    /// back from one query and nothing has to be matched by name.
    /// </summary>
    public partial class SignalPrefabSystem : GameSystemBase
    {
        public static readonly int kAssetCount = Enum.GetValues(typeof(SignalAsset)).Length;

        private EntityQuery m_Query;

        private readonly Entity[] m_Prefabs = new Entity[kAssetCount];

        private bool m_Resolved;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<RailwaySignalObjectData>(), ComponentType.ReadOnly<ObjectData>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
        }

        /// <summary>The prefab to instantiate for this part of a signal, or Entity.Null if the asset is missing.</summary>
        public Entity GetSignalPrefab(SignalAsset asset)
        {
            if (!m_Resolved)
            {
                Resolve();
            }
            return m_Prefabs[(int)asset];
        }

        /// <summary>Drops the cache so the next lookup goes back to the prefab entities.</summary>
        public void Invalidate()
        {
            m_Resolved = false;
        }

        private void Resolve()
        {
            Array.Clear(m_Prefabs, 0, m_Prefabs.Length);
            NativeArray<Entity> entities = m_Query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var asset = (int)EntityManager.GetComponentData<RailwaySignalObjectData>(entities[i]).m_Asset;
                if (m_Prefabs[asset] != Entity.Null)
                {
                    Mod.log.Warn($"More than one prefab claims to be the {(SignalAsset)asset}; using the first found.");
                    continue;
                }
                m_Prefabs[asset] = entities[i];
            }
            entities.Dispose();

            for (int i = 0; i < m_Prefabs.Length; i++)
            {
                if (m_Prefabs[i] == Entity.Null)
                {
                    Mod.log.Warn($"No prefab is marked as the {(SignalAsset)i}, so none will be placed.");
                }
            }
            m_Resolved = true;
        }
    }
}
