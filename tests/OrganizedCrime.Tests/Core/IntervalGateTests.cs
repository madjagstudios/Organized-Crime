using OrganizedCrime.Core.Scheduling;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-149: the shared wall-clock next-at throttle behind the visit pump's
// retry, HQ attempt and HQ maintain stamps.
public sealed class IntervalGateTests
{
    [Fact]
    public void Fresh_gate_passes_at_zero_and_at_positive_now()
    {
        var gate = new IntervalGate(1.0f);

        Assert.True(gate.TryPass(0.0f));
    }

    [Fact]
    public void A_second_pass_inside_the_interval_throttles_and_exactly_at_next_it_passes()
    {
        var gate = new IntervalGate(1.0f);

        Assert.True(gate.TryPass(0.0f));
        Assert.False(gate.TryPass(0.99f));
        Assert.True(gate.TryPass(1.0f));
        Assert.False(gate.TryPass(1.0f));
    }

    [Fact]
    public void Throttled_passes_do_not_move_the_stamp()
    {
        var gate = new IntervalGate(1.0f);

        Assert.True(gate.TryPass(0.0f));
        Assert.False(gate.TryPass(0.5f));
        Assert.False(gate.TryPass(0.6f));
        Assert.True(gate.TryPass(1.0f));
    }

    [Fact]
    public void Fresh_gate_throttles_negative_now_like_a_zeroed_stamp()
    {
        var gate = new IntervalGate(1.0f);

        Assert.False(gate.TryPass(-1.0f));
        Assert.True(gate.TryPass(0.0f));
    }

    [Fact]
    public void Reset_restores_the_fresh_state()
    {
        var gate = new IntervalGate(1.0f);

        Assert.True(gate.TryPass(5.0f));
        Assert.False(gate.TryPass(5.5f));
        gate.Reset();
        Assert.True(gate.TryPass(5.5f));
        Assert.False(gate.TryPass(-1.0f));
    }

    [Fact]
    public void Reset_at_now_arms_the_gate_to_pass_there_and_throttle_before_it()
    {
        var gate = new IntervalGate(1.0f);

        gate.Reset(10.0f);
        Assert.False(gate.TryPass(9.999f));
        Assert.True(gate.TryPass(10.0f));
        Assert.False(gate.TryPass(10.5f));
        Assert.True(gate.TryPass(11.0f));
    }

    [Fact]
    public void Reset_at_now_plus_interval_defers_the_next_pass_by_the_interval()
    {
        var gate = new IntervalGate(1.0f);

        gate.Reset(10.0f + 1.0f);
        Assert.False(gate.TryPass(10.5f));
        Assert.True(gate.TryPass(11.0f));
    }

    [Fact]
    public void Quarter_second_interval_passes_exactly_at_the_quarter()
    {
        var gate = new IntervalGate(0.25f);

        gate.Reset(5.0f);
        Assert.True(gate.TryPass(5.0f));
        Assert.False(gate.TryPass(5.24f));
        Assert.True(gate.TryPass(5.25f));
    }

    [Fact]
    public void Boundaries_stay_exact_far_into_a_session()
    {
        var gate = new IntervalGate(1.0f);

        gate.Reset(1000000.0f);
        Assert.True(gate.TryPass(1000000.0f));
        // One float step below the deadline: the ULP is 0.0625 this far
        // into a session, so 1000000.999f would already round up to it.
        Assert.False(gate.TryPass(1000000.9375f));
        Assert.True(gate.TryPass(1000001.0f));
    }

    [Fact]
    public void Re_arm_rounds_like_the_float_stamp_it_replaces()
    {
        var gate = new IntervalGate(1.0f);

        gate.Reset(0.3f);
        Assert.True(gate.TryPass(0.3f));
        Assert.False(gate.TryPass(MathF.BitDecrement(0.3f + 1.0f)));
        Assert.True(gate.TryPass(0.3f + 1.0f));
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(-1.0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Non_positive_or_non_finite_intervals_are_rejected(float interval)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntervalGate(interval));
    }
}
