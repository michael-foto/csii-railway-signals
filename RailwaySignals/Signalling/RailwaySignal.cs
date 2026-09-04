using Colossal.Serialization.Entities;
using Unity.Entities;

namespace RailwaySignals.Signalling
{
    /// <summary>
    /// Identifies every object this mod puts up. A rebuild matches these against the new plan so
    /// that a signal which has not moved keeps its entity, rather than being destroyed and made
    /// again: re-creating an object costs it its culling index and mesh batches, and doing that to
    /// the whole network on every track edit leaves objects unrendered.
    /// </summary>
    public struct RailwaySignalPart : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Approach lane of the signal this belongs to, or of a bridge's first member.</summary>
        public Entity m_Lane;

        public bool m_Forward;

        public SignalPartKind m_Kind;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(m_Lane);
            writer.Write(m_Forward);
            writer.Write((byte)m_Kind);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Lane);
            reader.Read(out m_Forward);
            reader.Read(out byte kind);
            m_Kind = (SignalPartKind)kind;
        }
    }

    /// <summary>One lamp of a signal head.</summary>
    /// <summary>Speed a signal admits a train into its block at.</summary>
    /// <summary>
    /// Placed on the signal object entity. Identifies the boundary the signal governs so a rebuild
    /// can match surviving entities to recomputed sites, and carries what it is displaying.
    /// The block behind the aspect is not stored here; it is recomputed from the track network.
    /// </summary>
    public struct RailwaySignal : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Approach lane. The signal stands at this lane's exit end.</summary>
        public Entity m_Lane;

        public bool m_Forward;

        public SignalClass m_Class;

        public SignalSpeed m_Speed;

        public SignalAspect m_Aspect;

        public DirectedLane Approach => new DirectedLane(m_Lane, m_Forward);

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(m_Lane);
            writer.Write(m_Forward);
            writer.Write((byte)m_Class);
            writer.Write((byte)m_Speed);
            writer.Write((byte)m_Aspect);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Lane);
            reader.Read(out m_Forward);
            reader.Read(out byte signalClass);
            reader.Read(out byte speed);
            reader.Read(out byte aspect);
            m_Class = (SignalClass)signalClass;
            m_Speed = (SignalSpeed)speed;
            m_Aspect = (SignalAspect)aspect;
        }
    }

    /// <summary>What each head of a signal is showing, and how that reaches the lamps.</summary>
}
