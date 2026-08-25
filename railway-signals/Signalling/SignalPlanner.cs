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

        /// <summary>Distance from track centre to the signal post, in metres.</summary>
        public float m_LateralOffset;

        public bool m_LeftHandTraffic;

        private const int kMaxBlockLanes = 512;

        public void Plan(NativeList<Entity> trackLanes, ref SignalNetwork network)
        {
            network.Clear();

            var scratch = new NativeList<DirectedLane>(16, Allocator.Temp);
            var scratch2 = new NativeList<DirectedLane>(16, Allocator.Temp);

            PlaceFixedSignals(trackLanes, ref network, ref scratch, ref scratch2);
            PlaceIntermediateSignals(trackLanes, ref network, ref scratch, ref scratch2);
            BuildBlocks(ref network, ref scratch);

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
                    if (IsJunctionApproach(approach, ref scratch, ref scratch2))
                    {
                        AddSite(approach, SignalKind.Junction, ref network);
                    }
                    else if (IsPlatformExit(approach, ref scratch))
                    {
                        AddSite(approach, SignalKind.Starting, ref network);
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
                    AddSite(lane, SignalKind.Intermediate, ref network);
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
                    AddSite(walk.m_Lane, SignalKind.Intermediate, ref network);
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

        private void AddSite(DirectedLane approach, SignalKind kind, ref SignalNetwork network)
        {
            if (network.m_SiteByApproach.ContainsKey(approach))
            {
                return;
            }
            GetPlacement(approach, out float3 position, out quaternion rotation);
            network.m_SiteByApproach.Add(approach, network.m_Sites.Length);
            network.m_Sites.Add(new SignalSiteData
            {
                m_Approach = approach,
                m_Kind = kind,
                m_Owner = m_Graph.m_OwnerData.TryGetComponent(approach.m_Lane, out var owner) ? owner.m_Owner : Entity.Null,
                m_Signal = Entity.Null,
                m_Position = position,
                m_Rotation = rotation,
                m_Aspect = SignalAspect.Stop
            });
        }

        /// <summary>
        /// Puts the post beside the track on the driver's side, set back from the boundary, with its
        /// forward axis pointing at the approaching train.
        /// </summary>
        private void GetPlacement(DirectedLane approach, out float3 position, out quaternion rotation)
        {
            Bezier4x3 bezier = m_Graph.m_CurveData[approach.m_Lane].m_Bezier;
            float length = math.max(1f, m_Graph.GetLength(approach.m_Lane));
            float setback = math.saturate(m_Setback / length);
            float t = approach.m_Forward ? 1f - setback : setback;

            position = MathUtils.Position(bezier, t);
            float3 tangent = math.normalizesafe(MathUtils.Tangent(bezier, t), new float3(0f, 0f, 1f));
            float3 travel = approach.m_Forward ? tangent : -tangent;
            float3 right = math.normalizesafe(math.cross(math.up(), travel), new float3(1f, 0f, 0f));

            position += right * (m_LeftHandTraffic ? -m_LateralOffset : m_LateralOffset);
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
                    network.m_BlockLanes.Add(lane.m_Lane);
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
