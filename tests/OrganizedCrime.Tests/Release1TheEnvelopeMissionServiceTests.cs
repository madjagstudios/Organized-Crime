using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1TheEnvelopeMissionServiceTests
{
    [Fact]
    public void The_offer_is_ineligible_until_keep_the_lights_off_is_satisfied()
    {
        using var harness = Release1TheEnvelopeHarness.KeepTheLightsOffStillActive();

        Assert.Equal(Release1TheEnvelopeOfferStatus.Ineligible, harness.Service.OfferStatus);
    }

    [Fact]
    public void Offer_is_available_when_KeepTheLightsOff_is_satisfied_and_TheEnvelope_is_offered()
    {
        using var harness = Release1TheEnvelopeHarness.Offered();

        Assert.Equal(Release1TheEnvelopeOfferStatus.Available, harness.Service.OfferStatus);
        Assert.Equal(Release1MissionState.Offered, harness.Mission().State);
    }

    // Proves TryReview never resolves a dead drop by making the fake's TryReadDeadDrops throw
    // unconditionally: a review that reached it would fail this test with the synthetic exception
    // instead of returning Ready.
    [Fact]
    public void The_offer_needs_no_world_drop_read()
    {
        using var harness = Release1TheEnvelopeHarness.Offered();

        var review = harness.Service.TryReview();

        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, review.Status);
    }

    [Fact]
    public void The_quote_carries_the_room_key_nine_closets_twenty_thousand_and_a_twenty_four_hour_deadline()
    {
        using var harness = Release1TheEnvelopeHarness.Offered();
        var revisionBefore = harness.Story.State!.Revision;

        var review = harness.Service.TryReview();

        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, review.Status);
        Assert.NotNull(review.Quote);
        Assert.Equal(Release1TheEnvelopeAssignmentMode.Primary, review.Quote!.Assignment.Mode);
        Assert.Equal(20000d, review.Quote.Assignment.AmountWholeDollars);
        Assert.Equal(24d, review.Quote.DeadlineDurationHours);
        Assert.Equal(Release1RoomWithNoNameAssignment.SyndicateHqRoomKey, review.Quote.Assignment.HoldRoomKey);
        Assert.Equal(Release1RoomWithNoNameAssignment.SyndicateHqClosetCount, review.Quote.Assignment.ExpectedClosetCount);
        Assert.Equal(revisionBefore, harness.Story.State.Revision);
        Assert.Empty(harness.Story.State.TheEnvelopeAssignments);
    }

    [Fact]
    public void Make_good_and_recovery_quotes_carry_ten_thousand_and_five_thousand_and_recovery_has_no_deadline()
    {
        using var makeGood = Release1TheEnvelopeHarness.MakeGoodOffered();
        var makeGoodQuote = makeGood.Service.TryReview().Quote!;
        Assert.Equal(Release1TheEnvelopeAssignmentMode.MakeGood, makeGoodQuote.Assignment.Mode);
        Assert.Equal(10000d, makeGoodQuote.Assignment.AmountWholeDollars);
        Assert.Equal(24d, makeGoodQuote.DeadlineDurationHours);

        using var recovery = Release1TheEnvelopeHarness.RecoveryAvailable();
        var recoveryQuote = recovery.Service.TryReview().Quote!;
        Assert.Equal(Release1TheEnvelopeAssignmentMode.Recovery, recoveryQuote.Assignment.Mode);
        Assert.Equal(5000d, recoveryQuote.Assignment.AmountWholeDollars);
        Assert.Null(recoveryQuote.DeadlineDurationHours);
    }

    [Fact]
    public void Accept_requires_a_prior_matching_review()
    {
        using var harness = Release1TheEnvelopeHarness.Offered();

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1TheEnvelopeDecisionStatus.ReviewRequired, decision.Status);
        Assert.Empty(harness.Story.State!.TheEnvelopeAssignments);
    }

    [Fact]
    public void Accepting_freezes_the_assignment_and_activates_the_stage()
    {
        using var harness = Release1TheEnvelopeHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        var quote = harness.Service.TryReview().Quote!;

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, decision.Status);
        var assignment = Assert.Single(harness.Story.State!.TheEnvelopeAssignments);
        Assert.Equal(quote.Assignment, assignment);
        var mission = harness.Mission();
        Assert.Equal(Release1MissionState.Active, mission.State);
        Assert.Equal("the-envelope-v1", mission.TermsVersion);
        Assert.Equal(100d, mission.AcceptedGameTimeHours);
        Assert.Equal(124d, mission.DeadlineGameTimeHours);
        // Acceptance and activation land together in one durable revision; unlike Wrong Address there
        // is no separate in-memory-only staging flag left riding the next save.
        Assert.Equal(harness.Story.State.Revision, harness.Story.LastPersistedRevision);
    }

    // Fold-in from the Task 2 review: the redesigned quote is fully determined by mode, attempt, and
    // authorization correlation, so nothing in the world can make it drift between review and accept
    // the way a shifting dead drop candidate once could. QuoteChanged still exists and still fires,
    // though, whenever the mission itself moves out from under a cached review without going through
    // TryDismiss/TryDefer (which would short circuit accept earlier); this proves that path directly
    // by deferring the mission on the story straight through TryExecuteDurably, bypassing the service.
    [Fact]
    public void Accept_reports_QuoteChanged_when_a_re_check_between_review_and_accept_no_longer_matches()
    {
        using var harness = Release1TheEnvelopeHarness.Offered();
        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, harness.Service.TryReview().Status);
        var mission = harness.Mission();
        var receipt = $"test-envelope-defer-v1-a{mission.Attempt}";
        var command = new Release1StoryCommand(
            harness.Context.Snapshot.SessionEpoch,
            harness.Context.Snapshot.LoadEpoch,
            harness.Context.Snapshot.PlayerId,
            Release1MissionCatalog.TheEnvelope,
            mission.Attempt,
            Release1TransitionKind.MissionDeferred,
            receipt,
            Release1LogicalCorrelation.Create(
                harness.Context.Snapshot.PlayerId, Release1MissionCatalog.TheEnvelope, mission.Attempt,
                Release1TransitionKind.MissionDeferred, receipt).Value);
        Assert.True(harness.Story.TryExecuteDurably(command).Accepted);

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1TheEnvelopeDecisionStatus.QuoteChanged, decision.Status);
        // The room replaced the drop as the destination; the message must not still say "drop".
        Assert.Equal("The terms changed; review again.", decision.Message);
        Assert.Equal(Release1MissionState.Deferred, harness.Mission().State);
    }

    [Fact]
    public void Deferring_records_the_deferral_and_dismisses_for_the_load()
    {
        var repository = new Release1TheEnvelopeHarness.FakeRepository();
        var world = new Release1TheEnvelopeHarness.FakeWorld();
        using (var first = Release1TheEnvelopeHarness.Offered(repository, world))
        {
            first.Service.TryReview();
            Assert.Equal(Release1TheEnvelopeDecisionStatus.Deferred, first.Service.TryDefer().Status);
            Assert.Equal(Release1MissionState.Deferred, first.Mission().State);
            Assert.Equal(1, first.Mission().Attempt);
            Assert.Equal(Release1TheEnvelopeOfferStatus.Dismissed, first.Service.OfferStatus);
            first.Service.OnPreLoad();
        }

        using var restored = Release1TheEnvelopeHarness.Load(repository, world);
        Assert.Equal(Release1MissionState.Offered, restored.Mission().State);
        Assert.Equal(2, restored.Mission().Attempt);
        Assert.Equal(Release1TheEnvelopeOfferStatus.Available, restored.Service.OfferStatus);
    }

    [Fact]
    public void Dismissing_hides_the_offer_for_the_load()
    {
        using var harness = Release1TheEnvelopeHarness.Offered();
        harness.Service.TryReview();

        var decision = harness.Service.TryDismiss();

        Assert.Equal(Release1TheEnvelopeDecisionStatus.Dismissed, decision.Status);
        Assert.Equal(Release1TheEnvelopeOfferStatus.Dismissed, harness.Service.OfferStatus);
        Assert.Equal(Release1TheEnvelopeReviewStatus.Dismissed, harness.Service.TryReview().Status);
        // Dismissal is a load-scoped presentation choice only; the mission itself is untouched, unlike
        // a defer, which durably records MissionDeferred.
        Assert.Equal(Release1MissionState.Offered, harness.Mission().State);
    }

    [Fact]
    public void The_primary_deadline_lapses_to_RequiredFailure_and_the_make_good_deadline_to_MakeGoodFailed()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        var standingBefore = harness.Story.State!.Standing;
        harness.World.TotalMinutes = 200d * 60d;

        harness.Service.Update();
        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.RequiredFailure, harness.Mission().LastOutcome);
        Assert.Single(harness.Mission().PenaltyReceiptIds);
        Assert.Equal(standingBefore - 12, harness.Story.State!.Standing);

        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Mission().State);
        var standingBeforeMakeGood = harness.Story.State!.Standing;
        harness.World.TotalMinutes = 400d * 60d;

        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.MakeGoodFailure, harness.Mission().LastOutcome);
        Assert.Equal(standingBeforeMakeGood - 8, harness.Story.State!.Standing);
    }

    [Fact]
    public void A_lock_failure_never_suppresses_the_deadline_and_the_cash_stays_untouched()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex < 20; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        harness.World.RejectClosetLocks = true;

        // The first pass builds the full plan and hits the lock failure: the effect is Prepared and
        // unblocked (OC-61 decision 7), and no cash has moved.
        harness.Service.Update();
        Assert.Equal(Release1NativeEffectPhase.Prepared, harness.Consumption().Phase);
        Assert.False(harness.Consumption().ExecutionBlocked);
        Assert.Empty(harness.World.ClosetCashChangeCalls);

        // The deadline lapses while that same unexecuted, unblocked effect still exists. The old
        // guard suppressed the deadline for as long as any consumption effect existed, which would
        // dead end the mission forever behind a lock that never frees. Keying the guard on the
        // effect having reached Applied lets the deadline still fail the stage.
        harness.World.TotalMinutes = 200d * 60d;
        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.RequiredFailure, harness.Mission().LastOutcome);
        for (var slotIndex = 0; slotIndex < 20; slotIndex++)
            Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, slotIndex));
        Assert.Empty(harness.World.ClosetCashChangeCalls);
    }

    [Fact]
    public void A_lapsed_deadline_with_a_partial_deposit_present_leaves_A_slot_0_at_1000_and_A_slot_1_at_1000_untouched()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        harness.World.SetClosetCash(closetA, 0, 1000f);
        harness.World.SetClosetCash(closetA, 1, 1000f);
        harness.World.TotalMinutes = 200d * 60d;

        harness.Service.Update();
        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 0));
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 1));
        Assert.Empty(harness.World.ClosetCashChangeCalls);
    }

    // OC-61 the paid-deposit race: a full decrement can succeed while only the Applied mark itself
    // fails to record, leaving the effect Prepared with the cash already gone. If the deadline then
    // lapses before the next pass, Update() must reconcile that Prepared-at-post effect to Applied
    // before it decides whether the stage failed, or the player's cash is gone with no completion
    // and no recovery path.
    [Fact]
    public void A_full_decrement_whose_applied_mark_failed_once_re_marks_and_completes_after_the_deadline_lapses()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex < 20; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        var standingBefore = harness.Story.State!.Standing;

        // Fail exactly the third context read this pass makes: the first is ReconcileDeposit's own
        // ReadMatchingContext gate, the second is TryPrepareNativeEffect's own authority check (this
        // is a brand new deposit, so BeginDeposit/BeginTransaction prepare the effect fresh instead
        // of a test fixture seeding it directly), and the third is the TryMarkNativeEffectApplied
        // gate that MarkConsumptionApplied calls only after every slot has already been decremented.
        // This reproduces the exact repro from the finding: the full $20,000 decrement succeeds and
        // only the Applied mark itself fails.
        harness.Context.FailNthReadFromNow(3);

        var firstPass = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Ambiguous, firstPass);
        Assert.Equal(Release1NativeEffectPhase.Prepared, harness.Consumption().Phase);
        Assert.False(harness.Consumption().ExecutionBlocked);
        Assert.Equal(20, harness.World.ClosetCashChangeCalls.Count);
        for (var slotIndex = 0; slotIndex < 20; slotIndex++)
            Assert.Null(harness.World.ReadClosetCash(closetA, slotIndex));

        // The deadline lapses before the next pass, with the drained-but-unmarked effect still
        // Prepared.
        harness.World.TotalMinutes = 200d * 60d;

        harness.Service.Update();

        // The pass re-marks the effect Applied and completes the mission in the same pass; it never
        // fails the stage (no penalty), and moves no further cash, since every planned slot was
        // already at its post balance before this pass began.
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Empty(harness.Mission().PenaltyReceiptIds);
        Assert.True(harness.Story.State!.Standing >= standingBefore);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Consumption().Phase);
        Assert.Equal(20, harness.World.ClosetCashChangeCalls.Count);

        Release1TheEnvelopeHarness.Save(harness);

        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Consumption().Phase);
    }

    [Fact]
    public void Recovery_has_no_deadline_and_never_lapses()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryAvailable();
        var quote = harness.Service.TryReview().Quote!;
        Assert.Null(quote.DeadlineDurationHours);

        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Null(harness.Mission().DeadlineGameTimeHours);
        Assert.Equal(Release1MissionState.RecoveryActive, harness.Mission().State);

        harness.World.TotalMinutes = 100_000d * 60d;
        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(Release1MissionState.RecoveryActive, harness.Mission().State);
    }

    // Reflection, not a name list: covers every Envelope enum, including any added later, rather than
    // pinning the three the shipped drop-based design happened to carry the status on.
    [Fact]
    public void There_is_no_no_empty_dead_drop_status_on_any_envelope_enum()
    {
        var envelopeEnums = typeof(Release1TheEnvelopeOfferStatus).Assembly.GetTypes()
            .Where(type => type.IsEnum && type.Name.Contains("TheEnvelope", StringComparison.Ordinal));

        foreach (var enumType in envelopeEnums)
            Assert.DoesNotContain("NoEmptyDeadDrop", Enum.GetNames(enumType));
    }

    [Fact]
    public void A_room_that_is_not_ready_holds_and_never_messages()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        harness.World.RoomReadiness = Release1HoldRoomReadiness.NotReady;

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Unavailable, status);
        Assert.Null(harness.Progress());
        Assert.Empty(harness.World.ClosetCashChangeCalls);
        Assert.Single(harness.Logs);
    }

    [Fact]
    public void A_room_with_eight_closets_holds()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        harness.World.ClosetCount = 8;

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Unavailable, status);
        Assert.Null(harness.Progress());
        Assert.Empty(harness.World.ClosetCashChangeCalls);
    }

    [Fact]
    public void A_closet_whose_slot_list_reads_back_empty_holds_and_changes_nothing()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        harness.World.EmptySlotListClosetIndex = 3;

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Unavailable, status);
        Assert.Null(harness.Progress());
        Assert.Empty(harness.World.ClosetCashChangeCalls);
    }

    [Fact]
    public void A_faulted_cash_read_holds_and_is_never_read_as_empty()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        harness.World.SetClosetCash(closetA, 0, 1000f);
        harness.World.FaultClosetCashReads = true;

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Unavailable, status);
        Assert.Null(harness.Progress());
        Assert.Empty(harness.World.ClosetCashChangeCalls);
    }

    [Fact]
    public void No_cash_anywhere_is_NoWork()
    {
        using var harness = Release1TheEnvelopeHarness.Active();

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.NoWork, status);
        Assert.Null(harness.Progress());
    }

    [Fact]
    public void A_shortfall_records_the_remaining_amount_and_the_observed_sum()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        harness.World.SetClosetCash(closetA, 0, 1000f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.NoWork, status);
        Assert.Equal(1000d, harness.Progress()!.ObservedBalance);
        Assert.Equal(4000d, harness.Progress()!.LastShortfallNoticed);
        var revisionAfterFirst = harness.Story.State!.Revision;

        var repeat = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.NoWork, repeat);
        Assert.Equal(revisionAfterFirst, harness.Story.State!.Revision);

        harness.World.SetClosetCash(closetA, 1, 1000f);
        var third = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.NoWork, third);
        Assert.Equal(2000d, harness.Progress()!.ObservedBalance);
        Assert.Equal(3000d, harness.Progress()!.LastShortfallNoticed);
    }

    [Fact]
    public void A_shortfall_dedupes_by_the_remaining_amount_and_not_by_the_pass()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];

        harness.World.SetClosetCash(closetA, 0, 1000f);
        harness.Service.ReconcileDeposit();
        Assert.Equal(4000d, harness.Progress()!.LastShortfallNoticed);
        var revisionA = harness.Story.State!.Revision;

        harness.Service.ReconcileDeposit();
        Assert.Equal(revisionA, harness.Story.State!.Revision);

        harness.World.SetClosetCash(closetA, 0, 2000f);
        harness.Service.ReconcileDeposit();
        Assert.Equal(3000d, harness.Progress()!.LastShortfallNoticed);
        var revisionB = harness.Story.State!.Revision;
        Assert.NotEqual(revisionA, revisionB);

        harness.Service.ReconcileDeposit();
        Assert.Equal(revisionB, harness.Story.State!.Revision);

        harness.World.SetClosetCash(closetA, 0, 1000f);
        harness.Service.ReconcileDeposit();
        Assert.Equal(4000d, harness.Progress()!.LastShortfallNoticed);
        var revisionC = harness.Story.State!.Revision;
        Assert.NotEqual(revisionB, revisionC);
    }

    [Fact]
    public void Cash_in_two_closets_sets_SpreadNoticed_once_and_consumes_nothing()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        var closetB = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[1];
        harness.World.SetClosetCash(closetA, 0, 1000f);
        harness.World.SetClosetCash(closetB, 0, 1f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Held, status);
        Assert.True(harness.Progress()!.SpreadNoticed);
        Assert.Empty(harness.World.ClosetCashChangeCalls);
        var revision = harness.Story.State!.Revision;

        var repeat = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Held, repeat);
        Assert.Equal(revision, harness.Story.State!.Revision);
    }

    [Fact]
    public void Non_cash_items_are_never_counted_and_never_touched()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        harness.World.SetClosetCash(closetA, 0, 1000f);
        harness.World.SetClosetProduct(closetA, 1, "product");

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.NoWork, status);
        Assert.Equal(4000d, harness.Progress()!.LastShortfallNoticed);
        Assert.Empty(harness.World.ClosetCashChangeCalls);
    }

    [Fact]
    public void Non_cash_items_in_another_closet_do_not_make_a_spread()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        var closetB = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[1];
        for (var slotIndex = 0; slotIndex < 5; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);
        harness.World.SetClosetProduct(closetB, 0, "product");

        var status = harness.Service.ReconcileDeposit();

        // Task 4 wires the real transaction, so a sufficient, unspread closet now actually consumes;
        // what this test still proves is that the pass never misclassified the room as Spread on
        // account of closet B's non-cash product. The consumption itself, per slot, is Release1TheEnvelopeDepositTests's scope.
        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, status);
        Assert.DoesNotContain(harness.Logs, line => line.Contains("more than one closet", StringComparison.Ordinal));
    }

    [Fact]
    public void A_sufficient_closet_plans_whole_stacks_ascending_and_one_partial_last()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        for (var slotIndex = 0; slotIndex <= 5; slotIndex++)
            harness.World.SetClosetCash(closetA, slotIndex, 1000f);

        var status = harness.Service.ReconcileDeposit();

        // Task 4 wires the real transaction: slots 0-4 (5000 whole dollars) are the plan and are fully
        // consumed; slot 5 is surplus, never in the plan, and stays untouched.
        Assert.Equal(Release1TheEnvelopeDepositStatus.AwaitingAppliedSave, status);
        Assert.Equal(5, harness.World.ClosetCashChangeCalls.Count);
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            Assert.Null(harness.World.ReadClosetCash(closetA, slotIndex));
        Assert.Equal(1000f, harness.World.ReadClosetCash(closetA, 5));

        var cashSlots = Enumerable.Range(0, 6)
            .Select(slotIndex => new Release1TheEnvelopeCashSlot(slotIndex, 1000d))
            .ToArray();
        Assert.True(Release1TheEnvelopeClosetPlan.TryBuild(cashSlots, closetA, 5000d, out var plan));
        Assert.NotNull(plan);
        Assert.Equal(5, plan!.Slots.Count);
        for (var slotIndex = 0; slotIndex < 5; slotIndex++)
        {
            Assert.Equal(slotIndex, plan.Slots[slotIndex].SlotIndex);
            Assert.Equal(1000d, plan.Slots[slotIndex].PreBalance);
            Assert.Equal(0d, plan.Slots[slotIndex].PostBalance);
        }

        // The prepared-effect identity Task 4 adds: the consumption's own encoded payload matches this
        // independently built plan's serialization exactly.
        Assert.Equal(plan.Serialize(), harness.Consumption().AmountOrCargoIdentity);
        Assert.Equal(closetA, harness.Consumption().SourceIdentity);
    }

    [Fact]
    public void A_non_whole_dollar_pre_balance_holds_and_prepares_no_effect()
    {
        using var harness = Release1TheEnvelopeHarness.RecoveryActive();
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        harness.World.SetClosetCash(closetA, 0, 5000.25f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Held, status);
        Assert.Empty(harness.Story.State!.NativeEffects);
        Assert.Equal(5000.25f, harness.World.ReadClosetCash(closetA, 0));
        Assert.Empty(harness.World.ClosetCashChangeCalls);
    }

    // The brief's own literal numbers (21 slots at $500 against $10,500) do not reach the 256 character
    // identity bound at small slot indices: 21 groups of a 1-2 digit index, a 3 digit whole dollar pre
    // balance, and a mostly 1 digit post balance land at 191 characters, the same shortfall Task 2's
    // closet plan model test found for the identical digit shape. Reaching the bound against one of The
    // Envelope's own fixed stage amounts (this uses Primary, $20,000) needs wider index digits, so this
    // closet is given 10,021 slots and the 21 occupied ones sit at indices 10000-10020: the same
    // 21-groups-needed arithmetic (20 slots of $999 leave $20 outstanding for a 21st, partial, group),
    // but with five digit indices the encoding lands at 266 characters, past the 256 bound.
    [Fact]
    public void A_plan_that_cannot_be_encoded_within_the_bound_holds()
    {
        using var harness = Release1TheEnvelopeHarness.Active();
        var world = harness.World;
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        world.SlotsPerCloset = 10021;
        for (var slotIndex = 10000; slotIndex <= 10020; slotIndex++)
            world.SetClosetCash(closetA, slotIndex, 999f);

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1TheEnvelopeDepositStatus.Held, status);
        Assert.Empty(harness.Story.State!.NativeEffects);
        Assert.Empty(world.ClosetCashChangeCalls);
        for (var slotIndex = 10000; slotIndex <= 10020; slotIndex++)
            Assert.Equal(999f, world.ReadClosetCash(closetA, slotIndex));
    }
}

internal static class Release1TheEnvelopeHarness
{
    private const string PlayerId = "76561190000000001";
    private static readonly Guid SessionEpoch = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly string[] PriorMissions =
    {
        Release1MissionCatalog.SmallCourtesy,
        Release1MissionCatalog.WrongAddress,
        Release1MissionCatalog.RoomWithNoName,
        Release1MissionCatalog.ShortNotice,
        Release1MissionCatalog.KeepTheLightsOff
    };

    internal static Harness KeepTheLightsOffStillActive()
    {
        var repository = new FakeRepository();
        var context = new FakeContext();
        var story = SeededStory(context, repository, satisfyKeepTheLightsOff: false);
        var world = new FakeWorld();
        var logs = new List<string>();
        var service = new Release1TheEnvelopeMissionService(story, world, log: logs.Add);
        service.OnLoadComplete();
        return new(story, service, world, context, repository, logs, phone: null, queue: new FakeQueue());
    }

    internal static Harness Offered(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        repository ??= new FakeRepository();
        var context = new FakeContext();
        var story = SeededStory(context, repository, satisfyKeepTheLightsOff: true);
        world ??= new FakeWorld();
        var logs = new List<string>();
        var queue = new FakeQueue();
        var phone = withPhone ? new Release1PhoneCallService(story, queue, new FakeCue(), log: logs.Add) : null;
        var service = new Release1TheEnvelopeMissionService(story, world, phone, log: logs.Add);
        service.OnLoadComplete();
        repository.ResetEvidence();
        world.ResetMutationEvidence();
        return new(story, service, world, context, repository, logs, phone, queue);
    }

    internal static Harness Load(FakeRepository repository, FakeWorld world, bool withPhone = false)
    {
        var context = new FakeContext();
        var story = NewStory(context, repository);
        var logs = new List<string>();
        var queue = new FakeQueue();
        var phone = withPhone ? new Release1PhoneCallService(story, queue, new FakeCue(), log: logs.Add) : null;
        var service = new Release1TheEnvelopeMissionService(story, world, phone, log: logs.Add);
        // Reset before OnLoadComplete (not after): OnLoadComplete's own Converge() pass is the reload's
        // reconciliation, and a caller measuring CashChangeCalls/WalletCashChangeCalls right after Load()
        // returns needs those to reflect exactly that pass, not have it silently erased.
        world.ResetMutationEvidence();
        service.OnLoadComplete();
        return new(story, service, world, context, repository, logs, phone, queue);
    }

    internal static void Save(Harness harness)
    {
        harness.Story.OnSaveStart();
        harness.Service.OnSaveStart();
        harness.Story.OnSaveComplete();
        harness.Service.OnSaveComplete();
        harness.World.MarkSaved();
    }

    internal static Harness Active(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = Offered(repository, world, withPhone);
        harness.World.TotalMinutes = 6_000d;
        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    internal static Harness MakeGoodOffered(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = Active(repository, world, withPhone);
        harness.World.TotalMinutes = 200d * 60d;
        harness.Service.Update();
        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    internal static Harness MakeGoodActive(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = MakeGoodOffered(repository, world, withPhone);
        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Mission().State);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    internal static Harness RecoveryAvailable(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = MakeGoodActive(repository, world, withPhone);
        harness.World.TotalMinutes = 400d * 60d;
        harness.Service.Update();
        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    internal static Harness RecoveryActive(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = RecoveryAvailable(repository, world, withPhone);
        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.RecoveryActive, harness.Mission().State);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Seeds a story whose Small Courtesy, Wrong Address, Room With No Name, Short Notice, and (when
    // requested) Keep the Lights Off missions are Satisfied through the same generic
    // Release1StoryTransitions accept/activate/complete path every mission's own CompleteMission
    // transition uses, without running any of those missions' own (largely nonexistent, in Keep the
    // Lights Off's case) mission services.
    private static Release1StoryRuntimeService SeededStory(FakeContext context, FakeRepository repository, bool satisfyKeepTheLightsOff)
    {
        var state = Release1StoryState.CreateAccepted(context.Snapshot.PlayerId, Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-the-envelope").Value);
        foreach (var missionKey in PriorMissions)
        {
            if (missionKey == Release1MissionCatalog.KeepTheLightsOff && !satisfyKeepTheLightsOff) break;
            state = Satisfy(state, missionKey);
        }
        repository.Update(state);
        return NewStory(context, repository);
    }

    private static Release1StoryState Satisfy(Release1StoryState story, string missionKey)
    {
        var index = Release1MissionCatalog.IndexOf(missionKey);
        var mission = story.Missions[index];
        var acceptReceipt = $"{missionKey}-accept";
        var accept = new Release1StoryCommand(
            SessionEpoch, 1, story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionAccepted,
            acceptReceipt,
            Release1LogicalCorrelation.Create(story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionAccepted, acceptReceipt).Value,
            "terms-v1");
        var accepted = Release1StoryTransitions.Apply(story, accept);
        if (!accepted.Accepted) throw new InvalidOperationException($"Satisfy accept fixture setup failed for {missionKey}: {accepted.Message}");
        var activateReceipt = $"{missionKey}-activate";
        var activate = new Release1StoryCommand(
            SessionEpoch, 1, story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionActivated,
            activateReceipt,
            Release1LogicalCorrelation.Create(story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionActivated, activateReceipt).Value);
        var activated = Release1StoryTransitions.Apply(accepted.State!, activate);
        if (!activated.Accepted) throw new InvalidOperationException($"Satisfy activate fixture setup failed for {missionKey}: {activated.Message}");
        var completeReceipt = $"{missionKey}-complete";
        var complete = new Release1StoryCommand(
            SessionEpoch, 1, story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionCompleted,
            completeReceipt,
            Release1LogicalCorrelation.Create(story.PlayerId, missionKey, mission.Attempt, Release1TransitionKind.MissionCompleted, completeReceipt).Value,
            CompletionTiming: Release1CompletionTiming.OnTime,
            RewardAuthorizationReceiptId: $"{missionKey}-reward");
        var completed = Release1StoryTransitions.Apply(activated.State!, complete);
        if (!completed.Accepted) throw new InvalidOperationException($"Satisfy complete fixture setup failed for {missionKey}: {completed.Message}");
        return completed.State!;
    }

    private static Release1StoryRuntimeService NewStory(FakeContext context, FakeRepository repository)
    {
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        return story;
    }

    internal sealed class Harness : IDisposable
    {
        public Harness(
            Release1StoryRuntimeService story,
            Release1TheEnvelopeMissionService service,
            FakeWorld world,
            FakeContext context,
            FakeRepository repository,
            List<string> logs,
            Release1PhoneCallService? phone,
            FakeQueue queue)
        {
            Story = story;
            Service = service;
            World = world;
            Context = context;
            Repository = repository;
            Logs = logs;
            Phone = phone;
            Queue = queue;
        }

        public Release1StoryRuntimeService Story { get; }
        public Release1TheEnvelopeMissionService Service { get; }
        public FakeWorld World { get; }
        public FakeContext Context { get; }
        public FakeRepository Repository { get; }
        public List<string> Logs { get; }
        public Release1PhoneCallService? Phone { get; }
        public FakeQueue Queue { get; }
        public Release1TheEnvelopeAssignment Assignment => Story.State!.TheEnvelopeAssignments.MaxBy(value => value.Attempt)!;

        public Release1MissionRecord Mission() =>
            Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope)];

        public Release1TheEnvelopeProgress? Progress() =>
            Story.State?.TheEnvelopeProgress.SingleOrDefault(progress => progress.Attempt == Assignment.Attempt);

        public Release1NativeEffectJournalEntry Consumption() => Story.State!.NativeEffects.Single(effect =>
            effect.MissionKey == Release1MissionCatalog.TheEnvelope &&
            effect.EffectKind == "CashTransfer" &&
            effect.Attempt == Assignment.Attempt);

        // Records the Nell accepted-message receipt Arthur's prerequisite requires, for the current
        // Envelope mission attempt.
        public void RecordNellAcceptedReceipt()
        {
            var correlation = Release1LogicalCorrelation.Create(
                Context.Snapshot.PlayerId, Release1MissionCatalog.TheEnvelope, Mission().Attempt,
                Release1TransitionKind.MissionAccepted, "presentation-nell-te-accepted-v1").Value;
            var result = Story.TryRecordPresentationReceipt(correlation);
            if (!result.Accepted) throw new InvalidOperationException("Nell accepted-message receipt fixture setup failed: " + result.Message);
        }

        public void Dispose() { Service.Dispose(); Phone?.Dispose(); Story.Dispose(); }
    }

    // A phone-call queue test double that records every invocation attempt (including one that
    // throws) so tests can assert exactly-once delivery and failure-tolerance the same way the
    // production S1ApiRelease1PhoneCallQueue's caller (Release1PhoneCallService) observes it.
    internal sealed class FakeQueue : IRelease1PhoneCallQueue
    {
        public List<Release1PhoneCallRequest> Invocations { get; } = new();
        public bool Available { get; set; } = true;
        public bool ThrowOnInvoke { get; set; }
        public bool IsAvailable(Release1PhoneCallRequest request) => Available;

        public void Invoke(Release1PhoneCallRequest request)
        {
            Invocations.Add(request);
            if (ThrowOnInvoke) throw new InvalidOperationException("synthetic Envelope phone queue failure");
        }
    }

    internal sealed class FakeCue : IRelease1PayphoneCue
    {
        public bool TryShow(string correlationId) => true;
        public void End(string correlationId) { }
        public void Reconcile(string correlationId) { }
        public void Dispose() { }
    }

    internal sealed class FakeWorld : IRelease1SmallCourtesyWorld
    {
        private Action<string>? _closed;
        private static readonly string[] AllDropGuids = { "drop-a", "drop-b", "drop-c", "drop-d" };

        public static readonly string[] AllClosetGuids = Enumerable.Range(1, 9)
            .Select(index => $"0000000{index}-0000-0000-0000-000000000000").ToArray();

        public FakeWorld()
        {
            Context = new(SessionEpoch, 1, PlayerId, Path.GetTempPath());
            ResetAllDropsToEmpty();
        }

        public Release1StoryHostContextSnapshot Context { get; set; }
        public double TotalMinutes { get; set; }

        public Release1HoldRoomReadiness RoomReadiness { get; set; } = Release1HoldRoomReadiness.Ready;
        public int ClosetCount { get; set; } = 9;
        public int SlotsPerCloset { get; set; } = 20;
        public int EmptySlotListClosetIndex { get; set; } = -1;
        public bool FaultClosetCashReads { get; set; }
        public bool RejectClosetLocks { get; set; }
        public bool RejectClosetUnlocks { get; set; }
        public (string ClosetGuid, int SlotIndex)? FailClosetChangeAt { get; set; }
        public (string ClosetGuid, int SlotIndex, float Drift)? DriftClosetCashOnce { get; set; }
        public Dictionary<(string ClosetGuid, int SlotIndex), float> ClosetCash { get; } = new();
        public Dictionary<(string ClosetGuid, int SlotIndex), string> ClosetProducts { get; } = new();
        public List<(string ClosetGuid, int SlotIndex, float Amount)> ClosetCashChangeCalls { get; } = new();
        public List<string> ClosetMutationLog { get; } = new();
        public HashSet<(string ClosetGuid, int SlotIndex)> ClosetLocks { get; } = new();

        public void SetClosetCash(string closetGuid, int slotIndex, float balance) => ClosetCash[(closetGuid, slotIndex)] = balance;
        public float? ReadClosetCash(string closetGuid, int slotIndex) => ClosetCash.TryGetValue((closetGuid, slotIndex), out var value) ? value : null;

        public void SetClosetProduct(string closetGuid, int slotIndex, string productId) => ClosetProducts[(closetGuid, slotIndex)] = productId;

        // Keyed by dead drop GUID so a read of one drop can never observe another drop's slots.
        public Dictionary<string, Dictionary<int, Release1SmallCourtesySlotSnapshot>> DropSlots { get; } = new(StringComparer.Ordinal);

        // Keyed by (dead drop GUID, slot index); a slot with no entry here is not a cash slot, no
        // matter what its legacy MonetaryValue reads as.
        public Dictionary<(string DropGuid, int SlotIndex), float> CashBalances { get; } = new();
        public string? SubscribedDropGuid { get; private set; }
        public bool SubscriptionDisposed { get; private set; }
        public List<string> MutationLog { get; } = new();

        // Every call made to TryChangeDeadDropSlotCashBalance, in order, so tests can assert exactly
        // one call was made and with exactly the negative frozen amount.
        public List<(string DropGuid, int SlotIndex, float Amount)> CashChangeCalls { get; } = new();
        public bool IsLocked { get; private set; }
        public bool ReturnAmbiguousAfterLockOnce { get; set; }
        public int WalletCashChangeCalls { get; private set; }

        // The world state a native save would durably capture. Populated lazily the first time
        // TryChangeDeadDropSlotCashBalance mutates a balance since the last MarkSaved()/RevertToLastSave(),
        // so RevertToLastSave() can undo exactly the mutations this mod made since the last real save
        // while leaving whatever the test fixture itself set up directly untouched.
        private Dictionary<(string DropGuid, int SlotIndex), float>? _preSaveCashBalances;
        private readonly Dictionary<(string DropGuid, int SlotIndex), Release1SmallCourtesySlotSnapshot> _preSaveSlots = new();

        // The closet cash mirror of _preSaveCashBalances: populated lazily on the first
        // TryChangeHoldRoomSlotCashBalance mutation since the last MarkSaved()/RevertToLastSave(), so
        // a quit-without-saving test can undo exactly this session's closet decrements.
        private Dictionary<(string ClosetGuid, int SlotIndex), float>? _preSaveClosetCash;

        // OC-63 read counters. Every world read except the canonical clock is counted here so the
        // convergence throttle can be pinned by observation rather than by timing. Nothing else
        // reads them, so no existing test is affected. OC-61 moved The Envelope's deposit
        // reconciliation off the dead drop it used to read (TryReadDeadDropSlots and
        // TryReadDeadDropSlotCashBalance are unreachable from this mission's own production code now)
        // and onto the hold room and its closets' cash slots, so those are what this fake counts.
        public int ContextReads { get; private set; }
        public int HoldRoomReads { get; private set; }
        public int ClosetCashSlotReads { get; private set; }

        public int TotalWorldReadsExcludingClock =>
            ContextReads + HoldRoomReads + ClosetCashSlotReads;

        public void ResetReadCounters()
        {
            ContextReads = 0;
            HoldRoomReads = 0;
            ClosetCashSlotReads = 0;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context)
        {
            ContextReads++;
            context = Context;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
        {
            totalMinutes = TotalMinutes;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products)
        {
            products = Array.Empty<Release1SmallCourtesyProductCandidate>();
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        // The room is the destination now, so the mission service never resolves a dead drop; this
        // proves it by refusing to answer at all, the same way TryChangeSlotQuantity refuses below.
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops) =>
            throw new InvalidOperationException("The Envelope must never read the world's dead drops.");

        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(
            Release1SmallCourtesyPackageKind kind,
            out Release1SmallCourtesyPackagingCandidate packaging)
        {
            packaging = kind == Release1SmallCourtesyPackageKind.Brick ? new("brick", "Brick") : new("jar", "Jar");
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(
            string deadDropGuid,
            out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
        {
            slots = DropOf(deadDropGuid).Values.OrderBy(slot => slot.SlotIndex).ToArray();
            return AllDropGuids.Contains(deadDropGuid, StringComparer.Ordinal)
                ? Release1SmallCourtesyWorldReadStatus.Ready
                : Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
        {
            HoldRoomReads++;
            if (RoomReadiness != Release1HoldRoomReadiness.Ready)
            {
                room = RoomReadiness == Release1HoldRoomReadiness.NotReady
                    ? Release1HoldRoomSnapshot.NotReady()
                    : Release1HoldRoomSnapshot.Unavailable();
                return Release1SmallCourtesyWorldReadStatus.Ready;
            }

            var closets = new List<Release1HoldRoomClosetSnapshot>();
            for (var closetIndex = 0; closetIndex < ClosetCount; closetIndex++)
            {
                var closetGuid = AllClosetGuids[closetIndex];
                var slotCount = closetIndex == EmptySlotListClosetIndex ? 0 : SlotsPerCloset;
                var slots = new List<Release1SmallCourtesySlotSnapshot>(slotCount);
                for (var slotIndex = 0; slotIndex < slotCount; slotIndex++)
                {
                    if (ClosetCash.ContainsKey((closetGuid, slotIndex)))
                        slots.Add(new(slotIndex, "cash", "unpackaged", 1, false, 0f));
                    else if (ClosetProducts.TryGetValue((closetGuid, slotIndex), out var productId))
                        slots.Add(new(slotIndex, productId, "brick", 1, true, 1_000f));
                    else
                        slots.Add(new(slotIndex, null, null, 0, false, 0f));
                }
                closets.Add(new(closetGuid, slotCount, slots));
            }
            room = new(Release1HoldRoomReadiness.Ready, closets);
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked)
        {
            MutationLog.Add($"lock:{slotIndex}:{locked.ToString().ToLowerInvariant()}");
            if (locked && ReturnAmbiguousAfterLockOnce)
            {
                IsLocked = true;
                ReturnAmbiguousAfterLockOnce = false;
                return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
            }
            IsLocked = locked;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        // The Envelope's deposit transaction never mutates a dead drop's item quantity (cash carries
        // no packaging); a pass reaching this member anyway is a defect, so it throws.
        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount) =>
            throw new InvalidOperationException("The Envelope must never change a dead drop slot's quantity.");

        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(
            string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
        {
            reason = "The Envelope never stages a packaged product.";
            return Release1SmallCourtesyWorldMutationStatus.Rejected;
        }

        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(
            string deadDropGuid,
            Action<string> callback,
            out IRelease1SmallCourtesyDropSubscription? subscription)
        {
            SubscribedDropGuid = deadDropGuid;
            SubscriptionDisposed = false;
            _closed = callback;
            subscription = new FakeSubscription(deadDropGuid, () =>
            {
                SubscriptionDisposed = true;
                _closed = null;
            });
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        // The Envelope never touches the player's own wallet cash (there is no reward); this member
        // counts every call made to it, regardless of outcome, so tests can assert it was never
        // invoked at all.
        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount)
        {
            WalletCashChangeCalls++;
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount)
        {
            WalletCashChangeCalls++;
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)
        {
            reason = string.Empty;
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)
        {
            reason = string.Empty;
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }

        // When true, TryReadDeadDropSlotCashBalance reports Faulted regardless of any stored balance,
        // simulating a transient read failure (e.g. the first pass right after a reload) rather than a
        // genuine, positively confirmed empty or non-cash slot. Persists until the test turns it off.
        public bool FaultCashBalanceReads { get; set; }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance)
        {
            MutationLog.Add($"cashread:{slotIndex}");
            balance = 0f;
            if (FaultCashBalanceReads) return Release1SmallCourtesyWorldReadStatus.Faulted;
            if (CashBalances.TryGetValue((deadDropGuid, slotIndex), out var value))
            {
                balance = value;
                return Release1SmallCourtesyWorldReadStatus.Ready;
            }
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        // The Envelope's redesigned closet transaction never mutates a dead drop's cash balance (cash
        // moves through TryChangeHoldRoomSlotCashBalance below); a pass reaching this member anyway is
        // a defect, so it throws, the same way TryReadDeadDrops and TryChangeSlotQuantity do above.
        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) =>
            throw new InvalidOperationException("The Envelope must never change a dead drop slot's cash balance.");

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance)
        {
            ClosetCashSlotReads++;
            balance = 0f;
            if (FaultClosetCashReads) return Release1SmallCourtesyWorldReadStatus.Faulted;
            if (ClosetCash.TryGetValue((closetGuid, slotIndex), out var value) && value > 0f)
            {
                balance = value;
                return Release1SmallCourtesyWorldReadStatus.Ready;
            }
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked)
        {
            ClosetMutationLog.Add($"lock:{slotIndex}:{locked.ToString().ToLowerInvariant()}");
            if (locked && RejectClosetLocks) return Release1SmallCourtesyWorldMutationStatus.Rejected;
            if (!locked && RejectClosetUnlocks) return Release1SmallCourtesyWorldMutationStatus.Rejected;
            if (locked) ClosetLocks.Add((closetGuid, slotIndex));
            else ClosetLocks.Remove((closetGuid, slotIndex));
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount)
        {
            ClosetMutationLog.Add($"cashchange:{slotIndex}");
            ClosetCashChangeCalls.Add((closetGuid, slotIndex, amount));
            if (!ClosetLocks.Contains((closetGuid, slotIndex))) return Release1SmallCourtesyWorldMutationStatus.Rejected;
            if (FailClosetChangeAt is { } fail && string.Equals(fail.ClosetGuid, closetGuid, StringComparison.Ordinal) && fail.SlotIndex == slotIndex)
                return Release1SmallCourtesyWorldMutationStatus.Ambiguous;

            if (_preSaveClosetCash is null) _preSaveClosetCash = new Dictionary<(string, int), float>(ClosetCash);
            var current = ClosetCash.TryGetValue((closetGuid, slotIndex), out var existing) ? existing : 0f;
            var next = current + amount;
            if (DriftClosetCashOnce is { } drift && string.Equals(drift.ClosetGuid, closetGuid, StringComparison.Ordinal) && drift.SlotIndex == slotIndex)
            {
                next += drift.Drift;
                DriftClosetCashOnce = null;
            }
            if (next <= 0f) ClosetCash.Remove((closetGuid, slotIndex));
            else ClosetCash[(closetGuid, slotIndex)] = next;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        // The mission under test never reads production activity.
        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)
        {
            activity = Release1ProductionActivitySnapshot.Empty;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
        { snapshot = Release1FieldContactSnapshot.Unavailable(); return Release1SmallCourtesyWorldReadStatus.Unavailable; }

        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason)
        { reason = "this fake never despawns a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }

        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)
        { reason = "this fake never provokes a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }
        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
        { reason = "this fake never parks a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }
        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason)
        { reason = "this fake never unparks a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }

        public void ResetMutationEvidence()
        {
            MutationLog.Clear();
            CashChangeCalls.Clear();
            WalletCashChangeCalls = 0;
            ClosetMutationLog.Clear();
            ClosetCashChangeCalls.Clear();
        }

        // Captures both the cash-balance dictionary and the exact slot snapshot for the slot about to
        // be mutated: a full consumption also rewrites the DropSlots entry to an empty slot (see
        // TryChangeDeadDropSlotCashBalance above), so undoing the balance alone would leave a
        // reverted balance sitting behind a slot that still reads as empty (Quantity 0) and is never
        // observed as a cash slot again.
        private void CapturePreSaveSnapshotIfNeeded(string deadDropGuid, int slotIndex)
        {
            if (_preSaveCashBalances is null) _preSaveCashBalances = new Dictionary<(string, int), float>(CashBalances);
            if (DropSlots.TryGetValue(deadDropGuid, out var drop) && drop.TryGetValue(slotIndex, out var slot))
                _preSaveSlots.TryAdd((deadDropGuid, slotIndex), slot);
        }

        // Undoes every dead-drop and closet cash mutation made since the last MarkSaved() (i.e. since
        // the last real native save), while leaving anything the test itself set up directly
        // (SetClosetCash, etc.) untouched, the same way quitting without saving discards this
        // session's unsaved native mutations but not what was already on disk.
        public void RevertToLastSave()
        {
            if (_preSaveCashBalances is not null)
            {
                CashBalances.Clear();
                foreach (var pair in _preSaveCashBalances) CashBalances[pair.Key] = pair.Value;
            }
            foreach (var pair in _preSaveSlots)
            {
                if (DropSlots.TryGetValue(pair.Key.DropGuid, out var drop)) drop[pair.Key.SlotIndex] = pair.Value;
            }
            if (_preSaveClosetCash is not null)
            {
                ClosetCash.Clear();
                foreach (var pair in _preSaveClosetCash) ClosetCash[pair.Key] = pair.Value;
            }
            _preSaveCashBalances = null;
            _preSaveSlots.Clear();
            _preSaveClosetCash = null;
        }

        // Commits the world mutations made since the last save, exactly as a real native save would;
        // RevertToLastSave() can no longer undo them.
        public void MarkSaved()
        {
            _preSaveCashBalances = null;
            _preSaveSlots.Clear();
            _preSaveClosetCash = null;
        }

        // Resets every known drop to genuine empty slot entries (indices 0-3, quantity 0), the
        // baseline a freshly-loaded, never-touched world reads as.
        public void ResetAllDropsToEmpty()
        {
            foreach (var guid in AllDropGuids)
            {
                var drop = new Dictionary<int, Release1SmallCourtesySlotSnapshot>();
                for (var index = 0; index < 4; index++)
                    drop[index] = new(index, null, null, 0, false, 0f);
                DropSlots[guid] = drop;
            }
        }

        private Dictionary<int, Release1SmallCourtesySlotSnapshot> DropOf(string deadDropGuid)
        {
            if (!DropSlots.TryGetValue(deadDropGuid, out var drop))
            {
                drop = new Dictionary<int, Release1SmallCourtesySlotSnapshot>();
                DropSlots[deadDropGuid] = drop;
            }
            return drop;
        }
    }

    internal sealed class FakeSubscription : IRelease1SmallCourtesyDropSubscription
    {
        private readonly Action _dispose;
        private bool _disposed;
        public FakeSubscription(string deadDropGuid, Action dispose) { DeadDropGuid = deadDropGuid; _dispose = dispose; }
        public string DeadDropGuid { get; }
        public void Dispose() { if (_disposed) return; _disposed = true; _dispose(); }
    }

    internal sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(SessionEpoch, 1, PlayerId, Path.GetTempPath());
        private int _failCountdown;

        // Fails exactly the n-th TryRead call counting from now (1-based), then resumes reading Ready
        // forever after. Lets a test fail one specific context read deep inside a call stack (e.g. the
        // TryMarkNativeEffectApplied gate check that runs after ConsumeEnvelope's own decrements, but
        // not the ReadMatchingContext check at the very top of the same pass) without needing to know
        // how many reads happened before it was armed.
        public void FailNthReadFromNow(int n) => _failCountdown = n;

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            if (_failCountdown > 0)
            {
                _failCountdown--;
                if (_failCountdown == 0) return Release1StoryHostContextReadStatus.Pending;
            }
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    internal sealed class FakeRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public string BoundSaveFolder { get; } = Path.GetFullPath(Path.GetTempPath());
        public Release1StoryState? StoredState { get; private set; }
        public List<Release1StoryState> Updates { get; } = new();
        public bool FailNextUpdate { get; set; }

        public Release1StoryStoreLoadResult Load() => new(
            true,
            StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded,
            new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState),
            Release1StoryStoreFailureReason.None,
            string.Empty);

        public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
        {
            if (FailNextUpdate)
            {
                FailNextUpdate = false;
                return new(false, Release1StoryStoreUpdateStatus.Rejected, null, Release1StoryStoreFailureReason.AtomicReplacementFailed, "synthetic save failure");
            }
            StoredState = state;
            if (state is not null) Updates.Add(state);
            return new(true, Release1StoryStoreUpdateStatus.Updated, new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state), Release1StoryStoreFailureReason.None, string.Empty);
        }

        public void ResetEvidence() => Updates.Clear();
    }
}
