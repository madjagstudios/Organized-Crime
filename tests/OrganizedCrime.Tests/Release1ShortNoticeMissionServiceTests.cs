using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ShortNoticeMissionServiceTests
{
    [Fact]
    public void Offer_is_unavailable_until_the_room_with_no_name_is_satisfied()
    {
        using var blocked = Release1ShortNoticeHarness.RoomWithNoNameStillActive();
        Assert.Equal(Release1ShortNoticeOfferStatus.Ineligible, blocked.Service.OfferStatus);

        using var ready = Release1ShortNoticeHarness.Offered();
        Assert.Equal(Release1ShortNoticeOfferStatus.Available, ready.Service.OfferStatus);
    }

    [Fact]
    public void Review_produces_three_bricks_at_one_deterministically_chosen_drop()
    {
        using var harness = Release1ShortNoticeHarness.Offered();
        var revisionBefore = harness.Story.State!.Revision;

        var review = harness.Service.TryReview();

        Assert.Equal(Release1ShortNoticeReviewStatus.Ready, review.Status);
        Assert.NotNull(review.Quote);
        Assert.Equal(Release1ShortNoticeAssignmentMode.Primary, review.Quote!.Assignment.Mode);
        Assert.Equal(3, review.Quote.Assignment.RequiredQuantity);
        Assert.Equal("brick", review.Quote.Assignment.PackagingId);
        Assert.Equal(12d, review.Quote.DeadlineDurationHours);
        Assert.False(string.IsNullOrEmpty(review.Quote.Assignment.HandoffDropGuid));
        Assert.Equal(revisionBefore, harness.Story.State.Revision);
        Assert.Empty(harness.Story.State.ShortNoticeAssignments);

        // Same acceptance correlation, same deterministic drop, every time it is reviewed again.
        var reviewedAgain = harness.Service.TryReview();
        Assert.Equal(review.Quote.Assignment.HandoffDropGuid, reviewedAgain.Quote!.Assignment.HandoffDropGuid);
    }

    [Fact]
    public void Review_reports_NoEmptyDeadDrop_when_every_drop_is_occupied_and_accept_changes_nothing()
    {
        using var harness = Release1ShortNoticeHarness.Offered();
        Assert.Equal(Release1ShortNoticeReviewStatus.Ready, harness.Service.TryReview().Status);
        harness.World.EmptyDropGuids = Array.Empty<string>();

        // Acceptance rebuilds its own quote from the current world before writing anything, so the
        // stale-but-still-reviewed quote from before the drops filled up is not enough on its own.
        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1ShortNoticeDecisionStatus.NoEmptyDeadDrop, decision.Status);
        Assert.Empty(harness.Story.State!.ShortNoticeAssignments);
        Assert.Equal(Release1MissionState.Offered, harness.Mission().State);

        var review = harness.Service.TryReview();

        Assert.Equal(Release1ShortNoticeReviewStatus.NoEmptyDeadDrop, review.Status);
        Assert.Null(review.Quote);
        Assert.Equal(Release1ShortNoticeOfferStatus.NoEmptyDeadDrop, harness.Service.OfferStatus);
    }

    [Fact]
    public void Accept_writes_the_assignment_and_the_twelve_hour_deadline_and_activates_the_mission()
    {
        using var harness = Release1ShortNoticeHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        var quote = harness.Service.TryReview().Quote!;

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1ShortNoticeDecisionStatus.Accepted, decision.Status);
        var assignment = Assert.Single(harness.Story.State!.ShortNoticeAssignments);
        Assert.Equal(quote.Assignment, assignment);
        var mission = harness.Mission();
        Assert.Equal(Release1MissionState.Active, mission.State);
        Assert.Equal("short-notice-v1", mission.TermsVersion);
        Assert.Equal(100d, mission.AcceptedGameTimeHours);
        Assert.Equal(112d, mission.DeadlineGameTimeHours);
    }

    [Fact]
    public void Accept_after_the_quote_changed_reports_QuoteChanged_and_writes_nothing()
    {
        using var harness = Release1ShortNoticeHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        Assert.Equal(Release1ShortNoticeReviewStatus.Ready, harness.Service.TryReview().Status);
        // A higher asking price product is discovered after review; acceptance re-quotes and the
        // higher priced product now wins selection, so the quote on file no longer matches.
        harness.World.Products = new[]
        {
            new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true),
            new Release1SmallCourtesyProductCandidate("methamphetamine", "Methamphetamine", 5_000d, true)
        };

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1ShortNoticeDecisionStatus.QuoteChanged, decision.Status);
        Assert.Empty(harness.Story.State!.ShortNoticeAssignments);
        Assert.Equal(Release1MissionState.Offered, harness.Mission().State);
    }

    [Fact]
    public void Accept_during_a_save_reports_PersistenceDeferred_and_the_acceptance_rides_the_next_save()
    {
        using var harness = Release1ShortNoticeHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        var quote = harness.Service.TryReview().Quote!;
        harness.Service.OnSaveStart();

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1ShortNoticeDecisionStatus.PersistenceDeferred, decision.Status);
        Assert.Empty(harness.Story.State!.ShortNoticeAssignments);

        harness.Service.OnSaveComplete();
        var retried = harness.Service.TryAccept();

        Assert.Equal(Release1ShortNoticeDecisionStatus.Accepted, retried.Status);
        var assignment = Assert.Single(harness.Story.State!.ShortNoticeAssignments);
        Assert.Equal(quote.Assignment, assignment);
    }

    [Fact]
    public void Defer_records_MissionDeferred_and_a_later_load_re_offers_once_and_advances_the_attempt()
    {
        var repository = new Release1ShortNoticeHarness.FakeRepository();
        var world = new Release1ShortNoticeHarness.FakeWorld();
        using (var first = Release1ShortNoticeHarness.Offered(repository, world))
        {
            first.Service.TryReview();
            Assert.Equal(Release1ShortNoticeDecisionStatus.Deferred, first.Service.TryDefer().Status);
            Assert.Equal(Release1MissionState.Deferred, first.Mission().State);
            Assert.Equal(1, first.Mission().Attempt);
            first.Service.OnPreLoad();
        }

        using var restored = Release1ShortNoticeHarness.Load(repository, world);
        Assert.Equal(Release1MissionState.Offered, restored.Mission().State);
        Assert.Equal(2, restored.Mission().Attempt);
        Assert.Equal(Release1ShortNoticeOfferStatus.Available, restored.Service.OfferStatus);
    }

    [Fact]
    public void Make_good_offers_two_bricks_of_the_same_product_at_a_freshly_chosen_drop()
    {
        using var harness = Release1ShortNoticeHarness.MakeGoodOffered();
        var primary = harness.Story.State!.ShortNoticeAssignments.Single(a => a.Attempt == 1);

        var quote = harness.Service.TryReview().Quote!;

        Assert.Equal(Release1ShortNoticeAssignmentMode.MakeGood, quote.Assignment.Mode);
        Assert.Equal(primary.ProductId, quote.Assignment.ProductId);
        Assert.Equal(2, quote.Assignment.RequiredQuantity);
        Assert.Equal("brick", quote.Assignment.PackagingId);
        Assert.Equal(2, quote.Assignment.Attempt);
        Assert.Equal(12d, quote.DeadlineDurationHours);
        Assert.NotEqual(primary.AuthorizationCorrelationId, quote.Assignment.AuthorizationCorrelationId);
    }

    [Fact]
    public void Recovery_offers_one_brick_with_no_deadline()
    {
        using var harness = Release1ShortNoticeHarness.RecoveryAvailable();

        var quote = harness.Service.TryReview().Quote!;
        Assert.Equal(Release1ShortNoticeAssignmentMode.Recovery, quote.Assignment.Mode);
        Assert.Equal(1, quote.Assignment.RequiredQuantity);
        Assert.Equal("brick", quote.Assignment.PackagingId);
        Assert.Null(quote.DeadlineDurationHours);

        Assert.Equal(Release1ShortNoticeDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Null(harness.Mission().DeadlineGameTimeHours);
        Assert.Equal(Release1MissionState.RecoveryActive, harness.Mission().State);
    }

    [Fact]
    public void The_window_lapsing_records_RequiredFailure_once_and_costs_twelve_standing()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var standingBefore = harness.Story.State!.Standing;
        harness.World.TotalMinutes = 200d * 60d;

        harness.Service.Update();
        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(standingBefore - 12, harness.Story.State!.Standing);
        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        Assert.Single(harness.Mission().PenaltyReceiptIds);
    }

    [Fact]
    public void The_window_lapsing_in_the_make_good_records_MakeGoodFailed_once_and_costs_eight_standing()
    {
        using var harness = Release1ShortNoticeHarness.MakeGoodActive();
        var standingBefore = harness.Story.State!.Standing;
        harness.World.TotalMinutes = 400d * 60d;

        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(standingBefore - 8, harness.Story.State!.Standing);
        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);
    }

    [Fact]
    public void The_window_never_fails_a_stage_that_already_has_a_consumption_effect()
    {
        using var harness = Release1ShortNoticeHarness.DeliveredThisSession();
        harness.World.TotalMinutes = 200d * 60d;

        harness.Service.Update();

        Assert.NotEqual(Release1MissionState.MakeGoodOffered, harness.Mission().State);
    }

    [Fact]
    public void The_service_subscribes_to_the_handoff_drop_once_and_disposes_it_on_pre_load()
    {
        using var harness = Release1ShortNoticeHarness.Active();

        Assert.Equal(harness.Assignment.HandoffDropGuid, harness.World.SubscribedDropGuid);
        Assert.False(harness.World.SubscriptionDisposed);

        // A second convergence pass with nothing changed rebinds to the same guid rather than
        // tearing the subscription down and reopening it.
        harness.Service.Update();
        Assert.Equal(harness.Assignment.HandoffDropGuid, harness.World.SubscribedDropGuid);
        Assert.False(harness.World.SubscriptionDisposed);

        harness.Service.OnPreLoad();
        Assert.True(harness.World.SubscriptionDisposed);
    }

    // Pins the OC-58 owner proof's F6 gate against a regression back to per-unit math: three Big
    // Monkey bricks in one slot read a pre-consumption MonetaryValue of 4320 (the whole stack, not
    // 4320 per brick), and consuming the whole stack must pay exactly 4320 * 1.75 = 7560, not the
    // 22680 a per-unit reading of the same slot value would produce (3 * 4320 * 1.75).
    [Fact]
    public void Three_bricks_reading_a_per_stack_slot_value_of_4320_pay_exactly_7560_at_one_hundred_and_seventy_five_percent()
    {
        using var harness = Release1ShortNoticeHarness.Active();
        var assignment = harness.Assignment;
        Assert.Equal(Release1ShortNoticeValueConvention.PerStack, assignment.ValueConvention);
        Assert.Equal(3, assignment.RequiredQuantity);
        harness.World.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity, monetaryValue: 4_320f);
        var cashBefore = harness.World.CashBalance;

        var status = harness.Service.ReconcileDeposit();

        Assert.Equal(Release1ShortNoticeDepositStatus.Paid, status);
        Assert.Equal(0, harness.World.Slot(assignment.HandoffDropGuid, 0).Quantity);
        Assert.Equal(cashBefore + 7_560f, harness.World.CashBalance);
    }
}

internal static class Release1ShortNoticeHarness
{
    private const string PlayerId = "76561190000000001";
    private static readonly Guid SessionEpoch = Guid.Parse("77777777-7777-7777-7777-777777777777");

    internal static Harness RoomWithNoNameStillActive()
    {
        var repository = new FakeRepository();
        var context = new FakeContext();
        var story = SeededStory(context, repository, satisfyRoomWithNoName: false);
        var world = new FakeWorld();
        var logs = new List<string>();
        var service = new Release1ShortNoticeMissionService(story, world, log: logs.Add);
        service.OnLoadComplete();
        return new(story, service, world, context, repository, logs, phone: null, queue: new FakeQueue());
    }

    internal static Harness Offered(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        repository ??= new FakeRepository();
        var context = new FakeContext();
        var story = SeededStory(context, repository, satisfyRoomWithNoName: true);
        world ??= new FakeWorld();
        var logs = new List<string>();
        var queue = new FakeQueue();
        var phone = withPhone ? new Release1PhoneCallService(story, queue, new FakeCue(), log: logs.Add) : null;
        var service = new Release1ShortNoticeMissionService(story, world, phone, log: logs.Add);
        service.OnLoadComplete();
        repository.ResetEvidence();
        world.ResetMutationEvidence();
        return new(story, service, world, context, repository, logs, phone, queue);
    }

    internal static Harness Load(FakeRepository repository, FakeWorld world)
    {
        var context = new FakeContext();
        var story = NewStory(context, repository);
        var logs = new List<string>();
        var service = new Release1ShortNoticeMissionService(story, world, log: logs.Add);
        service.OnLoadComplete();
        world.ResetMutationEvidence();
        return new(story, service, world, context, repository, logs, phone: null, queue: new FakeQueue());
    }

    internal static void Save(Harness harness)
    {
        harness.Story.OnSaveStart();
        harness.Service.OnSaveStart();
        harness.Story.OnSaveComplete();
        harness.Service.OnSaveComplete();
        harness.World.MarkSaved();
    }

    // valueConvention defaults to Release1ShortNoticeValueRule.Observed, the production convention,
    // so a plain Active() call exercises exactly what the real acceptance path freezes and tracks it
    // automatically if that constant ever moves again. Acceptance always freezes the assignment's
    // ValueConvention from that same constant, so a test that needs to exercise the other reading of
    // GetMonetaryValue() cannot get it through the normal review/accept path; instead it bypasses
    // Service.TryAccept() and drives the same durable acceptance the service itself would, with a
    // copy of the reviewed assignment whose ValueConvention is swapped. Every other field (product,
    // packaging, drop, deadline, authorization correlation) is exactly what the normal path would
    // have produced, so this differs from Active() only in which convention is frozen.
    internal static Harness Active(
        FakeRepository? repository = null,
        FakeWorld? world = null,
        bool withPhone = false,
        Release1ShortNoticeValueConvention valueConvention = Release1ShortNoticeValueRule.Observed)
    {
        var harness = Offered(repository, world, withPhone);
        harness.World.TotalMinutes = 6_000d;
        var quote = harness.Service.TryReview().Quote!;
        if (valueConvention == Release1ShortNoticeValueRule.Observed)
        {
            Assert.Equal(Release1ShortNoticeDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        }
        else
        {
            var assignment = quote.Assignment with { ValueConvention = valueConvention };
            var command = new Release1StoryCommand(
                harness.Context.Snapshot.SessionEpoch,
                harness.Context.Snapshot.LoadEpoch,
                harness.Context.Snapshot.PlayerId,
                Release1MissionCatalog.ShortNotice,
                assignment.Attempt,
                Release1TransitionKind.MissionAccepted,
                $"short-notice-primary-accept-v1-a{assignment.Attempt}",
                assignment.AuthorizationCorrelationId,
                "short-notice-v1",
                100d,
                112d);
            var result = harness.Story.TryExecuteShortNoticeAcceptanceDurably(command, assignment);
            Assert.True(result.Accepted, result.Message);
        }
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
        Assert.Equal(Release1ShortNoticeReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1ShortNoticeDecisionStatus.Accepted, harness.Service.TryAccept().Status);
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
        Assert.Equal(Release1ShortNoticeReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1ShortNoticeDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.RecoveryActive, harness.Mission().State);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Drives a required-failure primary attempt through to the still-unaccepted make-good offer,
    // with the Nell accepted-message receipt already recorded so a single subsequent Update() can
    // queue Arthur's warning call without any further fixture setup.
    internal static Harness RequiredFailed(bool withPhone = false)
    {
        var harness = MakeGoodOffered(withPhone: withPhone);
        harness.RecordNellAcceptedReceipt();
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    internal static Harness DeliveredThisSession(FakeRepository? repository = null, FakeWorld? world = null)
    {
        var harness = Active(repository, world);
        var mission = harness.Mission();
        var activationReceipt = $"short-notice-activate-v1-a{mission.Attempt}";
        var activationCorrelation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.ShortNotice, mission.Attempt, Release1TransitionKind.MissionActivated, activationReceipt).Value;
        var effect = new Release1NativeEffectJournalEntry(
            $"short-notice-cargo-v1-a{mission.Attempt}",
            Release1MissionCatalog.ShortNotice,
            mission.Attempt,
            "CargoTransfer",
            "handoff-drop",
            "oc-short-notice",
            "cargo-identity",
            Release1NativeEffectPhase.Prepared,
            null,
            harness.Story.State!.Revision + 1,
            AuthorizedStoryCorrelationId: activationCorrelation,
            AuthorizedMissionRevision: -1);
        Assert.True(harness.Story.TryPrepareNativeEffect(effect).Accepted);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Seeds a story whose Small Courtesy and Wrong Address missions (and, when requested, Room With
    // No Name too) are Satisfied through the same pure Release1StoryTransitions path Task 3's Satisfy
    // helper uses, then loads it into a fresh runtime service backed by the given repository.
    private static Release1StoryRuntimeService SeededStory(FakeContext context, FakeRepository repository, bool satisfyRoomWithNoName)
    {
        var state = Release1StoryState.CreateAccepted(context.Snapshot.PlayerId, Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-short-notice").Value);
        state = Satisfy(state, Release1MissionCatalog.SmallCourtesy);
        state = Satisfy(state, Release1MissionCatalog.WrongAddress);
        if (satisfyRoomWithNoName) state = Satisfy(state, Release1MissionCatalog.RoomWithNoName);
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
            Release1ShortNoticeMissionService service,
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
        public Release1ShortNoticeMissionService Service { get; }
        public FakeWorld World { get; }
        public FakeContext Context { get; }
        public FakeRepository Repository { get; }
        public List<string> Logs { get; }
        public Release1PhoneCallService? Phone { get; }
        public FakeQueue Queue { get; }
        public Release1ShortNoticeAssignment Assignment => Story.State!.ShortNoticeAssignments.MaxBy(value => value.Attempt)!;

        public Release1MissionRecord Mission() =>
            Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)];

        public Release1ShortNoticeProgress? Progress() =>
            Story.State?.ShortNoticeProgress.SingleOrDefault(progress => progress.Attempt == Assignment.Attempt);

        public Release1NativeEffectJournalEntry Consumption() => Story.State!.NativeEffects.Single(effect =>
            effect.MissionKey == Release1MissionCatalog.ShortNotice &&
            effect.EffectKind == "CargoTransfer" &&
            effect.Attempt == Assignment.Attempt);

        public Release1NativeEffectJournalEntry Reward() => Story.State!.NativeEffects.Single(effect =>
            effect.MissionKey == Release1MissionCatalog.ShortNotice &&
            effect.EffectKind == "Reward" &&
            effect.Attempt == Assignment.Attempt);

        // Records the Nell accepted-message receipt Arthur's widened prerequisite accepts on the
        // native presentation path, for the current Short Notice mission attempt.
        public void RecordNellAcceptedReceipt()
        {
            var correlation = Release1LogicalCorrelation.Create(
                Context.Snapshot.PlayerId, Release1MissionCatalog.ShortNotice, Mission().Attempt,
                Release1TransitionKind.MissionAccepted, "presentation-nell-sn-accepted-v1").Value;
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
            if (ThrowOnInvoke) throw new InvalidOperationException("synthetic Short Notice phone queue failure");
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

        public FakeWorld()
        {
            Context = new(SessionEpoch, 1, PlayerId, Path.GetTempPath());
            ResetAllDropsToEmpty();
        }

        public Release1StoryHostContextSnapshot Context { get; set; }
        public double TotalMinutes { get; set; }
        public IReadOnlyList<string> EmptyDropGuids { get; set; } = AllDropGuids;
        public IReadOnlyList<Release1SmallCourtesyProductCandidate> Products { get; set; } =
            new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true) };
        public string? SubscribedDropGuid { get; private set; }
        public bool SubscriptionDisposed { get; private set; }
        public float CashBalance { get; set; } = 500f;

        // Keyed by dead drop GUID so a read of one drop can never observe another drop's slots.
        // Every known drop starts with four genuine empty slots (quantity 0), the baseline a
        // freshly-loaded, never-touched world reads as; ClearDropSlots simulates the distinct
        // zero-length read of a drop that has not finished initializing.
        public Dictionary<string, Dictionary<int, Release1SmallCourtesySlotSnapshot>> DropSlots { get; } = new(StringComparer.Ordinal);
        public bool ThrowOnRead { get; set; }
        public List<string> MutationLog { get; } = new();
        public bool IsLocked { get; private set; }
        public bool ThrowOnChange { get; set; }
        public bool ThrowAfterLockOnce { get; set; }
        public bool ReturnAmbiguousAfterLockOnce { get; set; }
        public bool ThrowOnUnlockOnce { get; set; }
        public int UnlockFailuresRemaining { get; set; }
        public int QuantityChanges { get; private set; }
        public int CashChanges { get; private set; }
        public bool ThrowOnCashChange { get; set; }

        // Which of the two live readings TryChangeSlotQuantity simulates for the slot's post-mutation
        // MonetaryValue: PerUnit (the default) leaves it unchanged, since a per-unit reading is the
        // same number regardless of how many units remain; PerStack scales it down proportionally to
        // the remaining quantity, since a per-stack reading is the whole stack's total value.
        public Release1ShortNoticeValueConvention SlotValueSimulation { get; set; } = Release1ShortNoticeValueConvention.PerUnit;

        // One-shot overrides a test arms before a single TryChangeSlotQuantity call, to simulate a
        // post-mutation reading that disagrees with both the requested amount and the frozen
        // convention (rather than reconstructing a plausible-but-wrong native quirk by hand).
        public float? ForcedPostChangeMonetaryValue { get; set; }
        public int QuantityDriftOnNextChange { get; set; }

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context)
        {
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
            products = Products;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops)
        {
            drops = AllDropGuids.Select((guid, index) => new Release1SmallCourtesyDropCandidate(
                guid,
                $"Drop {(char)('A' + index)}",
                "A vanilla dead drop.",
                index + 1,
                index + 2,
                index + 3,
                EmptyDropGuids.Contains(guid, StringComparer.Ordinal))).ToArray();
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

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
            slots = Array.Empty<Release1SmallCourtesySlotSnapshot>();
            MutationLog.Add("read");
            if (ThrowOnRead) throw new InvalidOperationException("synthetic slot read failure");
            slots = DropOf(deadDropGuid).Values.OrderBy(slot => slot.SlotIndex).ToArray();
            return AllDropGuids.Contains(deadDropGuid, StringComparer.Ordinal)
                ? Release1SmallCourtesyWorldReadStatus.Ready
                : Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public void SetSlot(
            string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity,
            bool isPackaged = true, float? monetaryValue = null) =>
            DropOf(deadDropGuid)[slotIndex] = new(slotIndex, productId, packagingId, quantity, isPackaged, monetaryValue ?? quantity * 1_000f);

        public Release1SmallCourtesySlotSnapshot Slot(string deadDropGuid, int slotIndex) =>
            DropOf(deadDropGuid).TryGetValue(slotIndex, out var slot) ? slot : new(slotIndex, null, null, 0, false, 0f);

        public void EmptySlot(string deadDropGuid, int slotIndex) =>
            DropOf(deadDropGuid)[slotIndex] = new(slotIndex, null, null, 0, false, 0f);

        // Simulates a zero-length slot read (e.g. a native storage entity that has not finished
        // initializing) on an otherwise known, Ready-reporting drop.
        public void ClearDropSlots(string deadDropGuid) => DropSlots[deadDropGuid] = new();

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

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
        {
            room = Release1HoldRoomSnapshot.Unavailable();
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
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
            if (locked && ThrowAfterLockOnce)
            {
                IsLocked = true;
                ThrowAfterLockOnce = false;
                throw new InvalidOperationException("synthetic partial lock failure");
            }
            if (!locked && UnlockFailuresRemaining > 0)
            {
                UnlockFailuresRemaining--;
                throw new InvalidOperationException("synthetic repeated unlock failure");
            }
            if (!locked && ThrowOnUnlockOnce)
            {
                ThrowOnUnlockOnce = false;
                throw new InvalidOperationException("synthetic unlock failure");
            }
            IsLocked = locked;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount)
        {
            MutationLog.Add($"change:{slotIndex}:{amount}");
            if (ThrowOnChange) throw new InvalidOperationException("synthetic quantity failure");
            CapturePreSaveSnapshotIfNeeded();
            var drop = DropOf(deadDropGuid);
            var current = drop[slotIndex];
            var newQuantity = current.Quantity + amount + QuantityDriftOnNextChange;
            QuantityDriftOnNextChange = 0;
            float newValue;
            if (ForcedPostChangeMonetaryValue is float forced)
            {
                newValue = forced;
                ForcedPostChangeMonetaryValue = null;
            }
            else if (newQuantity <= 0)
            {
                // An emptied slot reads back at zero value under either convention: there is no
                // stack left to price, per unit or otherwise.
                newValue = 0f;
            }
            else if (SlotValueSimulation == Release1ShortNoticeValueConvention.PerStack && current.Quantity > 0)
            {
                newValue = current.MonetaryValue * newQuantity / current.Quantity;
            }
            else
            {
                newValue = current.MonetaryValue;
            }
            drop[slotIndex] = current with { Quantity = newQuantity, MonetaryValue = newValue };
            QuantityChanges++;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(
            string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
        {
            reason = "not used by Short Notice, which has no staging.";
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
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

        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            balance = CashBalance;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount)
        {
            if (ThrowOnCashChange) throw new InvalidOperationException("synthetic cash mutation failure");
            CapturePreSaveSnapshotIfNeeded();
            CashBalance += amount;
            CashChanges++;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        // Short Notice never debits the player wallet; this fake is not Chief-specific.
        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason) { reason = string.Empty; throw new NotSupportedException(); }
        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason) { reason = string.Empty; throw new NotSupportedException(); }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

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
            QuantityChanges = 0;
            CashChanges = 0;
        }

        public void RaiseClosed(string deadDropGuid) => _closed?.Invoke(deadDropGuid);

        // The world state a native save would durably capture. Populated lazily the first time a
        // world-mutating call (TryChangeSlotQuantity/TryChangeCashBalance) runs since the last
        // MarkSaved()/RevertToLastSave(), so RevertToLastSave() can undo exactly the mutations this
        // mod made since the last real save while leaving whatever the test fixture itself set up
        // (e.g. a manifest a test placed directly into a drop) untouched.
        private Dictionary<string, Dictionary<int, Release1SmallCourtesySlotSnapshot>>? _preSaveDropSlots;
        private float? _preSaveCashBalance;

        private void CapturePreSaveSnapshotIfNeeded()
        {
            if (_preSaveDropSlots is not null) return;
            _preSaveDropSlots = DropSlots.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<int, Release1SmallCourtesySlotSnapshot>(pair.Value));
            _preSaveCashBalance = CashBalance;
        }

        // Undoes every world mutation made since the last MarkSaved() (i.e. since the last real
        // native save), while leaving anything the test itself set up directly (SetSlot, etc.)
        // untouched, the same way quitting without saving discards this session's unsaved native
        // mutations but not what was already on disk.
        public void RevertToLastSave()
        {
            if (_preSaveDropSlots is not null)
            {
                DropSlots.Clear();
                foreach (var pair in _preSaveDropSlots)
                    DropSlots[pair.Key] = new Dictionary<int, Release1SmallCourtesySlotSnapshot>(pair.Value);
            }
            if (_preSaveCashBalance is not null) CashBalance = _preSaveCashBalance.Value;
            _preSaveDropSlots = null;
            _preSaveCashBalance = null;
        }

        // Commits the world mutations made since the last save, exactly as a real native save would;
        // RevertToLastSave() can no longer undo them.
        public void MarkSaved()
        {
            _preSaveDropSlots = null;
            _preSaveCashBalance = null;
        }

        public WorldSnapshot CaptureSnapshot() => new(
            DropSlots.ToDictionary(pair => pair.Key, pair => new Dictionary<int, Release1SmallCourtesySlotSnapshot>(pair.Value)),
            CashBalance);

        public void RestoreSnapshot(WorldSnapshot snapshot)
        {
            DropSlots.Clear();
            foreach (var pair in snapshot.DropSlots)
                DropSlots[pair.Key] = new Dictionary<int, Release1SmallCourtesySlotSnapshot>(pair.Value);
            CashBalance = snapshot.CashBalance;
        }

        public readonly record struct WorldSnapshot(
            Dictionary<string, Dictionary<int, Release1SmallCourtesySlotSnapshot>> DropSlots,
            float CashBalance);

        private sealed class FakeSubscription : IRelease1SmallCourtesyDropSubscription
        {
            private readonly Action _dispose;
            private bool _disposed;
            public FakeSubscription(string deadDropGuid, Action dispose) { DeadDropGuid = deadDropGuid; _dispose = dispose; }
            public string DeadDropGuid { get; }
            public void Dispose() { if (_disposed) return; _disposed = true; _dispose(); }
        }
    }

    internal sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(SessionEpoch, 1, PlayerId, Path.GetTempPath());
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
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
