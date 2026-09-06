using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.Doors;
using Il2CppScheduleOne.Interaction;
using UnityEngine;
using UnityEngine.Events;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public interface ISyndicateHqListenerRegistration
{
    string StableIdentity { get; }
    bool TryRemove(out string? failure);
}

public interface ISyndicateHqDoorSource
{
    SyndicateHqDoorFingerprint? CurrentFingerprint { get; }
    long Generation { get; }
    int SubscriptionCount { get; }
    bool TryResolveFresh(out SyndicateHqDoorFingerprint fingerprint);
    bool TryAttach();
    bool Detach();
    bool IsCurrent(SyndicateHqDoorFingerprint fingerprint, long generation);
    bool TryDispose();
    void EvictRetainedForLoad();
}

public sealed class SyndicateHqListenerRegistry
{
    private readonly Dictionary<string, ISyndicateHqListenerRegistration> _registrations = new(StringComparer.Ordinal);

    public int Count => _registrations.Count;

    public bool TryTrack(ISyndicateHqListenerRegistration registration)
    {
        if (registration is null || string.IsNullOrWhiteSpace(registration.StableIdentity) || _registrations.ContainsKey(registration.StableIdentity))
            return false;
        _registrations.Add(registration.StableIdentity, registration);
        return true;
    }

    public bool RemoveAll(Action<string>? log = null)
    {
        foreach (var pair in _registrations.ToArray())
        {
            try
            {
                if (pair.Value.TryRemove(out var failure))
                {
                    _registrations.Remove(pair.Key);
                    continue;
                }
                log?.Invoke($"HQ listener removal remained pending for {pair.Key}: {failure ?? "unknown failure"}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"HQ listener removal threw for {pair.Key}: {ex.Message}");
            }
        }
        return _registrations.Count == 0;
    }
}

public sealed class SyndicateHqDoorSubscriptionState
{
    private readonly SyndicateHqListenerRegistry _registry = new();
    private bool _detachRequested;
    private bool _disposed;

    public long Generation { get; private set; }
    public int SubscriptionCount => _registry.Count;
    public bool IsDetachRequested => _detachRequested;
    public bool IsDisposed => _disposed;
    public bool CanAttach => !_disposed && !(_detachRequested && SubscriptionCount > 0);
    public bool TryTrack(ISyndicateHqListenerRegistration registration) => _registry.TryTrack(registration);
    public long BeginAttach() { _detachRequested = false; return ++Generation; }
    public void RequestDetach() { _detachRequested = true; ++Generation; }
    public bool TryRemoveAll(Action<string>? log = null) => _registry.RemoveAll(log);
    public void MarkDisposed() => _disposed = true;
}

public sealed record SyndicateHqDoorResolution(SyndicateHqDoorFingerprint Fingerprint, InteractableObject InteractableObject);

public sealed class SyndicateHqDoorObserver : ISyndicateHqDoorSource, IDisposable
{
    private readonly Action<SyndicateHqDoorFingerprint, long> _onInteraction;
    private readonly Action<string> _log;
    private readonly SyndicateHqDoorSubscriptionState _subscriptionState = new();
    private SyndicateHqDoorResolution? _resolution;

    public OrganizedCrimeTimingReceipts Timing { get; set; } = OrganizedCrimeTimingReceipts.Disabled;

    /// <summary>
    /// OC-70: the door's resolved identity, retained independently of the listener subscription state
    /// <see cref="_resolution"/> tracks (detached and re-attached every boundary, OC-63's own decision).
    /// Cleared only when <see cref="TryResolveRetainedOrScan"/> finds it destroyed.
    /// </summary>
    private SyndicateHqDoorResolution? _retainedResolution;

    public SyndicateHqDoorObserver(Action<SyndicateHqDoorFingerprint, long> onInteraction, Action<string>? log = null)
    {
        _onInteraction = onInteraction ?? throw new ArgumentNullException(nameof(onInteraction));
        _log = log ?? (_ => { });
    }

    public SyndicateHqDoorFingerprint? CurrentFingerprint => _resolution?.Fingerprint;
    public long Generation => _subscriptionState.Generation;
    public int SubscriptionCount => _subscriptionState.SubscriptionCount;
    public bool IsCurrent(SyndicateHqDoorFingerprint fingerprint, long generation) =>
        !_subscriptionState.IsDisposed && !_subscriptionState.IsDetachRequested && generation == _subscriptionState.Generation && ReferenceEquals(_resolution?.Fingerprint, fingerprint) && _subscriptionState.SubscriptionCount == 1;

    /// <summary>
    /// OC-70 final review fix (finding 2). Its only caller,
    /// <see cref="SyndicateHqRuntimeService.TryReturnForTeardown"/>, resolves through here exactly when
    /// <c>_recoveryRequiresFreshAccessPoint</c> is set, i.e. after a teardown-time return already
    /// failed once, which is exactly the case where the world may have changed under the mod and the
    /// planned teleport must not target a retained-but-superseded access point. Evicting
    /// <see cref="_retainedResolution"/> first forces <see cref="TryResolveRetainedOrScan"/> through
    /// <see cref="TryResolveExact"/>'s full scene scan and identity check every time this is called,
    /// so the name and the contract agree: this always resolves fresh, never a cached hit.
    /// </summary>
    public bool TryResolveFresh(out SyndicateHqDoorFingerprint fingerprint)
    {
        if (_subscriptionState.IsDisposed)
        {
            fingerprint = default!;
            return false;
        }

        EvictRetainedForLoad();
        if (!TryResolveRetainedOrScan(out var resolution, out _) || resolution is null)
        {
            fingerprint = default!;
            return false;
        }

        fingerprint = resolution.Fingerprint;
        return true;
    }

    public bool TryAttach()
    {
        if (!_subscriptionState.CanAttach) return false;
        if (_resolution is not null && _subscriptionState.SubscriptionCount == 1) return true;

        if (!TryResolveRetainedOrScan(out var resolution, out var failure) || resolution is null)
        {
            _log(failure);
            return false;
        }

        try
        {
            var resolved = resolution;
            var generation = _subscriptionState.BeginAttach();
            var listener = DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => Dispatch(resolved.Fingerprint, generation)));
            if (listener is null) throw new InvalidOperationException("UnityAction conversion returned null.");
            var registration = new SyndicateHqDoorListenerRegistration(resolved.Fingerprint.DoorPath, resolved.InteractableObject, listener);
            if (!_subscriptionState.TryTrack(registration))
                throw new InvalidOperationException("Duplicate exact-door listener registration was rejected.");
            _resolution = resolved;
            registration.MarkAdded();
            resolved.InteractableObject.onInteractStart.AddListener(listener);
            return true;
        }
        catch (Exception ex)
        {
            _log($"Exact Syndicate HQ door listener registration failed: {ex.Message}");
            _subscriptionState.RequestDetach();
            _subscriptionState.TryRemoveAll(_log);
            if (_subscriptionState.SubscriptionCount == 0) _resolution = null;
            return false;
        }
    }

    public bool Detach()
    {
        _subscriptionState.RequestDetach();
        var detached = _subscriptionState.TryRemoveAll(_log);
        if (detached) _resolution = null;
        return detached;
    }

    public bool TryDispose()
    {
        if (_subscriptionState.IsDisposed) return true;
        if (!Detach()) return false;
        _subscriptionState.MarkDisposed();
        return true;
    }

    void IDisposable.Dispose() => TryDispose();

    private void Dispatch(SyndicateHqDoorFingerprint fingerprint, long generation)
    {
        try { if (IsCurrent(fingerprint, generation)) _onInteraction(fingerprint, generation); }
        catch (Exception ex) { _log($"Syndicate HQ door callback was isolated: {ex.Message}"); }
    }

    /// <summary>
    /// OC-70 final review fix (finding 5). Evicts <see cref="_retainedResolution"/> so the very next
    /// <see cref="TryResolveRetainedOrScan"/> call is forced through <see cref="TryResolveExact"/> (the
    /// <c>doors.Length != 1</c> duplicate-identity check and <see cref="SyndicateHqIdentity.Validate"/>
    /// included), instead of trusting a possibly-stale retained reference. Call exactly once at each
    /// real vanilla load boundary (<see cref="SyndicateHqRuntimeComposition.OnLoadComplete"/>, which
    /// unlike <see cref="SyndicateHqRuntimeService.OnSaveComplete"/>'s own internal fallback is reached
    /// only from the mod's real load-complete handler), so a vanilla restore that produces a second
    /// in-scene door object for the same identity is still caught. Retention resumes immediately after:
    /// the forced fresh scan repopulates <see cref="_retainedResolution"/>, so every save-only boundary
    /// in between two loads keeps the OC-70 performance win untouched.
    /// </summary>
    public void EvictRetainedForLoad() => _retainedResolution = null;

    /// <summary>
    /// OC-70. Reuses <see cref="_retainedResolution"/> when it is still alive (the
    /// <see cref="UnityEngine.Object"/> overload of <see cref="IsUnityNull(UnityEngine.Object)"/>, the
    /// same overload <see cref="SyndicateHqNativeStorageBoundary"/> pins for the same reason); only
    /// falls through to the full scene scan in <see cref="TryResolveExact"/> when it is null or reads
    /// destroyed. Wrapped in one timing receipt so a cache hit and a cache miss are both visible in the
    /// morning log at whatever threshold the owner sets.
    /// </summary>
    private bool TryResolveRetainedOrScan(out SyndicateHqDoorResolution? resolution, out string failure)
    {
        SyndicateHqDoorResolution? found = null;
        var reason = string.Empty;
        var ok = Timing.Measure("hq/door-resolve", () =>
        {
            if (_retainedResolution is not null && !IsUnityNull(_retainedResolution.InteractableObject))
            {
                found = _retainedResolution;
                reason = "Retained the exact Nightclub door across the boundary.";
                return true;
            }

            if (!TryResolveExact(out var fresh, out var scanFailure))
            {
                reason = scanFailure;
                return false;
            }

            _retainedResolution = fresh;
            found = fresh;
            reason = "Resolved the exact Nightclub door by a fresh scene scan.";
            return true;
        });
        resolution = found;
        failure = reason;
        return ok;
    }

    public static bool TryResolveExact(
        out SyndicateHqDoorResolution? resolution, out string failure)
    {
        resolution = null;
        try
        {
            var shellPaths = Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(gameObject => !IsUnityNull(gameObject) && gameObject.scene.IsValid())
                .Select(gameObject => GetTransformPath(gameObject.transform))
                .Where(path => string.Equals(path, SyndicateHqContract.NightclubShellPath, StringComparison.Ordinal))
                .ToArray();
            var doors = Resources.FindObjectsOfTypeAll<StaticDoor>()
                .Where(door => !IsUnityNull(door) && door.gameObject.scene.IsValid())
                .Where(door => string.Equals(GetTransformPath(door.transform), SyndicateHqContract.NightclubDoorPath, StringComparison.Ordinal))
                .ToArray();

            var snapshots = doors.Select(Capture).ToArray();
            var check = SyndicateHqIdentity.Validate(new(shellPaths, snapshots));
            if (!check.Accepted || doors.Length != 1)
            {
                failure = check.Reason;
                return false;
            }

            var interactable = doors[0].IntObj;
            if (IsUnityNull(interactable))
            {
                failure = "Exact Nightclub door IntObj was unavailable.";
                return false;
            }
            resolution = new SyndicateHqDoorResolution(check.Door!, interactable);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = $"Exact Nightclub door resolution threw: {ex.Message}";
            return false;
        }
    }

    private static SyndicateHqDoorFingerprint Capture(StaticDoor door)
    {
        var interactable = door.IntObj as Component;
        var building = GetMemberValue(door, "Building") as Component;
        var accessPoint = GetMemberValue(door, "AccessPoint") as Component;
        return new(
            GetTransformPath(door.transform), TypeName(door),
            GetTransformPath(interactable?.transform), TypeName(interactable),
            GetTransformPath(building?.transform), TypeName(building), ReadMemberText(building, "GUID"),
            GetTransformPath(accessPoint?.transform), ReadInt(door, "doorIndex"),
            ToValue(door.transform.position), ToValue(door.transform.forward),
            accessPoint is null ? null : ToValue(accessPoint.transform.position),
            GetMemberValue(door, "IntObj") is not null,
            building is not null,
            accessPoint is not null,
            GetMemberValue(door, "CanKnock") is not null,
            GetMemberValue(door, "Usable") is not null);
    }

    private static int ReadInt(object value, string name) => GetMemberValue(value, name) is int integer ? integer : int.MinValue;
    private static string ReadMemberText(object? value, string name) => GetMemberValue(value, name)?.ToString() ?? string.Empty;
    private static object? GetMemberValue(object? value, string name)
    {
        if (value is null) return null;
        var type = value.GetType();
        return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value) ??
               type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);
    }

    private static SyndicateHqVector3 ToValue(Vector3 value) => new(value.x, value.y, value.z);
    private static string TypeName(object? value) => value?.GetType().FullName ?? string.Empty;
    private static bool IsUnityNull(object? value) => value is null || value == null;
    private static bool IsUnityNull(UnityEngine.Object? value) => value == null;
    private static string GetTransformPath(Transform? transform)
    {
        if (IsUnityNull(transform)) return string.Empty;
        var parts = new List<string>();
        var current = transform;
        while (!IsUnityNull(current)) { parts.Add(current!.name); current = current.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private sealed class SyndicateHqDoorListenerRegistration : ISyndicateHqListenerRegistration
    {
        private readonly InteractableObject _target;
        private readonly UnityAction _listener;
        private bool _added;
        private bool _removed;
        public SyndicateHqDoorListenerRegistration(string stableIdentity, InteractableObject target, UnityAction listener) { StableIdentity = stableIdentity; _target = target; _listener = listener; }
        public string StableIdentity { get; }
        public void MarkAdded() => _added = true;
        public bool TryRemove(out string? failure)
        {
            failure = null;
            if (_removed) return true;
            if (!_added) { _removed = true; return true; }
            try { _target.onInteractStart.RemoveListener(_listener); _removed = true; return true; }
            catch (Exception ex) { failure = ex.Message; return false; }
        }
    }
}
