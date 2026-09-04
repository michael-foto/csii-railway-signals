using Colossal.Mathematics;
using Game;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Serialization;
using Game.Tools;
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
    public partial class SignalNetworkSystem : GameSystemBase, IPostDeserialize
    {
        private SignalNetwork m_Network;

        private EntityQuery m_TrackLaneQuery;

        private EntityQuery m_ChangedTrackQuery;

        private EntityQuery m_PartQuery;

        private SignalPrefabSystem m_SignalPrefabSystem;

        private CityConfigurationSystem m_CityConfigurationSystem;

        private Unity.Mathematics.Random m_Random;

        private bool m_Dirty;

        private int m_Settle;

        private int m_ProbeFrames;

        /// <summary>Frames of quiet after the last track change before rebuilding.</summary>
        private const int kSettleFrames = 20;

        /// <summary>How often unregistered parts are offered to the culling system again.</summary>
        private const int kAdoptInterval = 30;

        // Placement baselines, tuned in game and fixed here. Every one has a matching Advanced
        // setting that offsets it, so honing a value means moving a slider and then editing the
        // constant it belongs to, not adding a knob.

        /// <summary>How far back from the block boundary a signal stands, in metres.</summary>
        private const float kSetback = 3f;

        /// <summary>Distance from track centre to a lineside post, in metres.</summary>
        private const float kLateralOffset = 2f;

        /// <summary>Drop from the normal speed head to the medium speed head below it, in metres.</summary>
        private const float kHeadSpacing = 1.15f;

        /// <summary>
        /// How far every part is lowered from the lane centreline, in metres. The lane sits a little
        /// above the railhead the models are built from, so without this the whole assembly floats.
        /// </summary>
        private const float kGroundDrop = 0.15f;

        /// <summary>Structure width added beyond the outermost track a bridge spans, in metres.</summary>
        private const float kGantryMargin = 7f;

        /// <summary>How far off its own track centre a bridge-carried signal sits, in metres.</summary>
        private const float kGantryLateralOffset = 1.5f;

        /// <summary>Height of the normal speed head above rail level on a bridge, in metres.</summary>
        private const float kGantryHeadHeight = 2.25f;

        /// <summary>Head offset from the cage across the track, in metres.</summary>
        private const float kGantryHeadSide = 0.65f;

        /// <summary>Head offset from the cage along the track, in metres.</summary>
        private const float kGantryHeadForward = 1.05f;

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

        /// <summary>
        /// Discards signal objects restored from a save written by a version of the mod that still
        /// persisted them. Runs in the Deserialize phase, before PreCullingSystem has taken an
        /// interest in them, so marking them Deleted here leaves nothing pointing at them.
        /// </summary>
        public void PostDeserialize(Colossal.Serialization.Entities.Context context)
        {
            if (m_PartQuery.IsEmptyIgnoreFilter)
            {
                return;
            }
            Mod.log.Info($"Discarding {m_PartQuery.CalculateEntityCount()} signal objects restored from the save.");
            EntityManager.AddComponent<Deleted>(m_PartQuery);
        }

        /// <summary>
        /// Removes every signal post and forgets the plan. Used when the mod is switched off.
        /// Deleted rather than DestroyEntity: PreCullingSystem holds an entry per rendered object
        /// and only stops dereferencing it once it has seen the object carry Deleted, so destroying
        /// one outright leaves the culling data pointing at an entity that no longer exists.
        /// </summary>
        public void Clear()
        {
            CompleteDependency();
            EntityManager.AddComponent<Deleted>(m_PartQuery);
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
            AdoptUnrenderedParts();
            if (m_ProbeFrames > 0 && --m_ProbeFrames % 15 == 0)
            {
                LogRenderState();
            }
            if (!Mod.setting.enableSignals)
            {
                // Tested against the query Clear empties. Anything else leaves parts standing with
                // no rebuild to reconcile them, because this returns before the dirty check.
                if (!m_PartQuery.IsEmptyIgnoreFilter)
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

            if (Mod.setting.minGantryTracks > 0)
            {
                Mod.log.Info("No signal bridge asset is installed, so every signal goes on a lineside post.");
            }

            NativeList<Entity> trackLanes = CollectSignalledTrackLanes(out TrackGraph graph);
            int laneCount = trackLanes.Length;
            var planner = new SignalPlanner
            {
                m_Graph = graph,
                m_LaneOverlaps = GetBufferLookup<LaneOverlap>(isReadOnly: true),
                m_BlockSpacing = Mod.setting.intermediateBlockSpacing,
                m_IntermediateOnBidirectional = Mod.setting.intermediateOnBidirectionalTrack,
                m_Setback = kSetback + Mod.setting.adjustSetback,
                m_LateralOffset = kLateralOffset + Mod.setting.adjustLateral,
                m_HeightAdjust = Mod.setting.adjustHeight - kGroundDrop,
                m_LeftHandTraffic = m_CityConfigurationSystem.leftHandTraffic,
                m_MediumCurviness = 1f / math.max(1f, Mod.setting.mediumSpeedCurveRadius),
                m_MediumSpeedLimit = Mod.setting.mediumSpeedLimit / 3.6f,
                m_MediumBlockLength = Mod.setting.mediumSpeedBlockLength,
                m_MinGantryTracks = Mod.setting.minGantryTracks,
                m_MaxGantryTrackSpacing = Mod.setting.maxGantryTrackSpacing,
                m_GantryAlignTolerance = Mod.setting.gantryAlignTolerance,
                m_GantryMargin = kGantryMargin + Mod.setting.adjustGantryMargin,
                m_GantryLateralOffset = kGantryLateralOffset + Mod.setting.adjustGantryLateral,
                m_MinGantryTrackSeparation = Mod.setting.minGantryTrackSeparation
            };
            planner.Plan(trackLanes, ref m_Network);
            trackLanes.Dispose();

            PlaceSignalObjects();
            WriteSignalComponents();

            Mod.log.Info($"Signal plan rebuilt from {laneCount} track lanes: {m_Network.m_Sites.Length} signals over "
                + $"{m_Network.m_BlockLanes.Length} block lanes, {m_Network.m_Gantries.Length} on bridges; "
                + $"{m_PartQuery.CalculateEntityCount()} objects placed.");
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

        private struct PartKey : System.IEquatable<PartKey>
        {
            public DirectedLane m_Approach;

            public SignalPartKind m_Kind;

            public bool Equals(PartKey other)
            {
                return m_Approach.Equals(other.m_Approach) && m_Kind == other.m_Kind;
            }

            public override int GetHashCode()
            {
                return (m_Approach.GetHashCode() * 4) ^ (int)m_Kind;
            }
        }

        /// <summary>
        /// Brings the objects in the world into line with the plan. Anything already standing in the
        /// right place with the right asset is left completely alone.
        /// </summary>
        private void PlaceSignalObjects()
        {
            var prefabs = new Entity[SignalPrefabSystem.kAssetCount];
            var archetypes = new EntityArchetype[SignalPrefabSystem.kAssetCount];
            for (int i = 0; i < prefabs.Length; i++)
            {
                prefabs[i] = m_SignalPrefabSystem.GetSignalPrefab((SignalAsset)i);
                if (prefabs[i] != Entity.Null)
                {
                    archetypes[i] = EntityManager.GetComponentData<ObjectData>(prefabs[i]).m_Archetype;
                }
            }
            if (!archetypes[(int)SignalAsset.HomeHead].Valid)
            {
                Mod.log.Warn("No signal head asset with an instantiable archetype is available.");
                return;
            }

            NativeArray<Entity> existing = m_PartQuery.ToEntityArray(Allocator.Temp);
            var standing = new NativeParallelHashMap<PartKey, Entity>(math.max(1, existing.Length), Allocator.Temp);
            for (int i = 0; i < existing.Length; i++)
            {
                RailwaySignalPart part = EntityManager.GetComponentData<RailwaySignalPart>(existing[i]);
                var key = new PartKey { m_Approach = new DirectedLane(part.m_Lane, part.m_Forward), m_Kind = part.m_Kind };
                if (!standing.TryAdd(key, existing[i]))
                {
                    EntityManager.AddComponent<Deleted>(existing[i]);
                }
            }

            int kept = 0;
            int made = 0;
            for (int i = 0; i < m_Network.m_Sites.Length; i++)
            {
                SignalSiteData site = m_Network.m_Sites[i];
                SignalAsset bottomAsset = site.m_Class == SignalClass.Automatic ? SignalAsset.AutomaticHead : SignalAsset.HomeHead;

                site.m_Signal = Reconcile(site.m_Approach, SignalPartKind.TopHead, SignalAsset.HomeHead, site, prefabs, archetypes, ref standing, ref kept, ref made);
                site.m_BottomHead = Reconcile(site.m_Approach, SignalPartKind.BottomHead, bottomAsset, site, prefabs, archetypes, ref standing, ref kept, ref made);
                site.m_Mast = Reconcile(site.m_Approach, SignalPartKind.Mast, site.m_Gantry >= 0 ? SignalAsset.GantryCage : SignalAsset.Mast,
                    site, prefabs, archetypes, ref standing, ref kept, ref made);
                m_Network.m_Sites[i] = site;
            }

            // Place Gantries
            for (int i = 0; i < m_Network.m_Gantries.Length; i++)
            {
                GantryData gantry = m_Network.m_Gantries[i];
                gantry.m_Entity = Reconcile(gantry.m_Key, SignalPartKind.Gantry, SignalAsset.Gantry,
                    gantry, prefabs, archetypes, ref standing, ref kept, ref made);
                if (gantry.m_Entity != Entity.Null)
                {
                    SetStackRange(gantry.m_Entity, prefabs[(int)SignalAsset.Gantry], -gantry.m_Span, gantry.m_Span);
                }
                m_Network.m_Gantries[i] = gantry;
            }

            NativeArray<Entity> stale = standing.GetValueArray(Allocator.Temp);
            for (int i = 0; i < stale.Length; i++)
            {
                EntityManager.AddComponent<Deleted>(stale[i]);
            }
            Mod.log.Info($"objects: {kept} kept, {made} created, {stale.Length} removed");

            stale.Dispose();
            standing.Dispose();
            existing.Dispose();
        }

        /// <summary>
        /// Returns the object for one part, reusing what is already there when the asset matches and
        /// only moving it if it has actually shifted.
        /// </summary>
        private Entity Reconcile(DirectedLane approach, SignalPartKind kind, SignalAsset asset, IPositionable site, Entity[] prefabs, EntityArchetype[] archetypes, ref NativeParallelHashMap<PartKey, Entity> standing,
            ref int kept, ref int made)
        {
            Entity prefab = prefabs[(int)asset];
            if (prefab == Entity.Null || !archetypes[(int)asset].Valid)
            {
                return Entity.Null;
            }
            var key = new PartKey { m_Approach = approach, m_Kind = kind };
            // we only need to reposition signal sub-elements. Gantries go by unchanged.
            var transform = site is SignalSiteData data
                ? GetTransformedPosition(data, kind)
                : new Transform(site.Position, site.Rotation);

            if (standing.TryGetValue(key, out Entity entity)
                && EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab == prefab)
            {
                standing.Remove(key);
                if (!EntityManager.GetComponentData<Transform>(entity).Equals(transform))
                {
                    EntityManager.SetComponentData(entity, transform);
                    if (!EntityManager.HasComponent<Updated>(entity))
                    {
                        EntityManager.AddComponent<Updated>(entity);
                    }
                }
                kept++;
                return entity;
            }

            // The one argument CreateEntity cannot be resolved against net48's reference
            // assemblies, whose mscorlib has no Span; the array form has no such overload.
            var created = new NativeArray<Entity>(1, Allocator.Temp);
            EntityManager.CreateEntity(archetypes[(int)asset], created);
            entity = created[0];
            created.Dispose();
            EntityManager.SetComponentData(entity, new PrefabRef(prefab));
            EntityManager.SetComponentData(entity, transform);
            if (EntityManager.HasComponent<PseudoRandomSeed>(entity))
            {
                EntityManager.SetComponentData(entity, new PseudoRandomSeed(ref m_Random));
            }
            EntityManager.AddComponentData(entity, new RailwaySignalPart
            {
                m_Lane = approach.m_Lane,
                m_Forward = approach.m_Forward,
                m_Kind = kind
            });
            EntityManager.AddComponent<Created>(entity);
            EntityManager.AddComponent<Updated>(entity);
            made++;
            return entity;
        }

        /// <summary>
        /// Where a part sits, given the assembly hangs higher on a bridge than it stands on a post.
        /// Only the height varies by part: the site position already carries the horizontal offset
        /// the planner chose, so the cage and the heads it holds share one vertical line.
        /// </summary>
        private static Transform GetTransformedPosition(SignalSiteData site, SignalPartKind kind)
        {
            float3 position = site.m_Position;
            if (kind is SignalPartKind.TopHead or SignalPartKind.BottomHead)
            {
                float head = site.m_Gantry >= 0 ? kGantryHeadHeight + Mod.setting.adjustGantryHeadHeight : 0f;
                float spacing = kHeadSpacing + Mod.setting.adjustHeadSpacing;
                position.y += kind == SignalPartKind.BottomHead ? head - spacing : head;
                if (site.m_Gantry >= 0)
                {
                    // In the signal's own frame, so the offset follows the track rather than world X
                    var offset = new float3(
                        kGantryHeadSide + Mod.setting.adjustGantryHeadSide,
                        0f,
                        kGantryHeadForward + Mod.setting.adjustGantryHeadForward);
                    position += math.rotate(site.m_Rotation, offset);
                }
            }

            return new Transform(position, site.m_Rotation);
        }

        /// <summary>
        /// Offers parts the renderer has not taken up back to the culling system.
        ///
        /// PreCullingSystem only considers an object on a frame where it carries Updated, and only
        /// records it if it passes culling on that same frame; FailedCulling does nothing for an
        /// object that has no culling index yet. So a static object born outside the camera's range
        /// is never looked at again and stays invisible for good. That is fine for the base game,
        /// which creates objects under the player's cursor, but signals appear all over the map. So
        /// keep handing the unregistered ones back until they are in range and get picked up.
        /// </summary>
        private void AdoptUnrenderedParts()
        {
            if (m_PartQuery.IsEmptyIgnoreFilter || ++m_AdoptTimer < kAdoptInterval)
            {
                return;
            }
            m_AdoptTimer = 0;

            NativeArray<Entity> parts = m_PartQuery.ToEntityArray(Allocator.Temp);
            var pending = new NativeList<Entity>(parts.Length, Allocator.Temp);
            for (int i = 0; i < parts.Length; i++)
            {
                Entity part = parts[i];
                if (EntityManager.HasComponent<CullingInfo>(part)
                    && EntityManager.GetComponentData<CullingInfo>(part).m_CullingIndex == 0
                    && !EntityManager.HasComponent<Updated>(part))
                {
                    pending.Add(part);
                }
            }
            if (pending.Length > 0)
            {
                EntityManager.AddComponent<Updated>(pending.AsArray());
            }
            pending.Dispose();
            parts.Dispose();
        }

        private int m_AdoptTimer;

        /// <summary>
        /// Reports whether the renderer has taken up each part. CullingInfo and MeshBatch are what
        /// decide whether an object is drawn at all, so they separate "never registered" from
        /// "registered but culled" from "fine, so the fault is in the asset".
        /// </summary>
        private void LogRenderState()
        {
            for (int i = 0; i < m_Network.m_Sites.Length && i < 2; i++)
            {
                SignalSiteData site = m_Network.m_Sites[i];
                Mod.log.Info($"render+{m_ProbeFrames} site {i} (gantry {site.m_Gantry}): mast {Render(site.m_Mast)} | top {Render(site.m_Signal)} | bottom {Render(site.m_BottomHead)}");
            }
            if (m_Network.m_Gantries.Length > 0)
            {
                Mod.log.Info($"render+{m_ProbeFrames} bridge 0: {Render(m_Network.m_Gantries[0].m_Entity)}");
            }
            if (m_ProbeFrames == 0 && m_Network.m_Sites.Length > 0)
            {
                LogArchetype("head", m_Network.m_Sites[0].m_Signal);
                LogArchetype("mast", m_Network.m_Sites[0].m_Mast);
                if (m_Network.m_Gantries.Length > 0)
                {
                    LogArchetype("bridge", m_Network.m_Gantries[0].m_Entity);
                }
            }
        }

        /// <summary>
        /// Dumps an entity's whole archetype. Guessing at one component at a time has not worked;
        /// the difference between a part that draws and one that does not has to be in here.
        /// </summary>
        private void LogArchetype(string label, Entity e)
        {
            if (e == Entity.Null || !EntityManager.Exists(e))
            {
                Mod.log.Info($"archetype {label}: no entity");
                return;
            }
            var types = EntityManager.GetChunk(e).Archetype.GetComponentTypes(Allocator.Temp);
            var names = new System.Collections.Generic.List<string>(types.Length);
            for (int i = 0; i < types.Length; i++)
            {
                string n = types[i].GetManagedType().FullName;
                names.Add(n.Substring(n.LastIndexOf('.') + 1));
            }
            names.Sort(System.StringComparer.OrdinalIgnoreCase);
            types.Dispose();
            Mod.log.Info($"archetype {label} ({names.Count}): {string.Join(" ", names)}");
        }

        private string Render(Entity e)
        {
            if (e == Entity.Null)
            {
                return "null";
            }
            if (!EntityManager.Exists(e))
            {
                return $"{e.Index} DEAD";
            }
            var b = new System.Text.StringBuilder($"{e.Index}");
            if (EntityManager.HasComponent<Hidden>(e)) b.Append(" HIDDEN");
            if (EntityManager.HasComponent<Deleted>(e)) b.Append(" DELETED");
            if (!EntityManager.HasComponent<Static>(e)) b.Append(" no-Static");
            if (EntityManager.HasComponent<CullingInfo>(e))
            {
                CullingInfo c = EntityManager.GetComponentData<CullingInfo>(e);
                float3 size = c.m_Bounds.max - c.m_Bounds.min;
                float3 centre = (c.m_Bounds.min + c.m_Bounds.max) * 0.5f;
                b.Append($" cull(centre={centre.x:0.#},{centre.y:0.#},{centre.z:0.#} size={size.x:0.#}x{size.y:0.#}x{size.z:0.#}"
                    + $" r={c.m_Radius:0.#} idx={c.m_CullingIndex} mask={(int)c.m_Mask} minLod={c.m_MinLod} passed={c.m_PassedCulling})");
            }
            else b.Append(" no-CullingInfo");
            b.Append(EntityManager.HasBuffer<MeshBatch>(e) ? $" batches={EntityManager.GetBuffer<MeshBatch>(e).Length}" : " no-MeshBatch");
            if (EntityManager.HasComponent<Game.Objects.Transform>(e))
            {
                float3 pos = EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position;
                b.Append($" xform={pos.x:0.#},{pos.y:0.#},{pos.z:0.#}");
            }
            return b.ToString();
        }

        /// <summary>
        /// Writes where everything ended up. A head that is placed but invisible looks identical to
        /// one that was never placed, and only the positions tell the two apart.
        /// </summary>
        private void LogLayout()
        {
            for (int g = 0; g < m_Network.m_Gantries.Length; g++)
            {
                GantryData gantry = m_Network.m_Gantries[g];
                var members = new System.Text.StringBuilder();
                int count = 0;
                for (int i = 0; i < m_Network.m_Sites.Length; i++)
                {
                    SignalSiteData site = m_Network.m_Sites[i];
                    if (site.m_Gantry != g)
                    {
                        continue;
                    }
                    count++;
                    members.Append($" [{site.m_Class}/{site.m_Speed} top {Describe(site.m_Signal)} bottom {Describe(site.m_BottomHead)} at {site.m_Position.x:0.#},{site.m_Position.y:0.#},{site.m_Position.z:0.#}]");
                }
                Mod.log.Info($"bridge {g}: span {gantry.m_Span:0.##} at {gantry.m_Position.x:0.#},{gantry.m_Position.y:0.#},{gantry.m_Position.z:0.#}, {count} heads:{members}");
            }
            for (int i = 0; i < m_Network.m_Sites.Length; i++)
            {
                SignalSiteData site = m_Network.m_Sites[i];
                if (site.m_Gantry < 0)
                {
                    Mod.log.Info($"lineside {i}: {site.m_Class}/{site.m_Speed} mast {Describe(site.m_Mast)} top {Describe(site.m_Signal)} bottom {Describe(site.m_BottomHead)} at {site.m_Position.x:0.#},{site.m_Position.y:0.#},{site.m_Position.z:0.#}");
                }
            }
        }

        private string Describe(Entity entity)
        {
            if (entity == Entity.Null)
            {
                return "none";
            }
            return EntityManager.Exists(entity) ? entity.Index.ToString() : $"{entity.Index}!dead";
        }

        private void SetStackRange(Entity entity, Entity prefab, float min, float max)
        {
            if (!EntityManager.HasComponent<Game.Objects.Stack>(entity))
            {
                return;
            }
            var stack = new Game.Objects.Stack { m_Range = new Bounds1(min, max) };
            if (EntityManager.HasComponent<StackData>(prefab))
            {
                BatchDataHelpers.AlignStack(ref stack, EntityManager.GetComponentData<StackData>(prefab), start: false, end: false);
            }
            EntityManager.SetComponentData(entity, stack);
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
