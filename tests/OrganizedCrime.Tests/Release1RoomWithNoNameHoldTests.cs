using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1RoomWithNoNameHoldTests
{
    [Fact]
    public void The_first_observed_stow_records_the_closet_and_the_time_in_memory_only()
    {
        using var harness = Release1RoomWithNoNameHarness.InCustody();
        harness.World.TotalMinutes = 6_000d;
        harness.Repository.ResetEvidence();

        harness.World.StowIntoCloset(harness.Assignment, Release1RoomWithNoNameHarness.ClosetA);
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Stowed, harness.Service.ReconcileHold());

        var progress = harness.Progress()!;
        Assert.True(progress.Stowed);
        Assert.Equal(Release1RoomWithNoNameHarness.ClosetA, progress.HoldingClosetGuid);
        Assert.Equal(6_000d, progress.StowedAtGameMinutes);
        Assert.Null(progress.MissingSincePassGameMinutes);
        Assert.Empty(harness.Repository.Updates);

        Release1RoomWithNoNameHarness.Save(harness);
        Assert.True(harness.Repository.StoredState!.RoomWithNoNameProgress.Single().Stowed);
    }

    [Fact]
    public void The_stow_is_recorded_once_and_the_window_never_restarts_while_it_stays_in_the_room()
    {
        using var harness = Release1RoomWithNoNameHarness.Stowed();
        var stowedAt = harness.Progress()!.StowedAtGameMinutes;

        harness.World.TotalMinutes += 300d;
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Holding, harness.Service.ReconcileHold());

        Assert.Equal(stowedAt, harness.Progress()!.StowedAtGameMinutes);
    }

    [Fact]
    public void Moving_the_consignment_between_closets_never_fails_the_hold_and_refreshes_the_guid()
    {
        using var harness = Release1RoomWithNoNameHarness.Stowed();
        var stowedAt = harness.Progress()!.StowedAtGameMinutes;

        harness.World.EmptyCloset(Release1RoomWithNoNameHarness.ClosetA);
        harness.World.StowIntoCloset(harness.Assignment, Release1RoomWithNoNameHarness.ClosetB);
        harness.World.TotalMinutes += 10d;

        Assert.Equal(Release1RoomWithNoNameHoldStatus.Holding, harness.Service.ReconcileHold());
        Assert.Equal(Release1RoomWithNoNameHarness.ClosetB, harness.Progress()!.HoldingClosetGuid);
        Assert.Equal(stowedAt, harness.Progress()!.StowedAtGameMinutes);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
    }

    [Fact]
    public void One_missing_pass_inside_the_grace_never_fails_the_hold()
    {
        using var harness = Release1RoomWithNoNameHarness.Stowed();
        harness.World.EmptyCloset(Release1RoomWithNoNameHarness.ClosetA);

        harness.World.TotalMinutes += 10d;
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Missing, harness.Service.ReconcileHold());
        Assert.NotNull(harness.Progress()!.MissingSincePassGameMinutes);

        harness.World.TotalMinutes += 30d;
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Missing, harness.Service.ReconcileHold());
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
    }

    [Fact]
    public void Returning_the_consignment_inside_the_grace_clears_the_miss()
    {
        using var harness = Release1RoomWithNoNameHarness.Stowed();
        harness.World.EmptyCloset(Release1RoomWithNoNameHarness.ClosetA);
        harness.World.TotalMinutes += 10d;
        harness.Service.ReconcileHold();

        harness.World.StowIntoCloset(harness.Assignment, Release1RoomWithNoNameHarness.ClosetA);
        harness.World.TotalMinutes += 20d;
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Holding, harness.Service.ReconcileHold());

        Assert.Null(harness.Progress()!.MissingSincePassGameMinutes);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
    }

    [Fact]
    public void Two_consecutive_missing_passes_sixty_minutes_apart_fail_the_hold_once()
    {
        using var harness = Release1RoomWithNoNameHarness.Stowed();
        var standingBefore = harness.Story.State!.Standing;
        harness.World.EmptyCloset(Release1RoomWithNoNameHarness.ClosetA);

        harness.World.TotalMinutes += 10d;
        harness.Service.ReconcileHold();
        harness.World.TotalMinutes += 60d;
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Failed, harness.Service.ReconcileHold());
        Assert.Equal(Release1RoomWithNoNameHoldStatus.NoWork, harness.Service.ReconcileHold());

        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        Assert.Equal(standingBefore - 12, harness.Story.State!.Standing);
        Assert.Single(harness.Mission().PenaltyReceiptIds);
    }

    [Fact]
    public void A_make_good_hold_miss_applies_minus_eight_and_exposes_recovery()
    {
        using var harness = Release1RoomWithNoNameHarness.MakeGoodStowed();
        var standingBefore = harness.Story.State!.Standing;
        harness.World.EmptyCloset(Release1RoomWithNoNameHarness.ClosetA);

        harness.World.TotalMinutes += 10d;
        harness.Service.ReconcileHold();
        harness.World.TotalMinutes += 60d;
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Failed, harness.Service.ReconcileHold());

        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);
        Assert.Equal(standingBefore - 8, harness.Story.State!.Standing);
    }

    [Fact]
    public void A_recovery_hold_miss_re_offers_recovery_and_advances_the_attempt_without_a_penalty()
    {
        using var harness = Release1RoomWithNoNameHarness.RecoveryStowed();
        var standingBefore = harness.Story.State!.Standing;
        var attemptBefore = harness.Mission().Attempt;
        harness.World.EmptyCloset(Release1RoomWithNoNameHarness.ClosetA);

        harness.World.TotalMinutes += 10d;
        harness.Service.ReconcileHold();
        harness.World.TotalMinutes += 60d;
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Failed, harness.Service.ReconcileHold());

        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);
        Assert.Equal(attemptBefore + 1, harness.Mission().Attempt);
        Assert.Equal(standingBefore, harness.Story.State!.Standing);
        Assert.Equal(Release1MissionOutcome.RecoveryFailure, harness.Mission().LastOutcome);
    }

    [Fact]
    public void A_re_offered_recovery_stages_exactly_once_on_the_newly_accepted_attempt()
    {
        using var harness = Release1RoomWithNoNameHarness.RecoveryStowed();
        harness.World.EmptyCloset(Release1RoomWithNoNameHarness.ClosetA);
        harness.World.TotalMinutes += 10d;
        harness.Service.ReconcileHold();
        harness.World.TotalMinutes += 60d;
        harness.Service.ReconcileHold();
        harness.World.ResetAllDropsToEmpty();
        harness.World.ResetMutationEvidence();

        Assert.Equal(Release1RoomWithNoNameReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1RoomWithNoNameDecisionStatus.Accepted, harness.Service.TryAccept().Status);

        Assert.Equal(1, harness.World.InsertCount);
        harness.Service.ReconcileStaging();
        harness.Service.ReconcileStaging();
        Assert.Equal(1, harness.World.InsertCount);
    }

    [Fact]
    public void The_hold_is_satisfied_once_when_the_window_expires()
    {
        using var harness = Release1RoomWithNoNameHarness.Stowed();
        harness.World.TotalMinutes += 1_440d;

        Assert.Equal(Release1RoomWithNoNameHoldStatus.Released, harness.Service.ReconcileHold());
        Assert.Equal(Release1RoomWithNoNameHoldStatus.NoWork, harness.Service.ReconcileHold());

        Assert.True(harness.Progress()!.HoldSatisfied);
    }

    [Fact]
    public void Taking_the_consignment_out_after_the_release_is_not_a_miss()
    {
        using var harness = Release1RoomWithNoNameHarness.Released();
        harness.World.EmptyCloset(Release1RoomWithNoNameHarness.ClosetA);

        harness.World.TotalMinutes += 500d;
        Assert.Equal(Release1RoomWithNoNameHoldStatus.NoWork, harness.Service.ReconcileHold());
        Assert.Equal(Release1RoomWithNoNameHoldStatus.NoWork, harness.Service.ReconcileHold());

        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
    }

    [Fact]
    public void An_unready_room_holds_with_one_status_line_and_never_fails_the_hold()
    {
        using var harness = Release1RoomWithNoNameHarness.Stowed();
        harness.World.HoldRoomReadiness = Release1HoldRoomReadiness.NotReady;

        harness.World.TotalMinutes += 5_000d;
        Assert.Equal(Release1RoomWithNoNameHoldStatus.RoomNotReady, harness.Service.ReconcileHold());
        Assert.Equal(Release1RoomWithNoNameHoldStatus.RoomNotReady, harness.Service.ReconcileHold());

        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
        Assert.False(harness.Progress()!.HoldSatisfied);
        Assert.Single(harness.Logs, line => line.Contains("the hold room was not readable", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_matching_consignments_in_the_room_hold_and_change_nothing()
    {
        using var harness = Release1RoomWithNoNameHarness.Stowed();
        harness.World.StowIntoCloset(harness.Assignment, Release1RoomWithNoNameHarness.ClosetB);

        harness.World.TotalMinutes += 1_440d;
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Ambiguous, harness.Service.ReconcileHold());

        Assert.False(harness.Progress()!.HoldSatisfied);
    }

    [Fact]
    public void The_hold_writes_no_native_effect_journal_entry()
    {
        using var harness = Release1RoomWithNoNameHarness.Stowed();
        harness.World.TotalMinutes += 1_440d;

        harness.Service.ReconcileHold();

        Assert.DoesNotContain(harness.Story.State!.NativeEffects, effect => effect.MissionKey == Release1MissionCatalog.RoomWithNoName);
    }

    // A ClosetMutations == 0 counter assertion can never fail: nothing on IRelease1SmallCourtesyWorld
    // writes a closet, so the counter can never be incremented by production code in the first place.
    // Assert the stronger, actually-failable claim directly: the contract itself exposes no member
    // that could write closet contents. Any method that mutates the world reports it through
    // Release1SmallCourtesyWorldMutationStatus, so none of those methods may name a closet.
    [Fact]
    public void The_small_courtesy_world_contract_exposes_no_member_that_writes_a_closet()
    {
        var mutatingMembers = typeof(IRelease1SmallCourtesyWorld).GetMethods()
            .Where(method => method.ReturnType == typeof(Release1SmallCourtesyWorldMutationStatus));

        Assert.All(mutatingMembers, method =>
            Assert.DoesNotContain("closet", method.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_unsaved_quit_after_the_stow_restarts_the_window_on_the_next_stow()
    {
        var repository = new Release1RoomWithNoNameHarness.FakeRepository();
        var world = new Release1RoomWithNoNameHarness.FakeWorld();
        double firstStowedAt;
        using (var first = Release1RoomWithNoNameHarness.Stowed(repository, world))
        {
            firstStowedAt = first.Progress()!.StowedAtGameMinutes!.Value;
            Assert.True(first.Progress()!.Stowed);

            first.World.RevertToLastSave();
            // The brick itself was never saved either, so it must actually be gone from the closet,
            // not merely forgotten by the mission's own in-memory progress.
            Assert.DoesNotContain(
                first.World.ClosetSlots[Release1RoomWithNoNameHarness.ClosetA].Values,
                slot => slot.Quantity > 0);

            first.Service.OnPreLoad();
        }

        using var restored = Release1RoomWithNoNameHarness.Load(repository, world, clearSlots: true);
        Assert.False(restored.Progress()!.Stowed);

        restored.World.TotalMinutes += 10d;
        restored.World.StowIntoCloset(restored.Assignment, Release1RoomWithNoNameHarness.ClosetA);
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Stowed, restored.Service.ReconcileHold());
        Assert.NotEqual(firstStowedAt, restored.Progress()!.StowedAtGameMinutes);
    }

    [Fact]
    public void A_saved_stow_continues_the_hold_from_the_persisted_stow_time()
    {
        var repository = new Release1RoomWithNoNameHarness.FakeRepository();
        var world = new Release1RoomWithNoNameHarness.FakeWorld();
        double stowedAt;
        using (var first = Release1RoomWithNoNameHarness.Stowed(repository, world))
        {
            Release1RoomWithNoNameHarness.Save(first);
            stowedAt = first.Progress()!.StowedAtGameMinutes!.Value;
            first.Service.OnPreLoad();
        }

        using var restored = Release1RoomWithNoNameHarness.Load(repository, world);
        Assert.Equal(stowedAt, restored.Progress()!.StowedAtGameMinutes);
        restored.World.TotalMinutes = stowedAt + 1_440d;
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Released, restored.Service.ReconcileHold());
    }

    // Task 4 review gap: custody persisted by a save must survive OnPreLoad plus a reload, not just
    // the same-session Story-only reload most other tests exercise.
    [Fact]
    public void Custody_persisted_by_a_save_survives_a_full_preload_and_reload()
    {
        var repository = new Release1RoomWithNoNameHarness.FakeRepository();
        var world = new Release1RoomWithNoNameHarness.FakeWorld();
        using (var first = Release1RoomWithNoNameHarness.InCustody(repository, world))
        {
            Assert.True(first.Progress()!.Custody);
            first.Service.OnPreLoad();
        }

        using var restored = Release1RoomWithNoNameHarness.Load(repository, world, clearSlots: false);
        Assert.True(restored.Progress()!.Custody);
    }

    // Task 4 review gap: a staged-only revert (staged, no save, quit) followed by reload must
    // re-stage exactly once, not leave the drop double-inserted or skip the re-stage entirely.
    [Fact]
    public void A_staged_only_revert_followed_by_reload_re_stages_exactly_once()
    {
        var repository = new Release1RoomWithNoNameHarness.FakeRepository();
        var world = new Release1RoomWithNoNameHarness.FakeWorld();
        using (var first = Release1RoomWithNoNameHarness.Active(repository, world))
        {
            Assert.Equal(Release1RoomWithNoNameStageStatus.Staged, first.Service.ReconcileStaging());
            Assert.True(first.Progress()!.Staged);
            // The native insert itself was never saved, so it reverts along with everything else on
            // an unsaved quit; the harness models that the same way Release1RoomWithNoNameStagingTests
            // models a custody revert, by poking the drop back to empty directly.
            first.World.EmptySlot(first.Assignment.SourceDropGuid, 0);
            first.World.RevertToLastSave();
            first.Service.OnPreLoad();
        }

        using var restored = Release1RoomWithNoNameHarness.Load(repository, world, clearSlots: true);
        Assert.Equal(Release1RoomWithNoNameStageStatus.Staged, restored.Service.ReconcileStaging());
        Assert.Equal(1, restored.World.InsertCount);
        restored.Service.ReconcileStaging();
        Assert.Equal(1, restored.World.InsertCount);
    }

    // Task 5 review gap: ReconcileHold() never checks the mission's own stage deadline, only the hold
    // room. If custody drags on and the consignment is not stowed until close to the 72 hour deadline,
    // the 1440 minute hold window can finish after that deadline has already passed. The host drives
    // Update() every tick, and Update() checks the deadline before it ever converges the hold, so the
    // mission must fail as RequiredFailure there rather than quietly reading Released.
    [Fact]
    public void A_hold_that_would_satisfy_after_the_stage_deadline_fails_the_mission_instead_of_releasing_it()
    {
        using var harness = Release1RoomWithNoNameHarness.InCustody();
        var deadlineMinutes = harness.Mission().DeadlineGameTimeHours!.Value * 60d;

        // Stow just before the deadline, so the hold's own 24 hour window would not finish until well
        // after it.
        harness.World.TotalMinutes = deadlineMinutes - 10d;
        harness.World.StowIntoCloset(harness.Assignment, Release1RoomWithNoNameHarness.ClosetA);
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Stowed, harness.Service.ReconcileHold());

        // Advance past both the hold window and the stage deadline, then run the same Update() pass
        // the host drives every tick.
        harness.World.TotalMinutes = deadlineMinutes + Release1RoomWithNoNameAssignment.OneInGameDayMinutes + 10d;
        harness.Service.Update();

        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        Assert.False(harness.Progress()!.HoldSatisfied);
    }
}
