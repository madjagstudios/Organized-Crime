using Il2CppFishNet;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using MelonLoader;
using S1API.Lifecycle;
using UnityEngine;

namespace OrganizedCrime.Runtime;

public sealed class LocalPressureHostLifecycleAdapter : ILocalPressureRuntimeHostAdapter
{
    private readonly Action<string> _log;
    private readonly LocalPressureHostLifecycleBindingState _timeBindingState = new();
    private TimeManager? _timeManager;
    private Il2CppSystem.Action? _hourPassHandler;
    private Il2CppSystem.Action? _sleepEndHandler;
    private bool _lifecycleSubscribed;
    private bool _timeEventsSubscribed;
    private bool _timeBindingDiagnosticIssued;
    private bool _sleepReadDiagnosticIssued;
    private long? _lastForwardedSleepEndTotalGameMinutes;
    private LocalPressureClockBoundary? _forwardingBoundary;
    private bool _disposed;

    public LocalPressureHostLifecycleAdapter(Action<string>? log = null)
    {
        _log = log ?? (message => MelonLogger.Warning($"[Organized Crime] {message}"));
        SubscribeLifecycleEvents();
    }

    public LocalPressureHostAuthorityReadStatus ReadHostAuthority()
    {
        try
        {
            var serverManager = InstanceFinder.ServerManager;
            var clientManager = InstanceFinder.ClientManager;
            if (serverManager is null || clientManager is null)
                return LocalPressureHostAuthorityReadStatus.Pending;

            var serverStarted = serverManager.OneServerStarted();
            var clientStarted = clientManager.Started;
            if (!serverStarted && !clientStarted)
                return LocalPressureHostAuthorityReadStatus.Pending;
            if (!serverStarted)
                return LocalPressureHostAuthorityReadStatus.NotAuthoritative;
            if (!clientStarted)
                return LocalPressureHostAuthorityReadStatus.Pending;

            return LocalPressureHostAuthorityReadStatus.Ready;
        }
        catch
        {
            return LocalPressureHostAuthorityReadStatus.Faulted;
        }
    }

    public string? ActiveSaveFolder
    {
        get
        {
            try
            {
                return LoadManager.Instance?.LoadedGameFolderPath;
            }
            catch
            {
                return null;
            }
        }
    }

    public string? CanonicalHostIdentity
    {
        get
        {
            return LocalPressureHostLifecyclePolicies.TryGetCanonicalHostIdentity(
                ActiveSaveFolder,
                out var identity)
                ? identity
                : null;
        }
    }

    public event Action? PreLoad;
    public event Action? LoadComplete;
    public event Action? SaveStart;
    public event Action? SaveComplete;
    public event Action<LocalPressureClockBoundary>? ClockBoundary;

    public LocalPressureClockReadStatus TryReadHostClock(out LocalPressureClockSample sample)
    {
        sample = default;

        try
        {
            var timeManager = TimeManager.Instance;
            if (timeManager is null)
                return LocalPressureClockReadStatus.Pending;

            var dateTime = timeManager.GetDateTime();
            sample = new LocalPressureClockSample(
                dateTime.GetMinSum(),
                dateTime.elapsedDays,
                dateTime.time,
                SourceSequence: null,
                _forwardingBoundary ?? LocalPressureClockBoundary.HostReady,
                DateTime.UtcNow);
            return LocalPressureClockReadStatus.Ready;
        }
        catch
        {
            sample = default;
            return LocalPressureClockReadStatus.Faulted;
        }
    }

    public LocalPressurePlayerReadStatus TryReadSupportedPlayers(out IReadOnlyList<LocalPressurePlayerSample> players)
    {
        players = Array.Empty<LocalPressurePlayerSample>();

        try
        {
            var registry = Player.PlayerList;
            if (registry is null)
                return LocalPressurePlayerReadStatus.Pending;

            var registrySnapshot = new List<LocalPressurePlayerSample>();
            foreach (var player in registry)
            {
                if (player is null || !player.IsServerInitialized)
                    continue;

                registrySnapshot.Add(new LocalPressurePlayerSample(
                    PlayerId: player.PlayerCode?.Trim(),
                    IsHostOwned: player.IsServerInitialized,
                    IsLocalPlayer: player.IsLocalPlayer,
                    PlayerCode: player.PlayerCode?.Trim(),
                    Region: null,
                    PropertyCode: null,
                    SourcePlayer: player,
                    HasConnection: player.Connection is not null));
            }

            return LocalPressureHostLifecyclePolicies.ClassifyPlayerRegistrySnapshot(
                registrySnapshot,
                out players);
        }
        catch
        {
            players = Array.Empty<LocalPressurePlayerSample>();
            return LocalPressurePlayerReadStatus.Faulted;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        UnsubscribeTimeEvents();

        if (_lifecycleSubscribed)
        {
            GameLifecycle.OnPreLoad -= HandlePreLoad;
            GameLifecycle.OnLoadComplete -= HandleLoadComplete;
            GameLifecycle.OnSaveStart -= HandleSaveStart;
            GameLifecycle.OnSaveComplete -= HandleSaveComplete;
            GameLifecycle.OnPreSceneChange -= HandlePreSceneChange;
            _lifecycleSubscribed = false;
        }

        PreLoad = null;
        LoadComplete = null;
        SaveStart = null;
        SaveComplete = null;
        ClockBoundary = null;
    }

    private void SubscribeLifecycleEvents()
    {
        GameLifecycle.OnPreLoad += HandlePreLoad;
        GameLifecycle.OnLoadComplete += HandleLoadComplete;
        GameLifecycle.OnSaveStart += HandleSaveStart;
        GameLifecycle.OnSaveComplete += HandleSaveComplete;
        GameLifecycle.OnPreSceneChange += HandlePreSceneChange;
        _lifecycleSubscribed = true;
    }

    private void HandlePreLoad()
    {
        UnsubscribeTimeEvents();
        _timeBindingDiagnosticIssued = false;
        _sleepReadDiagnosticIssued = false;
        _lastForwardedSleepEndTotalGameMinutes = null;
        PreLoad?.Invoke();
    }

    private void HandleLoadComplete()
    {
        EnsureClockBoundarySubscriptions();
        LoadComplete?.Invoke();
    }

    private void HandleSaveStart() => SaveStart?.Invoke();

    private void HandleSaveComplete() => SaveComplete?.Invoke();

    private void HandlePreSceneChange()
    {
        UnsubscribeTimeEvents();
        _timeBindingDiagnosticIssued = false;
        _sleepReadDiagnosticIssued = false;
        _lastForwardedSleepEndTotalGameMinutes = null;
    }

    private void HandleHourPass()
    {
        if (!TryReadSleepInProgress(out var sleepInProgress) ||
            !LocalPressureHostLifecyclePolicies.ShouldForwardAwakeHour(sleepInProgress))
            return;

        ForwardClockBoundary(LocalPressureClockBoundary.Hour);
    }

    private void HandleSleepEnd()
    {
        if (TryReadHostClock(out var sample) != LocalPressureClockReadStatus.Ready ||
            !LocalPressureHostLifecyclePolicies.TryAcceptSleepEnd(
                sample.TotalGameMinutes,
                ref _lastForwardedSleepEndTotalGameMinutes))
            return;

        ForwardClockBoundary(LocalPressureClockBoundary.SleepEnd);
    }

    private bool TryReadSleepInProgress(out bool sleepInProgress)
    {
        sleepInProgress = false;
        try
        {
            var timeManager = _timeManager;
            if (timeManager is null)
                return false;

            sleepInProgress = timeManager.IsSleepInProgress;
            return true;
        }
        catch (Exception exception)
        {
            if (!_sleepReadDiagnosticIssued)
            {
                _sleepReadDiagnosticIssued = true;
                _log($"Local Pressure sleep-state read failed; clock boundary suppressed: {exception.GetType().Name}: {exception.Message}");
            }

            return false;
        }
    }

    private void ForwardClockBoundary(LocalPressureClockBoundary boundary)
    {
        if (_disposed)
            return;

        _forwardingBoundary = boundary;
        try
        {
            ClockBoundary?.Invoke(boundary);
        }
        catch (Exception exception)
        {
            _log($"Local Pressure clock boundary forwarding failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            _forwardingBoundary = null;
        }
    }

    public LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions()
    {
        if (_disposed)
            return LocalPressureClockBoundaryBindingStatus.Faulted;

        try
        {
            var timeManager = TimeManager.Instance;
            if (timeManager is null)
            {
                ReportTimeBindingDiagnostic("TimeManager unavailable; time-event binding deferred to the pre-Active readiness pump.");
                return LocalPressureClockBoundaryBindingStatus.Pending;
            }

            if (_timeEventsSubscribed && _timeBindingState.IsBoundTo(timeManager))
                return LocalPressureClockBoundaryBindingStatus.Ready;

            if (_timeEventsSubscribed)
                UnsubscribeTimeEvents();

            _timeManager = timeManager;
            _hourPassHandler = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(HandleHourPass));
            _sleepEndHandler = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(HandleSleepEnd));
            _timeEventsSubscribed = true;
            _timeManager.onHourPass += _hourPassHandler;
            _timeManager.onSleepEnd += _sleepEndHandler;
            _timeBindingState.MarkBound(timeManager);
            return LocalPressureClockBoundaryBindingStatus.Ready;
        }
        catch (Exception exception)
        {
            UnsubscribeTimeEvents();
            _log($"Local Pressure TimeManager event binding failed: {exception.GetType().Name}: {exception.Message}");
            return LocalPressureClockBoundaryBindingStatus.Faulted;
        }
    }

    private void ReportTimeBindingDiagnostic(string message)
    {
        if (_timeBindingDiagnosticIssued)
            return;

        _timeBindingDiagnosticIssued = true;
        _log($"Local Pressure {message}");
    }

    private void UnsubscribeTimeEvents()
    {
        var timeManager = _timeManager;
        if (_timeEventsSubscribed && timeManager is not null)
        {
            try
            {
                if (_hourPassHandler is not null)
                    timeManager.onHourPass -= _hourPassHandler;
            }
            catch (Exception exception)
            {
                _log($"Local Pressure hour-event unsubscription failed: {exception.GetType().Name}: {exception.Message}");
            }

            try
            {
                if (_sleepEndHandler is not null)
                    timeManager.onSleepEnd -= _sleepEndHandler;
            }
            catch (Exception exception)
            {
                _log($"Local Pressure sleep-event unsubscription failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        _timeManager = null;
        _hourPassHandler = null;
        _sleepEndHandler = null;
        _timeEventsSubscribed = false;
        _timeBindingState.Clear();
    }
}
