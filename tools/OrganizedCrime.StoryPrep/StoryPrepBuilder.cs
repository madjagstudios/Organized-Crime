using OrganizedCrime.Model;
using OrganizedCrime.Runtime;

namespace OrganizedCrime.StoryPrep;

public enum StoryPrepBuildStatus
{
    Succeeded,
    UnknownMissionKey,
    MissionRequiresRecognition,
    TransitionRejected
}

public sealed record StoryPrepBuildResult(StoryPrepBuildStatus Status, Release1StoryState? State, string Message)
{
    public bool Succeeded => Status == StoryPrepBuildStatus.Succeeded;
}

/// <summary>
/// Builds a fresh Release 1 story state, entirely in memory, by driving
/// Release1StoryRuntimeService through the same validated Accept, Activate, Complete mission
/// transitions that tools/OrganizedCrime/Runtime/Release1StoryOwnerQaRecognitionHarness.cs (the
/// in-game F8 owner QA key) uses, but stopping after a chosen mission reaches Satisfied instead of
/// continuing on through every mission to story recognition. The next mission in arc order is left
/// exactly where the runtime puts it on a normal completion (Offered, if there is one), so the game
/// presents it the next time this state is loaded.
///
/// This never passes a recognition receipt, so the produced story is never Release1Recognized. The
/// final mission, The Envelope, cannot be reached this way at all: completing it is the one
/// transition in the whole story engine that requires a recognition receipt
/// (Release1StoryTransitions.CompleteMission), so asking to go "through" it is rejected up front
/// rather than attempted and left to fail mid build.
/// </summary>
public static class StoryPrepBuilder
{
    public const string ReceiptPrefix = "story-prep";
    public const string MissionTermsVersion = "oc48-owner-qa-v1";
    public const string None = "none";

    /// <summary>The values --through accepts, in arc order, with "none" first.</summary>
    public static IReadOnlyList<string> ThroughOptions { get; } =
        new[] { None }
            .Concat(Release1MissionCatalog.All
                .Where(definition => definition.MissionKey != Release1MissionCatalog.TheEnvelope)
                .Select(definition => definition.MissionKey))
            .ToArray();

    public static StoryPrepBuildResult Build(string playerId, string throughMissionKey)
    {
        if (string.IsNullOrWhiteSpace(playerId))
            return new(StoryPrepBuildStatus.TransitionRejected, null, "Player id was empty.");
        if (string.IsNullOrWhiteSpace(throughMissionKey))
            return new(StoryPrepBuildStatus.UnknownMissionKey, null, "No --through mission key was given.");

        var isNone = string.Equals(throughMissionKey, None, StringComparison.OrdinalIgnoreCase);
        if (!isNone && !Release1MissionCatalog.IsMissionKey(throughMissionKey))
            return new(
                StoryPrepBuildStatus.UnknownMissionKey, null,
                $"'{throughMissionKey}' is not a Release 1 mission key. Run --list to see the valid values.");
        if (!isNone && throughMissionKey == Release1MissionCatalog.TheEnvelope)
            return new(
                StoryPrepBuildStatus.MissionRequiresRecognition, null,
                "release1.the-envelope is the final mission in the arc; completing it requires a " +
                "recognition receipt, and this tool never issues one. Use release1.keep-the-lights-off " +
                "(or an earlier mission) instead, and let the game offer The Envelope on load.");

        var context = new StoryPrepHostContext(playerId);
        var repository = new StoryPrepRepository();
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();
        service.OnLoadComplete();

        var intro = Command(
            context.Snapshot, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted,
            $"{ReceiptPrefix}-intro");
        var introResult = service.TryExecute(intro);
        if (!introResult.Accepted)
            return new(StoryPrepBuildStatus.TransitionRejected, null, $"Intro acceptance was rejected: {introResult.Message}");

        if (isNone)
            return new(StoryPrepBuildStatus.Succeeded, service.State, "Story prepared with only the Release 1 relationship accepted.");

        var throughIndex = Release1MissionCatalog.IndexOf(throughMissionKey);
        for (var index = 0; index <= throughIndex; index++)
        {
            var missionKey = Release1MissionCatalog.All[index].MissionKey;
            var sequence = index + 1;

            var accept = MissionCommand(
                context.Snapshot, service.State!.Missions[index], sequence,
                Release1TransitionKind.MissionAccepted, "accepted", termsVersion: MissionTermsVersion);
            var acceptResult = service.TryExecute(accept);
            if (!acceptResult.Accepted)
                return new(StoryPrepBuildStatus.TransitionRejected, null, $"Mission {missionKey} accept was rejected: {acceptResult.Message}");

            var activate = MissionCommand(
                context.Snapshot, service.State!.Missions[index], sequence,
                Release1TransitionKind.MissionActivated, "activated");
            var activateResult = service.TryExecute(activate);
            if (!activateResult.Accepted)
                return new(StoryPrepBuildStatus.TransitionRejected, null, $"Mission {missionKey} activate was rejected: {activateResult.Message}");

            var complete = MissionCommand(
                context.Snapshot, service.State!.Missions[index], sequence,
                Release1TransitionKind.MissionCompleted, "completed",
                completionTiming: Release1CompletionTiming.OnTime,
                rewardReceipt: $"{ReceiptPrefix}-{sequence}-reward");
            var completeResult = service.TryExecute(complete);
            if (!completeResult.Accepted)
                return new(StoryPrepBuildStatus.TransitionRejected, null, $"Mission {missionKey} complete was rejected: {completeResult.Message}");
        }

        return new(StoryPrepBuildStatus.Succeeded, service.State, $"Story prepared through {throughMissionKey}.");
    }

    private static Release1StoryCommand MissionCommand(
        Release1StoryHostContextSnapshot snapshot,
        Release1MissionRecord mission,
        int sequence,
        Release1TransitionKind kind,
        string suffix,
        string? termsVersion = null,
        Release1CompletionTiming? completionTiming = null,
        string? rewardReceipt = null) =>
        Command(
            snapshot, mission.MissionKey, mission.Attempt, kind, $"{ReceiptPrefix}-{sequence}-{suffix}",
            termsVersion, completionTiming, rewardReceipt);

    private static Release1StoryCommand Command(
        Release1StoryHostContextSnapshot snapshot,
        string missionKey,
        int attempt,
        Release1TransitionKind kind,
        string receipt,
        string? termsVersion = null,
        Release1CompletionTiming? completionTiming = null,
        string? rewardReceipt = null) =>
        new(
            snapshot.SessionEpoch,
            snapshot.LoadEpoch,
            snapshot.PlayerId,
            missionKey,
            attempt,
            kind,
            receipt,
            Release1LogicalCorrelation.Create(snapshot.PlayerId, missionKey, attempt, kind, receipt).Value,
            TermsVersion: termsVersion,
            CompletionTiming: completionTiming,
            RewardAuthorizationReceiptId: rewardReceipt);
}
