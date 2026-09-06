using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1StoryRecoveryTests
{
    private const string Player = "76561190000000001";
    private static readonly Guid Session = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Required_failure_then_make_good_failure_caps_penalty_and_opens_recovery()
    {
        var state = ActiveMissionState(20);
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.RequiredFailure, "payload-lost")).State!;
        Assert.Equal(8, state.Standing);
        Assert.Equal(12, state.Missions[0].StandingPenaltyApplied);
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodAccepted, "make-good-accepted", accepted: 1, deadline: 2)).State!;
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodFailed, "make-good-failed")).State!;
        Assert.Equal(0, state.Standing);
        Assert.Equal(20, state.Missions[0].StandingPenaltyApplied);
        Assert.Equal(1, state.Missions[0].MakeGoodFailures);
        Assert.Equal(Release1MissionState.RecoveryAvailable, state.Missions[0].State);
    }

    [Fact]
    public void Recovery_is_penalty_free_and_success_restores_then_awards_once()
    {
        var state = RecoveryAvailableState(0, 20);
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.RecoveryAccepted, "recovery-accepted")).State!;
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.RecoveryFailed, "recovery-1")).State!;
        Assert.Equal(0, state.Standing);
        Assert.Equal(20, state.Missions[0].StandingPenaltyApplied);
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.RecoveryAccepted, "recovery-accepted-2")).State!;
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionCompleted, "reward-1", timing: Release1CompletionTiming.OnTime, reward: "reward-auth")).State!;
        Assert.Equal(30, state.Standing);
        Assert.Equal(0, state.Missions[0].StandingPenaltyApplied);
        var replay = Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionCompleted, "reward-1", timing: Release1CompletionTiming.OnTime, reward: "reward-auth"));
        Assert.False(replay.Changed);
        Assert.Same(state, replay.State);
    }

    [Fact]
    public void Abandon_penalty_and_make_good_failure_are_bounded_and_recovery_has_no_deadline()
    {
        var state = ActiveMissionState(20);
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionAbandoned, "abandon")).State!;
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodOffered, "offer-good")).State!;
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodAccepted, "accept-good")).State!;
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MakeGoodFailed, "fail-good")).State!;
        Assert.Equal(18, state.Missions[0].StandingPenaltyApplied);
        Assert.Null(state.Missions[0].DeadlineGameTimeHours);
        Assert.Equal(Release1MissionState.RecoveryAvailable, state.Missions[0].State);
    }

    private static Release1StoryState ActiveMissionState(int standing) =>
        AcceptAndActivate(Release1StoryState.CreateAccepted(Player, "oc10/v1/76561190000000001/release1.intro/0/IntroAccepted/intro-1"), standing);

    private static Release1StoryState RecoveryAvailableState(int standing, int penalty)
    {
        var state = ActiveMissionState(standing);
        var mission = state.Missions[0] with { State = Release1MissionState.RecoveryAvailable, StandingPenaltyApplied = penalty, RecoveryMode = Release1RecoveryMode.GuaranteedRecovery };
        return state with { Missions = state.Missions.Select((m, i) => i == 0 ? mission : m).ToArray() };
    }

    private static Release1StoryState AcceptAndActivate(Release1StoryState state, int standing)
    {
        state = state with { Standing = standing };
        state = Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionAccepted, "accept", terms: "v1")).State!;
        return Apply(state, Command(state.Missions[0], Release1TransitionKind.MissionActivated, "activate")).State!;
    }

    private static Release1StoryTransitionResult Apply(Release1StoryState state, Release1StoryCommand command) => Release1StoryTransitions.Apply(state, command);
    private static Release1StoryCommand Command(Release1MissionRecord mission, Release1TransitionKind kind, string receipt, string? terms = null, double? accepted = null, double? deadline = null, Release1CompletionTiming? timing = null, string? reward = null) => new(Session, 1, Player, mission.MissionKey, mission.Attempt, kind, receipt, Release1LogicalCorrelation.Create(Player, mission.MissionKey, mission.Attempt, kind, receipt).Value, terms, accepted, deadline, timing, reward);
}
