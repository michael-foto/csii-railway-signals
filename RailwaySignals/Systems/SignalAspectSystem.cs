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

        /// <summary>
        /// Priority written onto a lane to hold a train short of it. Any non-zero value satisfies
        /// TrainNavigationSystem.CanReserveLane, which is what actually stops the train, so this is
        /// the lowest value that stays below every other reader: traffic lights count a lane as
        /// occupied from 100, pedestrians give way from 108, and cars halve their speed at exactly
        /// 102. Writing 100 or more would hold level crossing barriers shut.
        /// </summary>
        private const byte kHoldPriority = 99;

        /// <summary>
        /// Presents a site's successors to the rules without copying them out of the plan.
        /// </summary>
        private readonly struct SuccessorView : ISignalStates
        {
            private readonly SignalNetwork m_Network;

            private readonly int2 m_Range;

            public SuccessorView(in SignalNetwork network, int2 range)
            {
                m_Network = network;
                m_Range = range;
            }

            public int Length => m_Range.y;

            public SignalState this[int index] => State(m_Network.m_Sites[m_Network.m_Successors[m_Range.x + index]]);
        }

        private SignalNetworkSystem m_NetworkSystem;

        private EntityQuery m_TrainQuery;

        private ComponentLookup<Game.Net.TrackLane> m_TrackLaneData;

        private ComponentLookup<Train> m_TrainData;

        private ComponentLookup<TrafficLight> m_TrafficLightData;

        private ComponentLookup<RailwaySignal> m_RailwaySignalData;

        private ComponentLookup<LaneReservation> m_LaneReservationData;

        private BufferLookup<LaneObject> m_LaneObjects;

        private BufferLookup<LaneOverlap> m_LaneOverlaps;

        private BufferLookup<TrainNavigationLane> m_NavigationLanes;

        private BufferLookup<Game.Vehicles.LayoutElement> m_Layouts;

        private ComponentLookup<TrainCurrentLane> m_CurrentLaneData;

        private BufferLookup<PathElement> m_PathElements;

        private ComponentLookup<PathOwner> m_PathOwnerData;

        /// <summary>
        /// Lanes a train has a road booked over, mapped to the site that admits it into them.
        /// First writer wins, which is how two trains wanting the same section are arbitrated: the
        /// one whose booking landed first keeps its road and the other's signal stays at danger.
        /// </summary>
        private NativeParallelHashMap<Entity, int> m_BookedBy;

        /// <summary>How many consecutive passes each signal has been holding a train, by site index.</summary>
        private NativeParallelHashMap<int, int> m_HoldPasses;

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
            m_LaneReservationData = GetComponentLookup<LaneReservation>(isReadOnly: false);
            m_LaneObjects = GetBufferLookup<LaneObject>(isReadOnly: true);
            m_LaneOverlaps = GetBufferLookup<LaneOverlap>(isReadOnly: true);
            m_NavigationLanes = GetBufferLookup<TrainNavigationLane>(isReadOnly: true);
            m_Layouts = GetBufferLookup<Game.Vehicles.LayoutElement>(isReadOnly: true);
            m_CurrentLaneData = GetComponentLookup<TrainCurrentLane>(isReadOnly: true);
            m_PathElements = GetBufferLookup<PathElement>(isReadOnly: true);
            m_PathOwnerData = GetComponentLookup<PathOwner>(isReadOnly: true);
            m_RouteBySite = new NativeParallelHashMap<int, RouteData>(64, Allocator.Persistent);
            m_HoldPasses = new NativeParallelHashMap<int, int>(64, Allocator.Persistent);
            m_BookedBy = new NativeParallelHashMap<Entity, int>(256, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            m_RouteBySite.Dispose();
            m_HoldPasses.Dispose();
            m_BookedBy.Dispose();
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
            m_LaneReservationData.Update(this);
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
            HoldTrainsAtStop(ref network);
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
            m_BookedBy.Clear();
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
                    // Booked to the signal that admits the train here, so that signal is not held
                    // by its own train while every other signal reading over this lane is.
                    m_BookedBy.TryAdd(lane.m_Lane, siteIndex);
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
                    blocked = IsLaneClaimed(lane, i);
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
                            blocked = IsLaneClaimed(overlap.m_Other, i);
                        }
                    }
                }
                SignalSiteData site = network.m_Sites[i];
                site.m_Blocked = blocked;
                network.m_Sites[i] = site;
            }
        }

        /// <summary>
        /// True when this signal may not offer the lane: a train stands in it, or another signal has
        /// a road booked over it. Booking is what makes the approaches to a junction go to danger as
        /// soon as a train is signalled through it, rather than only once it arrives.
        /// </summary>
        private bool IsLaneClaimed(Entity lane, int siteIndex)
        {
            bool booked = m_BookedBy.TryGetValue(lane, out int bookedBy);
            return SignalRules.Claimed(IsLaneBusy(lane), booked, bookedBy, siteIndex);
        }

        /// <summary>The slice of a site the rules read.</summary>
        private static SignalState State(in SignalSiteData site)
        {
            return new SignalState(site.m_Blocked, site.m_HasClearRoute, site.m_HasNormalRoute, site.m_Speed);
        }

        private RouteState Route(int siteIndex)
        {
            return m_RouteBySite.TryGetValue(siteIndex, out RouteData route)
                ? new RouteState(route.m_Successor, route.m_Speed)
                : RouteState.None;
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
                site.m_Speed = SignalRules.Speed(State(site), Route(i));
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
        /// Holds a train short of a signal whose block another train stands in, by reserving the
        /// lanes travel enters that block by. TrainNavigationSystem.CanReserveLane refuses a lane
        /// carrying any reservation that is not the train's own, which truncates its reservation
        /// chain and brings it to a stand about ten metres short.
        /// <para>
        /// Only <c>m_Blocked</c> is enforced, never a signal with no road at all: the one at a
        /// buffer stop is at danger for good, and holding trains for it would put every terminal
        /// platform, siding and depot road out of reach.
        /// </para>
        /// <para>
        /// The entry lanes are reserved, never the whole block, or the train already inside it
        /// would be stopped along with the one waiting outside; CanReserveLane excuses a train only
        /// for the lanes its own bogies are standing on, not the ones in front of it.
        /// </para>
        /// <para>
        /// m_Blocker is deliberately left unset. TrainLaneSpeedIterator copies it into the train's
        /// Blocker component, and StuckMovingObjectSystem passes over a train whose blocker is
        /// null, which keeps a hold of ours out of the chain walk that ends in the game deleting
        /// the train. Liveness is this pass's own responsibility instead, through the release
        /// count below.
        /// </para>
        /// </summary>
        private void HoldTrainsAtStop(ref SignalNetwork network)
        {
            if (!Mod.setting.holdTrainsAtSignals)
            {
                if (!m_HoldPasses.IsEmpty)
                {
                    // Nothing to undo: a reservation lapses on its own within a couple of cycles.
                    m_HoldPasses.Clear();
                }
                return;
            }
            int releasePasses = math.max(1, Mod.setting.holdReleaseSeconds * 60 / GetUpdateInterval(SystemUpdatePhase.GameSimulation));

            for (int i = 0; i < network.m_Sites.Length; i++)
            {
                if (!network.m_Sites[i].m_Blocked)
                {
                    m_HoldPasses.Remove(i);
                    continue;
                }
                m_HoldPasses.TryGetValue(i, out int passes);
                m_HoldPasses[i] = passes + 1;

                int2 range = network.m_EntryRanges[i];
                for (int j = range.x; j < range.x + range.y; j++)
                {
                    Entity lane = network.m_EntryLanes[j];
                    bool bookedElsewhere = m_BookedBy.TryGetValue(lane, out int bookedBy) && bookedBy != i;
                    if (SignalRules.ShouldHoldLane(enforcing: true, blocked: true, passes, releasePasses, bookedElsewhere))
                    {
                        Reserve(lane);
                    }
                }
            }
        }

        /// <summary>
        /// Claims a lane at the holding priority. The game only ever raises a reservation, never
        /// lowers one, so writing the same claim every pass is harmless and cannot undo a claim a
        /// train has made for itself.
        /// </summary>
        private void Reserve(Entity lane)
        {
            if (!m_LaneReservationData.HasComponent(lane))
            {
                return;
            }
            ref LaneReservation reservation = ref m_LaneReservationData.GetRefRW(lane).ValueRW;
            if (kHoldPriority > reservation.m_Next.m_Priority)
            {
                reservation.m_Next.m_Priority = kHoldPriority;
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
        /// Resolves what a signal shows, deferring the decision itself to
        /// <see cref="SignalRules"/> so it can be exercised without the game running.
        /// </summary>
        private SignalAspect ResolveAspect(ref SignalNetwork network, int siteIndex, SignalSiteData site)
        {
            RouteState route = Route(siteIndex);
            SignalState ahead = route.m_IsSet && route.m_Successor >= 0
                ? State(network.m_Sites[route.m_Successor])
                : default;
            var successors = new SuccessorView(network, network.m_SuccessorRanges[siteIndex]);
            return SignalRules.Aspect(State(site), route, ahead, successors);
        }
    }
}
