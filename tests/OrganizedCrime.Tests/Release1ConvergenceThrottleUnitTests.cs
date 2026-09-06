using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ConvergenceThrottleUnitTests
{
    [Fact]
    public void The_first_pass_in_a_game_minute_begins_and_the_second_does_not()
    {
        var throttle = new Release1ConvergenceThrottle();

        Assert.True(throttle.TryBegin(600d, 7L));
        Assert.False(throttle.TryBegin(600d, 7L));
        Assert.False(throttle.TryBegin(600.99d, 7L));
    }

    [Fact]
    public void The_next_game_minute_begins_again()
    {
        var throttle = new Release1ConvergenceThrottle();

        Assert.True(throttle.TryBegin(600d, 7L));
        Assert.False(throttle.TryBegin(600.5d, 7L));
        Assert.True(throttle.TryBegin(601d, 7L));
    }

    [Fact]
    public void A_story_revision_change_inside_one_game_minute_begins_again()
    {
        var throttle = new Release1ConvergenceThrottle();

        Assert.True(throttle.TryBegin(600d, 7L));
        Assert.False(throttle.TryBegin(600d, 7L));
        Assert.True(throttle.TryBegin(600d, 8L));
        Assert.False(throttle.TryBegin(600d, 8L));
    }

    [Fact]
    public void An_unreadable_clock_fails_open_on_every_pass()
    {
        var throttle = new Release1ConvergenceThrottle();

        Assert.True(throttle.TryBegin(null, 7L));
        Assert.True(throttle.TryBegin(null, 7L));
        // And a readable clock straight after an unreadable one still begins, because the stamp was
        // cleared rather than kept.
        Assert.True(throttle.TryBegin(600d, 7L));
        Assert.False(throttle.TryBegin(600d, 7L));
    }

    [Fact]
    public void Clear_lets_a_boundary_pass_read_inside_the_same_game_minute()
    {
        var throttle = new Release1ConvergenceThrottle();

        Assert.True(throttle.TryBegin(600d, 7L));
        Assert.False(throttle.TryBegin(600d, 7L));
        throttle.Clear();
        Assert.True(throttle.TryBegin(600d, 7L));
        Assert.False(throttle.TryBegin(600d, 7L));
    }

    [Fact]
    public void A_fresh_throttle_begins_at_the_zero_minute_and_at_the_default_revision()
    {
        var throttle = new Release1ConvergenceThrottle();

        Assert.True(throttle.TryBegin(0d, -1L));
        Assert.False(throttle.TryBegin(0d, -1L));
    }
}
