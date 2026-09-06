using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1SmallCourtesyMissionServiceTests
{
    private const string PlayerId = "76561190000000001";
    private static readonly Guid SessionEpoch = Guid.Parse("51515151-5151-5151-5151-515151515151");

    [Theory]
    [InlineData(null)]
    [InlineData(Release1PhonePresentationAttemptState.Pending)]
    [InlineData(Release1PhonePresentationAttemptState.Attempting)]
    [InlineData(Release1PhonePresentationAttemptState.Delivered)]
    [InlineData(Release1PhonePresentationAttemptState.Ambiguous)]
    [InlineData(Release1PhonePresentationAttemptState.Completed)]
    public void Offer_eligibility_is_story_derived_and_phone_independent(Release1PhonePresentationAttemptState? phoneState)
    {
        using var harness = ActiveHarness(phoneState);

        Assert.Equal(Release1SmallCourtesyOfferStatus.Available, harness.Service.OfferStatus);
        var review = harness.Service.TryReview();
        Assert.Equal(Release1SmallCourtesyReviewStatus.Ready, review.Status);
        Assert.Equal(1, review.Quote!.Assignment.Attempt);
        Assert.Equal(Release1SmallCourtesyAssignmentMode.Primary, review.Quote.Assignment.Mode);
    }

    [Fact]
    public void Review_is_read_only_and_reports_no_product_or_no_empty_drop_without_advancing_story()
    {
        using var harness = ActiveHarness();
        var before = harness.Story.State;
        harness.World.Products = Array.Empty<Release1SmallCourtesyProductCandidate>();

        var noProduct = harness.Service.TryReview();

        Assert.Equal(Release1SmallCourtesyReviewStatus.NoDiscoveredProduct, noProduct.Status);
        Assert.Same(before, harness.Story.State);
        Assert.Empty(harness.Repository.Updates);

        harness.World.Products = DefaultProducts();
        harness.World.Drops = harness.World.Drops.Select(drop => drop with { IsEmpty = false }).ToArray();
        var noDrop = harness.Service.TryReview();

        Assert.Equal(Release1SmallCourtesyReviewStatus.NoEmptyDeadDrop, noDrop.Status);
        Assert.Same(before, harness.Story.State);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void Accept_recomputes_the_reviewed_quote_and_persists_acceptance_then_activation()
    {
        using var harness = ActiveHarness();
        harness.World.TotalMinutes = 600d;
        var review = harness.Service.TryReview();

        var accepted = harness.Service.TryAccept();

        Assert.Equal(Release1SmallCourtesyDecisionStatus.Accepted, accepted.Status);
        var mission = harness.Story.State!.Missions[0];
        Assert.Equal(Release1MissionState.Active, mission.State);
        Assert.Equal("small-courtesy-v1", mission.TermsVersion);
        Assert.Equal(10d, mission.AcceptedGameTimeHours);
        Assert.Equal(34d, mission.DeadlineGameTimeHours);
        Assert.Equal(review.Quote!.Assignment, Assert.Single(harness.Story.State.SmallCourtesyAssignments));
        Assert.Equal(2, harness.Repository.Updates.Count);
    }

    [Fact]
    public void Accept_rejects_a_changed_quote_without_mutating_story_or_repository()
    {
        using var harness = ActiveHarness();
        Assert.Equal(Release1SmallCourtesyReviewStatus.Ready, harness.Service.TryReview().Status);
        var before = harness.Story.State;
        harness.World.Drops = new[]
        {
            new Release1SmallCourtesyDropCandidate("replacement-drop", "Replacement", "Changed", 9, 8, 7, true)
        };

        var result = harness.Service.TryAccept();

        Assert.Equal(Release1SmallCourtesyDecisionStatus.QuoteChanged, result.Status);
        Assert.Same(before, harness.Story.State);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void Pending_world_and_acceptance_persistence_failure_are_non_destructive()
    {
        using var harness = ActiveHarness();
        var before = harness.Story.State;
        harness.World.Status = Release1SmallCourtesyWorldReadStatus.Pending;

        var pending = harness.Service.TryReview();

        Assert.Equal(Release1SmallCourtesyReviewStatus.Pending, pending.Status);
        Assert.Same(before, harness.Story.State);
        Assert.Empty(harness.Repository.Updates);

        harness.World.Status = Release1SmallCourtesyWorldReadStatus.Ready;
        Assert.Equal(Release1SmallCourtesyReviewStatus.Ready, harness.Service.TryReview().Status);
        harness.Repository.FailUpdates = true;
        var rejected = harness.Service.TryAccept();

        Assert.Equal(Release1SmallCourtesyDecisionStatus.Rejected, rejected.Status);
        Assert.Same(before, harness.Story.State);
        Assert.Equal(Release1MissionState.Offered, harness.Story.State!.Missions[0].State);
        Assert.Empty(harness.Story.State.SmallCourtesyAssignments);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void Throwing_world_reads_fail_closed_without_mutating_story()
    {
        using var harness = ActiveHarness();
        var before = harness.Story.State;
        harness.World.ThrowOnRead = true;

        var reviewException = Record.Exception(() => harness.Service.TryReview());
        var updateException = Record.Exception(harness.Service.Update);

        Assert.Null(reviewException);
        Assert.Null(updateException);
        Assert.Equal(Release1SmallCourtesyReviewStatus.Unavailable, harness.Service.TryReview().Status);
        Assert.Same(before, harness.Story.State);
        Assert.Empty(harness.Repository.Updates);
    }

    [Fact]
    public void Not_now_is_durable_and_the_next_load_reoffers_exactly_once_with_a_new_attempt()
    {
        var repository = new FakeRepository();
        using (var first = ActiveHarness(repository: repository))
        {
            var deferred = first.Service.TryDefer();
            Assert.Equal(Release1SmallCourtesyDecisionStatus.Deferred, deferred.Status);
            Assert.Equal(Release1MissionState.Deferred, first.Story.State!.Missions[0].State);
            Assert.Equal(Release1SmallCourtesyOfferStatus.Dismissed, first.Service.OfferStatus);
        }

        repository.ResetEvidence();
        using var second = LoadedHarness(repository);

        Assert.Equal(Release1MissionState.Offered, second.Story.State!.Missions[0].State);
        Assert.Equal(2, second.Story.State.Missions[0].Attempt);
        Assert.Equal(Release1SmallCourtesyOfferStatus.Available, second.Service.OfferStatus);
        Assert.Equal(2, second.Service.TryReview().Quote!.Assignment.Attempt);
        Assert.Single(repository.Updates);
        second.Service.OnLoadComplete();
        Assert.Single(repository.Updates);
    }

    [Fact]
    public void Deadline_uses_canonical_minutes_as_hours_and_fails_at_the_exact_boundary_once()
    {
        using var harness = ActiveHarness();
        harness.World.TotalMinutes = 300d;
        Assert.Equal(Release1SmallCourtesyReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1SmallCourtesyDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(5d, harness.Story.State!.Missions[0].AcceptedGameTimeHours);
        Assert.Equal(29d, harness.Story.State.Missions[0].DeadlineGameTimeHours);

        harness.World.TotalMinutes = 1_739.999d;
        harness.Service.Update();
        Assert.Equal(Release1MissionState.Active, harness.Story.State.Missions[0].State);

        harness.World.TotalMinutes = 1_740d;
        harness.Service.Update();
        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Story.State.Missions[0].State);
        Assert.Equal(8, harness.Story.State.Standing);
        var revision = harness.Story.State.Revision;
        harness.Service.Update();
        Assert.Equal(revision, harness.Story.State.Revision);
        Assert.Equal(8, harness.Story.State.Standing);
    }

    [Theory]
    [InlineData(1_439.999d, Release1MissionState.Active)]
    [InlineData(1_440d, Release1MissionState.MakeGoodOffered)]
    [InlineData(1_440.001d, Release1MissionState.MakeGoodOffered)]
    public void Deadline_boundary_uses_stored_double_precision(double totalMinutes, Release1MissionState expected)
    {
        using var harness = ActiveHarness();
        Assert.Equal(Release1SmallCourtesyReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1SmallCourtesyDecisionStatus.Accepted, harness.Service.TryAccept().Status);

        harness.World.TotalMinutes = totalMinutes;
        harness.Service.Update();

        Assert.Equal(expected, harness.Story.State!.Missions[0].State);
    }

    [Fact]
    public void Make_good_has_a_24_hour_deadline_and_recovery_has_none()
    {
        using var harness = ActiveHarness();
        Assert.Equal(Release1SmallCourtesyReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1SmallCourtesyDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        harness.World.TotalMinutes = 1_440d;
        harness.Service.Update();

        harness.World.TotalMinutes = 1_800d;
        var makeGoodReview = harness.Service.TryReview();
        Assert.Equal(Release1SmallCourtesyAssignmentMode.MakeGood, makeGoodReview.Quote!.Assignment.Mode);
        Assert.Equal("jar", makeGoodReview.Quote.Assignment.PackagingId);
        Assert.Equal(Release1SmallCourtesyDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Equal(30d, harness.Story.State!.Missions[0].AcceptedGameTimeHours);
        Assert.Equal(54d, harness.Story.State.Missions[0].DeadlineGameTimeHours);

        harness.World.TotalMinutes = 3_240d;
        harness.Service.Update();
        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Story.State.Missions[0].State);
        Assert.Equal(0, harness.Story.State.Standing);

        harness.World.TotalMinutes = 6_000d;
        var recoveryReview = harness.Service.TryReview();
        Assert.Equal(Release1SmallCourtesyAssignmentMode.Recovery, recoveryReview.Quote!.Assignment.Mode);
        Assert.Equal(Release1SmallCourtesyDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        Assert.Null(harness.Story.State!.Missions[0].DeadlineGameTimeHours);
        var revision = harness.Story.State.Revision;
        harness.World.TotalMinutes = 1_000_000d;
        harness.Service.Update();
        Assert.Equal(Release1MissionState.RecoveryActive, harness.Story.State.Missions[0].State);
        Assert.Equal(revision, harness.Story.State.Revision);
    }

    [Fact]
    public void Saving_preload_context_mismatch_and_disposal_are_inert()
    {
        using var harness = ActiveHarness();
        Assert.Equal(Release1SmallCourtesyReviewStatus.Ready, harness.Service.TryReview().Status);
        Assert.Equal(Release1SmallCourtesyDecisionStatus.Accepted, harness.Service.TryAccept().Status);
        harness.World.TotalMinutes = 1_440d;

        harness.Story.OnSaveStart();
        harness.Service.OnSaveStart();
        harness.Service.Update();
        Assert.Equal(Release1MissionState.Active, harness.Story.State!.Missions[0].State);
        harness.Story.OnSaveComplete();
        harness.Service.OnSaveComplete();

        harness.World.Context = harness.World.Context with { PlayerId = "76561197984645370" };
        harness.Service.Update();
        Assert.Equal(Release1MissionState.Active, harness.Story.State.Missions[0].State);

        harness.Service.OnPreLoad();
        Assert.Equal(Release1SmallCourtesyOfferStatus.Inactive, harness.Service.OfferStatus);
        harness.Service.Dispose();
        Assert.Equal(Release1SmallCourtesyDecisionStatus.Disposed, harness.Service.TryDismiss().Status);
    }

    private static Harness ActiveHarness(
        Release1PhonePresentationAttemptState? phoneState = null,
        FakeRepository? repository = null)
    {
        repository ??= new FakeRepository();
        var context = new FakeContext();
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var introReceipt = "intro-small-courtesy-service";
        var intro = new Release1StoryCommand(
            context.Snapshot.SessionEpoch,
            context.Snapshot.LoadEpoch,
            context.Snapshot.PlayerId,
            Release1MissionCatalog.IntroScopeKey,
            0,
            Release1TransitionKind.IntroAccepted,
            introReceipt,
            Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, introReceipt).Value);
        Assert.True(story.TryExecuteDurably(intro).Accepted);
        if (phoneState is not null) SetPhoneState(story, context, phoneState.Value);
        repository.ResetEvidence();
        var world = new FakeWorld(context.Snapshot);
        var service = new Release1SmallCourtesyMissionService(story, world);
        service.OnLoadComplete();
        return new(story, service, world, context, repository);
    }

    private static Harness LoadedHarness(FakeRepository repository)
    {
        var context = new FakeContext();
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var world = new FakeWorld(context.Snapshot);
        var service = new Release1SmallCourtesyMissionService(story, world);
        service.OnLoadComplete();
        return new(story, service, world, context, repository);
    }

    private static void SetPhoneState(
        Release1StoryRuntimeService story,
        FakeContext context,
        Release1PhonePresentationAttemptState state)
    {
        var correlation = Release1PhoneCallCorrelation.Create(
            PlayerId,
            Release1MissionCatalog.SmallCourtesy,
            1,
            Release1PhoneCallRole.Nell,
            "intro-contact-v1");
        Assert.True(story.TryAuthorizePhonePresentation(
            Release1MissionCatalog.SmallCourtesy,
            1,
            correlation,
            Release1PhoneCallRole.Nell.ToString(),
            null).Accepted);
        if (state == Release1PhonePresentationAttemptState.Pending) return;
        Assert.True(story.TryTransitionPhonePresentation(correlation, Release1PhonePresentationAttemptState.Attempting).Accepted);
        if (state == Release1PhonePresentationAttemptState.Attempting) return;
        if (state == Release1PhonePresentationAttemptState.Ambiguous)
        {
            Assert.True(story.TryTransitionPhonePresentation(correlation, state).Accepted);
            return;
        }
        Assert.True(story.TryTransitionPhonePresentation(correlation, Release1PhonePresentationAttemptState.Delivered).Accepted);
        if (state == Release1PhonePresentationAttemptState.Completed)
            Assert.True(story.TryTransitionPhonePresentation(correlation, state).Accepted);
    }

    private static IReadOnlyList<Release1SmallCourtesyProductCandidate> DefaultProducts() =>
        new[]
        {
            new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true),
            new Release1SmallCourtesyProductCandidate("og-kush", "OG Kush", 100d, true)
        };

    private sealed class Harness : IDisposable
    {
        public Harness(
            Release1StoryRuntimeService story,
            Release1SmallCourtesyMissionService service,
            FakeWorld world,
            FakeContext context,
            FakeRepository repository)
        {
            Story = story;
            Service = service;
            World = world;
            Context = context;
            Repository = repository;
        }

        public Release1StoryRuntimeService Story { get; }
        public Release1SmallCourtesyMissionService Service { get; }
        public FakeWorld World { get; }
        public FakeContext Context { get; }
        public FakeRepository Repository { get; }
        public void Dispose() { Service.Dispose(); Story.Dispose(); }
    }

    private sealed class FakeWorld : IRelease1SmallCourtesyWorld
    {
        public FakeWorld(Release1StoryHostContextSnapshot context)
        {
            Context = context;
        }

        public Release1StoryHostContextSnapshot Context { get; set; }
        public Release1SmallCourtesyWorldReadStatus Status { get; set; } = Release1SmallCourtesyWorldReadStatus.Ready;
        public bool ThrowOnRead { get; set; }
        public double TotalMinutes { get; set; }
        public IReadOnlyList<Release1SmallCourtesyProductCandidate> Products { get; set; } = DefaultProducts();
        public IReadOnlyList<Release1SmallCourtesyDropCandidate> Drops { get; set; } = new[]
        {
            new Release1SmallCourtesyDropCandidate("drop-b", "Drop B", "Second", 4, 5, 6, true),
            new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "First", 1, 2, 3, true)
        };

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context)
        {
            if (ThrowOnRead) throw new InvalidOperationException("synthetic world failure");
            context = Context;
            return Status;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
        {
            if (ThrowOnRead) throw new InvalidOperationException("synthetic world failure");
            totalMinutes = TotalMinutes;
            return Status;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products)
        {
            if (ThrowOnRead) throw new InvalidOperationException("synthetic world failure");
            products = Products;
            return Status;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops)
        {
            if (ThrowOnRead) throw new InvalidOperationException("synthetic world failure");
            drops = Drops;
            return Status;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(
            Release1SmallCourtesyPackageKind kind,
            out Release1SmallCourtesyPackagingCandidate packaging)
        {
            if (ThrowOnRead) throw new InvalidOperationException("synthetic world failure");
            packaging = kind == Release1SmallCourtesyPackageKind.Brick
                ? new("brick", "Brick")
                : new("jar", "Jar");
            return Status;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(
            string deadDropGuid,
            out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
        {
            slots = Array.Empty<Release1SmallCourtesySlotSnapshot>();
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
        {
            room = Release1HoldRoomSnapshot.Unavailable();
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked) =>
            Release1SmallCourtesyWorldMutationStatus.Unavailable;

        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount) =>
            Release1SmallCourtesyWorldMutationStatus.Unavailable;

        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(
            string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
        {
            reason = "unavailable in this fake world.";
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(
            string deadDropGuid,
            Action<string> callback,
            out IRelease1SmallCourtesyDropSubscription? subscription)
        {
            subscription = null;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Unavailable;

        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Unavailable;

        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)
        {
            reason = string.Empty;
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)
        {
            reason = string.Empty;
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }

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
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(SessionEpoch, 1, PlayerId, Path.GetTempPath());
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class FakeRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public string BoundSaveFolder { get; } = Path.GetFullPath(Path.GetTempPath());
        public Release1StoryState? StoredState { get; private set; }
        public List<Release1StoryState> Updates { get; } = new();
        public bool FailUpdates { get; set; }

        public Release1StoryStoreLoadResult Load() => new(
            true,
            StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded,
            new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState),
            Release1StoryStoreFailureReason.None,
            string.Empty);

        public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
        {
            if (FailUpdates)
                return new(
                    false,
                    Release1StoryStoreUpdateStatus.Rejected,
                    null,
                    Release1StoryStoreFailureReason.AtomicReplacementFailed,
                    "synthetic persistence failure");
            StoredState = state;
            if (state is not null) Updates.Add(state);
            return new(
                true,
                Release1StoryStoreUpdateStatus.Updated,
                new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state),
                Release1StoryStoreFailureReason.None,
                string.Empty);
        }

        public void ResetEvidence() => Updates.Clear();
    }
}
