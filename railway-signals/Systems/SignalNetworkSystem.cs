using Game;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using RailwaySignals.Signalling;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RailwaySignals.Systems
{
    /// <summary>
    /// Keeps the signalling plan and the signal posts realising it in step with the track network.
    /// A rebuild recomputes every signal position and block from scratch, then reconciles the
    /// existing post entities against the new plan so unchanged signals keep their entity.
    /// </summary>
    public partial class SignalNetworkSystem : GameSystemBase
    {
        private SignalNetwork m_Network;

        private EntityQuery m_TrackLaneQuery;

        private EntityQuery m_ChangedTrackQuery;

        private EntityQuery m_SignalQuery;

        private SignalPrefabSystem m_SignalPrefabSystem;

        private CityConfigurationSystem m_CityConfigurationSystem;

        private Unity.Mathematics.Random m_Random;

        private bool m_Dirty;

        private int m_Settle;

        /// <summary>Frames of quiet after the last track change before rebuilding.</summary>
        private const int kSettleFrames = 20;

        public ref SignalNetwork network => ref m_Network;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SignalPrefabSystem = World.GetOrCreateSystemManaged<SignalPrefabSystem>();
            m_CityConfigurationSystem = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            m_Network = SignalNetwork.Create(Allocator.Persistent);
            m_Random = new Unity.Mathematics.Random(0x5A17E1u);

            m_TrackLaneQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Net.TrackLane>(),
                    ComponentType.ReadOnly<Game.Net.Lane>(),
                    ComponentType.ReadOnly<Curve>(),
                    ComponentType.ReadOnly<PrefabRef>()
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() }
            });
            m_ChangedTrackQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.TrackLane>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() }
            });
            m_SignalQuery = GetEntityQuery(ComponentType.ReadOnly<RailwaySignal>());
        }

        protected override void OnDestroy()
        {
            m_Network.Dispose();
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_SignalPrefabSystem.Invalidate();
            m_Dirty = mode.IsGameOrEditor();
            m_Settle = kSettleFrames;
        }

        /// <summary>Removes every signal post and forgets the plan. Used when the mod is switched off.</summary>
        public void Clear()
        {
            CompleteDependency();
            EntityManager.DestroyEntity(m_SignalQuery);
            m_Network.Clear();
            m_Dirty = false;
        }

        public void Invalidate()
        {
            m_Dirty = true;
            m_Settle = kSettleFrames;
        }

        protected override void OnUpdate()
        {
            if (!Mod.setting.enableSignals)
            {
                if (!m_SignalQuery.IsEmptyIgnoreFilter)
                {
                    Clear();
                }
                return;
            }
            if (!m_ChangedTrackQuery.IsEmptyIgnoreFilter)
            {
                m_Dirty = true;
                m_Settle = kSettleFrames;
                return;
            }
            if (!m_Dirty || --m_Settle > 0)
            {
                return;
            }
            m_Dirty = false;
            Rebuild();
        }

        private void Rebuild()
        {
            CompleteDependency();

            NativeList<Entity> trackLanes = CollectSignalledTrackLanes(out TrackGraph graph);
            var planner = new SignalPlanner
            {
                m_Graph = graph,
                m_LaneOverlaps = GetBufferLookup<LaneOverlap>(isReadOnly: true),
                m_BlockSpacing = Mod.setting.intermediateBlockSpacing,
                m_IntermediateOnBidirectional = Mod.setting.intermediateOnBidirectionalTrack,
                m_Setback = Mod.setting.signalSetback,
                m_LateralOffset = Mod.setting.signalOffset,
                m_LeftHandTraffic = m_CityConfigurationSystem.leftHandTraffic,
                m_MediumCurviness = 1f / math.max(1f, Mod.setting.mediumSpeedCurveRadius),
                m_MediumSpeedLimit = Mod.setting.mediumSpeedLimit / 3.6f,
                m_MediumBlockLength = Mod.setting.mediumSpeedBlockLength
            };
            planner.Plan(trackLanes, ref m_Network);
            trackLanes.Dispose();

            ReconcileSignalObjects();

            Mod.log.Info($"Signal plan rebuilt: {m_Network.m_Sites.Length} signals over {m_Network.m_BlockLanes.Length} block lanes.");
        }

        private NativeList<Entity> CollectSignalledTrackLanes(out TrackGraph graph)
        {
            graph = new TrackGraph
            {
                m_LaneData = GetComponentLookup<Game.Net.Lane>(isReadOnly: true),
                m_TrackLaneData = GetComponentLookup<Game.Net.TrackLane>(isReadOnly: true),
                m_EdgeLaneData = GetComponentLookup<Game.Net.EdgeLane>(isReadOnly: true),
                m_OwnerData = GetComponentLookup<Owner>(isReadOnly: true),
                m_EdgeData = GetComponentLookup<Game.Net.Edge>(isReadOnly: true),
                m_CurveData = GetComponentLookup<Curve>(isReadOnly: true),
                m_PrefabRefData = GetComponentLookup<PrefabRef>(isReadOnly: true),
                m_PrefabTrackLaneData = GetComponentLookup<TrackLaneData>(isReadOnly: true),
                m_ConnectedEdges = GetBufferLookup<Game.Net.ConnectedEdge>(isReadOnly: true),
                m_SubLanes = GetBufferLookup<Game.Net.SubLane>(isReadOnly: true),
                m_TrackTypes = Mod.setting.signalledTrackTypes
            };

            NativeArray<Entity> all = m_TrackLaneQuery.ToEntityArray(Allocator.Temp);
            var result = new NativeList<Entity>(all.Length, Allocator.Temp);
            for (int i = 0; i < all.Length; i++)
            {
                if (graph.IsSignalledTrack(all[i]))
                {
                    result.Add(all[i]);
                }
            }
            all.Dispose();
            return result;
        }

        /// <summary>
        /// Matches the posts already in the world to the freshly planned sites by the boundary they
        /// govern, moving those that survived, creating the new ones and destroying the rest. A
        /// signal whose class changed needs a different asset, so it is rebuilt rather than moved.
        /// </summary>
        private void ReconcileSignalObjects()
        {
            var prefabs = new Entity[2];
            var archetypes = new EntityArchetype[2];
            for (int i = 0; i < prefabs.Length; i++)
            {
                prefabs[i] = m_SignalPrefabSystem.GetSignalPrefab((SignalClass)i);
                if (prefabs[i] != Entity.Null)
                {
                    archetypes[i] = EntityManager.GetComponentData<ObjectData>(prefabs[i]).m_Archetype;
                }
            }
            if (!archetypes[0].Valid && !archetypes[1].Valid)
            {
                Mod.log.Warn("No signal prefab with an instantiable archetype is available.");
                return;
            }

            NativeArray<Entity> existing = m_SignalQuery.ToEntityArray(Allocator.Temp);
            var byApproach = new NativeParallelHashMap<DirectedLane, Entity>(math.max(1, existing.Length), Allocator.Temp);
            for (int i = 0; i < existing.Length; i++)
            {
                RailwaySignal signal = EntityManager.GetComponentData<RailwaySignal>(existing[i]);
                if (!byApproach.TryAdd(signal.Approach, existing[i]))
                {
                    EntityManager.AddComponent<Deleted>(existing[i]);
                }
            }

            var missingHome = new NativeList<int>(m_Network.m_Sites.Length, Allocator.Temp);
            var missingAutomatic = new NativeList<int>(m_Network.m_Sites.Length, Allocator.Temp);
            for (int i = 0; i < m_Network.m_Sites.Length; i++)
            {
                SignalSiteData site = m_Network.m_Sites[i];
                Entity prefab = prefabs[(int)site.m_Class];
                if (prefab == Entity.Null)
                {
                    continue;
                }
                if (!byApproach.TryGetValue(site.m_Approach, out Entity entity) || EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab != prefab)
                {
                    if (site.m_Class == SignalClass.Automatic)
                    {
                        missingAutomatic.Add(i);
                    }
                    else
                    {
                        missingHome.Add(i);
                    }
                    continue;
                }
                byApproach.Remove(site.m_Approach);
                EntityManager.SetComponentData(entity, new Game.Objects.Transform(site.m_Position, site.m_Rotation));
                EntityManager.SetComponentData(entity, MakeSignal(site));
                EntityManager.SetComponentData(entity, new Owner(site.m_Owner));
                if (!EntityManager.HasComponent<Updated>(entity))
                {
                    EntityManager.AddComponent<Updated>(entity);
                }
                site.m_Signal = entity;
                m_Network.m_Sites[i] = site;
            }

            NativeArray<Entity> stale = byApproach.GetValueArray(Allocator.Temp);
            for (int i = 0; i < stale.Length; i++)
            {
                EntityManager.AddComponent<Deleted>(stale[i]);
            }

            CreateSignalObjects(archetypes[(int)SignalClass.Home], prefabs[(int)SignalClass.Home], missingHome);
            CreateSignalObjects(archetypes[(int)SignalClass.Automatic], prefabs[(int)SignalClass.Automatic], missingAutomatic);

            stale.Dispose();
            missingHome.Dispose();
            missingAutomatic.Dispose();
            byApproach.Dispose();
            existing.Dispose();
        }

        private void CreateSignalObjects(EntityArchetype archetype, Entity prefab, NativeList<int> siteIndices)
        {
            if (siteIndices.Length == 0 || !archetype.Valid)
            {
                return;
            }
            var created = new NativeArray<Entity>(siteIndices.Length, Allocator.Temp);
            EntityManager.CreateEntity(archetype, created);
            for (int i = 0; i < created.Length; i++)
            {
                int siteIndex = siteIndices[i];
                SignalSiteData site = m_Network.m_Sites[siteIndex];
                Entity entity = created[i];
                EntityManager.AddComponent<Secondary>(entity);
                EntityManager.AddComponent<Owner>(entity);
                EntityManager.SetComponentData(entity, new Owner(site.m_Owner));
                EntityManager.SetComponentData(entity, new PrefabRef(prefab));
                EntityManager.SetComponentData(entity, new Game.Objects.Transform(site.m_Position, site.m_Rotation));
                EntityManager.AddComponentData(entity, MakeSignal(site));
                if (EntityManager.HasComponent<PseudoRandomSeed>(entity))
                {
                    EntityManager.SetComponentData(entity, new PseudoRandomSeed(ref m_Random));
                }
                EntityManager.AddComponent<Created>(entity);
                EntityManager.AddComponent<Updated>(entity);
                site.m_Signal = entity;
                m_Network.m_Sites[siteIndex] = site;
            }
            created.Dispose();
        }

        private static RailwaySignal MakeSignal(SignalSiteData site)
        {
            return new RailwaySignal
            {
                m_Lane = site.m_Approach.m_Lane,
                m_Forward = site.m_Approach.m_Forward,
                m_Kind = site.m_Kind,
                m_Class = site.m_Class,
                m_Speed = site.m_Speed,
                m_Aspect = SignalAspect.Stop
            };
        }
    }
}
