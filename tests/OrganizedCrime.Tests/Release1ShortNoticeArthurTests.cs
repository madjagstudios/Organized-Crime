using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// Arthur's Short Notice warning call: queued exactly once on a required failure, never on a clean
/// completion (Short Notice has no clean call, unlike Wrong Address and Room With No Name) and never
/// on a make-good failure. Mirrors <see cref="Release1WrongAddressArthurTests"/> in shape.
/// </summary>
public sealed class Release1ShortNoticeArthurTests
{
    [Fact]
    public void A_required_failure_queues_the_warning_call_exactly_once()
    {
        using var harness = Release1ShortNoticeHarness.RequiredFailed(withPhone: true);

        harness.Service.Update();

        var request = Assert.Single(harness.Queue.Invocations);
        Assert.Equal("Arthur Selby", request.CallerLabel);
        Assert.Equal(Release1PhoneCallRole.Arthur, request.Role);
        Assert.Equal(3, request.StageTexts.Count);
        Assert.StartsWith("The order did not land", request.StageTexts[0], StringComparison.Ordinal);
        Assert.Contains("twelve hours", request.StageTexts[1], StringComparison.Ordinal);
        foreach (var stage in request.StageTexts) AssertPlayerCopy(stage);
        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);

        harness.Service.Update();
        Assert.Single(harness.Queue.Invocations);
    }

    // The phone presentation attempt Arthur's queue call records is durably persisted the moment it
    // reaches Delivered (Release1StoryRuntimeService.PersistState writes straight through when no
    // native effect is Applied, which is the case here), so a service rebuilt from the same
    // repository sees it as already Delivered and never invokes a fresh queue for it.
    [Fact]
    public void A_required_failure_queues_the_warning_call_exactly_once_across_a_reconstruction()
    {
        var repository = new Release1ShortNoticeHarness.FakeRepository();
        var world = new Release1ShortNoticeHarness.FakeWorld();
        using (var first = Release1ShortNoticeHarness.MakeGoodOffered(repository, world, withPhone: true))
        {
            first.RecordNellAcceptedReceipt();
            first.Service.Update();
            Assert.Single(first.Queue.Invocations);
            first.Service.OnPreLoad();
        }

        var context = new Release1ShortNoticeHarness.FakeContext();
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var queue = new Release1ShortNoticeHarness.FakeQueue();
        var logs = new List<string>();
        var phone = new Release1PhoneCallService(story, queue, new Release1ShortNoticeHarness.FakeCue(), log: logs.Add);
        using var service = new Release1ShortNoticeMissionService(story, world, phone, log: logs.Add);

        service.OnLoadComplete();
        service.Update();

        Assert.Empty(queue.Invocations);

        phone.Dispose();
        story.Dispose();
    }

    [Fact]
    public void A_clean_completion_never_queues_arthur()
    {
        using var harness = Release1ShortNoticeHarness.Active(withPhone: true);
        var assignment = harness.Assignment;
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 1_000f);

        Assert.Equal(Release1ShortNoticeDepositStatus.Paid, harness.Service.ReconcileDeposit());
        harness.Service.Update();

        Assert.Equal(Release1MissionState.Satisfied, harness.Mission().State);
        Assert.Empty(harness.Queue.Invocations);
    }

    [Fact]
    public void A_make_good_failure_never_queues_arthur()
    {
        using var harness = Release1ShortNoticeHarness.MakeGoodActive(withPhone: true);
        harness.RecordNellAcceptedReceipt();
        harness.World.TotalMinutes = 400d * 60d;

        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.MakeGoodFailure, harness.Mission().LastOutcome);
        Assert.Empty(harness.Queue.Invocations);
    }

    [Fact]
    public void The_request_carries_the_three_stages_and_the_nell_prerequisite()
    {
        using var harness = Release1ShortNoticeHarness.RequiredFailed(withPhone: true);

        harness.Service.Update();

        var request = Assert.Single(harness.Queue.Invocations);
        Assert.Equal(Release1MissionCatalog.ShortNotice, request.MissionKey);
        Assert.NotNull(request.RequiredPriorCorrelationId);
        Assert.True(Release1LogicalCorrelation.TryParse(request.RequiredPriorCorrelationId, out var prior));
        Assert.True(Release1PhoneCallCorrelation.IsNellPrerequisite(prior));
        Assert.Equal("presentation-nell-sn-accepted-v1", prior.ReceiptId);
        Assert.Equal(
            "Do not make her ask twice. You are pissing me off.",
            request.StageTexts[2]);
    }

    [Fact]
    public void Arthur_is_refused_without_a_nell_prerequisite_on_the_same_attempt()
    {
        using var harness = Release1ShortNoticeHarness.MakeGoodOffered(withPhone: true);

        harness.Service.Update();

        Assert.Empty(harness.Queue.Invocations);
        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
    }

    [Fact]
    public void A_throwing_queue_is_logged_and_never_rethrown()
    {
        using var harness = Release1ShortNoticeHarness.RequiredFailed(withPhone: true);
        harness.Queue.ThrowOnInvoke = true;

        var exception = Record.Exception(() => harness.Service.Update());

        Assert.Null(exception);
        Assert.Single(harness.Queue.Invocations);
    }

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
