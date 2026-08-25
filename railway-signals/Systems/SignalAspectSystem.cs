using Game;
using Game.Net;
using Game.Objects;
using Game.Vehicles;
using RailwaySignals.Signalling;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RailwaySignals.Systems
{
    /// <summary>
    /// Works out what each signal is showing and pushes it onto the post. Occupancy comes from the
    /// same state that makes trains wait in the base game: vehicles sitting in a lane
    /// (<c>LaneObject</c>) and lanes a train has claimed ahead of itself (<c>LaneReservation</c>).
    /// A train still on approach to a signal does not put that signal to danger with its own
    /// claim, but a claim from any other movement does.
    /// </summary>
    public partial class SignalAspectSystem : GameSystemBase
    {
        private SignalNetworkSystem m_NetworkSystem;

        private EntityQuery m_TrainQuery;

        private ComponentLookup<LaneReservation> m_LaneReservationData;

        private ComponentLookup<Game.Net.TrackLane> m_TrackLaneData;

        private ComponentLookup<Controller> m_ControllerData;

        private ComponentLookup<Train> m_TrainData;

        private ComponentLookup<TrafficLight> m_TrafficLightData;

        private ComponentLookup<RailwaySignal> m_RailwaySignalData;

        private BufferLookup<LaneObject> m_LaneObjects;

        private BufferLookup<LaneOverlap> m_LaneOverlaps;

        private BufferLookup<TrainNavigationLane> m_NavigationLanes;

        private NativeParallelHashMap<Entity, int> m_ApproachingSignal;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_NetworkSystem = World.GetOrCreateSystemManaged<SignalNetworkSystem>();
            m_TrainQuery = GetEntityQuery(ComponentType.ReadOnly<TrainNavigationLane>(), ComponentType.ReadOnly<Game.Vehicles.LayoutElement>());
            m_LaneReservationData = GetComponentLookup<LaneReservation>(isReadOnly: true);
            m_TrackLaneData = GetComponentLookup<Game.Net.TrackLane>(isReadOnly: true);
            m_ControllerData = GetComponentLookup<Controller>(isReadOnly: true);
            m_TrainData = GetComponentLookup<Train>(isReadOnly: true);
            m_TrafficLightData = GetComponentLookup<TrafficLight>(isReadOnly: false);
            m_RailwaySignalData = GetComponentLookup<RailwaySignal>(isReadOnly: false);
            m_LaneObjects = GetBufferLookup<LaneObject>(isReadOnly: true);
            m_LaneOverlaps = GetBufferLookup<LaneOverlap>(isReadOnly: true);
            m_NavigationLanes = GetBufferLookup<TrainNavigationLane>(isReadOnly: true);
            m_ApproachingSignal = new NativeParallelHashMap<Entity, int>(64, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            m_ApproachingSignal.Dispose();
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

            m_LaneReservationData.Update(this);
            m_TrackLaneData.Update(this);
            m_ControllerData.Update(this);
            m_TrainData.Update(this);
            m_TrafficLightData.Update(this);
            m_RailwaySignalData.Update(this);
            m_LaneObjects.Update(this);
            m_LaneOverlaps.Update(this);
            m_NavigationLanes.Update(this);

            FindApproachingSignals(ref network);
            FindBlockedSignals(ref network);
            ApplyAspects(ref network);
        }

        /// <summary>
        /// For every train, the signal it is running towards. Its own claim on the block beyond that
        /// signal is what would otherwise make the signal protecting it show stop.
        /// </summary>
        private void FindApproachingSignals(ref SignalNetwork network)
        {
            m_ApproachingSignal.Clear();
            NativeArray<Entity> trains = m_TrainQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < trains.Length; i++)
            {
                if (!m_NavigationLanes.TryGetBuffer(trains[i], out DynamicBuffer<TrainNavigationLane> lanes))
                {
                    continue;
                }
                for (int j = 0; j < lanes.Length; j++)
                {
                    TrainNavigationLane lane = lanes[j];
                    var directed = new DirectedLane(lane.m_Lane, lane.m_CurvePosition.y >= lane.m_CurvePosition.x);
                    if (network.m_SiteByApproach.TryGetValue(directed, out int site))
                    {
                        m_ApproachingSignal[trains[i]] = site;
                        break;
                    }
                }
            }
            trains.Dispose();
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
                    blocked = IsLaneBusy(lane, i);
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
                            blocked = IsLaneBusy(overlap.m_Other, i);
                        }
                    }
                }
                SignalSiteData site = network.m_Sites[i];
                site.m_Blocked = blocked;
                network.m_Sites[i] = site;
            }
        }

        /// <summary>True when a train stands in the lane, or one that is not the train this signal is admitting has claimed it.</summary>
        private bool IsLaneBusy(Entity lane, int siteIndex)
        {
            if (m_LaneObjects.TryGetBuffer(lane, out DynamicBuffer<LaneObject> objects))
            {
                for (int i = 0; i < objects.Length; i++)
                {
                    if (m_TrainData.HasComponent(objects[i].m_LaneObject))
                    {
                        return true;
                    }
                }
            }
            if (!m_LaneReservationData.TryGetComponent(lane, out LaneReservation reservation) || reservation.GetPriority() == 0)
            {
                return false;
            }
            Entity claimant = reservation.m_Blocker;
            if (m_ControllerData.TryGetComponent(claimant, out Controller controller) && controller.m_Controller != Entity.Null)
            {
                claimant = controller.m_Controller;
            }
            return !m_ApproachingSignal.TryGetValue(claimant, out int approaching) || approaching != siteIndex;
        }

        private void ApplyAspects(ref SignalNetwork network)
        {
            for (int i = 0; i < network.m_Sites.Length; i++)
            {
                SignalSiteData site = network.m_Sites[i];
                site.m_Aspect = ResolveAspect(ref network, i, site);
                network.m_Sites[i] = site;

                if (m_TrafficLightData.TryGetComponent(site.m_Signal, out TrafficLight light))
                {
                    Game.Objects.TrafficLightState state = GetLightState(site.m_Aspect);
                    if (light.m_State != state)
                    {
                        light.m_State = state;
                        m_TrafficLightData[site.m_Signal] = light;
                    }
                }
                if (m_RailwaySignalData.TryGetComponent(site.m_Signal, out RailwaySignal signal) && signal.m_Aspect != site.m_Aspect)
                {
                    signal.m_Aspect = site.m_Aspect;
                    m_RailwaySignalData[site.m_Signal] = signal;
                }
            }
        }

        /// <summary>
        /// Caution when the block ahead is clear but any signal at the far end of it is at stop, or
        /// when the block runs into buffers. Without knowing which way a train will be routed at a
        /// divergence, warning for the worst of the routes is the safe reading, and the same goes
        /// for warning of a medium speed signal ahead.
        /// </summary>
        private static SignalAspect ResolveAspect(ref SignalNetwork network, int siteIndex, SignalSiteData site)
        {
            if (site.m_Blocked)
            {
                return SignalAspect.Stop;
            }
            int2 range = network.m_SuccessorRanges[siteIndex];
            if (range.y == 0)
            {
                return SignalAspect.Caution;
            }
            bool medium = false;
            for (int i = range.x; i < range.x + range.y; i++)
            {
                SignalSiteData successor = network.m_Sites[network.m_Successors[i]];
                if (successor.m_Blocked)
                {
                    return SignalAspect.Caution;
                }
                medium |= successor.m_Speed == SignalSpeed.Medium;
            }
            return (medium && site.m_Speed == SignalSpeed.Normal) ? SignalAspect.ReduceToMedium : SignalAspect.Clear;
        }

        /// <summary>
        /// Maps an aspect onto the lamps. Reduce to medium has no lamp of its own on a three
        /// position head, so it is signalled either by flashing the green or, on a signal modelled
        /// with a second head, by showing yellow above the green.
        /// </summary>
        private static Game.Objects.TrafficLightState GetLightState(SignalAspect aspect)
        {
            switch (aspect)
            {
                case SignalAspect.Stop:
                    return Game.Objects.TrafficLightState.Red;
                case SignalAspect.Caution:
                    return Game.Objects.TrafficLightState.Yellow;
                case SignalAspect.ReduceToMedium:
                    return Mod.setting.mediumIndication == MediumIndication.YellowOverGreen
                        ? Game.Objects.TrafficLightState.Yellow | Game.Objects.TrafficLightState.Green
                        : Game.Objects.TrafficLightState.Green | Game.Objects.TrafficLightState.Flashing;
                default:
                    return Game.Objects.TrafficLightState.Green;
            }
        }
    }
}
