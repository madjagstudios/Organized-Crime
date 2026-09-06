using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1WrongAddressArthurTests
{
    [Fact]
    public void A_clean_primary_completion_queues_the_courteous_call_exactly_once()
    {
        using var harness = Release1WrongAddressHarness.InCustody(withPhone: true);
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.RecordNellAcceptedReceipt();

        harness.Service.ReconcileDelivery();
        Assert.Empty(harness.Queue.Invocations);

        // FinishDelivery captures the story revision once and gates both effects' commits on that
        // same snapshot, so one save is enough for the consumption and reward effects to both leave
        // the Applied phase together; only then does PersistState stop deferring Arthur's own
        // story-level authorization.
        Release1WrongAddressHarness.Save(harness);

        var request = Assert.Single(harness.Queue.Invocations);
        Assert.Equal("Arthur Selby", request.CallerLabel);
        Assert.Equal(Release1PhoneCallRole.Arthur, request.Role);
        Assert.Equal(3, request.StageTexts.Count);
        Assert.StartsWith("Oi! This is Arthur Selby.", request.StageTexts[0], StringComparison.Ordinal);
        foreach (var stage in request.StageTexts) AssertPlayerCopy(stage);

        harness.Service.Update();
        harness.Service.Update();
        Assert.Single(harness.Queue.Invocations);
    }

    [Fact]
    public void A_required_failure_queues_the_warning_call_exactly_once()
    {
        using var harness = Release1WrongAddressHarness.Active(withPhone: true);
        harness.RecordNellAcceptedReceipt();
        harness.World.TotalMinutes = 300d * 60d;

        harness.Service.Update();
        harness.Service.Update();

        var request = Assert.Single(harness.Queue.Invocations);
        Assert.StartsWith("I am told the package is still sitting where it should not be.", request.StageTexts[0], StringComparison.Ordinal);
        Assert.Contains("twenty four hours", request.StageTexts[1], StringComparison.Ordinal);
        foreach (var stage in request.StageTexts) AssertPlayerCopy(stage);
        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
    }

    [Fact]
    public void The_two_outcomes_use_different_correlations()
    {
        using var clean = Release1WrongAddressHarness.CleanlyCompleted(withPhone: true);
        using var failed = Release1WrongAddressHarness.RequiredFailed(withPhone: true);

        clean.Service.Update();
        failed.Service.Update();

        Assert.NotEqual(
            clean.Queue.Invocations.Single().CorrelationId,
            failed.Queue.Invocations.Single().CorrelationId);
    }

    [Fact]
    public void A_late_or_make_good_completion_does_not_queue_the_courteous_call()
    {
        using var harness = Release1WrongAddressHarness.MakeGoodCompleted(withPhone: true);

        harness.Service.Update();

        Assert.Empty(harness.Queue.Invocations);
    }

    [Fact]
    public void A_failed_or_unavailable_phone_never_blocks_the_mission()
    {
        using var harness = Release1WrongAddressHarness.CleanlyCompleted(withPhone: true);
        harness.Queue.ThrowOnInvoke = true;

        harness.Service.Update();

        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Equal(
            Release1MissionState.Offered,
            harness.Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)].State);

        harness.Service.Update();
        Assert.Single(harness.Queue.Invocations);
    }

    [Fact]
    public void Arthur_is_refused_without_a_nell_prerequisite_on_the_same_attempt()
    {
        using var harness = Release1WrongAddressHarness.CleanlyCompleted(withPhone: true, recordNellReceipt: false);

        harness.Service.Update();

        Assert.Empty(harness.Queue.Invocations);
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
    }

    [Fact]
    public void A_nell_receipt_for_a_different_attempt_does_not_authorize_this_attempts_arthur_call()
    {
        using var harness = Release1WrongAddressHarness.CleanlyCompleted(withPhone: true, recordNellReceipt: false);
        var otherAttemptCorrelation = Release1LogicalCorrelation.Create(
            harness.Context.Snapshot.PlayerId, Release1MissionCatalog.WrongAddress, harness.Mission().Attempt + 1,
            Release1TransitionKind.MissionAccepted, "presentation-nell-wa-accepted-v1").Value;
        Assert.True(harness.Story.TryRecordPresentationReceipt(otherAttemptCorrelation).Accepted);

        harness.Service.Update();

        Assert.Empty(harness.Queue.Invocations);
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
    }

    [Fact]
    public void A_nell_receipt_under_small_courtesy_does_not_authorize_the_wrong_address_arthur_call()
    {
        using var harness = Release1WrongAddressHarness.CleanlyCompleted(withPhone: true, recordNellReceipt: false);
        var smallCourtesyCorrelation = Release1LogicalCorrelation.Create(
            harness.Context.Snapshot.PlayerId, Release1MissionCatalog.SmallCourtesy, harness.Mission().Attempt,
            Release1TransitionKind.MissionAccepted, "presentation-nell-wa-accepted-v1").Value;
        Assert.True(harness.Story.TryRecordPresentationReceipt(smallCourtesyCorrelation).Accepted);

        harness.Service.Update();

        Assert.Empty(harness.Queue.Invocations);
        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
    }

    [Fact]
    public void A_receipted_nell_message_is_an_acceptable_prerequisite_and_a_random_correlation_is_not()
    {
        Assert.True(Release1PhoneCallCorrelation.IsNellPrerequisite(NellMessageCorrelation()));
        Assert.False(Release1PhoneCallCorrelation.IsNellPrerequisite(ArbitraryCorrelation()));
    }

    private static Release1LogicalCorrelation NellMessageCorrelation() =>
        Release1LogicalCorrelation.Create(
            "76561190000000001", Release1MissionCatalog.WrongAddress, 1,
            Release1TransitionKind.MissionAccepted, "presentation-nell-wa-accepted-v1");

    private static Release1LogicalCorrelation ArbitraryCorrelation() =>
        Release1LogicalCorrelation.Create(
            "76561190000000001", Release1MissionCatalog.WrongAddress, 1,
            Release1TransitionKind.MissionAccepted, "some-unrelated-receipt");

    private static void AssertPlayerCopy(string value)
    {
        Assert.DoesNotContain('—', value);
        Assert.DoesNotContain('–', value);
        Assert.DoesNotContain("--", value, StringComparison.Ordinal);
        Assert.DoesNotContain('‘', value);
        Assert.DoesNotContain('’', value);
        Assert.DoesNotContain('%', value);
        Assert.Equal(value, Release1PlayerCopy.Normalize(value));
    }
}
