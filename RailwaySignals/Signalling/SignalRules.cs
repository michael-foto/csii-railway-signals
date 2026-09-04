namespace RailwaySignals.Signalling
{
    /// <summary>The part of a signal's state the aspect rules read.</summary>
    public readonly struct SignalState
    {
        /// <summary>A train stands in the block, or another signal has a road booked over it.</summary>
        public readonly bool m_Blocked;

        /// <summary>Some road through the block reaches another signal. False at a buffer stop.</summary>
        public readonly bool m_HasClearRoute;

        /// <summary>Some road reaches another signal without crossing medium speed track.</summary>
        public readonly bool m_HasNormalRoute;

        /// <summary>Speed the signal is admitting a train at, once resolved for the tick.</summary>
        public readonly SignalSpeed m_Speed;

        public SignalState(bool blocked, bool hasClearRoute, bool hasNormalRoute, SignalSpeed speed)
        {
            m_Blocked = blocked;
            m_HasClearRoute = hasClearRoute;
            m_HasNormalRoute = hasNormalRoute;
            m_Speed = speed;
        }
    }

    /// <summary>The road a train has booked over a signal, if one is set.</summary>
    public readonly struct RouteState
    {
        public readonly bool m_IsSet;

        /// <summary>Site the road runs to, or -1 when it stops short of another signal.</summary>
        public readonly int m_Successor;

        public readonly SignalSpeed m_Speed;

        public RouteState(int successor, SignalSpeed speed)
        {
            m_IsSet = true;
            m_Successor = successor;
            m_Speed = speed;
        }

        public static RouteState None => default;
    }

    /// <summary>
    /// The signals at the far end of a block. Read through a constrained generic so the game side
    /// can hand over a view of its native lists and a test can hand over one of an array, neither
    /// allocating nor going through a virtual call.
    /// </summary>
    public interface ISignalStates
    {
        int Length { get; }

        SignalState this[int index] { get; }
    }

    /// <summary>
    /// What a signal shows and whether it holds a train, as pure decisions over plain data. Kept
    /// clear of the entity component system on purpose: the occupancy that feeds
    /// <see cref="SignalState.m_Blocked"/> and the reservation that carries out a hold are the only
    /// parts that need the game, and both are inputs and outputs rather than rules. Everything here
    /// can therefore be exercised directly by tests.
    /// </summary>
    public static class SignalRules
    {
        /// <summary>
        /// Whether a signal is at danger for a reason of its own, without regard to what lies beyond
        /// it. Read both for the signal being resolved and for the one ahead of it, so a caution is
        /// shown for a signal at a buffer stop as well as for one with something in its block.
        /// </summary>
        public static bool IsAtStop(in SignalState site)
        {
            return site.m_Blocked || !site.m_HasClearRoute;
        }

        /// <summary>
        /// The speed a signal admits a train at. A booked road is priced exactly; with none set the
        /// least restrictive road out of the block is taken, which is the fast road at a junction.
        /// </summary>
        public static SignalSpeed Speed(in SignalState site, in RouteState route)
        {
            if (route.m_IsSet)
            {
                return route.m_Speed;
            }
            return site.m_HasNormalRoute ? SignalSpeed.Normal : SignalSpeed.Medium;
        }

        /// <summary>
        /// Reads every road out of the block for a signal with no train booked over it, worst case:
        /// any one of them shut brings the signal to caution, because a following train has no
        /// guarantee of being turned down a clear branch at the junction ahead. False means a
        /// warning is called for.
        /// </summary>
        public static bool LookAhead<T>(in T successors, out bool mediumAhead) where T : ISignalStates
        {
            mediumAhead = false;
            for (int i = 0; i < successors.Length; i++)
            {
                SignalState successor = successors[i];
                if (IsAtStop(successor))
                {
                    return false;
                }
                mediumAhead |= successor.m_Speed == SignalSpeed.Medium;
            }
            return true;
        }

        /// <summary>
        /// What a signal shows. A road with a successor is the only case where the signal ahead is
        /// known; a train booked no further than this block, which is every train terminating at a
        /// platform, says nothing about the road beyond and falls back to reading every road out.
        /// </summary>
        /// <param name="ahead">
        /// The signal the booked road runs to. Ignored unless the road is set and names one.
        /// </param>
        public static SignalAspect Aspect<T>(in SignalState site, in RouteState route, in SignalState ahead, in T successors)
            where T : ISignalStates
        {
            if (IsAtStop(site))
            {
                return SignalAspect.Stop;
            }

            bool medium = site.m_Speed == SignalSpeed.Medium;
            bool mediumAhead;

            if (route.m_IsSet && route.m_Successor >= 0)
            {
                if (IsAtStop(ahead))
                {
                    return medium ? SignalAspect.MediumCaution : SignalAspect.Caution;
                }
                mediumAhead = ahead.m_Speed == SignalSpeed.Medium;
            }
            else if (!LookAhead(successors, out mediumAhead))
            {
                return medium ? SignalAspect.MediumCaution : SignalAspect.Caution;
            }

            if (medium)
            {
                return SignalAspect.MediumClear;
            }
            return mediumAhead ? SignalAspect.ReduceToMedium : SignalAspect.Clear;
        }

        /// <summary>
        /// Whether a signal may not offer a lane. Occupancy is passed in because reading it needs
        /// the game; the booking half is the route locking that puts the approaches to a junction to
        /// danger as soon as a train is signalled through it, rather than only once it arrives.
        /// </summary>
        /// <param name="bookedBy">Site admitting a train into the lane. Meaningless unless booked.</param>
        public static bool Claimed(bool occupied, bool booked, int bookedBy, int siteIndex)
        {
            return occupied || (booked && bookedBy != siteIndex);
        }

        /// <summary>
        /// Whether to hold a train short of a lane entering this signal's block.
        /// <para>
        /// A lane booked to another signal is left alone. Otherwise a signal at danger because of
        /// train A would reserve the very lanes A is booked over and hold A at its own proceed
        /// aspect, which is what happens at a terminal throat where the roads out of every platform
        /// share lanes. Failing to hold a train is cosmetic; holding one that has a clear signal
        /// stalls the railway.
        /// </para>
        /// <para>
        /// Nothing is held once the count passes <paramref name="releasePasses"/>. Absolute block
        /// over track laid without signalling in mind can bring two trains to a stand waiting on
        /// each other, and the game's own answer to a deadlocked train is to delete it, so a hold
        /// always has to end.
        /// </para>
        /// </summary>
        public static bool ShouldHoldLane(bool enforcing, bool blocked, int holdPasses, int releasePasses, bool bookedElsewhere)
        {
            return enforcing && blocked && !bookedElsewhere && holdPasses < releasePasses;
        }
    }
}
