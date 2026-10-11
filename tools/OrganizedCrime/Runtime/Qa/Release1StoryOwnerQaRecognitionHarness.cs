using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

internal enum Release1StoryOwnerQaRecognitionStatus
{
    Recognized,
    AlreadyRecognized,
    Rejected
}

internal sealed record Release1StoryOwnerQaRecognitionResult(
    Release1StoryOwnerQaRecognitionStatus Status,
    string Message);

internal static class Release1StoryOwnerQaRecognitionHarness
{
    private const string ReceiptPrefix = "oc48-owner-qa";

    public static Release1StoryOwnerQaRecognitionResult TryRecognize(
        IRelease1StoryHostContext context,
        Release1StoryRuntimeService service)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(service);

        Release1StoryHostContextReadStatus status;
        Release1StoryHostContextSnapshot snapshot;
        try
        {
            status = context.TryRead(out snapshot);
        }
        catch (Exception ex)
        {
            return Reject($"Owner QA host context faulted: {ex.Message}");
        }

        if (status != Release1StoryHostContextReadStatus.Ready)
            return Reject($"Owner QA recognition rejected by host context: {status}.");
        if (service.State?.Release1Recognized == true)
            return new(Release1StoryOwnerQaRecognitionStatus.AlreadyRecognized, "Release 1 was already recognized; no story transition ran.");

        if (service.State is null || service.State.RelationshipState == Release1RelationshipState.Deferred)
        {
            var intro = Command(
                snapshot,
                Release1MissionCatalog.IntroScopeKey,
                0,
                Release1TransitionKind.IntroAccepted,
                $"{ReceiptPrefix}-intro");
            var introResult = service.TryExecute(intro);
            if (!introResult.Accepted) return Reject(introResult.Message);
        }

        if (service.State?.RelationshipState != Release1RelationshipState.Accepted)
            return Reject("Owner QA recognition requires an accepted Release 1 relationship.");

        for (var index = 0; index < Release1MissionCatalog.All.Count; index++)
        {
            var sequence = index + 1;
            while (service.State!.Missions[index].State != Release1MissionState.Satisfied)
            {
                var mission = service.State.Missions[index];
                var command = mission.State switch
                {
                    Release1MissionState.Offered => MissionCommand(snapshot, mission, sequence, Release1TransitionKind.MissionAccepted, "accepted", termsVersion: "oc48-owner-qa-v1"),
                    Release1MissionState.Accepted => MissionCommand(snapshot, mission, sequence, Release1TransitionKind.MissionActivated, "activated"),
                    Release1MissionState.Active => MissionCommand(
                        snapshot,
                        mission,
                        sequence,
                        Release1TransitionKind.MissionCompleted,
                        "completed",
                        completionTiming: Release1CompletionTiming.OnTime,
                        rewardReceipt: $"{ReceiptPrefix}-{sequence}-reward",
                        recognitionReceipt: index == Release1MissionCatalog.All.Count - 1 ? $"{ReceiptPrefix}-recognition" : null),
                    _ => null
                };

                if (command is null)
                    return Reject($"Mission {mission.MissionKey} was in unsupported QA state {mission.State}; no bypass was attempted.");
                var result = service.TryExecute(command);
                if (!result.Accepted) return Reject($"Mission {mission.MissionKey} rejected {command.TransitionKind}: {result.Message}");
            }
        }

        return service.State?.Release1Recognized == true
            ? new(Release1StoryOwnerQaRecognitionStatus.Recognized, "Release 1 recognition was reached through the six canonical mission transition sequences.")
            : Reject("Canonical mission transitions completed without a recognition receipt.");
    }

    private static Release1StoryCommand MissionCommand(
        Release1StoryHostContextSnapshot snapshot,
        Release1MissionRecord mission,
        int sequence,
        Release1TransitionKind kind,
        string suffix,
        string? termsVersion = null,
        Release1CompletionTiming? completionTiming = null,
        string? rewardReceipt = null,
        string? recognitionReceipt = null) =>
        Command(
            snapshot,
            mission.MissionKey,
            mission.Attempt,
            kind,
            $"{ReceiptPrefix}-{sequence}-{suffix}",
            termsVersion,
            completionTiming,
            rewardReceipt,
            recognitionReceipt);

    private static Release1StoryCommand Command(
        Release1StoryHostContextSnapshot snapshot,
        string missionKey,
        int attempt,
        Release1TransitionKind kind,
        string receipt,
        string? termsVersion = null,
        Release1CompletionTiming? completionTiming = null,
        string? rewardReceipt = null,
        string? recognitionReceipt = null) =>
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
            RewardAuthorizationReceiptId: rewardReceipt,
            RecognitionReceiptId: recognitionReceipt);

    private static Release1StoryOwnerQaRecognitionResult Reject(string message) =>
        new(Release1StoryOwnerQaRecognitionStatus.Rejected, message);
}
