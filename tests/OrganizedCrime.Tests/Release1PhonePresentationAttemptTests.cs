using System.Reflection;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1PhonePresentationAttemptTests
{
    [Fact]
    public void The_models_nell_presentation_prefix_matches_the_runtime_constant()
    {
        var field = typeof(Release1PhonePresentationAttempt).GetField(
            "NellPresentationPrefix", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var modelPrefix = (string)field!.GetValue(null)!;

        Assert.Equal(Release1PhoneCallCorrelation.NellPresentationPrefix, modelPrefix);
    }

    [Fact]
    public void A_cross_attempt_nell_prior_is_a_typed_rejection_not_a_thrown_exception()
    {
        using var harness = Release1WrongAddressHarness.Active();
        var playerId = harness.Context.Snapshot.PlayerId;
        var nellCorrelation = Release1LogicalCorrelation.Create(
            playerId, Release1MissionCatalog.WrongAddress, 1,
            Release1TransitionKind.MissionAccepted, "release1-phone-nell-cross-attempt-test-v1").Value;
        Assert.True(harness.Story.TryAuthorizePhonePresentation(
            Release1MissionCatalog.WrongAddress, 1, nellCorrelation, "Nell", null).Accepted);
        Assert.True(harness.Story.TryTransitionPhonePresentation(
            nellCorrelation, Release1PhonePresentationAttemptState.Attempting).Accepted);
        Assert.True(harness.Story.TryTransitionPhonePresentation(
            nellCorrelation, Release1PhonePresentationAttemptState.Delivered).Accepted);

        // Drive the mission from attempt 1 to attempt 2 (MakeGoodActive), so the delivered Nell call
        // recorded above is now a prior from an earlier attempt of the same mission.
        harness.World.TotalMinutes = 200d * 60d;
        harness.Service.Update();
        Assert.Equal(Release1WrongAddressReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1WrongAddressDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(2, harness.Mission().Attempt);

        var arthurCorrelation = Release1LogicalCorrelation.Create(
            playerId, Release1MissionCatalog.WrongAddress, 2,
            Release1TransitionKind.MissionAccepted, "release1-phone-arthur-cross-attempt-test-v1").Value;

        var result = harness.Story.TryAuthorizePhonePresentation(
            Release1MissionCatalog.WrongAddress, 2, arthurCorrelation, "Arthur", nellCorrelation);

        Assert.False(result.Accepted);
        Assert.Equal(Release1StoryRuntimeRejectReason.InvalidTransition, result.RejectReason);
    }

    [Fact]
    public void Attempting_is_persisted_before_native_invocation_and_normal_return_is_delivered()
    {
        var harness = ActiveHarness();
        var request = Request(harness, Release1PhoneCallRole.Nell, "attempting");
        var queue = new QueueFake
        {
            BeforeInvoke = () => Assert.Equal(
                Release1PhonePresentationAttemptState.Attempting,
                harness.Story.GetPhonePresentationAttempt(request.CorrelationId)!.State)
        };
        using var service = new Release1PhoneCallService(harness.Story, queue, new CueFake());

        Assert.Equal(Release1PhoneCallResultState.Delivered, service.TryQueue(request).State);
        Assert.Equal(Release1PhonePresentationAttemptState.Delivered, harness.Story.GetPhonePresentationAttempt(request.CorrelationId)!.State);
    }

    [Fact]
    public void Manager_unavailable_is_pending_and_a_later_preflight_can_deliver()
    {
        var harness = ActiveHarness();
        var queue = new QueueFake { Available = false };
        using var service = new Release1PhoneCallService(harness.Story, queue, new CueFake());
        var request = Request(harness, Release1PhoneCallRole.Nell, "pending");

        Assert.Equal(Release1PhoneCallResultState.Pending, service.TryQueue(request).State);
        Assert.Equal(Release1PhonePresentationAttemptState.Pending, harness.Story.GetPhonePresentationAttempt(request.CorrelationId)!.State);
        queue.Available = true;
        Assert.Equal(Release1PhoneCallResultState.Delivered, service.TryQueue(request).State);
        Assert.Equal(1, queue.Invocations);
    }

    [Fact]
    public void Throwing_invocation_becomes_ambiguous_and_is_never_automatically_retried()
    {
        var harness = ActiveHarness();
        var queue = new QueueFake { ThrowOnInvoke = true };
        using var service = new Release1PhoneCallService(harness.Story, queue, new CueFake());
        var request = Request(harness, Release1PhoneCallRole.Nell, "ambiguous");

        Assert.Equal(Release1PhoneCallResultState.Ambiguous, service.TryQueue(request).State);
        queue.ThrowOnInvoke = false;
        Assert.Equal(Release1PhoneCallResultState.Ambiguous, service.TryQueue(request).State);
        Assert.Equal(1, queue.Invocations);
    }

    [Fact]
    public void Reload_recovers_attempting_as_ambiguous_without_retrying()
    {
        var harness = ActiveHarness();
        var request = Request(harness, Release1PhoneCallRole.Nell, "reload-ambiguous");
        Assert.True(harness.Story.TryAuthorizePhonePresentation(request.MissionKey, request.Attempt, request.CorrelationId, request.Role.ToString(), null).Accepted);
        Assert.True(harness.Story.TryTransitionPhonePresentation(request.CorrelationId, Release1PhonePresentationAttemptState.Attempting).Accepted);

        harness.Context.Snapshot = harness.Context.Snapshot with { LoadEpoch = 2 };
        using var restored = new Release1StoryRuntimeService(harness.Context, harness.Repository);
        restored.OnPreLoad();
        restored.OnLoadComplete();

        Assert.Equal(Release1PhonePresentationAttemptState.Ambiguous, restored.GetPhonePresentationAttempt(request.CorrelationId)!.State);
    }

    [Fact]
    public void Presentation_attempt_states_round_trip_through_the_story_codec()
    {
        var harness = ActiveHarness();
        using var service = new Release1PhoneCallService(harness.Story, new QueueFake(), new CueFake());
        var request = Request(harness, Release1PhoneCallRole.Nell, "codec");
        Assert.Equal(Release1PhoneCallResultState.Delivered, service.TryQueue(request).State);

        Assert.True(Release1StorySaveCodec.TrySerialize(harness.Story.State, out var json, out var encodeResult), encodeResult.Message);
        Assert.True(Release1StorySaveCodec.TryDeserialize(json, out var envelope, out var decodeResult), decodeResult.Message);
        Assert.Equal(harness.Story.State, envelope!.Story);
    }

    [Fact]
    public void Nell_precedes_Arthur_while_caller_labels_remain_presentation_only()
    {
        var harness = ActiveHarness();
        var queue = new QueueFake();
        using var service = new Release1PhoneCallService(harness.Story, queue, new CueFake());
        var nell = Request(harness, Release1PhoneCallRole.Nell, "nell");
        var arthur = Release1PhoneCallRequest.Create(
            harness.Context.Snapshot,
            Release1MissionCatalog.SmallCourtesy,
            1,
            Release1PhoneCallCorrelation.Create(harness.Context.Snapshot.PlayerId, Release1MissionCatalog.SmallCourtesy, 1, Release1PhoneCallRole.Arthur, "arthur"),
            Release1PhoneCallRole.Arthur,
            "A presentation-only label",
            new[] { "presentation-only" },
            nell.CorrelationId);

        Assert.Equal(Release1PhoneCallResultState.Delivered, service.TryQueue(nell).State);
        Assert.Equal(Release1PhoneCallResultState.Delivered, service.TryQueue(arthur).State);
        Assert.Equal(new[] { "Nell", "A presentation-only label" }, queue.Requests.Select(request => request.CallerLabel));
        Assert.Equal(20, harness.Story.State!.Standing);
        Assert.Empty(harness.Story.State.NativeEffects);
    }

    [Fact]
    public void Completion_and_teardown_only_manage_the_presentation_receipt_and_cue()
    {
        var harness = ActiveHarness();
        var cue = new CueFake();
        var request = Request(harness, Release1PhoneCallRole.Nell, "completed");
        using (var service = new Release1PhoneCallService(harness.Story, new QueueFake(), cue))
        {
            Assert.Equal(Release1PhoneCallResultState.Delivered, service.TryQueue(request).State);
            Assert.True(service.ObserveCompleted(request.CorrelationId));
        }

        Assert.Equal(Release1PhonePresentationAttemptState.Completed, harness.Story.GetPhonePresentationAttempt(request.CorrelationId)!.State);
        Assert.Equal(20, harness.Story.State!.Standing);
        Assert.Empty(harness.Story.State.NativeEffects);
        Assert.Equal(Release1MissionState.Offered, harness.Story.State.Missions[0].State);
    }

    private static Release1PhoneCallRequest Request(Harness harness, Release1PhoneCallRole role, string receipt) =>
        Release1PhoneCallRequest.Create(
            harness.Context.Snapshot,
            Release1MissionCatalog.SmallCourtesy,
            1,
            Release1PhoneCallCorrelation.Create(harness.Context.Snapshot.PlayerId, Release1MissionCatalog.SmallCourtesy, 1, role, receipt),
            role,
            role.ToString(),
            new[] { "presentation-only" });

    private static Harness ActiveHarness()
    {
        var context = new FakeContext();
        var repository = new FakeRepository();
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        story.TryExecute(new(
            context.Snapshot.SessionEpoch,
            context.Snapshot.LoadEpoch,
            context.Snapshot.PlayerId,
            Release1MissionCatalog.IntroScopeKey,
            0,
            Release1TransitionKind.IntroAccepted,
            "intro",
            "oc10/v1/76561190000000001/release1.intro/0/IntroAccepted/intro"));
        return new(story, context, repository);
    }

    private sealed record Harness(Release1StoryRuntimeService Story, FakeContext Context, FakeRepository Repository);

    private sealed class QueueFake : IRelease1PhoneCallQueue
    {
        public bool Available { get; set; } = true;
        public bool ThrowOnInvoke { get; set; }
        public int Invocations { get; private set; }
        public List<Release1PhoneCallRequest> Requests { get; } = new();
        public Action? BeforeInvoke { get; set; }
        public bool IsAvailable(Release1PhoneCallRequest request) => Available;
        public void Invoke(Release1PhoneCallRequest request)
        {
            Invocations++;
            BeforeInvoke?.Invoke();
            if (ThrowOnInvoke) throw new InvalidOperationException("native queue failure");
            Requests.Add(request);
        }
    }

    private sealed class CueFake : IRelease1PayphoneCue
    {
        public bool TryShow(string correlationId) => true;
        public void End(string correlationId) { }
        public void Reconcile(string correlationId) { }
        public void Dispose() { }
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; set; } = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), 1, "76561190000000001", Path.GetFullPath(Path.GetTempPath()));
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot) { snapshot = Snapshot; return Release1StoryHostContextReadStatus.Ready; }
    }

    private sealed class FakeRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public string BoundSaveFolder { get; } = Path.GetFullPath(Path.GetTempPath());
        public Release1StoryState? StoredState { get; private set; }
        public Release1StoryStoreLoadResult Load() => new(true, StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded, new Release1StorySaveEnvelope(1, StoredState), Release1StoryStoreFailureReason.None, string.Empty);
        public Release1StoryStoreUpdateResult Update(Release1StoryState? state) { StoredState = state; return new(true, Release1StoryStoreUpdateStatus.Updated, new Release1StorySaveEnvelope(1, state), Release1StoryStoreFailureReason.None, string.Empty); }
    }
}
