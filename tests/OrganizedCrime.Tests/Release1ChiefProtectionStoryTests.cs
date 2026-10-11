using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ChiefProtectionStoryTests
{
    [Fact]
    public void Prepare_protection_effect_on_adopted_flips_to_paying_protection()
    {
        var harness = CreateHarness();
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TrySetChiefRecord(record).Status);
        var capture = harness.Service.State!;
        var correlation = Release1LogicalCorrelation.Create(
            capture.PlayerId, Release1MissionCatalog.ChiefCampbell, 1, Release1TransitionKind.ChiefProtectionAccepted, "protection-tap").Value;
        var effect = new Release1NativeEffectJournalEntry(
            Release1ChiefProtection.EffectId(1, record.Revision), Release1MissionCatalog.ChiefCampbell, 1,
            "CashTransfer", $"wallet:{capture.PlayerId}", Release1MissionCatalog.ChiefCampbell, "30000",
            Release1NativeEffectPhase.Prepared, null, capture.Revision + 1,
            AuthorizedStoryCorrelationId: correlation, AuthorizedMissionRevision: record.Revision);

        var prepared = harness.Service.TryPrepareNativeEffect(effect);

        Assert.Equal(Release1StoryCommandStatus.Accepted, prepared.Status);
        var next = harness.Service.State!.ChiefRecord!;
        Assert.Equal(Release1ChiefState.PayingProtection, next.State);
        Assert.Equal(1, next.ProtectionPurchases);
        Assert.Contains(correlation, next.AcceptedLogicalCorrelations);
        Assert.Contains(effect.EffectId, next.NativeEffectIds);
        Assert.Equal(record.Revision + 1, next.Revision);
    }

    [Fact]
    public void Prepare_protection_effect_refuses_while_unpaid_and_for_future_purchases()
    {
        var harness = CreateHarness();
        var adopted = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TrySetChiefRecord(adopted).Status);
        var unpaid = Release1ChiefLedger.Observe(adopted, Release1ChiefObservedEvent.ArrestObserved).Next;
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TrySetChiefRecord(unpaid).Status);
        var capture = harness.Service.State!;
        var correlation = Release1LogicalCorrelation.Create(
            capture.PlayerId, Release1MissionCatalog.ChiefCampbell, 1, Release1TransitionKind.ChiefProtectionAccepted, "protection-tap").Value;
        var effect = new Release1NativeEffectJournalEntry(
            Release1ChiefProtection.EffectId(1, unpaid.Revision), Release1MissionCatalog.ChiefCampbell, 1,
            "CashTransfer", $"wallet:{capture.PlayerId}", Release1MissionCatalog.ChiefCampbell, "30000",
            Release1NativeEffectPhase.Prepared, null, capture.Revision + 1,
            AuthorizedStoryCorrelationId: correlation, AuthorizedMissionRevision: unpaid.Revision);

        Assert.Equal(
            Release1StoryRuntimeRejectReason.InvalidTransition,
            harness.Service.TryPrepareNativeEffect(effect).RejectReason);

        var settledHarness = CreateHarness();
        var settled = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false) with { State = Release1ChiefState.Settled, DemandRound = 1 };
        Assert.Equal(Release1StoryCommandStatus.Accepted, settledHarness.Service.TrySetChiefRecord(settled).Status);
        var settledCapture = settledHarness.Service.State!;
        var future = Release1LogicalCorrelation.Create(
            settledCapture.PlayerId, Release1MissionCatalog.ChiefCampbell, 2, Release1TransitionKind.ChiefProtectionAccepted, "protection-tap").Value;
        var futureEffect = new Release1NativeEffectJournalEntry(
            Release1ChiefProtection.EffectId(2, settled.Revision), Release1MissionCatalog.ChiefCampbell, 2,
            "CashTransfer", $"wallet:{settledCapture.PlayerId}", Release1MissionCatalog.ChiefCampbell, "30000",
            Release1NativeEffectPhase.Prepared, null, settledCapture.Revision + 1,
            AuthorizedStoryCorrelationId: future, AuthorizedMissionRevision: settled.Revision);

        Assert.Equal(
            Release1StoryRuntimeRejectReason.InvalidTransition,
            settledHarness.Service.TryPrepareNativeEffect(futureEffect).RejectReason);
    }

    [Fact]
    public void Prepare_protection_effect_rejects_a_superseded_retry_like_demands()
    {
        var harness = CreateHarness();
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TrySetChiefRecord(record).Status);
        var capture = harness.Service.State!;
        var correlation = Release1LogicalCorrelation.Create(
            capture.PlayerId, Release1MissionCatalog.ChiefCampbell, 1, Release1TransitionKind.ChiefProtectionAccepted, "protection-tap").Value;
        var effect = new Release1NativeEffectJournalEntry(
            Release1ChiefProtection.EffectId(1, record.Revision), Release1MissionCatalog.ChiefCampbell, 1,
            "CashTransfer", $"wallet:{capture.PlayerId}", Release1MissionCatalog.ChiefCampbell, "30000",
            Release1NativeEffectPhase.Prepared, null, capture.Revision + 1,
            AuthorizedStoryCorrelationId: correlation, AuthorizedMissionRevision: record.Revision);

        Assert.Equal(
            Release1StoryCommandStatus.Accepted,
            harness.Service.TryPrepareNativeEffect(effect).Status);
        Assert.Equal(
            Release1StoryRuntimeRejectReason.InvalidTransition,
            harness.Service.TryPrepareNativeEffect(effect).RejectReason);
    }

    [Fact]
    public void Protection_effect_applies_and_commits_across_its_native_save_boundaries()
    {
        var harness = CreateHarness();
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TrySetChiefRecord(record).Status);
        var capture = harness.Service.State!;
        var correlation = Release1LogicalCorrelation.Create(
            capture.PlayerId, Release1MissionCatalog.ChiefCampbell, 1, Release1TransitionKind.ChiefProtectionAccepted, "protection-tap").Value;
        var effect = new Release1NativeEffectJournalEntry(
            Release1ChiefProtection.EffectId(1, record.Revision), Release1MissionCatalog.ChiefCampbell, 1,
            "CashTransfer", $"wallet:{capture.PlayerId}", Release1MissionCatalog.ChiefCampbell, "30000",
            Release1NativeEffectPhase.Prepared, null, capture.Revision + 1,
            AuthorizedStoryCorrelationId: correlation, AuthorizedMissionRevision: record.Revision);
        Assert.Equal(
            Release1StoryCommandStatus.Accepted,
            harness.Service.TryPrepareNativeEffect(effect).Status);

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        Assert.Equal(
            Release1StoryCommandStatus.Accepted,
            harness.Service.TryMarkNativeEffectApplied(effect.EffectId, Release1ChiefProtection.NativeReceiptId(1, record.Revision)).Status);

        var paying = harness.Service.State!.ChiefRecord!;
        var covered = Release1ChiefLedger.ApplyProtectionPurchase(paying, 5).Next with { State = Release1ChiefState.Adopted };
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TrySetChiefRecord(covered).Status);
        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();

        Assert.Equal(
            Release1StoryCommandStatus.Accepted,
            harness.Service.TryCommitNativeEffect(effect.EffectId, correlation).Status);
        Assert.Equal(Release1NativeEffectPhase.Committed, harness.Service.State!.NativeEffects.Single(e => e.EffectId == effect.EffectId).Phase);
    }

    [Fact]
    public void Committed_protection_effect_survives_an_arrest_reset_of_the_record_lists()
    {
        var harness = CreateHarness();
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TrySetChiefRecord(record).Status);
        var capture = harness.Service.State!;
        var correlation = Release1LogicalCorrelation.Create(
            capture.PlayerId, Release1MissionCatalog.ChiefCampbell, 1, Release1TransitionKind.ChiefProtectionAccepted, "protection-tap").Value;
        var committed = new Release1NativeEffectJournalEntry(
            Release1ChiefProtection.EffectId(1, record.Revision), Release1MissionCatalog.ChiefCampbell, 1,
            "CashTransfer", $"wallet:{capture.PlayerId}", Release1MissionCatalog.ChiefCampbell, "30000",
            Release1NativeEffectPhase.Committed, Release1ChiefProtection.NativeReceiptId(1, record.Revision), capture.Revision + 1,
            CommittedStoryCorrelationId: correlation, AuthorizedStoryCorrelationId: correlation, AuthorizedMissionRevision: record.Revision);
        var reset = Release1ChiefLedger.Observe(
            record with { State = Release1ChiefState.Settled, DemandRound = 1, ProtectedUntilDay = null, ProtectionPurchases = 1 },
            Release1ChiefObservedEvent.ArrestObserved).Next;

        var state = harness.Service.State! with
        {
            NativeEffects = new[] { committed },
            ChiefRecord = reset,
            Revision = capture.Revision + 1
        };

        state.Validate();
    }

    [Fact]
    public void Journal_rejects_a_protection_effect_beyond_the_prepared_purchases()
    {
        var harness = CreateHarness();
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Quiet, false);
        Assert.Equal(Release1StoryCommandStatus.Accepted, harness.Service.TrySetChiefRecord(record).Status);
        var capture = harness.Service.State!;
        var correlation = Release1LogicalCorrelation.Create(
            capture.PlayerId, Release1MissionCatalog.ChiefCampbell, 5, Release1TransitionKind.ChiefProtectionAccepted, "protection-tap").Value;
        var effect = new Release1NativeEffectJournalEntry(
            Release1ChiefProtection.EffectId(5, record.Revision), Release1MissionCatalog.ChiefCampbell, 5,
            "CashTransfer", $"wallet:{capture.PlayerId}", Release1MissionCatalog.ChiefCampbell, "30000",
            Release1NativeEffectPhase.Prepared, null, capture.Revision + 1,
            AuthorizedStoryCorrelationId: correlation, AuthorizedMissionRevision: record.Revision);

        var state = capture with { NativeEffects = new[] { effect } };

        Assert.Throws<ArgumentException>(() => state.Validate());
    }

    private static StoryHarness CreateHarness()
    {
        var context = new Release1SmallCourtesyDepositTests.FakeContext();
        var repository = new Release1SmallCourtesyDepositTests.FakeRepository();
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        const string receipt = "chief-protection-story-seed";
        var commandCorrelation = Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
        Assert.True(story.TryExecuteDurably(new(
            context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
            Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt, commandCorrelation)).Accepted);
        return new StoryHarness(story, repository);
    }

    private sealed record StoryHarness(
        Release1StoryRuntimeService Service, Release1SmallCourtesyDepositTests.FakeRepository Repository);
}
