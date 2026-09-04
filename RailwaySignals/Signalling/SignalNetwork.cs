using System;
using System.Security.Policy;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RailwaySignals.Signalling
{
    /// <summary>One signal position and the state it is displaying.</summary>
    public struct SignalSiteData : IPositionable
    {
        /// <summary>The signal governs the boundary at this lane's exit end.</summary>
        public DirectedLane m_Approach;

        public SignalClass m_Class;

        /// <summary>Speed the signal is admitting a train at, resolved each tick from the set route.</summary>
        public SignalSpeed m_Speed;

        /// <summary>
        /// Some route through the block reaches another signal. False means the block only runs into
        /// buffers, which a signal can never clear for.
        /// </summary>
        public bool m_HasClearRoute;

        /// <summary>
        /// Stands at the stop blocks at the end of the track. Nothing lies beyond it, so it is a
        /// home signal permanently at danger and the signal behind it warns for it.
        /// </summary>
        public bool m_AtBuffers;

        /// <summary>
        /// Some route through the block reaches another signal without crossing a medium speed lane.
        /// Read when no route is set and the choice has to be made without knowing which way a train
        /// will go, where the least restrictive road is the one to show.
        /// </summary>
        public bool m_HasNormalRoute;

        /// <summary>On a lineside signal, this carries the post frame. On a gantry, this holds the signal gantry cage.</summary>
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

        public float3 Position => m_Position;

        public quaternion Rotation => m_Rotation;
    }
    /// <summary>
    /// A signal bridge carrying the heads for a group of parallel tracks. One object entity spans
    /// the group: the game tiles the beam mesh between the leg meshes to fill <see cref="m_Span"/>.
    /// </summary>
    public struct GantryData : IPositionable
    {
        public float3 m_Position;

        public quaternion m_Rotation;

        /// <summary>Approach of the first member, used as a stable identity across rebuilds.</summary>
        public DirectedLane m_Key;

        /// <summary>Half width of the structure, measured out from the position along its own X axis.</summary>
        public float m_Span;

        public Entity m_Entity;

        public float3 Position => m_Position;

        public quaternion Rotation => m_Rotation;
    }

    public interface IPositionable
    {
        public float3 Position { get; }
        public quaternion Rotation { get; }
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

        /// <summary>
        /// Lanes travel enters each block by, indexed through <see cref="m_EntryRanges"/>. These are
        /// the lanes a signal stands in front of, as opposed to the whole block behind it.
        /// </summary>
        public NativeList<Entity> m_EntryLanes;

        public NativeList<int2> m_EntryRanges;

        /// <summary>Site indices of the signals ending each block, through <see cref="m_SuccessorRanges"/>.</summary>
        public NativeList<int> m_Successors;

        public NativeList<int2> m_SuccessorRanges;

        /// <summary>
        /// Lanes whose geometry calls for medium speed. Worked out once per plan so the aspect pass
        /// can price a route by membership rather than re-deriving the criteria.
        /// </summary>
        public NativeParallelHashSet<Entity> m_MediumLanes;

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
                m_EntryLanes = new NativeList<Entity>(512, allocator),
                m_EntryRanges = new NativeList<int2>(256, allocator),
                m_Successors = new NativeList<int>(512, allocator),
                m_SuccessorRanges = new NativeList<int2>(256, allocator),
                m_SiteByApproach = new NativeParallelHashMap<DirectedLane, int>(256, allocator),
                m_MediumLanes = new NativeParallelHashSet<Entity>(512, allocator),
                m_Gantries = new NativeList<GantryData>(32, allocator),
                m_IsCreated = true
            };
        }

        public void Clear()
        {
            m_Sites.Clear();
            m_BlockLanes.Clear();
            m_BlockRanges.Clear();
            m_EntryLanes.Clear();
            m_EntryRanges.Clear();
            m_Successors.Clear();
            m_SuccessorRanges.Clear();
            m_SiteByApproach.Clear();
            m_MediumLanes.Clear();
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
            m_EntryLanes.Dispose();
            m_EntryRanges.Dispose();
            m_Successors.Dispose();
            m_SuccessorRanges.Dispose();
            m_SiteByApproach.Dispose();
            m_MediumLanes.Dispose();
            m_Gantries.Dispose();
            m_IsCreated = false;
        }
    }
}
