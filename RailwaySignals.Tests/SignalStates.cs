using RailwaySignals.Signalling;

namespace RailwaySignals.Tests
{
    /// <summary>
    /// An array of signals presented to the rules, standing in for the view over the plan's native
    /// lists that the game side supplies.
    /// </summary>
    internal readonly struct Successors : ISignalStates
    {
        private readonly SignalState[] m_States;

        public Successors(params SignalState[] states)
        {
            m_States = states;
        }

        public int Length => m_States.Length;

        public SignalState this[int index] => m_States[index];
    }

    /// <summary>Shorthand for the signal states the scenarios are built from.</summary>
    internal static class Signals
    {
        /// <summary>Clear road ahead to another signal, nothing standing in it.</summary>
        public static SignalState Open(SignalSpeed speed = SignalSpeed.Normal)
        {
            return new SignalState(blocked: false, hasClearRoute: true, hasNormalRoute: true, speed);
        }

        /// <summary>Something stands in the block, or a road is booked over it elsewhere.</summary>
        public static SignalState Occupied(SignalSpeed speed = SignalSpeed.Normal)
        {
            return new SignalState(blocked: true, hasClearRoute: true, hasNormalRoute: true, speed);
        }

        /// <summary>At the stop blocks: no road reaches another signal, so it never comes off.</summary>
        public static SignalState AtBuffers()
        {
            return new SignalState(blocked: false, hasClearRoute: false, hasNormalRoute: false, SignalSpeed.Normal);
        }

        /// <summary>A road reaches another signal, but every one of them crosses slow track.</summary>
        public static SignalState OnlyMediumRoads()
        {
            return new SignalState(blocked: false, hasClearRoute: true, hasNormalRoute: false, SignalSpeed.Medium);
        }
    }
}
