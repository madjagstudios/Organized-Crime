using System.Linq;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1WrongAddressCompositionTests
{
    [Fact]
    public void Real_composition_drives_the_whole_mission_end_to_end_and_a_full_reconstruction_pays_nothing_further()
    {
        var repository = new Release1WrongAddressHarness.FakeRepository();
        var context = new Release1WrongAddressHarness.FakeContext();
        var world = new Release1WrongAddressHarness.FakeWorld();
        var story = LoadStory(context, repository);
        AcceptIntro(story, context);
        CompleteSmallCourtesy(story, context);

        Release1WrongAddressAssignment assignment;
        using (var phone = new Release1PhoneCallService(story, new Release1WrongAddressHarness.FakeQueue(), new Release1WrongAddressHarness.FakeCue()))
        using (var composition = new Release1WrongAddressComposition(story, world, phone))
        {
            composition.OnLoadComplete();

            Assert.Equal(Release1WrongAddressOfferStatus.Available, composition.Service.OfferStatus);

            var review = composition.Presenter.TryReview();
            Assert.Equal(Release1WrongAddressReviewStatus.Ready, review.Status);

            world.TotalMinutes = 6_000d;
            var decision = composition.Presenter.TryAccept();
            Assert.Equal(Release1WrongAddressDecisionStatus.Accepted, decision.Status);

            assignment = story.State!.WrongAddressAssignments.Single();
            Assert.Equal(1, world.InsertCount);

            // The player physically takes the package from the source drop.
            world.EmptySlot(assignment.SourceDropGuid, 0);
            composition.Update();
            Assert.True(story.State!.WrongAddressProgress.Single(progress => progress.Attempt == assignment.Attempt).Custody);

            // The player deposits it at the handoff drop.
            world.PlaceExactPackage(assignment, assignment.HandoffDropGuid, slotIndex: 2, value: 1_000f);
            composition.Update();

            Assert.Equal(
                Release1MissionState.Satisfied,
                Mission(story).State);
            Assert.Equal(1, world.InsertCount);
            Assert.Equal(1, world.CashChanges);

            Save(story, composition);

            Assert.Equal(
                Release1NativeEffectPhase.Committed,
                story.State!.NativeEffects.Single(effect => effect.EffectKind == "CargoTransfer").Phase);
            Assert.Equal(
                Release1NativeEffectPhase.Committed,
                story.State.NativeEffects.Single(effect => effect.EffectKind == "Reward").Phase);
            Assert.Equal(40, story.State.Standing);
            Assert.Equal(
                Release1MissionState.Offered,
                story.State.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)].State);

            composition.OnPreLoad();
        }
        story.Dispose();

        // A full reconstruction from the persisted sidecar consumes and pays nothing further, and a
        // second reconstruction on top of that one still consumes and pays nothing further.
        RestoreAndAssertNothingFurther(repository, world, assignment);
        RestoreAndAssertNothingFurther(repository, world, assignment);
    }

    private static void RestoreAndAssertNothingFurther(
        Release1WrongAddressHarness.FakeRepository repository,
        Release1WrongAddressHarness.FakeWorld world,
        Release1WrongAddressAssignment assignment)
    {
        var context = new Release1WrongAddressHarness.FakeContext();
        var story = LoadStory(context, repository);
        using var phone = new Release1PhoneCallService(story, new Release1WrongAddressHarness.FakeQueue(), new Release1WrongAddressHarness.FakeCue());
        using var composition = new Release1WrongAddressComposition(story, world, phone);
        world.ResetMutationEvidence();

        composition.OnLoadComplete();
        composition.Update();

        Assert.Equal(0, world.InsertCount);
        Assert.Equal(0, world.QuantityChanges);
        Assert.Equal(0, world.CashChanges);
        Assert.Equal(
            Release1MissionState.Satisfied,
            story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)].State);
        Assert.Equal(assignment, story.State.WrongAddressAssignments.Single());

        composition.OnPreLoad();
        story.Dispose();
    }

    private static Release1StoryRuntimeService LoadStory(
        Release1WrongAddressHarness.FakeContext context,
        Release1WrongAddressHarness.FakeRepository repository)
    {
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        return story;
    }

    private static void AcceptIntro(Release1StoryRuntimeService story, Release1WrongAddressHarness.FakeContext context)
    {
        const string introReceipt = "intro-wrong-address-composition";
        var correlation = Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, introReceipt).Value;
        var result = story.TryExecuteDurably(new(
            context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
            Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, introReceipt, correlation));
        if (!result.Accepted) throw new InvalidOperationException("Intro acceptance fixture setup failed: " + result.Message);
    }

    private static void CompleteSmallCourtesy(Release1StoryRuntimeService story, Release1WrongAddressHarness.FakeContext context)
    {
        var smallCourtesy = story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        var acceptReceipt = "accept-sc-for-wa-composition";
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
        var completeReceipt = "complete-sc-for-wa-composition";
        var completeCorrelation = Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.SmallCourtesy, smallCourtesy.Attempt, Release1TransitionKind.MissionCompleted, completeReceipt).Value;
        var completeCommand = new Release1StoryCommand(
            context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
            Release1MissionCatalog.SmallCourtesy, smallCourtesy.Attempt, Release1TransitionKind.MissionCompleted, completeReceipt,
            completeCorrelation, CompletionTiming: Release1CompletionTiming.OnTime, RewardAuthorizationReceiptId: "reward-sc-for-wa-composition");
        var completeResult = story.TryExecuteDurably(completeCommand);
        if (!completeResult.Accepted) throw new InvalidOperationException("Small Courtesy completion fixture setup failed: " + completeResult.Message);
    }

    private static Release1MissionRecord Mission(Release1StoryRuntimeService story) =>
        story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)];

    private static void Save(Release1StoryRuntimeService story, Release1WrongAddressComposition composition)
    {
        story.OnSaveStart();
        composition.OnSaveStart();
        story.OnSaveComplete();
        composition.OnSaveComplete();
    }
}
