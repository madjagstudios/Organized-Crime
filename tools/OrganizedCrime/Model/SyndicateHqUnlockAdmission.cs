using OrganizedCrime.Runtime;

namespace OrganizedCrime.Model;

public sealed record SyndicateHqUnlockDecision(bool IsUnlocked, string Reason)
{
    public static SyndicateHqUnlockDecision Locked(string reason) => new(false, reason);
    public static SyndicateHqUnlockDecision Unlocked() => new(true, "OC-10 Release 1 recognition authorizes Syndicate HQ entry.");
    public static SyndicateHqUnlockDecision UnlockedByWrongAddressSatisfied() => new(true,
        "Wrong Address is satisfied, so Room With No Name's HQ closets are authorized ahead of full Release 1 recognition.");
}

public static class SyndicateHqUnlockAdmission
{
    public static SyndicateHqUnlockDecision Evaluate(
        Release1StoryHostContextReadStatus contextStatus,
        Release1StoryHostContextSnapshot? context,
        Release1StoryState? story)
    {
        if (contextStatus != Release1StoryHostContextReadStatus.Ready || context is null)
            return SyndicateHqUnlockDecision.Locked("Canonical host authority was not ready.");
        var currentContext = context.Value;
        if (!LocalPressureHostLifecyclePolicies.IsCanonicalPlayerIdentity(currentContext.PlayerId) ||
            !LocalPressureHostLifecyclePolicies.TryGetCanonicalHostIdentity(currentContext.ActiveSaveFolder, out var saveIdentity) ||
            !string.Equals(saveIdentity, currentContext.PlayerId, StringComparison.Ordinal))
            return SyndicateHqUnlockDecision.Locked("Canonical save-account identity did not match the host identity.");
        if (story is null || !string.Equals(story.PlayerId, currentContext.PlayerId, StringComparison.Ordinal))
            return SyndicateHqUnlockDecision.Locked("OC-10 Release 1 recognition has not been authorized for this host.");

        var hasCanonicalRecognition = story.Release1Recognized && story.RecognitionLogicalCorrelationIds.Any(id =>
            Release1LogicalCorrelation.TryParse(id, out var correlation) &&
            correlation.PlayerId == currentContext.PlayerId &&
            correlation.MissionKey == Release1MissionCatalog.IntroScopeKey &&
            correlation.Attempt == 0 &&
            correlation.TransitionKind == Release1TransitionKind.Release1Recognized);
        if (hasCanonicalRecognition)
            return SyndicateHqUnlockDecision.Unlocked();

        var wrongAddressSatisfied = story.Missions.Any(mission =>
            mission.MissionKey == Release1MissionCatalog.WrongAddress &&
            mission.State == Release1MissionState.Satisfied);
        return wrongAddressSatisfied
            ? SyndicateHqUnlockDecision.UnlockedByWrongAddressSatisfied()
            : SyndicateHqUnlockDecision.Locked("OC-10 recognition correlation was missing or non-canonical, and Wrong Address is not yet satisfied.");
    }
}
