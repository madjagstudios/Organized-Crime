using OrganizedCrime.Core.Scheduling;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-149: the shared once-per-game-minute gate. The first six tests migrate
// Release1ConvergenceThrottleUnitTests 1:1 (type and TryBegin renamed, same
// values and assertions); the rest migrate the convergence characterization
// pins from Characterization/ConvergenceThrottleCharacterizationTests.cs.
public sealed class GameHourGateTests
{
    [Fact]
    public void The_first_pass_in_a_game_minute_begins_and_the_second_does_not()
    {
        var gate = new GameHourGate();

        Assert.True(gate.TryPass(600d, 7L));
        Assert.False(gate.TryPass(600d, 7L));
        Assert.False(gate.TryPass(600.99d, 7L));
    }

    [Fact]
    public void The_next_game_minute_begins_again()
    {
        var gate = new GameHourGate();

        Assert.True(gate.TryPass(600d, 7L));
        Assert.False(gate.TryPass(600.5d, 7L));
        Assert.True(gate.TryPass(601d, 7L));
    }

    [Fact]
    public void A_story_revision_change_inside_one_game_minute_begins_again()
    {
        var gate = new GameHourGate();

        Assert.True(gate.TryPass(600d, 7L));
        Assert.False(gate.TryPass(600d, 7L));
        Assert.True(gate.TryPass(600d, 8L));
        Assert.False(gate.TryPass(600d, 8L));
    }

    [Fact]
    public void An_unreadable_clock_fails_open_on_every_pass()
    {
        var gate = new GameHourGate();

        Assert.True(gate.TryPass(null, 7L));
        Assert.True(gate.TryPass(null, 7L));
        // And a readable clock straight after an unreadable one still begins, because the stamp was
        // cleared rather than kept.
        Assert.True(gate.TryPass(600d, 7L));
        Assert.False(gate.TryPass(600d, 7L));
    }

    [Fact]
    public void Clear_lets_a_boundary_pass_read_inside_the_same_game_minute()
    {
        var gate = new GameHourGate();

        Assert.True(gate.TryPass(600d, 7L));
        Assert.False(gate.TryPass(600d, 7L));
        gate.Clear();
        Assert.True(gate.TryPass(600d, 7L));
        Assert.False(gate.TryPass(600d, 7L));
    }

    [Fact]
    public void A_fresh_gate_begins_at_the_zero_minute_and_at_the_default_revision()
    {
        var gate = new GameHourGate();

        Assert.True(gate.TryPass(0d, -1L));
        Assert.False(gate.TryPass(0d, -1L));
    }

    [Fact]
    public void Just_before_the_minute_boundary_stays_throttled_and_exactly_at_it_passes()
    {
        var gate = new GameHourGate();

        Assert.True(gate.TryPass(600d, 7L));
        Assert.False(gate.TryPass(600.9999999999999d, 7L));
        Assert.True(gate.TryPass(601d, 7L));
        Assert.False(gate.TryPass(601d, 7L));
    }

    [Fact]
    public void A_backwards_clock_passes_again_and_so_does_returning_to_the_old_minute()
    {
        var gate = new GameHourGate();

        Assert.True(gate.TryPass(600d, 7L));
        Assert.False(gate.TryPass(600d, 7L));
        Assert.True(gate.TryPass(599d, 7L));
        Assert.False(gate.TryPass(599d, 7L));
        Assert.True(gate.TryPass(600d, 7L));
    }

    [Fact]
    public void Negative_minutes_floor_downward_like_any_other_minute()
    {
        var gate = new GameHourGate();

        Assert.True(gate.TryPass(-0.5d, 7L));
        Assert.False(gate.TryPass(-0.9d, 7L));
        Assert.True(gate.TryPass(-1.5d, 7L));
        Assert.False(gate.TryPass(-1.5d, 7L));
    }

    [Fact]
    public void A_revision_change_back_inside_one_minute_passes_again()
    {
        var gate = new GameHourGate();

        Assert.True(gate.TryPass(600d, 7L));
        Assert.True(gate.TryPass(600d, 8L));
        Assert.True(gate.TryPass(600d, 7L));
        Assert.False(gate.TryPass(600d, 7L));
    }

    [Fact]
    public void Clear_re_arms_the_same_minute_and_revision_once()
    {
        var gate = new GameHourGate();

        Assert.True(gate.TryPass(600d, 7L));
        Assert.False(gate.TryPass(600d, 7L));
        gate.Clear();
        Assert.True(gate.TryPass(600d, 7L));
        Assert.False(gate.TryPass(600d, 7L));
    }
}
