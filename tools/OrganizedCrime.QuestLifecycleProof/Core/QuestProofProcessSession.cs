namespace OrganizedCrime.QuestLifecycleProof;

public sealed record QuestProofEvaluationContext(
    string RunChainId,
    string ProcessSessionId,
    bool CurrentProcessLoadBoundaryObserved,
    bool CurrentProcessLoadBoundaryValid = true);

public sealed record QuestProofLoadBoundaryDecision(
    bool Accepted,
    bool ShouldRecordReload,
    string Reason,
    string? NextLoadEpoch = null,
    bool IsReconstruction = false,
    QuestReloadClassification ReloadClassification = QuestReloadClassification.None,
    bool IsDuplicateNoOp = false);

public sealed class QuestProofProcessSession
{
    private bool _loadCompleteObserved;

    private QuestProofProcessSession(
        string runChainId,
        string processSessionId,
        bool persistedChainValid,
        string? invalidReason)
    {
        RunChainId = runChainId;
        ProcessSessionId = processSessionId;
        PersistedChainValid = persistedChainValid;
        InvalidReason = invalidReason;
    }

    public string RunChainId { get; }

    public string ProcessSessionId { get; }

    public bool PersistedChainValid { get; }

    public string? InvalidReason { get; }

    public bool CurrentProcessLoadBoundaryObserved => _loadCompleteObserved;

    public bool CurrentProcessLoadBoundaryValid { get; private set; } = true;

    public QuestProofEvaluationContext EvaluationContext =>
        new(
            RunChainId,
            ProcessSessionId,
            CurrentProcessLoadBoundaryObserved,
            CurrentProcessLoadBoundaryValid);

    public static QuestProofProcessSession Restore(
        IReadOnlyList<QuestLifecycleObservation> persistedObservations,
        string processSessionId,
        string newRunChainId)
    {
        ArgumentNullException.ThrowIfNull(persistedObservations);
        if (string.IsNullOrWhiteSpace(processSessionId))
            throw new ArgumentException("Process-session identity is required.", nameof(processSessionId));
        if (string.IsNullOrWhiteSpace(newRunChainId))
            throw new ArgumentException("New run-chain identity is required.", nameof(newRunChainId));

        if (persistedObservations.Count == 0)
            return new QuestProofProcessSession(newRunChainId, processSessionId, true, null);

        if (persistedObservations.Any(x => string.IsNullOrWhiteSpace(x.RunChainId)))
            return Invalid(newRunChainId, processSessionId, "persisted run-chain identity is missing");

        var runChainIds = persistedObservations
            .Select(x => x.RunChainId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (runChainIds.Length != 1)
            return Invalid(newRunChainId, processSessionId, "persisted run-chain identity drifted");

        if (persistedObservations.Any(x => string.IsNullOrWhiteSpace(x.ProcessSessionId)))
            return Invalid(newRunChainId, processSessionId, "persisted process-session identity is missing");

        if (persistedObservations.Any(x => string.Equals(x.ProcessSessionId, processSessionId, StringComparison.Ordinal)))
            return Invalid(newRunChainId, processSessionId, "current process-session identity already exists in persisted evidence");

        return new QuestProofProcessSession(runChainIds[0], processSessionId, true, null);
    }

    public QuestProofLoadBoundaryDecision ObserveLoadComplete(
        IReadOnlyList<QuestLifecycleObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        if (_loadCompleteObserved)
            return new(
                true,
                false,
                "duplicate OnLoadComplete accepted as a same-process no-op",
                IsDuplicateNoOp: true);

        _loadCompleteObserved = true;
        if (!PersistedChainValid)
        {
            CurrentProcessLoadBoundaryValid = false;
            return new(false, false, InvalidReason ?? "persisted run-chain evidence is invalid");
        }

        if (observations.Count == 0 || observations[^1].Phase != QuestLifecyclePhase.Save)
            return new(true, false, "load boundary observed without a pending saved launch");

        if (observations.Any(x => string.Equals(x.ProcessSessionId, ProcessSessionId, StringComparison.Ordinal)))
        {
            CurrentProcessLoadBoundaryValid = false;
            return new(false, false, "current process wrote proof evidence before its load boundary");
        }

        var terminalWasSaved = HasTerminalSinceLastReload(observations);
        return new(
            true,
            true,
            terminalWasSaved
                ? "terminal save is ready for completed-Quest no-replay observation"
                : "active save is ready for reconstruction observation",
            NextLoadEpoch(observations[^1].LoadEpoch),
            IsReconstruction: !terminalWasSaved,
            ReloadClassification: terminalWasSaved
                ? QuestReloadClassification.CompletedQuestAbsentNoReplay
                : QuestReloadClassification.ActiveReconstruction);
    }

    private static QuestProofProcessSession Invalid(
        string newRunChainId,
        string processSessionId,
        string reason) =>
        new(newRunChainId, processSessionId, false, reason);

    private static bool HasTerminalSinceLastReload(IReadOnlyList<QuestLifecycleObservation> observations)
    {
        var lastReloadIndex = -1;
        for (var index = observations.Count - 1; index >= 0; index--)
        {
            if (observations[index].Phase != QuestLifecyclePhase.FullReload)
                continue;

            lastReloadIndex = index;
            break;
        }

        return observations
            .Skip(lastReloadIndex + 1)
            .Any(x => x.Phase == QuestLifecyclePhase.Terminal);
    }

    private static string NextLoadEpoch(string currentEpoch)
    {
        const string prefix = "load-";
        if (currentEpoch.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(currentEpoch[prefix.Length..], out var number))
            return $"{prefix}{number + 1}";

        return $"{prefix}unknown";
    }
}
