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
        /// Both heads of a two headed signal are separate objects sharing one transform, because a
        /// head can only show three lamps and one TrafficLight component drives only one head.
        /// </summary>
        private void ReconcileSignalObjects()
        {
            var prefabs = new Entity[3];
            var archetypes = new EntityArchetype[3];
            for (int i = 0; i < prefabs.Length; i++)
            {
                prefabs[i] = m_SignalPrefabSystem.GetSignalPrefab((SignalAsset)i);
                if (prefabs[i] != Entity.Null)
                {
                    archetypes[i] = EntityManager.GetComponentData<ObjectData>(prefabs[i]).m_Archetype;
                }
            }
            if (!archetypes[(int)SignalAsset.Home].Valid && !archetypes[(int)SignalAsset.Automatic].Valid)
            {
                Mod.log.Warn("No signal prefab with an instantiable archetype is available.");
                return;
            }

            NativeArray<Entity> existing = m_SignalQuery.ToEntityArray(Allocator.Temp);
            var byApproach = new NativeParallelHashMap<DirectedLane, RailwaySignal>(math.max(1, existing.Length), Allocator.Temp);
            var entityByApproach = new NativeParallelHashMap<DirectedLane, Entity>(math.max(1, existing.Length), Allocator.Temp);
            for (int i = 0; i < existing.Length; i++)
            {
                RailwaySignal signal = EntityManager.GetComponentData<RailwaySignal>(existing[i]);
                if (entityByApproach.TryAdd(signal.Approach, existing[i]))
                {
                    byApproach.TryAdd(signal.Approach, signal);
                }
                else
                {
                    DeleteSignal(existing[i], signal.m_BottomHead);
                }
            }

            var newHome = new NativeList<int>(m_Network.m_Sites.Length, Allocator.Temp);
            var newAutomatic = new NativeList<int>(m_Network.m_Sites.Length, Allocator.Temp);
            var newBottom = new NativeList<int>(m_Network.m_Sites.Length, Allocator.Temp);

            for (int i = 0; i < m_Network.m_Sites.Length; i++)
            {
                SignalSiteData site = m_Network.m_Sites[i];
                SignalAsset asset = site.m_Class == SignalClass.Automatic ? SignalAsset.Automatic : SignalAsset.Home;
                Entity prefab = prefabs[(int)asset];
                if (prefab == Entity.Null)
                {
                    continue;
                }
                var transform = new Game.Objects.Transform(site.m_Position, site.m_Rotation);

                if (entityByApproach.TryGetValue(site.m_Approach, out Entity entity) && EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab == prefab)
                {
                    RailwaySignal previous = byApproach[site.m_Approach];
                    entityByApproach.Remove(site.m_Approach);
                    Move(entity, transform, site.m_Owner);
                    site.m_Signal = entity;
                    site.m_BottomHead = ReconcileBottomHead(site, previous.m_BottomHead, prefabs[(int)SignalAsset.BottomHead]);
                    m_Network.m_Sites[i] = site;
                    QueueBottomHead(i, newBottom);
                }
                else
                {
                    m_Network.m_Sites[i] = site;
                    (site.m_Class == SignalClass.Automatic ? newAutomatic : newHome).Add(i);
                }
            }

            NativeArray<DirectedLane> staleKeys = entityByApproach.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < staleKeys.Length; i++)
            {
                DeleteSignal(entityByApproach[staleKeys[i]], byApproach[staleKeys[i]].m_BottomHead);
            }

            CreateHeads(archetypes[(int)SignalAsset.Home], prefabs[(int)SignalAsset.Home], newHome, bottomHead: false);
            CreateHeads(archetypes[(int)SignalAsset.Automatic], prefabs[(int)SignalAsset.Automatic], newAutomatic, bottomHead: false);

            // Sites that got a brand new top head above still need their bottom head queued.
            for (int i = 0; i < newHome.Length; i++)
            {
                QueueBottomHead(newHome[i], newBottom);
            }
            for (int i = 0; i < newAutomatic.Length; i++)
            {
                QueueBottomHead(newAutomatic[i], newBottom);
            }
            CreateHeads(archetypes[(int)SignalAsset.BottomHead], prefabs[(int)SignalAsset.BottomHead], newBottom, bottomHead: true);

            WriteSignalComponents();

            staleKeys.Dispose();
            newHome.Dispose();
            newAutomatic.Dispose();
            newBottom.Dispose();
            entityByApproach.Dispose();
            byApproach.Dispose();
            existing.Dispose();
        }

        private void QueueBottomHead(int siteIndex, NativeList<int> queue)
        {
            SignalSiteData site = m_Network.m_Sites[siteIndex];
            if (site.m_TwoHead && site.m_Signal != Entity.Null && site.m_BottomHead == Entity.Null)
            {
                queue.Add(siteIndex);
            }
        }

        /// <summary>Keeps, moves or drops the medium speed head a surviving signal already had.</summary>
        private Entity ReconcileBottomHead(SignalSiteData site, Entity bottomHead, Entity bottomPrefab)
        {
            bool usable = bottomHead != Entity.Null
                && EntityManager.Exists(bottomHead)
                && bottomPrefab != Entity.Null
                && EntityManager.GetComponentData<PrefabRef>(bottomHead).m_Prefab == bottomPrefab;

            if (site.m_TwoHead && usable)
            {
                Move(bottomHead, HeadTransform(site, bottomHead: true), site.m_Owner);
                return bottomHead;
            }
            if (bottomHead != Entity.Null && EntityManager.Exists(bottomHead))
            {
                EntityManager.AddComponent<Deleted>(bottomHead);
            }
            return Entity.Null;
        }

        /// <summary>
        /// Where a head sits. Both heads share the mast, so the asset itself normally places its
        /// lamps at the right height and the drop stays zero; it exists for stand-in assets that
        /// would otherwise land on top of each other.
        /// </summary>
        private static Game.Objects.Transform HeadTransform(SignalSiteData site, bool bottomHead)
        {
            float3 position = site.m_Position;
            if (bottomHead)
            {
                position.y -= Mod.setting.bottomHeadDrop;
            }
            return new Game.Objects.Transform(position, site.m_Rotation);
        }

        private void Move(Entity entity, Game.Objects.Transform transform, Entity owner)
        {
            EntityManager.SetComponentData(entity, transform);
            EntityManager.SetComponentData(entity, new Owner(owner));
            if (!EntityManager.HasComponent<Updated>(entity))
            {
                EntityManager.AddComponent<Updated>(entity);
            }
        }

        private void DeleteSignal(Entity signal, Entity bottomHead)
        {
            EntityManager.AddComponent<Deleted>(signal);
            if (bottomHead != Entity.Null && EntityManager.Exists(bottomHead))
            {
                EntityManager.AddComponent<Deleted>(bottomHead);
            }
        }

        private void CreateHeads(EntityArchetype archetype, Entity prefab, NativeList<int> siteIndices, bool bottomHead)
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
                EntityManager.SetComponentData(entity, HeadTransform(site, bottomHead));
                if (EntityManager.HasComponent<PseudoRandomSeed>(entity))
                {
                    EntityManager.SetComponentData(entity, new PseudoRandomSeed(ref m_Random));
                }
                EntityManager.AddComponent<Created>(entity);
                EntityManager.AddComponent<Updated>(entity);

                if (bottomHead)
                {
                    site.m_BottomHead = entity;
                }
                else
                {
                    site.m_Signal = entity;
                }
                m_Network.m_Sites[siteIndex] = site;
            }
            created.Dispose();
        }

        /// <summary>
        /// Stamps the plan onto the top heads once both heads exist, so each one knows the boundary
        /// it governs and where its medium speed head is.
        /// </summary>
        private void WriteSignalComponents()
        {
            for (int i = 0; i < m_Network.m_Sites.Length; i++)
            {
                SignalSiteData site = m_Network.m_Sites[i];
                if (site.m_Signal == Entity.Null)
                {
                    continue;
                }
                var signal = new RailwaySignal
                {
                    m_Lane = site.m_Approach.m_Lane,
                    m_Forward = site.m_Approach.m_Forward,
                    m_Class = site.m_Class,
                    m_Speed = site.m_Speed,
                    m_Aspect = SignalAspect.Stop,
                    m_BottomHead = site.m_BottomHead
                };
                if (EntityManager.HasComponent<RailwaySignal>(site.m_Signal))
                {
                    EntityManager.SetComponentData(site.m_Signal, signal);
                }
                else
                {
                    EntityManager.AddComponentData(site.m_Signal, signal);
                }
            }
        }
    }
}
