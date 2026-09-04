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
    }

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

    public enum SignalSpeed : byte
    {
        Normal,
        /// <summary>Tight curves, slow track, or the cramped geometry of a junction or yard.</summary>
        Medium
    }

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
