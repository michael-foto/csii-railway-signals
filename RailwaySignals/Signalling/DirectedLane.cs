using System;
using Unity.Entities;

namespace RailwaySignals.Signalling
{
    /// <summary>
    /// A track lane together with the direction of travel over it. <see cref="m_Forward"/> means
    /// travel runs along the lane curve from t=0 to t=1, which is also the lane's
    /// <c>Lane.m_StartNode</c> to <c>Lane.m_EndNode</c> direction.
    /// </summary>
    public struct DirectedLane : IEquatable<DirectedLane>
    {
        public Entity m_Lane;

        public bool m_Forward;

        public DirectedLane(Entity lane, bool forward)
        {
            m_Lane = lane;
            m_Forward = forward;
        }

        public static DirectedLane Null => new DirectedLane(Entity.Null, forward: true);

        public bool IsNull => m_Lane == Entity.Null;

        /// <summary>Curve parameter at which travel enters the lane.</summary>
        public float EntryPosition => m_Forward ? 0f : 1f;

        /// <summary>Curve parameter at which travel leaves the lane.</summary>
        public float ExitPosition => m_Forward ? 1f : 0f;

        public DirectedLane Reversed => new DirectedLane(m_Lane, !m_Forward);

        public bool Equals(DirectedLane other)
        {
            return m_Lane == other.m_Lane && m_Forward == other.m_Forward;
        }

        public override bool Equals(object obj)
        {
            return obj is DirectedLane other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (m_Lane.GetHashCode() << 1) | (m_Forward ? 1 : 0);
        }

        public override string ToString()
        {
            return $"{m_Lane.Index}:{(m_Forward ? '+' : '-')}";
        }
    }
}
