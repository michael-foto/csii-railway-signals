using Colossal.Mathematics;
using Game.Common;
using Game.Net;
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

        /// <summary>Fewest parallel tracks that warrant a signal bridge. Zero disables them.</summary>
        public int m_MinGantryTracks;

        /// <summary>Widest gap between neighbouring tracks that still counts as the same group, in metres.</summary>
        public float m_MaxGantryTrackSpacing;

        /// <summary>How far apart along the track two signals can be and still share a bridge, in metres.</summary>
        public float m_GantryAlignTolerance;

        /// <summary>Structure width added beyond the outermost track, in metres.</summary>
        public float m_GantryMargin;

        /// <summary>
        /// How far to the driver's side of the track centre a bridge-carried signal hangs, in
        /// metres. The overhead wiring runs down the middle of the track, so a head sitting on the
        /// centreline would foul it.
        /// </summary>
        public float m_GantryLateralOffset;

        /// <summary>
        /// Closest two signals on one bridge may sit across the track, in metres. Approaches to a
        /// junction run nearly parallel a few metres apart, so without this the diverging routes of
        /// one switch each claim a slot and their heads land on top of each other.
        /// </summary>
        public float m_MinGantryTrackSeparation;

        private const int kMaxBlockLanes = 512;

        /// <summary>Tracks must run within about 20 degrees of each other to share a bridge.</summary>
        private const float kParallelDot = 0.94f;


        public void Plan(NativeList<Entity> trackLanes, ref SignalNetwork network)
        {
            network.Clear();

            var scratch = new NativeList<DirectedLane>(16, Allocator.Temp);
            var scratch2 = new NativeList<DirectedLane>(16, Allocator.Temp);

            PlaceFixedSignals(trackLanes, ref network, ref scratch, ref scratch2);
            PlaceIntermediateSignals(trackLanes, ref network, ref scratch, ref scratch2);
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
                    if (IsJunctionApproach(approach, ref scratch, ref scratch2) || IsPlatformExit(approach, ref scratch))
                    {
                        AddSite(approach, ref network);
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
                    AddSite(lane, ref network);
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
                    AddSite(walk.m_Lane, ref network);
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

        private void AddSite(DirectedLane approach, ref SignalNetwork network)
        {
            if (network.m_SiteByApproach.ContainsKey(approach))
            {
                return;
            }
            GetPlacement(approach, out float3 position, out quaternion rotation, out float3 trackPosition, out float3 travel);
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
                m_Class = SignalClass.Home,
                m_Speed = SignalSpeed.Normal,
                m_Aspect = SignalAspect.Stop
            });
        }

        /// <summary>
        /// Puts the post beside the track on the driver's side, set back from the boundary, with its
        /// forward axis pointing at the approaching train.
        /// </summary>
        private void GetPlacement(DirectedLane approach, out float3 position, out quaternion rotation, out float3 trackPosition, out float3 travel)
        {
            Bezier4x3 bezier = m_Graph.m_CurveData[approach.m_Lane].m_Bezier;
            float length = math.max(1f, m_Graph.GetLength(approach.m_Lane));
            float setback = math.saturate(m_Setback / length);
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
            for (int i = 0; i < network.m_Sites.Length; i++)
            {
                SignalSiteData site = network.m_Sites[i];
                int2 lanes = network.m_BlockRanges[i];
                bool interlocked = network.m_SuccessorRanges[i].y > 1 || IsPlatform(site.m_Approach.m_Lane);
                float length = 0f;
                float curviness = 0f;
                float speedLimit = float.MaxValue;

                for (int j = lanes.x; j < lanes.x + lanes.y; j++)
                {
                    DirectedLane lane = network.m_BlockLanes[j];
                    if (!m_Graph.m_TrackLaneData.TryGetComponent(lane.m_Lane, out var trackLane))
                    {
                        continue;
                    }
                    length += m_Graph.GetLength(lane.m_Lane);
                    curviness = math.max(curviness, trackLane.m_Curviness);
                    speedLimit = math.min(speedLimit, trackLane.m_SpeedLimit);
                    interlocked |= HasPointwork(lane.m_Lane) || IsPlatform(lane.m_Lane) || HasCrossingOverlap(lane.m_Lane) || IsConverging(lane, ref scratch);
                }

                site.m_Class = interlocked ? SignalClass.Home : SignalClass.Automatic;
                site.m_Speed = (lanes.y == 0 || curviness >= m_MediumCurviness || speedLimit <= m_MediumSpeedLimit || length <= m_MediumBlockLength)
                    ? SignalSpeed.Medium
                    : SignalSpeed.Normal;
                network.m_Sites[i] = site;
            }

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
        /// head in one row, each one plainly over the track it applies to.
        /// </summary>
        private void PlanGantries(ref SignalNetwork network)
        {
            if (m_MinGantryTracks <= 0 || network.m_Sites.Length < m_MinGantryTracks)
            {
                return;
            }
            var group = new NativeList<int>(8, Allocator.Temp);
            var taken = new NativeArray<bool>(network.m_Sites.Length, Allocator.Temp);

            for (int i = 0; i < network.m_Sites.Length; i++)
            {
                if (taken[i])
                {
                    continue;
                }
                group.Clear();
                group.Add(i);
                taken[i] = true;
                CollectAbreast(ref network, i, ref group, ref taken);

                if (group.Length >= m_MinGantryTracks)
                {
                    AddGantry(ref network, group);
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
            taken.Dispose();
        }

        /// <summary>
        /// Grows a group outwards one track at a time, so a wide formation is gathered by stepping
        /// across neighbouring tracks rather than by requiring every track to be near the first.
        /// </summary>
        private void CollectAbreast(ref SignalNetwork network, int seed, ref NativeList<int> group, ref NativeArray<bool> taken)
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
                    if (math.dot(from.m_Direction, candidate.m_Direction) < kParallelDot)
                    {
                        continue;
                    }
                    float3 delta = candidate.m_TrackPosition - from.m_TrackPosition;
                    float along = math.dot(delta, from.m_Direction);
                    if (math.abs(along) > m_GantryAlignTolerance)
                    {
                        continue;
                    }
                    float across = math.length(delta - from.m_Direction * along);
                    if (across > m_MaxGantryTrackSpacing)
                    {
                        continue;
                    }
                    if (SharesTrackWithGroup(ref network, group, candidate))
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

        private void AddGantry(ref SignalNetwork network, NativeList<int> group)
        {
            float3 direction = float3.zero;
            float3 centre = float3.zero;
            for (int i = 0; i < group.Length; i++)
            {
                SignalSiteData site = network.m_Sites[group[i]];
                direction += site.m_Direction;
                centre += site.m_TrackPosition;
            }
            direction = math.normalizesafe(direction / group.Length, new float3(0f, 0f, 1f));
            centre /= group.Length;
            float3 right = math.normalizesafe(math.cross(math.up(), direction), new float3(1f, 0f, 0f));

            // Square the structure across the group: one line, at the mean distance along the track.
            float alongCentre = math.dot(centre, direction);
            float acrossMin = float.MaxValue;
            float acrossMax = float.MinValue;
            float railLevel = float.MinValue;
            for (int i = 0; i < group.Length; i++)
            {
                float3 trackPosition = network.m_Sites[group[i]].m_TrackPosition;
                float across = math.dot(trackPosition - centre, right);
                acrossMin = math.min(acrossMin, across);
                acrossMax = math.max(acrossMax, across);
                railLevel = math.max(railLevel, trackPosition.y);
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
