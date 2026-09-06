namespace OrganizedCrime.Runtime;

public sealed class SyndicateHqRuntimeComposition
{
    private readonly SyndicateHqRuntimeService _service;
    private readonly Action _beginLoad;
    private bool _pendingLoadRecovery;
    private bool _deinitializing;

    public SyndicateHqRuntimeComposition(SyndicateHqRuntimeService service, Action beginLoad)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _beginLoad = beginLoad ?? throw new ArgumentNullException(nameof(beginLoad));
    }

    public SyndicateHqRuntimeService Service => _service;
    public bool IsPendingCleanup => _deinitializing && !_service.IsDisposed;

    public bool OnPreLoad()
    {
        if (_deinitializing) return false;
        var succeeded = _service.OnPreLoad();
        if (succeeded)
        {
            _pendingLoadRecovery = false;
            _beginLoad();
        }
        else _pendingLoadRecovery = true;
        return succeeded;
    }

    public bool PrepareLoadComplete()
    {
        if (_deinitializing) return false;
        if (!_pendingLoadRecovery) return true;
        if (!_service.TryTeardown()) return false;
        _beginLoad();
        _pendingLoadRecovery = false;
        return true;
    }

    public bool OnLoadComplete()
    {
        if (!PrepareLoadComplete()) return false;
        // OC-70 final review fix (finding 5): this composition-level entry point is reached only from
        // the mod's real vanilla load-complete handler (OnSaveComplete calls the service's own
        // OnLoadComplete directly, bypassing this method), so evicting the door's retained identity
        // here runs exactly once per real load, never on a save-only boundary.
        _service.EvictRetainedDoorIdentityForLoad();
        return _service.OnLoadComplete();
    }

    public bool OnPreSceneChange() => !_deinitializing && _service.OnPreSceneChange();
    public bool OnSaveStart() => !_deinitializing && _service.OnSaveStart();
    public bool OnSaveComplete() => !_deinitializing && _service.OnSaveComplete();
    public void Update() { if (!_deinitializing) _service.Update(); }
    public void OnGUI() { if (!_deinitializing) _service.OnGUI(); }

    public bool TryDispose()
    {
        _deinitializing = true;
        _service.Dispose();
        if (!_service.IsDisposed) return false;
        _pendingLoadRecovery = false;
        return true;
    }
}
