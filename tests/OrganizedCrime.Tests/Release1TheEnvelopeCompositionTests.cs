using System.Linq;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// Drives The Envelope end to end through the real <see cref="Release1TheEnvelopeComposition"/>, the
/// real <see cref="Release1TheEnvelopeMissionService"/> and <see cref="Release1TheEnvelopePresenter"/>
/// underneath it, and a fake world whose Syndicate HQ hold room is Ready with nine closets. Reuses
/// <see cref="Release1TheEnvelopeHarness"/>'s fakes (its assignment reaches every prior mission's own
/// composition test file the same way), but carries its own copy of the prior-mission seeding helper,
/// exactly as <c>Release1ShortNoticeCompositionTests</c> does, since that helper is private to the
/// harness's own file.
/// </summary>
public sealed class Release1TheEnvelopeCompositionTests
{
    [Fact]
    public void Real_composition_drives_the_whole_mission_end_to_end_through_recovery_and_a_full_reconstruction_pays_nothing_further()
    {
        var repository = new Release1TheEnvelopeHarness.FakeRepository();
        var context = new Release1TheEnvelopeHarness.FakeContext();
        var world = new Release1TheEnvelopeHarness.FakeWorld();
        SeedKeepTheLightsOffSatisfied(context, repository);
        var story = NewStory(context, repository);
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];

        Release1TheEnvelopeAssignment assignment;
        using (var phone = new Release1PhoneCallService(
                   story, new Release1TheEnvelopeHarness.FakeQueue(), new Release1TheEnvelopeHarness.FakeCue()))
        using (var composition = new Release1TheEnvelopeComposition(story, world, phone))
        {
            composition.OnLoadComplete();
            Assert.Equal(Release1TheEnvelopeOfferStatus.Available, composition.Service.OfferStatus);

            // The primary window lapses with nothing deposited, then the make-good window lapses too,
            // both through the same real review/accept path every tier uses, so recovery is reached
            // exactly the way play would reach it, not seeded directly.
            world.TotalMinutes = 6_000d;
            Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, composition.Presenter.TryReview().Status);
            Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);
            world.TotalMinutes = 200d * 60d;
            composition.Update();
            composition.Update();
            Assert.Equal(Release1MissionState.MakeGoodOffered, Mission(story).State);
            Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, composition.Presenter.TryReview().Status);
            Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);
            Assert.Equal(Release1MissionState.MakeGoodActive, Mission(story).State);
            world.TotalMinutes = 400d * 60d;
            composition.Update();
            composition.Update();
            Assert.Equal(Release1MissionState.RecoveryAvailable, Mission(story).State);
            Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, composition.Presenter.TryReview().Status);
            Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);
            Assert.Equal(Release1MissionState.RecoveryActive, Mission(story).State);

            assignment = story.State!.TheEnvelopeAssignments.Single(candidate =>
                candidate.Mode == Release1TheEnvelopeAssignmentMode.Recovery);
            Assert.Equal(5_000d, assignment.AmountWholeDollars);

            // Three of the five thousand-dollar stacks the recovery amount needs: a shortfall of two
            // thousand, noticed once and rendered as the shortfall card.
            world.SetClosetCash(closetA, 0, 1_000f);
            world.SetClosetCash(closetA, 1, 1_000f);
            world.SetClosetCash(closetA, 2, 1_000f);
            world.TotalMinutes += 1d;
            composition.Update();
            Assert.Equal(2_000d, Progress(story, assignment)!.LastShortfallNoticed);
            Assert.Empty(world.ClosetCashChangeCalls);
            Assert.Equal(Release1TheEnvelopeCardStage.Shortfall, composition.Presenter.View.Stage);
            Assert.Contains("2000", composition.Presenter.View.Body);

            // A second pass at the same shortfall does not re-notice it.
            world.TotalMinutes += 1d;
            composition.Update();
            Assert.Equal(2_000d, Progress(story, assignment)!.LastShortfallNoticed);

            // Topping the last two stacks up to the frozen amount consumes the whole plan in one pass
            // and completes the stage.
            world.SetClosetCash(closetA, 3, 1_000f);
            world.SetClosetCash(closetA, 4, 1_000f);
            world.TotalMinutes += 1d;
            composition.Update();

            Assert.Equal(Release1MissionState.Satisfied, Mission(story).State);
            for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
                Assert.Null(world.ReadClosetCash(closetA, slotIndex));
            Assert.Equal(5, world.ClosetCashChangeCalls.Count);
            Assert.Equal(0, world.WalletCashChangeCalls);
            Assert.Null(Progress(story, assignment)!.LastShortfallNoticed);
            Assert.Equal(80, story.State!.Standing);
            Assert.True(story.State.Release1Recognized);
            Assert.Single(story.State.RecognitionLogicalCorrelationIds);

            Save(story, composition);

            Assert.Equal(
                Release1NativeEffectPhase.Committed,
                story.State!.NativeEffects.Single(effect =>
                    effect.MissionKey == Release1MissionCatalog.TheEnvelope && effect.EffectKind == "CashTransfer").Phase);

            composition.OnPreLoad();
        }
        story.Dispose();

        // A full reconstruction from the persisted sidecar consumes and pays nothing further, and a
        // second reconstruction on top of that one still does neither.
        RestoreAndAssertNothingFurther(repository, world, assignment, closetA);
        RestoreAndAssertNothingFurther(repository, world, assignment, closetA);
    }

    [Fact]
    public void A_spread_across_two_closets_holds_and_consumes_nothing_through_the_real_composition()
    {
        var repository = new Release1TheEnvelopeHarness.FakeRepository();
        var context = new Release1TheEnvelopeHarness.FakeContext();
        var world = new Release1TheEnvelopeHarness.FakeWorld();
        SeedKeepTheLightsOffSatisfied(context, repository);
        var story = NewStory(context, repository);

        using var phone = new Release1PhoneCallService(
            story, new Release1TheEnvelopeHarness.FakeQueue(), new Release1TheEnvelopeHarness.FakeCue());
        using var composition = new Release1TheEnvelopeComposition(story, world, phone);
        composition.OnLoadComplete();
        world.TotalMinutes = 6_000d;
        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, composition.Presenter.TryReview().Status);
        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);
        var assignment = story.State!.TheEnvelopeAssignments.Single();

        // The same total split across two closets: one stack in each closet never consumes, regardless
        // of how much either closet carries, because the deposit resolves exactly one closet.
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        var closetB = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[1];
        world.SetClosetCash(closetA, 0, 1_000f);
        world.SetClosetCash(closetB, 0, 1_000f);

        composition.Update();

        Assert.True(Progress(story, assignment)!.SpreadNoticed);
        Assert.Empty(world.ClosetCashChangeCalls);
        Assert.Equal(0, world.WalletCashChangeCalls);
        Assert.Equal(Release1MissionState.Active, Mission(story).State);
        Assert.Equal(Release1TheEnvelopeCardStage.Spread, composition.Presenter.View.Stage);

        composition.OnPreLoad();
        story.Dispose();
    }

    [Fact]
    public void A_lapsed_primary_window_with_a_partial_deposit_leaves_A_slots_0_1_2_at_1000_each()
    {
        var repository = new Release1TheEnvelopeHarness.FakeRepository();
        var context = new Release1TheEnvelopeHarness.FakeContext();
        var world = new Release1TheEnvelopeHarness.FakeWorld();
        SeedKeepTheLightsOffSatisfied(context, repository);
        var story = NewStory(context, repository);

        using var phone = new Release1PhoneCallService(
            story, new Release1TheEnvelopeHarness.FakeQueue(), new Release1TheEnvelopeHarness.FakeCue());
        using var composition = new Release1TheEnvelopeComposition(story, world, phone);
        composition.OnLoadComplete();
        world.TotalMinutes = 6_000d;
        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, composition.Presenter.TryReview().Status);
        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);
        var assignment = story.State!.TheEnvelopeAssignments.Single();

        // Three of the twenty thousand-dollar stacks land, short of the frozen primary amount, then
        // the twenty four hour window lapses.
        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        world.SetClosetCash(closetA, 0, 1_000f);
        world.SetClosetCash(closetA, 1, 1_000f);
        world.SetClosetCash(closetA, 2, 1_000f);
        composition.Update();
        Assert.Equal(17_000d, Progress(story, assignment)!.LastShortfallNoticed);

        world.TotalMinutes = 200d * 60d;
        composition.Update();
        composition.Update();

        Assert.Equal(Release1MissionState.MakeGoodOffered, Mission(story).State);
        Assert.Empty(world.ClosetCashChangeCalls);
        Assert.Equal(0, world.WalletCashChangeCalls);
        Assert.Equal(1_000f, world.ReadClosetCash(closetA, 0));
        Assert.Equal(1_000f, world.ReadClosetCash(closetA, 1));
        Assert.Equal(1_000f, world.ReadClosetCash(closetA, 2));

        composition.OnPreLoad();
        story.Dispose();
    }

    [Fact]
    public void A_make_good_completion_reaches_Release1Recognized_exactly_once_through_the_real_composition()
    {
        var repository = new Release1TheEnvelopeHarness.FakeRepository();
        var context = new Release1TheEnvelopeHarness.FakeContext();
        var world = new Release1TheEnvelopeHarness.FakeWorld();
        SeedKeepTheLightsOffSatisfied(context, repository);
        var story = NewStory(context, repository);

        using var phone = new Release1PhoneCallService(
            story, new Release1TheEnvelopeHarness.FakeQueue(), new Release1TheEnvelopeHarness.FakeCue());
        using var composition = new Release1TheEnvelopeComposition(story, world, phone);
        composition.OnLoadComplete();
        world.TotalMinutes = 6_000d;
        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, composition.Presenter.TryReview().Status);
        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);

        // The primary window lapses with nothing deposited.
        world.TotalMinutes = 200d * 60d;
        composition.Update();
        composition.Update();
        Assert.Equal(Release1MissionState.MakeGoodOffered, Mission(story).State);

        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, composition.Presenter.TryReview().Status);
        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, Mission(story).State);
        var makeGood = story.State!.TheEnvelopeAssignments.Single(candidate =>
            candidate.Mode == Release1TheEnvelopeAssignmentMode.MakeGood);

        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        world.SetClosetCash(closetA, 0, (float)makeGood.AmountWholeDollars);
        composition.Update();

        Assert.Equal(Release1MissionState.Satisfied, Mission(story).State);
        Assert.True(story.State!.Release1Recognized);
        Assert.Single(story.State.RecognitionLogicalCorrelationIds);
        Assert.Equal(0, world.WalletCashChangeCalls);
        Assert.Equal(80, story.State.Standing);

        // A further pass over the already-Satisfied attempt never re-fires recognition.
        composition.Update();
        Assert.Single(story.State.RecognitionLogicalCorrelationIds);

        composition.OnPreLoad();
        story.Dispose();
    }

    [Fact]
    public void A_recovery_completion_reaches_Release1Recognized_exactly_once_through_the_real_composition()
    {
        var repository = new Release1TheEnvelopeHarness.FakeRepository();
        var context = new Release1TheEnvelopeHarness.FakeContext();
        var world = new Release1TheEnvelopeHarness.FakeWorld();
        SeedKeepTheLightsOffSatisfied(context, repository);
        var story = NewStory(context, repository);

        using var phone = new Release1PhoneCallService(
            story, new Release1TheEnvelopeHarness.FakeQueue(), new Release1TheEnvelopeHarness.FakeCue());
        using var composition = new Release1TheEnvelopeComposition(story, world, phone);
        composition.OnLoadComplete();
        world.TotalMinutes = 6_000d;
        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, composition.Presenter.TryReview().Status);
        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);

        // The primary window lapses, the make-good is accepted, and the make-good window lapses too.
        world.TotalMinutes = 200d * 60d;
        composition.Update();
        composition.Update();
        Assert.Equal(Release1MissionState.MakeGoodOffered, Mission(story).State);
        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, composition.Presenter.TryReview().Status);
        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);
        Assert.Equal(Release1MissionState.MakeGoodActive, Mission(story).State);

        world.TotalMinutes = 400d * 60d;
        composition.Update();
        composition.Update();
        Assert.Equal(Release1MissionState.RecoveryAvailable, Mission(story).State);

        var recoveryReview = composition.Presenter.TryReview();
        Assert.Equal(Release1TheEnvelopeReviewStatus.Ready, recoveryReview.Status);
        Assert.Null(recoveryReview.Quote!.DeadlineDurationHours);
        Assert.Equal(Release1TheEnvelopeDecisionStatus.Accepted, composition.Presenter.TryAccept().Status);
        Assert.Equal(Release1MissionState.RecoveryActive, Mission(story).State);
        var recovery = story.State!.TheEnvelopeAssignments.Single(candidate =>
            candidate.Mode == Release1TheEnvelopeAssignmentMode.Recovery);

        var closetA = Release1TheEnvelopeHarness.FakeWorld.AllClosetGuids[0];
        world.SetClosetCash(closetA, 0, (float)recovery.AmountWholeDollars);
        composition.Update();

        Assert.Equal(Release1MissionState.Satisfied, Mission(story).State);
        Assert.True(story.State!.Release1Recognized);
        Assert.Single(story.State.RecognitionLogicalCorrelationIds);
        Assert.Equal(0, world.WalletCashChangeCalls);
        Assert.Equal(80, story.State.Standing);

        // A further pass over the already-Satisfied attempt never re-fires recognition.
        composition.Update();
        Assert.Single(story.State.RecognitionLogicalCorrelationIds);

        composition.OnPreLoad();
        story.Dispose();
    }

    private static void RestoreAndAssertNothingFurther(
        Release1TheEnvelopeHarness.FakeRepository repository,
        Release1TheEnvelopeHarness.FakeWorld world,
        Release1TheEnvelopeAssignment assignment,
        string closetA)
    {
        var context = new Release1TheEnvelopeHarness.FakeContext();
        var story = NewStory(context, repository);
        using var phone = new Release1PhoneCallService(
            story, new Release1TheEnvelopeHarness.FakeQueue(), new Release1TheEnvelopeHarness.FakeCue());
        using var composition = new Release1TheEnvelopeComposition(story, world, phone);
        world.ResetMutationEvidence();

        composition.OnLoadComplete();
        composition.Update();

        Assert.Empty(world.ClosetCashChangeCalls);
        Assert.Equal(0, world.WalletCashChangeCalls);
        Assert.Equal(
            Release1MissionState.Satisfied,
            story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope)].State);
        Assert.Equal(assignment, story.State.TheEnvelopeAssignments.Single(candidate => candidate.Mode == assignment.Mode));
        for (var slotIndex = 0; slotIndex <= 4; slotIndex++)
            Assert.Null(world.ReadClosetCash(closetA, slotIndex));

        composition.OnPreLoad();
        story.Dispose();
    }

    // Seeds the repository with a story whose Small Courtesy, Wrong Address, Room With No Name,
    // Short Notice, and Keep the Lights Off missions are all already Satisfied, through the same
    // pure Release1StoryTransitions path Release1TheEnvelopeHarness's own private Satisfy helper
    // uses (that helper is private to its file, so this composition test carries its own copy, the
    // same way Release1ShortNoticeCompositionTests carries its own copy rather than reaching into
    // another file's private helpers). Keep the Lights Off has no mission service on this branch (it
    // lands on a separate branch), so it is satisfied the same generic way as every other prior
    // mission here, never through a working service of its own.
    private static void SeedKeepTheLightsOffSatisfied(
        Release1TheEnvelopeHarness.FakeContext context, Release1TheEnvelopeHarness.FakeRepository repository)
    {
        var state = Release1StoryState.CreateAccepted(context.Snapshot.PlayerId, Release1LogicalCorrelation.Create(
            context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-the-envelope-composition").Value);
        state = Satisfy(context, state, Release1MissionCatalog.SmallCourtesy);
        state = Satisfy(context, state, Release1MissionCatalog.WrongAddress);
        state = Satisfy(context, state, Release1MissionCatalog.RoomWithNoName);
        state = Satisfy(context, state, Release1MissionCatalog.ShortNotice);
        state = Satisfy(context, state, Release1MissionCatalog.KeepTheLightsOff);
        repository.Update(state);
    }

    private static Release1StoryState Satisfy(
        Release1TheEnvelopeHarness.FakeContext context, Release1StoryState state, string missionKey)
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
        Release1TheEnvelopeHarness.FakeContext context, Release1TheEnvelopeHarness.FakeRepository repository)
    {
        var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        return story;
    }

    private static Release1MissionRecord Mission(Release1StoryRuntimeService story) =>
        story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope)];

    private static Release1TheEnvelopeProgress? Progress(Release1StoryRuntimeService story, Release1TheEnvelopeAssignment assignment) =>
        story.State!.TheEnvelopeProgress.SingleOrDefault(progress => progress.Attempt == assignment.Attempt);

    private static void Save(Release1StoryRuntimeService story, Release1TheEnvelopeComposition composition)
    {
        story.OnSaveStart();
        composition.OnSaveStart();
        story.OnSaveComplete();
        composition.OnSaveComplete();
    }
}
