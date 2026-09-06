using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1WrongAddressMissionServiceTests
{
    [Fact]
    public void The_offer_appears_only_when_small_courtesy_is_satisfied_and_wrong_address_is_offered()
    {
        using var blocked = Release1WrongAddressHarness.SmallCourtesyStillActive();
        Assert.Equal(Release1WrongAddressOfferStatus.Ineligible, blocked.Service.OfferStatus);

        using var ready = Release1WrongAddressHarness.Offered();
        Assert.Equal(Release1WrongAddressOfferStatus.Available, ready.Service.OfferStatus);
    }

    [Fact]
    public void Review_is_read_only_and_names_two_different_drops()
    {
        using var harness = Release1WrongAddressHarness.Offered();
        var revisionBefore = harness.Story.State!.Revision;

        var review = harness.Service.TryReview();

        Assert.Equal(Release1WrongAddressReviewStatus.Ready, review.Status);
        Assert.NotNull(review.Quote);
        Assert.NotEqual(review.Quote!.Assignment.SourceDropGuid, review.Quote.Assignment.HandoffDropGuid);
        Assert.Equal(24d, review.Quote.DeadlineDurationHours);
        Assert.Equal(revisionBefore, harness.Story.State.Revision);
        Assert.Empty(harness.Story.State.WrongAddressAssignments);
        Assert.Equal(0, harness.World.InsertCount);
    }

    [Fact]
    public void Accept_without_review_is_refused_and_changes_nothing()
    {
        using var harness = Release1WrongAddressHarness.Offered();

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1WrongAddressDecisionStatus.ReviewRequired, decision.Status);
        Assert.Empty(harness.Story.State!.WrongAddressAssignments);
    }

    [Fact]
    public void Accept_freezes_the_assignment_and_activates_in_one_pass()
    {
        using var harness = Release1WrongAddressHarness.Offered();
        harness.World.TotalMinutes = 6_000d;
        var quote = harness.Service.TryReview().Quote!;

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1WrongAddressDecisionStatus.Accepted, decision.Status);
        var assignment = Assert.Single(harness.Story.State!.WrongAddressAssignments);
        Assert.Equal(quote.Assignment, assignment);
        var mission = harness.Mission();
        Assert.Equal(Release1MissionState.Active, mission.State);
        Assert.Equal("wrong-address-v1", mission.TermsVersion);
        Assert.Equal(100d, mission.AcceptedGameTimeHours);
        Assert.Equal(124d, mission.DeadlineGameTimeHours);
        // Acceptance and activation are durably persisted together; the one revision still ahead of
        // LastPersistedRevision is the in-memory-only Staged flag TryAccept's own convergence pass
        // just recorded, which rides the next native save rather than persisting immediately.
        Assert.Equal(harness.Story.State.Revision - 1, harness.Story.LastPersistedRevision);
        Assert.True(harness.Progress()!.Staged);
    }

    [Fact]
    public void Accept_with_fewer_than_two_clear_drops_is_non_destructive()
    {
        using var harness = Release1WrongAddressHarness.Offered();
        harness.Service.TryReview();
        harness.World.EmptyDropGuids = new[] { "drop-a" };

        var decision = harness.Service.TryAccept();

        Assert.Equal(Release1WrongAddressDecisionStatus.InsufficientEmptyDeadDrops, decision.Status);
        Assert.Empty(harness.Story.State!.WrongAddressAssignments);
        Assert.Equal(Release1MissionState.Offered, harness.Mission().State);
        Assert.Equal(0, harness.World.InsertCount);
    }

    [Fact]
    public void Review_with_fewer_than_two_clear_drops_reports_the_typed_refusal()
    {
        using var harness = Release1WrongAddressHarness.Offered();
        harness.World.EmptyDropGuids = new[] { "drop-a" };

        var review = harness.Service.TryReview();

        Assert.Equal(Release1WrongAddressReviewStatus.InsufficientEmptyDeadDrops, review.Status);
        Assert.Null(review.Quote);
        Assert.Equal(Release1WrongAddressOfferStatus.InsufficientEmptyDeadDrops, harness.Service.OfferStatus);
    }

    [Fact]
    public void Not_now_defers_once_and_the_next_load_reoffers_with_one_attempt_increment()
    {
        var repository = new Release1WrongAddressHarness.FakeRepository();
        var world = new Release1WrongAddressHarness.FakeWorld();
        using (var first = Release1WrongAddressHarness.Offered(repository, world))
        {
            first.Service.TryReview();
            Assert.Equal(Release1WrongAddressDecisionStatus.Deferred, first.Service.TryDefer().Status);
            Assert.Equal(Release1MissionState.Deferred, first.Mission().State);
            Assert.Equal(1, first.Mission().Attempt);
            first.Service.OnPreLoad();
        }

        using var restored = Release1WrongAddressHarness.Load(repository, world);
        Assert.Equal(Release1MissionState.Offered, restored.Mission().State);
        Assert.Equal(2, restored.Mission().Attempt);
        Assert.Equal(Release1WrongAddressOfferStatus.Available, restored.Service.OfferStatus);
    }

    [Fact]
    public void A_reoffered_attempt_quotes_a_new_authorization_correlation()
    {
        var repository = new Release1WrongAddressHarness.FakeRepository();
        var world = new Release1WrongAddressHarness.FakeWorld();
        Release1WrongAddressAssignment firstQuote;
        using (var first = Release1WrongAddressHarness.Offered(repository, world))
        {
            firstQuote = first.Service.TryReview().Quote!.Assignment;
            first.Service.TryDefer();
            first.Service.OnPreLoad();
        }

        using var restored = Release1WrongAddressHarness.Load(repository, world);
        var secondQuote = restored.Service.TryReview().Quote!.Assignment;

        Assert.Equal(2, secondQuote.Attempt);
        Assert.NotEqual(firstQuote.AuthorizationCorrelationId, secondQuote.AuthorizationCorrelationId);
    }

    [Theory]
    [InlineData(123.99d, Release1MissionState.Active)]
    [InlineData(124d, Release1MissionState.MakeGoodOffered)]
    [InlineData(200d, Release1MissionState.MakeGoodOffered)]
    public void The_primary_deadline_boundary_is_exact(double gameHours, Release1MissionState expected)
    {
        using var harness = Release1WrongAddressHarness.Active();
        harness.World.TotalMinutes = gameHours * 60d;

        harness.Service.Update();

        Assert.Equal(expected, harness.Mission().State);
    }

    [Fact]
    public void A_missed_primary_deadline_applies_minus_twelve_exactly_once()
    {
        using var harness = Release1WrongAddressHarness.Active();
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
    public void A_missed_make_good_deadline_applies_minus_eight_and_exposes_recovery()
    {
        using var harness = Release1WrongAddressHarness.MakeGoodActive();
        var standingBefore = harness.Story.State!.Standing;
        harness.World.TotalMinutes = 400d * 60d;

        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(standingBefore - 8, harness.Story.State!.Standing);
        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);
    }

    [Fact]
    public void Make_good_uses_a_jar_of_the_same_product_at_the_next_attempt()
    {
        using var harness = Release1WrongAddressHarness.MakeGoodOffered();
        var primary = harness.Story.State!.WrongAddressAssignments.Single(a => a.Attempt == 1);

        var quote = harness.Service.TryReview().Quote!;

        Assert.Equal(Release1WrongAddressAssignmentMode.MakeGood, quote.Assignment.Mode);
        Assert.Equal(primary.ProductId, quote.Assignment.ProductId);
        Assert.Equal("jar", quote.Assignment.PackagingId);
        Assert.Equal(2, quote.Assignment.Attempt);
        Assert.Equal(24d, quote.DeadlineDurationHours);
    }

    [Fact]
    public void Recovery_has_no_deadline()
    {
        using var harness = Release1WrongAddressHarness.RecoveryAvailable();

        var quote = harness.Service.TryReview().Quote!;
        Assert.Equal(Release1WrongAddressAssignmentMode.Recovery, quote.Assignment.Mode);
        Assert.Null(quote.DeadlineDurationHours);

        Assert.Equal(Release1WrongAddressDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Null(harness.Mission().DeadlineGameTimeHours);
        Assert.Equal(Release1MissionState.RecoveryActive, harness.Mission().State);
    }

    [Fact]
    public void A_deadline_cannot_fail_a_stage_that_already_holds_a_consumption_effect()
    {
        using var harness = Release1WrongAddressHarness.DeliveredThisSession();
        harness.World.TotalMinutes = 500d * 60d;

        harness.Service.Update();

        Assert.NotEqual(Release1MissionState.MakeGoodOffered, harness.Mission().State);
    }

    [Fact]
    public void Everything_is_inert_while_saving_or_without_a_matching_context()
    {
        using var harness = Release1WrongAddressHarness.Offered();
        harness.Service.OnSaveStart();
        Assert.Equal(Release1WrongAddressReviewStatus.Saving, harness.Service.TryReview().Status);
        Assert.Equal(Release1WrongAddressDecisionStatus.PersistenceDeferred, harness.Service.TryAccept().Status);
        harness.Service.OnSaveComplete();

        harness.World.Context = harness.World.Context with { LoadEpoch = 99 };
        Assert.Equal(Release1WrongAddressReviewStatus.Inactive, harness.Service.TryReview().Status);
    }
}

internal static class Release1WrongAddressHarness
{
    private const string PlayerId = "76561190000000001";
    private static readonly Guid SessionEpoch = Guid.Parse("77777777-7777-7777-7777-777777777777");

    internal static Harness SmallCourtesyStillActive()
    {
        var repository = new FakeRepository();
        var context = new FakeContext();
        var story = NewStory(context, repository);
        AcceptIntro(story, context);
        var world = new FakeWorld();
        var logs = new List<string>();
        var service = new Release1WrongAddressMissionService(story, world, log: logs.Add);
        service.OnLoadComplete();
        return new(story, service, world, context, repository, logs, phone: null, queue: new FakeQueue());
    }

    internal static Harness Offered(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        repository ??= new FakeRepository();
        var context = new FakeContext();
        var story = NewStory(context, repository);
        AcceptIntro(story, context);
        CompleteSmallCourtesy(story, context);
        world ??= new FakeWorld();
        var logs = new List<string>();
        var queue = new FakeQueue();
        var phone = withPhone ? new Release1PhoneCallService(story, queue, new FakeCue(), log: logs.Add) : null;
        var service = new Release1WrongAddressMissionService(story, world, phone, log: logs.Add);
        service.OnLoadComplete();
        repository.ResetEvidence();
        world.ResetMutationEvidence();
        return new(story, service, world, context, repository, logs, phone, queue);
    }

    // clearSlots resets every drop back to genuine empty slots before the caller's own convergence
    // runs. Default true preserves the pre-existing self-healing-baseline behaviour used by most
    // reload tests. Pass false when the test needs to observe a reload against whatever the world
    // was actually left holding (e.g. reload matrix rows where the package is still in a drop, or
    // custody already moved it), matching the spec's reload matrix.
    internal static Harness Load(FakeRepository repository, FakeWorld world, bool clearSlots = true)
    {
        var context = new FakeContext();
        var story = NewStory(context, repository);
        var logs = new List<string>();
        var service = new Release1WrongAddressMissionService(story, world, log: logs.Add);
        var worldBeforeConvergence = world.CaptureSnapshot();
        service.OnLoadComplete();

        // OnLoadComplete converges once and, while a stage is active, may immediately re-stage the
        // package (staging) or consume and pay it (delivery) if the world already reads as ready
        // for it (the point of self-healing convergence). Reload the story only, not the service,
        // so any subscription it just opened survives, and (when clearSlots is true) undo whatever
        // the world-mutating side of that convergence just did by restoring the exact snapshot
        // taken immediately before it ran; that hands callers the freshly-reloaded, pre-convergence
        // baseline so they can exercise their own explicit convergence and observe it, the same way
        // Active() does after TryAccept().
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
        Assert.Equal(Release1WrongAddressReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1WrongAddressDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.Active, harness.Mission().State);

        // TryAccept() converges once as part of acceptance: it stages the package into the (then
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
        Assert.Equal(Release1WrongAddressReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1WrongAddressDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, harness.Mission().State);
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

    // Drives an accepted stage all the way to Custody (the package staged, then physically taken
    // from the source drop) and durably saves that baseline, leaving the handoff drop genuinely
    // empty for the caller's own delivery fixture to fill.
    internal static Harness InCustody(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = Active(repository, world, withPhone);
        Assert.Equal(Release1WrongAddressStageStatus.Staged, harness.Service.ReconcileStaging());
        harness.World.EmptySlot(harness.Assignment.SourceDropGuid, 0);
        Assert.Equal(Release1WrongAddressStageStatus.CustodyInferred, harness.Service.ReconcileStaging());
        Assert.True(harness.Progress()!.Custody);
        Save(harness);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    internal static Harness MakeGoodInCustody(FakeRepository? repository = null, FakeWorld? world = null, bool withPhone = false)
    {
        var harness = MakeGoodActive(repository, world, withPhone);
        // Unlike Active(), MakeGoodActive() does not undo the staging its own acceptance convergence
        // already performed, so the make-good jar may already be staged by the time we get here.
        var stagingStatus = harness.Service.ReconcileStaging();
        Assert.True(
            stagingStatus is Release1WrongAddressStageStatus.Staged or Release1WrongAddressStageStatus.AlreadyStaged,
            $"Expected Staged or AlreadyStaged but got {stagingStatus}.");
        harness.World.EmptySlot(harness.Assignment.SourceDropGuid, 0);
        Assert.Equal(Release1WrongAddressStageStatus.CustodyInferred, harness.Service.ReconcileStaging());
        Assert.True(harness.Progress()!.Custody);
        Save(harness);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Drives a primary attempt through delivery to a durably persisted Satisfied/OnTime completion
    // with both the consumption and reward native effects fully Committed, leaving a single
    // subsequent Update() to queue Arthur's clean call and nothing else standing in its way.
    //
    // Release1WrongAddressMissionService.FinishDelivery captures the story revision once at the
    // start of its post-save pass and gates both the consumption and reward commits on that one
    // snapshot, so a single Save() cycle is enough for both effects to leave the Applied phase
    // together. Release1StoryRuntimeService's PersistState guard defers any new story mutation -
    // including Arthur's own phone-presentation authorization - for as long as any native effect
    // remains Applied, so recording the Nell accepted-message receipt is deferred until after that
    // save: recording it earlier would let Converge (which both Save() and Update() trigger) attempt
    // and durably reject an Arthur authorization before the caller's own single Update() is meant to
    // be what queues it. recordNellReceipt defaults on so the common case needs no further setup;
    // callers exercising the missing-prerequisite path can suppress it.
    internal static Harness CleanlyCompleted(bool withPhone = false, bool recordNellReceipt = true)
    {
        var harness = InCustody(withPhone: withPhone);
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.Service.ReconcileDelivery();
        Save(harness);
        if (recordNellReceipt) harness.RecordNellAcceptedReceipt();
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    // Drives a make-good attempt (a non-primary assignment, completed late by definition) through
    // delivery to a durably persisted Satisfied completion, so Arthur's clean condition (LastOutcome
    // OnTime and a Primary assignment at the satisfied attempt) cannot be met.
    internal static Harness MakeGoodCompleted(bool withPhone = false)
    {
        var harness = MakeGoodInCustody(withPhone: withPhone);
        harness.World.PlaceExactPackage(harness.Assignment, harness.Assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
        harness.Service.ReconcileDelivery();
        Save(harness);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    internal static Harness RecoveryAvailable(FakeRepository? repository = null, FakeWorld? world = null)
    {
        var harness = MakeGoodActive(repository, world);
        harness.World.TotalMinutes = 400d * 60d;
        harness.Service.Update();
        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);
        harness.Repository.ResetEvidence();
        harness.World.ResetMutationEvidence();
        return harness;
    }

    internal static Harness DeliveredThisSession(FakeRepository? repository = null, FakeWorld? world = null)
    {
        var harness = Active(repository, world);
        var mission = harness.Mission();
        var activationReceipt = $"wrong-address-activate-v1-a{mission.Attempt}";
        var activationCorrelation = Release1LogicalCorrelation.Create(
            PlayerId, Release1MissionCatalog.WrongAddress, mission.Attempt, Release1TransitionKind.MissionActivated, activationReceipt).Value;
        var effect = new Release1NativeEffectJournalEntry(
            $"wrong-address-cargo-v1-a{mission.Attempt}",
            Release1MissionCatalog.WrongAddress,
            mission.Attempt,
            "CargoTransfer",
            "source-drop",
            "oc-wrong-address",
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

    private static Release1StoryRuntimeService NewStory(FakeContext context, FakeRepository repository)
    {
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        return story;
    }

    private static void AcceptIntro(Release1StoryRuntimeService story, FakeContext context)
    {
        var receipt = "intro-wrong-address";
        var correlation = Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
        var result = story.TryExecuteDurably(new(
            context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
            Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt, correlation));
        if (!result.Accepted) throw new InvalidOperationException("Intro acceptance fixture setup failed: " + result.Message);
    }

    private static void CompleteSmallCourtesy(Release1StoryRuntimeService story, FakeContext context)
    {
        var smallCourtesy = story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        var acceptReceipt = "accept-sc-for-wa";
        var acceptCorrelation = Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.SmallCourtesy, smallCourtesy.Attempt, Release1TransitionKind.MissionAccepted, acceptReceipt).Value;
        var assignment = new Release1SmallCourtesyAssignment(
            Release1MissionCatalog.SmallCourtesy, smallCourtesy.Attempt, Release1SmallCourtesyAssignmentMode.Primary, acceptCorrelation,
            "cocaine", "Cocaine", 1_000d, "brick", "Brick", "sc-drop", "SC Drop", "Behind the diner", 1, 2, 3, 1.25d);
        var acceptCommand = new Release1StoryCommand(
            context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
            Release1MissionCatalog.SmallCourtesy, smallCourtesy.Attempt, Release1TransitionKind.MissionAccepted, acceptReceipt,
            acceptCorrelation, "small-courtesy-v1");
        var acceptResult = story.TryExecuteSmallCourtesyAcceptanceDurably(acceptCommand, assignment);
        if (!acceptResult.Accepted) throw new InvalidOperationException("Small Courtesy acceptance fixture setup failed: " + acceptResult.Message);

        smallCourtesy = story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        var completeReceipt = "complete-sc-for-wa";
        var completeCorrelation = Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.SmallCourtesy, smallCourtesy.Attempt, Release1TransitionKind.MissionCompleted, completeReceipt).Value;
        var completeCommand = new Release1StoryCommand(
            context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
            Release1MissionCatalog.SmallCourtesy, smallCourtesy.Attempt, Release1TransitionKind.MissionCompleted, completeReceipt,
            completeCorrelation, CompletionTiming: Release1CompletionTiming.OnTime, RewardAuthorizationReceiptId: "reward-sc-for-wa");
        var completeResult = story.TryExecuteDurably(completeCommand);
        if (!completeResult.Accepted) throw new InvalidOperationException("Small Courtesy completion fixture setup failed: " + completeResult.Message);
    }

    internal sealed class Harness : IDisposable
    {
        public Harness(
            Release1StoryRuntimeService story,
            Release1WrongAddressMissionService service,
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
        public Release1WrongAddressMissionService Service { get; }
        public FakeWorld World { get; }
        public FakeContext Context { get; }
        public FakeRepository Repository { get; }
        public List<string> Logs { get; }
        public Release1PhoneCallService? Phone { get; }
        public FakeQueue Queue { get; }
        public Release1WrongAddressAssignment Assignment => Story.State!.WrongAddressAssignments.MaxBy(value => value.Attempt)!;

        public Release1MissionRecord Mission() =>
            Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)];

        public Release1WrongAddressProgress? Progress() =>
            Story.State?.WrongAddressProgress.SingleOrDefault(progress => progress.Attempt == Assignment.Attempt);

        public Release1NativeEffectJournalEntry Consumption() => Story.State!.NativeEffects.Single(effect =>
            effect.MissionKey == Release1MissionCatalog.WrongAddress &&
            effect.EffectKind == "CargoTransfer" &&
            effect.Attempt == Assignment.Attempt);

        public Release1NativeEffectJournalEntry Reward() => Story.State!.NativeEffects.Single(effect =>
            effect.MissionKey == Release1MissionCatalog.WrongAddress &&
            effect.EffectKind == "Reward" &&
            effect.Attempt == Assignment.Attempt);

        // Records the Nell accepted-message receipt Arthur's widened prerequisite accepts on the
        // native presentation path, for the current Wrong Address mission attempt.
        public void RecordNellAcceptedReceipt()
        {
            var correlation = Release1LogicalCorrelation.Create(
                Context.Snapshot.PlayerId, Release1MissionCatalog.WrongAddress, Mission().Attempt,
                Release1TransitionKind.MissionAccepted, "presentation-nell-wa-accepted-v1").Value;
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
            if (ThrowOnInvoke) throw new InvalidOperationException("synthetic Wrong Address phone queue failure");
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

        // Keyed by dead drop GUID so a read of one drop can never observe another drop's slots.
        public Dictionary<string, Dictionary<int, Release1SmallCourtesySlotSnapshot>> DropSlots { get; } = new(StringComparer.Ordinal);
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

        // Simulates an unrelated, out-of-band cash change (not one of ours) that lands between the
        // baseline read taken right after consumption and the verification read Wrong Address makes
        // before attempting to pay. Zero means no drift; set once before the delivery pass under test.
        public float DriftCashAfterConsumption { get; set; }
        private bool _cashDriftArmed;
        private bool _cashDriftSkippedOnce;

        // The world state a native save would durably capture. Populated lazily the first time a
        // world-mutating call (TryChangeSlotQuantity/TryChangeCashBalance) runs since the last
        // MarkSaved()/RevertToLastSave(), so RevertToLastSave() can undo exactly the mutations this
        // mod made since the last real save while leaving whatever the test fixture itself set up
        // (e.g. a package a test placed directly into a drop) untouched.
        private Dictionary<string, Dictionary<int, Release1SmallCourtesySlotSnapshot>>? _preSaveDropSlots;
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
            reason = "the packaged product was staged and read back with the exact expected values.";
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesySlotSnapshot Slot(string deadDropGuid, int slotIndex) =>
            DropOf(deadDropGuid).TryGetValue(slotIndex, out var slot) ? slot : new(slotIndex, null, null, 0, false, 0f);

        public void PlaceExactPackage(Release1WrongAddressAssignment assignment) =>
            DropOf(assignment.SourceDropGuid)[0] = new(0, assignment.ProductId, assignment.PackagingId, assignment.PackageQuantity, true, assignment.PackageQuantity * 1_000f);

        public void PlaceExactPackage(Release1WrongAddressAssignment assignment, string deadDropGuid, int slotIndex, float value) =>
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

        private Dictionary<int, Release1SmallCourtesySlotSnapshot> DropOf(string deadDropGuid)
        {
            if (!DropSlots.TryGetValue(deadDropGuid, out var drop))
            {
                drop = new Dictionary<int, Release1SmallCourtesySlotSnapshot>();
                DropSlots[deadDropGuid] = drop;
            }
            return drop;
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

        // Wrong Address never debits the player wallet; this fake is not Chief-specific.
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
        }

        private void CapturePreSaveSnapshotIfNeeded()
        {
            if (_preSaveDropSlots is not null) return;
            _preSaveDropSlots = DropSlots.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<int, Release1SmallCourtesySlotSnapshot>(pair.Value));
            _preSaveCashBalance = CashBalance;
        }

        // Undoes every world mutation made since the last MarkSaved() (i.e. since the last real
        // native save), while leaving anything the test itself set up directly (PlaceExactPackage,
        // SetSlot, etc.) untouched, the same way quitting without saving discards this session's
        // unsaved native mutations but not what was already on disk.
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
            _cashDriftArmed = false;
            _cashDriftSkippedOnce = false;
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
