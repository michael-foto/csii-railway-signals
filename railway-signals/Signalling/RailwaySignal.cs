using Colossal.Serialization.Entities;
using Unity.Entities;

namespace RailwaySignals.Signalling
{
    /// <summary>Indications a three-position colour light signal can display.</summary>
    public enum SignalAspect : byte
    {
        /// <summary>Block ahead occupied, or a conflicting movement is set through it.</summary>
        Stop,
        /// <summary>Block ahead clear, next signal at stop.</summary>
        Caution,
        /// <summary>Road clear, but the next signal admits a medium speed movement only.</summary>
        ReduceToMedium,
        /// <summary>This block and the next are clear at normal speed.</summary>
        Clear
    }

    /// <summary>
    /// Whether a signal guards a movement that needs interlocking. Home signals stand at platforms
    /// and at anything with pointwork or a crossing beyond them; automatic signals only divide
    /// plain line into blocks and carry an "A" plate on the prototype.
    /// </summary>
    public enum SignalClass : byte
    {
        Home,
        Automatic
    }

    /// <summary>Speed a signal admits a train into its block at.</summary>
    public enum SignalSpeed : byte
    {
        Normal,
        /// <summary>Tight curves, slow track, or the cramped geometry of a junction or yard.</summary>
        Medium
    }

    /// <summary>Why the placement pass put a signal here. Lets asset variants differ by role.</summary>
    public enum SignalKind : byte
    {
        /// <summary>Protecting a junction, crossover or diamond crossing.</summary>
        Junction,
        /// <summary>At the departure end of a station platform.</summary>
        Starting,
        /// <summary>Dividing plain line into blocks.</summary>
        Intermediate
    }

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

        public SignalKind m_Kind;

        public SignalClass m_Class;

        public SignalSpeed m_Speed;

        public SignalAspect m_Aspect;

        public DirectedLane Approach => new DirectedLane(m_Lane, m_Forward);

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(m_Lane);
            writer.Write(m_Forward);
            writer.Write((byte)m_Kind);
            writer.Write((byte)m_Class);
            writer.Write((byte)m_Speed);
            writer.Write((byte)m_Aspect);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Lane);
            reader.Read(out m_Forward);
            reader.Read(out byte kind);
            reader.Read(out byte signalClass);
            reader.Read(out byte speed);
            reader.Read(out byte aspect);
            m_Kind = (SignalKind)kind;
            m_Class = (SignalClass)signalClass;
            m_Speed = (SignalSpeed)speed;
            m_Aspect = (SignalAspect)aspect;
        }
    }
}
