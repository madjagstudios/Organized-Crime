using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1StoryTransitionTests
{
    private static readonly Guid Session = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Player = "76561190000000001";

    [Fact]
    public void Correlation_is_restart_stable_and_has_no_load_epoch()
    {
        var correlation = Release1LogicalCorrelation.Create(
            Player, Release1MissionCatalog.SmallCourtesy, 1,
            Release1TransitionKind.MissionAccepted, "receipt-1");

        Assert.Equal(
            "oc10/v1/76561190000000001/release1.small-courtesy/1/MissionAccepted/receipt-1",
            correlation.Value);
        Assert.True(Release1LogicalCorrelation.TryParse(correlation.Value, out var parsed));
        Assert.Equal(1, parsed.Attempt);
        Assert.DoesNotContain("Epoch", correlation.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_path_reaches_twenty_forty_sixty_eighty_and_recognition()
    {
        Release1StoryState? state = null;
        state = Apply(state, IntroAccepted()).State;
        Assert.Equal(20, state!.Standing);

        for (var index = 0; index < Release1MissionCatalog.All.Count; index++)
        {
            var mission = state!.Missions[index];
            state = Apply(state, Command(mission, Release1TransitionKind.MissionAccepted, "accept-" + index, terms: "v1", accepted: 1, deadline: 2)).State;
            state = Apply(state, Command(state!.Missions[index], Release1TransitionKind.MissionActivated, "activate-" + index)).State;
            state = Apply(state, Command(state!.Missions[index], Release1TransitionKind.MissionCompleted, "complete-" + index,
                timing: Release1CompletionTiming.OnTime, reward: "reward-" + index,
                recognition: index == 5 ? "recognize-6" : null)).State;

            if (index == 1) Assert.Equal(40, state!.Standing);
            if (index == 3) Assert.Equal(60, state!.Standing);
        }

        Assert.Equal(80, state!.Standing);
        Assert.True(state.Release1Recognized);
        Assert.All(state.Missions, mission => Assert.Equal(Release1MissionState.Satisfied, mission.State));
        Assert.Equal(6, state.Missions.Count(mission => mission.RewardAuthorizationReceiptId is not null));
        Assert.Single(state.RecognitionLogicalCorrelationIds);
    }

    [Fact]
    public void Duplicate_correlation_is_idempotent_without_revision_churn()
    {
        var first = Apply(null, IntroAccepted());
        var replay = Apply(first.State, IntroAccepted());
        Assert.True(replay.Accepted);
        Assert.True(replay.Idempotent);
        Assert.Same(first.State, replay.State);
        Assert.Equal(first.State!.Revision, replay.State!.Revision);
    }

    [Fact]
    public void Out_of_order_and_satisfied_replays_are_rejected_or_idempotent()
    {
        var state = Apply(null, IntroAccepted()).State!;
        var outOfOrder = Apply(state, Command(state.Missions[1], Release1TransitionKind.MissionAccepted, "accept-2", terms: "v1"));
        Assert.False(outOfOrder.Accepted);
        Assert.Equal(Release1StoryTransitionRejectReason.WrongState, outOfOrder.RejectReason);
    }

    private static Release1StoryTransitionResult Apply(Release1StoryState? state, Release1StoryCommand command) =>
        Release1StoryTransitions.Apply(state, command);

    private static Release1StoryCommand IntroAccepted() => new(
        Session, 7, Player, Release1MissionCatalog.IntroScopeKey, 0,
        Release1TransitionKind.IntroAccepted, "intro-1",
        "oc10/v1/76561190000000001/release1.intro/0/IntroAccepted/intro-1");

    private static Release1StoryCommand Command(Release1MissionRecord mission, Release1TransitionKind kind, string receipt,
        string? terms = null, double? accepted = null, double? deadline = null,
        Release1CompletionTiming? timing = null, string? reward = null, string? recognition = null) =>
        new(Session, 7, Player, mission.MissionKey, mission.Attempt, kind, receipt,
            Release1LogicalCorrelation.Create(Player, mission.MissionKey, mission.Attempt, kind, receipt).Value,
            terms, accepted, deadline, timing, reward, null, recognition);
}
