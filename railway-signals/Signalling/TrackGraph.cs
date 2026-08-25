using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RailwaySignals.Signalling
{
    /// <summary>
    /// Directed traversal of the track lane graph. Lanes belong either to an edge
    /// (<see cref="EdgeLane"/>) or to a node, and connect where one lane's exit
    /// <c>PathNode</c> equals the next lane's entry <c>PathNode</c>. This mirrors
    /// <c>NetUtils.FindConnectedLane</c>, but enumerates every branch rather than the first.
    /// </summary>
    public struct TrackGraph
    {
        public ComponentLookup<Game.Net.Lane> m_LaneData;

        public ComponentLookup<Game.Net.TrackLane> m_TrackLaneData;

        public ComponentLookup<Game.Net.EdgeLane> m_EdgeLaneData;

        public ComponentLookup<Owner> m_OwnerData;

        public ComponentLookup<Game.Net.Edge> m_EdgeData;

        public ComponentLookup<Game.Net.Curve> m_CurveData;

        public ComponentLookup<PrefabRef> m_PrefabRefData;

        public ComponentLookup<TrackLaneData> m_PrefabTrackLaneData;

        public BufferLookup<Game.Net.ConnectedEdge> m_ConnectedEdges;

        public BufferLookup<Game.Net.SubLane> m_SubLanes;

        public TrackTypes m_TrackTypes;

        /// <summary>True when the lane is a track lane of a type this mod signals.</summary>
        public bool IsSignalledTrack(Entity lane)
        {
            if (!m_TrackLaneData.HasComponent(lane) || !m_PrefabRefData.TryGetComponent(lane, out var prefabRef))
            {
                return false;
            }
            return m_PrefabTrackLaneData.TryGetComponent(prefabRef.m_Prefab, out var trackLaneData)
                && (trackLaneData.m_TrackTypes & m_TrackTypes) != 0;
        }

        /// <summary>
        /// True when the lane may be run over in the given direction. One-way lanes only run
        /// from start node to end node; <see cref="TrackLaneFlags.Twoway"/> lanes run either way.
        /// </summary>
        public bool CanTravel(DirectedLane lane)
        {
            if (!m_TrackLaneData.TryGetComponent(lane.m_Lane, out var trackLane))
            {
                return false;
            }
            return lane.m_Forward || (trackLane.m_Flags & TrackLaneFlags.Twoway) != 0;
        }

        public float GetLength(Entity lane)
        {
            return m_CurveData.TryGetComponent(lane, out var curve) ? curve.m_Length : 0f;
        }

        public float3 GetPosition(DirectedLane lane, float curvePosition)
        {
            return MathUtils.Position(m_CurveData[lane.m_Lane].m_Bezier, curvePosition);
        }

        /// <summary>
        /// The net entity travel passes through when leaving <paramref name="lane"/>: the node at
        /// the end of an edge lane, the edge itself for a lane ending mid-edge, or the owning node
        /// for a node connector lane.
        /// </summary>
        public Entity GetExitOwner(DirectedLane lane)
        {
            if (!m_OwnerData.TryGetComponent(lane.m_Lane, out var owner))
            {
                return Entity.Null;
            }
            if (!m_EdgeLaneData.TryGetComponent(lane.m_Lane, out var edgeLane))
            {
                return owner.m_Owner;
            }
            float edgeDelta = lane.m_Forward ? edgeLane.m_EdgeDelta.y : edgeLane.m_EdgeDelta.x;
            if (edgeDelta == 0f)
            {
                return m_EdgeData[owner.m_Owner].m_Start;
            }
            if (edgeDelta == 1f)
            {
                return m_EdgeData[owner.m_Owner].m_End;
            }
            return owner.m_Owner;
        }

        /// <summary>Appends every track lane travel can continue onto after <paramref name="lane"/>.</summary>
        public void GetSuccessors(DirectedLane lane, ref NativeList<DirectedLane> results)
        {
            Collect(lane, exit: true, ref results);
        }

        /// <summary>Appends every track lane travel can arrive from before <paramref name="lane"/>.</summary>
        public void GetPredecessors(DirectedLane lane, ref NativeList<DirectedLane> results)
        {
            Collect(lane, exit: false, ref results);
        }

        private void Collect(DirectedLane lane, bool exit, ref NativeList<DirectedLane> results)
        {
            if (!m_LaneData.TryGetComponent(lane.m_Lane, out var laneData) || !m_OwnerData.TryGetComponent(lane.m_Lane, out var owner))
            {
                return;
            }
            PathNode boundary = (lane.m_Forward == exit) ? laneData.m_EndNode : laneData.m_StartNode;
            Entity searchOwner = GetExitOwner(exit ? lane : lane.Reversed);

            CollectFrom(searchOwner, lane.m_Lane, boundary, exit, ref results);

            // A node connector lane, or an edge lane ending on a node, continues into the lanes of
            // the edges meeting there. A lane ending mid-edge has no ConnectedEdge buffer.
            if (m_ConnectedEdges.TryGetBuffer(searchOwner, out var connectedEdges))
            {
                for (int i = 0; i < connectedEdges.Length; i++)
                {
                    Entity edge = connectedEdges[i].m_Edge;
                    if (edge != owner.m_Owner)
                    {
                        CollectFrom(edge, lane.m_Lane, boundary, exit, ref results);
                    }
                }
            }
        }

        private void CollectFrom(Entity netEntity, Entity excludeLane, PathNode boundary, bool exit, ref NativeList<DirectedLane> results)
        {
            if (!m_SubLanes.TryGetBuffer(netEntity, out var subLanes))
            {
                return;
            }
            for (int i = 0; i < subLanes.Length; i++)
            {
                Entity subLane = subLanes[i].m_SubLane;
                if (subLane == excludeLane || !IsSignalledTrack(subLane))
                {
                    continue;
                }
                Game.Net.Lane candidate = m_LaneData[subLane];
                // Travelling forward out of the boundary means entering the neighbour at its start
                // node; travelling backwards means leaving it at its end node.
                if (candidate.m_StartNode.Equals(boundary))
                {
                    Add(new DirectedLane(subLane, exit), ref results);
                }
                else if (candidate.m_EndNode.Equals(boundary))
                {
                    Add(new DirectedLane(subLane, !exit), ref results);
                }
            }
        }

        private void Add(DirectedLane lane, ref NativeList<DirectedLane> results)
        {
            if (!CanTravel(lane))
            {
                return;
            }
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].Equals(lane))
                {
                    return;
                }
            }
            results.Add(lane);
        }
    }
}
