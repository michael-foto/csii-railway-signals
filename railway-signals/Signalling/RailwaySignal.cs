using Colossal.Serialization.Entities;
using Unity.Entities;

namespace RailwaySignals.Signalling
{
    /// <summary>
    /// Indications of a two-head speed signal. The top head carries the normal speed indications
    /// and the bottom head the medium speed ones, so the aspect says both how far the road is clear
    /// and at what speed it may be taken.
    /// </summary>
    public enum SignalAspect : byte
    {
        /// <summary>Block ahead occupied, or a conflicting movement is set through it.</summary>
        Stop,
        /// <summary>Block ahead clear at normal speed, next signal at stop.</summary>
        Caution,
        /// <summary>Block ahead clear at medium speed, next signal at stop.</summary>
        MediumCaution,
        /// <summary>Road clear, but the next signal admits a medium speed movement only.</summary>
        ReduceToMedium,
        /// <summary>Block ahead clear at medium speed, next signal off stop.</summary>
        MediumClear,
        /// <summary>This block and the next are clear at normal speed.</summary>
        Clear
    }

    /// <summary>
    /// The separate objects a signal is assembled from. Heads are modelled without a mast so the
    /// same ones serve on a lineside post and hung from a bridge. Every signal has two heads: the
    /// upper one is always a home lamp, and the lower one can be a home or automatic lamp (offset)
    /// </summary>
    public enum SignalAsset : byte
    {
        /// <summary>The post a lineside signal stands on. Not used on a bridge.</summary>
        Mast,
        /// <summary>Plain lamp head, with no "A" plate.</summary>
        HomeHead,
        /// <summary>Lamp head carrying an "A" plate, used as the lower head of an automatic.</summary>
        AutomaticHead,
        /// <summary>The bridge spanning a group of parallel tracks.</summary>
        Gantry,
        /// <summary>The catwalk cage and frame that attaches signal heads to a gantry</summary>
        GantryCage
    }

    /// <summary>Which piece of the assembly an object is.</summary>
    public enum SignalPartKind : byte
    {
        Mast,
        TopHead,
        BottomHead,
        Gantry,
        GantryCage
    }

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
    public enum SignalLamp : byte
    {
        None,
        Red,
        Yellow,
        Green
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
    public static class SignalAspectExtensions
    {
        /// <summary>The normal speed head, at the top of the mast.</summary>
        public static SignalLamp TopLamp(this SignalAspect aspect)
        {
            return aspect switch
            {
                SignalAspect.Caution or SignalAspect.ReduceToMedium => SignalLamp.Yellow,
                SignalAspect.Clear => SignalLamp.Green,
                _ => SignalLamp.Red,
            };
        }

        /// <summary>The medium speed head, below the top one.</summary>
        public static SignalLamp BottomLamp(this SignalAspect aspect)
        {
            return aspect switch
            {
                SignalAspect.MediumCaution => SignalLamp.Yellow,
                SignalAspect.MediumClear or SignalAspect.ReduceToMedium => SignalLamp.Green,
                _ => SignalLamp.Red,
            };
        }
    }
}
