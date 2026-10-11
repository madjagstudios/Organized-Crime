using System.Security.Cryptography;
using Il2CppScheduleOne.Persistence.Datas;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;

namespace OrganizedCrime.Runtime;

public enum FishWarehousePropertyCaptureState
{
    Inactive,
    Captured,
    RecoveryRequired,
    Degraded
}

public sealed record FishWarehousePropertyCaptureResult(
    FishWarehousePropertyCaptureState State,
    string? FailureReason = null)
{
    // Full exception detail (type, stack, inner). Logged once where the
    // failure is recorded; never persisted to the sidecar.
    public string? FailureDetail { get; init; }
}

internal enum FishWarehousePropertyWriteGuardDecision
{
    RunOriginal,
    SuppressWithEmptyResult
}

internal sealed record FishWarehousePropertyCaptureAttempt(
    bool ByteProtectionSucceeded,
    bool InspectionSucceeded,
    bool TypedPayloadRetentionSucceeded,
    bool CheckpointPersistenceSucceeded,
    string? FailureReason)
{
    // Full exception detail (type, stack, inner). Logged once where the
    // failure is recorded; never persisted to the sidecar.
    public string? FailureDetail { get; init; }

    public bool AllSucceeded =>
        ByteProtectionSucceeded &&
        InspectionSucceeded &&
        TypedPayloadRetentionSucceeded &&
        CheckpointPersistenceSucceeded;

    public FishWarehouseCaptureRecoveryPrerequisites ToRecoveryPrerequisites() => new(
        ByteProtectionSucceeded,
        InspectionSucceeded,
        TypedPayloadRetentionSucceeded,
        CheckpointPersistenceSucceeded);
}

internal sealed record FishWarehousePropertyCapturePolicyDecision(
    bool RunOriginal,
    FishWarehousePropertyCaptureResult Result)
{
    public bool PermitsReplay => FishWarehousePropertyCapturePolicy.PermitsReplay(Result);
}

internal static class FishWarehousePropertyCapturePolicy
{
    public static FishWarehousePropertyCapturePolicyDecision EvaluateLoad(
        bool isAuthoritativeHost,
        string? propertyCode,
        string? activeSaveFolder,
        FishWarehousePropertyCaptureAttempt attempt)
    {
        if (ShouldPassThrough(isAuthoritativeHost, propertyCode, activeSaveFolder))
            return new FishWarehousePropertyCapturePolicyDecision(
                RunOriginal: true,
                new FishWarehousePropertyCaptureResult(FishWarehousePropertyCaptureState.Inactive));

        return new FishWarehousePropertyCapturePolicyDecision(
            RunOriginal: false,
            EvaluateInitialCapture(attempt));
    }

    public static bool ShouldPassThrough(
        bool isAuthoritativeHost,
        string? propertyCode,
        string? activeSaveFolder) =>
        !isAuthoritativeHost ||
        !string.Equals(propertyCode, FishWarehouseSaveState.ExpectedPropertyCode, StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(activeSaveFolder);

    public static FishWarehousePropertyCaptureResult EvaluateInitialCapture(FishWarehousePropertyCaptureAttempt attempt) =>
        attempt.AllSucceeded
            ? new FishWarehousePropertyCaptureResult(FishWarehousePropertyCaptureState.Captured)
            : new FishWarehousePropertyCaptureResult(
                FishWarehousePropertyCaptureState.RecoveryRequired,
                attempt.FailureReason ?? "Fish Warehouse capture prerequisites were not all satisfied.")
            { FailureDetail = attempt.FailureDetail };

    public static FishWarehousePropertyCaptureResult EvaluateLoadCompleteRetry(FishWarehousePropertyCaptureAttempt attempt) =>
        attempt.AllSucceeded
            ? new FishWarehousePropertyCaptureResult(FishWarehousePropertyCaptureState.Captured)
            : new FishWarehousePropertyCaptureResult(
                FishWarehousePropertyCaptureState.Degraded,
                attempt.FailureReason ?? "Fish Warehouse capture recovery prerequisites were not all satisfied.")
            { FailureDetail = attempt.FailureDetail };

    public static bool PermitsReplay(FishWarehousePropertyCaptureResult result) =>
        result.State == FishWarehousePropertyCaptureState.Captured;

    public static bool EnablesWriteGuard(FishWarehousePropertyCaptureResult result) =>
        result.State == FishWarehousePropertyCaptureState.Degraded;
}

public sealed class FishWarehousePropertyCaptureService
{
    private readonly IFishWarehousePropertySnapshotStore _snapshotStore;
    private readonly FishWarehouseNativePropertySnapshotInspector _inspector;
    private readonly FishWarehousePersistenceService _persistenceService;
    private readonly FishWarehouseReplayLifecycle _replayLifecycle;
    private readonly Action<string> _log;
    private FishWarehousePropertyCaptureAttempt _latestAttempt =
        new(false, false, false, false, "Fish Warehouse capture has not run.");
    private FishWarehouseReplayCheckpoint? _persistedCapturedCheckpoint;
    private string? _generationId;
    private FishWarehousePropertyCaptureResult _latestResult =
        new(FishWarehousePropertyCaptureState.Inactive);

    public FishWarehousePropertyCaptureService(
        IFishWarehousePropertySnapshotStore snapshotStore,
        FishWarehouseNativePropertySnapshotInspector inspector,
        FishWarehousePersistenceService persistenceService,
        FishWarehouseReplayLifecycle replayLifecycle,
        Action<string>? log = null)
    {
        _snapshotStore = snapshotStore;
        _inspector = inspector;
        _persistenceService = persistenceService;
        _replayLifecycle = replayLifecycle;
        _log = log ?? (_ => { });
    }

    public FishWarehousePropertyCaptureState State => _latestResult.State;
    public PropertyData? CapturedPropertyData { get; private set; }
    public IReadOnlyList<FishWarehouseNativePropertyEmployeeSnapshot> CapturedEmployeeRecords { get; private set; } =
        Array.Empty<FishWarehouseNativePropertyEmployeeSnapshot>();
    public string? GenerationId => _generationId ??
        (_persistenceService.CurrentState?.Replay is { Phase: FishWarehouseReplayPhase.Failed } failedReplay
            ? failedReplay.GenerationId
            : null);
    public bool IsDegraded { get; private set; }

    public static IReadOnlyList<FishWarehouseEmployeeReplayDescriptor> CreateEmployeeReplayDescriptors(
        IReadOnlyList<FishWarehouseNativePropertyEmployeeSnapshot> records) =>
        (records ?? throw new ArgumentNullException(nameof(records)))
            .Select(record => new FishWarehouseEmployeeReplayDescriptor(
                record.Guid,
                record.DataType,
                record.Identity,
                record.RawJson))
            .ToArray();

    // Restores previously preserved employees whose type the replay now
    // supports: they become fallback-creation descriptors while still-
    // unsupported records are kept. A GUID the native file already has
    // replays from the native record; the stale preserved copy is consumed.
    public static IReadOnlyList<FishWarehouseEmployeeReplayDescriptor> CreatePreservedEmployeeReplayDescriptors(
        IReadOnlyList<FishWarehouseUnsupportedEmployeeRecord> preserved,
        IReadOnlyList<FishWarehouseEmployeeReplayDescriptor> nativeDescriptors,
        out IReadOnlyList<FishWarehouseUnsupportedEmployeeRecord> keptUnsupported)
    {
        if (preserved is null)
            throw new ArgumentNullException(nameof(preserved));
        if (nativeDescriptors is null)
            throw new ArgumentNullException(nameof(nativeDescriptors));

        var nativeGuids = new HashSet<string>(nativeDescriptors.Select(descriptor => descriptor.Guid), StringComparer.OrdinalIgnoreCase);
        var restore = new List<FishWarehouseEmployeeReplayDescriptor>();
        var kept = new List<FishWarehouseUnsupportedEmployeeRecord>();
        foreach (var record in preserved)
        {
            if (nativeGuids.Contains(record.Guid))
                continue;

            var descriptor = new FishWarehouseEmployeeReplayDescriptor(
                record.Guid,
                record.DataType,
                record.Identity,
                record.RawJson,
                RequiresFallbackCreation: true);
            if (descriptor.IsSupportedPackager)
                restore.Add(descriptor);
            else
                kept.Add(record);
        }

        keptUnsupported = kept;
        return restore;
    }

    public FishWarehousePropertyCaptureResult TryCapture(
        PropertyData propertyData,
        string saveFolder,
        string generationId)
    {
        if (propertyData is null)
            return RecordInitialFailure(saveFolder, generationId, FailureAttempt("typed property payload was unavailable."), null, null);

        if (!string.Equals(propertyData.PropertyCode, FishWarehouseSaveState.ExpectedPropertyCode, StringComparison.Ordinal))
            return SetResult(new FishWarehousePropertyCaptureResult(FishWarehousePropertyCaptureState.Inactive));

        if (string.IsNullOrWhiteSpace(generationId))
            return RecordInitialFailure(saveFolder, generationId, FailureAttempt("capture generation was unavailable."), null, null);

        if (State == FishWarehousePropertyCaptureState.Captured &&
            string.Equals(_generationId, generationId, StringComparison.Ordinal) &&
            ReferenceEquals(CapturedPropertyData, propertyData))
            return _latestResult;

        _generationId = generationId;
        CapturedPropertyData = propertyData;
        if (FindStaleBlockingCheckpoint(generationId) is { } stale)
            return TryCaptureAgainstStaleCheckpoint(saveFolder, generationId, stale, CapturedPropertyData is not null);
        var attempt = TryCaptureNativeProperty(saveFolder, propertyData, generationId, out var protectedSnapshot, out var inspection, out var nativeBytes);
        if (!attempt.ByteProtectionSucceeded || !attempt.InspectionSucceeded || !attempt.TypedPayloadRetentionSucceeded)
            return RecordInitialFailure(saveFolder, generationId, attempt, protectedSnapshot, nativeBytes, inspection);

        CapturedEmployeeRecords = inspection!.Employees;

        if (!TryBuildCapturedCheckpoint(saveFolder, generationId, protectedSnapshot, inspection, nativeBytes, out var checkpoint, out var checkpointFailure, out var checkpointDetail))
            return RecordInitialFailure(saveFolder, generationId, attempt with { FailureReason = checkpointFailure, FailureDetail = checkpointDetail }, protectedSnapshot, nativeBytes, inspection);

        return AdoptCapturedCheckpoint(saveFolder, generationId, attempt, protectedSnapshot, nativeBytes, inspection, checkpoint);
    }

    // Adopts a fully-verified fresh capture into the replay lifecycle. A
    // checkpoint adopted from a previous session (an interrupted replay left
    // in Captured, mid-replay, or Failed) only yields to a fresh capture
    // that holds at least as much: the stale snapshot may be the only full
    // copy left, so superseding with less would silently lose content. A
    // same-generation conflict still fails: that is an in-flight replay,
    // not a stale one.
    internal FishWarehousePropertyCaptureResult AdoptCapturedCheckpoint(
        string saveFolder,
        string generationId,
        FishWarehousePropertyCaptureAttempt attempt,
        FishWarehouseProtectedSnapshot? protectedSnapshot,
        byte[]? nativeBytes,
        FishWarehouseNativePropertySnapshot? inspection,
        FishWarehouseReplayCheckpoint checkpoint)
    {
        var existingFailedCheckpoint = _replayLifecycle.CurrentCheckpoint is { Phase: FishWarehouseReplayPhase.Failed } failedCheckpoint &&
            string.Equals(failedCheckpoint.GenerationId, checkpoint.GenerationId, StringComparison.Ordinal) &&
            string.Equals(failedCheckpoint.SaveFolderKey, checkpoint.SaveFolderKey, StringComparison.Ordinal);
        if (existingFailedCheckpoint)
        {
            if (!_persistenceService.TryPersistRecoveredCaptureCheckpoint(checkpoint))
            {
                return RecordInitialFailure(
                    saveFolder,
                    generationId,
                    attempt with { FailureReason = "Fish Warehouse recovered capture checkpoint persistence failed." },
                    protectedSnapshot,
                    nativeBytes,
                    inspection);
            }
        }
        else
        {
            if (!_replayLifecycle.Begin(checkpoint, out var beginFailure) && !TrySupersedeStaleCheckpoint(checkpoint))
                return RecordInitialFailure(saveFolder, generationId, attempt with { FailureReason = beginFailure }, protectedSnapshot, nativeBytes, inspection);

            if (!_persistenceService.TryPersistReplayCheckpoint(checkpoint))
            {
                return RecordInitialFailure(
                    saveFolder,
                    generationId,
                    attempt with { FailureReason = "Fish Warehouse capture checkpoint persistence failed." },
                    protectedSnapshot,
                    nativeBytes,
                    inspection);
            }
        }

        _latestAttempt = attempt with { CheckpointPersistenceSucceeded = true, FailureReason = null, FailureDetail = null };
        _persistedCapturedCheckpoint = checkpoint;
        if (existingFailedCheckpoint &&
            !_replayLifecycle.TryRecoverCaptureFailure(checkpoint, out var recoveryFailure))
        {
            return RecordInitialFailure(
                saveFolder,
                generationId,
                attempt with { FailureReason = recoveryFailure ?? "Fish Warehouse failed capture recovery." },
                protectedSnapshot,
                nativeBytes,
                inspection);
        }
        IsDegraded = false;
        return SetResult(FishWarehousePropertyCapturePolicy.EvaluateInitialCapture(_latestAttempt));
    }

    private bool TrySupersedeStaleCheckpoint(FishWarehouseReplayCheckpoint checkpoint)
    {
        var active = _replayLifecycle.CurrentCheckpoint;
        if (active is null || string.Equals(active.GenerationId, checkpoint.GenerationId, StringComparison.Ordinal))
            return false;

        if (checkpoint.CapturedObjectCount < active.CapturedObjectCount ||
            checkpoint.CapturedEmployeeCount < active.CapturedEmployeeCount)
        {
            _log($"WARNING: Fish Warehouse refused to supersede the stale {active.Phase} replay checkpoint from generation {active.GenerationId} (objects {active.CapturedObjectCount}, employees {active.CapturedEmployeeCount}): the fresh capture holds fewer (objects {checkpoint.CapturedObjectCount}, employees {checkpoint.CapturedEmployeeCount}); the stale generation stays active.");
            return false;
        }

        _replayLifecycle.ResetForNewCaptureGeneration();
        if (!_replayLifecycle.Begin(checkpoint, out _))
            return false;

        _log($"Fish Warehouse superseded a stale {active.Phase} replay checkpoint from generation {active.GenerationId} (objects {active.CapturedObjectCount}, employees {active.CapturedEmployeeCount}); the fresh capture owns generation {checkpoint.GenerationId} (objects {checkpoint.CapturedObjectCount}, employees {checkpoint.CapturedEmployeeCount}).");
        return true;
    }

    private FishWarehouseReplayCheckpoint? FindStaleBlockingCheckpoint(string generationId)
    {
        var active = _replayLifecycle.CurrentCheckpoint;
        return active is not null && !string.Equals(active.GenerationId, generationId, StringComparison.Ordinal)
            ? active
            : null;
    }

    // Captures while a previous session's checkpoint is still active. The
    // fresh content is inspected BEFORE anything is protected: protecting
    // first would overwrite the stale snapshot, which may be the only full
    // copy left when a save-time wipe emptied the native file. Supersede
    // only on at least as many objects and employees; otherwise restore
    // the native file from the stale snapshot and capture that, or fail
    // closed when the stale snapshot is unreadable. Internal for tests:
    // the Il2Cpp PropertyData cannot be constructed outside the game, so
    // tests vouch for the already-retained typed payload directly.
    internal FishWarehousePropertyCaptureResult TryCaptureAgainstStaleCheckpoint(
        string saveFolder,
        string generationId,
        FishWarehouseReplayCheckpoint stale,
        bool typedPayloadRetained,
        bool restoredRetry = false)
    {
        if (!TryReadNativePropertyBytes(saveFolder, out var nativeBytes, out var readFailure, out var readDetail))
            return RecordInitialFailure(saveFolder, generationId, FailureAttempt(readFailure ?? "Fish Warehouse native property bytes could not be read.", readDetail) with { TypedPayloadRetentionSucceeded = typedPayloadRetained }, null, nativeBytes);

        if (!_inspector.TryInspect(nativeBytes, out var inspection, out var inspectionFailure, out var inspectionDetail))
        {
            PreserveFailedInspection(saveFolder, nativeBytes);
            return RecordInitialFailure(saveFolder, generationId, FailureAttempt(inspectionFailure ?? "Fish Warehouse native property inspection failed.", inspectionDetail) with { TypedPayloadRetentionSucceeded = typedPayloadRetained }, null, nativeBytes);
        }

        if (!string.Equals(inspection.PropertyCode, FishWarehouseSaveState.ExpectedPropertyCode, StringComparison.Ordinal))
            return RecordInitialFailure(saveFolder, generationId, FailureAttempt("Fish Warehouse native property inspection returned a different property code.") with { TypedPayloadRetentionSucceeded = typedPayloadRetained }, null, nativeBytes);

        if (inspection.Objects.Count >= stale.CapturedObjectCount &&
            inspection.Employees.Count >= stale.CapturedEmployeeCount)
        {
            return ProtectAdoptFreshCapture(saveFolder, generationId, nativeBytes, inspection, typedPayloadRetained);
        }

        var staleDescriptor = new FishWarehouseProtectedSnapshot(
            stale.GenerationId,
            stale.SaveFolderKey,
            stale.SnapshotRelativePath,
            stale.SnapshotSha256,
            0);
        if (!restoredRetry && _snapshotStore.TryRestoreNativeProperty(saveFolder, staleDescriptor, out _))
        {
            _log($"WARNING: Fish Warehouse fresh capture holds fewer than the stale {stale.Phase} replay checkpoint from generation {stale.GenerationId} (stale objects {stale.CapturedObjectCount}, employees {stale.CapturedEmployeeCount}; fresh objects {inspection.Objects.Count}, employees {inspection.Employees.Count}); the native property was restored from the stale snapshot and capture restarts from it as generation {generationId}.");
            return TryCaptureAgainstStaleCheckpoint(saveFolder, generationId, stale, typedPayloadRetained, restoredRetry: true);
        }

        var restoreNote = restoredRetry
            ? "the native property was already restored from the stale snapshot once"
            : "the stale snapshot could not be restored";
        _log($"WARNING: Fish Warehouse refused to supersede the stale {stale.Phase} replay checkpoint from generation {stale.GenerationId} (stale objects {stale.CapturedObjectCount}, employees {stale.CapturedEmployeeCount}; fresh objects {inspection.Objects.Count}, employees {inspection.Employees.Count}): {restoreNote}; native load stays suppressed and saves stay blocked.");
        return RecordInitialFailure(
            saveFolder,
            generationId,
            FailureAttempt($"Fish Warehouse capture holds fewer objects or employees than the stale {stale.Phase} replay checkpoint from generation {stale.GenerationId} (stale objects {stale.CapturedObjectCount}, employees {stale.CapturedEmployeeCount}; fresh objects {inspection.Objects.Count}, employees {inspection.Employees.Count}), and {restoreNote}.") with { TypedPayloadRetentionSucceeded = typedPayloadRetained },
            null,
            nativeBytes,
            inspection);
    }

    private FishWarehousePropertyCaptureResult ProtectAdoptFreshCapture(
        string saveFolder,
        string generationId,
        byte[] nativeBytes,
        FishWarehouseNativePropertySnapshot inspection,
        bool typedPayloadRetained)
    {
        CapturedEmployeeRecords = inspection.Employees;
        if (!_snapshotStore.TryProtect(saveFolder, generationId, nativeBytes, out var protectedSnapshot, out var protectFailure))
            return RecordInitialFailure(saveFolder, generationId, new FishWarehousePropertyCaptureAttempt(false, true, typedPayloadRetained, false, protectFailure), null, nativeBytes, inspection);

        var attempt = new FishWarehousePropertyCaptureAttempt(true, true, typedPayloadRetained, false, null);
        if (!TryBuildCapturedCheckpoint(saveFolder, generationId, protectedSnapshot, inspection, nativeBytes, out var checkpoint, out var checkpointFailure, out var checkpointDetail))
            return RecordInitialFailure(saveFolder, generationId, attempt with { FailureReason = checkpointFailure, FailureDetail = checkpointDetail }, protectedSnapshot, nativeBytes, inspection);

        return AdoptCapturedCheckpoint(saveFolder, generationId, attempt, protectedSnapshot, nativeBytes, inspection, checkpoint);
    }

    internal FishWarehousePropertyCaptureResult RecordCaptureFailure(
        string saveFolder,
        string generationId,
        string failureReason,
        string? failureDetail = null)
    {
        _generationId = generationId;
        CapturedPropertyData = null;
        return RecordInitialFailure(saveFolder, generationId, FailureAttempt(failureReason, failureDetail), null, null);
    }

    public FishWarehousePropertyCaptureResult TryRecoverAtLoadComplete(string saveFolder)
    {
        if (State != FishWarehousePropertyCaptureState.RecoveryRequired)
            return _latestResult;

        if (CapturedPropertyData is null || string.IsNullOrWhiteSpace(_generationId))
            return RecordRetryFailure(FailureAttempt("typed property payload was unavailable for capture recovery."));

        var attempt = TryCaptureNativeProperty(saveFolder, CapturedPropertyData, _generationId, out var protectedSnapshot, out var inspection, out var nativeBytes);
        if (!attempt.ByteProtectionSucceeded || !attempt.InspectionSucceeded || !attempt.TypedPayloadRetentionSucceeded)
            return RecordRetryFailure(attempt);

        CapturedEmployeeRecords = inspection!.Employees;

        if (!TryBuildCapturedCheckpoint(saveFolder, _generationId, protectedSnapshot, inspection, nativeBytes, out var checkpoint, out var checkpointDetail, out var checkpointRetryDetail))
            return RecordRetryFailure(attempt with { FailureReason = checkpointDetail, FailureDetail = checkpointRetryDetail });

        if (!_persistenceService.TryPersistRecoveredCaptureCheckpoint(checkpoint))
            return RecordRetryFailure(attempt with { FailureReason = "Fish Warehouse recovered capture checkpoint persistence failed." });

        _latestAttempt = attempt with { CheckpointPersistenceSucceeded = true, FailureReason = null };
        _persistedCapturedCheckpoint = checkpoint;
        if (!_replayLifecycle.TryRecoverCaptureFailure(checkpoint, out var recoveryFailure))
        {
            _persistenceService.TryPersistReplayCheckpoint(_replayLifecycle.CurrentCheckpoint!);
            return RecordRetryFailure(_latestAttempt with { FailureReason = recoveryFailure });
        }

        IsDegraded = false;
        return SetResult(FishWarehousePropertyCapturePolicy.EvaluateLoadCompleteRetry(_latestAttempt));
    }

    public FishWarehouseCaptureRecoveryPrerequisites GetRecoveryPrerequisites(FishWarehouseReplayCheckpoint checkpoint)
    {
        var isPersistedRecoveryCheckpoint = _persistedCapturedCheckpoint == checkpoint;
        return isPersistedRecoveryCheckpoint
            ? _latestAttempt.ToRecoveryPrerequisites()
            : new FishWarehouseCaptureRecoveryPrerequisites(false, false, false, false);
    }

    public void ResetRuntimeReferences()
    {
        CapturedPropertyData = null;
        CapturedEmployeeRecords = Array.Empty<FishWarehouseNativePropertyEmployeeSnapshot>();
        _persistedCapturedCheckpoint = null;
        _generationId = null;
        _latestAttempt = new(false, false, false, false, "Fish Warehouse capture has not run for this load.");
        _replayLifecycle.ResetForNewCaptureGeneration();
        if (!IsDegraded)
            _latestResult = new FishWarehousePropertyCaptureResult(FishWarehousePropertyCaptureState.Inactive);
    }

    public void Reset()
    {
        CapturedPropertyData = null;
        CapturedEmployeeRecords = Array.Empty<FishWarehouseNativePropertyEmployeeSnapshot>();
        _persistedCapturedCheckpoint = null;
        _generationId = null;
        _latestAttempt = new(false, false, false, false, "Fish Warehouse capture has not run.");
        _latestResult = new FishWarehousePropertyCaptureResult(FishWarehousePropertyCaptureState.Inactive);
        IsDegraded = false;
    }

    private FishWarehousePropertyCaptureAttempt TryCaptureNativeProperty(
        string saveFolder,
        PropertyData propertyData,
        string generationId,
        out FishWarehouseProtectedSnapshot? protectedSnapshot,
        out FishWarehouseNativePropertySnapshot? inspection,
        out byte[] nativeBytes)
    {
        protectedSnapshot = null;
        inspection = null;
        nativeBytes = Array.Empty<byte>();
        var typedPayloadRetentionSucceeded = propertyData is not null && ReferenceEquals(CapturedPropertyData, propertyData);
        if (!TryReadNativePropertyBytes(saveFolder, out nativeBytes, out var readFailure, out var readDetail))
            return new FishWarehousePropertyCaptureAttempt(false, false, typedPayloadRetentionSucceeded, false, readFailure)
            { FailureDetail = readDetail };

        if (!_snapshotStore.TryProtect(saveFolder, generationId, nativeBytes, out var snapshot, out var protectFailure))
            return new FishWarehousePropertyCaptureAttempt(false, false, typedPayloadRetentionSucceeded, false, protectFailure);

        protectedSnapshot = snapshot;
        if (!_inspector.TryInspect(nativeBytes, out var inspectedSnapshot, out var inspectionFailure, out var inspectionDetail))
        {
            PreserveFailedInspection(saveFolder, nativeBytes);
            return new FishWarehousePropertyCaptureAttempt(true, false, typedPayloadRetentionSucceeded, false, inspectionFailure)
            { FailureDetail = inspectionDetail };
        }

        inspection = inspectedSnapshot;
        if (!string.Equals(inspection.PropertyCode, FishWarehouseSaveState.ExpectedPropertyCode, StringComparison.Ordinal))
        {
            return new FishWarehousePropertyCaptureAttempt(
                true,
                false,
                typedPayloadRetentionSucceeded,
                false,
                "Fish Warehouse native property inspection returned a different property code.");
        }

        return new FishWarehousePropertyCaptureAttempt(true, true, typedPayloadRetentionSucceeded, false, null);
    }

    private void PreserveFailedInspection(string saveFolder, byte[] nativeBytes)
    {
        try
        {
            if (_snapshotStore.TryPreserveFailedInspection(saveFolder, nativeBytes, out var preservedPath, out var preserveFailure))
                _log($"Fish Warehouse failing native property file preserved for diagnostics: {preservedPath}");
            else
                _log($"WARNING: Fish Warehouse failing native property file could not be preserved: {preserveFailure ?? "unknown failure"}");
        }
        catch (Exception ex)
        {
            _log($"WARNING: Fish Warehouse failing native property file preservation threw: {ex}");
        }
    }

    private FishWarehousePropertyCaptureResult RecordInitialFailure(
        string saveFolder,
        string generationId,
        FishWarehousePropertyCaptureAttempt attempt,
        FishWarehouseProtectedSnapshot? protectedSnapshot,
        byte[]? nativeBytes,
        FishWarehouseNativePropertySnapshot? inspection = null)
    {
        var failureReason = attempt.FailureReason ?? "Fish Warehouse native property capture failed.";
        var failureDetail = attempt.FailureDetail;
        if (!TryBuildCapturedCheckpoint(saveFolder, generationId, protectedSnapshot, inspection, nativeBytes, out var checkpoint, out _, out var recordCheckpointDetail))
        {
            failureDetail ??= recordCheckpointDetail;
        }
        else if (_replayLifecycle.Begin(checkpoint, out _))
        {
            _replayLifecycle.Fail(FishWarehouseReplayPhase.Captured, failureReason);
            attempt = attempt with
            {
                CheckpointPersistenceSucceeded = _persistenceService.TryPersistReplayCheckpoint(_replayLifecycle.CurrentCheckpoint!),
                FailureReason = failureReason
            };
        }

        attempt = attempt with { FailureDetail = failureDetail };
        _latestAttempt = attempt;
        _persistedCapturedCheckpoint = null;
        _log($"WARNING: Fish Warehouse native property capture failed; native load remains suppressed and recovery is required: {failureReason}");
        LogFailureDetail(attempt.FailureDetail);
        return SetResult(FishWarehousePropertyCapturePolicy.EvaluateInitialCapture(attempt));
    }

    private FishWarehousePropertyCaptureResult RecordRetryFailure(FishWarehousePropertyCaptureAttempt attempt)
    {
        _latestAttempt = attempt;
        _persistedCapturedCheckpoint = null;
        IsDegraded = true;
        var result = FishWarehousePropertyCapturePolicy.EvaluateLoadCompleteRetry(attempt);
        _log($"WARNING: Fish Warehouse capture recovery failed; normal Fish Warehouse saves are blocked to preserve the native property file: {result.FailureReason}");
        LogFailureDetail(result.FailureDetail);
        return SetResult(result);
    }

    private void LogFailureDetail(string? failureDetail)
    {
        if (!string.IsNullOrWhiteSpace(failureDetail))
            _log($"Fish Warehouse capture failure detail: {failureDetail}");
    }

    private static FishWarehousePropertyCaptureAttempt FailureAttempt(string reason, string? failureDetail = null) =>
        new(false, false, false, false, reason)
        { FailureDetail = failureDetail };

    private static bool TryBuildCapturedCheckpoint(
        string saveFolder,
        string generationId,
        FishWarehouseProtectedSnapshot? protectedSnapshot,
        FishWarehouseNativePropertySnapshot? inspection,
        byte[]? nativeBytes,
        out FishWarehouseReplayCheckpoint checkpoint,
        out string? failureReason,
        out string? failureDetail)
    {
        checkpoint = null!;
        failureReason = null;
        failureDetail = null;
        if (string.IsNullOrWhiteSpace(generationId))
        {
            failureReason = "Fish Warehouse capture generation was unavailable.";
            return false;
        }

        try
        {
            var snapshot = protectedSnapshot ?? new FishWarehouseProtectedSnapshot(
                generationId,
                FishWarehouseSaveFolderKey.Create(saveFolder),
                "OrganizedCrime/fish-warehouse.snapshot.json",
                Convert.ToHexString(SHA256.HashData(nativeBytes ?? Array.Empty<byte>())),
                nativeBytes?.Length ?? 0);
            checkpoint = new FishWarehouseReplayCheckpoint(
                generationId,
                FishWarehouseSaveFolderKey.Create(saveFolder),
                FishWarehouseReplayPhase.Captured,
                snapshot.RelativePath,
                snapshot.Sha256,
                inspection?.Objects.Count ?? 0,
                inspection?.Employees.Count ?? 0);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            failureReason = $"Fish Warehouse capture checkpoint could not be created: {ex.Message}";
            failureDetail = ex.ToString();
            return false;
        }
    }

    private static bool TryReadNativePropertyBytes(string saveFolder, out byte[] bytes, out string? failureReason, out string? failureDetail)
    {
        bytes = Array.Empty<byte>();
        failureDetail = null;
        if (!FishWarehouseSavePath.TryResolveNativePropertyPath(
                saveFolder,
                RuntimePropertyDefinition.FishWarehouse,
                out var nativePropertyPath,
                out failureReason))
            return false;

        try
        {
            bytes = File.ReadAllBytes(nativePropertyPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            failureReason = $"Fish Warehouse native property bytes could not be read: {ex.Message}";
            failureDetail = ex.ToString();
            return false;
        }
    }

    private FishWarehousePropertyCaptureResult SetResult(FishWarehousePropertyCaptureResult result)
    {
        _latestResult = result;
        return result;
    }
}
