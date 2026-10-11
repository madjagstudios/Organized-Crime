using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc179RaidRefusalTests
{
    [Theory]
    [InlineData("sweep", "sweep active; end minutes=2520")]
    [InlineData("protection", "protection active")]
    [InlineData("unknown-cover", "protection unknown")]
    [InlineData("arrest", "arrest transition active")]
    [InlineData("unknown-arrest", "arrest transition unknown")]
    [InlineData("cap", "cap; remaining minutes=10079")]
    [InlineData("restoration", "restoration pending")]
    [InlineData("plan", "no removable package/plan")]
    [InlineData("inside", "occupancy not clear; site=a; presence=inside; away minutes=0")]
    [InlineData("unknown", "occupancy not clear; site=a; presence=unknown; away minutes=0")]
    public void Queued_force_only_bypasses_occupancy_and_retains_visible_refusal(string gate, string expected)
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Input = h.Input with { Tier = LocalPressureTier.Watched };
        var logs = new List<string>();
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: logs.Add);
        Assert.True(h.Feature.Queue("a", gate is not ("inside" or "unknown")).Ok);
        switch (gate)
        {
            case "sweep": h.Context.State = h.State with { Sweep = new("s", "a", 1080, false, ActiveStartMinutes: 1080, ActiveEndMinutes: 2520) }; break;
            case "protection": h.Input = h.Input with { Protected = true }; break;
            case "unknown-cover": h.Input = h.Input with { Protected = null }; break;
            case "arrest": h.Input = h.Input with { ArrestTransition = true }; break;
            case "unknown-arrest": h.Input = h.Input with { ArrestTransition = null }; break;
            case "cap": h.Context.State = h.State with { LastRaidSuccessMinutes = 1079 }; break;
            case "restoration": h.World.Restored = false; break;
            case "plan": h.World.Slots = new[] { Chapter3RaidSeizureTests.Harness.Slot(0, 1, 20) }; break;
            case "inside": h.World.Presence = Chapter3RaidPresence.Inside; break;
            case "unknown": h.World.Presence = Chapter3RaidPresence.Unknown; break;
        }
        h.Poll();
        Assert.Null(h.State.Raid);
        Assert.Contains(h.Feature.Status(), line => line.Contains(expected, StringComparison.Ordinal));
        Assert.Single(logs); Assert.Contains(expected, logs[0]);
        Assert.Empty(h.World.Writes); Assert.Empty(h.Coolings);
        h.Poll(); Assert.Single(logs);
    }

    [Fact]
    public void Natural_outside_window_is_reported_once_and_status_is_read_only()
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Input = h.Input with { Minutes = 1000 };
        var logs = new List<string>();
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: logs.Add);
        h.Poll(); h.Poll();
        Assert.Single(logs); Assert.Contains("outside 18:00-23:00", logs[0]);
        var state = h.State;
        Assert.Contains(h.Feature.Status(), line => line.Contains("outside 18:00-23:00"));
        Assert.Same(state, h.State); Assert.Single(logs);
    }

    [Fact]
    public void Unknown_site_immediate_refusal_is_retained_and_logged()
    {
        var h = new Chapter3RaidSeizureTests.Harness(); var logs = new List<string>();
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: logs.Add);
        Assert.False(h.Feature.Queue("missing", true).Ok);
        Assert.Contains(h.Feature.Status(), line => line.Contains("unknown site 'missing'"));
        Assert.Single(logs);
    }

    [Fact]
    public void Occupancy_countdown_status_updates_without_log_spam_and_clears_on_start()
    {
        var h = new Chapter3RaidSeizureTests.Harness(); var logs = new List<string>();
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: logs.Add);
        Assert.True(h.Feature.Queue("a", false).Ok);
        for (var i = 0; i < 10; i++) { h.Poll(); h.Input = h.Input with { Minutes = h.Input.Minutes + 1 }; }
        Assert.Single(logs);
        Assert.Contains(h.Feature.Status(), line => line.Contains("away minutes=9"));
        h.Poll(); Assert.NotNull(h.State.Raid);
        Assert.DoesNotContain(h.Feature.Status(), line => line.Contains("raid refused:"));
    }

    [Theory]
    [InlineData("sweep", "sweep active; end minutes=2520")]
    [InlineData("protection", "protection active")]
    [InlineData("unknown-cover", "protection unknown")]
    [InlineData("arrest", "arrest transition active")]
    [InlineData("unknown-arrest", "arrest transition unknown")]
    [InlineData("cap", "cap; remaining minutes=10079")]
    [InlineData("restoration", "restoration pending")]
    [InlineData("plan", "no removable package/plan")]
    [InlineData("inside", "occupancy not clear; site=a; presence=inside; away minutes=0")]
    [InlineData("near", "occupancy not clear; site=a; presence=near; away minutes=0")]
    [InlineData("unknown", "occupancy not clear; site=a; presence=unknown; away minutes=0")]
    public void Natural_start_refusals_are_visible_and_do_not_mutate(string gate, string expected)
    {
        var h = new Chapter3RaidSeizureTests.Harness(); var logs = new List<string>();
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: logs.Add);
        switch (gate)
        {
            case "sweep": h.Context.State = h.State with { Sweep = new("s", "a", 1080, false, ActiveStartMinutes: 1080, ActiveEndMinutes: 2520) }; break;
            case "protection": h.Input = h.Input with { Protected = true }; break;
            case "unknown-cover": h.Input = h.Input with { Protected = null }; break;
            case "arrest": h.Input = h.Input with { ArrestTransition = true }; break;
            case "unknown-arrest": h.Input = h.Input with { ArrestTransition = null }; break;
            case "cap": h.Context.State = h.State with { LastRaidSuccessMinutes = 1079 }; break;
            case "restoration": h.World.Restored = false; break;
            case "plan": h.World.Slots = new[] { Chapter3RaidSeizureTests.Harness.Slot(0, 1, 20) }; break;
            case "inside": h.World.Presence = Chapter3RaidPresence.Inside; break;
            case "near": h.World.Presence = Chapter3RaidPresence.Near; break;
            case "unknown": h.World.Presence = Chapter3RaidPresence.Unknown; break;
        }
        if (gate == "plan") h.Away(); else h.Poll();
        Assert.Null(h.State.Raid); Assert.Empty(h.World.Writes); Assert.Empty(h.Coolings);
        Assert.Contains(h.Feature.Status(), line => line.Contains(expected));
        Assert.Contains(expected, logs.Last());
        var count = logs.Count; h.Poll(); Assert.Equal(count, logs.Count);
    }

    [Theory]
    [InlineData("host", "active host Chapter 3 required")]
    [InlineData("clock", "clock unavailable")]
    [InlineData("expired", "queued raid expired")]
    [InlineData("missing", "unknown site 'a'")]
    [InlineData("empty", "no removable package/plan; no eligible site")]
    [InlineData("duplicate", "site census unavailable")]
    [InlineData("throw", "site census unavailable")]
    [InlineData("slot-changed", "no removable package/plan; slot read unavailable or changed")]
    public void Queued_failure_after_acceptance_has_reason_and_one_line(string failure, string expected)
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Input = h.Input with { Tier = LocalPressureTier.Watched };
        var logs = new List<string>(); var world = new CensusWorld(h.World);
        h.Feature = new(h.Context, world, h.World, () => h.Input, _ => true, report: logs.Add);
        Assert.True(h.Feature.Queue(failure == "empty" ? null : "a", true).Ok);
        switch (failure)
        {
            case "host": h.Context.Phase = Chapter3RuntimePhase.Saving; break;
            case "clock": h.Input = h.Input with { Minutes = null }; break;
            case "expired": h.Input = h.Input with { Minutes = 1380 }; break;
            case "missing": case "empty": world.Mode = "empty"; break;
            default: world.Mode = failure; break;
        }
        h.Poll(); Assert.Null(h.State.Raid); Assert.Empty(h.World.Writes);
        Assert.Single(logs); Assert.Contains(expected, logs[0]);
        Assert.Contains(h.Feature.Status(), line => line.Contains(expected));
        h.Poll(); Assert.Single(logs);
    }

    [Theory]
    [InlineData("host")] [InlineData("clock")] [InlineData("duplicate")] [InlineData("throw")]
    public void Immediate_queue_failure_is_logged_and_retained(string failure)
    {
        var h = new Chapter3RaidSeizureTests.Harness(); var logs = new List<string>(); var world = new CensusWorld(h.World);
        h.Feature = new(h.Context, world, h.World, () => h.Input, _ => true, report: logs.Add);
        if (failure == "host") h.Context.Phase = Chapter3RuntimePhase.Saving;
        else if (failure == "clock") h.Input = h.Input with { Minutes = double.NaN };
        else world.Mode = failure;
        var result = h.Feature.Queue("a", true);
        Assert.False(result.Ok); Assert.Single(logs); Assert.Equal(result.Message, logs[0]);
        Assert.Contains(result.Message, h.Feature.Status());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Previous_receipts_and_ambiguous_holds_report_without_replacing_journal(bool ambiguous)
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Away();
        var raid = h.State.Raid!;
        var slots = raid.Slots.Select((s, i) => s with { State = ambiguous ? Chapter3RaidRemovalState.Ambiguous :
            i == 0 ? Chapter3RaidRemovalState.Confirmed : Chapter3RaidRemovalState.Planned }).ToArray();
        h.Context.State = h.State with { Raid = raid with { Completed = true, Slots = slots, CoolingApplied = true } };
        var original = h.State.Raid;
        var logs = new List<string>();
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: logs.Add);
        Assert.True(h.Feature.Queue("a", true).Ok); h.Poll();
        var reason = ambiguous ? "previous raid ambiguous hold" : "previous raid cooling/notice pending";
        Assert.Single(logs); Assert.Contains(reason, logs[0]);
        Assert.Contains(h.Feature.Status(), line => line.Contains(reason));
        Assert.Same(original, h.State.Raid); Assert.Empty(h.World.Writes);
    }

    [Fact]
    public void Normal_logging_policy_keeps_refusals_visible_and_logger_failure_cannot_admit_a_raid()
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Input = h.Input with { Protected = true };
        var visible = new List<string>(); var policy = new OrganizedCrime.Runtime.OcLoggingPolicy(_ => throw new Exception(), visible.Add);
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: policy.Warning);
        Assert.True(h.Feature.Queue("a", true).Ok); h.Poll(); Assert.Single(visible);
        Assert.Contains("protection active", visible[0]);
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: _ => throw new Exception());
        Assert.True(h.Feature.Queue("a", true).Ok); h.Poll(); Assert.Null(h.State.Raid);
        Assert.Contains(h.Feature.Status(), line => line.Contains("protection active"));
        h.Feature.ResetTransient(); Assert.DoesNotContain(h.Feature.Status(), line => line.Contains("raid refused:"));
    }

    private sealed class CensusWorld : IChapter3RaidWorld
    {
        private readonly Chapter3RaidSeizureTests.World _inner;
        internal string? Mode;
        internal CensusWorld(Chapter3RaidSeizureTests.World inner) => _inner = inner;
        public bool IsHost => true;
        public void Reset() { }
        public IReadOnlyList<Chapter3RaidSite> Read(Chapter3RaidExclusions exclusions)
        {
            if (Mode == "throw") throw new InvalidOperationException();
            if (Mode == "empty") return Array.Empty<Chapter3RaidSite>();
            var sites = _inner.Read(exclusions);
            if (Mode == "duplicate") return new[] { sites[0], sites[0] };
            if (Mode == "slot-changed") _inner.SetQuantity(0, 4);
            return sites;
        }
    }
    [Fact]
    public void Window_close_replaces_natural_occupancy_wait_but_retains_a_consumed_QA_refusal()
    {
        var h = new Chapter3RaidSeizureTests.Harness(); var logs = new List<string>();
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: logs.Add);
        h.World.Presence = Chapter3RaidPresence.Inside; h.Poll();
        h.Input = h.Input with { Minutes = 1380 }; h.Poll();
        Assert.Contains(h.Feature.Status(), line => line.Contains("outside 18:00-23:00"));
        h.Input = h.Input with { Protected = true };
        Assert.True(h.Feature.Queue("a", true).Ok); h.Poll(); h.Poll();
        Assert.Contains(h.Feature.Status(), line => line.Contains("protection active"));
        Assert.Equal(3, logs.Count);
    }

    [Fact]
    public void Cap_remaining_minutes_refresh_without_spam_and_new_QA_attempt_logs_again()
    {
        var h = new Chapter3RaidSeizureTests.Harness(); var logs = new List<string>();
        h.Context.State = h.State with { LastRaidSuccessMinutes = 1079 };
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: logs.Add);
        h.Poll(); h.Input = h.Input with { Minutes = 1081 }; h.Poll();
        Assert.Single(logs); Assert.Contains(h.Feature.Status(), line => line.Contains("remaining minutes=10078"));
        Assert.True(h.Feature.Queue("a", true).Ok); h.Poll(); Assert.Equal(2, logs.Count);
    }
    [Fact]
    public void Save_suspension_reports_cancelled_queue_without_changing_lifecycle_gates()
    {
        var h = new Chapter3RaidSeizureTests.Harness(); var logs = new List<string>();
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: logs.Add);
        Assert.True(h.Feature.Queue("a", true).Ok); h.Feature.Suspend(); h.Poll();
        Assert.Single(logs); Assert.Contains("raid suspended for save/load", logs[0]);
        Assert.Contains(h.Feature.Status(), line => line.Contains("raid suspended for save/load"));
        Assert.Null(h.State.Raid); Assert.Empty(h.World.Writes);
    }
    [Fact]
    public void Queued_start_waiting_for_existing_raid_reports_once()
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Away(); h.World.Restored = false;
        var logs = new List<string>();
        h.Feature = new(h.Context, h.World, h.World, () => h.Input, _ => true, report: logs.Add);
        Assert.True(h.Feature.Queue("a", true).Ok); h.Poll(); h.Poll();
        Assert.Single(logs); Assert.Contains("raid already in progress", logs[0]);
        Assert.Contains(h.Feature.Status(), line => line.Contains("raid already in progress"));
        Assert.Empty(h.World.Writes);
    }
}
