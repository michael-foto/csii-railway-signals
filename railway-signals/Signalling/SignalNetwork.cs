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

        public SignalClass m_Class;

        public SignalSpeed m_Speed;

        /// <summary>The post, on a lineside signal. Null on one carried by a bridge.</summary>
        public Entity m_Mast;

        /// <summary>Placed object entity for the top head, or Null while the site has no object yet.</summary>
        public Entity m_Signal;

        /// <summary>Placed object entity for the medium speed head, Null on a single headed signal.</summary>
        public Entity m_BottomHead;

        public float3 m_Position;

        public quaternion m_Rotation;

        /// <summary>Centre of the track at the signal, before the post is offset to the side.</summary>
        public float3 m_TrackPosition;

        /// <summary>Unit vector along the direction of travel past the signal.</summary>
        public float3 m_Direction;

        /// <summary>Index into <see cref="SignalNetwork.m_Gantries"/>, or -1 for a line-side post.</summary>
        public int m_Gantry;

        public SignalAspect m_Aspect;

        /// <summary>Set during the aspect pass when something stands in or is claiming the block.</summary>
        public bool m_Blocked;
    }

    /// <summary>
    /// A signal bridge carrying the heads for a group of parallel tracks. One object entity spans
    /// the group: the game tiles the beam mesh between the leg meshes to fill <see cref="m_Span"/>.
    /// </summary>
    public struct GantryData
    {
        public float3 m_Position;

        public quaternion m_Rotation;

        /// <summary>Approach of the first member, used as a stable identity across rebuilds.</summary>
        public DirectedLane m_Key;

        /// <summary>Half width of the structure, measured out from the position along its own X axis.</summary>
        public float m_Span;

        public Entity m_Entity;
    }

    /// <summary>
    /// The signalling plan for the whole track network: where signals stand, which lanes make up
    /// the block each one protects, and which signals sit at the far end of that block.
    /// Rebuilt from scratch whenever the track network changes.
    /// </summary>
    public struct SignalNetwork : IDisposable
    {
        public NativeList<SignalSiteData> m_Sites;

        /// <summary>Lanes of every block with the direction travel takes over them, indexed through <see cref="m_BlockRanges"/>.</summary>
        public NativeList<DirectedLane> m_BlockLanes;

        public NativeList<int2> m_BlockRanges;

        /// <summary>Site indices of the signals ending each block, through <see cref="m_SuccessorRanges"/>.</summary>
        public NativeList<int> m_Successors;

        public NativeList<int2> m_SuccessorRanges;

        /// <summary>Maps an approach lane to the site standing at its exit.</summary>
        public NativeParallelHashMap<DirectedLane, int> m_SiteByApproach;

        public NativeList<GantryData> m_Gantries;

        public bool m_IsCreated;

        public static SignalNetwork Create(Allocator allocator)
        {
            return new SignalNetwork
            {
                m_Sites = new NativeList<SignalSiteData>(256, allocator),
                m_BlockLanes = new NativeList<DirectedLane>(2048, allocator),
                m_BlockRanges = new NativeList<int2>(256, allocator),
                m_Successors = new NativeList<int>(512, allocator),
                m_SuccessorRanges = new NativeList<int2>(256, allocator),
                m_SiteByApproach = new NativeParallelHashMap<DirectedLane, int>(256, allocator),
                m_Gantries = new NativeList<GantryData>(32, allocator),
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
            m_Gantries.Clear();
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
            m_Gantries.Dispose();
            m_IsCreated = false;
        }
    }
}
