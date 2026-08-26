using Colossal.Mathematics;
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

        private EntityQuery m_PartQuery;

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
            m_SignalQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<RailwaySignal>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
            m_PartQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<RailwaySignalPart>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
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
            EntityManager.DestroyEntity(m_PartQuery);
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

            bool hasGantryAsset = m_SignalPrefabSystem.GetSignalPrefab(SignalAsset.Gantry) != Entity.Null;
            if (!hasGantryAsset && Mod.setting.minGantryTracks > 0)
            {
                Mod.log.Info("No signal bridge asset is installed, so every signal goes on a lineside post.");
            }

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
                m_MediumBlockLength = Mod.setting.mediumSpeedBlockLength,
                m_MinGantryTracks = hasGantryAsset ? Mod.setting.minGantryTracks : 0,
                m_MaxGantryTrackSpacing = Mod.setting.maxGantryTrackSpacing,
                m_GantryAlignTolerance = Mod.setting.gantryAlignTolerance,
                m_GantryMargin = Mod.setting.gantryMargin
            };
            planner.Plan(trackLanes, ref m_Network);
            trackLanes.Dispose();

            PlaceSignalObjects();

            Mod.log.Info($"Signal plan rebuilt: {m_Network.m_Sites.Length} signals over {m_Network.m_BlockLanes.Length} block lanes, {m_Network.m_Gantries.Length} on bridges.");
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
        /// Puts up every part of every signal. A signal is assembled from separate objects: a mast
        /// where it stands on the ground, a normal speed head, and a medium speed head below it
        /// where one is called for. Keeping the mast out of the head assets is what lets the same
        /// heads serve both a lineside post and a bridge.
        ///
        /// The lot is torn down and rebuilt rather than matched up, because a plan only gets rebuilt
        /// after the track has been left alone for a moment and the parts are cheap to replace.
        /// </summary>
        private void PlaceSignalObjects()
        {
            if (!m_PartQuery.IsEmptyIgnoreFilter)
            {
                EntityManager.AddComponent<Deleted>(m_PartQuery);
            }

            var prefabs = new Entity[5];
            var archetypes = new EntityArchetype[5];
            for (int i = 0; i < prefabs.Length; i++)
            {
                prefabs[i] = m_SignalPrefabSystem.GetSignalPrefab((SignalAsset)i);
                if (prefabs[i] != Entity.Null)
                {
                    archetypes[i] = EntityManager.GetComponentData<ObjectData>(prefabs[i]).m_Archetype;
                }
            }
            if (!archetypes[(int)SignalAsset.HomeHead].Valid && !archetypes[(int)SignalAsset.AutomaticHead].Valid)
            {
                Mod.log.Warn("No signal head asset with an instantiable archetype is available.");
                return;
            }

            var masts = new NativeList<int>(m_Network.m_Sites.Length, Allocator.Temp);
            var homeHeads = new NativeList<int>(m_Network.m_Sites.Length, Allocator.Temp);
            var automaticHeads = new NativeList<int>(m_Network.m_Sites.Length, Allocator.Temp);
            var bottomHeads = new NativeList<int>(m_Network.m_Sites.Length, Allocator.Temp);

            for (int i = 0; i < m_Network.m_Sites.Length; i++)
            {
                SignalSiteData site = m_Network.m_Sites[i];
                (site.m_Class == SignalClass.Automatic ? automaticHeads : homeHeads).Add(i);
                if (site.m_Gantry < 0)
                {
                    masts.Add(i);
                }
                if (site.m_TwoHead)
                {
                    bottomHeads.Add(i);
                }
            }

            PlaceParts(archetypes, prefabs, SignalAsset.Mast, masts);
            PlaceParts(archetypes, prefabs, SignalAsset.HomeHead, homeHeads);
            PlaceParts(archetypes, prefabs, SignalAsset.AutomaticHead, automaticHeads);
            PlaceParts(archetypes, prefabs, SignalAsset.BottomHead, bottomHeads);
            PlaceGantries(archetypes[(int)SignalAsset.Gantry], prefabs[(int)SignalAsset.Gantry]);
            WriteSignalComponents();

            masts.Dispose();
            homeHeads.Dispose();
            automaticHeads.Dispose();
            bottomHeads.Dispose();
        }

        private void PlaceParts(EntityArchetype[] archetypes, Entity[] prefabs, SignalAsset asset, NativeList<int> siteIndices)
        {
            EntityArchetype archetype = archetypes[(int)asset];
            if (siteIndices.Length == 0 || !archetype.Valid)
            {
                return;
            }
            Entity prefab = prefabs[(int)asset];
            var created = new NativeArray<Entity>(siteIndices.Length, Allocator.Temp);
            EntityManager.CreateEntity(archetype, created);

            for (int i = 0; i < created.Length; i++)
            {
                int siteIndex = siteIndices[i];
                SignalSiteData site = m_Network.m_Sites[siteIndex];
                Entity entity = created[i];
                float3 position = site.m_Position;
                position.y += GetPartHeight(site, asset);
                Initialize(entity, prefab, site.m_Owner, position, site.m_Rotation);

                if (asset == SignalAsset.Mast)
                {
                    // A mast built as a stack grows its shaft to reach whatever head height is set,
                    // so one asset serves any height rather than fixing it in the model.
                    SetStackRange(entity, 0f, Mod.setting.signalHeadHeight);
                    site.m_Mast = entity;
                }
                else if (asset == SignalAsset.BottomHead)
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

        /// <summary>Height above the base of the signal at which a part sits.</summary>
        private static float GetPartHeight(SignalSiteData site, SignalAsset asset)
        {
            if (asset == SignalAsset.Mast)
            {
                return 0f;
            }
            float head = site.m_Gantry >= 0 ? Mod.setting.gantryHeadHeight : Mod.setting.signalHeadHeight;
            return asset == SignalAsset.BottomHead ? head - Mod.setting.headSpacing : head;
        }

        private void PlaceGantries(EntityArchetype archetype, Entity prefab)
        {
            if (m_Network.m_Gantries.Length == 0 || !archetype.Valid)
            {
                return;
            }
            var created = new NativeArray<Entity>(m_Network.m_Gantries.Length, Allocator.Temp);
            EntityManager.CreateEntity(archetype, created);
            for (int i = 0; i < created.Length; i++)
            {
                GantryData gantry = m_Network.m_Gantries[i];
                Entity entity = created[i];
                Initialize(entity, prefab, gantry.m_Owner, gantry.m_Position, gantry.m_Rotation);
                // The beam mesh tiles between the leg meshes to fill this range along the local X
                // axis, which is how one structure covers any number of tracks.
                SetStackRange(entity, -gantry.m_Span, gantry.m_Span);
                gantry.m_Entity = entity;
                m_Network.m_Gantries[i] = gantry;
            }
            created.Dispose();
        }

        private void Initialize(Entity entity, Entity prefab, Entity owner, float3 position, quaternion rotation)
        {
            EntityManager.AddComponent<Secondary>(entity);
            EntityManager.AddComponent<Owner>(entity);
            EntityManager.SetComponentData(entity, new Owner(owner));
            EntityManager.SetComponentData(entity, new PrefabRef(prefab));
            EntityManager.SetComponentData(entity, new Game.Objects.Transform(position, rotation));
            if (EntityManager.HasComponent<PseudoRandomSeed>(entity))
            {
                EntityManager.SetComponentData(entity, new PseudoRandomSeed(ref m_Random));
            }
            EntityManager.AddComponentData(entity, default(RailwaySignalPart));
            EntityManager.AddComponent<Created>(entity);
            EntityManager.AddComponent<Updated>(entity);
        }

        private void SetStackRange(Entity entity, float min, float max)
        {
            if (EntityManager.HasComponent<Game.Objects.Stack>(entity))
            {
                EntityManager.SetComponentData(entity, new Game.Objects.Stack { m_Range = new Bounds1(min, max) });
            }
        }

        /// <summary>Stamps the plan onto the normal speed heads, which are what the aspect pass drives.</summary>
        private void WriteSignalComponents()
        {
            for (int i = 0; i < m_Network.m_Sites.Length; i++)
            {
                SignalSiteData site = m_Network.m_Sites[i];
                if (site.m_Signal == Entity.Null)
                {
                    continue;
                }
                EntityManager.AddComponentData(site.m_Signal, new RailwaySignal
                {
                    m_Lane = site.m_Approach.m_Lane,
                    m_Forward = site.m_Approach.m_Forward,
                    m_Class = site.m_Class,
                    m_Speed = site.m_Speed,
                    m_Aspect = SignalAspect.Stop
                });
            }
        }
    }
}
