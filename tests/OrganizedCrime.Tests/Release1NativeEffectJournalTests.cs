using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1NativeEffectJournalTests
{
    [Fact]
    public void Effect_moves_prepared_applied_committed_without_blind_retry()
    {
        var effects = Array.Empty<Release1NativeEffectJournalEntry>();
        var correlation = Release1LogicalCorrelation.Create("76561190000000001", Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionCompleted, "transition-receipt-1").Value;
        var prepared = Apply(effects, new(Release1NativeEffectCommandKind.Prepare, "effect-1", Release1MissionCatalog.SmallCourtesy, 1, "CargoTransfer", "source", "destination", "cargo", StoryCorrelationId: correlation));
        Assert.Equal(Release1NativeEffectPhase.Prepared, prepared.Effects.Single().Phase);
        var applied = Apply(prepared.Effects, new(Release1NativeEffectCommandKind.MarkApplied, "effect-1", Release1MissionCatalog.SmallCourtesy, 1, "CargoTransfer", "source", "destination", "cargo", "native-receipt-1"));
        Assert.Equal(Release1NativeEffectPhase.Applied, applied.Effects.Single().Phase);
        var committed = Apply(applied.Effects, new(Release1NativeEffectCommandKind.Commit, "effect-1", Release1MissionCatalog.SmallCourtesy, 1, "CargoTransfer", "source", "destination", "cargo", "native-receipt-1", correlation));
        Assert.Equal(Release1NativeEffectPhase.Committed, committed.Effects.Single().Phase);
    }

    [Fact]
    public void Duplicate_prepare_is_idempotent_but_conflicting_id_rejects()
    {
        var authorization = Release1LogicalCorrelation.Create("76561190000000001", Release1MissionCatalog.SmallCourtesy, 1, Release1TransitionKind.MissionActivated, "activate").Value;
        var command = new Release1NativeEffectCommand(Release1NativeEffectCommandKind.Prepare, "effect-1", Release1MissionCatalog.SmallCourtesy, 1, "CargoTransfer", "source", "destination", "cargo", StoryCorrelationId: authorization);
        var first = Apply(Array.Empty<Release1NativeEffectJournalEntry>(), command);
        var duplicate = Apply(first.Effects, command);
        Assert.True(duplicate.Accepted);
        Assert.True(duplicate.Idempotent);
        var conflict = Apply(first.Effects, command with { AmountOrCargoIdentity = "different" });
        Assert.False(conflict.Accepted);
        Assert.Equal(Release1NativeEffectTransitionRejectReason.DuplicateConflict, conflict.RejectReason);
    }

    [Fact]
    public void Completing_a_mission_can_prepare_an_outbound_effect_at_resulting_revision()
    {
        var state = Release1StoryState.CreateAccepted("76561190000000001", "oc10/v1/76561190000000001/release1.intro/0/IntroAccepted/intro-1");
        var accept = Cmd(state.Missions[0], Release1TransitionKind.MissionAccepted, "accept", terms: "v1");
        state = Release1StoryTransitions.Apply(state, accept).State!;
        state = Release1StoryTransitions.Apply(state, Cmd(state.Missions[0], Release1TransitionKind.MissionActivated, "activate")).State!;
        var effect = new Release1NativeEffectJournalEntry("reward-effect", Release1MissionCatalog.SmallCourtesy, state.Missions[0].Attempt, "Reward", "oc", "native", "cargo", Release1NativeEffectPhase.Prepared, null, 0);
        state = Release1StoryTransitions.Apply(state, Cmd(state.Missions[0], Release1TransitionKind.MissionCompleted, "complete", timing: Release1CompletionTiming.OnTime, reward: "reward", effect: effect)).State!;
        Assert.Equal(Release1NativeEffectPhase.Prepared, state.NativeEffects.Single().Phase);
        Assert.Equal(state.Revision, state.NativeEffects.Single().PreparedStoryRevision);
    }

    private static Release1NativeEffectTransitionResult Apply(IReadOnlyList<Release1NativeEffectJournalEntry> effects, Release1NativeEffectCommand command) => Release1NativeEffectJournal.Apply(effects, command, 1);
    private static Release1StoryCommand Cmd(Release1MissionRecord mission, Release1TransitionKind kind, string receipt, string? terms = null, Release1CompletionTiming? timing = null, string? reward = null, Release1NativeEffectJournalEntry? effect = null) => new(Guid.Empty, 1, "76561190000000001", mission.MissionKey, mission.Attempt, kind, receipt, Release1LogicalCorrelation.Create("76561190000000001", mission.MissionKey, mission.Attempt, kind, receipt).Value, terms, null, null, timing, reward, null, null, effect);
}
