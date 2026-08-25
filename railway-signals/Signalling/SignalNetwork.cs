using System;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RailwaySignals.Signalling
{
    /// <summary>One signal position and the state it is displaying.</summary>
    public struct SignalSiteData
    {
        /// <summary>The signal governs the boundary at this lane's exit end.</summary>
        public DirectedLane m_Approach;

        public SignalKind m_Kind;

        /// <summary>Net entity the placed object hangs off, so it dies with the track.</summary>
        public Entity m_Owner;

        /// <summary>Placed object entity, or Null while the site has no object yet.</summary>
        public Entity m_Signal;

        public float3 m_Position;

        public quaternion m_Rotation;

        public SignalAspect m_Aspect;

        /// <summary>Set during the aspect pass when something stands in or is claiming the block.</summary>
        public bool m_Blocked;
    }

    /// <summary>
    /// The signalling plan for the whole track network: where signals stand, which lanes make up
    /// the block each one protects, and which signals sit at the far end of that block.
    /// Rebuilt from scratch whenever the track network changes.
    /// </summary>
    public struct SignalNetwork : IDisposable
    {
        public NativeList<SignalSiteData> m_Sites;

        /// <summary>Lanes of every block, indexed through <see cref="m_BlockRanges"/>.</summary>
        public NativeList<Entity> m_BlockLanes;

        public NativeList<int2> m_BlockRanges;

        /// <summary>Site indices of the signals ending each block, through <see cref="m_SuccessorRanges"/>.</summary>
        public NativeList<int> m_Successors;

        public NativeList<int2> m_SuccessorRanges;

        /// <summary>Maps an approach lane to the site standing at its exit.</summary>
        public NativeParallelHashMap<DirectedLane, int> m_SiteByApproach;

        public bool m_IsCreated;

        public static SignalNetwork Create(Allocator allocator)
        {
            return new SignalNetwork
            {
                m_Sites = new NativeList<SignalSiteData>(256, allocator),
                m_BlockLanes = new NativeList<Entity>(2048, allocator),
                m_BlockRanges = new NativeList<int2>(256, allocator),
                m_Successors = new NativeList<int>(512, allocator),
                m_SuccessorRanges = new NativeList<int2>(256, allocator),
                m_SiteByApproach = new NativeParallelHashMap<DirectedLane, int>(256, allocator),
                m_IsCreated = true
            };
        }

        public void Clear()
        {
            m_Sites.Clear();
            m_BlockLanes.Clear();
            m_BlockRanges.Clear();
            m_Successors.Clear();
            m_SuccessorRanges.Clear();
            m_SiteByApproach.Clear();
        }

        public void Dispose()
        {
            if (!m_IsCreated)
            {
                return;
            }
            m_Sites.Dispose();
            m_BlockLanes.Dispose();
            m_BlockRanges.Dispose();
            m_Successors.Dispose();
            m_SuccessorRanges.Dispose();
            m_SiteByApproach.Dispose();
            m_IsCreated = false;
        }
    }
}
