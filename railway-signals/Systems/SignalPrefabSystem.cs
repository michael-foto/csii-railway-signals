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

        private EntityQuery m_GantryQuery;

        private EntityQuery m_MastQuery;

        private readonly Entity[] m_Prefabs = new Entity[4];

        private readonly string[] m_ResolvedFor = new string[4];

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_CandidateQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ObjectData>(), ComponentType.ReadOnly<TrafficLightData>(), ComponentType.ReadOnly<ObjectGeometryData>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
            // A signal bridge is picked out by StackData instead, which a prefab gets when one of
            // its meshes carries StackProperties. That is what lets the beam tile across the span.
            m_GantryQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ObjectData>(), ComponentType.ReadOnly<StackData>(), ComponentType.ReadOnly<ObjectGeometryData>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
            // A mast has no lamps, so it is any plain object. Requiring TrafficLightData of it, as
            // the heads do, would rule out every post ever modelled.
            m_MastQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ObjectData>(), ComponentType.ReadOnly<ObjectGeometryData>() },
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
                case SignalAsset.Mast:
                    preferredName = Mod.setting.mastPrefabName;
                    break;
                case SignalAsset.AutomaticHead:
                    preferredName = Mod.setting.automaticHeadPrefabName;
                    break;
                case SignalAsset.Gantry:
                    preferredName = Mod.setting.gantryPrefabName;
                    break;
                default:
                    preferredName = Mod.setting.homeHeadPrefabName;
                    break;
            }
            if (m_Prefabs[index] != Entity.Null && m_ResolvedFor[index] == preferredName && EntityManager.Exists(m_Prefabs[index]))
            {
                return m_Prefabs[index];
            }
            m_ResolvedFor[index] = preferredName;
            // A mast and a bridge have no vanilla equivalent worth standing in for, so they are
            // matched by name only and simply go unbuilt until an asset exists. A stand-in head is
            // a road traffic light, which brings its own pole and so needs no mast anyway.
            EntityQuery query = asset switch
            {
                SignalAsset.Mast => m_MastQuery,
                SignalAsset.Gantry => m_GantryQuery,
                _ => m_CandidateQuery
            };
            bool nameOnly = asset == SignalAsset.Mast || asset == SignalAsset.Gantry;
            m_Prefabs[index] = Resolve(query, preferredName, nameOnly, asset);
            return m_Prefabs[index];
        }

        public void Invalidate()
        {
            for (int i = 0; i < m_Prefabs.Length; i++)
            {
                m_Prefabs[i] = Entity.Null;
            }
        }

        /// <summary>
        /// Finds the named prefab, or the best stand-in when a name is not given. A signal bridge
        /// has no vanilla equivalent to stand in for, so it is matched by name only and simply goes
        /// unbuilt until an asset exists.
        /// </summary>
        private Entity Resolve(EntityQuery query, string preferredName, bool exactOnly, SignalAsset asset)
        {
            NativeArray<Entity> candidates = query.ToEntityArray(Allocator.Temp);
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

            if (exact != Entity.Null)
            {
                return exact;
            }
            if (exactOnly)
            {
                // Silence here used to hide a missing asset completely, so say so once.
                Mod.log.Warn(string.IsNullOrEmpty(preferredName)
                    ? $"No asset is named for {asset}, so none will be placed."
                    : $"No usable asset called '{preferredName}' was found for {asset}, so none will be placed.");
                return Entity.Null;
            }
            if (fallback == Entity.Null)
            {
                Mod.log.Warn("No object prefab with a TrafficLightObject component is available; signals cannot be placed.");
            }
            else if (!string.IsNullOrEmpty(preferredName))
            {
                Mod.log.Info($"Signal prefab '{preferredName}' not found, standing in with '{m_PrefabSystem.GetPrefab<PrefabBase>(fallback).name}'.");
            }
            return fallback;
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
