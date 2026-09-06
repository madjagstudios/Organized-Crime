namespace OrganizedCrime.PhoneCallProof;

public readonly record struct PhoneCallProofContext(bool IsCanonicalSinglePlayerHost);

public enum PhoneCallProofResult
{
    Queued,
    Duplicate,
    Deferred,
    NotCanonicalHost
}

public enum PhoneProofKey
{
    Nell,
    Arthur,
    Classify
}

public enum PhoneProofProtocolState
{
    Detached,
    AwaitingNell,
    NellQueued,
    ArthurQueued,
    Complete,
    Stop
}

public enum PhoneProofClassification
{
    Pass,
    Inconclusive,
    Stop
}

public sealed record PhoneProofOwnerObservations(
    bool QueueReceiptObserved,
    bool CallerNamesStable,
    bool StageOrderObserved,
    bool ReloadNoDuplicate,
    bool TeardownInert,
    bool NoStoryStandingRewardMutation)
{
    public bool AllRequiredObservationsPresent =>
        QueueReceiptObserved &&
        CallerNamesStable &&
        StageOrderObserved &&
        ReloadNoDuplicate &&
        TeardownInert &&
        NoStoryStandingRewardMutation;
}

public sealed record PhoneProofEvidence(
    PhoneProofClassification Classification,
    PhoneProofProtocolState State,
    PhoneCallProofResult? NellResult,
    PhoneCallProofResult? ArthurResult,
    int QueueAttempts,
    PhoneProofOwnerObservations? OwnerObservations,
    string? StopReason,
    int StoryWrites,
    int StandingWrites,
    int RewardWrites)
{
    public string Status => Classification.ToString().ToUpperInvariant();

    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this);
}

public sealed record PhoneCallProofRequest(string CallerName, IReadOnlyList<string> StageTexts)
{
    public static PhoneCallProofRequest Create(string callerName, params string[] stageTexts)
    {
        if (string.IsNullOrWhiteSpace(callerName))
            throw new ArgumentException("Caller name is required.", nameof(callerName));
        ArgumentNullException.ThrowIfNull(stageTexts);
        if (stageTexts.Length == 0 || stageTexts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-empty stage is required.", nameof(stageTexts));

        return new PhoneCallProofRequest(callerName, stageTexts.ToArray());
    }
}

public interface IPhoneCallQueue
{
    bool TryQueue(PhoneCallProofRequest request);
}

public sealed record PhoneCallProofSnapshot(
    string[] QueuedCorrelations,
    string[] CompletedCorrelations,
    int StoryWrites,
    int StandingWrites,
    int RewardWrites);

public sealed class PhoneCallProofState
{
    private readonly HashSet<string> _queuedCorrelations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completedCorrelations = new(StringComparer.Ordinal);

    public bool IsQueued(string correlation) => _queuedCorrelations.Contains(correlation);

    public void MarkQueued(string correlation) => _queuedCorrelations.Add(correlation);

    public void MarkCompleted(string correlation) => _completedCorrelations.Add(correlation);

    public PhoneCallProofSnapshot Snapshot() => new(
        _queuedCorrelations.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
        _completedCorrelations.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
        StoryWrites: 0,
        StandingWrites: 0,
        RewardWrites: 0);

    public static PhoneCallProofState Restore(PhoneCallProofSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var state = new PhoneCallProofState();
        foreach (var correlation in snapshot.QueuedCorrelations)
            state._queuedCorrelations.Add(correlation);
        foreach (var correlation in snapshot.CompletedCorrelations)
            state._completedCorrelations.Add(correlation);
        return state;
    }
}
