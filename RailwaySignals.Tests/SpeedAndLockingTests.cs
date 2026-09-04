using RailwaySignals.Signalling;
using Xunit;

namespace RailwaySignals.Tests
{
    /// <summary>The speed a signal admits a train at, and the route locking behind it.</summary>
    public class SpeedTests
    {
        [Fact]
        public void A_booked_road_over_slow_track_is_medium()
        {
            var route = new RouteState(successor: 0, SignalSpeed.Medium);
            Assert.Equal(SignalSpeed.Medium, SignalRules.Speed(Signals.Open(), route));
        }

        [Fact]
        public void A_booked_road_on_fast_track_is_normal_though_the_junction_has_a_slow_siding()
        {
            // Regression: the original complaint. The siding is a road out of the block, but the
            // train is not booked over it, so it must not pull the train down.
            var route = new RouteState(successor: 0, SignalSpeed.Normal);
            SignalState atJunctionWithSiding = Signals.OnlyMediumRoads();
            Assert.Equal(SignalSpeed.Normal, SignalRules.Speed(atJunctionWithSiding, route));
        }

        [Fact]
        public void With_no_road_set_a_normal_road_out_of_the_block_gives_normal()
        {
            Assert.Equal(SignalSpeed.Normal, SignalRules.Speed(Signals.Open(), RouteState.None));
        }

        [Fact]
        public void With_no_road_set_and_only_slow_roads_out_gives_medium()
        {
            Assert.Equal(SignalSpeed.Medium, SignalRules.Speed(Signals.OnlyMediumRoads(), RouteState.None));
        }
    }

    /// <summary>
    /// Route locking: the approaches to a junction go to danger as soon as a train is signalled
    /// through it, rather than only once it arrives.
    /// </summary>
    public class RouteLockingTests
    {
        private const int Admitting = 4;
        private const int Conflicting = 7;

        [Fact]
        public void A_booked_lane_is_claimed_against_every_other_signal()
        {
            Assert.True(SignalRules.Claimed(occupied: false, booked: true, bookedBy: Admitting, siteIndex: Conflicting));
        }

        [Fact]
        public void A_booked_lane_is_not_claimed_against_the_signal_admitting_the_train()
        {
            // This is what keeps a departure signal off danger for its own train.
            Assert.False(SignalRules.Claimed(occupied: false, booked: true, bookedBy: Admitting, siteIndex: Admitting));
        }

        [Fact]
        public void An_unbooked_empty_lane_is_free()
        {
            Assert.False(SignalRules.Claimed(occupied: false, booked: false, bookedBy: -1, siteIndex: Conflicting));
        }

        [Fact]
        public void Occupancy_claims_a_lane_even_for_the_signal_that_booked_it()
        {
            // A train standing in the block is decisive whoever holds the booking.
            Assert.True(SignalRules.Claimed(occupied: true, booked: true, bookedBy: Admitting, siteIndex: Admitting));
        }
    }

    /// <summary>Whether a stop aspect actually holds a train, and when it lets go.</summary>
    public class HoldingTests
    {
        private const int Release = 100;

        [Fact]
        public void Nothing_is_held_while_the_option_is_off()
        {
            Assert.False(SignalRules.ShouldHoldLane(
                enforcing: false, blocked: true, holdPasses: 0, Release, bookedElsewhere: false));
        }

        [Fact]
        public void A_blocked_signal_holds_the_lanes_into_its_block()
        {
            Assert.True(SignalRules.ShouldHoldLane(
                enforcing: true, blocked: true, holdPasses: 0, Release, bookedElsewhere: false));
        }

        [Fact]
        public void A_signal_that_is_not_blocked_holds_nothing()
        {
            // Covers the signal at the stop blocks: permanently at danger, never enforced, so a
            // terminal platform, siding or depot road stays reachable.
            Assert.False(SignalRules.ShouldHoldLane(
                enforcing: true, blocked: false, holdPasses: 0, Release, bookedElsewhere: false));
        }

        [Fact]
        public void A_lane_booked_to_another_signal_is_never_held()
        {
            // Regression: trains stopping at their own departure signal. At a terminal throat the
            // lanes out of every platform overlap, so a signal at danger because of train A would
            // otherwise reserve the lanes A itself is booked over.
            Assert.False(SignalRules.ShouldHoldLane(
                enforcing: true, blocked: true, holdPasses: 0, Release, bookedElsewhere: true));
        }

        [Fact]
        public void Holding_stops_once_the_release_count_is_reached()
        {
            Assert.True(SignalRules.ShouldHoldLane(
                enforcing: true, blocked: true, holdPasses: Release - 1, Release, bookedElsewhere: false));
            Assert.False(SignalRules.ShouldHoldLane(
                enforcing: true, blocked: true, holdPasses: Release, Release, bookedElsewhere: false));
            Assert.False(SignalRules.ShouldHoldLane(
                enforcing: true, blocked: true, holdPasses: Release + 500, Release, bookedElsewhere: false));
        }
    }

    /// <summary>Which lamp each aspect lights, on each of the two heads.</summary>
    public class LampTests
    {
        [Theory]
        [InlineData(SignalAspect.Stop, SignalLamp.Red, SignalLamp.Red)]
        [InlineData(SignalAspect.Caution, SignalLamp.Yellow, SignalLamp.Red)]
        [InlineData(SignalAspect.Clear, SignalLamp.Green, SignalLamp.Red)]
        [InlineData(SignalAspect.MediumCaution, SignalLamp.Red, SignalLamp.Yellow)]
        [InlineData(SignalAspect.MediumClear, SignalLamp.Red, SignalLamp.Green)]
        [InlineData(SignalAspect.ReduceToMedium, SignalLamp.Yellow, SignalLamp.Green)]
        public void Each_aspect_lights_the_right_heads(SignalAspect aspect, SignalLamp top, SignalLamp bottom)
        {
            Assert.Equal(top, aspect.TopLamp());
            Assert.Equal(bottom, aspect.BottomLamp());
        }
    }
}
