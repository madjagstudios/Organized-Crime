using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ChiefLedgerTests
{
    private static Release1ChiefRecord Adopted(LocalPressureTier tier = LocalPressureTier.Quiet, bool knownOffender = false) =>
        Release1ChiefRecord.Adopt(tier, knownOffender);

    private static Release1ChiefRecord Open(int round) => Adopted() with
    {
        State = Release1ChiefState.DemandOpen,
        DemandRound = round,
        Revision = 1
    };

    private static Release1ChiefRecord Declined(int round) => Open(round) with { State = Release1ChiefState.Declined };

    private static Release1LogicalCorrelation Correlation(int round) =>
        Release1LogicalCorrelation.Create("player-1", Release1MissionCatalog.ChiefCampbell, round, Release1TransitionKind.ChiefPaymentAccepted, $"chief-pay-r{round}");

    private static Release1ChiefRecord Paying(int round) => Open(round) with
    {
        State = Release1ChiefState.Paying,
        AcceptedLogicalCorrelations = new[] { Correlation(round).Value },
        NativeEffectIds = new[] { Release1ChiefEffect.EffectId(round, Open(round).Revision) }
    };

    private static Release1ChiefRecord Settled(int round) => Paying(round) with { State = Release1ChiefState.Settled };

    [Fact]
    public void Adoption_plus_an_arrest_opens_round_one()
    {
        var decision = Release1ChiefLedger.Observe(Adopted(), Release1ChiefObservedEvent.ArrestObserved);

        Assert.Equal(Release1ChiefAction.OpenDemand, decision.Action);
        Assert.Equal(Release1ChiefState.DemandOpen, decision.Next.State);
        Assert.Equal(1, decision.Next.DemandRound);
        Assert.Equal(Adopted().Revision + 1, decision.Next.Revision);
    }

    [Fact]
    public void Adoption_plus_a_rise_into_watched_opens_round_one_and_records_the_watch_line_together()
    {
        var decision = Release1ChiefLedger.Observe(Adopted(), Release1ChiefObservedEvent.RoseToWatched);

        Assert.Equal(Release1ChiefAction.RecordWatchLine, decision.Action);
        Assert.Equal(Release1ChiefState.DemandOpen, decision.Next.State);
        Assert.Equal(1, decision.Next.DemandRound);
        Assert.Equal(1, decision.Next.WatchLinesSent);
    }

    [Fact]
    public void Demand_open_plus_an_arrest_leaves_the_round_alone()
    {
        var record = Open(1);
        var decision = Release1ChiefLedger.Observe(record, Release1ChiefObservedEvent.ArrestObserved);

        Assert.Equal(Release1ChiefAction.None, decision.Action);
        Assert.Same(record, decision.Next);
    }

    [Fact]
    public void Declined_plus_arrests_advances_the_round_then_caps()
    {
        var round1 = Release1ChiefLedger.Observe(Declined(1), Release1ChiefObservedEvent.ArrestObserved);
        Assert.Equal(2, round1.Next.DemandRound);

        var round2 = Release1ChiefLedger.Observe(Declined(2), Release1ChiefObservedEvent.ArrestObserved);
        Assert.Equal(3, round2.Next.DemandRound);

        var round3 = Release1ChiefLedger.Observe(Declined(3), Release1ChiefObservedEvent.ArrestObserved);
        Assert.Equal(3, round3.Next.DemandRound);
    }

    [Fact]
    public void Settled_plus_an_arrest_restarts_at_round_one_with_history_cleared()
    {
        // OC-73 review fix. History used to be retained (the prior AcceptedLogicalCorrelations and
        // NativeEffectIds carried forward unchanged), but both are validated against DemandRound,
        // which this same transition resets to 1. A payoff settled at round 2 or 3 left a retained
        // correlation/effect whose Attempt permanently exceeded the reset round, so the demand could
        // never reopen (Release1ChiefRecord.Validate threw forever after). Clearing both here is what
        // lets the reset record validate regardless of which round it was settled at.
        var settled = Settled(1);
        var decision = Release1ChiefLedger.Observe(settled, Release1ChiefObservedEvent.ArrestObserved);

        Assert.Equal(Release1ChiefAction.OpenDemand, decision.Action);
        Assert.Equal(Release1ChiefState.DemandOpen, decision.Next.State);
        Assert.Equal(1, decision.Next.DemandRound);
        Assert.Empty(decision.Next.AcceptedLogicalCorrelations);
        Assert.Empty(decision.Next.NativeEffectIds);
        decision.Next.Validate();
    }

    [Fact]
    public void Settled_at_round_two_then_an_arrest_resets_cleanly_and_validates()
    {
        // The exact regression the review found: declining round 1, arresting into round 2, paying
        // and settling there, then a later arrest used to produce a record whose own Validate threw,
        // because the retained round-2 correlation/effect id could never satisfy a round reset to 1.
        var declined = Declined(1);
        var reopened = Release1ChiefLedger.Observe(declined, Release1ChiefObservedEvent.ArrestObserved).Next;
        Assert.Equal(2, reopened.DemandRound);

        // Manually shaped in the same way Release1StoryRuntimeService.PrepareChiefEffect's atomic
        // DemandOpen-to-Paying write would leave it, now that Release1ChiefLedger.BeginPaying itself
        // is gone (OC-73 review fix, finding 4: it was production-dead, re-implemented inline in
        // PrepareChiefEffect, and tested nothing about the shipped path).
        var paying = reopened with
        {
            State = Release1ChiefState.Paying,
            AcceptedLogicalCorrelations = new[] { Correlation(2).Value },
            NativeEffectIds = new[] { Release1ChiefEffect.EffectId(2, reopened.Revision) },
            Revision = reopened.Revision + 1
        };
        var settled = Release1ChiefLedger.Settle(paying).Next;
        Assert.Equal(Release1ChiefState.Settled, settled.State);
        Assert.Equal(2, settled.DemandRound);

        var decision = Release1ChiefLedger.Observe(settled, Release1ChiefObservedEvent.ArrestObserved);

        decision.Next.Validate();
        Assert.Equal(Release1ChiefState.DemandOpen, decision.Next.State);
        Assert.Equal(1, decision.Next.DemandRound);
        Assert.Empty(decision.Next.AcceptedLogicalCorrelations);
        Assert.Empty(decision.Next.NativeEffectIds);
    }

    [Fact]
    public void A_rise_into_watched_while_unpaid_increments_the_watch_counter_and_changes_nothing_else()
    {
        var record = Open(1);
        var decision = Release1ChiefLedger.Observe(record, Release1ChiefObservedEvent.RoseToWatched);

        Assert.Equal(Release1ChiefAction.RecordWatchLine, decision.Action);
        Assert.Equal(1, decision.Next.WatchLinesSent);
        Assert.Equal(record.DemandRound, decision.Next.DemandRound);
        Assert.Equal(record.State, decision.Next.State);
    }

    [Fact]
    public void A_rise_into_critical_while_unpaid_increments_lockdown_announcements_and_changes_no_state()
    {
        var record = Open(1);
        var decision = Release1ChiefLedger.Observe(record, Release1ChiefObservedEvent.RoseToCritical);

        Assert.Equal(Release1ChiefAction.AnnounceLockdown, decision.Action);
        Assert.Equal(1, decision.Next.LockdownAnnouncements);
        Assert.Equal(record.State, decision.Next.State);
    }

    [Theory]
    [InlineData(Release1ChiefState.Settled)]
    [InlineData(Release1ChiefState.Paying)]
    public void A_rise_into_critical_while_paid_does_nothing(Release1ChiefState state)
    {
        var record = state == Release1ChiefState.Settled ? Settled(1) : Paying(1);
        var decision = Release1ChiefLedger.Observe(record, Release1ChiefObservedEvent.RoseToCritical);

        Assert.Equal(Release1ChiefAction.None, decision.Action);
        Assert.Same(record, decision.Next);
    }

    [Fact]
    public void Decline_moves_demand_open_to_declined()
    {
        var decision = Release1ChiefLedger.Decline(Open(1));

        Assert.Equal(Release1ChiefAction.ReplyDeclined, decision.Action);
        Assert.Equal(Release1ChiefState.Declined, decision.Next.State);
    }

    [Fact]
    public void Decline_increments_its_own_non_resetting_counter()
    {
        // OC-73 review fix (finding 1). DeclineRepliesSent mirrors WatchLinesSent and PaidLifts: it
        // never resets, so Release1ChiefPresentation can key the reply's own correlation on it instead
        // of DemandRound, which the Adopted-or-Settled arrest branch above resets to 1 every ladder
        // restart.
        var first = Release1ChiefLedger.Decline(Open(1));
        Assert.Equal(1, first.Next.DeclineRepliesSent);

        var reopened = first.Next with { State = Release1ChiefState.DemandOpen };
        var second = Release1ChiefLedger.Decline(reopened);
        Assert.Equal(2, second.Next.DeclineRepliesSent);
    }

    [Fact]
    public void AbandonUnappliedPayment_moves_paying_back_to_demand_open_at_the_same_round()
    {
        // OC-73 review fix. RunPay used to leave a Paying record frozen forever the moment its debit
        // came back non-Succeeded. A non-Succeeded debit is known, synchronously, to have moved no
        // cash, so this transition hands the demand straight back rather than merely retrying.
        var paying = Paying(2);

        var decision = Release1ChiefLedger.AbandonUnappliedPayment(paying);

        Assert.Equal(Release1ChiefAction.None, decision.Action);
        Assert.Equal(Release1ChiefState.DemandOpen, decision.Next.State);
        Assert.Equal(2, decision.Next.DemandRound);
        Assert.Equal(paying.Revision + 1, decision.Next.Revision);
        decision.Next.Validate();
    }

    [Fact]
    public void AbandonUnappliedPayment_is_a_no_op_off_paying()
    {
        var record = Open(1);
        var decision = Release1ChiefLedger.AbandonUnappliedPayment(record);

        Assert.Equal(Release1ChiefAction.None, decision.Action);
        Assert.Same(record, decision.Next);
    }

    [Fact]
    public void Settle_moves_paying_to_settled_and_clears_lockdown_incrementing_paid_lifts()
    {
        var engaged = Paying(1) with { LockdownAnnouncements = 1, LockdownEngaged = true };
        var decision = Release1ChiefLedger.Settle(engaged);

        Assert.Equal(Release1ChiefState.Settled, decision.Next.State);
        Assert.False(decision.Next.LockdownEngaged);
        Assert.Equal(1, decision.Next.PaidLifts);
        Assert.Equal(Release1ChiefAction.LiftPaid, decision.Action);
    }

    [Fact]
    public void Settle_without_a_lockdown_does_not_touch_paid_lifts()
    {
        var decision = Release1ChiefLedger.Settle(Paying(1));

        Assert.Equal(Release1ChiefState.Settled, decision.Next.State);
        Assert.Equal(0, decision.Next.PaidLifts);
        Assert.Equal(Release1ChiefAction.None, decision.Action);
    }

    [Fact]
    public void CoolBelowWatched_clears_lockdown_incrementing_cooled_lifts_only_when_set()
    {
        var engaged = Open(1) with { LockdownAnnouncements = 1, LockdownEngaged = true };
        var decision = Release1ChiefLedger.CoolBelowWatched(engaged);

        Assert.False(decision.Next.LockdownEngaged);
        Assert.Equal(1, decision.Next.CooledLifts);
        Assert.Equal(Release1ChiefAction.LiftCooled, decision.Action);

        var notEngaged = Open(1);
        var noOp = Release1ChiefLedger.CoolBelowWatched(notEngaged);
        Assert.Equal(Release1ChiefAction.None, noOp.Action);
        Assert.Same(notEngaged, noOp.Next);
    }

    [Fact]
    public void Every_returned_record_revision_is_exactly_one_past_the_input()
    {
        var record = Open(1);
        Assert.Equal(record.Revision + 1, Release1ChiefLedger.Observe(record, Release1ChiefObservedEvent.RoseToWatched).Next.Revision);
        Assert.Equal(record.Revision + 1, Release1ChiefLedger.Decline(record).Next.Revision);

        var paying = Paying(1);
        Assert.Equal(paying.Revision + 1, Release1ChiefLedger.Settle(paying).Next.Revision);

        var engaged = record with { LockdownAnnouncements = 1, LockdownEngaged = true };
        Assert.Equal(engaged.Revision + 1, Release1ChiefLedger.CoolBelowWatched(engaged).Next.Revision);
    }

    [Fact]
    public void ChargeLateFee_on_a_new_day_raises_the_round_and_marks_the_day()
    {
        var record = Open(1) with { LastLateFeeChargedDay = 5 };

        var decision = Release1ChiefLedger.ChargeLateFee(record, 6);

        Assert.Equal(Release1ChiefAction.ChargeLateFee, decision.Action);
        Assert.Equal(2, decision.Next.DemandRound);
        Assert.Equal(6, decision.Next.LastLateFeeChargedDay);
        Assert.Equal(Release1ChiefState.DemandOpen, decision.Next.State);
        Assert.Equal(record.Revision + 1, decision.Next.Revision);
        decision.Next.Validate();
    }

    [Fact]
    public void ChargeLateFee_establishes_the_marker_without_charging()
    {
        var record = Open(1);

        var decision = Release1ChiefLedger.ChargeLateFee(record, 5);

        Assert.Equal(Release1ChiefAction.None, decision.Action);
        Assert.Equal(1, decision.Next.DemandRound);
        Assert.Equal(5, decision.Next.LastLateFeeChargedDay);
        Assert.Equal(record.Revision + 1, decision.Next.Revision);
        decision.Next.Validate();
    }

    [Fact]
    public void ChargeLateFee_never_charges_twice_in_one_day()
    {
        var record = Open(1) with { LastLateFeeChargedDay = 5 };

        var decision = Release1ChiefLedger.ChargeLateFee(record, 5);

        Assert.Equal(Release1ChiefAction.None, decision.Action);
        Assert.Same(record, decision.Next);
    }

    [Fact]
    public void ChargeLateFee_treats_a_clock_that_ran_backwards_as_already_charged()
    {
        var record = Open(1) with { LastLateFeeChargedDay = 6 };

        var decision = Release1ChiefLedger.ChargeLateFee(record, 5);

        Assert.Equal(Release1ChiefAction.None, decision.Action);
        Assert.Same(record, decision.Next);
    }

    [Fact]
    public void ChargeLateFee_stops_at_the_cap()
    {
        var record = Open(3) with { LastLateFeeChargedDay = 5 };

        var decision = Release1ChiefLedger.ChargeLateFee(record, 6);

        Assert.Equal(Release1ChiefAction.None, decision.Action);
        Assert.Same(record, decision.Next);
    }

    [Theory]
    [InlineData(Release1ChiefState.Adopted)]
    [InlineData(Release1ChiefState.Paying)]
    [InlineData(Release1ChiefState.Settled)]
    public void ChargeLateFee_is_a_no_op_off_unpaid(Release1ChiefState state)
    {
        var record = state switch
        {
            Release1ChiefState.Adopted => Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false),
            Release1ChiefState.Paying => Paying(1),
            _ => Settled(1),
        };

        var decision = Release1ChiefLedger.ChargeLateFee(record, 6);

        Assert.Equal(Release1ChiefAction.None, decision.Action);
        Assert.Same(record, decision.Next);
    }

    [Fact]
    public void ChargeLateFee_charges_a_declined_demand_and_leaves_it_declined()
    {
        var declined = Release1ChiefLedger.Decline(Open(1)).Next with { LastLateFeeChargedDay = 5 };

        var decision = Release1ChiefLedger.ChargeLateFee(declined, 6);

        Assert.Equal(Release1ChiefAction.ChargeLateFee, decision.Action);
        Assert.Equal(2, decision.Next.DemandRound);
        Assert.Equal(Release1ChiefState.Declined, decision.Next.State);
        decision.Next.Validate();
    }

    [Fact]
    public void An_arrest_step_on_top_of_a_fee_step_stays_capped()
    {
        var record = Open(1) with { LastLateFeeChargedDay = 5 };
        var firstFee = Release1ChiefLedger.ChargeLateFee(record, 6).Next;
        var secondFee = Release1ChiefLedger.ChargeLateFee(firstFee, 7).Next;
        Assert.Equal(3, secondFee.DemandRound);

        var decision = Release1ChiefLedger.Observe(secondFee, Release1ChiefObservedEvent.ArrestObserved);

        Assert.Equal(3, decision.Next.DemandRound);
        decision.Next.Validate();
    }

    [Fact]
    public void A_fee_step_preserves_correlations_and_effect_linkage()
    {
        var authorization = Release1LogicalCorrelation.Create(
            "player-1", Release1MissionCatalog.ChiefCampbell, 1,
            Release1TransitionKind.ChiefPaymentAccepted, "chief-pay-r1-c2").Value;
        var record = Open(1) with
        {
            LastLateFeeChargedDay = 5,
            AcceptedLogicalCorrelations = new[] { authorization },
            NativeEffectIds = new[] { Release1ChiefEffect.EffectId(1, 2) },
        };

        var decision = Release1ChiefLedger.ChargeLateFee(record, 6);

        Assert.Equal(new[] { authorization }, decision.Next.AcceptedLogicalCorrelations);
        Assert.Equal(new[] { Release1ChiefEffect.EffectId(1, 2) }, decision.Next.NativeEffectIds);
        decision.Next.Validate();
    }

    [Fact]
    public void A_fresh_demand_open_resets_the_late_fee_marker()
    {
        var settled = Settled(1) with { LastLateFeeChargedDay = 9 };

        var decision = Release1ChiefLedger.Observe(settled, Release1ChiefObservedEvent.ArrestObserved);

        Assert.Equal(Release1ChiefState.DemandOpen, decision.Next.State);
        Assert.Null(decision.Next.LastLateFeeChargedDay);
    }

    [Fact]
    public void An_arrest_reopen_keeps_the_late_fee_marker()
    {
        var declined = Release1ChiefLedger.Decline(Open(1)).Next with { LastLateFeeChargedDay = 5 };

        var decision = Release1ChiefLedger.Observe(declined, Release1ChiefObservedEvent.ArrestObserved);

        Assert.Equal(Release1ChiefState.DemandOpen, decision.Next.State);
        Assert.Equal(2, decision.Next.DemandRound);
        Assert.Equal(5, decision.Next.LastLateFeeChargedDay);
    }

    [Fact]
    public void ChargeLateFee_counts_each_raise()
    {
        var record = Open(1) with { LastLateFeeChargedDay = 5 };

        var first = Release1ChiefLedger.ChargeLateFee(record, 6).Next;
        var second = Release1ChiefLedger.ChargeLateFee(first, 7).Next;

        Assert.Equal(1, first.LateFeesCharged);
        Assert.Equal(2, second.LateFeesCharged);
    }

    [Fact]
    public void ChargeLateFee_establish_leaves_the_count_alone()
    {
        var decision = Release1ChiefLedger.ChargeLateFee(Open(1), 5);

        Assert.Equal(0, decision.Next.LateFeesCharged);
    }

    [Fact]
    public void A_fresh_demand_open_resets_the_late_fee_count()
    {
        var settled = Settled(1) with { LastLateFeeChargedDay = 9, LateFeesCharged = 2 };

        var decision = Release1ChiefLedger.Observe(settled, Release1ChiefObservedEvent.ArrestObserved);

        Assert.Equal(0, decision.Next.LateFeesCharged);
    }

    [Fact]
    public void An_arrest_reopen_keeps_the_late_fee_count()
    {
        var declined = Release1ChiefLedger.Decline(Open(1)).Next with { LastLateFeeChargedDay = 5, LateFeesCharged = 1 };

        var decision = Release1ChiefLedger.Observe(declined, Release1ChiefObservedEvent.ArrestObserved);

        Assert.Equal(1, decision.Next.LateFeesCharged);
    }

    [Fact]
    public void EffectId_carries_the_shared_prefix()
    {
        Assert.Equal("chief-campbell-cash-v1-", Release1ChiefEffect.EffectIdPrefix);
        Assert.Equal("chief-campbell-cash-v1-r1-c2", Release1ChiefEffect.EffectId(1, 2));
    }

    [Theory]
    [InlineData("chief-campbell-cash-v1-r1-c2", Release1MissionCatalog.ChiefCampbell, true)]
    [InlineData("chief-campbell-cash-v1-r1-c2", Release1MissionCatalog.SmallCourtesy, false)]
    [InlineData("small-courtesy-reward-v1-a1", Release1MissionCatalog.ChiefCampbell, false)]
    [InlineData("small-courtesy-reward-v1-a1", Release1MissionCatalog.SmallCourtesy, false)]
    public void IsCashEffect_matches_the_prefix_and_the_mission_key(string effectId, string missionKey, bool expected)
    {
        var effect = new Release1NativeEffectJournalEntry(
            effectId, missionKey, 1, Release1ChiefEffect.Kind,
            Release1ChiefEffect.WalletSource, Release1ChiefEffect.Scope, "25000",
            Release1NativeEffectPhase.Applied, "native-1", 1);

        Assert.Equal(expected, Release1ChiefEffect.IsCashEffect(effect));
    }

    [Fact]
    public void IsCashEffect_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => Release1ChiefEffect.IsCashEffect(null!));
    }
}
