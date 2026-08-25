using Colossal.Serialization.Entities;
using Unity.Entities;

namespace RailwaySignals.Signalling
{
    /// <summary>Aspects of a three-position colour light signal.</summary>
    public enum SignalAspect : byte
    {
        /// <summary>Block ahead occupied, or a conflicting movement is set through it.</summary>
        Stop,
        /// <summary>Block ahead clear, next signal at stop.</summary>
        Caution,
        /// <summary>This block and the next are clear.</summary>
        Clear
    }

    /// <summary>Why the placement pass put a signal here. Affects which model is chosen.</summary>
    public enum SignalKind : byte
    {
        /// <summary>Home signal protecting a junction, crossover or diamond crossing.</summary>
        Junction,
        /// <summary>Starting signal at the departure end of a station platform.</summary>
        Starting,
        /// <summary>Automatic signal dividing plain line into blocks.</summary>
        Intermediate
    }

    /// <summary>
    /// Placed on the signal object entity. Identifies the boundary the signal governs so a rebuild
    /// can match surviving entities to recomputed sites, and carries the displayed aspect.
    /// The block behind the aspect is not stored here; it is recomputed from the track network.
    /// </summary>
    public struct RailwaySignal : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Approach lane. The signal stands at this lane's exit end.</summary>
        public Entity m_Lane;

        public bool m_Forward;

        public SignalKind m_Kind;

        public SignalAspect m_Aspect;

        public DirectedLane Approach => new DirectedLane(m_Lane, m_Forward);

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(m_Lane);
            writer.Write(m_Forward);
            writer.Write((byte)m_Kind);
            writer.Write((byte)m_Aspect);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Lane);
            reader.Read(out m_Forward);
            reader.Read(out byte kind);
            reader.Read(out byte aspect);
            m_Kind = (SignalKind)kind;
            m_Aspect = (SignalAspect)aspect;
        }
    }
}
