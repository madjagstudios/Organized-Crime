using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-63 Part 3a: the once per game minute throttle on the two convergence passes that read the
/// whole world every frame. Keep the Lights Off resolves every owned property's employees and
/// production stations on every census (OC-65 Task 4 moves this off the dead drop and hold room
/// read the mission used before), and The Envelope (redesigned onto the closet in OC-61) resolves
/// the hold room plus every occupied closet slot's cash on every deposit reconciliation, both
/// through IL2CPP. Those passes are throttled to one per game minute, and every lifecycle boundary
/// still converges on its own pass.
/// </summary>
public sealed class Release1ConvergenceThrottleTests
{
    [Fact]
    public void A_second_keep_the_lights_off_update_in_the_same_game_minute_reads_no_world_state()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        harness.World.TotalMinutes = 6_000d;

        harness.Service.Update();
        Assert.True(harness.World.TotalWorldReadsExcludingClock > 0);

        harness.World.ResetReadCounters();
        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(0, harness.World.TotalWorldReadsExcludingClock);
    }

    [Fact]
    public void The_next_game_minute_lets_the_keep_the_lights_off_census_read_again()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        harness.World.TotalMinutes = 6_000d;
        harness.Service.Update();
        harness.World.ResetReadCounters();

        harness.World.TotalMinutes = 6_001d;
        harness.Service.Update();

        Assert.True(harness.World.ProductionActivityReads > 0);
    }

    [Fact]
    public void A_sub_minute_clock_advance_never_lets_the_keep_the_lights_off_census_read_again()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        harness.World.TotalMinutes = 6_000d;
        harness.Service.Update();
        harness.World.ResetReadCounters();

        harness.World.TotalMinutes = 6_000.75d;
        harness.Service.Update();

        Assert.Equal(0, harness.World.TotalWorldReadsExcludingClock);
    }

    [Fact]
    public void Every_keep_the_lights_off_boundary_pass_reads_the_world_inside_the_same_game_minute()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        harness.World.TotalMinutes = 6_000d;
        harness.Service.Update();

        harness.World.ResetReadCounters();
        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        Assert.True(harness.World.ProductionActivityReads > 0);

        // And the Update that follows a boundary is not throttled by the pass the boundary ran.
        harness.World.ResetReadCounters();
        harness.Service.Update();
        Assert.True(harness.World.ProductionActivityReads > 0);
    }

    [Fact]
    public void A_keep_the_lights_off_load_boundary_reads_the_world_and_clears_the_throttle()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        harness.World.TotalMinutes = 6_000d;
        harness.Service.Update();

        harness.Service.OnPreLoad();
        harness.World.ResetReadCounters();
        harness.Service.OnLoadComplete();

        Assert.True(harness.World.ProductionActivityReads > 0);
    }

    [Fact]
    public void A_story_revision_change_inside_one_game_minute_still_converges_keep_the_lights_off()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        harness.World.TotalMinutes = 6_000d;
        harness.Service.Update();
        harness.World.ResetReadCounters();

        // A durable transition from anywhere else in the mod moves the story on inside the same
        // game minute; the throttle must not hide that from the census.
        harness.RecordNellAcceptedReceipt();
        harness.Service.Update();

        Assert.True(harness.World.TotalWorldReadsExcludingClock > 0);
    }

    [Fact]
    public void A_second_the_envelope_update_in_the_same_game_minute_reads_no_world_state()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        harness.World.TotalMinutes = 6_000d;

        harness.Service.Update();
        Assert.True(harness.World.TotalWorldReadsExcludingClock > 0);

        harness.World.ResetReadCounters();
        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(0, harness.World.TotalWorldReadsExcludingClock);
    }

    [Fact]
    public void A_the_envelope_load_boundary_reads_the_world_and_clears_the_throttle()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        harness.World.TotalMinutes = 6_000d;
        harness.Service.Update();

        harness.Service.OnPreLoad();
        harness.World.ResetReadCounters();
        harness.Service.OnLoadComplete();

        Assert.True(harness.World.TotalWorldReadsExcludingClock > 0);
    }

    // OC-61 Task 2 moved The Envelope's deposit reconciliation off the dead drop it used to resolve
    // here onto the hold room and its closets' cash slots; these two restore the throttle coverage
    // Task 2 deleted, now pinned to the closet reads the redesigned deposit actually makes.
    [Fact]
    public void A_second_the_envelope_update_in_the_same_game_minute_makes_no_room_or_cash_reads()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        harness.World.SetClosetCash(closetA, 0, 1000f);
        harness.World.TotalMinutes = 6_000d;

        // The first pass records the shortfall, a durable write that bumps the story revision; the
        // throttle keys on revision as well as game minute, so a second pass in the same minute right
        // behind a revision bump is not itself throttled. Priming with that second pass here (it
        // re-observes the same shortfall and writes nothing further) lets the throttle's own
        // bookkeeping settle before the assertion window below.
        harness.Service.Update();
        harness.Service.Update();
        Assert.True(harness.World.HoldRoomReads > 0);
        Assert.True(harness.World.ClosetCashSlotReads > 0);

        harness.World.ResetReadCounters();
        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(0, harness.World.HoldRoomReads);
        Assert.Equal(0, harness.World.ClosetCashSlotReads);
    }

    [Fact]
    public void Every_the_envelope_boundary_pass_reads_the_closet_inside_the_same_game_minute()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        harness.World.SetClosetCash(closetA, 0, 1000f);
        harness.World.TotalMinutes = 6_000d;
        harness.Service.Update();

        harness.World.ResetReadCounters();
        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        Assert.True(harness.World.HoldRoomReads > 0);

        // And the Update that follows a boundary is not throttled by the pass the boundary ran.
        harness.World.ResetReadCounters();
        harness.Service.Update();
        Assert.True(harness.World.HoldRoomReads > 0);
    }

    [Fact]
    public void The_throttle_never_delays_a_lapsed_keep_the_lights_off_deadline_past_its_game_minute()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        harness.World.TotalMinutes = 6_000d;
        harness.Service.Update();

        // The deadline lapses in a later game minute: the first Update in that minute records it.
        harness.World.TotalMinutes = 200d * 60d;
        harness.Service.Update();

        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
    }
}
