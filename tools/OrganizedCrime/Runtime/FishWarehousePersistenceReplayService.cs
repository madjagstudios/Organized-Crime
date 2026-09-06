using OrganizedCrime.Persistence;

namespace OrganizedCrime.Runtime;

public enum FishWarehouseEmployeeHomeAdoptionResult
{
    NoneFound,
    Adopted,
    MultipleFound,
    Failed
}

public enum FishWarehouseReplayOperationState
{
    Pending,
    Succeeded,
    Failed
}

public sealed record FishWarehouseReplayOperationResult(
    FishWarehouseReplayOperationState State,
    int Count,
    string? FailureReason,
    IReadOnlyList<string>? InventoryRestoredEmployeeGuids = null)
{
    public static FishWarehouseReplayOperationResult Pending() =>
        new(FishWarehouseReplayOperationState.Pending, 0, null);

    public static FishWarehouseReplayOperationResult Succeeded(int count) =>
        new(FishWarehouseReplayOperationState.Succeeded, count, null);

    public static FishWarehouseReplayOperationResult Failed(string reason) =>
        new(FishWarehouseReplayOperationState.Failed, 0, reason);
}

public interface IFishWarehousePersistenceReplayOperations
{
    bool IsRuntimeReady { get; }
    bool IsNavigationReady { get; }
    void ConfigureReplay(bool objectReplayRequired, int? employeeAgentTypeId);
    void RequestOwnedRestore();
    FishWarehouseReplayOperationResult ReplayObjects();
    FishWarehouseReplayOperationResult MarkObjectsReplayed();
    FishWarehouseReplayOperationResult ReplayEmployees();
    FishWarehouseDeliveryRestoreFlushResult ReleaseDeliveries();
}

public sealed class FishWarehousePersistenceReplayService
{
    private readonly FishWarehouseReplayLifecycle _lifecycle;
    private readonly Func<string?> _activeSaveFolder;
    private readonly Func<string, FishWarehousePropertyCaptureResult> _recoverCapture;
    private readonly Func<bool> _loadState;
    private readonly Func<FishWarehouseSaveState?> _currentState;
    private readonly IFishWarehousePersistenceReplayOperations _operations;
    private readonly Func<FishWarehouseReplayCheckpoint, bool> _persistCheckpoint;
    private readonly Action? _dropTypedReferences;
    private readonly Action? _resetDeliveries;
    private readonly Action? _teardownRuntime;
    private readonly Func<string, string> _saveFolderKey;
    private readonly Action<string> _log;
    private bool _loadCompleteHandled;
    private bool _generationArmed;
    private bool _keepDeliveryFlushAliveAfterFailure;
    private FishWarehouseReplayPhase _phase = FishWarehouseReplayPhase.Inactive;
    private string? _failureReason;

    public static void RunIfAuthoritativeHost(bool isAuthoritativeHost, Action callback)
    {
        if (isAuthoritativeHost)
            callback();
    }

    public FishWarehousePersistenceReplayService(
        FishWarehouseReplayLifecycle lifecycle,
        Func<string?> activeSaveFolder,
        Func<string, FishWarehousePropertyCaptureResult> recoverCapture,
        Func<bool> loadState,
        Func<FishWarehouseSaveState?> currentState,
        IFishWarehousePersistenceReplayOperations operations,
        Func<FishWarehouseReplayCheckpoint, bool> persistCheckpoint,
        Action? dropTypedReferences = null,
        Action? resetDeliveries = null,
        Action? teardownRuntime = null,
        Func<string, string>? saveFolderKey = null,
        Action<string>? log = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _activeSaveFolder = activeSaveFolder ?? throw new ArgumentNullException(nameof(activeSaveFolder));
        _recoverCapture = recoverCapture ?? throw new ArgumentNullException(nameof(recoverCapture));
        _loadState = loadState ?? throw new ArgumentNullException(nameof(loadState));
        _currentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _persistCheckpoint = persistCheckpoint ?? throw new ArgumentNullException(nameof(persistCheckpoint));
        _dropTypedReferences = dropTypedReferences;
        _resetDeliveries = resetDeliveries;
        _teardownRuntime = teardownRuntime;
        _saveFolderKey = saveFolderKey ?? FishWarehouseSaveFolderKey.Create;
        _log = log ?? (_ => { });
    }

    public FishWarehouseReplayPhase Phase => _phase;

    public string? FailureReason => _failureReason;

    public bool RequiresSnapshotProtection =>
        _lifecycle.RequiresSnapshotProtection ||
        ((int)_phase >= (int)FishWarehouseReplayPhase.Captured &&
         (int)_phase <= (int)FishWarehouseReplayPhase.DeliveriesReleased) ||
        _phase == FishWarehouseReplayPhase.Failed;

    public void HandleLoadComplete()
    {
        if (_loadCompleteHandled)
            return;

        _loadCompleteHandled = true;
        var saveFolder = _activeSaveFolder();
        if (string.IsNullOrWhiteSpace(saveFolder))
        {
            FailCurrentPhase("Fish Warehouse replay could not start because the active save folder was unavailable.");
            return;
        }

        FishWarehousePropertyCaptureResult captureResult;
        try
        {
            captureResult = _recoverCapture(saveFolder);
        }
        catch (Exception ex)
        {
            FailCurrentPhase($"capture recovery threw: {ex.Message}");
            return;
        }

        if (captureResult.State is FishWarehousePropertyCaptureState.RecoveryRequired or FishWarehousePropertyCaptureState.Degraded)
        {
            FailCurrentPhase(captureResult.FailureReason ?? "Fish Warehouse capture recovery remained degraded.");
            return;
        }

        bool loaded;
        try
        {
            loaded = _loadState();
        }
        catch (Exception ex)
        {
            FailCurrentPhase($"save state load threw: {ex.Message}");
            return;
        }

        if (!loaded)
            return;

        var state = _currentState();
        if (state is null)
            return;

        try
        {
            if (state.Replay is null || state.Replay.Phase == FishWarehouseReplayPhase.Complete)
            {
                _operations.ConfigureReplay(false, state.EmployeeAgentTypeId);
                _operations.RequestOwnedRestore();
                return;
            }

            if (state.Replay.Phase == FishWarehouseReplayPhase.Failed)
            {
                if (_lifecycle.CurrentCheckpoint is not { Phase: FishWarehouseReplayPhase.Failed } failedCheckpoint ||
                    captureResult.State != FishWarehousePropertyCaptureState.Captured)
                {
                    _phase = FishWarehouseReplayPhase.Failed;
                    _failureReason = state.Replay.FailureReason;
                    return;
                }

                var recoveredCheckpoint = failedCheckpoint with
                {
                    Phase = FishWarehouseReplayPhase.Captured,
                    FailurePhase = null,
                    FailureReason = null
                };
                if (!_lifecycle.TryRecoverCaptureFailure(recoveredCheckpoint, out var recoveryFailure))
                {
                    _phase = FishWarehouseReplayPhase.Failed;
                    _failureReason = recoveryFailure ?? state.Replay.FailureReason;
                    return;
                }

                _log($"Fish Warehouse replay re-armed after failed checkpoint: {failedCheckpoint.FailureReason ?? state.Replay.FailureReason ?? "unknown failure"}.");
                if (!_persistCheckpoint(_lifecycle.CurrentCheckpoint!))
                {
                    _phase = FishWarehouseReplayPhase.Captured;
                    FailCurrentPhase("re-armed replay checkpoint persistence failed.");
                    return;
                }

                _phase = FishWarehouseReplayPhase.Captured;
                _generationArmed = true;
                _operations.ConfigureReplay(true, state.EmployeeAgentTypeId);
                _operations.RequestOwnedRestore();
                return;
            }

            if (captureResult.State != FishWarehousePropertyCaptureState.Captured ||
                !IsActiveSaveFolder(state.Replay.SaveFolderKey, saveFolder))
            {
                FailCurrentPhase("Fish Warehouse replay checkpoint did not match the captured active save folder.");
                return;
            }

            if (_lifecycle.CurrentCheckpoint is null ||
                _lifecycle.CurrentCheckpoint.GenerationId != state.Replay.GenerationId)
            {
                FailCurrentPhase("Fish Warehouse replay generation was not active for the captured save.");
                return;
            }

            _phase = state.Replay.Phase;
            _generationArmed = true;
            _operations.ConfigureReplay(true, state.EmployeeAgentTypeId);
            _operations.RequestOwnedRestore();
        }
        catch (Exception ex)
        {
            FailCurrentPhase($"load-complete replay setup threw: {ex.Message}");
        }
    }

    public void Update()
    {
        if (!_loadCompleteHandled)
            return;

        if (!_generationArmed)
        {
            if (_phase == FishWarehouseReplayPhase.Inactive)
            {
                try
                {
                    _operations.ReleaseDeliveries();
                }
                catch (Exception ex)
                {
                    _log($"Fish Warehouse fresh-load delivery flush failed safely: {ex.Message}");
                }
            }

            return;
        }

        if (_phase == FishWarehouseReplayPhase.Failed)
        {
            if (_keepDeliveryFlushAliveAfterFailure)
                FlushDeliveriesAfterFailure();
            return;
        }

        if (_phase == FishWarehouseReplayPhase.Complete)
            return;

        var activeSaveFolder = _activeSaveFolder();
        if (string.IsNullOrWhiteSpace(activeSaveFolder) ||
            _lifecycle.CurrentCheckpoint is null ||
            !IsActiveSaveFolder(_lifecycle.CurrentCheckpoint.SaveFolderKey, activeSaveFolder))
        {
            FailCurrentPhase("Fish Warehouse replay cannot continue under a different active save folder.");
            return;
        }

        try
        {
            switch (_phase)
            {
                case FishWarehouseReplayPhase.Captured:
                    if (_operations.IsRuntimeReady)
                        TryAdvance(FishWarehouseReplayPhase.RuntimeReady);
                    break;
                case FishWarehouseReplayPhase.RuntimeReady:
                    RunObjectsPhase();
                    break;
                case FishWarehouseReplayPhase.ObjectsReplayed:
                    if (_operations.IsNavigationReady)
                        TryAdvance(FishWarehouseReplayPhase.NavigationReady);
                    break;
                case FishWarehouseReplayPhase.NavigationReady:
                    RunDeliveriesPhase();
                    break;
                case FishWarehouseReplayPhase.EmployeesReplayed:
                    RunDeliveriesPhase();
                    break;
                case FishWarehouseReplayPhase.DeliveriesReleased:
                    RunEmployeesPhase();
                    break;
            }
        }
        catch (Exception ex)
        {
            FailCurrentPhase($"replay update threw: {ex.Message}");
        }
    }

    public void PrepareForLoad()
    {
        _loadCompleteHandled = false;
        _generationArmed = false;
        _phase = FishWarehouseReplayPhase.Inactive;
        _failureReason = null;
        _dropTypedReferences?.Invoke();
        _lifecycle.ResetRuntimeReferences();
        if (_lifecycle.CurrentCheckpoint is null)
        {
            var state = _currentState();
            if (state?.Replay is not null)
                _lifecycle.Begin(state.Replay, out _);
        }

        _resetDeliveries?.Invoke();
        _teardownRuntime?.Invoke();
    }

    private void RunObjectsPhase()
    {
        try
        {
            var result = _operations.ReplayObjects();
            if (result.State == FishWarehouseReplayOperationState.Pending)
                return;
            if (result.State == FishWarehouseReplayOperationState.Failed)
            {
                FailCurrentPhase(result.FailureReason ?? "Fish Warehouse object replay failed.");
                return;
            }

            var marked = _operations.MarkObjectsReplayed();
            if (marked.State == FishWarehouseReplayOperationState.Failed)
            {
                FailCurrentPhase(marked.FailureReason ?? "Fish Warehouse object replay gate could not be marked complete.");
                return;
            }
            if (marked.State == FishWarehouseReplayOperationState.Pending)
                return;

            TryAdvance(FishWarehouseReplayPhase.ObjectsReplayed);
        }
        catch (Exception ex)
        {
            FailCurrentPhase($"object replay threw: {ex.Message}");
        }
    }

    private void RunEmployeesPhase()
    {
        try
        {
            if (!_operations.IsNavigationReady)
                return;

            var result = _operations.ReplayEmployees();
            if (!TryPersistInventoryRestorationMarkers(result.InventoryRestoredEmployeeGuids))
                return;
            if (result.State == FishWarehouseReplayOperationState.Pending)
                return;
            if (result.State == FishWarehouseReplayOperationState.Failed)
            {
                FailCurrentPhase(result.FailureReason ?? "Fish Warehouse employee replay failed.");
                return;
            }

            TryAdvance(_phase == FishWarehouseReplayPhase.DeliveriesReleased
                ? FishWarehouseReplayPhase.Complete
                : FishWarehouseReplayPhase.EmployeesReplayed);
        }
        catch (Exception ex)
        {
            FailCurrentPhase($"employee replay threw: {ex.Message}");
        }
    }

    private void RunDeliveriesPhase()
    {
        try
        {
            var result = _operations.ReleaseDeliveries();
            if (result.Failure is not null)
            {
                FailCurrentPhase(result.Failure);
                return;
            }

            if (result.PendingCount != 0)
                return;

            TryAdvance(FishWarehouseReplayPhase.DeliveriesReleased);
        }
        catch (Exception ex)
        {
            FailCurrentPhase($"delivery replay threw: {ex.Message}");
        }
    }

    private void TryAdvance(FishWarehouseReplayPhase nextPhase)
    {
        if (!_lifecycle.TryAdvance(nextPhase, out var failureReason))
        {
            FailCurrentPhase(failureReason ?? $"Fish Warehouse replay could not advance to {nextPhase}.");
            return;
        }

        if (!_persistCheckpoint(_lifecycle.CurrentCheckpoint!))
        {
            FailCurrentPhase("replay checkpoint persistence failed.");
            return;
        }

        _phase = nextPhase;
    }

    private void FailCurrentPhase(string reason)
    {
        _failureReason = reason;
        var failurePhase = _phase == FishWarehouseReplayPhase.Inactive
            ? FishWarehouseReplayPhase.Captured
            : _phase;
        _lifecycle.Fail(failurePhase, reason);
        if (_lifecycle.CurrentCheckpoint is not null)
        {
            try
            {
                _persistCheckpoint(_lifecycle.CurrentCheckpoint);
            }
            catch (Exception ex)
            {
                _log($"Fish Warehouse failed replay checkpoint persistence threw: {ex.Message}");
            }
        }

        _phase = FishWarehouseReplayPhase.Failed;
        _keepDeliveryFlushAliveAfterFailure = failurePhase == FishWarehouseReplayPhase.DeliveriesReleased;
    }

    private bool TryPersistInventoryRestorationMarkers(IReadOnlyList<string>? employeeGuids)
    {
        if (employeeGuids is null || employeeGuids.Count == 0)
            return true;

        var existing = (_lifecycle.CurrentCheckpoint?.InventoryRestoredEmployeeGuids ?? Array.Empty<string>())
            .Select(CanonicalGuid)
            .ToHashSet(StringComparer.Ordinal);
        var newEmployeeGuids = employeeGuids
            .Select(CanonicalGuid)
            .Where(guid => !existing.Contains(guid))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (newEmployeeGuids.Length == 0)
            return true;

        if (!_lifecycle.RecordInventoryRestoredEmployeeGuids(newEmployeeGuids, out var failureReason))
        {
            FailCurrentPhase(failureReason ?? "inventory restoration marker could not be recorded.");
            return false;
        }

        if (!_persistCheckpoint(_lifecycle.CurrentCheckpoint!))
        {
            FailCurrentPhase("inventory restoration marker persistence failed.");
            return false;
        }

        return true;
    }

    private static string CanonicalGuid(string value) =>
        System.Guid.TryParse(value, out var parsed) ? parsed.ToString("D") : value;

    private void FlushDeliveriesAfterFailure()
    {
        try
        {
            var result = _operations.ReleaseDeliveries();
            if (result.Failure is not null)
                _log($"Fish Warehouse retained delivery flush failed after replay failure: {result.Failure}");
        }
        catch (Exception ex)
        {
            _log($"Fish Warehouse retained delivery flush threw after replay failure: {ex.Message}");
        }
    }

    private bool IsActiveSaveFolder(string expectedKey, string activeSaveFolder)
    {
        try
        {
            return string.Equals(_saveFolderKey(activeSaveFolder), expectedKey, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            _failureReason = $"active save folder key could not be validated: {ex.Message}";
            return false;
        }
    }
}
