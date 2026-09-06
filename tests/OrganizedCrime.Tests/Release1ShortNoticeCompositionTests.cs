using System.Linq;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ShortNoticeCompositionTests
{
    [Fact]
    public void Real_composition_drives_the_whole_mission_end_to_end_and_a_full_reconstruction_pays_nothing_further()
    {
        var repository = new Release1ShortNoticeHarness.FakeRepository();
        var context = new Release1ShortNoticeHarness.FakeContext();
        var world = new Release1ShortNoticeHarness.FakeWorld();
        SeedRoomWithNoNameSatisfied(context, repository);
        var story = NewStory(context, repository);

        Release1ShortNoticeAssignment assignment;
        using (var phone = new Release1PhoneCallService(
                   story, new Release1ShortNoticeHarness.FakeQueue(), new Release1ShortNoticeHarness.FakeCue()))
        using (var composition = new Release1ShortNoticeComposition(story, world, phone))
        {
            composition.OnLoadComplete();

            Assert.Equal(Release1ShortNoticeOfferStatus.Available, composition.Service.OfferStatus);

            world.TotalMinutes = 6_000d;
            var review = composition.Presenter.TryReview();
            Assert.Equal(Release1ShortNoticeReviewStatus.Ready, review.Status);

            var decision = composition.Presenter.TryAccept();
            Assert.Equal(Release1ShortNoticeDecisionStatus.Accepted, decision.Status);

            assignment = story.State!.ShortNoticeAssignments.Single();
            Assert.Equal(3, assignment.RequiredQuantity);

            // One brick: a shortfall of two, noticed once.
            world.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 1, monetaryValue: 1_000f);
            composition.Update();
            Assert.Equal(2, Progress(story, assignment)!.LastShortfallNoticed);
            Assert.Equal(0, world.QuantityChanges);

            // A second pass at the same count does not re-notice the same shortfall.
            composition.Update();
            Assert.Equal(2, Progress(story, assignment)!.LastShortfallNoticed);

            // Two bricks: a distinct shortfall of one, not the same notice as the first.
            world.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 2, monetaryValue: 2_000f);
            composition.Update();
            Assert.Equal(1, Progress(story, assignment)!.LastShortfallNoticed);
            Assert.Equal(0, world.QuantityChanges);

            // Three bricks in one slot: the deposit runs, consumes exactly N, and pays at once.
            world.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 3, monetaryValue: 3_000f);
            composition.Update();

            Assert.Equal(Release1MissionState.Satisfied, Mission(story).State);
            Assert.Equal(0, world.Slot(assignment.HandoffDropGuid, 0).Quantity);
            Assert.Equal(1, world.QuantityChanges);
            Assert.Equal(1, world.CashChanges);

            Save(story, composition);

            Assert.Equal(
                Release1NativeEffectPhase.Committed,
                story.State!.NativeEffects.Single(effect =>
                    effect.MissionKey == Release1MissionCatalog.ShortNotice && effect.EffectKind == "CargoTransfer").Phase);
            Assert.Equal(
                Release1NativeEffectPhase.Committed,
                story.State.NativeEffects.Single(effect =>
                    effect.MissionKey == Release1MissionCatalog.ShortNotice && effect.EffectKind == "Reward").Phase);
            Assert.Equal(60, story.State.Standing);
            Assert.Equal(
                Release1MissionState.Offered,
                story.State.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)].State);

            composition.OnPreLoad();
        }
        story.Dispose();

        // A full reconstruction from the persisted sidecar consumes and pays nothing further, and a
        // second reconstruction on top of that one still does neither.
        RestoreAndAssertNothingFurther(repository, world, assignment);
        RestoreAndAssertNothingFurther(repository, world, assignment);
    }

    [Fact]
    public void A_spread_across_two_slots_holds_and_consumes_nothing_through_the_real_composition()
    {
        var repository = new Release1ShortNoticeHarness.FakeRepository();
        var context = new Release1ShortNoticeHarness.FakeContext();
        var world = new Release1ShortNoticeHarness.FakeWorld();
        SeedRoomWithNoNameSatisfied(context, repository);
        var story = NewStory(context, repository);

        using var phone = new Release1PhoneCallService(
            story, new Release1ShortNoticeHarness.FakeQueue(), new Release1ShortNoticeHarness.FakeCue());
        using var composition = new Release1ShortNoticeComposition(story, world, phone);
        composition.OnLoadComplete();
        world.TotalMinutes = 6_000d;
        Assert.Equal(Release1ShortNoticeReviewStatus.Ready, composition.Presenter.TryReview().Status);
        Assert.Equal(Release1ShortNoticeDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);
        var assignment = story.State!.ShortNoticeAssignments.Single();

        // The same manifest split across two slots at the handoff: two matching slots hold the
        // deposit and never consume, regardless of how many units either slot carries.
        world.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 2, monetaryValue: 2_000f);
        world.SetSlot(assignment.HandoffDropGuid, 1, assignment.ProductId, assignment.PackagingId, 1, monetaryValue: 1_000f);

        composition.Update();

        Assert.True(Progress(story, assignment)!.SpreadNoticed);
        Assert.Equal(0, world.QuantityChanges);
        Assert.Equal(0, world.CashChanges);
        Assert.Equal(Release1MissionState.Active, Mission(story).State);

        composition.OnPreLoad();
        story.Dispose();
    }

    [Fact]
    public void A_lapsed_window_with_a_partial_deposit_fails_the_stage_and_leaves_the_units_in_the_drop()
    {
        var repository = new Release1ShortNoticeHarness.FakeRepository();
        var context = new Release1ShortNoticeHarness.FakeContext();
        var world = new Release1ShortNoticeHarness.FakeWorld();
        SeedRoomWithNoNameSatisfied(context, repository);
        var story = NewStory(context, repository);

        using var phone = new Release1PhoneCallService(
            story, new Release1ShortNoticeHarness.FakeQueue(), new Release1ShortNoticeHarness.FakeCue());
        using var composition = new Release1ShortNoticeComposition(story, world, phone);
        composition.OnLoadComplete();
        world.TotalMinutes = 6_000d;
        Assert.Equal(Release1ShortNoticeReviewStatus.Ready, composition.Presenter.TryReview().Status);
        Assert.Equal(Release1ShortNoticeDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);
        var assignment = story.State!.ShortNoticeAssignments.Single();

        // Two of the three bricks land, short of the manifest, then the twelve-hour window lapses.
        world.SetSlot(assignment.HandoffDropGuid, 0, assignment.ProductId, assignment.PackagingId, 2, monetaryValue: 2_000f);
        composition.Update();
        Assert.Equal(1, Progress(story, assignment)!.LastShortfallNoticed);

        world.TotalMinutes = 200d * 60d;
        composition.Update();

        Assert.Equal(Release1MissionState.MakeGoodOffered, Mission(story).State);
        Assert.Equal(0, world.QuantityChanges);
        Assert.Equal(0, world.CashChanges);
        Assert.Equal(2, world.Slot(assignment.HandoffDropGuid, 0).Quantity);

        composition.OnPreLoad();
        story.Dispose();
    }

    private static void RestoreAndAssertNothingFurther(
        Release1ShortNoticeHarness.FakeRepository repository,
        Release1ShortNoticeHarness.FakeWorld world,
        Release1ShortNoticeAssignment assignment)
    {
        var context = new Release1ShortNoticeHarness.FakeContext();
        var story = NewStory(context, repository);
        using var phone = new Release1PhoneCallService(
            story, new Release1ShortNoticeHarness.FakeQueue(), new Release1ShortNoticeHarness.FakeCue());
        using var composition = new Release1ShortNoticeComposition(story, world, phone);
        world.ResetMutationEvidence();

        composition.OnLoadComplete();
        composition.Update();

        Assert.Equal(0, world.QuantityChanges);
        Assert.Equal(0, world.CashChanges);
        Assert.Equal(
            Release1MissionState.Satisfied,
            story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)].State);
        Assert.Equal(assignment, story.State.ShortNoticeAssignments.Single());

        composition.OnPreLoad();
        story.Dispose();
    }

    // Seeds the repository with a story whose Small Courtesy, Wrong Address, and Room With No Name
    // missions are all already Satisfied, through the same pure Release1StoryTransitions path
    // Release1ShortNoticeHarness's own private Satisfy helper uses (that helper is private to its
    // file, so this composition test carries its own copy, the same way
    // Release1RoomWithNoNameCompositionTests carries its own copy rather than reaching into another
    // file's private helpers).
    private static void SeedRoomWithNoNameSatisfied(
        Release1ShortNoticeHarness.FakeContext context, Release1ShortNoticeHarness.FakeRepository repository)
    {
        var state = Release1StoryState.CreateAccepted(context.Snapshot.PlayerId, Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-short-notice-composition").Value);
        state = Satisfy(context, state, Release1MissionCatalog.SmallCourtesy);
        state = Satisfy(context, state, Release1MissionCatalog.WrongAddress);
        state = Satisfy(context, state, Release1MissionCatalog.RoomWithNoName);
        repository.Update(state);
    }

    private static Release1StoryState Satisfy(
        Release1ShortNoticeHarness.FakeContext context, Release1StoryState state, string missionKey)
    {
        var sessionEpoch = context.Snapshot.SessionEpoch;
        var loadEpoch = context.Snapshot.LoadEpoch;
        var playerId = context.Snapshot.PlayerId;
        var index = Release1MissionCatalog.IndexOf(missionKey);
        var mission = state.Missions[index];
        var acceptReceipt = $"{missionKey}-accept-composition";
        var accept = new Release1StoryCommand(
            sessionEpoch, loadEpoch, playerId, missionKey, mission.Attempt, Release1TransitionKind.MissionAccepted,
            acceptReceipt,
            Release1LogicalCorrelation.Create(playerId, missionKey, mission.Attempt, Release1TransitionKind.MissionAccepted, acceptReceipt).Value,
            "terms-v1");
        var accepted = Release1StoryTransitions.Apply(state, accept);
        if (!accepted.Accepted) throw new InvalidOperationException($"Satisfy accept fixture setup failed for {missionKey}: {accepted.Message}");
        var activateReceipt = $"{missionKey}-activate-composition";
        var activate = new Release1StoryCommand(
            sessionEpoch, loadEpoch, playerId, missionKey, mission.Attempt, Release1TransitionKind.MissionActivated,
            activateReceipt,
            Release1LogicalCorrelation.Create(playerId, missionKey, mission.Attempt, Release1TransitionKind.MissionActivated, activateReceipt).Value);
        var activated = Release1StoryTransitions.Apply(accepted.State!, activate);
        if (!activated.Accepted) throw new InvalidOperationException($"Satisfy activate fixture setup failed for {missionKey}: {activated.Message}");
        var completeReceipt = $"{missionKey}-complete-composition";
        var complete = new Release1StoryCommand(
            sessionEpoch, loadEpoch, playerId, missionKey, mission.Attempt, Release1TransitionKind.MissionCompleted,
            completeReceipt,
            Release1LogicalCorrelation.Create(playerId, missionKey, mission.Attempt, Release1TransitionKind.MissionCompleted, completeReceipt).Value,
            CompletionTiming: Release1CompletionTiming.OnTime,
            RewardAuthorizationReceiptId: $"{missionKey}-reward-composition");
        var completed = Release1StoryTransitions.Apply(activated.State!, complete);
        if (!completed.Accepted) throw new InvalidOperationException($"Satisfy complete fixture setup failed for {missionKey}: {completed.Message}");
        return completed.State!;
    }

    private static Release1StoryRuntimeService NewStory(
        Release1ShortNoticeHarness.FakeContext context, Release1ShortNoticeHarness.FakeRepository repository)
    {
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        return story;
    }

    private static Release1MissionRecord Mission(Release1StoryRuntimeService story) =>
        story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)];

    private static Release1ShortNoticeProgress? Progress(Release1StoryRuntimeService story, Release1ShortNoticeAssignment assignment) =>
        story.State!.ShortNoticeProgress.SingleOrDefault(progress => progress.Attempt == assignment.Attempt);

    private static void Save(Release1StoryRuntimeService story, Release1ShortNoticeComposition composition)
    {
        story.OnSaveStart();
        composition.OnSaveStart();
        story.OnSaveComplete();
        composition.OnSaveComplete();
    }
}
