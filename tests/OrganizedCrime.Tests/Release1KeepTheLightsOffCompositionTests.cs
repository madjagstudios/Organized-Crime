using System.Linq;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// Task 6: drives Keep the Lights Off end to end through the real
/// <see cref="Release1KeepTheLightsOffComposition"/> (the real mission service and the real
/// presenter) against the real story runtime and a fake production world, plus the real
/// <see cref="Release1PresentationPlanner"/> fed the composition's own live view, assignment, and
/// progress, so the quest and message shapes it asserts are the ones the composition actually
/// produces rather than hand built fixtures. There is no drop, no closet, and no product anywhere
/// in this mission any more: the shutdown is driven entirely by whether an owned property's own
/// employees and stations are working.
/// </summary>
public sealed class Release1KeepTheLightsOffCompositionTests
{
    private const double WindowMinutes = Release1KeepTheLightsOffAssignment.WindowGameMinutes;
    private const double GraceMinutes = Release1QuietWindow.BreachGraceGameMinutes;

    [Fact]
    public void Real_composition_drives_the_mission_end_to_end_with_no_reward_and_a_full_reconstruction_changes_nothing_further()
    {
        var repository = new Release1KeepTheLightsOffHarness.FakeRepository();
        var context = new Release1KeepTheLightsOffHarness.FakeContext();
        var world = new Release1KeepTheLightsOffHarness.FakeWorld();
        SetNoEmployeeWorld(world);
        SeedShortNoticeSatisfied(context, repository);
        var story = NewStory(context, repository);

        Release1KeepTheLightsOffAssignment assignment;
        using (var phone = new Release1PhoneCallService(
                   story, new Release1KeepTheLightsOffHarness.FakeQueue(), new Release1KeepTheLightsOffHarness.FakeCue()))
        using (var composition = new Release1KeepTheLightsOffComposition(story, world, phone))
        {
            composition.OnLoadComplete();

            // No employee anywhere the player owns: Nell has nothing to ask the player to stop.
            Assert.Equal(Release1KeepTheLightsOffOfferStatus.Ineligible, composition.Service.OfferStatus);
            Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ineligible, composition.Presenter.TryReview().Status);

            // An employee is assigned at the one owned property: the offer appears on the very next
            // review.
            SetWorkingWorld(world);
            world.TotalMinutes = 6_000d;
            var review = composition.Presenter.TryReview();
            Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ready, review.Status);

            var decision = composition.Presenter.TryAccept();
            Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, decision.Status);

            assignment = story.State!.KeepTheLightsOffAssignments.Single();
            Assert.Equal(Release1MissionState.Active, Mission(story).State);

            // The still working employee holds the census on the very acceptance pass (a baseline
            // pass, but a working employee is decisive regardless): the window never opens, and the
            // quest shows the plain open entry with no marker.
            Assert.Null(Progress(story, assignment.Attempt));
            var openQuest = QuestEntries(story, composition);
            Assert.Equal(Release1DesiredEntryState.Active, openQuest.Entries[0].State);
            Assert.Null(openQuest.Entries[0].Marker);
            Assert.Contains("No production activities from you or employees for 24 hours.", openQuest.Entries[0].Text, StringComparison.Ordinal);

            // The employee keeps working: the card holds on the production line and the window still
            // never starts.
            world.TotalMinutes = 6_010d;
            composition.Update();
            Assert.Null(Progress(story, assignment.Attempt));
            Assert.Equal(Release1MissionState.Active, Mission(story).State);

            // Everyone goes quiet: the confirmed message renders once and the window starts.
            SetQuietWorld(world);
            world.TotalMinutes = 6_050d;
            composition.Update();
            var confirmedAt = Progress(story, assignment.Attempt)!.ClearConfirmedAtGameMinutes;
            Assert.Equal(6_050d, confirmedAt);
            var confirmedPlan = Plan(story, composition);
            Assert.Single(confirmedPlan.Messages, message => message.Text == "All stopped. Keep it this way for 24 hours.");

            // The window elapses clean: the mission completes with no reward and no native effect.
            world.TotalMinutes = confirmedAt!.Value + WindowMinutes;
            composition.Update();

            Assert.Equal(Release1MissionState.Satisfied, Mission(story).State);
            Assert.Equal(0, world.CashChanges);
            Assert.Empty(story.State!.NativeEffects);
            Assert.Equal(70, story.State.Standing);
            Assert.Equal(
                Release1MissionState.Offered,
                story.State.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope)].State);

            var closedPlan = Plan(story, composition);
            Assert.Contains(Release1KeepTheLightsOffPresentation.CompletedText, closedPlan.Messages.Select(message => message.Text));
            var closedQuest = closedPlan.Quests.Single(quest => quest.Key == Release1MissionCatalog.KeepTheLightsOff);
            Assert.Equal(Release1DesiredEntryState.Complete, closedQuest.Entries[0].State);

            Save(story, composition);

            // A further pass after the save changes nothing further.
            composition.Update();
            Assert.Equal(0, world.CashChanges);
            Assert.Empty(story.State!.NativeEffects);
            Assert.Equal(70, story.State.Standing);

            composition.OnPreLoad();
        }
        story.Dispose();

        // A full reconstruction from the persisted sidecar changes nothing further, and a second
        // reconstruction on top of that one still does not either.
        RestoreAndAssertNothingFurther(repository, world, assignment);
        RestoreAndAssertNothingFurther(repository, world, assignment);
    }

    [Fact]
    public void A_breach_inside_the_grace_never_fails_the_stage_through_the_real_composition()
    {
        var repository = new Release1KeepTheLightsOffHarness.FakeRepository();
        var context = new Release1KeepTheLightsOffHarness.FakeContext();
        var world = new Release1KeepTheLightsOffHarness.FakeWorld();
        SeedShortNoticeSatisfied(context, repository);
        var story = NewStory(context, repository);

        using var phone = new Release1PhoneCallService(
            story, new Release1KeepTheLightsOffHarness.FakeQueue(), new Release1KeepTheLightsOffHarness.FakeCue());
        using var composition = new Release1KeepTheLightsOffComposition(story, world, phone);
        composition.OnLoadComplete();
        world.TotalMinutes = 6_000d;
        // The fixture world's default (one owned property, one working employee) is eligible and
        // holds the baseline pass at acceptance without needing to be set explicitly.
        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ready, composition.Presenter.TryReview().Status);
        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);
        var assignment = story.State!.KeepTheLightsOffAssignments.Single();

        SetQuietWorld(world);
        world.TotalMinutes = 6_050d;
        composition.Update();
        var confirmedAt = Progress(story, assignment.Attempt)!.ClearConfirmedAtGameMinutes!.Value;

        // An employee works again during the window, then everything goes quiet again inside the
        // sixty minute grace: no failure, and the original clear confirmation timestamp survives
        // untouched.
        SetWorkingWorld(world);
        world.TotalMinutes = confirmedAt + 100d;
        composition.Update();
        Assert.NotNull(Progress(story, assignment.Attempt)!.BreachSincePassGameMinutes);
        Assert.Equal(Release1MissionState.Active, Mission(story).State);

        SetQuietWorld(world);
        world.TotalMinutes = confirmedAt + 100d + GraceMinutes - 1d;
        composition.Update();

        Assert.Null(Progress(story, assignment.Attempt)!.BreachSincePassGameMinutes);
        Assert.Equal(confirmedAt, Progress(story, assignment.Attempt)!.ClearConfirmedAtGameMinutes);
        Assert.Equal(Release1MissionState.Active, Mission(story).State);

        composition.OnPreLoad();
        story.Dispose();
    }

    [Fact]
    public void A_breach_past_the_grace_fails_once_and_rings_arthur_exactly_once_through_the_real_composition()
    {
        var repository = new Release1KeepTheLightsOffHarness.FakeRepository();
        var context = new Release1KeepTheLightsOffHarness.FakeContext();
        var world = new Release1KeepTheLightsOffHarness.FakeWorld();
        SeedShortNoticeSatisfied(context, repository);
        var story = NewStory(context, repository);

        var queue = new Release1KeepTheLightsOffHarness.FakeQueue();
        using var phone = new Release1PhoneCallService(story, queue, new Release1KeepTheLightsOffHarness.FakeCue());
        using var composition = new Release1KeepTheLightsOffComposition(story, world, phone);
        composition.OnLoadComplete();
        world.TotalMinutes = 6_000d;
        Assert.Equal(Release1KeepTheLightsOffReviewStatus.Ready, composition.Presenter.TryReview().Status);
        Assert.Equal(Release1KeepTheLightsOffDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);
        var assignment = story.State!.KeepTheLightsOffAssignments.Single();
        var standingBefore = story.State!.Standing;

        // Arthur's own prerequisite: Nell already spoke on this attempt (the accepted terms),
        // recorded the same way the real presentation projector records any sent message.
        story.TryRecordPresentationReceipt(Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.KeepTheLightsOff, assignment.Attempt,
            Release1TransitionKind.MissionAccepted, "presentation-nell-ktlo-accepted-v1").Value);

        SetQuietWorld(world);
        world.TotalMinutes = 6_050d;
        composition.Update();
        var confirmedAt = Progress(story, assignment.Attempt)!.ClearConfirmedAtGameMinutes!.Value;

        SetWorkingWorld(world);
        world.TotalMinutes = confirmedAt + 100d;
        composition.Update();
        Assert.Equal(Release1MissionState.Active, Mission(story).State);

        world.TotalMinutes = confirmedAt + 100d + GraceMinutes;
        composition.Update();

        Assert.Equal(Release1MissionState.MakeGoodOffered, Mission(story).State);
        Assert.Equal(Release1MissionOutcome.RequiredFailure, Mission(story).LastOutcome);
        Assert.Equal(standingBefore - 12, story.State!.Standing);
        Assert.Single(Mission(story).PenaltyReceiptIds);
        var call = Assert.Single(queue.Invocations);
        Assert.Equal(Release1PhoneCallRole.Arthur, call.Role);

        // A further pass while the make good offer sits unaccepted rings Arthur no further.
        world.TotalMinutes = confirmedAt + 100d + GraceMinutes + 10d;
        composition.Update();
        Assert.Single(queue.Invocations);

        composition.OnPreLoad();
        story.Dispose();
    }

    private static void RestoreAndAssertNothingFurther(
        Release1KeepTheLightsOffHarness.FakeRepository repository,
        Release1KeepTheLightsOffHarness.FakeWorld world,
        Release1KeepTheLightsOffAssignment assignment)
    {
        var context = new Release1KeepTheLightsOffHarness.FakeContext();
        var story = NewStory(context, repository);
        using var phone = new Release1PhoneCallService(
            story, new Release1KeepTheLightsOffHarness.FakeQueue(), new Release1KeepTheLightsOffHarness.FakeCue());
        using var composition = new Release1KeepTheLightsOffComposition(story, world, phone);
        world.ResetMutationEvidence();

        composition.OnLoadComplete();
        composition.Update();

        Assert.Equal(0, world.CashChanges);
        Assert.Empty(story.State!.NativeEffects);
        Assert.Equal(
            Release1MissionState.Satisfied,
            story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)].State);
        Assert.Equal(assignment, story.State.KeepTheLightsOffAssignments.Single());

        composition.OnPreLoad();
        story.Dispose();
    }

    // Seeds the repository with a story whose Small Courtesy, Wrong Address, Room With No Name, and
    // Short Notice missions are all already Satisfied, through the same pure Release1StoryTransitions
    // path Release1KeepTheLightsOffHarness's own private Satisfy helper uses (that helper is private
    // to its file, so this composition test carries its own copy, the same way
    // Release1ShortNoticeCompositionTests carries its own copy rather than reaching into another
    // file's private helpers).
    private static void SeedShortNoticeSatisfied(
        Release1KeepTheLightsOffHarness.FakeContext context, Release1KeepTheLightsOffHarness.FakeRepository repository)
    {
        var state = Release1StoryState.CreateAccepted(context.Snapshot.PlayerId, Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-keep-the-lights-off-composition").Value);
        state = Satisfy(context, state, Release1MissionCatalog.SmallCourtesy);
        state = Satisfy(context, state, Release1MissionCatalog.WrongAddress);
        state = Satisfy(context, state, Release1MissionCatalog.RoomWithNoName);
        state = Satisfy(context, state, Release1MissionCatalog.ShortNotice);
        repository.Update(state);
    }

    private static Release1StoryState Satisfy(
        Release1KeepTheLightsOffHarness.FakeContext context, Release1StoryState state, string missionKey)
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
        Release1KeepTheLightsOffHarness.FakeContext context, Release1KeepTheLightsOffHarness.FakeRepository repository)
    {
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        return story;
    }

    private static Release1MissionRecord Mission(Release1StoryRuntimeService story) =>
        story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];

    private static Release1KeepTheLightsOffProgress? Progress(Release1StoryRuntimeService story, int attempt) =>
        story.State!.KeepTheLightsOffProgress.SingleOrDefault(progress => progress.Attempt == attempt);

    private static void Save(Release1StoryRuntimeService story, Release1KeepTheLightsOffComposition composition)
    {
        story.OnSaveStart();
        composition.OnSaveStart();
        story.OnSaveComplete();
        composition.OnSaveComplete();
    }

    // Feeds the real Release1PresentationPlanner the composition's own live view, assignment, and
    // progress, so the quest and message shapes asserted here are what the real composition actually
    // produces on this pass, not a hand built fixture.
    private static Release1PresentationPlan Plan(Release1StoryRuntimeService story, Release1KeepTheLightsOffComposition composition)
    {
        var mission = Mission(story);
        var assignment = story.State!.KeepTheLightsOffAssignments.SingleOrDefault(candidate => candidate.Attempt == mission.Attempt);
        var progress = Progress(story, mission.Attempt);
        var inputs = new Release1PresentationInputs(
            story.State, false, false, null, null,
            KeepTheLightsOff: composition.Presenter.View,
            KeepTheLightsOffAssignment: assignment,
            KeepTheLightsOffProgress: progress);
        return Release1PresentationPlanner.Build(inputs);
    }

    private static Release1DesiredQuest QuestEntries(Release1StoryRuntimeService story, Release1KeepTheLightsOffComposition composition) =>
        Plan(story, composition).Quests.Single(quest => quest.Key == Release1MissionCatalog.KeepTheLightsOff);

    private static void SetNoEmployeeWorld(Release1KeepTheLightsOffHarness.FakeWorld world, int properties = 1) =>
        world.Activity = Release1KeepTheLightsOffHarness.ProductionWorld(properties, working: false, withEmployee: false);

    private static void SetQuietWorld(Release1KeepTheLightsOffHarness.FakeWorld world, int properties = 1) =>
        world.Activity = Release1KeepTheLightsOffHarness.ProductionWorld(properties, working: false, withEmployee: true);

    private static void SetWorkingWorld(Release1KeepTheLightsOffHarness.FakeWorld world, int properties = 1) =>
        world.Activity = Release1KeepTheLightsOffHarness.ProductionWorld(properties, working: true, withEmployee: true);
}
