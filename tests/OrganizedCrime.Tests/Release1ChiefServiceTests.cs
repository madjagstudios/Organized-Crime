using System.Reflection;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ChiefServiceTests
{
    [Fact]
    public void No_retroactive_fire_on_a_record_free_save()
    {
        using var harness = Harness.Create();
        harness.Ledger.State = new LocalPressureState(80, true, null, null, null, harness.PlayerId, null, null, 0);

        harness.Service.Update();

        var record = harness.Record;
        Assert.NotNull(record);
        Assert.Equal(Release1ChiefState.Adopted, record!.State);
        Assert.True(record.AdoptedKnownOffender);
        Assert.Equal(LocalPressureTransitions.GetTier(80, LocalPressureProfile.Moderate), record.AdoptedTier);
        Assert.Empty(harness.Story.State!.NativeEffects);
        Assert.Empty(Release1ChiefPresentation.BuildPlan(harness.Story.State).Messages);
    }

    [Fact]
    public void Adopting_logs_the_tier_and_known_offender_it_observed()
    {
        // OC-73 review fix. Adopt used to be the one Chief transition with no log line at all, so
        // the owner protocol's own step 1 asked for evidence the code never produced. This pins that
        // adoption now writes an ordinary Receipt line naming both observed values.
        using var harness = Harness.Create();
        harness.Ledger.State = new LocalPressureState(80, true, null, null, null, harness.PlayerId, null, null, 0);
        var expectedTier = LocalPressureTransitions.GetTier(80, LocalPressureProfile.Moderate);

        var receipts = new List<string>();
        var previous = OrganizedCrimeLog.Receipt;
        OrganizedCrimeLog.Receipt = receipts.Add;
        try { harness.Service.Update(); }
        finally { OrganizedCrimeLog.Receipt = previous; }

        Assert.Equal(Release1ChiefState.Adopted, harness.Record!.State);
        Assert.Contains(receipts, r => r.Contains(expectedTier.ToString(), StringComparison.Ordinal) && r.Contains("True", StringComparison.Ordinal));
    }

    [Fact]
    public void First_contact_on_arrest_opens_round_one_and_never_reads_the_wallet()
    {
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();

        harness.Service.Update();

        var record = harness.Record!;
        Assert.Equal(Release1ChiefState.DemandOpen, record.State);
        Assert.Equal(1, record.DemandRound);
        Assert.Equal(0, harness.World.ReadCashCalls);

        var plan = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        // OC-73 review fix. The demand text is the decision's own prompt, not also a passive
        // message (the boundary sends the prompt as a message on its own behalf), so the plan
        // carries no message at all here.
        Assert.Empty(plan.Messages);
        Assert.NotNull(plan.Decision);
        Assert.Equal(Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.FirstDemandText), plan.Decision!.Prompt);
        Assert.Equal(2, plan.Decision!.Options.Count);
    }

    [Fact]
    public void First_contact_on_a_watched_crossing_carries_both_the_demand_and_the_watch_line()
    {
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest(LocalPressureTier.Quiet, LocalPressureTier.Watched);

        // Release1ChiefTierObserver.Publish always queues ArrestObserved first and the crossing
        // second; DrainOne takes at most one per pass, so the first pass opens the demand and the
        // second pass records the watch line.
        harness.Service.Update();
        harness.Service.Update();

        var record = harness.Record!;
        Assert.Equal(1, record.DemandRound);
        Assert.Equal(1, record.WatchLinesSent);
        var plan = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        // OC-73 review fix. The demand text is carried only by the decision's own prompt now, not
        // also queued as a message; the watch line is a genuine passive message.
        Assert.Equal(Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.FirstDemandText), plan.Decision!.Prompt);
        Assert.Contains(plan.Messages, m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.WatchListText));
        Assert.DoesNotContain(plan.Messages, m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.FirstDemandText));
    }

    [Fact]
    public void Decline_replies_and_touches_nothing_else()
    {
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        var standingBefore = harness.Story.State!.Standing;
        var missionsBefore = harness.Story.State!.Missions;

        var result = harness.Service.TryDecline();

        Assert.True(result);
        Assert.Equal(Release1ChiefState.Declined, harness.Record!.State);
        Assert.Equal(standingBefore, harness.Story.State!.Standing);
        Assert.Equal(missionsBefore.Count, harness.Story.State!.Missions.Count);
        for (var i = 0; i < missionsBefore.Count; i++)
            Assert.True(missionsBefore[i].ValueEquals(harness.Story.State!.Missions[i]));

        var plan = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        Assert.Null(plan.Decision);
        Assert.Contains(plan.Messages, m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.DeclineReplyText));
    }

    [Fact]
    public void The_price_rises_only_out_of_a_standing_decline_and_caps_at_the_third_rung()
    {
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        Assert.Equal(1, harness.Record!.DemandRound);

        // A second arrest while the demand is still open leaves the round alone.
        harness.Arrest();
        harness.Service.Update();
        Assert.Equal(1, harness.Record!.DemandRound);

        Assert.True(harness.Service.TryDecline());
        harness.Arrest();
        harness.Service.Update();
        Assert.Equal(2, harness.Record!.DemandRound);

        Assert.True(harness.Service.TryDecline());
        harness.Arrest();
        harness.Service.Update();
        Assert.Equal(3, harness.Record!.DemandRound);

        Assert.True(harness.Service.TryDecline());
        harness.Arrest();
        harness.Service.Update();
        Assert.Equal(3, harness.Record!.DemandRound);

        var plan = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        // OC-73 review fix. ThirdDemandText is the decision's own prompt now, not also a message.
        Assert.Equal(Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.ThirdDemandText), plan.Decision!.Prompt);
        Assert.Contains(plan.Decision!.Options, o => o.Label == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.PayLabel(25_000)));
    }

    [Fact]
    public void Short_cash_holds_and_the_shortfall_line_is_added_once_per_distinct_amount()
    {
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 10_000f;

        Assert.False(harness.Service.TryPay());
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        Assert.Equal(0, harness.World.DebitCalls);
        Assert.Equal(5_000d, harness.Record!.LastShortfallNoticed);
        var revisionAfterFirst = harness.Record!.Revision;

        Assert.False(harness.Service.TryPay());
        Assert.Equal(revisionAfterFirst, harness.Record!.Revision);

        harness.World.CashBalance = 12_000f;
        Assert.False(harness.Service.TryPay());
        Assert.Equal(3_000d, harness.Record!.LastShortfallNoticed);
        Assert.True(harness.Record!.Revision > revisionAfterFirst);
        Assert.Equal(0, harness.World.DebitCalls);
    }

    [Fact]
    public void Sufficient_cash_prepares_debits_exactly_once_and_marks_applied()
    {
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;

        var revisionBeforePay = harness.Record!.Revision;
        var result = harness.Service.TryPay();

        Assert.True(result);
        var effect = Assert.Single(harness.Story.State!.NativeEffects);
        Assert.Equal(Release1ChiefEffect.EffectId(1, revisionBeforePay), effect.EffectId);
        Assert.Equal(Release1MissionCatalog.ChiefCampbell, effect.MissionKey);
        Assert.Equal(1, effect.Attempt);
        Assert.Equal("CashTransfer", effect.EffectKind);
        Assert.Equal("player-wallet", effect.SourceIdentity);
        Assert.Equal("chief-campbell", effect.DestinationIdentity);
        Assert.Equal("15000", effect.AmountOrCargoIdentity);
        Assert.Equal(Release1NativeEffectPhase.Applied, effect.Phase);
        Assert.Equal(1, harness.World.DebitCalls);
        Assert.Equal(-15_000f, harness.World.LastDebitAmount);
        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
    }

    [Fact]
    public void The_wipe_runs_on_the_pass_after_applied_and_never_twice()
    {
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;
        Assert.True(harness.Service.TryPay());
        Assert.Equal(0, harness.Ledger.WipeCalls);

        harness.Service.Update();

        Assert.Equal(1, harness.Ledger.WipeCalls);
        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);

        harness.Service.Update();
        Assert.Equal(1, harness.Ledger.WipeCalls);
    }

    [Fact]
    public void A_refused_wipe_retries_without_ever_repeating_the_debit()
    {
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;
        Assert.True(harness.Service.TryPay());
        harness.Ledger.WipeResult = new(false, LocalPressureEvidenceWriteRejectReason.NotAuthoritativeHost, null, "not yet");

        harness.Service.Update();

        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Story.State!.NativeEffects.Single().Phase);
        Assert.Equal(1, harness.World.DebitCalls);

        harness.Ledger.WipeResult = new(true, LocalPressureEvidenceWriteRejectReason.None, null, "ok");
        harness.World.TotalMinutes = (harness.World.TotalMinutes ?? 0d) + 1d;
        harness.Service.Update();

        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);
        Assert.Equal(1, harness.World.DebitCalls);
    }

    [Fact]
    public void An_unavailable_debit_is_logged_blocked_and_reopens_the_demand_without_taking_any_money()
    {
        // OC-73 review fix. Release1ChiefService.RunPay used to treat every non-Succeeded debit status
        // the same: reopen the demand, on the theory that TryDebitCashBalance only ever mutates the
        // wallet on Succeeded. That is true of Unavailable (its own pre balance read never completed,
        // so the native mutation call was never even reached) but not of Ambiguous, which
        // S1ApiRelease1SmallCourtesyWorld.TryDebitCashBalance returns only from the catch wrapping the
        // native mutation call itself: the money may already be gone. RunPay now reopens only for
        // Unavailable (this test), and blocks everything else in place instead of reopening (the next
        // test) so a second Pay press can never debit the wallet twice for one demand. Either way the
        // effect is blocked (TryRecordNativeEffectIssuance with Ambiguous) so it is never revisited.
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;
        harness.World.DebitResult = Release1SmallCourtesyWorldMutationStatus.Unavailable;
        var balanceBeforePay = harness.World.CashBalance;

        var warnings = new List<string>();
        var previous = OrganizedCrimeLog.Warning;
        OrganizedCrimeLog.Warning = warnings.Add;
        bool result;
        try { result = harness.Service.TryPay(); }
        finally { OrganizedCrimeLog.Warning = previous; }

        Assert.False(result);
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        Assert.Equal(1, harness.Record!.DemandRound);
        Assert.Equal(balanceBeforePay, harness.World.CashBalance);
        Assert.Equal(1, harness.World.DebitCalls);
        // The unavailable debit's own effect is left Prepared but permanently ExecutionBlocked, and
        // orphaned from the reopened, still-unauthorized-for-this-effect record: unauthorized because
        // Release1StoryRuntimeService.IsCurrentEffectAuthorization requires both the matching revision
        // and a Paying or Settled state, neither of which this reopened DemandOpen record satisfies
        // any more; blocked so a later Settled-plus-arrest reset of ChiefRecord.NativeEffectIds can
        // never orphan a still-live link (Release1StoryState.Validate's closedCycle relaxation now
        // covers a blocked effect exactly as it already covers a Committed one).
        var orphaned = Assert.Single(harness.Story.State!.NativeEffects);
        Assert.Equal(Release1NativeEffectPhase.Prepared, orphaned.Phase);
        Assert.True(orphaned.ExecutionBlocked);
        Assert.Contains(warnings, w => w.Contains("debit", StringComparison.OrdinalIgnoreCase));

        // The player presses Pay again: a fresh effect, a real debit, and it lands normally.
        harness.World.DebitResult = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        var secondPay = harness.Service.TryPay();

        Assert.True(secondPay);
        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
        Assert.Equal(2, harness.World.DebitCalls);
        Assert.Equal(2, harness.Story.State!.NativeEffects.Count);
        Assert.Equal(balanceBeforePay - 15_000f, harness.World.CashBalance);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Story.State!.NativeEffects.Single(e => e.EffectId != orphaned.EffectId).Phase);
    }

    [Theory]
    [InlineData(Release1SmallCourtesyWorldMutationStatus.Ambiguous)]
    [InlineData(Release1SmallCourtesyWorldMutationStatus.Rejected)]
    public void An_ambiguous_or_rejected_debit_blocks_the_effect_and_never_reopens_the_demand(Release1SmallCourtesyWorldMutationStatus debitResult)
    {
        // OC-73 review fix (finding 1). Unlike Unavailable, neither Ambiguous nor a native Rejected
        // proves the wallet was untouched, so RunPay must not hand the demand back for a second Pay
        // press: that would risk a second debit for the same money. It blocks the prepared effect
        // instead (ExecutionBlocked, permanently) and leaves the record Paying, matching
        // Release1SmallCourtesyMissionService.ApplyPreparedReward's own convention (Unavailable alone
        // retries; every other outcome blocks).
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;
        harness.World.DebitResult = debitResult;
        var balanceBeforePay = harness.World.CashBalance;

        var warnings = new List<string>();
        var previous = OrganizedCrimeLog.Warning;
        OrganizedCrimeLog.Warning = warnings.Add;
        bool result;
        try { result = harness.Service.TryPay(); }
        finally { OrganizedCrimeLog.Warning = previous; }

        Assert.False(result);
        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
        Assert.Equal(balanceBeforePay, harness.World.CashBalance);
        Assert.Equal(1, harness.World.DebitCalls);
        var blocked = Assert.Single(harness.Story.State!.NativeEffects);
        Assert.Equal(Release1NativeEffectPhase.Prepared, blocked.Phase);
        Assert.True(blocked.ExecutionBlocked);
        Assert.Contains(warnings, w => w.Contains("debit", StringComparison.OrdinalIgnoreCase));

        // A second Pay press does nothing at all: the record never left Paying, so RunPay's own
        // DemandOpen guard refuses it before ever touching the world again.
        var secondPay = harness.Service.TryPay();
        Assert.False(secondPay);
        Assert.Equal(1, harness.World.DebitCalls);
        Assert.Equal(balanceBeforePay, harness.World.CashBalance);

        // The convergence pass holds the blocked effect forever rather than retrying it as an
        // unconfirmed payment: no further debit, no MarkApplied, no state change.
        harness.World.TotalMinutes = (harness.World.TotalMinutes ?? 0d) + 1d;
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
        Assert.Equal(1, harness.World.DebitCalls);
        Assert.Equal(Release1NativeEffectPhase.Prepared, harness.Story.State!.NativeEffects.Single().Phase);
    }

    [Theory]
    [InlineData(Release1SmallCourtesyWorldMutationStatus.Ambiguous)]
    [InlineData(Release1SmallCourtesyWorldMutationStatus.Rejected)]
    public void A_permanently_blocked_debit_notifies_the_player_exactly_once(Release1SmallCourtesyWorldMutationStatus debitResult)
    {
        // OC-73 review fix (finding 2). The player must be told the demand still stands once a
        // payment's debit is permanently blocked (Ambiguous or Rejected, never Unavailable, which
        // reopens the demand in the same call instead). PaymentBlockedNotices is a durable, non-
        // resetting counter (BlockEffect only bumps it the first time this exact effect is durably
        // blocked), so a later convergence pass holding the same blocked effect must not send a
        // second copy of the line.
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;
        harness.World.DebitResult = debitResult;

        Assert.False(harness.Service.TryPay());
        Assert.Equal(1, harness.Record!.PaymentBlockedNotices);
        var plan = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        Assert.Contains(plan.Messages, m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.PaymentBlockedText));

        // A later pass still holds the same permanently blocked effect: no second notice.
        harness.World.TotalMinutes = (harness.World.TotalMinutes ?? 0d) + 1d;
        harness.Service.Update();
        Assert.Equal(1, harness.Record!.PaymentBlockedNotices);
    }

    [Fact]
    public void An_unavailable_debit_reopens_the_demand_without_a_blocked_notice()
    {
        // OC-73 review fix (finding 2). Unavailable is excluded from the notice: RunPay reopens the
        // demand in this same call (BlockEffect is called with notifyPlayer: false), so the decision
        // prompt itself already shows the demand still open and a second "it stands" line would be
        // redundant.
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;
        harness.World.DebitResult = Release1SmallCourtesyWorldMutationStatus.Unavailable;

        Assert.False(harness.Service.TryPay());
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        Assert.Equal(0, harness.Record!.PaymentBlockedNotices);
    }

    [Theory]
    [InlineData(Release1SmallCourtesyWorldMutationStatus.Ambiguous)]
    [InlineData(Release1SmallCourtesyWorldMutationStatus.Rejected)]
    public void A_blocked_debit_with_a_lockdown_engaged_still_lifts_on_cooling(Release1SmallCourtesyWorldMutationStatus debitResult)
    {
        // OC-73 review fix (finding 2). ContinuePayment used to return straight out of Update() the
        // instant it found a permanently blocked effect (effect.ExecutionBlocked), which also skipped
        // ReconcileLockdown for that whole pass, forever: a lockdown already engaged before the blocked
        // payment could never lift again, not by payment (RunPay refuses; the record is not
        // DemandOpen) and not by cooling (ReconcileLockdown was unreachable). This pins that the
        // cooled-lift path is reached, and eventually fires, even while the blocked effect is held.
        using var harness = Harness.Create();
        EngageLockdown(harness);

        harness.World.CashBalance = 100_000f;
        harness.World.DebitResult = debitResult;
        Assert.False(harness.Service.TryPay());
        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
        Assert.True(harness.Record!.LockdownEngaged);
        var blocked = Assert.Single(harness.Story.State!.NativeEffects);
        Assert.True(blocked.ExecutionBlocked);

        // Tier is still Critical: the lockdown must keep holding, not release, since nothing has
        // actually cooled yet.
        Tick(harness);
        Assert.True(harness.Record!.LockdownEngaged);
        Assert.Equal(0, harness.Record!.CooledLifts);

        // Now it cools below Watched.
        harness.Ledger.State = new LocalPressureState(10, true, null, null, null, harness.PlayerId, null, null, harness.Ledger.State!.Revision + 1);
        Tick(harness);

        Assert.False(harness.Record!.LockdownEngaged);
        Assert.Equal(1, harness.Record!.CooledLifts);
        Assert.Equal(1, harness.World.ReleaseCalls);
        // The payment itself stays permanently blocked (money safety, finding 1's own fix, untouched):
        // only the lockdown lifts.
        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
        Assert.Equal(1, harness.World.DebitCalls);
        var plan = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        Assert.Contains(plan.Messages, m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LiftCooledText));
    }

    [Fact]
    public void A_refused_blocking_write_is_not_later_marked_applied()
    {
        // OC-73 review fix (finding 2). If the blocking write itself is refused right after a
        // non-Succeeded debit (here: a save starts in the same instant, so the block's own gate check
        // sees DeferredSaving), the effect is left Prepared and not ExecutionBlocked, exactly as a
        // genuinely successful, unconfirmed debit would leave it. Before this fix, ContinuePayment
        // treated any Prepared, non-blocked effect on a Paying record as "the debit already returned
        // Succeeded, just retry the confirm", and would have marked this one Applied on the very next
        // pass, for money that was never confirmed to move.
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;
        harness.World.DebitResult = Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        harness.World.OnDebitCalled = () => harness.Story.OnSaveStart();

        var warnings = new List<string>();
        var previous = OrganizedCrimeLog.Warning;
        OrganizedCrimeLog.Warning = warnings.Add;
        bool result;
        try { result = harness.Service.TryPay(); }
        finally { OrganizedCrimeLog.Warning = previous; }

        Assert.False(result);
        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
        var effect = Assert.Single(harness.Story.State!.NativeEffects);
        Assert.Equal(Release1NativeEffectPhase.Prepared, effect.Phase);
        Assert.False(effect.ExecutionBlocked);
        Assert.Contains(warnings, w => w.Contains("refused", StringComparison.OrdinalIgnoreCase));

        // The save completes; a later pass must retry the block, never mark Applied, and never
        // re-debit.
        harness.World.OnDebitCalled = null;
        harness.Story.OnSaveComplete();
        harness.Service.OnSaveComplete();
        harness.World.TotalMinutes = (harness.World.TotalMinutes ?? 0d) + 1d;
        harness.Service.Update();

        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
        var retried = harness.Story.State!.NativeEffects.Single();
        Assert.Equal(Release1NativeEffectPhase.Prepared, retried.Phase);
        Assert.True(retried.ExecutionBlocked);
        Assert.Equal(1, harness.World.DebitCalls);
    }

    [Fact]
    public void An_abandoned_debit_that_later_pays_and_settles_survives_the_next_arrest()
    {
        // OC-73 review fix (finding 2). Release1ChiefLedger.AbandonUnappliedPayment reopens the demand
        // but leaves its own now-blocked effect in the journal and (until the ladder restart) in
        // ChiefRecord.NativeEffectIds. A later successful payoff appends a second effect and settles;
        // the next arrest's Adopted-or-Settled reset then clears NativeEffectIds and
        // AcceptedLogicalCorrelations entirely. Before this fix, Release1StoryState.Validate's Chief
        // branch could no longer find the abandoned effect linked to the record and threw, which
        // TrySetChiefRecord (via Release1StoryRuntimeService) caught and rejected, wedging every
        // arrest after it. The widened closedCycle relaxation (Committed or ExecutionBlocked) is what
        // survives this.
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;

        // Abandon one debit (known safe: Unavailable), leaving a blocked, orphaned effect behind.
        harness.World.DebitResult = Release1SmallCourtesyWorldMutationStatus.Unavailable;
        Assert.False(harness.Service.TryPay());
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        var abandoned = Assert.Single(harness.Story.State!.NativeEffects);
        Assert.True(abandoned.ExecutionBlocked);

        // Pay successfully this time.
        harness.World.DebitResult = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        Assert.True(harness.Service.TryPay());
        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);

        // Settle: the pass after Applied wipes the ledger and moves the record to Settled.
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);

        // Commit the payoff effect after a save: ContinuePayment holds a Settled record with an
        // Applied-but-not-yet-Committed effect on every pass, so the arrest below could never reach
        // Observe at all without this, exactly as a real playthrough would need the save first.
        harness.Story.OnSaveStart();
        harness.Story.OnSaveComplete();
        harness.Service.OnSaveComplete();
        harness.Service.Update();
        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Story.State!.NativeEffects.Single(e => e.EffectId != abandoned.EffectId).Phase);

        // Arrest again: this used to be rejected forever once the reset outran Validate. It must now
        // reopen the demand cleanly at round 1, with the abandoned effect still sitting, untouched and
        // still blocked, in the journal.
        var warnings = new List<string>();
        var previous = OrganizedCrimeLog.Warning;
        OrganizedCrimeLog.Warning = warnings.Add;
        try
        {
            harness.Arrest();
            harness.Service.Update();
        }
        finally { OrganizedCrimeLog.Warning = previous; }

        Assert.DoesNotContain(warnings, w => w.Contains("rejected", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        Assert.Equal(1, harness.Record!.DemandRound);
        Assert.Empty(harness.Record!.NativeEffectIds);
        Assert.Empty(harness.Record!.AcceptedLogicalCorrelations);
        Assert.Equal(2, harness.Story.State!.NativeEffects.Count);
        Assert.Contains(harness.Story.State!.NativeEffects, e => e.EffectId == abandoned.EffectId && e.ExecutionBlocked);

        // The ladder keeps working normally afterward, not wedged: decline the reopened demand and
        // arrest once more, which must still advance the round exactly as it always has.
        Assert.True(harness.Service.TryDecline());
        harness.Arrest();
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        Assert.Equal(2, harness.Record!.DemandRound);
    }

    [Fact]
    public void An_interrupted_read_back_after_a_succeeded_debit_retries_and_completes_without_a_second_debit()
    {
        // OC-73 review fix. Once the debit itself has returned Succeeded, cash is already gone; RunPay
        // used to simply return false and leave the record Paying with its effect Prepared forever,
        // since ContinuePayment only ever looked at effects already Applied. The convergence pass now
        // retries exactly the read-back-and-mark-applied tail on this Prepared effect (never the
        // debit) until it completes.
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;
        harness.World.ReadCashResultAfterDebit = Release1SmallCourtesyWorldReadStatus.Unavailable;

        var warnings = new List<string>();
        var previous = OrganizedCrimeLog.Warning;
        OrganizedCrimeLog.Warning = warnings.Add;
        bool firstPay;
        try { firstPay = harness.Service.TryPay(); }
        finally { OrganizedCrimeLog.Warning = previous; }

        Assert.False(firstPay);
        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
        Assert.Equal(1, harness.World.DebitCalls);
        Assert.Equal(-15_000f, harness.World.LastDebitAmount);
        Assert.Equal(100_000f - 15_000f, harness.World.CashBalance);
        Assert.Equal(Release1NativeEffectPhase.Prepared, harness.Story.State!.NativeEffects.Single().Phase);
        Assert.Contains(warnings, w => w.Contains("read-back", StringComparison.OrdinalIgnoreCase));

        // The native hiccup clears; the next convergence pass retries and completes, never redebiting.
        harness.World.ReadCashResultAfterDebit = null;
        harness.World.TotalMinutes = (harness.World.TotalMinutes ?? 0d) + 1d;
        harness.Service.Update();

        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Story.State!.NativeEffects.Single().Phase);
        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
        Assert.Equal(1, harness.World.DebitCalls);
        Assert.Equal(85_000f, harness.World.CashBalance);
    }

    [Fact]
    public void OnSaveComplete_then_one_pass_commits_and_reads_settled_with_the_paid_line()
    {
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;
        Assert.True(harness.Service.TryPay());
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Story.State!.NativeEffects.Single().Phase);

        harness.Story.OnSaveStart();
        harness.Story.OnSaveComplete();
        harness.Service.OnSaveComplete();
        harness.Service.Update();

        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Story.State!.NativeEffects.Single().Phase);
        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);
        var plan = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        Assert.Contains(plan.Messages, m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.PaidText));
    }

    // OC-73 settled drain fix. Proven live on the installed Release build: the player paid, was
    // arrested again the same in game day, got no new demand, then saved and only then received the
    // queued messages. Root cause: ContinuePayment's old Settled branch returned true on every pass
    // regardless of outcome, which skipped DrainOne and ReconcileLockdown for the whole window
    // between the settle and the next save.

    [Fact]
    public void A_settled_payoff_still_awaiting_commit_drains_the_next_arrest_with_no_save_in_between()
    {
        // This is the live symptom itself: before the fix, ContinuePayment's Settled branch returned
        // true on every pass (waiting for a save to commit the payoff), which skipped DrainOne for the
        // whole window, so the queued arrest never opened a new demand until a save finally happened.
        // This test must fail before the fix (the record stays Settled) and pass after it.
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;
        Assert.True(harness.Service.TryPay());
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Story.State!.NativeEffects.Single().Phase);

        harness.Arrest();
        harness.Service.Update();

        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        Assert.Equal(1, harness.Record!.DemandRound);
        var plan = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        Assert.NotNull(plan.Decision);
        Assert.Equal(Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.FirstDemandText), plan.Decision!.Prompt);
        // No save has happened anywhere in this test: the payoff effect is exactly where TryPay left
        // it, proving the demand reopened without waiting for one.
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Story.State!.NativeEffects.Single().Phase);
    }

    [Fact]
    public void The_earlier_payoff_still_commits_after_a_save_even_though_the_record_has_moved_on()
    {
        // OC-73 settled drain fix. Once the fix above lets DrainOne run again before the next save,
        // Release1ChiefLedger.Observe's Adopted-or-Settled ArrestObserved branch resets DemandRound to
        // 1 and clears both AcceptedLogicalCorrelations and NativeEffectIds, so the payoff effect can
        // no longer be found through the record's own NativeEffectIds by the time a save finally
        // lands. CommitAppliedChiefCashEffects finds it by its own id prefix instead, and
        // Release1StoryRuntimeService.IsCurrentEffectAuthorization's widened Applied branch authorizes
        // the commit even though the live record is now DemandOpen at round one with an empty
        // correlation list.
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;
        Assert.True(harness.Service.TryPay());
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);
        var payoffEffectId = harness.Story.State!.NativeEffects.Single().EffectId;

        harness.Arrest();
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        Assert.Equal(1, harness.Record!.DemandRound);
        Assert.Empty(harness.Record!.NativeEffectIds);
        Assert.Empty(harness.Record!.AcceptedLogicalCorrelations);

        harness.Story.OnSaveStart();
        harness.Story.OnSaveComplete();
        harness.Service.OnSaveComplete();
        harness.Service.Update();

        var payoffEffect = harness.Story.State!.NativeEffects.Single(e => e.EffectId == payoffEffectId);
        Assert.Equal(Release1NativeEffectPhase.Committed, payoffEffect.Phase);
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        Assert.Equal(1, harness.Record!.DemandRound);
    }

    [Fact]
    public void A_settled_payoff_still_awaiting_commit_still_announces_and_engages_a_fresh_lockdown_with_no_save()
    {
        // OC-73 settled drain fix. Before the fix, a Settled record with its payoff effect still
        // Applied and uncommitted made ContinuePayment short circuit every pass, so ReconcileLockdown
        // (and DrainOne, the only path to the announcement in the first place) never ran until the
        // next save. This pins that the announce then engage ladder still runs against a fresh
        // arrest's own Critical crossing on later passes, with no save anywhere in the test.
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;
        Assert.True(harness.Service.TryPay());
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Story.State!.NativeEffects.Single().Phase);
        var lastPersistedRevisionBefore = harness.Story.LastPersistedRevision;

        harness.Ledger.State = new LocalPressureState(80, true, null, null, null, harness.PlayerId, null, null, harness.Ledger.State!.Revision + 1);
        harness.Arrest(LocalPressureTier.Watched, LocalPressureTier.Critical);
        harness.Service.Update(); // drains ArrestObserved: Settled moves to DemandOpen at round one
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);

        Tick(harness); // drains RoseToCritical: records the announcement
        Assert.Equal(1, harness.Record!.LockdownAnnouncements);
        Assert.False(harness.Record!.LockdownEngaged);

        var correlation = Release1ChiefPresentation.LockdownAnnouncementCorrelation(harness.PlayerId, harness.Record!);
        Assert.True(harness.Story.TryRecordPresentationReceipt(correlation).Accepted);

        Tick(harness);

        Assert.True(harness.Record!.LockdownEngaged);
        Assert.Equal(1, harness.World.EngageCalls);
        // No save happened anywhere in this test (past the harness's own setup): the earlier payoff
        // effect is still Applied, not Committed, and LastPersistedRevision never advanced past its
        // own setup value, proving the announce and engage ladder never waited for a save.
        Assert.Equal(Release1NativeEffectPhase.Applied, harness.Story.State!.NativeEffects.Single().Phase);
        Assert.Equal(lastPersistedRevisionBefore, harness.Story.LastPersistedRevision);
    }

    [Fact]
    public void Two_updates_in_the_same_minute_with_nothing_new_converge_to_one_stable_pass()
    {
        using var harness = Harness.Create();
        harness.World.TotalMinutes = 500d;
        harness.Service.Update();
        harness.Service.Update();
        var stableRevision = harness.Story.State!.Revision;

        harness.Service.Update();

        Assert.Equal(stableRevision, harness.Story.State!.Revision);
    }

    [Fact]
    public void Lifecycle_boundaries_clear_the_throttle_stamp_and_let_a_later_pass_converge()
    {
        using var harness = Harness.Create();
        harness.World.TotalMinutes = 10d;
        harness.Service.Update();
        harness.Service.Update();
        var revisionBefore = harness.Story.State!.Revision;

        harness.Service.OnSaveComplete();
        harness.Arrest();
        harness.Service.Update();

        Assert.True(harness.Story.State!.Revision > revisionBefore);
    }

    [Fact]
    public void A_null_ledger_read_leaves_the_record_and_the_world_untouched_and_retries_later()
    {
        using var harness = Harness.Create();
        harness.Ledger.State = null;

        harness.Service.Update();

        Assert.Null(harness.Record);
        Assert.Equal(0, harness.World.ReadCashCalls);
        Assert.Equal(0, harness.World.DebitCalls);

        harness.Ledger.State = LocalPressureState.Quiet(harness.PlayerId);
        harness.World.TotalMinutes = (harness.World.TotalMinutes ?? 0d) + 1d;
        harness.Service.Update();

        Assert.NotNull(harness.Record);
    }

    [Fact]
    public void A_non_active_story_phase_leaves_everything_untouched()
    {
        using var harness = Harness.Create();
        harness.Story.OnSaveStart();

        harness.Service.Update();

        Assert.Null(harness.Record);
        Assert.Equal(0, harness.World.ReadCashCalls);
    }

    [Fact]
    public void Standing_and_missions_stay_byte_identical_across_the_whole_pay_flow()
    {
        using var harness = Harness.Create();
        var standingBefore = harness.Story.State!.Standing;
        var missionsBefore = harness.Story.State!.Missions;

        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;
        Assert.True(harness.Service.TryPay());
        harness.Service.Update();

        Assert.Equal(standingBefore, harness.Story.State!.Standing);
        Assert.Equal(missionsBefore.Count, harness.Story.State!.Missions.Count);
        for (var i = 0; i < missionsBefore.Count; i++)
            Assert.True(missionsBefore[i].ValueEquals(harness.Story.State!.Missions[i]));
    }

    [Fact]
    public void A_second_full_payoff_cycle_at_round_one_reaches_demand_open_and_accepts_pay_again()
    {
        // OC-73 review regression. Paying once used to permanently break every future payoff at the
        // same round: the effect id reused the same string every cycle, so the second TryPay's
        // prepare found the first cycle's already-Committed entry and was silently rejected.
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        harness.World.CashBalance = 100_000f;

        Assert.True(harness.Service.TryPay());
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);

        harness.Story.OnSaveStart();
        harness.Story.OnSaveComplete();
        harness.Service.OnSaveComplete();
        harness.Service.Update();
        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Story.State!.NativeEffects.Single().Phase);

        harness.Arrest();
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        Assert.Equal(1, harness.Record!.DemandRound);

        var secondPay = harness.Service.TryPay();

        Assert.True(secondPay);
        Assert.Equal(2, harness.World.DebitCalls);
        Assert.Equal(2, harness.Story.State!.NativeEffects.Count);
        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
    }

    [Fact]
    public void A_second_full_payoff_cycle_after_a_round_two_settle_reaches_demand_open_and_accepts_pay_again()
    {
        // OC-73 review regression. A payoff settled at round 2 or 3 used to jam the demand shut
        // forever: the reset record's retained round-2/3 correlation and effect id could never
        // satisfy Release1ChiefRecord.Validate once the round reset to 1.
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        Assert.Equal(1, harness.Record!.DemandRound);

        Assert.True(harness.Service.TryDecline());
        harness.Arrest();
        harness.Service.Update();
        Assert.Equal(2, harness.Record!.DemandRound);

        harness.World.CashBalance = 100_000f;
        Assert.True(harness.Service.TryPay());
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);

        harness.Story.OnSaveStart();
        harness.Story.OnSaveComplete();
        harness.Service.OnSaveComplete();
        harness.Service.Update();
        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Story.State!.NativeEffects.Single().Phase);

        harness.Arrest();
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        Assert.Equal(1, harness.Record!.DemandRound);

        var secondPay = harness.Service.TryPay();

        Assert.True(secondPay);
        Assert.Equal(2, harness.World.DebitCalls);
        Assert.Equal(2, harness.Story.State!.NativeEffects.Count);
        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
    }

    [Fact]
    public void A_rejected_chief_record_write_in_drain_one_is_logged_instead_of_silently_discarded()
    {
        // OC-73 review fix. DrainOne used to discard TrySetChiefRecord's result outright, so a
        // rejected write (a stale revision, a failed Validate) froze the record with no signal
        // anywhere. This pins that a rejection is now surfaced through OrganizedCrimeLog.Warning.
        // Reproduced with a genuinely stale snapshot (every individual decision below is otherwise
        // valid on its own): DrainOne is invoked directly (it is private; every other path to it
        // goes through Update(), which re-reads state.ChiefRecord fresh and so can never hand it a
        // stale record within one call) with a record captured before the live one advanced twice
        // underneath it, exactly the shape a future regression in the calling order could reintroduce.
        using var harness = Harness.Create();
        harness.Service.Update();
        harness.Arrest();
        harness.Service.Update();
        Assert.True(harness.Service.TryDecline());
        var staleRecord = harness.Record!;
        Assert.Equal(Release1ChiefState.Declined, staleRecord.State);
        Assert.Equal(1, staleRecord.DemandRound);

        // Advance the live record twice more past the stale snapshot, so replaying the stale
        // snapshot's own next decision (a plain, individually valid Declined-plus-arrest advance)
        // lands on a revision far behind the live one instead of coincidentally re-deriving it.
        harness.Arrest();
        harness.Service.Update();
        Assert.True(harness.Service.TryDecline());
        harness.Arrest();
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
        Assert.Equal(3, harness.Record!.DemandRound);
        harness.Arrest();

        var warnings = new List<string>();
        var previous = OrganizedCrimeLog.Warning;
        OrganizedCrimeLog.Warning = warnings.Add;
        try
        {
            InvokeDrainOne(harness.Service, staleRecord, harness.PlayerId, harness.Context.Snapshot.SessionEpoch, harness.Context.Snapshot.LoadEpoch);
            Assert.Contains(warnings, w => w.Contains("Chief record write was rejected", StringComparison.Ordinal));
            // The rejected write never touched the live, already-advanced record.
            Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);
            Assert.Equal(3, harness.Record!.DemandRound);
        }
        finally { OrganizedCrimeLog.Warning = previous; }
    }

    // OC-73 Task 4. The lockdown engage and lift path on the proven curfew seam.

    [Fact]
    public void The_lockdown_only_engages_once_its_own_announcement_receipt_exists()
    {
        using var harness = Harness.Create();
        harness.Ledger.State = new LocalPressureState(80, true, null, null, null, harness.PlayerId, null, null, 0);
        harness.Service.Update();
        harness.Arrest(LocalPressureTier.Watched, LocalPressureTier.Critical);
        harness.Service.Update();
        Assert.Equal(Release1ChiefState.DemandOpen, harness.Record!.State);

        Tick(harness);
        Assert.Equal(1, harness.Record!.LockdownAnnouncements);
        Assert.False(harness.Record!.LockdownEngaged);
        Assert.Equal(0, harness.World.EngageCalls);

        // No receipt yet: further passes at the same state still do not engage.
        Tick(harness);
        Assert.Equal(0, harness.World.EngageCalls);
        Assert.False(harness.Record!.LockdownEngaged);

        var correlation = Release1ChiefPresentation.LockdownAnnouncementCorrelation(harness.PlayerId, harness.Record!);
        Assert.True(harness.Story.TryRecordPresentationReceipt(correlation).Accepted);

        Tick(harness);
        Assert.Equal(1, harness.World.EngageCalls);
        Assert.True(harness.Record!.LockdownEngaged);
    }

    [Fact]
    public void Announcing_and_engaging_never_share_a_pass()
    {
        using var harness = Harness.Create();
        harness.Ledger.State = new LocalPressureState(80, true, null, null, null, harness.PlayerId, null, null, 0);
        harness.Service.Update();
        harness.Arrest(LocalPressureTier.Watched, LocalPressureTier.Critical);
        harness.Service.Update();

        Tick(harness); // this is the pass that increments LockdownAnnouncements

        Assert.Equal(1, harness.Record!.LockdownAnnouncements);
        Assert.False(harness.Record!.LockdownEngaged);
        Assert.Equal(0, harness.World.EngageCalls);
    }

    [Fact]
    public void An_unavailable_engage_holds_once_per_load_and_retries_without_retracting_the_announcement()
    {
        using var harness = Harness.Create();
        harness.Ledger.State = new LocalPressureState(80, true, null, null, null, harness.PlayerId, null, null, 0);
        harness.Service.Update();
        harness.Arrest(LocalPressureTier.Watched, LocalPressureTier.Critical);
        harness.Service.Update();
        Tick(harness);
        var correlation = Release1ChiefPresentation.LockdownAnnouncementCorrelation(harness.PlayerId, harness.Record!);
        Assert.True(harness.Story.TryRecordPresentationReceipt(correlation).Accepted);

        harness.World.EngageResult = Release1SmallCourtesyWorldMutationStatus.Unavailable;
        harness.World.EngageReason = "NEEDS OPTION B: curfew could not be enabled this pass.";

        var warnings = new List<string>();
        var previous = OrganizedCrimeLog.Warning;
        OrganizedCrimeLog.Warning = warnings.Add;
        try
        {
            Tick(harness);
            Assert.Equal(1, harness.World.EngageCalls);
            Assert.False(harness.Record!.LockdownEngaged);
            Assert.Equal(1, harness.Record!.LockdownAnnouncements);

            Tick(harness);
            Assert.Equal(2, harness.World.EngageCalls);
            Assert.False(harness.Record!.LockdownEngaged);
            Assert.Single(warnings);
        }
        finally { OrganizedCrimeLog.Warning = previous; }

        var plan = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        Assert.Contains(plan.Messages, m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LockdownAnnouncementText));
    }

    [Fact]
    public void Settling_while_under_lockdown_releases_it_through_the_world_and_counts_the_paid_lift()
    {
        using var harness = Harness.Create();
        EngageLockdown(harness);

        harness.World.CashBalance = 100_000f;
        Assert.True(harness.Service.TryPay());

        Tick(harness); // the wipe, the settle, and (Task 4) the world release all land on this pass

        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);
        Assert.False(harness.Record!.LockdownEngaged);
        Assert.Equal(1, harness.Record!.PaidLifts);
        Assert.Equal(1, harness.World.ReleaseCalls);
        var plan = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        Assert.Contains(plan.Messages, m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LiftPaidText));
    }

    [Fact]
    public void Cooling_below_watched_releases_the_lockdown_through_the_world_and_counts_the_cooled_lift()
    {
        using var harness = Harness.Create();
        EngageLockdown(harness);

        // Decay is read from the ledger on the pass, never from a queued event.
        harness.Ledger.State = new LocalPressureState(10, true, null, null, null, harness.PlayerId, null, null, harness.Ledger.State!.Revision + 1);
        Tick(harness);

        Assert.False(harness.Record!.LockdownEngaged);
        Assert.Equal(1, harness.Record!.CooledLifts);
        Assert.Equal(1, harness.World.ReleaseCalls);
        var plan = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        Assert.Contains(plan.Messages, m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LiftCooledText));
    }

    [Fact]
    public void A_restart_with_the_flag_persisted_re_engages_with_no_second_announcement()
    {
        using var harness = Harness.Create();
        EngageLockdown(harness);
        var announcementsBefore = harness.Record!.LockdownAnnouncements;

        // Simulate a restart: a new service instance (fresh throttle, fresh world call counters) over
        // the same persisted story state, the ledger still Critical and the demand still unpaid, the
        // native gate not engaged this session (a fresh ChiefFakeWorld starts at zero calls).
        using var restart = RestartOver(harness);
        Tick(restart);

        Assert.Equal(1, restart.World.EngageCalls);
        Assert.Equal(announcementsBefore, harness.Record!.LockdownAnnouncements);
        Assert.True(harness.Record!.LockdownEngaged);
        Assert.Equal(0, harness.Record!.PaidLifts);
        Assert.Equal(0, harness.Record!.CooledLifts);
    }

    [Fact]
    public void A_restart_that_finds_the_debit_already_applied_settles_and_releases_through_the_world()
    {
        using var harness = Harness.Create();
        EngageLockdown(harness);
        harness.World.CashBalance = 100_000f;
        Assert.True(harness.Service.TryPay());
        Assert.Equal(Release1ChiefState.Paying, harness.Record!.State);
        Assert.True(harness.Record!.LockdownEngaged);

        // Restart before the wipe-and-settle pass ever ran: the persisted record is still Paying with
        // its debit already Applied, and the native gate reset by the process restart.
        using var restart = RestartOver(harness);
        Tick(restart);

        Assert.Equal(Release1ChiefState.Settled, harness.Record!.State);
        Assert.False(harness.Record!.LockdownEngaged);
        Assert.Equal(1, restart.World.ReleaseCalls);
        Assert.Equal(0, restart.World.EngageCalls);
    }

    [Fact]
    public void A_restart_with_the_ledger_read_back_below_watched_clears_the_lockdown()
    {
        using var harness = Harness.Create();
        EngageLockdown(harness);

        // Restart with heat having decayed below Watched while the session was closed.
        harness.Ledger.State = new LocalPressureState(10, true, null, null, null, harness.PlayerId, null, null, harness.Ledger.State!.Revision + 1);
        using var restart = RestartOver(harness);
        Tick(restart);

        Assert.False(harness.Record!.LockdownEngaged);
        Assert.Equal(1, harness.Record!.CooledLifts);
        Assert.Equal(1, restart.World.ReleaseCalls);
        Assert.Equal(0, restart.World.EngageCalls);
    }

    [Fact]
    public void A_refused_release_leaves_the_flag_set_and_retries_until_it_succeeds()
    {
        using var harness = Harness.Create();
        EngageLockdown(harness);

        harness.Ledger.State = new LocalPressureState(10, true, null, null, null, harness.PlayerId, null, null, harness.Ledger.State!.Revision + 1);
        harness.World.ReleaseResult = Release1SmallCourtesyWorldMutationStatus.Rejected;
        Tick(harness);

        Assert.True(harness.Record!.LockdownEngaged);
        Assert.Equal(0, harness.Record!.CooledLifts);
        Assert.Equal(1, harness.World.ReleaseCalls);
        var stillHeld = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        Assert.DoesNotContain(stillHeld.Messages, m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LiftCooledText));

        harness.World.ReleaseResult = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        Tick(harness);

        Assert.False(harness.Record!.LockdownEngaged);
        Assert.Equal(1, harness.Record!.CooledLifts);
        Assert.Equal(2, harness.World.ReleaseCalls);
        var lifted = Release1ChiefPresentation.BuildPlan(harness.Story.State);
        Assert.Contains(lifted.Messages, m => m.Text == Release1PlayerCopy.Normalize(Release1ChiefCampbellCopy.LiftCooledText));
    }

    /// <summary>Advances canonical game time by one minute and runs one convergence pass.</summary>
    private static void Tick(Harness harness)
    {
        harness.World.TotalMinutes = (harness.World.TotalMinutes ?? 0d) + 1d;
        harness.Service.Update();
    }

    private static void Tick(RestartHarness restart)
    {
        restart.World.TotalMinutes = (restart.World.TotalMinutes ?? 0d) + 1d;
        restart.Service.Update();
    }

    /// <summary>Arrests into Critical, announces, records the receipt, and engages: the shared setup every lift test starts from.</summary>
    private static void EngageLockdown(Harness harness)
    {
        harness.Ledger.State = new LocalPressureState(80, true, null, null, null, harness.PlayerId, null, null, 0);
        harness.Service.Update();
        harness.Arrest(LocalPressureTier.Watched, LocalPressureTier.Critical);
        harness.Service.Update();
        Tick(harness);
        var correlation = Release1ChiefPresentation.LockdownAnnouncementCorrelation(harness.PlayerId, harness.Record!);
        Assert.True(harness.Story.TryRecordPresentationReceipt(correlation).Accepted);
        Tick(harness);
        Assert.True(harness.Record!.LockdownEngaged);
    }

    /// <summary>
    /// A second Release1ChiefService over the same story runtime and the same ledger read, with a
    /// fresh world (call counters at zero) and a fresh throttle, exactly what a process restart hands
    /// the mod shell on the next load.
    /// </summary>
    private static RestartHarness RestartOver(Harness harness)
    {
        var world = new ChiefFakeWorld { CashBalance = harness.World.CashBalance, TotalMinutes = harness.World.TotalMinutes };
        var observer = new Release1ChiefTierObserver();
        var service = new Release1ChiefService(
            harness.Story, world, observer, harness.Ledger.Read, harness.Ledger.Wipe, harness.Ledger.Epochs, LocalPressureProfile.Moderate);
        return new RestartHarness(world, service);
    }

    private sealed class RestartHarness : IDisposable
    {
        public RestartHarness(ChiefFakeWorld world, Release1ChiefService service) { World = world; Service = service; }
        public ChiefFakeWorld World { get; }
        public Release1ChiefService Service { get; }
        public void Dispose() => Service.Dispose();
    }

    private static void InvokeDrainOne(Release1ChiefService service, Release1ChiefRecord record, string playerId, Guid sessionEpoch, long loadEpoch)
    {
        var method = typeof(Release1ChiefService).GetMethod("DrainOne", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        method!.Invoke(service, new object[] { record, playerId, sessionEpoch, loadEpoch });
    }

    private sealed class LedgerHarness
    {
        public LocalPressureState? State { get; set; }
        public int WipeCalls { get; private set; }
        public LocalPressureEvidenceWriteResult WipeResult { get; set; } =
            new(true, LocalPressureEvidenceWriteRejectReason.None, null, "wiped");
        public Guid SessionEpoch { get; set; }
        public long LoadEpoch { get; set; }

        public LocalPressureState? Read(string playerId) => State;

        public LocalPressureEvidenceWriteResult Wipe(string playerId)
        {
            WipeCalls++;
            return WipeResult;
        }

        public bool Epochs(out Guid sessionEpoch, out long loadEpoch)
        {
            sessionEpoch = SessionEpoch;
            loadEpoch = LoadEpoch;
            return true;
        }
    }

    private sealed class ChiefFakeWorld : Release1ProductionOnlyWorld
    {
        public float CashBalance { get; set; } = 100_000f;
        public double? TotalMinutes { get; set; } = 0d;
        public int ReadCashCalls { get; private set; }
        public int DebitCalls { get; private set; }
        public float? LastDebitAmount { get; private set; }
        public Release1SmallCourtesyWorldMutationStatus DebitResult { get; set; } = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        // OC-73 review fix. Lets a test simulate the debit succeeding while the very next read-back
        // is interrupted (a native hiccup, not a wallet problem): ReadCashResultAfterDebit overrides
        // only reads that happen once at least one debit call has been made, so the pre-debit
        // sufficiency read RunPay itself performs is never affected by it.
        public Release1SmallCourtesyWorldReadStatus? ReadCashResultAfterDebit { get; set; }

        public override Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity) =>
            throw new NotSupportedException();

        public override Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
        {
            if (TotalMinutes is { } minutes)
            {
                totalMinutes = minutes;
                return Release1SmallCourtesyWorldReadStatus.Ready;
            }
            totalMinutes = 0d;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public override Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            ReadCashCalls++;
            balance = CashBalance;
            if (DebitCalls > 0 && ReadCashResultAfterDebit is { } afterDebit) return afterDebit;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        // OC-73 review fix (finding 2). Lets a test put the story runtime into Saving in the same
        // instant the debit itself returns, so RunPay's very next call (the blocking write for a
        // non-Succeeded debit) hits a real DeferredSaving gate rejection instead of a fake shortcut.
        public Action? OnDebitCalled { get; set; }

        public override Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount)
        {
            DebitCalls++;
            LastDebitAmount = amount;
            OnDebitCalled?.Invoke();
            if (DebitResult == Release1SmallCourtesyWorldMutationStatus.Succeeded) CashBalance += amount;
            return DebitResult;
        }

        public int EngageCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public Release1SmallCourtesyWorldMutationStatus EngageResult { get; set; } = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public Release1SmallCourtesyWorldMutationStatus ReleaseResult { get; set; } = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public string EngageReason { get; set; } = string.Empty;
        public string ReleaseReason { get; set; } = string.Empty;

        public override Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)
        {
            EngageCalls++;
            reason = EngageReason;
            return EngageResult;
        }

        public override Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)
        {
            ReleaseCalls++;
            reason = ReleaseReason;
            return ReleaseResult;
        }
    }

    private sealed class Harness : IDisposable
    {
        private Harness(
            Release1StoryRuntimeService story,
            Release1SmallCourtesyDepositTests.FakeContext context,
            Release1SmallCourtesyDepositTests.FakeRepository repository,
            ChiefFakeWorld world,
            Release1ChiefTierObserver observer,
            LedgerHarness ledger,
            Release1ChiefService service)
        {
            Story = story;
            Context = context;
            Repository = repository;
            World = world;
            Observer = observer;
            Ledger = ledger;
            Service = service;
        }

        public Release1StoryRuntimeService Story { get; }
        public Release1SmallCourtesyDepositTests.FakeContext Context { get; }
        public Release1SmallCourtesyDepositTests.FakeRepository Repository { get; }
        public ChiefFakeWorld World { get; }
        public Release1ChiefTierObserver Observer { get; }
        public LedgerHarness Ledger { get; }
        public Release1ChiefService Service { get; }

        public string PlayerId => Context.Snapshot.PlayerId;
        public Release1ChiefRecord? Record => Story.State?.ChiefRecord;

        public void Arrest(LocalPressureTier previousTier = LocalPressureTier.Quiet, LocalPressureTier currentTier = LocalPressureTier.Quiet) =>
            Observer.Publish(new(
                Context.Snapshot.SessionEpoch, Context.Snapshot.LoadEpoch, PlayerId, null,
                $"arrest-{Guid.NewGuid()}", previousTier, currentTier, null, null));

        public static Harness Create()
        {
            var context = new Release1SmallCourtesyDepositTests.FakeContext();
            var repository = new Release1SmallCourtesyDepositTests.FakeRepository();
            var story = new Release1StoryRuntimeService(context, repository);
            story.OnPreLoad();
            story.OnLoadComplete();

            const string receipt = "chief-service-test-intro";
            var correlation = Release1LogicalCorrelation.Create(
                context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
            Assert.True(story.TryExecuteDurably(new(
                context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
                Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt, correlation)).Accepted);

            var world = new ChiefFakeWorld();
            var observer = new Release1ChiefTierObserver();
            var ledger = new LedgerHarness
            {
                SessionEpoch = context.Snapshot.SessionEpoch,
                LoadEpoch = context.Snapshot.LoadEpoch,
                State = LocalPressureState.Quiet(context.Snapshot.PlayerId)
            };
            var service = new Release1ChiefService(story, world, observer, ledger.Read, ledger.Wipe, ledger.Epochs, LocalPressureProfile.Moderate);
            return new Harness(story, context, repository, world, observer, ledger, service);
        }

        public void Dispose()
        {
            Service.Dispose();
            Story.Dispose();
        }
    }
}
