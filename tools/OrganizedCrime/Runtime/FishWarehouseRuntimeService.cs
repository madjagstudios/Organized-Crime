using OrganizedCrime.Model;
using OrganizedCrime.Persistence;

namespace OrganizedCrime.Runtime;

public sealed class FishWarehouseRuntimeService
{
    private readonly IRuntimePropertyHost _host;
    private readonly Func<bool> _isReady;
    private readonly Func<bool> _tryPlaceEmployeeHome;
    private readonly IFishWarehouseLightingOwnerQa? _lighting;
    private readonly Action<string> _log;
    private bool _restoreRequested;
    private bool _restoreEmployeeHomeRequested;
    private FishWarehouseContinuousNavigationState _lastReportedEmployeeNavigationState =
        FishWarehouseContinuousNavigationState.Inactive;

    public FishWarehouseRuntimeService(
        IRuntimePropertyHost host,
        Func<bool> isReady,
        Func<bool>? tryPlaceEmployeeHome = null,
        Action<string>? log = null,
        IFishWarehouseLightingOwnerQa? lighting = null)
    {
        _host = host;
        _isReady = isReady;
        _tryPlaceEmployeeHome = tryPlaceEmployeeHome ?? (() => false);
        _log = log ?? (_ => { });
        _lighting = lighting;
    }

    public RuntimePropertyLifecycleState State { get; private set; } = RuntimePropertyLifecycleState.Unavailable;
    public bool OwnershipUnlocked => _owned;
    public FishWarehouseContinuousNavigationState EmployeeNavigationState =>
        State == RuntimePropertyLifecycleState.Spawned && _propertyOwnershipApplied
            ? _host.EmployeeNavigationState
            : FishWarehouseContinuousNavigationState.Inactive;
    public bool EmployeeNavigationReady =>
        State == RuntimePropertyLifecycleState.Spawned &&
        _propertyOwnershipApplied &&
        _host.EmployeeNavigationReady;

    private bool _unlocked;
    private bool _owned;
    private bool _propertyOwnershipApplied;
    private bool _employeeHomePlaced;
    private bool _persistenceObjectReplayRequired;

    public FishWarehouseSaveState CaptureSaveState() =>
        new(
            FishWarehouseSaveState.CurrentSchemaVersion,
            FishWarehouseSaveState.ExpectedPropertyCode,
            _unlocked,
            _owned,
            _employeeHomePlaced,
            EmployeeAgentTypeId: _host.EmployeeNavigationAgentTypeId,
            Replay: null,
            UnsupportedEmployees: Array.Empty<FishWarehouseUnsupportedEmployeeRecord>());

    public void ApplyLoadedSaveState(FishWarehouseSaveState state)
    {
        _unlocked = state.Unlocked;
        _owned = state.Owned;
        _employeeHomePlaced = state.EmployeeHomePlaced;
        _persistenceObjectReplayRequired = state.Replay is { } replay &&
            (int)replay.Phase >= (int)FishWarehouseReplayPhase.Captured &&
            (int)replay.Phase <= (int)FishWarehouseReplayPhase.DeliveriesReleased;
        _host.ConfigurePersistenceReplay(
            objectReplayRequired: _persistenceObjectReplayRequired,
            employeeAgentTypeId: state.EmployeeAgentTypeId);
        _propertyOwnershipApplied = false;
        _restoreRequested = false;
        _restoreEmployeeHomeRequested = false;
    }

    public void RequestLoadedOwnedRestore()
    {
        if (_owned)
        {
            _restoreRequested = true;
            _restoreEmployeeHomeRequested = _employeeHomePlaced;
        }
    }

    /// <summary>
    /// Tears down the current runtime instance before the next save load is
    /// applied. The game can reuse this managed mod instance across scene
    /// loads, so leaving the old runtime alive leaves destroyed Unity objects
    /// and stale scene dressing behind.
    /// </summary>
    public bool PrepareForLoad()
    {
        _restoreRequested = false;
        _restoreEmployeeHomeRequested = false;
        _lastReportedEmployeeNavigationState = FishWarehouseContinuousNavigationState.Inactive;

        if (State == RuntimePropertyLifecycleState.Spawned)
        {
            var unload = Stop();
            if (!unload.CleanupPassed || State != RuntimePropertyLifecycleState.Unavailable)
            {
                // OC-108: loads only happen from the main menu, so the previous scene is
                // already unloaded and its objects destroyed. A partial cleanup cannot be
                // retried; abandon the stale runtime so the new load can restore cleanly.
                _log($"Fish Warehouse runtime Property from the previous scene was not fully cleaned ({unload.FailureReason ?? "cleanup was incomplete"}); abandoning it for the new load.");
                _host.AbandonAfterSceneUnload();
                State = RuntimePropertyLifecycleState.Unavailable;
            }
        }
        else if (State is RuntimePropertyLifecycleState.Starting or RuntimePropertyLifecycleState.Unloading)
        {
            _log($"Fish Warehouse runtime Property could not prepare for save load while in state {State}.");
            return false;
        }
        else if (State is RuntimePropertyLifecycleState.Ready or RuntimePropertyLifecycleState.Failed)
        {
            // These states have no live host instance to unload. Resetting at
            // the explicit load boundary allows the next OnLoadComplete event
            // to perform a clean readiness transition. A Failed runtime may still
            // hold a pending cleanup from the unloaded scene, so drop it too (OC-108).
            if (State == RuntimePropertyLifecycleState.Failed)
                _host.AbandonAfterSceneUnload();
            State = RuntimePropertyLifecycleState.Unavailable;
        }

        _unlocked = false;
        _owned = false;
        _propertyOwnershipApplied = false;
        _employeeHomePlaced = false;
        _persistenceObjectReplayRequired = false;
        return true;
    }

    public void Update()
    {
        if (State == RuntimePropertyLifecycleState.Spawned && _propertyOwnershipApplied)
        {
            _host.UpdateOwnedFeatures();
            ReportEmployeeNavigationState();
        }

        if (State == RuntimePropertyLifecycleState.Unavailable)
        {
            if (!_isReady())
                return;

            TransitionTo(RuntimePropertyLifecycleState.Ready);
            OrganizedCrimeLog.Diagnostic("Fish Warehouse runtime Property is ready for developer lifecycle validation.", _log);
        }

        if (State != RuntimePropertyLifecycleState.Ready || !_restoreRequested)
            return;

        _restoreRequested = false;
        OrganizedCrimeLog.Diagnostic("Restoring the owned Fish Warehouse runtime Property from the loaded save state.", _log);
        var start = Start();
        if (start.Succeeded)
            TryUnlockDeveloperLifecycle();
    }

    public RuntimePropertyHostResult ToggleDeveloperLifecycle()
    {
        if (State == RuntimePropertyLifecycleState.Ready)
            return Start();

        if (State == RuntimePropertyLifecycleState.Spawned)
            return Stop();

        return RuntimePropertyHostResult.Failure(
            "state",
            $"Developer lifecycle toggle is unavailable in state {State}.");
    }

    /// <summary>
    /// Whether the Chapter 3 story purchase can acquire the warehouse: the
    /// runtime can spawn from <see cref="RuntimePropertyLifecycleState.Ready"/>,
    /// or it is already spawned and unowned.
    /// </summary>
    public bool CanAcquireForStory =>
        State == RuntimePropertyLifecycleState.Ready ||
        (State == RuntimePropertyLifecycleState.Spawned && !_propertyOwnershipApplied);

    /// <summary>
    /// The Chapter 3 story acquisition: Start from Ready (or reuse an
    /// already-spawned unowned runtime), then the ownership unlock. The same
    /// calls as the load-restore path, under a production name so story logic
    /// never goes through the dev-key method. A failed unlock leaves the
    /// service terminal, exactly like the dev path.
    /// </summary>
    public bool TryAcquireForStory()
    {
        if (State == RuntimePropertyLifecycleState.Spawned && _propertyOwnershipApplied)
            return true;
        if (State == RuntimePropertyLifecycleState.Ready)
        {
            var start = Start();
            if (!start.Succeeded)
                return false;
        }
        if (State != RuntimePropertyLifecycleState.Spawned || _propertyOwnershipApplied)
        {
            _log($"Fish Warehouse story acquire is unavailable in state {State}.");
            return false;
        }
        return TryUnlockOwnership();
    }

    public bool TryUnlockDeveloperLifecycle()
    {
        _restoreRequested = false;
        if (State != RuntimePropertyLifecycleState.Spawned)
        {
            _log($"Fish Warehouse ownership unlock is unavailable in state {State}.");
            return false;
        }

        if (_propertyOwnershipApplied)
        {
            return true;
        }

        var passed = TryUnlockOwnership();
        if (passed)
        {
            if (_restoreEmployeeHomeRequested && !_persistenceObjectReplayRequired)
            {
                _restoreEmployeeHomeRequested = false;
                TryPlaceEmployeeHome();
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// The shared ownership unlock behind the dev key, the load restore, and
    /// the story acquisition: native own, flags on success, terminal cleanup
    /// on failure. Restore-only follow-ups stay with the callers.
    /// </summary>
    private bool TryUnlockOwnership()
    {
        var passed = _host.TrySetOwned();
        if (passed)
        {
            _unlocked = true;
            _owned = true;
            _propertyOwnershipApplied = true;
            _lastReportedEmployeeNavigationState = FishWarehouseContinuousNavigationState.Inactive;
            return true;
        }

        TransitionTo(RuntimePropertyLifecycleState.Unloading);
        var cleanup = _host.Stop();
        TransitionTo(RuntimePropertyLifecycleState.Failed);
        _log(cleanup.CleanupPassed
            ? "Fish Warehouse ownership unlock failed; runtime Property was cleaned up and the service is terminal for this process."
            : "Fish Warehouse ownership unlock and cleanup failed; the service is terminal for this process.");
        return false;
    }

    public bool TryPlaceEmployeeHome()
    {
        if (State != RuntimePropertyLifecycleState.Spawned || !_propertyOwnershipApplied)
        {
            _log("Fish Warehouse employee-home placement is unavailable until the runtime Property is spawned and owned.");
            return false;
        }

        var passed = _tryPlaceEmployeeHome();
        if (passed)
        {
            _employeeHomePlaced = true;
        }
        if (!passed)
            _log("Fish Warehouse employee-home placement did not pass its guarded runtime checks.");
        return passed;
    }

    public bool TryLightingAddLamp() =>
        WhenLightingActive(lighting => lighting.TryAddLampAbovePlayer(), "lamp add");

    public bool TryLightingRemoveLamp() =>
        WhenLightingActive(lighting => lighting.TryRemoveMostRecentLamp(), "lamp remove");

    public bool TryLightingRaiseLamp() =>
        WhenLightingActive(
            lighting => lighting.TryAdjustMostRecentLampHeight(FishWarehouseLightingDefinition.HeightStep),
            "lamp height");

    public bool TryLightingLowerLamp() =>
        WhenLightingActive(
            lighting => lighting.TryAdjustMostRecentLampHeight(-FishWarehouseLightingDefinition.HeightStep),
            "lamp height");

    public bool TryLightingDimLamps() =>
        WhenLightingActive(
            lighting => lighting.TryAdjustAllLampIntensity(-FishWarehouseLightingDefinition.IntensityStep),
            "lamp intensity");

    public bool TryLightingBrightenLamps() =>
        WhenLightingActive(
            lighting => lighting.TryAdjustAllLampIntensity(FishWarehouseLightingDefinition.IntensityStep),
            "lamp intensity");

    public bool TryLightingToggleLampYaw() =>
        WhenLightingActive(lighting => lighting.TryToggleMostRecentLampYaw(), "lamp yaw");

    public bool TryLightingLogLayout() =>
        WhenLightingActive(lighting => lighting.TryLogLampLayout(), "lamp layout");

    private bool WhenLightingActive(Func<IFishWarehouseLightingOwnerQa, bool> action, string verb)
    {
        if (State != RuntimePropertyLifecycleState.Spawned || !_propertyOwnershipApplied || _lighting is null || !_lighting.LightingActive)
        {
            _log($"OC-109 {verb} refused: the warehouse is not spawned and owned with an active room");
            return false;
        }

        return action(_lighting);
    }

    public void MarkPersistenceObjectsReplayed()
    {
        _host.MarkPersistenceObjectsReplayed();
        _persistenceObjectReplayRequired = false;
        _restoreEmployeeHomeRequested = false;
    }

    public void MarkPersistenceEmployeeHomeRestored() => _employeeHomePlaced = true;

    private RuntimePropertyHostResult Start()
    {
        TransitionTo(RuntimePropertyLifecycleState.Starting);
        var result = _host.Start(RuntimePropertyDefinition.FishWarehouse);
        if (result.Succeeded)
        {
            _lastReportedEmployeeNavigationState = FishWarehouseContinuousNavigationState.Inactive;
            TransitionTo(RuntimePropertyLifecycleState.Spawned);
            OrganizedCrimeLog.Diagnostic("Fish Warehouse runtime Property is spawned.", _log);
        }
        else
        {
            TransitionTo(RuntimePropertyLifecycleState.Failed);
            _log($"Fish Warehouse runtime Property start failed at {result.Stage}: {result.FailureReason}");
        }

        return result;
    }

    private RuntimePropertyHostResult Stop()
    {
        TransitionTo(RuntimePropertyLifecycleState.Unloading);
        OrganizedCrimeLog.Diagnostic("Unloading Fish Warehouse runtime Property.", _log);
        var result = _host.Stop();
        if (string.Equals(result.Stage, "occupied-loading-docks", StringComparison.Ordinal))
        {
            TransitionTo(RuntimePropertyLifecycleState.Spawned);
            _log($"Fish Warehouse unload refused: {result.FailureReason}");
            return result;
        }

        if (string.Equals(result.Stage, "pending-native-navigation-cleanup", StringComparison.Ordinal))
        {
            TransitionTo(RuntimePropertyLifecycleState.Spawned);
            _log($"Fish Warehouse unload deferred: {result.FailureReason}");
            return result;
        }

        if (result.CleanupPassed)
        {
            TransitionTo(RuntimePropertyLifecycleState.Unavailable);
            OrganizedCrimeLog.Diagnostic("Fish Warehouse runtime Property unloaded cleanly.", _log);
        }
        else
        {
            TransitionTo(RuntimePropertyLifecycleState.Failed);
            _log($"Fish Warehouse runtime Property cleanup failed: {result.FailureReason}");
        }

        return result;
    }

    private void ReportEmployeeNavigationState()
    {
        var current = EmployeeNavigationState;
        if (current == _lastReportedEmployeeNavigationState)
            return;

        _lastReportedEmployeeNavigationState = current;
        switch (current)
        {
            case FishWarehouseContinuousNavigationState.Ready:
                OrganizedCrimeLog.Diagnostic("Fish Warehouse employee navigation is ready.", _log);
                break;
            case FishWarehouseContinuousNavigationState.Failed:
                _log("Fish Warehouse employee navigation failed closed; property ownership and existing features remain active.");
                break;
        }
    }

    private void TransitionTo(RuntimePropertyLifecycleState next)
    {
        if (!RuntimePropertyLifecycle.CanTransition(State, next))
            throw new InvalidOperationException($"Invalid runtime Property transition: {State} -> {next}.");

        State = next;
    }
}
