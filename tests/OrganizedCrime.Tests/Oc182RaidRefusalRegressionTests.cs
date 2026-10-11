using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc182RaidRefusalRegressionTests
{
    [Fact]
    public void Natural_presence_changes_and_countdown_share_one_occupancy_warning()
    {
        var (h, logs) = Create();
        foreach (var presence in new[] { Chapter3RaidPresence.Inside, Chapter3RaidPresence.Near,
            Chapter3RaidPresence.Unknown, Chapter3RaidPresence.Clear })
        {
            h.World.Presence = presence; h.Poll();
            h.Input = h.Input with { Minutes = h.Input.Minutes + 1 };
        }
        h.Poll();
        Assert.Single(logs);
        Assert.Contains(h.Feature.Status(), line => line.Contains("presence=clear; away minutes=1"));
        Assert.Null(h.State.Raid); Assert.Empty(h.World.Writes);
    }

    [Theory]
    [InlineData("protection")]
    [InlineData("arrest")]
    [InlineData("sweep")]
    public void Natural_detail_changes_do_not_rearm_a_safety_category(string gate)
    {
        var (h, logs) = Create();
        if (gate == "protection") h.Input = h.Input with { Protected = true };
        if (gate == "arrest") h.Input = h.Input with { ArrestTransition = true };
        if (gate == "sweep") h.Context.State = h.State with { Sweep = Sweep(1200) };
        h.Poll();
        if (gate == "protection") h.Input = h.Input with { Protected = null };
        if (gate == "arrest") h.Input = h.Input with { ArrestTransition = null };
        if (gate == "sweep") h.Context.State = h.State with { Sweep = Sweep(1210) };
        h.Poll(); h.Poll();
        Assert.Single(logs);
        var latest = gate == "sweep" ? "end minutes=1210" : gate == "arrest" ? "arrest transition unknown" : "protection unknown";
        Assert.Contains(h.Feature.Status(), line => line.Contains(latest));
        Assert.Null(h.State.Raid); Assert.Empty(h.World.Writes);
    }

    [Fact]
    public void Different_category_rearms_the_returning_category()
    {
        var (h, logs) = Create(); h.World.Presence = Chapter3RaidPresence.Inside;
        h.Poll(); h.Poll();
        h.Input = h.Input with { Protected = true }; h.Poll(); h.Poll();
        h.Input = h.Input with { Protected = false }; h.World.Presence = Chapter3RaidPresence.Near;
        h.Poll(); h.Poll();
        Assert.Equal(3, logs.Count);
        Assert.Contains("occupancy", logs[0]); Assert.Contains("protection", logs[1]); Assert.Contains("occupancy", logs[2]);
    }

    [Fact]
    public void Next_evening_rearms_the_same_category()
    {
        var (h, logs) = Create(); h.World.Presence = Chapter3RaidPresence.Inside;
        h.Poll(); h.Poll();
        h.Input = h.Input with { Minutes = 2520 }; h.Poll(); h.Poll();
        Assert.Equal(2, logs.Count);
    }

    [Fact]
    public void Next_evening_rearms_even_when_previous_raid_cooling_is_pending()
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Away(); h.Poll();
        var raid = h.State.Raid!;
        h.Context.State = h.State with { Raid = raid with { Completed = true } };
        var logs = new List<string>();
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => false, report: logs.Add);
        h.Poll(); h.Poll(); Assert.Single(logs);
        h.Input = h.Input with { Minutes = 2520 }; h.Poll(); h.Poll();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, line => Assert.Contains("previous raid cooling/notice pending", line));
    }

    [Fact]
    public void Raid_start_clears_latest_refusal_after_natural_occupancy_wait()
    {
        var (h, logs) = Create(); h.World.Presence = Chapter3RaidPresence.Inside;
        h.Poll(); Assert.Single(logs);
        h.World.Presence = Chapter3RaidPresence.Clear; h.Away();
        Assert.NotNull(h.State.Raid);
        Assert.DoesNotContain(h.Feature.Status(), line => line.Contains("raid refused:"));
        Assert.Single(logs);
    }

    [Theory]
    [InlineData("cap")]
    [InlineData("occupancy")]
    [InlineData("protection")]
    public void Each_owner_QA_requeue_reports_its_refusal_again(string gate)
    {
        var (h, logs) = Create();
        if (gate == "cap") h.Context.State = h.State with { LastRaidSuccessMinutes = 1079 };
        if (gate == "occupancy") h.World.Presence = Chapter3RaidPresence.Inside;
        if (gate == "protection") h.Input = h.Input with { Protected = true };
        h.Poll(); Assert.Single(logs);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(h.Feature.Queue("a", false).Ok); h.Poll(); h.Poll();
            Assert.Equal(i + 2, logs.Count);
        }
        Assert.Null(h.State.Raid); Assert.Empty(h.World.Writes);
    }

    [Fact]
    public void Each_immediately_refused_owner_command_reports_again()
    {
        var (h, logs) = Create();
        for (var i = 0; i < 3; i++)
        {
            var result = h.Feature.Queue("missing", true);
            Assert.False(result.Ok); Assert.Equal(i + 1, logs.Count);
            Assert.Contains("unknown site", result.Message);
        }
    }

    private static Chapter3Sweep Sweep(double end) =>
        new("s", "a", 1080, false, ActiveStartMinutes: 1080, ActiveEndMinutes: end);

    private static (Chapter3RaidSeizureTests.Harness, List<string>) Create()
    {
        var h = new Chapter3RaidSeizureTests.Harness(); var logs = new List<string>();
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: logs.Add);
        return (h, logs);
    }
}
