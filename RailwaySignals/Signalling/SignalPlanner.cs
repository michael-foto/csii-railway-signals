using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RailwaySignals.Signalling
{
    /// <summary>
    /// Decides where signals stand and what each one protects. Signals are placed on the approach
    /// side of every junction, at the departure end of station platforms, and at a fixed spacing
    /// along plain line. Each signal's block is then everything reachable ahead of it up to the
    /// next signal, following every diverging route.
    /// </summary>
    public struct SignalPlanner
    {
        private struct Walk
        {
            public DirectedLane m_Lane;

            public float m_Distance;
        }

        public TrackGraph m_Graph;

        public BufferLookup<LaneOverlap> m_LaneOverlaps;

        /// <summary>Target plain-line block length in metres. Zero disables intermediate signals.</summary>
        public float m_BlockSpacing;

        /// <summary>Shortest block worth signalling, in metres. Zero keeps every signal placed.</summary>
        public float m_MinBlockLength;

        public bool m_IntermediateOnBidirectional;

        /// <summary>How far back from the boundary the signal stands, in metres.</summary>
        public float m_Setback;

        /// <summary>Distance from track centre to a lineside post, in metres.</summary>
        public float m_LateralOffset;

        /// <summary>Raises or lowers every placed part from the lane centreline, in metres.</summary>
        public float m_HeightAdjust;

        public bool m_LeftHandTraffic;

        /// <summary>Curves at least this sharp make the signal admitting them a medium speed one. Units are 1/radius.</summary>
        public float m_MediumCurviness;

        /// <summary>Track limited to at or below this speed, in metres per second, is medium speed.</summary>
        public float m_MediumSpeedLimit;

        /// <summary>Blocks no longer than this, in metres, are cramped enough to be medium speed.</summary>
        public float m_MediumBlockLength;

        /// <summary>Fewest tracks under a structure that warrant a signal bridge. Zero disables them.</summary>
        public int m_MinGantryTracks;

        /// <summary>Widest formation one bridge may span, measured across its outermost tracks, in metres.</summary>
        public float m_MaxGantryWidth;

        /// <summary>Widest gap a bridge may reach over from the tracks it spans to the next network, in metres.</summary>
        public float m_MaxGantryTrackGap;

        /// <summary>How far apart along the track two signals can be and still share a bridge, in metres.</summary>
        public float m_GantryAlignTolerance;

        /// <summary>Structure width added beyond the edge of the networks it spans, in metres.</summary>
        public float m_GantryMargin;

        /// <summary>
        /// How far to the driver's side of the track centre a bridge-carried signal hangs, in
        /// metres. The overhead wiring runs down the middle of the track, so a head sitting on the
        /// centreline would foul it.
        /// </summary>
        public float m_GantryLateralOffset;

        /// <summary>
        /// Closest two signals on one bridge may sit across the track, in metres, and equally the
        /// closest two tracks may run and still be counted separately. Approaches to a junction run
        /// nearly parallel a few metres apart, so without this the diverging routes of one switch
        /// each claim a slot and their heads land on top of each other.
        /// </summary>
        public float m_MinGantryTrackSeparation;

        private const int kMaxBlockLanes = 512;

        /// <summary>Tracks must run within about 20 degrees of each other to share a bridge.</summary>
        private const float kParallelDot = 0.94f;

        /// <summary>How far short of the stop blocks a buffer signal stands, in metres.</summary>
        private const float kBufferSetback = 0.5f;

        /// <summary>
        /// Safety bound on the short block sweep rather than the number of sweeps it takes. Folding
        /// a block into a neighbour can leave the result short in turn, but a run of them collapses
        /// in a handful of sweeps and every sweep drops at least one signal.
        /// </summary>
        private const int kMaxPrunePasses = 8;


        public void Plan(NativeList<Entity> trackLanes, ref SignalNetwork network)
        {
            network.Clear();

            var scratch = new NativeList<DirectedLane>(16, Allocator.Temp);
            var scratch2 = new NativeList<DirectedLane>(16, Allocator.Temp);

            PlaceFixedSignals(trackLanes, ref network, ref scratch, ref scratch2);
            PlaceIntermediateSignals(trackLanes, ref network, ref scratch, ref scratch2);
            PruneShortBlocks(ref network);
            BuildBlocks(ref network, ref scratch);
            ClassifySignals(ref network, ref scratch);
            PlanGantries(ref network);

            scratch.Dispose();
            scratch2.Dispose();
        }

        /// <summary>Junction protection and platform starting signals, which depend only on topology.</summary>
        private void PlaceFixedSignals(NativeList<Entity> trackLanes, ref SignalNetwork network, ref NativeList<DirectedLane> scratch, ref NativeList<DirectedLane> scratch2)
        {
            for (int i = 0; i < trackLanes.Length; i++)
            {
                Entity lane = trackLanes[i];
                for (int direction = 0; direction < 2; direction++)
                {
                    var approach = new DirectedLane(lane, direction == 0);
                    if (!m_Graph.CanTravel(approach) || !m_Graph.m_EdgeLaneData.HasComponent(lane))
                    {
                        continue;
                    }
                    if (RunsIntoBuffers(approach, ref scratch))
                    {
                        AddSite(approach, atBuffers: true, ref network);
                    }
                    else if (IsJunctionApproach(approach, ref scratch, ref scratch2) || IsPlatformExit(approach, ref scratch))
                    {
                        AddSite(approach, atBuffers: false, ref network);
                    }
                }
            }
        }

        /// <summary>
        /// True when travel leaving this lane immediately meets points, a crossing, or a converging
        /// route. Only edge lanes qualify, so the signal always stands on plain track short of the
        /// node rather than in among the connector lanes.
        /// </summary>
        private bool IsJunctionApproach(DirectedLane approach, ref NativeList<DirectedLane> successors, ref NativeList<DirectedLane> scratch)
        {
            Entity node = m_Graph.GetExitOwner(approach);
            if (!m_Graph.m_ConnectedEdges.HasBuffer(node))
            {
                return false;
            }
            successors.Clear();
            m_Graph.GetSuccessors(approach, ref successors);
            if (successors.Length == 0)
            {
                return false;
            }
            if (successors.Length > 1)
            {
                return true;
            }
            DirectedLane next = successors[0];
            if (HasPointwork(next.m_Lane) || HasCrossingOverlap(next.m_Lane))
            {
                return true;
            }
            scratch.Clear();
            m_Graph.GetPredecessors(next, ref scratch);
            if (scratch.Length > 1)
            {
                return true;
            }
            // Trailing points read as a single connector lane whose own successor is shared with
            // another route, so look one hop further through the node.
            if (!m_Graph.m_EdgeLaneData.HasComponent(next.m_Lane))
            {
                scratch.Clear();
                m_Graph.GetSuccessors(next, ref scratch);
                if (scratch.Length > 1)
                {
                    return true;
                }
                if (scratch.Length == 1)
                {
                    DirectedLane beyond = scratch[0];
                    if (HasCrossingOverlap(beyond.m_Lane))
                    {
                        return true;
                    }
                    successors.Clear();
                    m_Graph.GetPredecessors(beyond, ref successors);
                    if (successors.Length > 1)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsPlatformExit(DirectedLane approach, ref NativeList<DirectedLane> scratch)
        {
            if (!m_Graph.m_TrackLaneData.TryGetComponent(approach.m_Lane, out var trackLane) || (trackLane.m_Flags & TrackLaneFlags.Station) == 0)
            {
                return false;
            }
            scratch.Clear();
            m_Graph.GetSuccessors(approach, ref scratch);
            if (scratch.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < scratch.Length; i++)
            {
                if (m_Graph.m_TrackLaneData.TryGetComponent(scratch[i].m_Lane, out var next) && (next.m_Flags & TrackLaneFlags.Station) != 0)
                {
                    return false;
                }
            }
            return true;
        }

        private bool HasPointwork(Entity lane)
        {
            return m_Graph.m_TrackLaneData.TryGetComponent(lane, out var trackLane)
                && (trackLane.m_Flags & (TrackLaneFlags.Switch | TrackLaneFlags.DoubleSwitch | TrackLaneFlags.DiamondCrossing)) != 0;
        }

        /// <summary>True when another track crosses this lane rather than merging with it.</summary>
        private bool HasCrossingOverlap(Entity lane)
        {
            if (!m_LaneOverlaps.TryGetBuffer(lane, out var overlaps))
            {
                return false;
            }
            const OverlapFlags merges = OverlapFlags.MergeStart | OverlapFlags.MergeEnd | OverlapFlags.MergeMiddleStart | OverlapFlags.MergeMiddleEnd;
            for (int i = 0; i < overlaps.Length; i++)
            {
                LaneOverlap overlap = overlaps[i];
                if ((overlap.m_Flags & merges) == 0 && m_Graph.m_TrackLaneData.HasComponent(overlap.m_Other))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Walks forward from every signal already placed, dropping an automatic signal each time
        /// the run of plain line since the last one exceeds the configured block spacing.
        /// </summary>
        private void PlaceIntermediateSignals(NativeList<Entity> trackLanes, ref SignalNetwork network, ref NativeList<DirectedLane> scratch, ref NativeList<DirectedLane> scratch2)
        {
            if (m_BlockSpacing <= 0f)
            {
                return;
            }
            var visited = new NativeParallelHashSet<DirectedLane>(trackLanes.Length * 2, Allocator.Temp);
            var stack = new NativeList<Walk>(64, Allocator.Temp);

            for (int i = 0; i < network.m_Sites.Length; i++)
            {
                Push(network.m_Sites[i].m_Approach, 0f, ref stack, ref scratch);
            }
            RunWalk(ref stack, ref visited, ref network, ref scratch, ref scratch2);

            // Track with no junction and no station forms a closed loop that the walk above never
            // reaches. Seed one signal into each such component and walk it too.
            for (int i = 0; i < trackLanes.Length; i++)
            {
                for (int direction = 0; direction < 2; direction++)
                {
                    var lane = new DirectedLane(trackLanes[i], direction == 0);
                    if (!m_Graph.CanTravel(lane) || visited.Contains(lane) || !IsPlainLine(lane, ref scratch, ref scratch2))
                    {
                        continue;
                    }
                    AddSite(lane, atBuffers: false, ref network);
                    Push(lane, 0f, ref stack, ref scratch);
                    RunWalk(ref stack, ref visited, ref network, ref scratch, ref scratch2);
                }
            }

            visited.Dispose();
            stack.Dispose();
        }

        private void RunWalk(ref NativeList<Walk> stack, ref NativeParallelHashSet<DirectedLane> visited, ref SignalNetwork network, ref NativeList<DirectedLane> scratch, ref NativeList<DirectedLane> scratch2)
        {
            while (stack.Length > 0)
            {
                Walk walk = stack[stack.Length - 1];
                stack.RemoveAt(stack.Length - 1);
                if (!visited.Add(walk.m_Lane))
                {
                    continue;
                }
                float distance = walk.m_Distance + m_Graph.GetLength(walk.m_Lane.m_Lane);
                if (network.m_SiteByApproach.ContainsKey(walk.m_Lane))
                {
                    distance = 0f;
                }
                else if (distance >= m_BlockSpacing && IsPlainLine(walk.m_Lane, ref scratch, ref scratch2))
                {
                    AddSite(walk.m_Lane, atBuffers: false, ref network);
                    distance = 0f;
                }
                Push(walk.m_Lane, distance, ref stack, ref scratch);
            }
        }

        private void Push(DirectedLane lane, float distance, ref NativeList<Walk> stack, ref NativeList<DirectedLane> scratch)
        {
            scratch.Clear();
            m_Graph.GetSuccessors(lane, ref scratch);
            for (int i = 0; i < scratch.Length; i++)
            {
                stack.Add(new Walk { m_Lane = scratch[i], m_Distance = distance });
            }
        }

        /// <summary>An edge lane ending on a node with exactly one route through and no other route joining.</summary>
        private bool IsPlainLine(DirectedLane lane, ref NativeList<DirectedLane> scratch, ref NativeList<DirectedLane> scratch2)
        {
            if (!m_Graph.m_EdgeLaneData.HasComponent(lane.m_Lane) || !m_Graph.m_ConnectedEdges.HasBuffer(m_Graph.GetExitOwner(lane)))
            {
                return false;
            }
            if (!m_IntermediateOnBidirectional && m_Graph.m_TrackLaneData.TryGetComponent(lane.m_Lane, out var trackLane) && (trackLane.m_Flags & TrackLaneFlags.Twoway) != 0)
            {
                return false;
            }
            scratch.Clear();
            m_Graph.GetSuccessors(lane, ref scratch);
            if (scratch.Length != 1)
            {
                return false;
            }
            scratch2.Clear();
            m_Graph.GetPredecessors(scratch[0], ref scratch2);
            return scratch2.Length == 1;
        }

        private void AddSite(DirectedLane approach, bool atBuffers, ref SignalNetwork network)
        {
            if (network.m_SiteByApproach.ContainsKey(approach))
            {
                return;
            }
            float setback = atBuffers ? kBufferSetback : m_Setback;
            GetPlacement(approach, setback, out float3 position, out quaternion rotation, out float3 trackPosition, out float3 travel);
            network.m_SiteByApproach.Add(approach, network.m_Sites.Length);
            network.m_Sites.Add(new SignalSiteData
            {
                m_Approach = approach,
                m_Signal = Entity.Null,
                m_Position = position,
                m_Rotation = rotation,
                m_TrackPosition = trackPosition,
                m_Direction = travel,
                m_Gantry = -1,
                m_AtBuffers = atBuffers,
                m_Class = SignalClass.Home,
                m_Speed = SignalSpeed.Normal,
                m_Aspect = SignalAspect.Stop
            });
        }

        /// <summary>
        /// Puts the post beside the track on the driver's side, set back from the boundary, with its
        /// forward axis pointing at the approaching train.
        /// </summary>
        private void GetPlacement(DirectedLane approach, float setbackMetres, out float3 position, out quaternion rotation, out float3 trackPosition, out float3 travel)
        {
            Bezier4x3 bezier = m_Graph.m_CurveData[approach.m_Lane].m_Bezier;
            float length = math.max(1f, m_Graph.GetLength(approach.m_Lane));
            float setback = math.saturate(setbackMetres / length);
            float t = approach.m_Forward ? 1f - setback : setback;

            trackPosition = MathUtils.Position(bezier, t);
            float3 tangent = math.normalizesafe(MathUtils.Tangent(bezier, t), new float3(0f, 0f, 1f));
            travel = approach.m_Forward ? tangent : -tangent;
            float3 right = math.normalizesafe(math.cross(math.up(), travel), new float3(1f, 0f, 0f));

            position = trackPosition + right * (m_LeftHandTraffic ? -m_LateralOffset : m_LateralOffset);
            position.y += m_HeightAdjust;
            rotation = quaternion.LookRotationSafe(-travel, math.up());
        }

        /// <summary>
        /// Drops signals that would stand only a few metres apart. Nodes packed close together,
        /// which is what the vanilla elevated stations are laid out from, put a junction approach on
        /// each one and leave blocks too short to be worth signalling. A block under the minimum is
        /// absorbed into whichever of the two blocks either side of it is itself the shorter, by
        /// dropping the signal that divides the two. Signals at buffer stops are always kept: there
        /// is nothing beyond one for its block to be absorbed into.
        /// </summary>
        private void PruneShortBlocks(ref SignalNetwork network)
        {
            if (m_MinBlockLength <= 0f)
            {
                return;
            }
            for (int pass = 0; pass < kMaxPrunePasses; pass++)
            {
                var ahead = new NativeArray<float>(network.m_Sites.Length, Allocator.Temp);
                var next = new NativeArray<int>(network.m_Sites.Length, Allocator.Temp);
                var behind = new NativeArray<float>(network.m_Sites.Length, Allocator.Temp);
                var drop = new NativeArray<bool>(network.m_Sites.Length, Allocator.Temp);
                MeasureBlocks(ref network, ref ahead, ref next, ref behind);

                bool dropped = false;
                for (int i = 0; i < network.m_Sites.Length; i++)
                {
                    int far = next[i];
                    // One decision per signal per sweep: a signal already dropped has no block of
                    // its own left, and the block of one whose far end went has just grown.
                    if (ahead[i] >= m_MinBlockLength || far < 0 || far == i || drop[i] || drop[far])
                    {
                        continue;
                    }
                    // Nothing lies beyond a buffer signal for its block to be absorbed into, so a
                    // short block ending at one is always folded back into the block behind. The
                    // signal at this end is never itself one: no road leaves a buffer signal, so
                    // its own block is unbounded and never comes up as short.
                    drop[network.m_Sites[far].m_AtBuffers || behind[i] <= ahead[far] ? i : far] = true;
                    dropped = true;
                }

                if (dropped)
                {
                    network.m_SiteByApproach.Clear();
                    int kept = 0;
                    for (int i = 0; i < network.m_Sites.Length; i++)
                    {
                        if (drop[i])
                        {
                            continue;
                        }
                        SignalSiteData site = network.m_Sites[i];
                        network.m_Sites[kept] = site;
                        network.m_SiteByApproach.Add(site.m_Approach, kept);
                        kept++;
                    }
                    network.m_Sites.RemoveRange(kept, network.m_Sites.Length - kept);
                }

                ahead.Dispose();
                next.Dispose();
                behind.Dispose();
                drop.Dispose();
                if (!dropped)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// For every signal, the distance over the shortest road through its block to the first
        /// signal beyond it, which signal that is, and the same measured from the other end. The
        /// setback stands both signals the same distance back from their own boundary and so
        /// cancels, leaving the run of lanes between the two as the length of the block.
        /// </summary>
        private void MeasureBlocks(ref SignalNetwork network, ref NativeArray<float> ahead, ref NativeArray<int> next, ref NativeArray<float> behind)
        {
            var stack = new NativeList<Walk>(64, Allocator.Temp);
            var best = new NativeParallelHashMap<DirectedLane, float>(128, Allocator.Temp);
            var scratch = new NativeList<DirectedLane>(16, Allocator.Temp);

            for (int i = 0; i < network.m_Sites.Length; i++)
            {
                ahead[i] = float.MaxValue;
                behind[i] = float.MaxValue;
                next[i] = -1;
            }

            for (int i = 0; i < network.m_Sites.Length; i++)
            {
                stack.Clear();
                best.Clear();
                Push(network.m_Sites[i].m_Approach, 0f, ref stack, ref scratch);

                int steps = 0;
                while (stack.Length > 0 && steps < kMaxBlockLanes)
                {
                    Walk walk = stack[stack.Length - 1];
                    stack.RemoveAt(stack.Length - 1);
                    float distance = walk.m_Distance + m_Graph.GetLength(walk.m_Lane.m_Lane);
                    // Relaxed rather than visited once: a lane first reached the long way round a
                    // diverging route would otherwise fix the block at that length.
                    if (best.TryGetValue(walk.m_Lane, out float known) && known <= distance)
                    {
                        continue;
                    }
                    best[walk.m_Lane] = distance;
                    steps++;
                    if (network.m_SiteByApproach.TryGetValue(walk.m_Lane, out int site))
                    {
                        if (distance < ahead[i])
                        {
                            ahead[i] = distance;
                            next[i] = site;
                        }
                        behind[site] = math.min(behind[site], distance);
                        continue;
                    }
                    Push(walk.m_Lane, distance, ref stack, ref scratch);
                }
            }

            stack.Dispose();
            best.Dispose();
            scratch.Dispose();
        }

        /// <summary>
        /// Fills in each signal's block: every lane reachable ahead of it before the next signal,
        /// across all diverging routes, plus the signals that terminate those routes.
        /// </summary>
        private void BuildBlocks(ref SignalNetwork network, ref NativeList<DirectedLane> scratch)
        {
            var frontier = new NativeList<DirectedLane>(64, Allocator.Temp);
            var visited = new NativeParallelHashSet<DirectedLane>(128, Allocator.Temp);

            for (int i = 0; i < network.m_Sites.Length; i++)
            {
                int laneStart = network.m_BlockLanes.Length;
                int successorStart = network.m_Successors.Length;
                frontier.Clear();
                visited.Clear();

                m_Graph.GetSuccessors(network.m_Sites[i].m_Approach, ref frontier);

                // The lanes travel enters the block by, kept apart from the block itself: a signal
                // is imposed on these, never on the whole block, or a train already inside it
                // would be stopped along with the one waiting outside.
                int entryStart = network.m_EntryLanes.Length;
                for (int j = 0; j < frontier.Length; j++)
                {
                    network.m_EntryLanes.Add(frontier[j].m_Lane);
                }
                network.m_EntryRanges.Add(new int2(entryStart, network.m_EntryLanes.Length - entryStart));

                while (frontier.Length > 0 && network.m_BlockLanes.Length - laneStart < kMaxBlockLanes)
                {
                    DirectedLane lane = frontier[frontier.Length - 1];
                    frontier.RemoveAt(frontier.Length - 1);
                    if (!visited.Add(lane))
                    {
                        continue;
                    }
                    network.m_BlockLanes.Add(lane);
                    if (network.m_SiteByApproach.TryGetValue(lane, out int successor))
                    {
                        AddUnique(successor, successorStart, ref network.m_Successors);
                        continue;
                    }
                    scratch.Clear();
                    m_Graph.GetSuccessors(lane, ref scratch);
                    for (int j = 0; j < scratch.Length; j++)
                    {
                        frontier.Add(scratch[j]);
                    }
                }

                network.m_BlockRanges.Add(new int2(laneStart, network.m_BlockLanes.Length - laneStart));
                network.m_SuccessorRanges.Add(new int2(successorStart, network.m_Successors.Length - successorStart));
            }

            frontier.Dispose();
            visited.Dispose();
        }

        /// <summary>
        /// Works out what sort of signal each site is now that its block is known. A block with
        /// pointwork, a crossing or a platform in it has to be interlocked, so its signal is a home
        /// signal; plain line between two signals gets an automatic. Medium speed is called for
        /// where the road ahead curves sharply, is posted slow, or is short enough that the geometry
        /// is doing the limiting, which is what junction throats and yards look like.
        /// </summary>
        private void ClassifySignals(ref SignalNetwork network, ref NativeList<DirectedLane> scratch)
        {
            // Priced first for the whole network, because HasRoute below tests membership of this
            // set and would otherwise see it half filled for the sites it reaches early.
            for (int i = 0; i < network.m_BlockLanes.Length; i++)
            {
                Entity lane = network.m_BlockLanes[i].m_Lane;
                if (IsMedium(lane))
                {
                    network.m_MediumLanes.Add(lane);
                }
            }

            for (int i = 0; i < network.m_Sites.Length; i++)
            {
                SignalSiteData site = network.m_Sites[i];
                int2 lanes = network.m_BlockRanges[i];
                bool interlocked = network.m_SuccessorRanges[i].y > 1 || IsPlatform(site.m_Approach.m_Lane);

                for (int j = lanes.x; j < lanes.x + lanes.y; j++)
                {
                    DirectedLane lane = network.m_BlockLanes[j];
                    if (!m_Graph.m_TrackLaneData.HasComponent(lane.m_Lane))
                    {
                        continue;
                    }
                    interlocked |= HasPointwork(lane.m_Lane) || IsPlatform(lane.m_Lane) || HasCrossingOverlap(lane.m_Lane) || IsConverging(lane, ref scratch);
                }

                // A buffer signal has an empty block, so nothing above would mark it interlocked,
                // but it is permanently at danger and must not wear an automatic's plate.
                site.m_Class = interlocked || site.m_AtBuffers ? SignalClass.Home : SignalClass.Automatic;
                site.m_HasClearRoute = HasRoute(ref network, i, mediumAllowed: true);
                site.m_HasNormalRoute = site.m_HasClearRoute && HasRoute(ref network, i, mediumAllowed: false);
                network.m_Sites[i] = site;
            }
        }

        /// <summary>
        /// True when this lane's geometry is what medium speed is for: a sharp curve, slow posted
        /// track, or the cramped pointwork of a junction throat or yard.
        /// </summary>
        private bool IsMedium(Entity lane)
        {
            if (!m_Graph.m_TrackLaneData.TryGetComponent(lane, out var trackLane))
            {
                return false;
            }
            return trackLane.m_Curviness >= m_MediumCurviness
                || trackLane.m_SpeedLimit <= m_MediumSpeedLimit
                || m_Graph.GetLength(lane) <= m_MediumBlockLength;
        }

        /// <summary>
        /// True when travel over this lane runs out of track. The game's own end-of-lane flag says
        /// no other track lane touches this end, which a bare successor count does not: that also
        /// comes back empty for a one-way lane facing the other way and at a boundary with a track
        /// type this mod does not signal, neither of which is a buffer stop.
        /// </summary>
        private bool RunsIntoBuffers(DirectedLane approach, ref NativeList<DirectedLane> scratch)
        {
            if (!m_Graph.m_TrackLaneData.TryGetComponent(approach.m_Lane, out var trackLane))
            {
                return false;
            }
            TrackLaneFlags ending = approach.m_Forward ? TrackLaneFlags.EndingLane : TrackLaneFlags.StartingLane;
            if ((trackLane.m_Flags & ending) == 0)
            {
                return false;
            }
            scratch.Clear();
            m_Graph.GetSuccessors(approach, ref scratch);
            return scratch.Length == 0;
        }

        /// <summary>
        /// Whether any road from this signal reaches another signal, optionally refusing to cross
        /// medium speed track on the way. Reachability is enough because there are only two speeds:
        /// a road that never touches a medium lane is a normal speed road.
        /// </summary>
        private bool HasRoute(ref SignalNetwork network, int siteIndex, bool mediumAllowed)
        {
            var frontier = new NativeList<DirectedLane>(16, Allocator.Temp);
            var visited = new NativeParallelHashSet<DirectedLane>(64, Allocator.Temp);
            var scratch = new NativeList<DirectedLane>(8, Allocator.Temp);
            bool found = false;

            m_Graph.GetSuccessors(network.m_Sites[siteIndex].m_Approach, ref frontier);
            while (frontier.Length > 0 && !found && visited.Count() < kMaxBlockLanes)
            {
                DirectedLane lane = frontier[frontier.Length - 1];
                frontier.RemoveAt(frontier.Length - 1);
                if (!visited.Add(lane) || (!mediumAllowed && network.m_MediumLanes.Contains(lane.m_Lane)))
                {
                    continue;
                }
                if (network.m_SiteByApproach.ContainsKey(lane))
                {
                    found = true;
                    break;
                }
                scratch.Clear();
                m_Graph.GetSuccessors(lane, ref scratch);
                for (int i = 0; i < scratch.Length; i++)
                {
                    frontier.Add(scratch[i]);
                }
            }

            frontier.Dispose();
            visited.Dispose();
            scratch.Dispose();
            return found;
        }

        private bool IsPlatform(Entity lane)
        {
            return m_Graph.m_TrackLaneData.TryGetComponent(lane, out var trackLane) && (trackLane.m_Flags & TrackLaneFlags.Station) != 0;
        }

        /// <summary>True when another route joins this lane, which is trailing points however they are flagged.</summary>
        private bool IsConverging(DirectedLane lane, ref NativeList<DirectedLane> scratch)
        {
            scratch.Clear();
            m_Graph.GetPredecessors(lane, ref scratch);
            return scratch.Length > 1;
        }

        /// <summary>
        /// Groups signals that face the same way and stand abreast of each other onto signal
        /// bridges. Signals over a group are lifted from the lineside to above their own track and
        /// squared up onto the line of the structure, which is what makes a bridge readable: every
        /// head in one row, each one plainly over the track it applies to. A group earns a bridge on
        /// the tracks the structure would stand over, not on how many of them carry one of its
        /// signals. The group is gathered in the seed's own frame, which is the only one the width
        /// of a formation is well defined in; the structure is then squared onto the mean of the
        /// signals that ended up on it.
        /// </summary>
        private void PlanGantries(ref SignalNetwork network)
        {
            if (m_MinGantryTracks <= 0)
            {
                return;
            }
            var group = new NativeList<int>(8, Allocator.Temp);
            var tracks = new NativeList<float3>(8, Allocator.Temp);
            var taken = new NativeArray<bool>(network.m_Sites.Length, Allocator.Temp);

            for (int i = 0; i < network.m_Sites.Length; i++)
            {
                SignalSiteData seed = network.m_Sites[i];
                if (taken[i] || seed.m_AtBuffers)
                {
                    continue;
                }
                group.Clear();
                tracks.Clear();
                group.Add(i);
                taken[i] = true;
                float3 origin = seed.m_TrackPosition;
                float3 axis = math.normalizesafe(math.cross(math.up(), seed.m_Direction), new float3(1f, 0f, 0f));

                // A seed whose own network is wider than a bridge may be gets none, and neither
                // does anything else standing on that network.
                if (AddTracks(seed, origin, axis, ref tracks))
                {
                    CollectAbreast(ref network, origin, axis, ref group, ref tracks, ref taken);
                }

                if (tracks.Length >= m_MinGantryTracks)
                {
                    GetGroupAxis(ref network, group, out float3 direction, out float3 centre, out float3 right);
                    AddGantry(ref network, group, tracks, direction, centre, right);
                }
                else
                {
                    // Left ungrouped, but still not offered to another group: a signal belongs to
                    // at most one bridge, and re-testing it from a neighbour would just rebuild the
                    // same undersized group.
                    for (int j = 1; j < group.Length; j++)
                    {
                        taken[group[j]] = false;
                    }
                }
            }
            group.Dispose();
            tracks.Dispose();
            taken.Dispose();
        }

        /// <summary>Mean line of travel over a group, the point it is centred on, and the axis across it.</summary>
        private void GetGroupAxis(ref SignalNetwork network, NativeList<int> group, out float3 direction, out float3 centre, out float3 right)
        {
            direction = float3.zero;
            centre = float3.zero;
            for (int i = 0; i < group.Length; i++)
            {
                SignalSiteData site = network.m_Sites[group[i]];
                direction += site.m_Direction;
                centre += site.m_TrackPosition;
            }
            direction = math.normalizesafe(direction / group.Length, new float3(0f, 0f, 1f));
            centre /= group.Length;
            right = math.normalizesafe(math.cross(math.up(), direction), new float3(1f, 0f, 0f));
        }

        /// <summary>
        /// Brings every track of one signal's network under the structure, as a point on each one
        /// abreast of the group, and answers whether they fit. A network is spanned whole or not at
        /// all, so a double or quad track laid as one network is counted and spanned in full rather
        /// than as the single track its signal happens to stand on, and the limits decide how many
        /// networks one bridge gathers rather than where it cuts through one: it has to stay inside
        /// the width, and it has to come within the gap of track the structure already stands over
        /// so that a bridge is never thrown across open ground. Tracks on the same alignment are one
        /// road diverging and are held once. Nothing is added when the tracks do not fit, so a
        /// rejected network leaves the group as it was.
        /// </summary>
        private bool AddTracks(SignalSiteData site, float3 origin, float3 axis, ref NativeList<float3> tracks)
        {
            if (!m_Graph.m_OwnerData.TryGetComponent(site.m_Approach.m_Lane, out Owner owner)
                || !m_Graph.m_SubLanes.TryGetBuffer(owner.m_Owner, out var subLanes))
            {
                return false;
            }
            int start = tracks.Length;
            float reachAcross = math.max(30f, m_MaxGantryWidth);
            var line = new Line3.Segment(origin - axis * reachAcross, origin + axis * reachAcross);
            for (int i = 0; i < subLanes.Length; i++)
            {
                Entity lane = subLanes[i].m_SubLane;
                if (!m_Graph.IsSignalledTrack(lane) || !m_Graph.m_CurveData.TryGetComponent(lane, out Curve curve))
                {
                    continue;
                }
                // Taken where the track crosses the line of the structure. Its nearest point to the
                // seed is somewhere else entirely on a road curving away, which puts the track at a
                // width it is nowhere near by the time the structure reaches it.
                MathUtils.Distance(curve.m_Bezier, line, out float2 hit);
                float3 position = MathUtils.Position(curve.m_Bezier, hit.x);
                float across = math.dot(position - origin, axis);
                bool held = false;
                for (int j = 0; j < tracks.Length; j++)
                {
                    held |= math.abs(across - math.dot(tracks[j] - origin, axis)) < m_MinGantryTrackSeparation;
                }
                if (!held)
                {
                    tracks.Add(position);
                }
            }

            // Already spanned in full, so its signal takes a head on the structure whatever the
            // limits say. This is what keeps one network from ending up under two bridges.
            if (tracks.Length == start)
            {
                return true;
            }

            float acrossMin = float.MaxValue;
            float acrossMax = float.MinValue;
            for (int i = 0; i < tracks.Length; i++)
            {
                float across = math.dot(tracks[i] - origin, axis);
                acrossMin = math.min(acrossMin, across);
                acrossMax = math.max(acrossMax, across);
            }

            // How far out the structure has to reach from track it already stands over to take this
            // network in. The seed network stands on its own and has nothing to reach across to.
            float reach = start > 0 ? float.MaxValue : 0f;
            for (int i = start; i < tracks.Length; i++)
            {
                float across = math.dot(tracks[i] - origin, axis);
                for (int j = 0; j < start; j++)
                {
                    reach = math.min(reach, math.abs(across - math.dot(tracks[j] - origin, axis)));
                }
            }

            if (acrossMax - acrossMin > m_MaxGantryWidth || reach > m_MaxGantryTrackGap)
            {
                tracks.RemoveRange(start, tracks.Length - start);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Grows a group outwards network by network, so a wide formation is gathered by reaching
        /// across to whole networks in turn rather than by requiring every track to be near the
        /// first. Every new member is scanned against again, so a network held off for being too
        /// far to reach is taken up later once one between the two has brought the structure out to
        /// it. A signal standing on a network already spanned joins whatever the limits say, which
        /// is what keeps one network from ending up under two bridges.
        /// </summary>
        private void CollectAbreast(ref SignalNetwork network, float3 origin, float3 axis, ref NativeList<int> group, ref NativeList<float3> tracks, ref NativeArray<bool> taken)
        {
            for (int head = 0; head < group.Length; head++)
            {
                SignalSiteData from = network.m_Sites[group[head]];
                for (int i = 0; i < network.m_Sites.Length; i++)
                {
                    if (taken[i])
                    {
                        continue;
                    }
                    SignalSiteData candidate = network.m_Sites[i];
                    if (candidate.m_AtBuffers)
                    {
                        continue;
                    }
                    if (math.dot(from.m_Direction, candidate.m_Direction) < kParallelDot)
                    {
                        continue;
                    }
                    float3 delta = candidate.m_TrackPosition - from.m_TrackPosition;
                    if (math.abs(math.dot(delta, from.m_Direction)) > m_GantryAlignTolerance)
                    {
                        continue;
                    }
                    if (SharesTrackWithGroup(ref network, group, candidate) || !AddTracks(candidate, origin, axis, ref tracks))
                    {
                        continue;
                    }
                    taken[i] = true;
                    group.Add(i);
                }
            }
        }

        /// <summary>
        /// Whether a signal stands on effectively the same alignment as one already in the group.
        /// The heads of two such signals would be hung on top of each other, so the second is left
        /// off the bridge rather than given a slot of its own.
        /// </summary>
        private bool SharesTrackWithGroup(ref SignalNetwork network, NativeList<int> group, SignalSiteData candidate)
        {
            for (int i = 0; i < group.Length; i++)
            {
                SignalSiteData member = network.m_Sites[group[i]];
                float3 delta = candidate.m_TrackPosition - member.m_TrackPosition;
                float across = math.length(delta - member.m_Direction * math.dot(delta, member.m_Direction));
                if (across < m_MinGantryTrackSeparation)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Where the two sides of one signal's network cross the line of the structure, as offsets
        /// across from its centre. False when the network has no built section to measure or lies so
        /// nearly along the structure that it never crosses it.
        /// </summary>
        private bool GetNetworkSides(SignalSiteData site, float3 centre, float3 right, out float2 sides)
        {
            sides = float2.zero;
            if (!m_Graph.m_OwnerData.TryGetComponent(site.m_Approach.m_Lane, out Owner owner)
                || !m_Graph.m_CompositionData.TryGetComponent(owner.m_Owner, out Composition composition)
                || !m_Graph.m_PrefabCompositionData.TryGetComponent(composition.m_Edge, out NetCompositionData built)
                || !m_Graph.m_CurveData.TryGetComponent(owner.m_Owner, out Curve curve))
            {
                return false;
            }
            // Sampled where the edge crosses the structure rather than at its nearest point, which
            // on a road curving away from the group is not the same place at all.
            float span = math.max(30f, m_MaxGantryWidth);
            MathUtils.Distance(curve.m_Bezier, new Line3.Segment(centre - right * span, centre + right * span), out float2 hit);
            float3 point = MathUtils.Position(curve.m_Bezier, hit.x);
            float3 tangent = math.normalizesafe(MathUtils.Tangent(curve.m_Bezier, hit.x), new float3(0f, 0f, 1f));
            float3 edgeRight = math.normalizesafe(math.cross(math.up(), tangent), new float3(1f, 0f, 0f));

            // The structure and the edge are within the parallel test of each other, so they cross
            // at a shallow angle and the sides sit further apart along the structure than the width
            // of the network measured square to its own centreline.
            float lean = math.dot(right, edgeRight);
            if (math.abs(lean) < 0.5f)
            {
                return false;
            }
            float half = built.m_Width * 0.5f;
            float toCentre = math.dot(point - centre, edgeRight) / lean;
            float toSide = half / math.abs(lean);
            float middle = built.m_MiddleOffset / lean;
            sides = new float2(math.min(toCentre - toSide - middle, toCentre + toSide - middle),
                math.max(toCentre - toSide - middle, toCentre + toSide - middle));
            return true;
        }

        private void AddGantry(ref SignalNetwork network, NativeList<int> group, NativeList<float3> tracks, float3 direction, float3 centre, float3 right)
        {
            // Square the structure across the group: one line, at the mean distance along the track.
            float alongCentre = math.dot(centre, direction);
            float acrossMin = float.MaxValue;
            float acrossMax = float.MinValue;
            float railLevel = float.MinValue;
            for (int i = 0; i < tracks.Length; i++)
            {
                float across = math.dot(tracks[i] - centre, right);
                acrossMin = math.min(acrossMin, across);
                acrossMax = math.max(acrossMax, across);
                railLevel = math.max(railLevel, tracks[i].y);
            }

            // Carried out to the edge of every network the structure crosses, where the wiring masts
            // stand. A leg put a fixed distance out from the outermost rail instead has nothing to
            // do with how wide the ground under it is: on one formation it lands out on open ground
            // and on the next it comes down in the four foot of a track the group left out.
            for (int i = 0; i < group.Length; i++)
            {
                if (GetNetworkSides(network.m_Sites[group[i]], centre, right, out float2 sides))
                {
                    acrossMin = math.min(acrossMin, sides.x);
                    acrossMax = math.max(acrossMax, sides.y);
                }
            }

            int gantry = network.m_Gantries.Length;
            float3 position = centre + right * ((acrossMin + acrossMax) * 0.5f);
            position += direction * (alongCentre - math.dot(position, direction));
            position.y = railLevel + m_HeightAdjust;

            network.m_Gantries.Add(new GantryData
            {
                m_Key = network.m_Sites[group[0]].m_Approach,
                m_Position = position,
                m_Rotation = quaternion.LookRotationSafe(-direction, math.up()),
                m_Span = (acrossMax - acrossMin) * 0.5f + m_GantryMargin,
                m_Entity = Entity.Null
            });

            for (int i = 0; i < group.Length; i++)
            {
                SignalSiteData site = network.m_Sites[group[i]];
                // Squared onto the line of the structure but kept over its own track, then stepped
                // to the driver's side so the head clears the overhead wiring on the centreline.
                float3 head = site.m_TrackPosition;
                head += direction * (alongCentre - math.dot(head, direction));
                head += right * (m_LeftHandTraffic ? -m_GantryLateralOffset : m_GantryLateralOffset);
                head.y = railLevel + m_HeightAdjust;
                site.m_Position = head;
                site.m_Rotation = quaternion.LookRotationSafe(-direction, math.up());
                site.m_Gantry = gantry;
                network.m_Sites[group[i]] = site;
            }
        }

        private static void AddUnique(int value, int start, ref NativeList<int> list)
        {
            for (int i = start; i < list.Length; i++)
            {
                if (list[i] == value)
                {
                    return;
                }
            }
            list.Add(value);
        }
    }
}
