using OrganizedCrime.Model;

namespace OrganizedCrime.Persistence;

public sealed class FishWarehousePersistenceService
{
    private readonly IFishWarehouseSaveStore _store;
    private readonly Func<string?> _activeSaveFolder;
    private readonly Func<FishWarehouseSaveState> _captureState;
    private readonly Action<FishWarehouseSaveState> _applyState;
    private readonly Action<string> _log;
    private readonly IFishWarehousePropertySnapshotStore _snapshotStore;
    private readonly FishWarehouseReplayLifecycle _replayLifecycle;
    private FishWarehouseSaveState? _currentState;

    public FishWarehousePersistenceService(
        IFishWarehouseSaveStore store,
        Func<string?> activeSaveFolder,
        Func<FishWarehouseSaveState> captureState,
        Action<FishWarehouseSaveState> applyState,
        Action<string>? log = null,
        IFishWarehousePropertySnapshotStore? snapshotStore = null,
        FishWarehouseReplayLifecycle? replayLifecycle = null)
    {
        _store = store;
        _activeSaveFolder = activeSaveFolder;
        _captureState = captureState;
        _applyState = applyState;
        _log = log ?? (_ => { });
        _snapshotStore = snapshotStore ?? new FishWarehousePropertySnapshotStore();
        _replayLifecycle = replayLifecycle ?? new FishWarehouseReplayLifecycle();
    }

    public FishWarehouseSaveState? CurrentState => _currentState;

    public bool PrimeStateForLoad()
    {
        var saveFolder = _activeSaveFolder();
        if (string.IsNullOrWhiteSpace(saveFolder))
        {
            _currentState = null;
            _log("Fish Warehouse pre-load state prime skipped because the active save folder was unavailable.");
            return false;
        }

        _currentState = null;
        if (!_store.TryRead(saveFolder, out var state, out var failureReason))
        {
            _log($"Fish Warehouse pre-load state was not primed: {failureReason}");
            return false;
        }

        if (state.Replay is not null && !_replayLifecycle.Begin(state.Replay, out failureReason))
        {
            _log($"Fish Warehouse pre-load replay state was not primed: {failureReason}");
            return false;
        }

        _currentState = ReconcileOwnership(state);
        return true;
    }

    public bool RequiresSnapshotProtection => _replayLifecycle.RequiresSnapshotProtection || RequiresSnapshotProtectionFor(_currentState?.Replay);

    public bool HandleLoadComplete()
    {
        var saveFolder = _activeSaveFolder();
        if (string.IsNullOrWhiteSpace(saveFolder))
        {
            _log("Fish Warehouse save load skipped because the active save folder was unavailable.");
            return false;
        }

        if (_store.TryRead(saveFolder, out var state, out var failureReason))
        {
            if (state.Replay is not null && !TryBeginLoadedReplay(saveFolder, state.Replay, out failureReason))
            {
                _log($"Fish Warehouse save state was not loaded: {failureReason}");
                return false;
            }

            state = ReconcileOwnership(state);
            _currentState = state;
            _applyState(state);
            _log("Fish Warehouse save state loaded; owned-state restoration is queued after runtime readiness.");
            return true;
        }

        _log($"Fish Warehouse save state was not loaded: {failureReason}");
        return false;
    }

    public void HandleSaveComplete()
    {
        var saveFolder = _activeSaveFolder();
        if (string.IsNullOrWhiteSpace(saveFolder))
        {
            _log("Fish Warehouse save skipped because the active save folder was unavailable.");
            return;
        }

        if (RequiresSnapshotProtection)
        {
            if (TryRestoreProtectedNativeProperty(saveFolder, out var restoreFailure))
            {
                _log("WARNING: Fish Warehouse native property snapshot was restored to preserve the active replay; fresh runtime capture was skipped.");
                return;
            }

            _log($"WARNING: Fish Warehouse native property snapshot preservation failed: {restoreFailure}");
            return;
        }

        var runtimeState = _captureState();
        var state = runtimeState with
        {
            Replay = _currentState?.Replay ?? runtimeState.Replay,
            UnsupportedEmployees = _currentState?.UnsupportedEmployees ?? runtimeState.UnsupportedEmployees
        };
        state = ReconcileOwnership(state);
        if (_store.TryWrite(saveFolder, state, out var failureReason))
        {
            _currentState = state;
            _log("Fish Warehouse save state written to the Organized Crime sidecar.");
            return;
        }

        _log($"Fish Warehouse save state was not written: {failureReason}");
    }

    public bool TryPersistReplayCheckpoint(FishWarehouseReplayCheckpoint checkpoint)
    {
        var saveFolder = _activeSaveFolder();
        if (string.IsNullOrWhiteSpace(saveFolder))
        {
            _log("Fish Warehouse replay checkpoint persistence skipped because the active save folder was unavailable.");
            return false;
        }

        if (!TryPreserveExistingState(saveFolder))
        {
            _log("Fish Warehouse replay checkpoint was not persisted because the existing sidecar could not be validated without overwriting it.");
            return false;
        }

        if (!TrySynchronizeReplayCheckpoint(saveFolder, checkpoint, out var failureReason))
        {
            _log($"Fish Warehouse replay checkpoint was not persisted: {failureReason}");
            return false;
        }

        return TryPersistState(saveFolder, MergeCurrentState() with { Replay = checkpoint });
    }

    public bool TryPersistRecoveredCaptureCheckpoint(FishWarehouseReplayCheckpoint checkpoint)
    {
        var saveFolder = _activeSaveFolder();
        if (string.IsNullOrWhiteSpace(saveFolder))
        {
            _log("Fish Warehouse recovered capture checkpoint persistence skipped because the active save folder was unavailable.");
            return false;
        }

        string? failureReason = null;
        if (checkpoint is null || checkpoint.Phase != FishWarehouseReplayPhase.Captured ||
            !HasActiveSaveFolderKey(saveFolder, checkpoint, out failureReason))
        {
            _log($"Fish Warehouse recovered capture checkpoint was not persisted: {failureReason ?? "checkpoint was invalid."}");
            return false;
        }

        if (!TryPreserveExistingState(saveFolder))
        {
            _log("Fish Warehouse recovered capture checkpoint was not persisted because the existing sidecar could not be validated without overwriting it.");
            return false;
        }

        if (_replayLifecycle.CurrentCheckpoint is not { Phase: FishWarehouseReplayPhase.Failed } failedCheckpoint ||
            !string.Equals(failedCheckpoint.GenerationId, checkpoint.GenerationId, StringComparison.Ordinal) ||
            !string.Equals(failedCheckpoint.SaveFolderKey, checkpoint.SaveFolderKey, StringComparison.Ordinal))
        {
            _log("Fish Warehouse recovered capture checkpoint was not persisted because the active lifecycle was not the same capture failure.");
            return false;
        }

        return TryPersistState(saveFolder, MergeCurrentState() with { Replay = checkpoint });
    }

    public bool TryPersistEmployeeAgentTypeId(int agentTypeId) =>
        TryPersistState(MergeCurrentState() with { EmployeeAgentTypeId = agentTypeId });

    public bool TryMergeUnsupportedEmployees(IReadOnlyList<FishWarehouseUnsupportedEmployeeRecord> records)
    {
        if (records is null)
            return false;

        var merged = new Dictionary<string, FishWarehouseUnsupportedEmployeeRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in MergeCurrentState().UnsupportedEmployees)
            merged[record.Guid] = record;
        foreach (var record in records)
            merged[record.Guid] = record;

        return TryPersistState(MergeCurrentState() with { UnsupportedEmployees = merged.Values.ToArray() });
    }

    private FishWarehouseSaveState MergeCurrentState() => _currentState ?? _captureState();

    // Breaks the ownership doom-loop at both the read and write sides. A transient
    // restore-failure can stamp owned=false into the sidecar while the captured
    // snapshot still proves the property was owned; ownership is monotonic in OC (no
    // sell path), so any evidence of prior ownership means it is still owned. Applied
    // to a loaded state (so a corrupt owned=false still restores) and before every
    // sidecar write (so the downgrade is never persisted). Strictly a superset — it
    // only ever concludes owned, never downgrades — so a genuinely-owned save is
    // untouched.
    private FishWarehouseSaveState ReconcileOwnership(FishWarehouseSaveState state)
    {
        bool ownedByEvidence = FishWarehouseOwnershipReconciler.IsOwnedByEvidence(
            state.Owned,
            state.Replay?.CapturedObjectCount ?? 0,
            state.Replay?.CapturedEmployeeCount ?? 0,
            nativeIsOwned: null);

        if (!ownedByEvidence || (state.Owned && state.Unlocked))
            return state;

        if (!state.Owned)
        {
            _log(
                "WARNING: Fish Warehouse ownership reconciled to owned=true from a corrupt sidecar " +
                $"owned=false because captured content (objects={state.Replay?.CapturedObjectCount ?? 0}, " +
                $"employees={state.Replay?.CapturedEmployeeCount ?? 0}) proves prior ownership " +
                "(ownership is monotonic — there is no sell path).");
        }

        return state with { Owned = true, Unlocked = true };
    }

    private bool TryPreserveExistingState(string saveFolder)
    {
        if (_currentState is not null)
            return true;

        if (_store.TryRead(saveFolder, out var existingState, out _))
        {
            if (existingState is not null)
                _currentState = existingState;

            return true;
        }

        if (!FishWarehouseSavePath.TryResolveSidecarPath(saveFolder, out var sidecarPath, out var pathFailure))
        {
            _log($"Fish Warehouse existing sidecar path could not be validated: {pathFailure}");
            return false;
        }

        if (File.Exists(sidecarPath))
        {
            _log("Fish Warehouse existing sidecar could not be read; preserving it instead of overwriting it.");
            return false;
        }

        return true;
    }

    private bool TryPersistState(FishWarehouseSaveState state)
    {
        var saveFolder = _activeSaveFolder();
        if (string.IsNullOrWhiteSpace(saveFolder))
        {
            _log("Fish Warehouse persistence update skipped because the active save folder was unavailable.");
            return false;
        }

        return TryPersistState(saveFolder, state);
    }

    private bool TryPersistState(string saveFolder, FishWarehouseSaveState state)
    {
        state = ReconcileOwnership(state);
        if (!_store.TryWrite(saveFolder, state, out var failureReason))
        {
            _log($"Fish Warehouse persistence update was not written: {failureReason}");
            return false;
        }

        _currentState = state;
        return true;
    }

    private bool TryBeginLoadedReplay(
        string saveFolder,
        FishWarehouseReplayCheckpoint checkpoint,
        out string? failureReason)
    {
        if (!HasActiveSaveFolderKey(saveFolder, checkpoint, out failureReason))
            return false;

        return _replayLifecycle.Begin(checkpoint, out failureReason);
    }

    private bool TrySynchronizeReplayCheckpoint(
        string saveFolder,
        FishWarehouseReplayCheckpoint checkpoint,
        out string? failureReason)
    {
        if (!HasActiveSaveFolderKey(saveFolder, checkpoint, out failureReason))
            return false;

        var current = _replayLifecycle.CurrentCheckpoint;
        if (current == checkpoint)
        {
            failureReason = null;
            return true;
        }

        if (current is null || current.Phase == FishWarehouseReplayPhase.Complete)
        {
            if (checkpoint.Phase != FishWarehouseReplayPhase.Captured)
            {
                failureReason = "Fish Warehouse replay must begin at Captured.";
                return false;
            }

            return _replayLifecycle.Begin(checkpoint, out failureReason);
        }

        failureReason = "Fish Warehouse replay checkpoint was not the lifecycle's active phase.";
        return false;
    }

    private static bool HasActiveSaveFolderKey(
        string saveFolder,
        FishWarehouseReplayCheckpoint checkpoint,
        out string? failureReason)
    {
        try
        {
            if (string.Equals(FishWarehouseSaveFolderKey.Create(saveFolder), checkpoint.SaveFolderKey, StringComparison.Ordinal))
            {
                failureReason = null;
                return true;
            }

            failureReason = "Fish Warehouse replay checkpoint belongs to a different active save folder.";
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            failureReason = $"Fish Warehouse active save folder key could not be validated: {ex.Message}";
            return false;
        }
    }

    private bool TryRestoreProtectedNativeProperty(string saveFolder, out string? failureReason)
    {
        var replay = _replayLifecycle.CurrentCheckpoint ?? _currentState?.Replay;
        if (replay is null)
        {
            failureReason = "Fish Warehouse replay checkpoint was unavailable for snapshot preservation.";
            return false;
        }

        if (!FishWarehouseSavePath.TryResolveSnapshotPath(saveFolder, out var snapshotPath, out failureReason))
            return false;

        if (!File.Exists(snapshotPath))
        {
            failureReason = "Fish Warehouse protected snapshot file was unavailable for preservation.";
            return false;
        }

        var snapshot = new FishWarehouseProtectedSnapshot(
            replay.GenerationId,
            replay.SaveFolderKey,
            replay.SnapshotRelativePath,
            replay.SnapshotSha256,
            checked((int)new FileInfo(snapshotPath).Length));
        return _snapshotStore.TryRestoreNativeProperty(saveFolder, snapshot, out failureReason);
    }

    private static bool RequiresSnapshotProtectionFor(FishWarehouseReplayCheckpoint? replay) => replay is { Phase: >= FishWarehouseReplayPhase.Captured and <= FishWarehouseReplayPhase.DeliveriesReleased } ||
        replay?.Phase == FishWarehouseReplayPhase.Failed;
}
