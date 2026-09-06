using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// Task 4: the shutdown census reconciliation, window start, breach handling, and the migration
/// recovery. Covers every row of the spec's production census table through
/// <see cref="Release1KeepTheLightsOffMissionService.ReconcileCensus"/>, reusing the offer/accept
/// harness from <see cref="Release1KeepTheLightsOffMissionServiceTests"/> so every stage transition
/// and every mission-key/attempt wiring stays identical to the rest of the suite.
/// </summary>
public sealed class Release1KeepTheLightsOffCensusTests
{
    private const double WindowMinutes = Release1KeepTheLightsOffAssignment.WindowGameMinutes;

    // Mirrors the production Release1KeepTheLightsOffMissionService.BreachGraceGameMinutes constant,
    // which is private to the service; the value is shared engine tuning (also 60d in
    // Release1QuietWindow.BreachGraceGameMinutes and Release1HoldWindow.MissingGraceGameMinutes) and
    // has no reason to vary by condition.
    private const double GraceMinutes = 60d;

    [Fact]
    public void A_working_employee_holds_the_window_closed()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        Release1KeepTheLightsOffHarness.SetWorkingWorld(harness);

        var status = harness.Service.ReconcileCensus();

        Assert.Equal(Release1KeepTheLightsOffCensusStatus.Holding, status);
        Assert.Null(Progress(harness));
    }

    [Fact]
    public void The_first_quiet_pass_confirms_the_shutdown_and_starts_the_window()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness);
        harness.World.TotalMinutes = 5_000d;

        var status = harness.Service.ReconcileCensus();

        Assert.Equal(Release1KeepTheLightsOffCensusStatus.WindowStarted, status);
        var progress = Progress(harness)!;
        Assert.Equal(5_000d, progress.ClearConfirmedAtGameMinutes);
        Assert.Null(progress.BreachSincePassGameMinutes);
    }

    [Fact]
    public void A_breach_inside_the_grace_never_fails_the_stage()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness);
        harness.World.TotalMinutes = 5_000d;
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.WindowStarted, harness.Service.ReconcileCensus());

        Release1KeepTheLightsOffHarness.SetWorkingWorld(harness);
        harness.World.TotalMinutes = 5_050d;
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.BreachRecorded, harness.Service.ReconcileCensus());
        Assert.Equal(5_050d, Progress(harness)!.BreachSincePassGameMinutes);

        Release1KeepTheLightsOffHarness.SetQuietWorld(harness);
        harness.World.TotalMinutes = 5_059d;
        var status = harness.Service.ReconcileCensus();

        Assert.Equal(Release1KeepTheLightsOffCensusStatus.Holding, status);
        var progress = Progress(harness)!;
        Assert.Null(progress.BreachSincePassGameMinutes);
        Assert.Equal(5_000d, progress.ClearConfirmedAtGameMinutes);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
    }

    [Fact]
    public void A_breach_past_the_grace_fails_once_with_the_shipped_receipt()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        var standingBefore = harness.Story.State!.Standing;
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness);
        harness.World.TotalMinutes = 5_000d;
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.WindowStarted, harness.Service.ReconcileCensus());

        Release1KeepTheLightsOffHarness.SetWorkingWorld(harness);
        harness.World.TotalMinutes = 5_050d;
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.BreachRecorded, harness.Service.ReconcileCensus());

        harness.World.TotalMinutes = 5_050d + GraceMinutes;
        var status = harness.Service.ReconcileCensus();

        Assert.Equal(Release1KeepTheLightsOffCensusStatus.Failed, status);
        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.RequiredFailure, harness.Mission().LastOutcome);
        Assert.Equal(standingBefore - 12, harness.Story.State!.Standing);
        var receipt = Assert.Single(harness.Mission().PenaltyReceiptIds);
        Assert.Equal("keep-the-lights-off-breach-primary-v1-a1", receipt);

        // The stage already left Active; no make-good assignment exists yet, so a further pass finds
        // no active stage to reconcile at all, rather than failing a second time.
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.NoWork, harness.Service.ReconcileCensus());
    }

    // A station's own fingerprint diff is a one-time signal: an unchanged running station reads Quiet
    // on the very next pass (a genuinely in-progress cook is allowed to keep cooking). Sustaining a
    // Breached read past the grace, the way an actually-working employee does for free, needs a
    // second, distinct operation starting, exactly what a player restarting a cook after Nell's first
    // warning would produce.
    [Fact]
    public void A_station_starting_a_new_operation_past_the_grace_fails_the_stage()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness);
        harness.World.TotalMinutes = 5_000d;
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.WindowStarted, harness.Service.ReconcileCensus());

        Release1KeepTheLightsOffHarness.SetRunningStation(harness, "og", 1);
        harness.World.TotalMinutes = 5_050d;
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.BreachRecorded, harness.Service.ReconcileCensus());

        Release1KeepTheLightsOffHarness.SetRunningStation(harness, "sour", 1);
        harness.World.TotalMinutes = 5_050d + GraceMinutes;
        var status = harness.Service.ReconcileCensus();

        Assert.Equal(Release1KeepTheLightsOffCensusStatus.Failed, status);
    }

    [Fact]
    public void An_operation_already_running_at_acceptance_may_finish()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        Release1KeepTheLightsOffHarness.SetRunningStation(harness, "og", 10);
        harness.World.TotalMinutes = 6_000d;
        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ready, harness.Service.TryReview().Status);

        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, harness.Service.TryAccept().Status);

        // TryAccept's own internal ReconcileCensus call is the first pass after acceptance: a
        // baseline pass, so a station already running when the world loaded reads Quiet rather than
        // Working, and the window opens immediately rather than holding.
        Assert.Equal(6_000d, Progress(harness)!.ClearConfirmedAtGameMinutes);

        Release1KeepTheLightsOffHarness.SetRunningStation(harness, "og", 40);
        harness.World.TotalMinutes = 6_010d;
        var status = harness.Service.ReconcileCensus();

        Assert.Equal(Release1KeepTheLightsOffCensusStatus.Holding, status);
    }

    [Fact]
    public void Every_lifecycle_boundary_arms_the_next_pass_as_a_baseline_pass()
    {
        using var harness = Release1KeepTheLightsOffHarness.Active();
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness);
        harness.World.TotalMinutes = 5_000d;
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.WindowStarted, harness.Service.ReconcileCensus());

        // Save start then save complete: OnSaveComplete's own internal pass is the very next pass
        // after the save boundary, and must read a station that was running the whole time as
        // baseline rather than a fresh violation.
        Release1KeepTheLightsOffHarness.SetRunningStation(harness, "og", 1);
        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        Assert.Null(Progress(harness)!.BreachSincePassGameMinutes);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);

        // Pre load then load complete: OnLoadComplete's own internal pass is the next pass after the
        // reload boundary; OnPreLoad also clears the in memory fingerprint history, so this exercises
        // both effects together.
        Release1KeepTheLightsOffHarness.SetRunningStation(harness, "sour", 1);
        harness.Service.OnPreLoad();
        harness.Service.OnLoadComplete();
        Assert.Null(Progress(harness)!.BreachSincePassGameMinutes);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);

        // An accepted decision: driving the primary stage to a required failure, then accepting the
        // make good stage while a station is already running, proves acceptance re-arms the baseline
        // pass for a later stage too. If the running station were misread as Working (non-baseline),
        // the fresh attempt's progress would stay null (Holding); reading it as the baseline Quiet it
        // is opens the window immediately instead.
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness);
        harness.World.TotalMinutes = 10_320d; // exactly the primary deadline; RequiredFailure offers make good
        harness.Service.Update();
        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);

        Release1KeepTheLightsOffHarness.SetRunningStation(harness, "batch", 1);
        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, harness.Service.TryAccept().Status);

        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Mission().State);
        Assert.Equal(10_320d, Progress(harness)!.ClearConfirmedAtGameMinutes);
    }

    [Fact]
    public void A_property_count_mismatch_holds_with_its_own_reason_and_writes_nothing()
    {
        using var harness = Release1KeepTheLightsOffHarness.Offered();
        Release1KeepTheLightsOffHarness.SetWorkingWorld(harness, properties: 2);
        harness.World.TotalMinutes = 6_000d;
        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Null(Progress(harness));

        // The world now reports three owned properties, a layout change mid attempt against the two
        // frozen at acceptance: the census must hold on the mismatch, distinct from a genuinely
        // unready read, and never confuse the two in its own reported line.
        Release1KeepTheLightsOffHarness.SetQuietWorld(harness, properties: 3);
        var status = harness.Service.ReconcileCensus();

        Assert.Equal(Release1KeepTheLightsOffCensusStatus.NotReady, status);
        Assert.Null(Progress(harness));
        var mismatchLine = Assert.Single(harness.Logs);
        Assert.Contains("owned property count", mismatchLine, StringComparison.Ordinal);

        harness.World.ActivityStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;
        var unreadyStatus = harness.Service.ReconcileCensus();

        Assert.Equal(Release1KeepTheLightsOffCensusStatus.NotReady, unreadyStatus);
        var unreadyLine = harness.Logs.Last();
        Assert.DoesNotContain("owned property count", unreadyLine, StringComparison.Ordinal);
    }

    [Fact]
    public void A_migrated_active_attempt_restores_its_assignment_on_the_first_pass()
    {
        var repository = new Release1KeepTheLightsOffHarness.FakeRepository();
        var world = new Release1KeepTheLightsOffHarness.FakeWorld();
        Release1KeepTheLightsOffAssignment originalAssignment;
        int standingBefore;
        using (var first = Release1KeepTheLightsOffHarness.Active(repository, world))
        {
            originalAssignment = first.Assignment;
            standingBefore = first.Story.State!.Standing;
            // Simulates the v9 to v10 migration losing the assignment row: the mission stays Active
            // with its acceptance correlation on record, but the assignment collection comes back
            // empty, exactly the shape Release1KeepTheLightsOffStoryTests' own MigratedActiveHarness
            // reproduces through the real codec's v9 read path.
            var migrated = first.Story.State! with
            {
                KeepTheLightsOffAssignments = Array.Empty<Release1KeepTheLightsOffAssignment>()
            };
            repository.Update(migrated);
            first.Service.OnPreLoad();
        }

        using var restored = Release1KeepTheLightsOffHarness.Load(repository, world);
        var status = restored.Service.ReconcileCensus();

        Assert.NotEqual(Release1KeepTheLightsOffCensusStatus.NoWork, status);
        var restoredAssignment = Assert.Single(restored.Story.State!.KeepTheLightsOffAssignments);
        Assert.Equal(originalAssignment.Attempt, restoredAssignment.Attempt);
        Assert.Equal(originalAssignment.Mode, restoredAssignment.Mode);
        Assert.Equal(originalAssignment.ExpectedOwnedPropertyCount, restoredAssignment.ExpectedOwnedPropertyCount);
        Assert.Equal(Release1MissionState.Active, restored.Mission().State);
        Assert.Equal(standingBefore, restored.Story.State!.Standing);

        // The pass then proceeds normally: a later quiet pass still opens the window.
        Release1KeepTheLightsOffHarness.SetQuietWorld(restored);
        restored.World.TotalMinutes = 8_000d;
        Assert.Equal(Release1KeepTheLightsOffCensusStatus.WindowStarted, restored.Service.ReconcileCensus());
    }

    private static Release1KeepTheLightsOffProgress? Progress(Release1KeepTheLightsOffHarness.Harness harness) =>
        harness.Story.State!.KeepTheLightsOffProgress.SingleOrDefault(progress => progress.Attempt == harness.Mission().Attempt);
}
