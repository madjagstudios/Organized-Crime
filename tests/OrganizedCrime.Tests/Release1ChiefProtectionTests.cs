using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ChiefProtectionTests
{
    [Fact]
    public void Protection_price_is_thirty_thousand_within_the_wallet_debit_limit()
    {
        Assert.Equal(30_000, Release1ChiefProtection.ProtectionWeeklyPrice);
        Assert.True(Release1ChiefProtection.ProtectionWeeklyPrice <= Release1ChiefDemandLadder.Cap);
    }

    [Fact]
    public void Protection_cover_is_seven_days_capped_at_fourteen_banked()
    {
        Assert.Equal(7, Release1ChiefProtection.CoverDaysPerPurchase);
        Assert.Equal(14, Release1ChiefProtection.MaxBankedDays);
    }

    [Fact]
    public void Protection_offer_opens_at_three_successful_chapter3_jobs()
    {
        Assert.Equal(3, Release1ChiefProtection.OfferSuccessfulJobs);
    }

    [Fact]
    public void Protection_effect_ids_carry_purchase_and_revision_and_never_collide_with_demands()
    {
        Assert.Equal("chief-campbell-protection-v1-p1-c41", Release1ChiefProtection.EffectId(1, 41));
        Assert.NotEqual(Release1ChiefProtection.EffectId(1, 41), Release1ChiefEffect.EffectId(1, 41));
        Assert.Equal(
            "chief-campbell-protection-native-v1-p1-c41",
            Release1ChiefProtection.NativeReceiptId(1, 41));
    }

    [Fact]
    public void Freshly_adopted_record_is_unprotected_unoffered_and_unpurchased()
    {
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);

        Assert.Null(record.ProtectedUntilDay);
        Assert.False(record.ProtectionOffered);
        Assert.Null(record.ProtectionWarnedForUntilDay);
        Assert.Equal(0, record.ProtectionLapseNotices);
        Assert.Equal(0, record.ProtectionSettleFirstRefusals);
        Assert.Equal(0, record.ProtectionPurchases);
        Assert.Equal(0, record.ProtectionBlockedNotices);
    }

    [Fact]
    public void Protection_fields_round_trip_through_value_equality_and_hash()
    {
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with
        {
            ProtectedUntilDay = 10,
            ProtectionOffered = true,
            ProtectionWarnedForUntilDay = 10,
            ProtectionLapseNotices = 1,
            ProtectionSettleFirstRefusals = 2,
            ProtectionPurchases = 3,
            ProtectionBlockedNotices = 1
        };

        var same = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with
        {
            ProtectedUntilDay = 10,
            ProtectionOffered = true,
            ProtectionWarnedForUntilDay = 10,
            ProtectionLapseNotices = 1,
            ProtectionSettleFirstRefusals = 2,
            ProtectionPurchases = 3,
            ProtectionBlockedNotices = 1
        };
        var different = same with { ProtectedUntilDay = 11 };

        Assert.True(record.ValueEquals(same));
        Assert.Equal(record.GetHashCode(), same.GetHashCode());
        Assert.False(record.ValueEquals(different));
        Assert.False(record.ValueEquals(null));
    }

    [Fact]
    public void Negative_protection_days_are_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            (Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { ProtectedUntilDay = -1 }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { ProtectionWarnedForUntilDay = -1 }).Validate());
    }

    [Fact]
    public void Negative_protection_counters_are_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            (Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { ProtectionLapseNotices = -1 }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { ProtectionSettleFirstRefusals = -1 }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { ProtectionPurchases = -1 }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { ProtectionBlockedNotices = -1 }).Validate());
    }

    [Fact]
    public void Cover_start_checkpoint_snapshot_defaults_to_null_and_holds_four_bits()
    {
        var fresh = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);

        Assert.Null(fresh.ProtectionCoverStartCheckpoints);
        (fresh with { ProtectionCoverStartCheckpoints = 0 }).Validate();
        (fresh with { ProtectionCoverStartCheckpoints = 15 }).Validate();
        Assert.Throws<ArgumentException>(() =>
            (fresh with { ProtectionCoverStartCheckpoints = -1 }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (fresh with { ProtectionCoverStartCheckpoints = 16 }).Validate());
    }

    [Fact]
    public void Protection_correlations_must_be_canonical_for_a_reached_purchase()
    {
        var adopted = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { ProtectionPurchases = 1 };
        var good = Release1LogicalCorrelation.Create(
            "player", Release1MissionCatalog.ChiefCampbell, 1, Release1TransitionKind.ChiefProtectionAccepted, "chief-protection-p1").Value;
        (adopted with { AcceptedLogicalCorrelations = new[] { good } }).Validate();

        var future = Release1LogicalCorrelation.Create(
            "player", Release1MissionCatalog.ChiefCampbell, 2, Release1TransitionKind.ChiefProtectionAccepted, "chief-protection-p2").Value;
        Assert.Throws<ArgumentException>(() =>
            (adopted with { AcceptedLogicalCorrelations = new[] { future } }).Validate());
    }

    [Fact]
    public void Paying_protection_requires_its_authorization_and_its_effect()
    {
        var adopted = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);
        Assert.Throws<ArgumentException>(() =>
            (adopted with { State = Release1ChiefState.PayingProtection }).Validate());
    }

    [Theory]
    [InlineData(null, 5, false)]
    [InlineData(10, 5, true)]
    [InlineData(10, 9, true)]
    [InlineData(10, 10, false)]
    [InlineData(10, 11, false)]
    [InlineData(10, -1, false)]
    public void IsProtected_is_strictly_below_the_end_day(int? untilDay, int today, bool expected)
    {
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { ProtectedUntilDay = untilDay };

        Assert.Equal(expected, Release1ChiefLedger.IsProtected(record, today));
    }

    [Fact]
    public void OfferProtection_sets_the_flag_once()
    {
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);

        var offered = Release1ChiefLedger.OfferProtection(record);

        Assert.Equal(Release1ChiefAction.OfferProtection, offered.Action);
        Assert.True(offered.Next.ProtectionOffered);
        Assert.Equal(record.Revision + 1, offered.Next.Revision);

        var again = Release1ChiefLedger.OfferProtection(offered.Next);
        Assert.Equal(Release1ChiefAction.None, again.Action);
        Assert.Equal(offered.Next.Revision, again.Next.Revision);
    }

    [Fact]
    public void RefuseProtectionWhileUnpaid_counts_each_refusal()
    {
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);

        var first = Release1ChiefLedger.RefuseProtectionWhileUnpaid(record);
        var second = Release1ChiefLedger.RefuseProtectionWhileUnpaid(first.Next);

        Assert.Equal(Release1ChiefAction.RefuseProtection, first.Action);
        Assert.Equal(1, first.Next.ProtectionSettleFirstRefusals);
        Assert.Equal(2, second.Next.ProtectionSettleFirstRefusals);
        Assert.Equal(record.Revision + 2, second.Next.Revision);
    }

    [Fact]
    public void ApplyProtectionPurchase_counts_seven_days_from_today_when_unprotected()
    {
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);

        var applied = Release1ChiefLedger.ApplyProtectionPurchase(record, 5);

        Assert.Equal(Release1ChiefAction.ApplyProtection, applied.Action);
        Assert.Equal(12, applied.Next.ProtectedUntilDay);
        Assert.Equal(record.Revision + 1, applied.Next.Revision);
    }

    [Fact]
    public void ApplyProtectionPurchase_extends_from_the_end_day_when_covered()
    {
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { ProtectedUntilDay = 12 };

        var applied = Release1ChiefLedger.ApplyProtectionPurchase(record, 5);

        Assert.Equal(19, applied.Next.ProtectedUntilDay);
    }

    [Fact]
    public void WarnProtectionEnding_fires_once_on_the_day_before_for_each_cover_period()
    {
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { ProtectedUntilDay = 12 };

        Assert.Equal(Release1ChiefAction.None, Release1ChiefLedger.WarnProtectionEnding(record, 5).Action);
        var warned = Release1ChiefLedger.WarnProtectionEnding(record, 11);
        Assert.Equal(Release1ChiefAction.WarnProtectionEnding, warned.Action);
        Assert.Equal(12, warned.Next.ProtectionWarnedForUntilDay);

        var again = Release1ChiefLedger.WarnProtectionEnding(warned.Next, 11);
        Assert.Equal(Release1ChiefAction.None, again.Action);
        Assert.Equal(warned.Next.Revision, again.Next.Revision);

        var extended = Release1ChiefLedger.ApplyProtectionPurchase(warned.Next, 11).Next;
        var rewarned = Release1ChiefLedger.WarnProtectionEnding(extended, 18);
        Assert.Equal(Release1ChiefAction.WarnProtectionEnding, rewarned.Action);
        Assert.Equal(19, rewarned.Next.ProtectionWarnedForUntilDay);
    }

    [Fact]
    public void NoticeProtectionLapse_clears_cover_and_counts_once()
    {
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)
            with { ProtectedUntilDay = 12, ProtectionWarnedForUntilDay = 12 };

        Assert.Equal(Release1ChiefAction.None, Release1ChiefLedger.NoticeProtectionLapse(record, 11).Action);
        var lapsed = Release1ChiefLedger.NoticeProtectionLapse(record, 12);

        Assert.Equal(Release1ChiefAction.NoticeProtectionLapse, lapsed.Action);
        Assert.Null(lapsed.Next.ProtectedUntilDay);
        Assert.Null(lapsed.Next.ProtectionWarnedForUntilDay);
        Assert.Equal(1, lapsed.Next.ProtectionLapseNotices);
        Assert.Equal(record.Revision + 1, lapsed.Next.Revision);

        var again = Release1ChiefLedger.NoticeProtectionLapse(lapsed.Next, 13);
        Assert.Equal(Release1ChiefAction.None, again.Action);
        Assert.Equal(lapsed.Next.Revision, again.Next.Revision);
    }

    [Fact]
    public void Paying_protection_is_neither_unpaid_nor_observable()
    {
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)
            with { State = Release1ChiefState.PayingProtection, ProtectionPurchases = 1 };

        Assert.False(Release1ChiefLedger.IsUnpaid(record));
        Assert.Equal(
            Release1ChiefAction.None,
            Release1ChiefLedger.Observe(record, Release1ChiefObservedEvent.ArrestObserved).Action);
        Assert.Equal(
            Release1ChiefAction.None,
            Release1ChiefLedger.Observe(record, Release1ChiefObservedEvent.RoseToCritical).Action);
        Assert.Equal(Release1ChiefAction.None, Release1ChiefLedger.ChargeLateFee(record, 5).Action);
    }

    [Fact]
    public void AbandonUnappliedProtectionPayment_hands_the_record_back_unpaid()
    {
        var adopted = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)
            with { State = Release1ChiefState.PayingProtection, ProtectionPurchases = 1 };
        var settled = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)
            with { State = Release1ChiefState.PayingProtection, DemandRound = 2, ProtectionPurchases = 1 };

        Assert.Equal(Release1ChiefState.Adopted, Release1ChiefLedger.AbandonUnappliedProtectionPayment(adopted).Next.State);
        Assert.Equal(Release1ChiefState.Settled, Release1ChiefLedger.AbandonUnappliedProtectionPayment(settled).Next.State);
        Assert.Equal(
            Release1ChiefAction.None,
            Release1ChiefLedger.AbandonUnappliedProtectionPayment(
                Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false)).Action);
    }
}
