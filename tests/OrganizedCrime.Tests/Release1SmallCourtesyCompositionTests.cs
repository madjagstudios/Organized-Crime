using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1SmallCourtesyCompositionTests
{
    [Fact]
    public void Real_composition_completes_once_across_save_boundaries_and_full_reconstruction()
    {
        var repository = new Release1SmallCourtesyDepositTests.FakeRepository();
        var context = new Release1SmallCourtesyDepositTests.FakeContext();
        var world = new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot);
        var story = LoadStory(context, repository);
        AuthorIntroAndDeliveredNell(story, context);
        var unrelatedBefore = story.State!.Missions.Skip(2).ToArray();
        Assert.False(story.State.Release1Recognized);

        using (var composition = new Release1SmallCourtesyComposition(story, world))
        {
            composition.OnLoadComplete();
            Assert.Equal(Release1SmallCourtesyReviewStatus.Ready, composition.Presenter.TryReview().Status);
            Assert.Equal(Release1SmallCourtesyDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);
            var assignment = story.State!.SmallCourtesyAssignments.Single();
            world.RaiseClosed(assignment.DeadDropGuid);
            Assert.Equal(0, world.QuantityChanges);

            Save(story, composition);
            Assert.Equal(1, world.QuantityChanges);
            Assert.Equal(0, world.CashChanges);

            Save(story, composition);
            Assert.Equal(1, world.QuantityChanges);
            Assert.Equal(1, world.CashChanges);

            Save(story, composition);
            Assert.Equal(1, world.QuantityChanges);
            Assert.Equal(1, world.CashChanges);
            composition.OnPreLoad();
        }
        story.Dispose();

        using var restoredStory = LoadStory(context, repository);
        using var restored = new Release1SmallCourtesyComposition(restoredStory, world);
        restored.OnLoadComplete();
        restored.Update();

        Assert.Equal(1, world.QuantityChanges);
        Assert.Equal(1, world.CashChanges);
        Assert.Equal(Release1MissionState.Satisfied, restoredStory.State!.Missions[0].State);
        Assert.False(restoredStory.State.Release1Recognized);
        Assert.Equal(Release1MissionState.Offered, restoredStory.State.Missions[1].State);
        Assert.True(unrelatedBefore.SequenceEqual(restoredStory.State.Missions.Skip(2)));
    }

    [Fact]
    public void Lifecycle_is_idempotent_and_disposes_world_after_service()
    {
        var repository = new Release1SmallCourtesyDepositTests.FakeRepository();
        var context = new Release1SmallCourtesyDepositTests.FakeContext();
        var story = LoadStory(context, repository);
        AuthorIntroAndDeliveredNell(story, context);
        var world = new DisposableWorld(context.Snapshot);
        var composition = new Release1SmallCourtesyComposition(story, world);

        composition.OnLoadComplete();
        composition.OnLoadComplete();
        composition.OnPreLoad();
        composition.OnPreLoad();
        composition.Dispose();
        composition.Dispose();

        Assert.True(world.Disposed);
        Assert.Equal(Release1SmallCourtesyOfferStatus.Disposed, composition.Service.OfferStatus);
        story.Dispose();
    }

    private static Release1StoryRuntimeService LoadStory(
        Release1SmallCourtesyDepositTests.FakeContext context,
        Release1SmallCourtesyDepositTests.FakeRepository repository)
    {
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        return story;
    }

    private static void AuthorIntroAndDeliveredNell(
        Release1StoryRuntimeService story,
        Release1SmallCourtesyDepositTests.FakeContext context)
    {
        const string introReceipt = "intro-composition";
        Assert.True(story.TryExecuteDurably(new(
            context.Snapshot.SessionEpoch,
            context.Snapshot.LoadEpoch,
            context.Snapshot.PlayerId,
            Release1MissionCatalog.IntroScopeKey,
            0,
            Release1TransitionKind.IntroAccepted,
            introReceipt,
            Release1LogicalCorrelation.Create(
                context.Snapshot.PlayerId,
                Release1MissionCatalog.IntroScopeKey,
                0,
                Release1TransitionKind.IntroAccepted,
                introReceipt).Value)).Accepted);
        var correlation = Release1PhoneCallCorrelation.Create(
            context.Snapshot.PlayerId,
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
        Assert.True(story.TryTransitionPhonePresentation(correlation, Release1PhonePresentationAttemptState.Attempting).Accepted);
        Assert.True(story.TryTransitionPhonePresentation(correlation, Release1PhonePresentationAttemptState.Delivered).Accepted);
    }

    private static void Save(Release1StoryRuntimeService story, Release1SmallCourtesyComposition composition)
    {
        story.OnSaveStart();
        composition.OnSaveStart();
        story.OnSaveComplete();
        composition.OnSaveComplete();
    }

    private sealed class DisposableWorld : IRelease1SmallCourtesyWorld, IDisposable
    {
        private readonly Release1SmallCourtesyDepositTests.FakeWorld _inner;
        public DisposableWorld(Release1StoryHostContextSnapshot context) => _inner = new(context);
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context) => _inner.TryReadContext(out context);
        public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes) => _inner.TryReadCanonicalTotalMinutes(out totalMinutes);
        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products) => _inner.TryReadProducts(out products);
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops) => _inner.TryReadDeadDrops(out drops);
        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging) => _inner.TryReadPackaging(kind, out packaging);
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots) => _inner.TryReadDeadDropSlots(deadDropGuid, out slots);
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room) => _inner.TryReadHoldRoom(out room);
        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked) => _inner.TrySetSlotLocked(deadDropGuid, slotIndex, locked);
        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount) => _inner.TryChangeSlotQuantity(deadDropGuid, slotIndex, amount);
        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason) => _inner.TryInsertPackagedProduct(deadDropGuid, slotIndex, productId, packagingId, quantity, out reason);
        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(string deadDropGuid, Action<string> callback, out IRelease1SmallCourtesyDropSubscription? subscription) => _inner.TrySubscribeDeadDropClosed(deadDropGuid, callback, out subscription);
        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance) => _inner.TryReadCashBalance(out balance);
        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount) => _inner.TryChangeCashBalance(amount);
        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) => _inner.TryDebitCashBalance(amount);
        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason) => _inner.TryEngageLockdown(out reason);
        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason) => _inner.TryReleaseLockdown(out reason);
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance) => _inner.TryReadDeadDropSlotCashBalance(deadDropGuid, slotIndex, out balance);
        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) => _inner.TryChangeDeadDropSlotCashBalance(deadDropGuid, slotIndex, amount);
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance) => _inner.TryReadHoldRoomSlotCashBalance(closetGuid, slotIndex, out balance);
        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) => _inner.TrySetHoldRoomSlotLocked(closetGuid, slotIndex, locked);
        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) => _inner.TryChangeHoldRoomSlotCashBalance(closetGuid, slotIndex, amount);
        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity) => _inner.TryReadProductionActivity(out activity);
        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot) => _inner.TryReadFieldContact(contactId, out snapshot);
        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason) => _inner.TryDespawnFieldContact(contactId, out reason);
        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason) => _inner.TryProvokeFieldContact(contactId, out reason);
        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason) => _inner.TryParkFieldContact(contactId, out reason);
        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason) => _inner.TryUnparkFieldContact(contactId, aheadMetres, out reason);
    }
}
