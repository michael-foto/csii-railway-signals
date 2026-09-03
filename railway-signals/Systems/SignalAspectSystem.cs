using Game;
using Game.Net;
using Game.Common;
using Game.Objects;
using Game.Pathfind;
using Game.Rendering;
using Game.Vehicles;
using RailwaySignals.Signalling;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RailwaySignals.Systems
{
    /// <summary>
    /// Works out what each signal is showing and pushes it onto the post.
    /// <para>
    /// A block is occupied when a train stands in it (<c>LaneObject</c>), never when one has merely
    /// claimed it: a train reserves track over its whole braking distance ahead of itself, so
    /// reading claims put a signal to danger seconds before the train it was admitting arrived.
    /// </para>
    /// <para>
    /// Speed follows the road a train is actually booked over, read from its path. A signal with no
    /// train booked over it warns for the worst of every road out of its block, since a following
    /// train has no guarantee of being turned down a clear branch. Roads that exist only by
    /// reversing are not counted; those are read off a train's own path when one is booked over.
    /// </para>
    /// </summary>
    public partial class SignalAspectSystem : GameSystemBase
    {
        /// <summary>Where the road set over a signal leads and what speed it may be taken at.</summary>
        private struct RouteData
        {
            /// <summary>Site the road runs to, or -1 when it stops short of another signal.</summary>
            public int m_Successor;

            public SignalSpeed m_Speed;
        }

        private SignalNetworkSystem m_NetworkSystem;

        private EntityQuery m_TrainQuery;

        private ComponentLookup<Game.Net.TrackLane> m_TrackLaneData;

        private ComponentLookup<Train> m_TrainData;

        private ComponentLookup<TrafficLight> m_TrafficLightData;

        private ComponentLookup<RailwaySignal> m_RailwaySignalData;

        private BufferLookup<LaneObject> m_LaneObjects;

        private BufferLookup<LaneOverlap> m_LaneOverlaps;

        private BufferLookup<TrainNavigationLane> m_NavigationLanes;

        private BufferLookup<Game.Vehicles.LayoutElement> m_Layouts;

        private ComponentLookup<TrainCurrentLane> m_CurrentLaneData;

        private BufferLookup<PathElement> m_PathElements;

        private ComponentLookup<PathOwner> m_PathOwnerData;

        /// <summary>The road set over each signal, keyed by site index. Absent means no route is set.</summary>
        private NativeParallelHashMap<int, RouteData> m_RouteBySite;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_NetworkSystem = World.GetOrCreateSystemManaged<SignalNetworkSystem>();
            m_TrainQuery = GetEntityQuery(ComponentType.ReadOnly<TrainNavigationLane>(), ComponentType.ReadOnly<Game.Vehicles.LayoutElement>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
            m_TrackLaneData = GetComponentLookup<Game.Net.TrackLane>(isReadOnly: true);
            m_TrainData = GetComponentLookup<Train>(isReadOnly: true);
            m_TrafficLightData = GetComponentLookup<TrafficLight>(isReadOnly: false);
            m_RailwaySignalData = GetComponentLookup<RailwaySignal>(isReadOnly: false);
            m_LaneObjects = GetBufferLookup<LaneObject>(isReadOnly: true);
            m_LaneOverlaps = GetBufferLookup<LaneOverlap>(isReadOnly: true);
            m_NavigationLanes = GetBufferLookup<TrainNavigationLane>(isReadOnly: true);
            m_Layouts = GetBufferLookup<Game.Vehicles.LayoutElement>(isReadOnly: true);
            m_CurrentLaneData = GetComponentLookup<TrainCurrentLane>(isReadOnly: true);
            m_PathElements = GetBufferLookup<PathElement>(isReadOnly: true);
            m_PathOwnerData = GetComponentLookup<PathOwner>(isReadOnly: true);
            m_RouteBySite = new NativeParallelHashMap<int, RouteData>(64, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            m_RouteBySite.Dispose();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            ref SignalNetwork network = ref m_NetworkSystem.network;
            if (!Mod.setting.enableSignals || network.m_Sites.Length == 0)
            {
                return;
            }
            CompleteDependency();

            m_TrackLaneData.Update(this);
            m_TrainData.Update(this);
            m_TrafficLightData.Update(this);
            m_RailwaySignalData.Update(this);
            m_LaneObjects.Update(this);
            m_LaneOverlaps.Update(this);
            m_NavigationLanes.Update(this);
            m_Layouts.Update(this);
            m_CurrentLaneData.Update(this);
            m_PathElements.Update(this);
            m_PathOwnerData.Update(this);

            FindSetRoutes(ref network);
            FindBlockedSignals(ref network);
            ApplyAspects(ref network);
        }

        /// <summary>
        /// Prices the road each train has set over the signal in front of it, keyed by site index.
        /// A signal with no train routed over it gets no entry, and then weighs every road out of
        /// its block instead.
        /// <para>
        /// The road has to be read from two buffers in turn. TrainNavigationLane holds the lanes
        /// immediately ahead, but is trimmed from the front as the train advances, so it loses the
        /// lane the train is on. PathElement holds the rest, but only from m_ElementIndex, which
        /// runs ahead of the train because an element is consumed when it is copied into the
        /// navigation buffer rather than when the train drives over it. Reading it from index zero
        /// instead is no help either: PathUtils.TrimPath deletes consumed elements outright when a
        /// train picks up the next leg of its line.
        /// </para>
        /// </summary>
        private void FindSetRoutes(ref SignalNetwork network)
        {
            m_RouteBySite.Clear();
            NativeArray<Entity> trains = m_TrainQuery.ToEntityArray(Allocator.Temp);
            var road = new NativeList<DirectedLane>(32, Allocator.Temp);

            for (int i = 0; i < trains.Length; i++)
            {
                CollectRoadAhead(trains[i], ref road);

                int siteIndex = -1;
                var route = new RouteData { m_Successor = -1, m_Speed = SignalSpeed.Normal };
                for (int j = 0; j < road.Length; j++)
                {
                    DirectedLane lane = road[j];
                    bool isSite = network.m_SiteByApproach.TryGetValue(lane, out int site);
                    if (isSite && siteIndex < 0)
                    {
                        siteIndex = site;
                        continue;
                    }
                    if (siteIndex < 0)
                    {
                        continue;
                    }
                    // The next signal's own approach lane is the last lane of this block, so it
                    // counts towards the price of the road before the walk stops on it.
                    if (network.m_MediumLanes.Contains(lane.m_Lane))
                    {
                        route.m_Speed = SignalSpeed.Medium;
                    }
                    if (isSite)
                    {
                        route.m_Successor = site;
                        break;
                    }
                }
                if (siteIndex >= 0 && !m_RouteBySite.ContainsKey(siteIndex))
                {
                    m_RouteBySite.Add(siteIndex, route);
                }
            }

            road.Dispose();
            trains.Dispose();
        }

        /// <summary>
        /// The lanes a train will run over from where it is now, in order, as directed lanes.
        /// </summary>
        private void CollectRoadAhead(Entity train, ref NativeList<DirectedLane> road)
        {
            road.Clear();

            // The lane under the train comes first. The navigation buffer drops a lane as the
            // leading bogie enters it, so without this the signal at the end of the lane a train
            // is running along is missing from its own road, and a train standing at a terminal
            // platform never books the departure signal in front of it.
            // Read from layout[0], the leading car: the controller is appended last on a
            // locomotive hauled consist, so its own bogies are at the back of the train.
            if (m_Layouts.TryGetBuffer(train, out DynamicBuffer<Game.Vehicles.LayoutElement> layout) && layout.Length > 0
                && m_CurrentLaneData.TryGetComponent(layout[0].m_Vehicle, out TrainCurrentLane current)
                && current.m_Front.m_Lane != Entity.Null)
            {
                float4 span = current.m_Front.m_CurvePosition;
                road.Add(Direct(current.m_Front.m_Lane, new float2(span.x, span.w)));
            }

            if (m_NavigationLanes.TryGetBuffer(train, out DynamicBuffer<TrainNavigationLane> lanes))
            {
                for (int i = 0; i < lanes.Length; i++)
                {
                    road.Add(Direct(lanes[i].m_Lane, lanes[i].m_CurvePosition));
                }
            }
            if (!m_PathElements.TryGetBuffer(train, out DynamicBuffer<PathElement> path)
                || !m_PathOwnerData.TryGetComponent(train, out PathOwner owner))
            {
                return;
            }
            for (int i = math.max(0, owner.m_ElementIndex); i < path.Length; i++)
            {
                road.Add(Direct(path[i].m_Target, path[i].m_TargetDelta));
            }
        }

        /// <summary>
        /// Which way a train runs over a lane, from the span of it the path covers. The span is in
        /// the lane's own curve parameter, the same direction the plan keys its sites by. A span
        /// that covers no distance says nothing, so the lane's own geometry decides instead.
        /// </summary>
        private DirectedLane Direct(Entity lane, float2 span)
        {
            if (span.y > span.x)
            {
                return new DirectedLane(lane, true);
            }
            if (span.y < span.x)
            {
                return new DirectedLane(lane, false);
            }
            // Degenerate span. A one-way lane can only be run forwards; on two-way track both
            // directions can be sites of their own, so guessing would light the wrong signal.
            bool twoway = m_TrackLaneData.TryGetComponent(lane, out Game.Net.TrackLane trackLane)
                && (trackLane.m_Flags & TrackLaneFlags.Twoway) != 0;
            return new DirectedLane(lane, !twoway || (trackLane.m_Flags & TrackLaneFlags.Invert) == 0);
        }

        private void FindBlockedSignals(ref SignalNetwork network)
        {
            const OverlapFlags merges = OverlapFlags.MergeStart | OverlapFlags.MergeEnd | OverlapFlags.MergeMiddleStart | OverlapFlags.MergeMiddleEnd;

            for (int i = 0; i < network.m_Sites.Length; i++)
            {
                int2 range = network.m_BlockRanges[i];
                bool blocked = false;
                for (int j = range.x; j < range.x + range.y && !blocked; j++)
                {
                    Entity lane = network.m_BlockLanes[j].m_Lane;
                    blocked = IsLaneBusy(lane);
                    if (blocked || !m_LaneOverlaps.TryGetBuffer(lane, out DynamicBuffer<LaneOverlap> overlaps))
                    {
                        continue;
                    }
                    // A movement crossing this block on another lane conflicts with it; a movement
                    // merging into it is already covered by the lanes of the block itself.
                    for (int k = 0; k < overlaps.Length && !blocked; k++)
                    {
                        LaneOverlap overlap = overlaps[k];
                        if ((overlap.m_Flags & merges) == 0 && m_TrackLaneData.HasComponent(overlap.m_Other))
                        {
                            blocked = IsLaneBusy(overlap.m_Other);
                        }
                    }
                }
                SignalSiteData site = network.m_Sites[i];
                site.m_Blocked = blocked;
                network.m_Sites[i] = site;
            }
        }

        /// <summary>
        /// True when a train stands in the lane. Occupancy only, never a claim: a train reserves
        /// track over its whole braking distance ahead of itself, so counting reservations put the
        /// signal it was still approaching to danger seconds before it got there. LaneObject holds
        /// a vehicle only for lanes its bogies are actually on, so nothing has to be excused here.
        /// </summary>
        private bool IsLaneBusy(Entity lane)
        {
            if (!m_LaneObjects.TryGetBuffer(lane, out DynamicBuffer<LaneObject> objects))
            {
                return false;
            }
            for (int i = 0; i < objects.Length; i++)
            {
                if (m_TrainData.HasComponent(objects[i].m_LaneObject))
                {
                    return true;
                }
            }
            return false;
        }

        private void ApplyAspects(ref SignalNetwork network)
        {
            // Resolved first for every signal, because an aspect warning of medium speed ahead
            // reads its neighbour's speed and would otherwise see last tick's value.
            for (int i = 0; i < network.m_Sites.Length; i++)
            {
                SignalSiteData site = network.m_Sites[i];
                site.m_Speed = m_RouteBySite.TryGetValue(i, out RouteData set)
                    ? set.m_Speed
                    : (site.m_HasNormalRoute ? SignalSpeed.Normal : SignalSpeed.Medium);
                network.m_Sites[i] = site;
            }
            for (int i = 0; i < network.m_Sites.Length; i++)
            {
                SignalSiteData site = network.m_Sites[i];
                site.m_Aspect = ResolveAspect(ref network, i, site);
                network.m_Sites[i] = site;

                SetLamp(site.m_Signal, site.m_Aspect.TopLamp());
                SetLamp(site.m_BottomHead, site.m_Aspect.BottomLamp());

                if (m_RailwaySignalData.TryGetComponent(site.m_Signal, out RailwaySignal signal) && signal.m_Aspect != site.m_Aspect)
                {
                    signal.m_Aspect = site.m_Aspect;
                    m_RailwaySignalData[site.m_Signal] = signal;
                }
            }
        }

        /// <summary>
        /// Lights one head. Each head is its own object with its own TrafficLight, so both drive the
        /// ordinary TrafficLight_Red/Yellow/Green purposes and a head can show any of the three.
        /// </summary>
        private void SetLamp(Entity head, SignalLamp lamp)
        {
            if (!m_TrafficLightData.TryGetComponent(head, out TrafficLight light))
            {
                return;
            }
            Game.Objects.TrafficLightState state;
            switch (lamp)
            {
                case SignalLamp.Red:
                    state = Game.Objects.TrafficLightState.Red;
                    break;
                case SignalLamp.Yellow:
                    state = Game.Objects.TrafficLightState.Yellow;
                    break;
                case SignalLamp.Green:
                    state = Game.Objects.TrafficLightState.Green;
                    break;
                default:
                    state = Game.Objects.TrafficLightState.None;
                    break;
            }
            if (light.m_State != state)
            {
                light.m_State = state;
                m_TrafficLightData[head] = light;
            }
        }

        /// <summary>
        /// What a signal shows, given the road set over it. Speed follows the road a train is
        /// actually booked over rather than the worst road out of the block, so points into a slow
        /// siding no longer pull a train down when it is routed straight on.
        /// <para>
        /// With no road set there is nothing to price. A home signal protects pointwork or buffers
        /// and stays at danger until a route is set over it, the way an interlocked signal does. An
        /// automatic only divides plain line and has no route to wait for, so it keeps showing a
        /// road: the least restrictive one out of the block, which is the fast road at a junction.
        /// Occupancy stays worst case in both cases, because a signal must not clear over track
        /// something else is standing in whichever way the points happen to lie.
        /// </para>
        /// </summary>
        /// <summary>
        /// Whether a signal is at danger for a reason of its own, without regard to what lies beyond
        /// it. Read both for the signal being resolved and for the one ahead of it, so a caution is
        /// shown for a home signal held at stop by having no route set and not just for one with
        /// something standing in its block.
        /// </summary>
        private bool IsAtStop(SignalSiteData site)
        {
            return site.m_Blocked || !site.m_HasClearRoute;
        }

        /// <summary>
        /// Reads every road out of the block for a signal with no train booked over it, worst case:
        /// any one of them shut brings this signal to caution, because a following train has no
        /// guarantee of being turned down a clear branch at the junction ahead. False means a
        /// warning is called for.
        /// <para>
        /// A signal at a buffer stop counts like any other, which is what puts the signal before a
        /// terminal platform or a siding at caution. Roads that only exist by turning travel back on
        /// itself are already absent, filtered out of the lane graph by TrackGraph, so a departure
        /// signal is not held down by the buffers of the platform alongside it.
        /// </para>
        /// </summary>
        private bool LookAhead(ref SignalNetwork network, int siteIndex, out bool mediumAhead)
        {
            mediumAhead = false;
            int2 range = network.m_SuccessorRanges[siteIndex];
            for (int i = range.x; i < range.x + range.y; i++)
            {
                SignalSiteData successor = network.m_Sites[network.m_Successors[i]];
                if (IsAtStop(successor))
                {
                    return false;
                }
                mediumAhead |= successor.m_Speed == SignalSpeed.Medium;
            }
            return true;
        }

        private SignalAspect ResolveAspect(ref SignalNetwork network, int siteIndex, SignalSiteData site)
        {
            if (IsAtStop(site))
            {
                return SignalAspect.Stop;
            }

            bool medium = site.m_Speed == SignalSpeed.Medium;
            bool mediumAhead;

            // A route with a successor is the only case where the signal ahead is known. A train
            // booked no further than this block, which is every train terminating at a platform,
            // says nothing about the road beyond the signal, so it falls back with the rest.
            if (m_RouteBySite.TryGetValue(siteIndex, out RouteData route) && route.m_Successor >= 0)
            {
                SignalSiteData ahead = network.m_Sites[route.m_Successor];
                if (IsAtStop(ahead))
                {
                    return medium ? SignalAspect.MediumCaution : SignalAspect.Caution;
                }
                mediumAhead = ahead.m_Speed == SignalSpeed.Medium;
            }
            else if (!LookAhead(ref network, siteIndex, out mediumAhead))
            {
                return medium ? SignalAspect.MediumCaution : SignalAspect.Caution;
            }

            if (medium)
            {
                return SignalAspect.MediumClear;
            }
            return mediumAhead ? SignalAspect.ReduceToMedium : SignalAspect.Clear;
        }
    }
}
