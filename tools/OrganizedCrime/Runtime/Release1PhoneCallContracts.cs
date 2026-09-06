using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1PhoneCallRole { Nell, Arthur }

public enum Release1PhoneCallResultState
{
    Deferred, Pending, Attempting, Delivered, Ambiguous, Completed, NotAuthoritative,
    WrongEpoch, IdentityMismatch, Inactive, Saving, OrderRejected, InvalidRequest, Quarantined, Disposed
}

public static class Release1PhoneCallCorrelation
{
    private const string NellPrefix = "release1-phone-nell-";
    private const string ArthurPrefix = "release1-phone-arthur-";
    public const string NellPresentationPrefix = "presentation-nell-";

    public static string Create(string playerId, string missionKey, int attempt, Release1PhoneCallRole role, string receiptId)
    {
        if (string.IsNullOrWhiteSpace(receiptId) || receiptId.Any(char.IsControl) || receiptId.Any(char.IsWhiteSpace))
            throw new ArgumentException("Phone-call receipt must be non-empty and whitespace-free.", nameof(receiptId));
        var prefix = role switch
        {
            Release1PhoneCallRole.Nell => NellPrefix,
            Release1PhoneCallRole.Arthur => ArthurPrefix,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
        return Release1LogicalCorrelation.Create(playerId, missionKey, attempt, Release1TransitionKind.MissionAccepted, prefix + receiptId).Value;
    }

    public static bool MatchesRole(Release1LogicalCorrelation correlation, Release1PhoneCallRole role) => role switch
    {
        Release1PhoneCallRole.Nell => correlation.ReceiptId.StartsWith(NellPrefix, StringComparison.Ordinal),
        Release1PhoneCallRole.Arthur => correlation.ReceiptId.StartsWith(ArthurPrefix, StringComparison.Ordinal),
        _ => false
    };

    /// <summary>
    /// True when a correlation is an acceptable Arthur prerequisite: either a delivered Nell phone
    /// call (the OC-46 shape) or a receipted Nell message on the native presentation path (the OC-53
    /// shape, where Nell speaks in Messages and never rings). Both prove Nell spoke first on this
    /// mission and attempt, which is the rule the prerequisite exists to enforce.
    /// </summary>
    public static bool IsNellPrerequisite(Release1LogicalCorrelation correlation) =>
        MatchesRole(correlation, Release1PhoneCallRole.Nell) ||
        correlation.ReceiptId.StartsWith(NellPresentationPrefix, StringComparison.Ordinal);
}

public sealed record Release1PhoneCallRequest(
    Release1StoryHostContextSnapshot Context,
    string MissionKey,
    int Attempt,
    string CorrelationId,
    Release1PhoneCallRole Role,
    string CallerLabel,
    IReadOnlyList<string> StageTexts,
    string? RequiredPriorCorrelationId)
{
    public static Release1PhoneCallRequest Create(
        Release1StoryHostContextSnapshot context,
        string missionKey,
        int attempt,
        string correlationId,
        Release1PhoneCallRole role,
        string callerLabel,
        IReadOnlyList<string> stageTexts,
        string? requiredPriorCorrelationId = null)
    {
        if (context.SessionEpoch == Guid.Empty || context.LoadEpoch < 0 || string.IsNullOrWhiteSpace(context.PlayerId) || string.IsNullOrWhiteSpace(context.ActiveSaveFolder))
            throw new ArgumentException("Phone-call host context was invalid.", nameof(context));
        if (!Release1MissionCatalog.IsMissionKey(missionKey) || attempt < 1)
            throw new ArgumentException("Phone-call mission or attempt was invalid.", nameof(missionKey));
        if (!Release1LogicalCorrelation.TryParse(correlationId, out var correlation) || correlation.PlayerId != context.PlayerId || correlation.MissionKey != missionKey || correlation.Attempt != attempt)
            throw new ArgumentException("Phone-call correlation was not canonical for the request.", nameof(correlationId));
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));
        if (!Release1PhoneCallCorrelation.MatchesRole(correlation, role))
            throw new ArgumentException("Phone-call correlation did not carry the request role tag.", nameof(correlationId));
        if (string.IsNullOrWhiteSpace(callerLabel) || callerLabel.Length > 256 || callerLabel.Any(char.IsControl))
            throw new ArgumentException("Caller label must be bounded and control-character-free.", nameof(callerLabel));
        ArgumentNullException.ThrowIfNull(stageTexts);
        var stages = stageTexts.ToArray();
        if (stages.Length == 0 || stages.Any(stage => string.IsNullOrWhiteSpace(stage) || stage.Length > 2048 || stage.Any(char.IsControl)))
            throw new ArgumentException("Phone-call stages must be non-empty, bounded, and control-character-free.", nameof(stageTexts));
        if (role == Release1PhoneCallRole.Arthur)
        {
            if (!Release1LogicalCorrelation.TryParse(requiredPriorCorrelationId, out var prior) ||
                prior.PlayerId != context.PlayerId || prior.MissionKey != missionKey || prior.Attempt != attempt ||
                !Release1PhoneCallCorrelation.IsNellPrerequisite(prior))
                throw new ArgumentException("Arthur calls require a matching canonical Nell prerequisite.", nameof(requiredPriorCorrelationId));
        }
        if (role == Release1PhoneCallRole.Nell && requiredPriorCorrelationId is not null)
            throw new ArgumentException("Nell calls cannot declare an Arthur prerequisite.", nameof(requiredPriorCorrelationId));

        return new(context, missionKey, attempt, correlationId, role, callerLabel, stages, requiredPriorCorrelationId);
    }

    public void Validate() => _ = Create(Context, MissionKey, Attempt, CorrelationId, Role, CallerLabel, StageTexts, RequiredPriorCorrelationId);
}

public interface IRelease1PhoneCallQueue
{
    bool IsAvailable(Release1PhoneCallRequest request);
    void Invoke(Release1PhoneCallRequest request);
}

public interface IRelease1PayphoneCue : IDisposable
{
    bool TryShow(string correlationId);
    void End(string correlationId);
    void Reconcile(string correlationId);
}

public sealed record Release1PhoneCallResult(Release1PhoneCallResultState State, bool CueShown, string Message)
{
    public bool Accepted => State is Release1PhoneCallResultState.Pending or Release1PhoneCallResultState.Attempting or Release1PhoneCallResultState.Delivered or Release1PhoneCallResultState.Ambiguous or Release1PhoneCallResultState.Completed;
}
