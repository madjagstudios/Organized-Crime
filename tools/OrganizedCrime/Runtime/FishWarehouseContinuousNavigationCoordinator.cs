namespace OrganizedCrime.Runtime;

public enum FishWarehouseContinuousNavigationState
{
    Inactive,
    Settling,
    Ready,
    Failed
}

public interface IFishWarehouseContinuousNavigationActions
{
    bool TryPreflightGeometry();
    bool TryActivateRoom();
    bool TryOpenGarage();
    string GetBuildableAndConfigurableSignature();
    FishWarehouseNativeNavigationBuildResult TryBuildNavigation();
    bool ValidateNavigation();
    bool RemoveNavigation();
    void RestoreGarage();
    void DisposeRoom();
}

/// <summary>
/// Pure, fakeable lifecycle sequencing for the RuntimePropertyHost-owned
/// navigation lifecycle. Native teardown retries only on explicit Stop calls.
/// </summary>
public sealed class FishWarehouseContinuousNavigationCoordinator
{
    public const float StableSettleSeconds = 0.5f;
    public const float SettleTimeoutSeconds = 5f;

    private readonly IFishWarehouseContinuousNavigationActions _actions;
    private string? _lastSignature;
    private float _startedAt;
    private float _signatureStableSince;
    private bool _rollbackRequired;
    private bool _rolledBack;
    private bool _hasPostStartTick;

    public FishWarehouseContinuousNavigationCoordinator(IFishWarehouseContinuousNavigationActions actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public FishWarehouseContinuousNavigationState State { get; private set; }

    public bool HasPendingCleanup => _rollbackRequired && !_rolledBack;

    public void Start(float now)
    {
        if (State != FishWarehouseContinuousNavigationState.Inactive)
            return;

        ResetRun(now);
        if (!_actions.TryPreflightGeometry())
        {
            State = FishWarehouseContinuousNavigationState.Failed;
            return;
        }

        if (!_actions.TryActivateRoom() || !_actions.TryOpenGarage())
        {
            FailAfterMutation();
            return;
        }

        _rollbackRequired = true;
        _lastSignature = _actions.GetBuildableAndConfigurableSignature();
        _signatureStableSince = now;
        State = FishWarehouseContinuousNavigationState.Settling;
    }

    public void Tick(float now)
    {
        if (State != FishWarehouseContinuousNavigationState.Settling)
            return;

        if (!_hasPostStartTick)
        {
            _hasPostStartTick = true;
            if (now - _startedAt > SettleTimeoutSeconds)
            {
                _startedAt = now;
                _signatureStableSince = now;
            }
        }

        if (now - _startedAt >= SettleTimeoutSeconds)
        {
            FailAfterMutation();
            return;
        }

        string signature = _actions.GetBuildableAndConfigurableSignature();
        if (!string.Equals(signature, _lastSignature, StringComparison.Ordinal))
        {
            _lastSignature = signature;
            _signatureStableSince = now;
            return;
        }

        if (now - _signatureStableSince < StableSettleSeconds)
            return;

        FishWarehouseNativeNavigationBuildResult buildResult = _actions.TryBuildNavigation();
        if (buildResult == FishWarehouseNativeNavigationBuildResult.Pending)
            return;

        if (buildResult != FishWarehouseNativeNavigationBuildResult.Succeeded || !_actions.ValidateNavigation())
        {
            FailAfterMutation();
            return;
        }

        State = FishWarehouseContinuousNavigationState.Ready;
    }

    public bool Stop()
    {
        if (!_rollbackRequired)
            return true;

        if (!Rollback())
            return false;

        if (State != FishWarehouseContinuousNavigationState.Failed)
            State = FishWarehouseContinuousNavigationState.Inactive;
        return true;
    }

    private void FailAfterMutation()
    {
        _rollbackRequired = true;
        Rollback();
        State = FishWarehouseContinuousNavigationState.Failed;
    }

    private void ResetRun(float now)
    {
        _lastSignature = null;
        _startedAt = now;
        _signatureStableSince = now;
        _rollbackRequired = false;
        _rolledBack = false;
        _hasPostStartTick = false;
    }

    private bool Rollback()
    {
        if (_rolledBack)
            return true;

        if (!_actions.RemoveNavigation())
            return false;

        _actions.RestoreGarage();
        _actions.DisposeRoom();
        _rolledBack = true;
        return true;
    }
}
