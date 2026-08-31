using Colossal.Mathematics;
using Game;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
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

        private int m_ProbeFrames;

        /// <summary>Frames of quiet after the last track change before rebuilding.</summary>
        private const int kSettleFrames = 20;

        /// <summary>How often unregistered parts are offered to the culling system again.</summary>
        private const int kAdoptInterval = 30;

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
            AdoptUnrenderedParts();
            if (m_ProbeFrames > 0 && --m_ProbeFrames % 15 == 0)
            {
                LogRenderState();
            }
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
                m_Setback = Mod.setting.signalSetback,
                m_LateralOffset = 2f,
                m_LeftHandTraffic = m_CityConfigurationSystem.leftHandTraffic,
                m_MediumCurviness = 1f / math.max(1f, Mod.setting.mediumSpeedCurveRadius),
                m_MediumSpeedLimit = Mod.setting.mediumSpeedLimit / 3.6f,
                m_MediumBlockLength = Mod.setting.mediumSpeedBlockLength,
                m_MinGantryTracks = Mod.setting.minGantryTracks,
                m_MaxGantryTrackSpacing = Mod.setting.maxGantryTrackSpacing,
                m_GantryAlignTolerance = Mod.setting.gantryAlignTolerance,
                m_GantryMargin = Mod.setting.gantryMargin
            };
            planner.Plan(trackLanes, ref m_Network);
            trackLanes.Dispose();

            PlaceSignalObjects();

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
            var prefabs = new Entity[4];
            var archetypes = new EntityArchetype[4];
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

        /// <summary>Where a head sits, given the assembly is taller on a bridge than on a post.</summary>
        private static Transform GetTransformedPosition(SignalSiteData site, SignalPartKind kind)
        {
            float3 position = site.m_Position;
            if (kind is SignalPartKind.TopHead or SignalPartKind.BottomHead)
            {
                // If this is on a gantry, move it up a little and forward to clear the cage
                float head = site.m_Gantry >= 0 ? Mod.setting.gantryHeadHeight : 0;
                position.x += site.m_Gantry >= 0 ? Mod.setting.gantryHeadOffset : 0;
                // If this is a bottom head, move it down 1.1m (spacing distance)
                position.y += kind == SignalPartKind.BottomHead ? head - 1.1f : head;
            }
            else if (kind is SignalPartKind.Mast)
            {
                // If this is on a gantry, move it forward to clear the lattice
                position.x += site.m_Gantry >= 0 ? Mod.setting.gantryCageOffset : 0;
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

        /// <summary>Which part of a signal an object is. The asset used for it is chosen separately.</summary>
        private enum SignalPart
        {
            Mast,
            TopHead,
            BottomHead
        }

        private void PlaceParts(SignalAsset asset, SignalPart part, NativeList<int> siteIndices, EntityArchetype[] archetypes, Entity[] prefabs)
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
                position.y += GetPartHeight(site, part);
                Initialize(entity, prefab, position, site.m_Rotation);

                if (part == SignalPart.Mast)
                {
                    // A mast built as a stack grows its shaft to reach whatever head height is set,
                    // so one asset serves any height rather than fixing it in the model.
                    SetStackRange(entity, prefab, 0f, Mod.setting.signalHeadHeight);
                    site.m_Mast = entity;
                }
                else if (part == SignalPart.BottomHead)
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
        private static float GetPartHeight(SignalSiteData site, SignalPart part)
        {
            if (part == SignalPart.Mast)
            {
                return 0f;
            }
            float head = site.m_Gantry >= 0 ? Mod.setting.gantryHeadHeight : Mod.setting.signalHeadHeight;
            return part == SignalPart.BottomHead ? head - Mod.setting.headSpacing : head;
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
                Initialize(entity, prefab, gantry.m_Position, gantry.m_Rotation);
                // The beam mesh tiles between the leg meshes to fill this range along the local X
                // axis, which is how one structure covers any number of tracks.
                SetStackRange(entity, prefab, -gantry.m_Span, gantry.m_Span);
                gantry.m_Entity = entity;
                m_Network.m_Gantries[i] = gantry;
            }
            created.Dispose();
        }

        /// <summary>
        /// Brings one object into being. Deliberately no Owner or Secondary: those put the object
        /// into SecondaryObjectReferencesSystem's query, whose Burst job indexes the owner's
        /// SubObject buffer without checking it exists, and a track edge only has that buffer if its
        /// composition declares sub-objects. Lifetime is handled instead by RailwaySignalPart, which
        /// every plan rebuild clears out wholesale.
        /// </summary>
        private void Initialize(Entity entity, Entity prefab, float3 position, quaternion rotation)
        {
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

        /// <summary>
        /// Sets how far a stacked object tiles along its own axis. The game never leaves a range
        /// narrower than the two end pieces, so it is put through the same alignment the base game
        /// applies; a range the end pieces cannot fit inside produces a degenerate tiling.
        /// </summary>
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
