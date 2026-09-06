using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1RoomWithNoNameMissionServiceTests
{
    [Fact]
    public void The_offer_appears_only_when_wrong_address_is_satisfied_and_the_room_is_offered()
    {
        using var blocked = Release1RoomWithNoNameHarness.WrongAddressStillActive();
        Assert.Equal(Release1RoomWithNoNameOfferStatus.Ineligible, blocked.Service.OfferStatus);

        using var ready = Release1RoomWithNoNameHarness.Offered();
        Assert.Equal(Release1RoomWithNoNameOfferStatus.Available, ready.Service.OfferStatus);
    }

    [Fact]
    public void Review_is_read_only_and_names_two_different_drops_and_the_hq_room()
    {
        using var harness = Release1RoomWithNoNameHarness.Offered();
        var revisionBefore = harness.Story.State!.Revision;

        var review = harness.Service.TryReview();

        Assert.Equal(Release1RoomWithNoNameReviewStatus.Ready, review.Status);
        Assert.NotNull(review.Quote);
        Assert.NotEqual(review.Quote!.Assignment.SourceDropGuid, review.Quote.Assignment.HandoffDropGuid);
        Assert.Equal("syndicate-hq", review.Quote.Assignment.HoldRoomKey);
        Assert.Equal(9, review.Quote.Assignment.ExpectedClosetCount);
        Assert.Equal(1_440d, review.Quote.Assignment.HoldDurationGameMinutes);
        Assert.Equal(72d, review.Quote.DeadlineDurationHours);
        Assert.Equal(revisionBefore, harness.Story.State.Revision);
        Assert.Empty(harness.Story.State.RoomWithNoNameAssignments);
        Assert.Equal(0, harness.World.InsertCount);
    }

    [Fact]
    public void Accept_without_review_is_refused_and_changes_nothing()
    {
        using var harness = Release1RoomWithNoNameHarness.Offered();

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1RoomWithNoNameDecisionStatus.ReviewRequired, decision.Status);
        Assert.Empty(harness.Story.State!.RoomWithNoNameAssignments);
    }

    [Fact]
    public void Accept_freezes_the_assignment_and_activates_in_one_pass()
    {
        using var harness = Release1RoomWithNoNameHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        var quote = harness.Service.TryReview().Quote!;

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1RoomWithNoNameDecisionStatus.Accepted, decision.Status);
        var assignment = Assert.Single(harness.Story.State!.RoomWithNoNameAssignments);
        Assert.Equal(quote.Assignment, assignment);
        var mission = harness.Mission();
        Assert.Equal(Release1MissionState.Active, mission.State);
        Assert.Equal("room-with-no-name-v1", mission.TermsVersion);
        Assert.Equal(100d, mission.AcceptedGameTimeHours);
        Assert.Equal(172d, mission.DeadlineGameTimeHours);
        Assert.True(harness.Progress()!.Staged);
    }

    [Fact]
    public void Accept_with_fewer_than_two_clear_drops_is_non_destructive()
    {
        using var harness = Release1RoomWithNoNameHarness.Offered();
        harness.Service.TryReview();
        harness.World.EmptyDropGuids = new[] { "drop-a" };

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1RoomWithNoNameDecisionStatus.InsufficientEmptyDeadDrops, decision.Status);
        Assert.Empty(harness.Story.State!.RoomWithNoNameAssignments);
        Assert.Equal(Release1MissionState.Offered, harness.Mission().State);
        Assert.Equal(0, harness.World.InsertCount);
    }

    [Fact]
    public void Review_with_fewer_than_two_clear_drops_reports_the_typed_refusal()
    {
        using var harness = Release1RoomWithNoNameHarness.Offered();
        harness.World.EmptyDropGuids = new[] { "drop-a" };

        var review = harness.Service.TryReview();

        Assert.Equal(Release1RoomWithNoNameReviewStatus.InsufficientEmptyDeadDrops, review.Status);
        Assert.Null(review.Quote);
        Assert.Equal(Release1RoomWithNoNameOfferStatus.InsufficientEmptyDeadDrops, harness.Service.OfferStatus);
    }

    [Fact]
    public void Not_now_defers_once_and_the_next_load_reoffers_with_one_attempt_increment()
    {
        var repository = new Release1RoomWithNoNameHarness.FakeRepository();
        var world = new Release1RoomWithNoNameHarness.FakeWorld();
        using (var first = Release1RoomWithNoNameHarness.Offered(repository, world))
        {
            first.Service.TryReview();
            Assert.Equal(Release1RoomWithNoNameDecisionStatus.Deferred, first.Service.TryDefer().Status);
            Assert.Equal(Release1MissionState.Deferred, first.Mission().State);
            Assert.Equal(1, first.Mission().Attempt);
            first.Service.OnPreLoad();
        }

        using var restored = Release1RoomWithNoNameHarness.Load(repository, world);
        Assert.Equal(Release1MissionState.Offered, restored.Mission().State);
        Assert.Equal(2, restored.Mission().Attempt);
        Assert.Equal(Release1RoomWithNoNameOfferStatus.Available, restored.Service.OfferStatus);
    }

    [Theory]
    [InlineData(171.99d, Release1MissionState.Active)]
    [InlineData(172d, Release1MissionState.MakeGoodOffered)]
    [InlineData(400d, Release1MissionState.MakeGoodOffered)]
    public void The_primary_deadline_boundary_is_exact(double gameHours, Release1MissionState expected)
    {
        using var harness = Release1RoomWithNoNameHarness.Active();
        harness.World.TotalMinutes = gameHours * 60d;

        harness.Service.Update();

        Assert.Equal(expected, harness.Mission().State);
    }

    [Fact]
    public void A_missed_primary_deadline_applies_minus_twelve_exactly_once()
    {
        using var harness = Release1RoomWithNoNameHarness.Active();
        var standingBefore = harness.Story.State!.Standing;
        harness.World.TotalMinutes = 400d * 60d;

        harness.Service.Update();
        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(standingBefore - 12, harness.Story.State!.Standing);
        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        Assert.Single(harness.Mission().PenaltyReceiptIds);
    }

    [Fact]
    public void A_missed_make_good_deadline_applies_minus_eight_and_exposes_recovery()
    {
        using var harness = Release1RoomWithNoNameHarness.MakeGoodActive();
        var standingBefore = harness.Story.State!.Standing;
        harness.World.TotalMinutes = 900d * 60d;

        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(standingBefore - 8, harness.Story.State!.Standing);
        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);
    }

    [Fact]
    public void Make_good_uses_a_jar_of_the_same_product_at_the_next_attempt()
    {
        using var harness = Release1RoomWithNoNameHarness.MakeGoodOffered();
        var primary = harness.Story.State!.RoomWithNoNameAssignments.Single(a => a.Attempt == 1);

        var quote = harness.Service.TryReview().Quote!;

        Assert.Equal(Release1RoomWithNoNameAssignmentMode.MakeGood, quote.Assignment.Mode);
        Assert.Equal(primary.ProductId, quote.Assignment.ProductId);
        Assert.Equal("jar", quote.Assignment.PackagingId);
        Assert.Equal(2, quote.Assignment.Attempt);
        Assert.Equal(72d, quote.DeadlineDurationHours);
    }

    [Fact]
    public void Recovery_has_no_deadline_and_never_lapses()
    {
        using var harness = Release1RoomWithNoNameHarness.RecoveryActive();
        harness.World.TotalMinutes = 5_000d * 60d;

        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(Release1MissionState.RecoveryActive, harness.Mission().State);
        Assert.Null(harness.Mission().DeadlineGameTimeHours);
    }

    [Fact]
    public void An_inactive_or_mismatched_world_context_makes_every_decision_inert()
    {
        using var harness = Release1RoomWithNoNameHarness.Offered();
        harness.World.Context = harness.World.Context with { LoadEpoch = harness.World.Context.LoadEpoch + 1 };

        Assert.Equal(Release1RoomWithNoNameReviewStatus.Inactive, harness.Service.TryReview().Status);
        Assert.Equal(Release1RoomWithNoNameDecisionStatus.Inactive, harness.Service.TryDefer().Status);
        Assert.Empty(harness.Story.State!.RoomWithNoNameAssignments);
    }
}

internal static class Release1RoomWithNoNameHarness
{
    private const string PlayerId = "76561190000000001";
    private static readonly Guid SessionEpoch = Guid.Parse("77777777-7777-7777-7777-777777777777");

    // The same two HQ_PlayerLocker_A/B closet GUIDs SyndicateHqStorageContract.Definitions carries,
    // spelled out here so hold tests can name a specific closet without reaching into that contract.
    internal const string ClosetA = "8ec9d63b-f0f7-4af9-86fb-f1c73c7af481";
    internal const string ClosetB = "dcf4ca2a-4d27-47c4-a10a-2819ea298fe3";

    internal static Harness WrongAddressStillActive()
    {
        var repository = new FakeRepository();
        var context = new FakeContext();
        var story = SeededStory(context, repository, satisfyWrongAddress: false);
        var world = new FakeWorld();
        var logs = new List<string>();
        var service = new Release1RoomWithNoNameMissionService(story, world, log: logs.Add);
        service.OnLoadComplete();
        return new(story, service, world, context, repository, logs, phone: null, queue: new FakeQueue());
    }

    internal static Harness Offered(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        repository ??= new FakeRepository();
        var context = new FakeContext();
        var story = SeededStory(context, repository, satisfyWrongAddress: true);
        world ??= new FakeWorld();
        var logs = new List<string>();
        var queue = new FakeQueue();
        var phone = withPhone ? new Release1PhoneCallService(story, queue, new FakeCue(), log: logs.Add) : null;
        var service = new Release1RoomWithNoNameMissionService(story, world, phone, log: logs.Add);
        service.OnLoadComplete();
        repository.ResetEvidence();
        world.ResetMutationEvidence();
        return new(story, service, world, context, repository, logs, phone, queue);
    }

    // clearSlots resets every drop back to genuine empty slots before the caller's own convergence
    // runs. Default true preserves the pre-existing self-healing-baseline behaviour used by most
    // reload tests. Pass false when the test needs to observe a reload against whatever the world
    // was actually left holding, matching Wrong Address's reload matrix precedent.
    internal static Harness Load(FakeRepository repository, FakeWorld world, bool clearSlots = true)
    {
        var context = new FakeContext();
        var story = NewStory(context, repository);
        var logs = new List<string>();
        var service = new Release1RoomWithNoNameMissionService(story, world, log: logs.Add);
        var worldBeforeConvergence = world.CaptureSnapshot();
        service.OnLoadComplete();

        // OnLoadComplete converges once and, while a stage is active, may immediately re-stage the
        // consignment if the world already reads as ready for it (the point of self-healing
        // convergence). Reload the story only, not the service, so any subscription it just opened
        // survives, and (when clearSlots is true) undo whatever the world-mutating side of that
        // convergence just did by restoring the exact snapshot taken immediately before it ran; that
        // hands callers the freshly-reloaded, pre-convergence baseline so they can exercise their own
        // explicit convergence and observe it, the same way Active() does after TryAccept().
        if (clearSlots) world.RestoreSnapshot(worldBeforeConvergence);
        story.OnPreLoad();
        story.OnLoadComplete();
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

    internal static Harness Active(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = Offered(repository, world, withPhone);
        harness.World.TotalMinutes = 6_000d;
        Assert.Equal(Release1RoomWithNoNameReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1RoomWithNoNameDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);

        // TryAccept() converges once as part of acceptance: it stages the consignment into the (then
        // empty) source drop and binds the drop-closed subscription. Reload the story only, not the
        // mission service, so the subscription it already opened survives; the reload discards the
        // unsaved Staged flag from that convergence, and resetting the world's drops back to empty
        // slots undoes its insert, leaving every staging test a genuinely empty, not-yet-staged drop
        // to start from.
        harness.World.ResetAllDropsToEmpty();
        harness.Story.OnPreLoad();
        harness.Story.OnLoadComplete();

        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    internal static Harness MakeGoodOffered(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = Active(repository, world, withPhone);
        harness.World.TotalMinutes = 400d * 60d;
        harness.Service.Update();
        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    internal static Harness MakeGoodActive(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = MakeGoodOffered(repository, world, withPhone);
        Assert.Equal(Release1RoomWithNoNameReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1RoomWithNoNameDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Mission().State);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    internal static Harness RecoveryAvailable(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = MakeGoodActive(repository, world, withPhone);
        harness.World.TotalMinutes = 900d * 60d;
        harness.Service.Update();
        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    internal static Harness RecoveryActive(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = RecoveryAvailable(repository, world, withPhone);
        Assert.Equal(Release1RoomWithNoNameReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1RoomWithNoNameDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.RecoveryActive, harness.Mission().State);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Drives an accepted stage all the way to Custody (the consignment staged, then physically taken
    // from the source drop) and durably saves that baseline, leaving the hold room untouched for the
    // caller's own hold fixture to fill.
    internal static Harness InCustody(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = Active(repository, world, withPhone);
        Assert.Equal(Release1RoomWithNoNameStageStatus.Staged, harness.Service.ReconcileStaging());
        harness.World.EmptySlot(harness.Assignment.SourceDropGuid, 0);
        Assert.Equal(Release1RoomWithNoNameStageStatus.CustodyInferred, harness.Service.ReconcileStaging());
        Assert.True(harness.Progress()!.Custody);
        Save(harness);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Drives a saved InCustody baseline one step further: the consignment is physically moved into
    // ClosetA and observed by exactly one hold pass, recording the stow. The stow itself is left
    // unsaved (only the InCustody baseline underneath it was saved), so an unsaved-quit test can
    // revert just the stow and prove the window restarts on the next one.
    internal static Harness Stowed(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = InCustody(repository, world, withPhone);
        harness.World.StowIntoCloset(harness.Assignment, ClosetA);
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Stowed, harness.Service.ReconcileHold());
        Assert.True(harness.Progress()!.Stowed);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Same shape as Stowed(), but for the make-good attempt (a jar, staged and taken into custody
    // through MakeGoodActive()) rather than the primary brick. TryAccept() already converged once and
    // staged the jar; reset the drop back to empty and reload the story only (same trick Active() uses)
    // so the explicit ReconcileStaging() call below observes a genuine first stage, not a re-observe.
    internal static Harness MakeGoodStowed(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = MakeGoodActive(repository, world, withPhone);
        harness.World.ResetAllDropsToEmpty();
        harness.Story.OnPreLoad();
        harness.Story.OnLoadComplete();
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        Assert.Equal(Release1RoomWithNoNameStageStatus.Staged, harness.Service.ReconcileStaging());
        harness.World.EmptySlot(harness.Assignment.SourceDropGuid, 0);
        Assert.Equal(Release1RoomWithNoNameStageStatus.CustodyInferred, harness.Service.ReconcileStaging());
        Assert.True(harness.Progress()!.Custody);
        Save(harness);
        harness.World.StowIntoCloset(harness.Assignment, ClosetA);
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Stowed, harness.Service.ReconcileHold());
        Assert.True(harness.Progress()!.Stowed);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Same shape as MakeGoodStowed(), but for the recovery attempt through RecoveryActive().
    internal static Harness RecoveryStowed(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = RecoveryActive(repository, world, withPhone);
        harness.World.ResetAllDropsToEmpty();
        harness.Story.OnPreLoad();
        harness.Story.OnLoadComplete();
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        Assert.Equal(Release1RoomWithNoNameStageStatus.Staged, harness.Service.ReconcileStaging());
        harness.World.EmptySlot(harness.Assignment.SourceDropGuid, 0);
        Assert.Equal(Release1RoomWithNoNameStageStatus.CustodyInferred, harness.Service.ReconcileStaging());
        Assert.True(harness.Progress()!.Custody);
        Save(harness);
        harness.World.StowIntoCloset(harness.Assignment, ClosetA);
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Stowed, harness.Service.ReconcileHold());
        Assert.True(harness.Progress()!.Stowed);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Advances a Stowed() baseline past the 1440-minute hold window and runs one hold pass, leaving
    // HoldSatisfied true, for tests that exercise "after the release" behavior. Saved durably before
    // returning, the same way InCustody() is: HoldSatisfied is the checkpoint delivery is gated on, so
    // it has to survive a reload the same way Custody does, leaving only the delivery itself (run by
    // the caller afterwards) as the unsaved half a revert-without-saving test can undo.
    internal static Harness Released(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = Stowed(repository, world, withPhone);
        harness.World.TotalMinutes += Release1RoomWithNoNameAssignment.OneInGameDayMinutes;
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Released, harness.Service.ReconcileHold());
        Assert.True(harness.Progress()!.HoldSatisfied);
        Save(harness);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Same shape as Released(), but for the make-good attempt (MakeGoodStowed()), for tests that
    // exercise a late completion still paying once through the same delivery transaction.
    internal static Harness MakeGoodReleased(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = MakeGoodStowed(repository, world, withPhone);
        harness.World.TotalMinutes += Release1RoomWithNoNameAssignment.OneInGameDayMinutes;
        Assert.Equal(Release1RoomWithNoNameHoldStatus.Released, harness.Service.ReconcileHold());
        Assert.True(harness.Progress()!.HoldSatisfied);
        Save(harness);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Drives a required-failure primary attempt through to the still-unaccepted make-good offer,
    // with the Nell accepted-message receipt already recorded (unless suppressed) so a single
    // subsequent Update() can queue Arthur's warning call without any further fixture setup.
    internal static Harness RequiredFailed(bool withPhone = false, bool recordNellReceipt = true)
    {
        var harness = MakeGoodOffered(withPhone: withPhone);
        if (recordNellReceipt) harness.RecordNellAcceptedReceipt();
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Drives a primary attempt through delivery to a durably persisted Satisfied/OnTime completion
    // with both the consumption and reward native effects fully Committed, leaving a single
    // subsequent Update() to observe no Arthur call (Arthur is failure-path only for this mission).
    internal static Harness CleanlyCompleted(bool withPhone = false, bool recordNellReceipt = true)
    {
        var harness = Released(withPhone: withPhone);
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.Service.ReconcileDelivery();
        Save(harness);
        if (recordNellReceipt) harness.RecordNellAcceptedReceipt();
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Seeds a story whose Small Courtesy mission (and, when requested, Wrong Address too) is Satisfied
    // through the same pure Release1StoryTransitions path Task 3's Satisfy helper uses, then loads it
    // into a fresh runtime service backed by the given repository.
    private static Release1StoryRuntimeService SeededStory(FakeContext context, FakeRepository repository, bool satisfyWrongAddress)
    {
        var state = Release1StoryState.CreateAccepted(context.Snapshot.PlayerId, Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-room").Value);
        state = Satisfy(state, Release1MissionCatalog.SmallCourtesy);
        if (satisfyWrongAddress) state = Satisfy(state, Release1MissionCatalog.WrongAddress);
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
            Release1RoomWithNoNameMissionService service,
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
        public Release1RoomWithNoNameMissionService Service { get; }
        public FakeWorld World { get; }
        public FakeContext Context { get; }
        public FakeRepository Repository { get; }
        public List<string> Logs { get; }
        public Release1PhoneCallService? Phone { get; }
        public FakeQueue Queue { get; }
        public Release1RoomWithNoNameAssignment Assignment => Story.State!.RoomWithNoNameAssignments.MaxBy(value => value.Attempt)!;

        public Release1MissionRecord Mission() =>
            Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)];

        public Release1RoomWithNoNameProgress? Progress() =>
            Story.State?.RoomWithNoNameProgress.SingleOrDefault(progress => progress.Attempt == Assignment.Attempt);

        public Release1NativeEffectJournalEntry Consumption() => Story.State!.NativeEffects.Single(effect =>
            effect.MissionKey == Release1MissionCatalog.RoomWithNoName &&
            effect.EffectKind == "CargoTransfer" &&
            effect.Attempt == Assignment.Attempt);

        public Release1NativeEffectJournalEntry Reward() => Story.State!.NativeEffects.Single(effect =>
            effect.MissionKey == Release1MissionCatalog.RoomWithNoName &&
            effect.EffectKind == "Reward" &&
            effect.Attempt == Assignment.Attempt);

        // Records the Nell accepted-message receipt Arthur's widened prerequisite accepts on the
        // native presentation path, for the current Room With No Name mission attempt.
        public void RecordNellAcceptedReceipt()
        {
            var correlation = Release1LogicalCorrelation.Create(
                Context.Snapshot.PlayerId, Release1MissionCatalog.RoomWithNoName, Mission().Attempt,
                Release1TransitionKind.MissionAccepted, "presentation-nell-rn-accepted-v1").Value;
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
            if (ThrowOnInvoke) throw new InvalidOperationException("synthetic Room With No Name phone queue failure");
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
        private static readonly string[] AllClosetGuids = SyndicateHqStorageContract.Definitions
            .Select(definition => definition.Guid.ToString("D"))
            .ToArray();

        public FakeWorld()
        {
            Context = new(SessionEpoch, 1, PlayerId, Path.GetTempPath());
            ResetAllDropsToEmpty();
            ResetAllClosetsToEmpty();
        }

        public Release1StoryHostContextSnapshot Context { get; set; }
        public double TotalMinutes { get; set; }
        public IReadOnlyList<string> EmptyDropGuids { get; set; } = AllDropGuids;
        public Release1HoldRoomReadiness HoldRoomReadiness { get; set; } = Release1HoldRoomReadiness.Ready;

        // Keyed by dead drop GUID so a read of one drop can never observe another drop's slots.
        public Dictionary<string, Dictionary<int, Release1SmallCourtesySlotSnapshot>> DropSlots { get; } = new(StringComparer.Ordinal);

        // Keyed by the nine SyndicateHqStorageContract.Definitions GUIDs in "D" form. Task 4 never
        // reads or mutates this; it exists so Task 5's hold check and Task 6's delivery can reuse this
        // same harness without touching this file again.
        public Dictionary<string, Dictionary<int, Release1SmallCourtesySlotSnapshot>> ClosetSlots { get; } = new(StringComparer.Ordinal);

        public List<string> MutationLog { get; } = new();
        public string? SubscribedDropGuid { get; private set; }
        public bool SubscriptionDisposed { get; private set; }
        public bool IsLocked { get; private set; }
        public bool ThrowOnChange { get; set; }
        public bool ThrowAfterLockOnce { get; set; }
        public bool ReturnAmbiguousAfterLockOnce { get; set; }
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnReadAfterMutation { get; set; }
        public bool ThrowOnUnlockOnce { get; set; }
        public int UnlockFailuresRemaining { get; set; }
        public int QuantityChanges { get; private set; }
        public int InsertCount { get; private set; }
        public float CashBalance { get; set; } = 500f;
        public int CashChanges { get; private set; }
        public bool ThrowOnCashChange { get; set; }
        public bool ThrowOnCashReadAfterMutation { get; set; }
        public bool RejectNextInsert { get; set; }
        public bool ThrowOnInsert { get; set; }
        public bool CorruptNextInsertReadback { get; set; }
        public string? LastStagedDropGuid { get; private set; }

        // Counts every write to a closet's contents, whether made by production code (a future
        // TryStowIntoCloset-style method, which does not exist yet) or by the StowIntoCloset/
        // EmptyCloset fixture pokes below, which increment it precisely because they write closet
        // contents the same way a native drag-and-drop would. This lets
        // Delivery_never_touches_a_closet prove the invariant it names instead of trivially passing
        // on a counter nothing ever increments. Raw fixture setup (ResetAllClosetsToEmpty) does not
        // count, the same way ResetAllDropsToEmpty never touches QuantityChanges.
        public int ClosetMutations { get; private set; }

        // Counts production-code closet subscriptions. No such method exists on
        // IRelease1SmallCourtesyWorld yet, so this stays zero through Task 4; OC never subscribes to a
        // closet at all, per the plan.
        public int ClosetSubscriptions { get; private set; }

        // Simulates an unrelated, out-of-band cash change (not one of ours) that lands between the
        // baseline read taken right after consumption and the verification read a delivery pass makes
        // before attempting to pay. Zero means no drift; set once before the delivery pass under test.
        public float DriftCashAfterConsumption { get; set; }
        private bool _cashDriftArmed;
        private bool _cashDriftSkippedOnce;

        // The world state a native save would durably capture. Populated lazily the first time a
        // world-mutating call (TryChangeSlotQuantity/TryInsertPackagedProduct/TryChangeCashBalance) or
        // a closet poke that models one (StowIntoCloset/EmptyCloset) runs since the last
        // MarkSaved()/RevertToLastSave(), so RevertToLastSave() can undo exactly the mutations this
        // mod, or the player moving physical items around, made since the last real save while
        // leaving whatever the test fixture itself set up before that point (e.g. a package a test
        // placed directly into a drop) untouched.
        private Dictionary<string, Dictionary<int, Release1SmallCourtesySlotSnapshot>>? _preSaveDropSlots;
        private Dictionary<string, Dictionary<int, Release1SmallCourtesySlotSnapshot>>? _preSaveClosetSlots;
        private float? _preSaveCashBalance;

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
            products = new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true) };
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
            MutationLog.Add("read");
            if (ThrowOnRead)
                throw new InvalidOperationException("synthetic slot read failure");
            if (ThrowOnReadAfterMutation && QuantityChanges > 0)
                throw new InvalidOperationException("synthetic post-mutation read failure");
            slots = DropOf(deadDropGuid).Values.OrderBy(slot => slot.SlotIndex).ToArray();
            return AllDropGuids.Contains(deadDropGuid, StringComparer.Ordinal)
                ? Release1SmallCourtesyWorldReadStatus.Ready
                : Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
        {
            if (HoldRoomReadiness != Release1HoldRoomReadiness.Ready)
            {
                room = HoldRoomReadiness == Release1HoldRoomReadiness.NotReady
                    ? Release1HoldRoomSnapshot.NotReady()
                    : Release1HoldRoomSnapshot.Unavailable();
                return Release1SmallCourtesyWorldReadStatus.Ready;
            }
            var closets = ClosetSlots
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new Release1HoldRoomClosetSnapshot(
                    pair.Key, pair.Value.Count, pair.Value.Values.OrderBy(slot => slot.SlotIndex).ToArray()))
                .ToArray();
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
            drop[slotIndex] = current with { Quantity = current.Quantity + amount };
            QuantityChanges++;
            if (DriftCashAfterConsumption != 0f) { _cashDriftArmed = true; _cashDriftSkippedOnce = false; }
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(
            string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
        {
            MutationLog.Add($"insert:{slotIndex}:{productId}:{packagingId}:{quantity}");
            if (ThrowOnInsert)
            {
                ThrowOnInsert = false;
                throw new InvalidOperationException("synthetic insert failure");
            }
            if (RejectNextInsert)
            {
                RejectNextInsert = false;
                reason = "synthetic insertion refusal.";
                return Release1SmallCourtesyWorldMutationStatus.Rejected;
            }
            CapturePreSaveSnapshotIfNeeded();
            var drop = DropOf(deadDropGuid);
            if (drop.TryGetValue(slotIndex, out var existing) && existing.Quantity != 0)
            {
                reason = "the slot was not empty before staging.";
                return Release1SmallCourtesyWorldMutationStatus.Rejected;
            }
            if (CorruptNextInsertReadback)
            {
                CorruptNextInsertReadback = false;
                drop[slotIndex] = new(slotIndex, productId, packagingId, quantity + 1, true, (quantity + 1) * 1_000f);
            }
            else
            {
                drop[slotIndex] = new(slotIndex, productId, packagingId, quantity, true, quantity * 1_000f);
            }
            InsertCount++;
            LastStagedDropGuid = deadDropGuid;
            reason = "the packaged consignment was staged and read back with the exact expected values.";
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesySlotSnapshot Slot(string deadDropGuid, int slotIndex) =>
            DropOf(deadDropGuid).TryGetValue(slotIndex, out var slot) ? slot : new(slotIndex, null, null, 0, false, 0f);

        public void PlaceExactPackage(Release1RoomWithNoNameAssignment assignment) =>
            DropOf(assignment.SourceDropGuid)[0] = new(0, assignment.ProductId, assignment.PackagingId, assignment.PackageQuantity, true, assignment.PackageQuantity * 1_000f);

        public void PlaceExactPackage(Release1RoomWithNoNameAssignment assignment, string deadDropGuid, int slotIndex, float value) =>
            DropOf(deadDropGuid)[slotIndex] = new(slotIndex, assignment.ProductId, assignment.PackagingId, assignment.PackageQuantity, true, value);

        public void EmptySlot(string deadDropGuid, int slotIndex) =>
            DropOf(deadDropGuid)[slotIndex] = new(slotIndex, null, null, 0, false, 0f);

        public void SetSlot(string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, bool isPackaged) =>
            DropOf(deadDropGuid)[slotIndex] = new(slotIndex, productId, packagingId, quantity, isPackaged, quantity * 1_000f);

        // Simulates a zero-length slot read (e.g. a native storage entity that has not finished
        // initializing) on an otherwise known, Ready-reporting drop.
        public void ClearDropSlots(string deadDropGuid) => DropSlots[deadDropGuid] = new();

        // Resets every known drop to genuine empty slot entries (indices 0-3, quantity 0), the
        // baseline a freshly-loaded, never-staged world reads as. Used to give reload tests a
        // clean starting point without collapsing a drop to a zero-length read.
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

        // Resets every one of the nine HQ closets to twenty genuine empty slots. Test-fixture setup
        // only; Task 4 production code never reads or writes a closet.
        public void ResetAllClosetsToEmpty()
        {
            foreach (var guid in AllClosetGuids)
            {
                var closet = new Dictionary<int, Release1SmallCourtesySlotSnapshot>();
                for (var index = 0; index < SyndicateHqStorageContract.SlotCount; index++)
                    closet[index] = new(index, null, null, 0, false, 0f);
                ClosetSlots[guid] = closet;
            }
        }

        // Places the assignment's consignment into the first empty slot of the named closet. Counts
        // against ClosetMutations, the same way a real TryStowIntoCloset-style production write
        // would, so a test can prove delivery never reaches a closet. It does capture the pre-save
        // snapshot first, the same way a real native drag into a closet would be an unsaved mutation
        // RevertToLastSave() can undo.
        public void StowIntoCloset(Release1RoomWithNoNameAssignment assignment, string closetGuid)
        {
            var closet = ClosetOf(closetGuid);
            var index = closet.Where(pair => pair.Value.Quantity == 0).Select(pair => pair.Key).DefaultIfEmpty(-1).Min();
            if (index < 0) throw new InvalidOperationException("The closet has no empty slot to stow into.");
            CapturePreSaveSnapshotIfNeeded();
            closet[index] = new(index, assignment.ProductId, assignment.PackagingId, assignment.PackageQuantity, true, assignment.PackageQuantity * 1_000f);
            ClosetMutations++;
        }

        // Empties every slot in the named closet. Counts against ClosetMutations; see StowIntoCloset.
        public void EmptyCloset(string closetGuid)
        {
            var closet = ClosetOf(closetGuid);
            CapturePreSaveSnapshotIfNeeded();
            foreach (var index in closet.Keys.ToArray())
                closet[index] = new(index, null, null, 0, false, 0f);
            ClosetMutations++;
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

        private Dictionary<int, Release1SmallCourtesySlotSnapshot> ClosetOf(string closetGuid)
        {
            if (!ClosetSlots.TryGetValue(closetGuid, out var closet))
            {
                closet = new Dictionary<int, Release1SmallCourtesySlotSnapshot>();
                ClosetSlots[closetGuid] = closet;
            }
            return closet;
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
                if (string.Equals(SubscribedDropGuid, deadDropGuid, StringComparison.Ordinal))
                    SubscribedDropGuid = null;
            });
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            if (ThrowOnCashReadAfterMutation && CashChanges > 0)
                throw new InvalidOperationException("synthetic post-payment read failure");
            if (_cashDriftArmed)
            {
                if (!_cashDriftSkippedOnce) _cashDriftSkippedOnce = true;
                else { CashBalance += DriftCashAfterConsumption; _cashDriftArmed = false; _cashDriftSkippedOnce = false; }
            }
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

        // A Room With No Name never debits the player wallet; this fake is not Chief-specific.
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
            InsertCount = 0;
            CashChanges = 0;
            ClosetMutations = 0;
        }

        private void CapturePreSaveSnapshotIfNeeded()
        {
            if (_preSaveDropSlots is not null) return;
            _preSaveDropSlots = DropSlots.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<int, Release1SmallCourtesySlotSnapshot>(pair.Value));
            _preSaveClosetSlots = ClosetSlots.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<int, Release1SmallCourtesySlotSnapshot>(pair.Value));
            _preSaveCashBalance = CashBalance;
        }

        // Undoes every world mutation made since the last MarkSaved() (i.e. since the last real
        // native save), while leaving anything the test itself set up directly before that point
        // (PlaceExactPackage, SetSlot, etc.) untouched, the same way quitting without saving discards
        // this session's unsaved native mutations but not what was already on disk. Closets revert the
        // same way drops do: whatever the player physically stowed since the last save disappears too.
        public void RevertToLastSave()
        {
            if (_preSaveDropSlots is not null)
            {
                DropSlots.Clear();
                foreach (var pair in _preSaveDropSlots)
                    DropSlots[pair.Key] = new Dictionary<int, Release1SmallCourtesySlotSnapshot>(pair.Value);
            }
            if (_preSaveClosetSlots is not null)
            {
                ClosetSlots.Clear();
                foreach (var pair in _preSaveClosetSlots)
                    ClosetSlots[pair.Key] = new Dictionary<int, Release1SmallCourtesySlotSnapshot>(pair.Value);
            }
            if (_preSaveCashBalance is not null) CashBalance = _preSaveCashBalance.Value;
            _preSaveDropSlots = null;
            _preSaveClosetSlots = null;
            _preSaveCashBalance = null;
            _cashDriftArmed = false;
            _cashDriftSkippedOnce = false;
        }

        // Commits the world mutations made since the last save, exactly as a real native save would;
        // RevertToLastSave() can no longer undo them.
        public void MarkSaved()
        {
            _preSaveDropSlots = null;
            _preSaveClosetSlots = null;
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

        public void RaiseClosed(string deadDropGuid) => _closed?.Invoke(deadDropGuid);

        public bool TryPlayerSetQuantity(string deadDropGuid, int slotIndex, int quantity)
        {
            if (IsLocked) return false;
            var drop = DropOf(deadDropGuid);
            var current = drop[slotIndex];
            drop[slotIndex] = current with { Quantity = quantity };
            return true;
        }

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
