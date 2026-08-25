using Game;
using Game.Common;
using Game.Prefabs;
using RailwaySignals.Signalling;
using Unity.Collections;
using Unity.Entities;

namespace RailwaySignals.Systems
{
    /// <summary>
    /// Picks the object prefab used for signal posts. Any prefab carrying a
    /// <c>TrafficLightObject</c> component works, because that is what puts
    /// <c>Game.Objects.TrafficLight</c> into the instance archetype and lets the base game drive
    /// the <c>TrafficLight_Red</c>/<c>_Yellow</c>/<c>_Green</c> emissive purposes on the model.
    /// Until a purpose-built signal asset is installed this falls back to a vanilla road light so
    /// the signalling can be seen working.
    /// </summary>
    public partial class SignalPrefabSystem : GameSystemBase
    {
        private PrefabSystem m_PrefabSystem;

        private EntityQuery m_CandidateQuery;

        private readonly Entity[] m_Prefabs = new Entity[3];

        private readonly string[] m_ResolvedFor = new string[3];

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_CandidateQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ObjectData>(), ComponentType.ReadOnly<TrafficLightData>(), ComponentType.ReadOnly<ObjectGeometryData>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
        }

        /// <summary>The prefab to instantiate for this part of a signal, or Entity.Null if none is usable yet.</summary>
        public Entity GetSignalPrefab(SignalAsset asset)
        {
            int index = (int)asset;
            string preferredName;
            switch (asset)
            {
                case SignalAsset.Automatic:
                    preferredName = Mod.setting.automaticSignalPrefabName;
                    break;
                case SignalAsset.BottomHead:
                    preferredName = Mod.setting.bottomHeadPrefabName;
                    break;
                default:
                    preferredName = Mod.setting.homeSignalPrefabName;
                    break;
            }
            if (m_Prefabs[index] != Entity.Null && m_ResolvedFor[index] == preferredName && EntityManager.Exists(m_Prefabs[index]))
            {
                return m_Prefabs[index];
            }
            m_ResolvedFor[index] = preferredName;
            m_Prefabs[index] = Resolve(preferredName);
            return m_Prefabs[index];
        }

        public void Invalidate()
        {
            for (int i = 0; i < m_Prefabs.Length; i++)
            {
                m_Prefabs[i] = Entity.Null;
            }
        }

        private Entity Resolve(string preferredName)
        {
            NativeArray<Entity> candidates = m_CandidateQuery.ToEntityArray(Allocator.Temp);
            Entity exact = Entity.Null;
            Entity fallback = Entity.Null;
            int fallbackScore = int.MinValue;

            for (int i = 0; i < candidates.Length; i++)
            {
                Entity candidate = candidates[i];
                if (!EntityManager.GetComponentData<ObjectData>(candidate).m_Archetype.Valid
                    || !m_PrefabSystem.TryGetPrefab<PrefabBase>(candidate, out PrefabBase prefab))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(preferredName) && prefab.name == preferredName)
                {
                    exact = candidate;
                    break;
                }
                int score = ScoreFallback(prefab.name);
                if (score > fallbackScore)
                {
                    fallbackScore = score;
                    fallback = candidate;
                }
            }
            candidates.Dispose();

            Entity result = exact != Entity.Null ? exact : fallback;
            if (result == Entity.Null)
            {
                Mod.log.Warn("No object prefab with a TrafficLightObject component is available; signals cannot be placed.");
            }
            else if (exact == Entity.Null && !string.IsNullOrEmpty(preferredName))
            {
                Mod.log.Info($"Signal prefab '{preferredName}' not found, standing in with '{m_PrefabSystem.GetPrefab<PrefabBase>(result).name}'.");
            }
            return result;
        }

        /// <summary>Prefers a plain vehicle light over pedestrian heads and level crossing gear.</summary>
        private static int ScoreFallback(string name)
        {
            int score = 0;
            if (name.IndexOf("Traffic", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 10;
            }
            if (name.IndexOf("Pedestrian", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score -= 20;
            }
            if (name.IndexOf("Crossing", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score -= 20;
            }
            if (name.IndexOf("Median", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score -= 5;
            }
            return score - name.Length;
        }
    }
}
