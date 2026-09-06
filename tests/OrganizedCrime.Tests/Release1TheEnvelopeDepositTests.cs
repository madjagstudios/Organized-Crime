using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-61 Task 4: the multi slot closet consumption transaction, blocking on the first failure, and
/// completion with no payout. Every money assertion below names the exact per slot pre and post
/// balance, per the task brief. <c>A</c> is <see cref="Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids"/>'s
/// first closet GUID throughout.
/// </summary>
public sealed class Release1TheEnvelopeDepositTests
{
    [Fact]
    public void Five_stacks_of_one_thousand_consume_all_five_and_complete_the_recovery_stage()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, status);
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            Assert.Null(harness.World.ReadClosetCash(closetA, slotIndex));
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Consumption().Phase);
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
    }

    [Fact]
    public void Twenty_stacks_of_one_thousand_consume_all_twenty_at_the_primary_tier()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex < 20; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, status);
        for (var slotIndex = 0; slotIndex < 20; slotIndex++)
            Assert.Null(harness.World.ReadClosetCash(closetA, slotIndex));
        Assert.Equal(20, harness.World.ClosetCashChangeCalls.Count);
        for (var slotIndex = 0; slotIndex < 20; slotIndex++)
        {
            var call = harness.World.ClosetCashChangeCalls[slotIndex];
            Assert.Equal(slotIndex, call.SlotIndex);
            Assert.Equal(-1000f, call.Amount);
        }
    }

    [Fact]
    public void Nineteen_full_stacks_plus_one_partial_consume_all_twenty_at_the_primary_tier()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 18; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        harness.World.SetClosetCash(closetA, 19, 1200f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, status);
        for (var slotIndex = 0; slotIndex <= 18; slotIndex++)
            Assert.Null(harness.World.ReadClosetCash(closetA, slotIndex));
        Assert.Equal(200f, harness.World.ReadClosetCash(closetA, 19));
        Assert.Equal(20, harness.World.ClosetCashChangeCalls.Count);
        for (var slotIndex = 0; slotIndex <= 19; slotIndex++)
        {
            var call = harness.World.ClosetCashChangeCalls[slotIndex];
            Assert.Equal(slotIndex, call.SlotIndex);
            Assert.Equal(-1000f, call.Amount);
        }
    }

    [Fact]
    public void Eleven_stacks_at_the_make_good_tier_leave_exactly_one_stack_of_one_thousand_behind()
    {
        using var harness = Release1TheEnvelopeHarness.MakeGoodActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 10; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, status);
        for (var slotIndex = 0; slotIndex <= 9; slotIndex++)
            Assert.Null(harness.World.ReadClosetCash(closetA, slotIndex));
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 10));
    }

    [Fact]
    public void A_partial_last_stack_takes_only_the_residual_remainder()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 3; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        harness.World.SetClosetCash(closetA, 4, 1200f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, status);
        for (var slotIndex = 0; slotIndex <= 3; slotIndex++)
            Assert.Null(harness.World.ReadClosetCash(closetA, slotIndex));
        Assert.Equal(200f, harness.World.ReadClosetCash(closetA, 4));
        Assert.Equal(5, harness.World.ClosetCashChangeCalls.Count);
        foreach (var call in harness.World.ClosetCashChangeCalls)
            Assert.Equal(-1000f, call.Amount);
    }

    [Fact]
    public void Every_planned_slot_is_locked_before_the_first_decrement()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, harness.Service.ReconcileDeposit());

        Assert.Equal(
            new[] { "lock:0:true", "lock:1:true", "lock:2:true", "lock:3:true", "lock:4:true" },
            harness.World.ClosetMutationLog.Take(5));
        var firstCashChange = harness.World.ClosetMutationLog.FindIndex(entry => entry.StartsWith("cashchange:", StringComparison.Ordinal));
        Assert.Equal(5, firstCashChange);
    }

    [Fact]
    public void A_failure_on_the_third_slot_leaves_the_fourth_and_fifth_untouched_and_blocks_the_effect()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        harness.World.FailClosetChangeAt = (closetA, 2);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Ambiguous, status);
        Assert.Null(harness.World.ReadClosetCash(closetA, 0));
        Assert.Null(harness.World.ReadClosetCash(closetA, 1));
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 2));
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 3));
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 4));
        Assert.Equal(3, harness.World.ClosetCashChangeCalls.Count);
        Assert.True(harness.Consumption().ExecutionBlocked);
        Assert.Equal(Release1MissionState.RecoveryActive, harness.Mission().State);
    }

    [Fact]
    public void A_blocked_effect_is_never_reissued_and_never_retries_a_drained_slot()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        harness.World.FailClosetChangeAt = (closetA, 2);
        Assert.Equal(Release1TheEnvelopeDepositStatus.Ambiguous, harness.Service.ReconcileDeposit());
        var callsAfterFirstPass = harness.World.ClosetCashChangeCalls.Count;

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Ambiguous, status);
        Assert.Equal(callsAfterFirstPass, harness.World.ClosetCashChangeCalls.Count);
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 3));
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 4));
    }

    [Fact]
    public void A_post_read_that_disagrees_with_the_planned_post_balance_blocks_before_the_next_slot()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        harness.World.DriftClosetCashOnce = (closetA, 1, 50f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Ambiguous, status);
        Assert.Null(harness.World.ReadClosetCash(closetA, 0));
        Assert.Equal(50f, harness.World.ReadClosetCash(closetA, 1));
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 2));
        Assert.Equal(2, harness.World.ClosetCashChangeCalls.Count);
    }

    [Fact]
    public void A_lock_failure_on_any_planned_slot_releases_the_locks_taken_and_holds_without_blocking()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        harness.World.RejectClosetLocks = true;

        var status = harness.Service.ReconcileDeposit();

        // OC-61 decision 7: a lock failure takes no slot, so it is never the one irreversible mid
        // plan failure. Every lock taken so far is released, the effect stays Prepared and unblocked
        // (not Ambiguous), and the pass holds so a later pass with locks available can re-run the
        // plan from scratch.
        Assert.Equal(Release1TheEnvelopeDepositStatus.Held, status);
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, slotIndex));
        Assert.Empty(harness.World.ClosetCashChangeCalls);
        Assert.Empty(harness.World.ClosetLocks);
        Assert.Equal(Release1NativeEffectPhase.Prepared, harness.Consumption().Phase);
        Assert.False(harness.Consumption().ExecutionBlocked);
    }

    [Fact]
    public void After_a_lock_failure_the_next_pass_with_locks_available_consumes_exactly_once()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        harness.World.RejectClosetLocks = true;

        Assert.Equal(Release1TheEnvelopeDepositStatus.Held, harness.Service.ReconcileDeposit());
        Assert.Empty(harness.World.ClosetCashChangeCalls);

        harness.World.RejectClosetLocks = false;
        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, status);
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            Assert.Null(harness.World.ReadClosetCash(closetA, slotIndex));
        Assert.Equal(5, harness.World.ClosetCashChangeCalls.Count);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Consumption().Phase);

        // A third pass makes no further decrement: the deposit consumed exactly once.
        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, harness.Service.ReconcileDeposit());
        Assert.Equal(5, harness.World.ClosetCashChangeCalls.Count);
    }

    [Fact]
    public void A_stuck_unlock_from_a_blocked_pass_clears_on_a_later_pass_once_the_release_finally_succeeds()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        harness.World.FailClosetChangeAt = (closetA, 2);
        harness.World.RejectClosetUnlocks = true;

        Assert.Equal(Release1TheEnvelopeDepositStatus.Ambiguous, harness.Service.ReconcileDeposit());
        Assert.True(harness.Consumption().ExecutionBlocked);
        // Every planned slot's lock is still held: the world's own unlock, in ConsumeEnvelope's
        // finally block, failed too, so _lockedClosetGuid stays pinned to closet A.
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            Assert.Contains((closetA, slotIndex), harness.World.ClosetLocks);

        // The world's unlock now works again; the blocked effect itself never calls ConsumeEnvelope a
        // second time (it is never reissued), so it is ReconcileDeposit's own opportunistic retry, not
        // a fresh transaction, that must be what clears the pin here.
        harness.World.RejectClosetUnlocks = false;
        Assert.Equal(Release1TheEnvelopeDepositStatus.Ambiguous, harness.Service.ReconcileDeposit());

        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            Assert.DoesNotContain((closetA, slotIndex), harness.World.ClosetLocks);
    }

    [Fact]
    public void The_consumption_never_reaches_TryChangeSlotQuantity_or_TryChangeDeadDropSlotCashBalance()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);

        // Both members throw unconditionally in the fake (see FakeWorld.TryChangeSlotQuantity and
        // TryChangeDeadDropSlotCashBalance); a pass that reached either would fail this test with the
        // synthetic exception instead of completing normally.
        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, status);
    }

    [Fact]
    public void Completion_pays_no_cash_and_floors_standing_at_eighty_and_recognizes_once()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, harness.Service.ReconcileDeposit());

        Assert.Equal(0, harness.World.WalletCashChangeCalls);
        Assert.Equal(80, harness.Story.State!.Standing);
        Assert.True(harness.Story.State.Release1Recognized);
        Assert.Single(harness.Story.State.RecognitionLogicalCorrelationIds);
    }

    [Fact]
    public void The_effect_commits_after_the_save_against_the_captured_revision()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, harness.Service.ReconcileDeposit());
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Consumption().Phase);

        // Save's own OnSaveComplete reconciliation pass is what actually commits: it persists the
        // Applied effect (catching LastPersistedRevision up to the captured revision) and then, in the
        // same pass, reconciles the now-durable effect straight to Committed.
        Release1TheEnvelopeHarness.Save(harness);

        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Consumption().Phase);
    }

    [Fact]
    public void A_third_pass_after_commit_reports_no_work_instead_of_a_false_awaiting_save()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, harness.Service.ReconcileDeposit());
        Release1TheEnvelopeHarness.Save(harness);
        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Consumption().Phase);

        // A third pass, after Save's own reconciliation already committed the effect, must report the
        // commit's real outcome (there is no more work) rather than reissuing AwaitingAppliedSave as if
        // the commit had not happened.
        Assert.Equal(Release1TheEnvelopeDepositStatus.NoWork, harness.Service.ReconcileDeposit());
        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Consumption().Phase);
    }

    [Fact]
    public void A_corrupted_authorization_on_reload_produces_a_rejected_commit_not_a_false_positive()
    {
        var repository = new Release1TheEnvelopeHarness.FakeRepository();
        var world = new Release1TheEnvelopeHarness.FakeWorld();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        using (var harness = Release1TheEnvelopeHarness.RecoveryActive(repository, world))
        {
            for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
                harness.World.SetClosetCash(closetA, slotIndex, 1000f);
            Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, harness.Service.ReconcileDeposit());
            Assert.Equal(Release1NativeEffectPhase.Applied, harness.Consumption().Phase);

            // Persist the Applied effect the way a real save does (Story.OnSaveComplete writes the
            // snapshot it captured at OnSaveStart), but stop short of Service.OnSaveComplete's own
            // reconciliation pass, which would otherwise commit it cleanly. Then corrupt the persisted
            // document's authorization the way an on-disk edit or a bit flip would, before anything
            // reads it back.
            harness.Story.OnSaveStart();
            harness.Service.OnSaveStart();
            harness.Story.OnSaveComplete();

            var consumptionId = harness.Consumption().EffectId;
            var corrupted = repository.StoredState! with
            {
                NativeEffects = repository.StoredState!.NativeEffects
                    .Select(effect => effect.EffectId == consumptionId
                        ? effect with { AuthorizedStoryCorrelationId = "not-a-canonical-correlation" }
                        : effect)
                    .ToArray()
            };
            repository.Update(corrupted);
        }

        using var reloaded = Release1TheEnvelopeHarness.Load(repository, world);

        // The reload's own OnLoadComplete pass already attempted the commit once (and was rejected); a
        // further explicit pass must keep reporting the same honest rejection, not a false Committed.
        Assert.Equal(Release1TheEnvelopeDepositStatus.Rejected, reloaded.Service.ReconcileDeposit());
        Assert.Equal(Release1NativeEffectPhase.Applied, reloaded.Consumption().Phase);
    }

    [Fact]
    public void A_quit_without_saving_after_the_deposit_re_plans_from_a_fresh_read_and_consumes_once()
    {
        var repository = new Release1TheEnvelopeHarness.FakeRepository();
        var world = new Release1TheEnvelopeHarness.FakeWorld();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        using (var harness = Release1TheEnvelopeHarness.RecoveryActive(repository, world))
        {
            for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
                harness.World.SetClosetCash(closetA, slotIndex, 1000f);

            Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, harness.Service.ReconcileDeposit());
            for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
                Assert.Null(harness.World.ReadClosetCash(closetA, slotIndex));

            // Simulates quitting without a native save: the world's own mutations since the last real
            // save revert, but the story sidecar was never persisted either, so the repository still
            // holds the pre-deposit state.
            harness.World.RevertToLastSave();
        }

        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            Assert.Equal(1000f, world.ReadClosetCash(closetA, slotIndex));

        using var reloaded = Release1TheEnvelopeHarness.Load(repository, world);

        Assert.Equal(5, reloaded.World.ClosetCashChangeCalls.Count);
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            Assert.Null(reloaded.World.ReadClosetCash(closetA, slotIndex));
    }

    [Fact]
    public void A_prepared_effect_whose_slots_all_read_their_pre_balances_re_runs_the_plan_unchanged()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        var plan = BuildFullPlan(closetA, 5, 5000d);
        PreparePlanEffect(harness, plan);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, status);
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            Assert.Null(harness.World.ReadClosetCash(closetA, slotIndex));
        Assert.Equal(5, harness.World.ClosetCashChangeCalls.Count);
    }

    [Fact]
    public void A_prepared_effect_whose_slots_all_read_their_post_balances_marks_applied_and_consumes_nothing_further()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        var plan = BuildFullPlan(closetA, 5, 5000d);
        PreparePlanEffect(harness, plan);
        // Every planned slot already reads its post balance (nothing set at all, so every read is
        // Unavailable, which is the positively-confirmed drained reading for a zero post balance).

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, status);
        Assert.Empty(harness.World.ClosetCashChangeCalls);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Consumption().Phase);
    }

    [Fact]
    public void A_prepared_effect_with_some_slots_at_post_and_the_rest_at_pre_drains_only_the_slots_still_at_pre()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        // Slots 0 and 1 are already at their post balance (nothing set, so an Unavailable read); 2, 3,
        // and 4 are still at their pre balance.
        for (var slotIndex = 2; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        var plan = BuildFullPlan(closetA, 5, 5000d);
        PreparePlanEffect(harness, plan);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, status);
        Assert.Equal(3, harness.World.ClosetCashChangeCalls.Count);
        Assert.Equal(new[] { 2, 3, 4 }, harness.World.ClosetCashChangeCalls.Select(call => call.SlotIndex));
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            Assert.Null(harness.World.ReadClosetCash(closetA, slotIndex));
    }

    [Fact]
    public void A_mark_applied_failure_after_the_decrements_leaves_the_effect_prepared_and_unblocked_and_the_next_pass_marks_it_applied_with_no_further_mutation()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        var plan = BuildFullPlan(closetA, 5, 5000d);
        PreparePlanEffect(harness, plan);

        // Fail exactly the second context read this pass makes: the first is ReadMatchingContext's own
        // check at the top of ReconcileDeposit (must succeed, or the pass never starts); the second is
        // the TryMarkNativeEffectApplied gate that MarkConsumptionApplied calls only after every slot
        // has already been decremented. This reproduces a mark that fails immediately after a real
        // decrement pass, the only way a Prepared effect ever survives with its slots at post.
        harness.Context.FailNthReadFromNow(2);

        var firstPass = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Ambiguous, firstPass);
        Assert.Equal(Release1NativeEffectPhase.Prepared, harness.Consumption().Phase);
        Assert.False(harness.Consumption().ExecutionBlocked);
        Assert.Equal(5, harness.World.ClosetCashChangeCalls.Count);
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            Assert.Null(harness.World.ReadClosetCash(closetA, slotIndex));

        var secondPass = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, secondPass);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Consumption().Phase);
        Assert.Equal(5, harness.World.ClosetCashChangeCalls.Count);
    }

    [Fact]
    public void A_prepared_effect_with_any_planned_slot_at_neither_pre_nor_post_blocks_ambiguous_and_touches_nothing()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        var plan = BuildFullPlan(closetA, 5, 5000d);
        PreparePlanEffect(harness, plan);
        harness.World.SetClosetCash(closetA, 3, 640f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Ambiguous, status);
        Assert.Empty(harness.World.ClosetCashChangeCalls);
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 0));
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 1));
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 2));
        Assert.Equal(640f, harness.World.ReadClosetCash(closetA, 3));
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 4));
    }

    [Fact]
    public void A_prepared_effect_whose_source_identity_is_not_a_resolvable_closet_blocks()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        var plan = BuildFullPlan(closetA, 5, 5000d);
        // The stable ID rule the plan's own closet GUID must satisfy forbids whitespace; the native
        // effect ID rule does not, so this passes native effect validation but fails plan resolution.
        PreparePlanEffect(harness, plan, sourceIdentityOverride: "not a closet guid");

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Ambiguous, status);
        Assert.Empty(harness.World.ClosetCashChangeCalls);
        Assert.True(harness.Consumption().ExecutionBlocked);
    }

    [Fact]
    public void Arthur_rings_once_on_RequiredFailure_and_once_on_the_ending_and_never_otherwise()
    {
        using var harness = Release1TheEnvelopeHarness.Active(withPhone: true);
        harness.RecordNellAcceptedReceipt();
        harness.World.TotalMinutes = 200d * 60d;

        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        Assert.Single(harness.Queue.Invocations);

        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Mission().State);
        // MakeGood acceptance moves the mission to its next attempt; the ending call's own Nell
        // prerequisite is keyed to the current attempt, so it needs its own receipt.
        harness.RecordNellAcceptedReceipt();

        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex < 10; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);

        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, harness.Service.ReconcileDeposit());
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);

        // The ending call, like Wrong Address's own courteous call, waits on the consumption effect
        // leaving Applied via a real save: recording a new phone presentation attempt is itself a
        // durable write, and the story defers immediate persistence while an Applied effect is
        // pending. So it is not queued yet.
        harness.Service.Update();
        Assert.Single(harness.Queue.Invocations);

        Release1TheEnvelopeHarness.Save(harness);

        Assert.Equal(2, harness.Queue.Invocations.Count);
        Assert.All(harness.Queue.Invocations, request => Assert.Equal(Release1PhoneCallRole.Arthur, request.Role));

        harness.Service.Update();
        Assert.Equal(2, harness.Queue.Invocations.Count);
    }

    [Fact]
    public void A_full_reconstruction_after_completion_pays_nothing_and_does_not_re_fire_recognition()
    {
        var repository = new Release1TheEnvelopeHarness.FakeRepository();
        var world = new Release1TheEnvelopeHarness.FakeWorld();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        using (var harness = Release1TheEnvelopeHarness.RecoveryActive(repository, world, withPhone: true))
        {
            harness.RecordNellAcceptedReceipt();
            for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
                harness.World.SetClosetCash(closetA, slotIndex, 1000f);

            Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, harness.Service.ReconcileDeposit());
            Release1TheEnvelopeHarness.Save(harness);

            Assert.Equal(Release1NativeEffectPhase.Committed, harness.Consumption().Phase);
            Assert.True(harness.Story.State!.Release1Recognized);
            Assert.Equal(0, harness.World.WalletCashChangeCalls);
        }

        using var reloaded = Release1TheEnvelopeHarness.Load(repository, world, withPhone: true);

        Assert.Equal(Release1MissionState.Satisfied, reloaded.Mission().State);
        Assert.Equal(0, reloaded.World.WalletCashChangeCalls);
        Assert.Empty(reloaded.World.ClosetCashChangeCalls);
        Assert.Empty(reloaded.Queue.Invocations);
    }

    private static Release1TheEnvelopeClosetPlan BuildFullPlan(string closetGuid, int slotCount, double amountWholeDollars)
    {
        var cashSlots = Enumerable.Range(0, slotCount)
            .Select(slotIndex => new Release1TheEnvelopeCashSlot(slotIndex, amountWholeDollars / slotCount))
            .ToArray();
        Assert.True(Release1TheEnvelopeClosetPlan.TryBuild(cashSlots, closetGuid, amountWholeDollars, out var plan));
        return plan!;
    }

    /// <summary>
    /// Injects a Prepared native effect for the given plan directly through the story runtime,
    /// bypassing ConsumeEnvelope entirely, the same way a real native mutation that crashed between
    /// the Prepared write and its own first decrement would leave the journal. Reuses exactly the
    /// authorization <see cref="Release1TheEnvelopeMissionService.BeginTransaction"/> itself would
    /// compute (Primary's own accepted MissionActivated correlation, or the frozen assignment's
    /// AuthorizationCorrelationId for MakeGood/Recovery).
    /// </summary>
    private static void PreparePlanEffect(
        Release1TheEnvelopeHarness.Harness harness,
        Release1TheEnvelopeClosetPlan plan,
        string? sourceIdentityOverride = null)
    {
        var mission = harness.Mission();
        var assignment = harness.Assignment;
        var authorization = assignment.Mode == Release1TheEnvelopeAssignmentMode.Primary
            ? mission.AcceptedLogicalCorrelations.Last(value =>
                Release1LogicalCorrelation.TryParse(value, out var correlation) &&
                correlation.TransitionKind == Release1TransitionKind.MissionActivated)
            : assignment.AuthorizationCorrelationId;
        var prepared = new Release1NativeEffectJournalEntry(
            $"the-envelope-cash-v1-a{mission.Attempt}",
            Release1MissionCatalog.TheEnvelope,
            mission.Attempt,
            "CashTransfer",
            sourceIdentityOverride ?? plan.ClosetGuid,
            "oc-the-envelope",
            plan.Serialize(),
            Release1NativeEffectPhase.Prepared,
            null,
            harness.Story.State!.Revision + 1,
            AuthorizedStoryCorrelationId: authorization,
            AuthorizedMissionRevision: mission.Revision);
        Assert.True(harness.Story.TryPrepareNativeEffect(prepared).Accepted);
    }
}
