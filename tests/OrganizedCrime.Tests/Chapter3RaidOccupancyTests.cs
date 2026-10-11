using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3RaidOccupancyTests
{
    [Theory]
    [InlineData(1, 1, 1, true)]
    [InlineData(1, 2, 1, false)]
    [InlineData(1, 1, 2, false)]
    [InlineData(2, 2, 2, false)]
    [InlineData(0, 1, 1, false)]
    public void Unsupported_or_inconsistent_multiplayer_census_blocks(int registry, int connections, int lobby, bool expected) =>
        Assert.Equal(expected, Chapter3RaidOccupancy.SupportsCensus(registry, connections, lobby));
    [Theory]
    [InlineData(true, false, 100, (int)Chapter3RaidPresence.Inside)]
    [InlineData(false, true, 100, (int)Chapter3RaidPresence.Inside)]
    [InlineData(false, false, 50, (int)Chapter3RaidPresence.Near)]
    [InlineData(false, false, 50.001, (int)Chapter3RaidPresence.Clear)]
    [InlineData(false, false, double.NaN, (int)Chapter3RaidPresence.Unknown)]
    public void Census_uses_property_bounds_and_inclusive_fifty_metres(bool property, bool bounds, double distance, int expected) =>
        Assert.Equal((Chapter3RaidPresence)expected, Chapter3RaidOccupancy.Classify(true, new[] { new Chapter3RaidPlayer(true, property, bounds, distance) }));

    [Fact]
    public void Missing_census_or_unreadable_player_blocks()
    {
        Assert.Equal(Chapter3RaidPresence.Unknown, Chapter3RaidOccupancy.Classify(false, Array.Empty<Chapter3RaidPlayer>()));
        Assert.Equal(Chapter3RaidPresence.Unknown, Chapter3RaidOccupancy.Classify(true, new[] { new Chapter3RaidPlayer(false, false, false, 100) }));
    }

    [Theory]
    [InlineData(true, false, (int)Chapter3RaidPresence.Inside)]
    [InlineData(false, true, (int)Chapter3RaidPresence.Unknown)]
    public void Hq_inside_or_transition_blocks(bool inside, bool transition, int expected) =>
        Assert.Equal((Chapter3RaidPresence)expected, Chapter3RaidOccupancy.Classify(true, new[] { new Chapter3RaidPlayer(true, false, false, 100) }, inside, transition));

    [Fact]
    public void Ten_minutes_are_required_and_approach_resets_continuity()
    {
        var tracker = new Chapter3RaidAwayTracker();
        Assert.False(tracker.Observe("a", 100, Chapter3RaidPresence.Clear).CanRaid);
        for (var minute = 101; minute < 110; minute++) Assert.False(tracker.Observe("a", minute, Chapter3RaidPresence.Clear).CanRaid);
        Assert.True(tracker.Observe("a", 110, Chapter3RaidPresence.Clear).CanRaid);
        Assert.False(tracker.Observe("a", 111, Chapter3RaidPresence.Near).CanRaid);
        Assert.Equal(0, tracker.Observe("a", 112, Chapter3RaidPresence.Clear).AwayMinutes);
        Assert.False(tracker.Observe("b", 112, Chapter3RaidPresence.Clear).CanRaid);
    }

    [Theory]
    [InlineData((int)Chapter3RaidPresence.Unknown)]
    [InlineData((int)Chapter3RaidPresence.Inside)]
    public void Unknown_and_inside_reset(int presence)
    {
        var tracker = new Chapter3RaidAwayTracker();
        tracker.Observe("a", 0, Chapter3RaidPresence.Clear);
        Assert.False(tracker.Observe("a", 1, (Chapter3RaidPresence)presence).CanRaid);
        Assert.Equal(0, tracker.Observe("a", 2, Chapter3RaidPresence.Clear).AwayMinutes);
    }

    [Fact]
    public void Invalid_clock_backwards_clock_observation_gap_and_reset_discard_history()
    {
        var tracker = new Chapter3RaidAwayTracker();
        tracker.Observe("a", 10, Chapter3RaidPresence.Clear);
        Assert.Equal(0, tracker.Observe("a", 9, Chapter3RaidPresence.Clear).AwayMinutes);
        Assert.Equal(0, tracker.Observe("a", 20, Chapter3RaidPresence.Clear).AwayMinutes);
        Assert.False(tracker.Observe("a", null, Chapter3RaidPresence.Clear).CanRaid);
        Assert.False(tracker.Observe("a", double.NaN, Chapter3RaidPresence.Clear).CanRaid);
        tracker.Reset();
        Assert.Equal(0, tracker.Observe("a", 21, Chapter3RaidPresence.Clear).AwayMinutes);
    }
}
