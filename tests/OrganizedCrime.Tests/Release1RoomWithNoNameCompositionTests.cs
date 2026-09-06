using System.Linq;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1RoomWithNoNameCompositionTests
{
    [Fact]
    public void Real_composition_drives_the_whole_mission_end_to_end_and_a_full_reconstruction_pays_nothing_further()
    {
        var repository = new Release1RoomWithNoNameHarness.FakeRepository();
        var context = new Release1RoomWithNoNameHarness.FakeContext();
        var world = new Release1RoomWithNoNameHarness.FakeWorld();
        SeedWrongAddressSatisfied(context, repository);
        var story = NewStory(context, repository);

        Release1RoomWithNoNameAssignment assignment;
        using (var phone = new Release1PhoneCallService(
                   story, new Release1RoomWithNoNameHarness.FakeQueue(), new Release1RoomWithNoNameHarness.FakeCue()))
        using (var composition = new Release1RoomWithNoNameComposition(story, world, phone))
        {
            composition.OnLoadComplete();

            Assert.Equal(Release1RoomWithNoNameOfferStatus.Available, composition.Service.OfferStatus);

            var review = composition.Presenter.TryReview();
            Assert.Equal(Release1RoomWithNoNameReviewStatus.Ready, review.Status);

            world.TotalMinutes = 6_000d;
            var decision = composition.Presenter.TryAccept();
            Assert.Equal(Release1RoomWithNoNameDecisionStatus.Accepted, decision.Status);

            assignment = story.State!.RoomWithNoNameAssignments.Single();
            Assert.Equal(1, world.InsertCount);

            // The player physically takes the consignment from the pick up drop.
            world.EmptySlot(assignment.SourceDropGuid, 0);
            composition.Update();
            Assert.True(story.State!.RoomWithNoNameProgress.Single(progress => progress.Attempt == assignment.Attempt).Custody);

            // The player stows it in an HQ closet.
            world.StowIntoCloset(assignment, Release1RoomWithNoNameHarness.ClosetA);
            composition.Update();
            Assert.True(story.State!.RoomWithNoNameProgress.Single(progress => progress.Attempt == assignment.Attempt).Stowed);

            // One in-game day passes; the hold releases and Nell opens the hand off.
            world.TotalMinutes += Release1RoomWithNoNameAssignment.OneInGameDayMinutes;
            composition.Update();
            Assert.True(story.State!.RoomWithNoNameProgress.Single(progress => progress.Attempt == assignment.Attempt).HoldSatisfied);

            // The player deposits it at the hand off drop; delivery consumes and pays in one pass.
            world.PlaceExactPackage(assignment, assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
            composition.Update();

            Assert.Equal(Release1MissionState.Satisfied, Mission(story).State);
            Assert.Equal(1, world.InsertCount);
            Assert.Equal(1, world.CashChanges);

            Save(story, composition);

            Assert.Equal(
                Release1NativeEffectPhase.Committed,
                story.State!.NativeEffects.Single(effect =>
                    effect.MissionKey == Release1MissionCatalog.RoomWithNoName && effect.EffectKind == "CargoTransfer").Phase);
            Assert.Equal(
                Release1NativeEffectPhase.Committed,
                story.State.NativeEffects.Single(effect =>
                    effect.MissionKey == Release1MissionCatalog.RoomWithNoName && effect.EffectKind == "Reward").Phase);
            Assert.Equal(50, story.State.Standing);
            Assert.Equal(
                Release1MissionState.Offered,
                story.State.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)].State);

            composition.OnPreLoad();
        }
        story.Dispose();

        // A full reconstruction from the persisted sidecar consumes and pays nothing further, never
        // writes to a closet again, and a second reconstruction on top of that one still does none of
        // those things.
        RestoreAndAssertNothingFurther(repository, world, assignment);
        RestoreAndAssertNothingFurther(repository, world, assignment);
    }

    private static void RestoreAndAssertNothingFurther(
        Release1RoomWithNoNameHarness.FakeRepository repository,
        Release1RoomWithNoNameHarness.FakeWorld world,
        Release1RoomWithNoNameAssignment assignment)
    {
        var context = new Release1RoomWithNoNameHarness.FakeContext();
        var story = NewStory(context, repository);
        using var phone = new Release1PhoneCallService(
            story, new Release1RoomWithNoNameHarness.FakeQueue(), new Release1RoomWithNoNameHarness.FakeCue());
        using var composition = new Release1RoomWithNoNameComposition(story, world, phone);
        world.ResetMutationEvidence();

        composition.OnLoadComplete();
        composition.Update();

        Assert.Equal(0, world.InsertCount);
        Assert.Equal(0, world.QuantityChanges);
        Assert.Equal(0, world.CashChanges);
        Assert.Equal(0, world.ClosetMutations);
        Assert.Equal(
            Release1MissionState.Satisfied,
            story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)].State);
        Assert.Equal(assignment, story.State.RoomWithNoNameAssignments.Single());

        composition.OnPreLoad();
        story.Dispose();
    }

    // Seeds the repository with a story whose Small Courtesy and Wrong Address missions are both
    // already Satisfied, through the same pure Release1StoryTransitions path the Room With No Name
    // mission-service harness's own SeededStory helper uses (that helper is private to its file, so
    // this composition test carries its own copy, the same way Release1WrongAddressCompositionTests
    // carries its own Small Courtesy setup rather than reaching into another file's private helpers).
    private static void SeedWrongAddressSatisfied(
        Release1RoomWithNoNameHarness.FakeContext context, Release1RoomWithNoNameHarness.FakeRepository repository)
    {
        var state = Release1StoryState.CreateAccepted(context.Snapshot.PlayerId, Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-room-composition").Value);
        state = Satisfy(context, state, Release1MissionCatalog.SmallCourtesy);
        state = Satisfy(context, state, Release1MissionCatalog.WrongAddress);
        repository.Update(state);
    }

    private static Release1StoryState Satisfy(
        Release1RoomWithNoNameHarness.FakeContext context, Release1StoryState state, string missionKey)
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
        Release1RoomWithNoNameHarness.FakeContext context, Release1RoomWithNoNameHarness.FakeRepository repository)
    {
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        return story;
    }

    private static Release1MissionRecord Mission(Release1StoryRuntimeService story) =>
        story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)];

    private static void Save(Release1StoryRuntimeService story, Release1RoomWithNoNameComposition composition)
    {
        story.OnSaveStart();
        composition.OnSaveStart();
        story.OnSaveComplete();
        composition.OnSaveComplete();
    }
}
