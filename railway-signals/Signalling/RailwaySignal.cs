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
    /// same ones serve on a lineside post and hung from a bridge.
    /// </summary>
    public enum SignalAsset : byte
    {
        /// <summary>The post a lineside signal stands on. Not used on a bridge.</summary>
        Mast,
        /// <summary>Normal speed head of an interlocked signal.</summary>
        HomeHead,
        /// <summary>Normal speed head of an automatic, which carries an "A" plate.</summary>
        AutomaticHead,
        /// <summary>Medium speed head, hung below the normal speed one.</summary>
        BottomHead,
        /// <summary>The bridge spanning a group of parallel tracks.</summary>
        Gantry
    }

    /// <summary>
    /// Marks every object this mod puts up, whatever part of a signal it is, so a rebuild can clear
    /// the previous one out. The plan owns which entity is which; this is only for their lifetime.
    /// </summary>
    public struct RailwaySignalPart : IComponentData, IQueryTypeParameter, IEmptySerializable
    {
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
            switch (aspect)
            {
                case SignalAspect.Caution:
                case SignalAspect.ReduceToMedium:
                    return SignalLamp.Yellow;
                case SignalAspect.Clear:
                    return SignalLamp.Green;
                default:
                    return SignalLamp.Red;
            }
        }

        /// <summary>The medium speed head, below the top one.</summary>
        public static SignalLamp BottomLamp(this SignalAspect aspect)
        {
            switch (aspect)
            {
                case SignalAspect.MediumCaution:
                    return SignalLamp.Yellow;
                case SignalAspect.MediumClear:
                case SignalAspect.ReduceToMedium:
                    return SignalLamp.Green;
                default:
                    return SignalLamp.Red;
            }
        }
    }
}
