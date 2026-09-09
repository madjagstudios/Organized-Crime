using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1TransitionPublisherServiceTests
{
    [Fact]
    public void Pending_host_is_polled_at_the_fixed_interval_and_unlocked_shows_one_prompt()
    {
        var now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var harness = new Harness(
            Read(Release1PostBenziesUnlockReadStatus.Pending),
            Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        using var publisher = harness.CreatePublisher(() => now);

        publisher.OnLoadComplete();
        publisher.Update();
        Assert.False(publisher.PromptVisible);
        Assert.Equal(1, harness.Reader.ReadCount);

        now = now.AddMilliseconds(500);
        publisher.Update();
        Assert.Equal(1, harness.Reader.ReadCount);

        now = now.AddMilliseconds(500);
        publisher.Update();
        publisher.Update();

        Assert.True(publisher.PromptVisible);
        Assert.Equal(2, harness.Reader.ReadCount);
    }

    [Fact]
    public void EligibilityObserving_is_true_after_load_complete_false_after_preload_and_false_once_the_observation_completes()
    {
        var harness = new Harness(Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        using var publisher = harness.CreatePublisher(() => DateTime.UtcNow);

        Assert.False(publisher.EligibilityObserving);

        publisher.OnLoadComplete();
        Assert.True(publisher.EligibilityObserving);

        publisher.Update(); // reader resolves Unlocked immediately: observation completes this pass
        Assert.False(publisher.EligibilityObserving);

        publisher.OnPreLoad(); // starts a new load cycle; OnLoadComplete is a no-op mid-cycle otherwise
        Assert.False(publisher.EligibilityObserving);

        publisher.OnLoadComplete();
        Assert.True(publisher.EligibilityObserving);

        publisher.OnPreLoad();
        Assert.False(publisher.EligibilityObserving);
    }

    [Fact]
    public void EligibilityObserving_stays_true_while_the_reader_reports_pending()
    {
        var now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var harness = new Harness(
            Read(Release1PostBenziesUnlockReadStatus.Pending),
            Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        using var publisher = harness.CreatePublisher(() => now);

        publisher.OnLoadComplete();
        publisher.Update();
        Assert.True(publisher.EligibilityObserving);

        now = now.AddSeconds(1);
        publisher.Update();

        Assert.False(publisher.EligibilityObserving);
        Assert.True(publisher.PromptVisible);
    }

    [Fact]
    public void Locked_read_ends_observation_but_a_later_unlocked_read_shows_the_prompt_in_the_same_load_cycle()
    {
        var now = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        var harness = new Harness(
            Read(Release1PostBenziesUnlockReadStatus.Locked),
            Read(Release1PostBenziesUnlockReadStatus.Locked),
            Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        using var publisher = harness.CreatePublisher(() => now);

        publisher.OnLoadComplete();
        publisher.Update();
        Assert.False(publisher.EligibilityObserving);
        Assert.False(publisher.PromptVisible);
        Assert.Equal(1, harness.Reader.ReadCount);

        // The watch polls on its own interval, slower than the observation poll.
        now = now.AddSeconds(1);
        publisher.Update();
        Assert.Equal(1, harness.Reader.ReadCount);

        now = now.AddSeconds(4);
        publisher.Update();
        Assert.Equal(2, harness.Reader.ReadCount);
        Assert.False(publisher.PromptVisible);

        now = now.AddSeconds(5);
        publisher.Update();
        Assert.Equal(3, harness.Reader.ReadCount);
        Assert.True(publisher.PromptVisible);
        Assert.False(publisher.EligibilityObserving);

        // Once eligible the watch stops reading.
        now = now.AddSeconds(5);
        publisher.Update();
        Assert.Equal(3, harness.Reader.ReadCount);
    }

    [Fact]
    public void Watch_outlives_the_observation_deadline()
    {
        var now = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        var harness = new Harness(
            Read(Release1PostBenziesUnlockReadStatus.Locked),
            Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        using var publisher = harness.CreatePublisher(() => now);

        publisher.OnLoadComplete();
        publisher.Update();

        now = now.AddMinutes(30); // far past the 10 s harness deadline
        publisher.Update();

        Assert.True(publisher.PromptVisible);
        Assert.Equal(2, harness.Reader.ReadCount);
    }

    [Fact]
    public void Deadline_still_ends_a_pending_observation_without_starting_a_watch()
    {
        var now = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        var harness = new Harness(Read(Release1PostBenziesUnlockReadStatus.Pending));
        using var publisher = harness.CreatePublisher(() => now);

        publisher.OnLoadComplete();
        publisher.Update();
        Assert.Equal(1, harness.Reader.ReadCount);

        now = now.AddSeconds(11);
        publisher.Update();
        Assert.False(publisher.EligibilityObserving);
        var readsAtDeadline = harness.Reader.ReadCount;

        now = now.AddMinutes(5);
        publisher.Update();
        Assert.Equal(readsAtDeadline, harness.Reader.ReadCount);
        Assert.False(publisher.PromptVisible);
    }

    [Fact]
    public void Preload_ends_the_watch()
    {
        var now = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        var harness = new Harness(
            Read(Release1PostBenziesUnlockReadStatus.Locked),
            Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        using var publisher = harness.CreatePublisher(() => now);

        publisher.OnLoadComplete();
        publisher.Update();
        publisher.OnPreLoad();

        now = now.AddSeconds(10);
        publisher.Update();

        Assert.Equal(1, harness.Reader.ReadCount);
        Assert.False(publisher.PromptVisible);
    }

    [Fact]
    public void Faulted_read_ends_observation_without_a_watch()
    {
        var now = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        var harness = new Harness(
            Read(Release1PostBenziesUnlockReadStatus.Faulted),
            Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        using var publisher = harness.CreatePublisher(() => now);

        publisher.OnLoadComplete();
        publisher.Update();

        now = now.AddSeconds(10);
        publisher.Update();

        Assert.Equal(1, harness.Reader.ReadCount);
        Assert.False(publisher.PromptVisible);
    }

    [Fact]
    public void Watch_reaching_unlocked_in_the_accepted_state_publishes_the_intro_call_once()
    {
        var now = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        var harness = new Harness(
            AcceptedState(),
            Read(Release1PostBenziesUnlockReadStatus.Locked),
            Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        using var publisher = harness.CreatePublisher(() => now);

        publisher.OnLoadComplete();
        publisher.Update();
        Assert.Empty(harness.Queue.Requests);

        now = now.AddSeconds(5);
        publisher.Update();
        Assert.Single(harness.Queue.Requests);
        Assert.False(publisher.PromptVisible);

        now = now.AddSeconds(5);
        publisher.Update();
        Assert.Single(harness.Queue.Requests);
    }

    [Fact]
    public void Accept_persists_the_intro_revision_before_the_phone_boundary_is_invoked()
    {
        var harness = new Harness(Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        harness.Queue.BeforeInvoke = () =>
        {
            Assert.Equal(Release1RelationshipState.Accepted, harness.Repository.StoredState!.RelationshipState);
            Assert.Equal(harness.Repository.StoredState.Revision, harness.Story.LastPersistedRevision);
        };
        using var publisher = harness.CreatePublisher(() => DateTime.UtcNow);
        publisher.OnLoadComplete();
        publisher.Update();

        Assert.True(publisher.TryAccept());

        Assert.False(publisher.PromptVisible);
        Assert.Equal(1, harness.Queue.Invocations);
        Assert.Equal(Release1MissionState.Offered, harness.Repository.StoredState!.Missions[0].State);
        Assert.False(harness.Repository.StoredState.Release1Recognized);
    }

    [Fact]
    public void Publish_intro_call_disabled_queues_no_call_after_a_successful_accept()
    {
        var harness = new Harness(Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        using var publisher = harness.CreatePublisher(() => DateTime.UtcNow, publishIntroCall: false);
        publisher.OnLoadComplete();
        publisher.Update();

        Assert.True(publisher.TryAccept());

        Assert.False(publisher.PromptVisible);
        Assert.Equal(0, harness.Queue.Invocations);
        Assert.Equal(Release1MissionState.Offered, harness.Repository.StoredState!.Missions[0].State);
    }

    [Fact]
    public void Publish_intro_call_disabled_queues_no_call_when_loading_in_the_accepted_state()
    {
        var harness = new Harness(AcceptedState(), Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        using var publisher = harness.CreatePublisher(() => DateTime.UtcNow, publishIntroCall: false);

        publisher.OnLoadComplete();
        publisher.Update();
        publisher.Update();

        Assert.False(publisher.PromptVisible);
        Assert.Equal(0, harness.Queue.Invocations);
        Assert.Empty(harness.Story.GetPhonePresentationAttempts());
    }

    [Fact]
    public void Failed_intro_persistence_never_reaches_the_phone_boundary()
    {
        var harness = new Harness(Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        harness.Repository.FailUpdates = true;
        using var publisher = harness.CreatePublisher(() => DateTime.UtcNow);
        publisher.OnLoadComplete();
        publisher.Update();

        Assert.False(publisher.TryAccept());

        Assert.Equal(0, harness.Queue.Invocations);
        Assert.Null(harness.Story.State);
        Assert.Null(harness.Repository.StoredState);
    }

    [Fact]
    public void Not_now_uses_the_existing_deferred_story_transition_without_a_phone_request()
    {
        var harness = new Harness(Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        using var publisher = harness.CreatePublisher(() => DateTime.UtcNow);
        publisher.OnLoadComplete();
        publisher.Update();

        Assert.True(publisher.TryDefer());

        Assert.False(publisher.PromptVisible);
        Assert.Equal(Release1RelationshipState.Deferred, harness.Repository.StoredState!.RelationshipState);
        Assert.All(harness.Repository.StoredState.Missions, mission => Assert.Equal(Release1MissionState.Locked, mission.State));
        Assert.Equal(0, harness.Queue.Invocations);
    }

    [Fact]
    public void Durable_accepted_story_without_a_phone_receipt_publishes_once_for_the_load_epoch()
    {
        var harness = new Harness(AcceptedState(), Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        using var publisher = harness.CreatePublisher(() => DateTime.UtcNow);

        publisher.OnLoadComplete();
        publisher.Update();
        publisher.Update();

        Assert.False(publisher.PromptVisible);
        Assert.Equal(1, harness.Queue.AvailabilityChecks);
        Assert.Equal(1, harness.Queue.Invocations);
        Assert.Single(harness.Queue.Requests);
        Assert.Equal("Nell Grey", harness.Queue.Requests[0].CallerLabel);
        Assert.Equal(Release1PhonePresentationAttemptState.Delivered, harness.Story.GetPhonePresentationAttempts().Single().State);
    }

    [Fact]
    public void Pending_phone_is_not_retried_by_the_timer_and_gets_one_attempt_in_the_next_load_epoch()
    {
        var secondContext = Harness.ReadyContext with { LoadEpoch = 2 };
        var harness = new Harness(
            AcceptedState(),
            Read(Release1PostBenziesUnlockReadStatus.Unlocked),
            Read(Release1PostBenziesUnlockReadStatus.Unlocked, secondContext));
        harness.Queue.Available = false;
        using var publisher = harness.CreatePublisher(() => DateTime.UtcNow);

        publisher.OnLoadComplete();
        publisher.Update();
        publisher.Update();
        Assert.Equal(1, harness.Queue.AvailabilityChecks);
        Assert.Equal(0, harness.Queue.Invocations);
        Assert.Equal(Release1PhonePresentationAttemptState.Pending, harness.Story.GetPhonePresentationAttempts().Single().State);

        publisher.OnPreLoad();
        harness.Story.OnPreLoad();
        harness.Context.Snapshot = secondContext;
        harness.Story.OnLoadComplete();
        harness.Queue.Available = true;
        publisher.OnLoadComplete();
        publisher.Update();
        publisher.Update();

        Assert.Equal(2, harness.Queue.AvailabilityChecks);
        Assert.Equal(1, harness.Queue.Invocations);
        Assert.Equal(Release1PhonePresentationAttemptState.Delivered, harness.Story.GetPhonePresentationAttempts().Single().State);
    }

    [Fact]
    public void Ambiguous_phone_invocation_is_never_reinvoked_after_reload()
    {
        var secondContext = Harness.ReadyContext with { LoadEpoch = 2 };
        var harness = new Harness(
            AcceptedState(),
            Read(Release1PostBenziesUnlockReadStatus.Unlocked),
            Read(Release1PostBenziesUnlockReadStatus.Unlocked, secondContext));
        harness.Queue.ThrowOnInvoke = true;
        using var publisher = harness.CreatePublisher(() => DateTime.UtcNow);

        publisher.OnLoadComplete();
        publisher.Update();
        Assert.Equal(1, harness.Queue.Invocations);
        Assert.Equal(Release1PhonePresentationAttemptState.Ambiguous, harness.Story.GetPhonePresentationAttempts().Single().State);

        publisher.OnPreLoad();
        harness.Story.OnPreLoad();
        harness.Context.Snapshot = secondContext;
        harness.Story.OnLoadComplete();
        harness.Queue.ThrowOnInvoke = false;
        publisher.OnLoadComplete();
        publisher.Update();

        Assert.Equal(1, harness.Queue.Invocations);
        Assert.Equal(Release1PhonePresentationAttemptState.Ambiguous, harness.Story.GetPhonePresentationAttempts().Single().State);
    }

    [Fact]
    public void Duplicate_load_complete_does_not_retry_a_pending_phone_in_the_same_epoch()
    {
        var harness = new Harness(AcceptedState(), Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        harness.Queue.Available = false;
        using var publisher = harness.CreatePublisher(() => DateTime.UtcNow);

        publisher.OnLoadComplete();
        publisher.Update();
        publisher.OnLoadComplete();
        publisher.Update();

        Assert.Equal(1, harness.Queue.AvailabilityChecks);
        Assert.Equal(Release1PhonePresentationAttemptState.Pending, harness.Story.GetPhonePresentationAttempts().Single().State);
    }

    [Fact]
    public void Deferred_relationship_reoffers_once_next_load_and_not_now_does_not_add_another_transition()
    {
        var harness = new Harness(DeferredState(), Read(Release1PostBenziesUnlockReadStatus.Unlocked));
        using var publisher = harness.CreatePublisher(() => DateTime.UtcNow);
        var writesBeforeDecision = harness.Repository.UpdateCount;

        publisher.OnLoadComplete();
        publisher.Update();
        Assert.True(publisher.PromptVisible);
        Assert.True(publisher.TryDefer());

        Assert.False(publisher.PromptVisible);
        Assert.Equal(writesBeforeDecision, harness.Repository.UpdateCount);
        Assert.Equal(Release1RelationshipState.Deferred, harness.Repository.StoredState!.RelationshipState);
        Assert.Equal(0, harness.Queue.AvailabilityChecks);
    }

    private static ReaderResult Read(Release1PostBenziesUnlockReadStatus status) => new(status, status == Release1PostBenziesUnlockReadStatus.Unlocked
        ? new Release1PostBenziesUnlockSnapshot(Harness.ReadyContext, Release1CartelStatus.Defeated)
        : default);

    private static ReaderResult Read(Release1PostBenziesUnlockReadStatus status, Release1StoryHostContextSnapshot context) => new(
        status,
        status == Release1PostBenziesUnlockReadStatus.Unlocked
            ? new Release1PostBenziesUnlockSnapshot(context, Release1CartelStatus.Defeated)
            : default);

    private static Release1StoryState AcceptedState()
    {
        var correlation = Release1LogicalCorrelation.Create(
            Harness.ReadyContext.PlayerId,
            Release1MissionCatalog.IntroScopeKey,
            0,
            Release1TransitionKind.IntroAccepted,
            "accepted-before-load").Value;
        return Release1StoryState.CreateAccepted(Harness.ReadyContext.PlayerId, correlation);
    }

    private static Release1StoryState DeferredState()
    {
        const string receipt = "deferred-before-load";
        var command = new Release1StoryCommand(
            Harness.ReadyContext.SessionEpoch,
            Harness.ReadyContext.LoadEpoch,
            Harness.ReadyContext.PlayerId,
            Release1MissionCatalog.IntroScopeKey,
            0,
            Release1TransitionKind.IntroDeferred,
            receipt,
            Release1LogicalCorrelation.Create(
                Harness.ReadyContext.PlayerId,
                Release1MissionCatalog.IntroScopeKey,
                0,
                Release1TransitionKind.IntroDeferred,
                receipt).Value);
        return Release1StoryTransitions.Apply(null, command).State!;
    }

    private sealed class Harness
    {
        public static readonly Release1StoryHostContextSnapshot ReadyContext = new(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            1,
            "76561190000000001",
            Path.GetFullPath(Path.GetTempPath()));

        public Harness(params ReaderResult[] reads) : this(null, reads) { }

        public Harness(Release1StoryState? initialState, params ReaderResult[] reads)
        {
            Context = new FakeContext { Snapshot = ReadyContext };
            Repository = new FakeRepository(initialState);
            Story = new Release1StoryRuntimeService(Context, Repository);
            Story.OnPreLoad();
            Story.OnLoadComplete();
            Reader = new SequenceReader(reads);
            Queue = new QueueFake();
            Phone = new Release1PhoneCallService(Story, Queue, new CueFake());
        }

        public FakeContext Context { get; }
        public FakeRepository Repository { get; }
        public Release1StoryRuntimeService Story { get; }
        public SequenceReader Reader { get; }
        public QueueFake Queue { get; }
        public Release1PhoneCallService Phone { get; }

        public Release1TransitionPublisherService CreatePublisher(Func<DateTime> now, bool publishIntroCall = true) => new(
            Reader,
            Story,
            Phone,
            utcNow: now,
            deadline: TimeSpan.FromSeconds(10),
            pollInterval: TimeSpan.FromSeconds(1),
            watchInterval: TimeSpan.FromSeconds(5),
            publishIntroCall: publishIntroCall);
    }

    private readonly record struct ReaderResult(
        Release1PostBenziesUnlockReadStatus Status,
        Release1PostBenziesUnlockSnapshot Snapshot);

    private sealed class SequenceReader : IRelease1PostBenziesUnlockReader
    {
        private readonly Queue<ReaderResult> _results;
        private ReaderResult _last;

        public SequenceReader(IEnumerable<ReaderResult> results)
        {
            _results = new Queue<ReaderResult>(results);
            _last = _results.Count > 0 ? _results.Peek() : default;
        }

        public int ReadCount { get; private set; }

        public Release1PostBenziesUnlockReadStatus TryRead(out Release1PostBenziesUnlockSnapshot snapshot)
        {
            ReadCount++;
            if (_results.Count > 0) _last = _results.Dequeue();
            snapshot = _last.Snapshot;
            return _last.Status;
        }
    }

    private sealed class QueueFake : IRelease1PhoneCallQueue
    {
        public bool Available { get; set; } = true;
        public bool ThrowOnInvoke { get; set; }
        public int AvailabilityChecks { get; private set; }
        public int Invocations { get; private set; }
        public Action? BeforeInvoke { get; set; }
        public List<Release1PhoneCallRequest> Requests { get; } = new();
        public bool IsAvailable(Release1PhoneCallRequest request)
        {
            AvailabilityChecks++;
            return Available;
        }
        public void Invoke(Release1PhoneCallRequest request)
        {
            Invocations++;
            BeforeInvoke?.Invoke();
            if (ThrowOnInvoke) throw new InvalidOperationException("synthetic native queue failure");
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
        public Release1StoryHostContextReadStatus Status { get; set; } = Release1StoryHostContextReadStatus.Ready;
        public Release1StoryHostContextSnapshot Snapshot { get; set; }
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Status;
        }
    }

    private sealed class FakeRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public FakeRepository(Release1StoryState? storedState = null) => StoredState = storedState;
        public string BoundSaveFolder => Path.GetFullPath(Path.GetTempPath());
        public Release1StoryState? StoredState { get; private set; }
        public bool FailUpdates { get; set; }
        public int UpdateCount { get; private set; }
        public Release1StoryStoreLoadResult Load() => new(
            true,
            StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded,
            new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState),
            Release1StoryStoreFailureReason.None,
            string.Empty);
        public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
        {
            UpdateCount++;
            if (FailUpdates)
                return new(false, Release1StoryStoreUpdateStatus.Rejected, null, Release1StoryStoreFailureReason.AtomicReplacementFailed, "synthetic persistence failure");
            StoredState = state;
            return new(
                true,
                Release1StoryStoreUpdateStatus.Updated,
                new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state),
                Release1StoryStoreFailureReason.None,
                string.Empty);
        }
    }
}
