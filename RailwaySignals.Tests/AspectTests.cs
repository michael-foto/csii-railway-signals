using RailwaySignals.Signalling;
using Xunit;

namespace RailwaySignals.Tests
{
    /// <summary>
    /// What a signal shows, over the road it is reading. Every case here is a situation that can be
    /// walked up to in game, so a failure names the scenario rather than the branch.
    /// </summary>
    public class AspectTests
    {
        private static SignalAspect Unrouted(SignalState site, params SignalState[] successors)
        {
            return SignalRules.Aspect(site, RouteState.None, default, new Successors(successors));
        }

        private static SignalAspect Routed(SignalState site, int successor, SignalSpeed roadSpeed, SignalState ahead, params SignalState[] successors)
        {
            var route = new RouteState(successor, roadSpeed);
            SignalState resolved = new SignalState(site.m_Blocked, site.m_HasClearRoute, site.m_HasNormalRoute, SignalRules.Speed(site, route));
            return SignalRules.Aspect(resolved, route, ahead, new Successors(successors));
        }

        // ---- unrouted -------------------------------------------------------------------------

        [Fact]
        public void Clear_road_and_a_clear_signal_ahead_shows_clear()
        {
            Assert.Equal(SignalAspect.Clear, Unrouted(Signals.Open(), Signals.Open()));
        }

        [Fact]
        public void Something_in_its_own_block_shows_stop()
        {
            Assert.Equal(SignalAspect.Stop, Unrouted(Signals.Occupied(), Signals.Open()));
        }

        [Fact]
        public void Next_signal_at_stop_shows_caution()
        {
            Assert.Equal(SignalAspect.Caution, Unrouted(Signals.Open(), Signals.Occupied()));
        }

        [Fact]
        public void Next_signal_at_medium_shows_reduce_to_medium()
        {
            Assert.Equal(SignalAspect.ReduceToMedium, Unrouted(Signals.Open(), Signals.Open(SignalSpeed.Medium)));
        }

        [Fact]
        public void Medium_road_of_its_own_shows_medium_clear()
        {
            Assert.Equal(SignalAspect.MediumClear, Unrouted(Signals.Open(SignalSpeed.Medium), Signals.Open()));
        }

        [Fact]
        public void Medium_road_with_the_next_signal_at_stop_shows_medium_caution()
        {
            Assert.Equal(SignalAspect.MediumCaution, Unrouted(Signals.Open(SignalSpeed.Medium), Signals.Occupied()));
        }

        [Fact]
        public void A_signal_at_the_stop_blocks_shows_stop_whatever_lies_beyond()
        {
            Assert.Equal(SignalAspect.Stop, Unrouted(Signals.AtBuffers(), Signals.Open()));
            Assert.Equal(SignalAspect.Stop, Unrouted(Signals.AtBuffers()));
        }

        [Fact]
        public void The_signal_before_the_stop_blocks_shows_caution()
        {
            // The buffer signal can never come off, and warning for it is the whole point of
            // putting a signal at the stop blocks in the first place.
            Assert.Equal(SignalAspect.Caution, Unrouted(Signals.Open(), Signals.AtBuffers()));
        }

        [Fact]
        public void One_occupied_branch_at_a_junction_shows_caution()
        {
            // Worst case with no road set: a following train has no guarantee of being turned
            // down the clear branch.
            Assert.Equal(SignalAspect.Caution, Unrouted(Signals.Open(), Signals.Open(), Signals.Occupied()));
        }

        [Fact]
        public void All_branches_clear_but_one_medium_shows_reduce_to_medium()
        {
            Assert.Equal(
                SignalAspect.ReduceToMedium,
                Unrouted(Signals.Open(), Signals.Open(), Signals.Open(SignalSpeed.Medium)));
        }

        // ---- routed ---------------------------------------------------------------------------

        [Fact]
        public void Routed_to_a_clear_branch_shows_clear_though_another_branch_is_occupied()
        {
            // Regression: worst case must not apply once the road is known.
            Assert.Equal(
                SignalAspect.Clear,
                Routed(Signals.Open(), successor: 0, SignalSpeed.Normal, Signals.Open(), Signals.Open(), Signals.Occupied()));
        }

        [Fact]
        public void Routed_to_the_occupied_branch_shows_caution()
        {
            Assert.Equal(
                SignalAspect.Caution,
                Routed(Signals.Open(), successor: 1, SignalSpeed.Normal, Signals.Occupied(), Signals.Open(), Signals.Occupied()));
        }

        [Fact]
        public void A_train_terminating_in_the_block_reads_the_roads_out_not_a_caution()
        {
            // Regression: the station signal stuck at caution. A train pulling up short of the
            // signal says nothing about the road beyond it, so the lookahead decides.
            var route = new RouteState(successor: -1, SignalSpeed.Normal);
            Assert.Equal(
                SignalAspect.Clear,
                SignalRules.Aspect(Signals.Open(), route, default, new Successors(Signals.Open())));

            Assert.Equal(
                SignalAspect.Caution,
                SignalRules.Aspect(Signals.Open(), route, default, new Successors(Signals.Occupied())));
        }

        [Fact]
        public void Routed_over_a_medium_road_puts_the_indication_on_the_lower_head()
        {
            SignalAspect aspect = Routed(Signals.Open(), successor: 0, SignalSpeed.Medium, Signals.Open(), Signals.Open());
            Assert.Equal(SignalAspect.MediumClear, aspect);
            Assert.Equal(SignalLamp.Red, aspect.TopLamp());
            Assert.Equal(SignalLamp.Green, aspect.BottomLamp());
        }
    }
}
