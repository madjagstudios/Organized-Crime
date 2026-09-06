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
    string? FailureReason = null);

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
                attempt.FailureReason ?? "Fish Warehouse capture prerequisites were not all satisfied.");

    public static FishWarehousePropertyCaptureResult EvaluateLoadCompleteRetry(FishWarehousePropertyCaptureAttempt attempt) =>
        attempt.AllSucceeded
            ? new FishWarehousePropertyCaptureResult(FishWarehousePropertyCaptureState.Captured)
            : new FishWarehousePropertyCaptureResult(
                FishWarehousePropertyCaptureState.Degraded,
                attempt.FailureReason ?? "Fish Warehouse capture recovery prerequisites were not all satisfied.");

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
        var attempt = TryCaptureNativeProperty(saveFolder, propertyData, generationId, out var protectedSnapshot, out var inspection, out var nativeBytes);
        if (!attempt.ByteProtectionSucceeded || !attempt.InspectionSucceeded || !attempt.TypedPayloadRetentionSucceeded)
            return RecordInitialFailure(saveFolder, generationId, attempt, protectedSnapshot, nativeBytes, inspection);

        CapturedEmployeeRecords = inspection!.Employees;

        if (!TryBuildCapturedCheckpoint(saveFolder, generationId, protectedSnapshot, inspection, nativeBytes, out var checkpoint, out var checkpointFailure))
            return RecordInitialFailure(saveFolder, generationId, attempt with { FailureReason = checkpointFailure }, protectedSnapshot, nativeBytes, inspection);

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
            if (!_replayLifecycle.Begin(checkpoint, out var beginFailure))
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

        _latestAttempt = attempt with { CheckpointPersistenceSucceeded = true, FailureReason = null };
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

    internal FishWarehousePropertyCaptureResult RecordCaptureFailure(
        string saveFolder,
        string generationId,
        string failureReason)
    {
        _generationId = generationId;
        CapturedPropertyData = null;
        return RecordInitialFailure(saveFolder, generationId, FailureAttempt(failureReason), null, null);
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

        if (!TryBuildCapturedCheckpoint(saveFolder, _generationId, protectedSnapshot, inspection, nativeBytes, out var checkpoint, out var checkpointFailure))
            return RecordRetryFailure(attempt with { FailureReason = checkpointFailure });

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
        if (!TryReadNativePropertyBytes(saveFolder, out nativeBytes, out var readFailure))
            return new FishWarehousePropertyCaptureAttempt(false, false, typedPayloadRetentionSucceeded, false, readFailure);

        if (!_snapshotStore.TryProtect(saveFolder, generationId, nativeBytes, out var snapshot, out var protectFailure))
            return new FishWarehousePropertyCaptureAttempt(false, false, typedPayloadRetentionSucceeded, false, protectFailure);

        protectedSnapshot = snapshot;
        if (!_inspector.TryInspect(nativeBytes, out var inspectedSnapshot, out var inspectionFailure))
            return new FishWarehousePropertyCaptureAttempt(true, false, typedPayloadRetentionSucceeded, false, inspectionFailure);

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

    private FishWarehousePropertyCaptureResult RecordInitialFailure(
        string saveFolder,
        string generationId,
        FishWarehousePropertyCaptureAttempt attempt,
        FishWarehouseProtectedSnapshot? protectedSnapshot,
        byte[]? nativeBytes,
        FishWarehouseNativePropertySnapshot? inspection = null)
    {
        var failureReason = attempt.FailureReason ?? "Fish Warehouse native property capture failed.";
        if (TryBuildCapturedCheckpoint(saveFolder, generationId, protectedSnapshot, inspection, nativeBytes, out var checkpoint, out _))
        {
            if (_replayLifecycle.Begin(checkpoint, out _))
            {
                _replayLifecycle.Fail(FishWarehouseReplayPhase.Captured, failureReason);
                attempt = attempt with
                {
                    CheckpointPersistenceSucceeded = _persistenceService.TryPersistReplayCheckpoint(_replayLifecycle.CurrentCheckpoint!),
                    FailureReason = failureReason
                };
            }
        }

        _latestAttempt = attempt;
        _persistedCapturedCheckpoint = null;
        _log($"WARNING: Fish Warehouse native property capture failed; native load remains suppressed and recovery is required: {failureReason}");
        return SetResult(FishWarehousePropertyCapturePolicy.EvaluateInitialCapture(attempt));
    }

    private FishWarehousePropertyCaptureResult RecordRetryFailure(FishWarehousePropertyCaptureAttempt attempt)
    {
        _latestAttempt = attempt;
        _persistedCapturedCheckpoint = null;
        IsDegraded = true;
        var result = FishWarehousePropertyCapturePolicy.EvaluateLoadCompleteRetry(attempt);
        _log($"WARNING: Fish Warehouse capture recovery failed; normal Fish Warehouse saves are blocked to preserve the native property file: {result.FailureReason}");
        return SetResult(result);
    }

    private static FishWarehousePropertyCaptureAttempt FailureAttempt(string reason) =>
        new(false, false, false, false, reason);

    private static bool TryBuildCapturedCheckpoint(
        string saveFolder,
        string generationId,
        FishWarehouseProtectedSnapshot? protectedSnapshot,
        FishWarehouseNativePropertySnapshot? inspection,
        byte[]? nativeBytes,
        out FishWarehouseReplayCheckpoint checkpoint,
        out string? failureReason)
    {
        checkpoint = null!;
        failureReason = null;
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
            return false;
        }
    }

    private static bool TryReadNativePropertyBytes(string saveFolder, out byte[] bytes, out string? failureReason)
    {
        bytes = Array.Empty<byte>();
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
            return false;
        }
    }

    private FishWarehousePropertyCaptureResult SetResult(FishWarehousePropertyCaptureResult result)
    {
        _latestResult = result;
        return result;
    }
}
