using System.Text.Json;
using Il2CppFishNet;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using MelonLoader;
using OrganizedCrime.Runtime;
using S1API.Lifecycle;
using UnityEngine;

[assembly: MelonInfo(
    typeof(OrganizedCrime.Census.OrganizedCrimeCensusMod),
    "Organized Crime OC-30 Census",
    "0.1.0",
    "MadJag Studios")]

namespace OrganizedCrime.Census;

public sealed class OrganizedCrimeCensusMod : MelonMod, ILocalPressureRuntimeCensusSource
{
    private const int MaxCensusRows = 4096;
    private LocalPressureRuntimeCensusObserver? _observer;
    private readonly CensusTimeEventBindingState _timeBindingState = new();
    private readonly CensusCapacityNotice _capacityNotice = new();
    private TimeManager? _timeManager;
    private bool _timeEventsSubscribed;
    private Il2CppSystem.Action? _hourPassHandler;
    private Il2CppSystem.Action? _dayPassHandler;
    private Il2CppSystem.Action? _sleepEndHandler;

    public override void OnInitializeMelon()
    {
        _observer = new LocalPressureRuntimeCensusObserver(MaxCensusRows);
        GameLifecycle.OnPreLoad += HandlePreLoad;
        GameLifecycle.OnLoadComplete += HandleLoadComplete;
        GameLifecycle.OnPreSceneChange += HandlePreSceneChange;
        GameLifecycle.OnSaveInfoLoaded += HandleSaveInfoLoaded;
        GameLifecycle.OnSaveStart += HandleSaveStart;
        GameLifecycle.OnSaveComplete += HandleSaveComplete;
        TrySubscribeTimeEvents();
        MelonLogger.Msg("[OC-30 Census] initialized; read-only logging only.");
    }

    public override void OnDeinitializeMelon()
    {
        GameLifecycle.OnPreLoad -= HandlePreLoad;
        GameLifecycle.OnLoadComplete -= HandleLoadComplete;
        GameLifecycle.OnPreSceneChange -= HandlePreSceneChange;
        GameLifecycle.OnSaveInfoLoaded -= HandleSaveInfoLoaded;
        GameLifecycle.OnSaveStart -= HandleSaveStart;
        GameLifecycle.OnSaveComplete -= HandleSaveComplete;
        UnsubscribeTimeEvents();
        _timeBindingState.Clear();
        _observer?.Dispose();
        _observer = null;
    }

    public bool TryReadSnapshot(out LocalPressureRuntimeCensusSnapshot snapshot)
    {
        snapshot = default;

        try
        {
            var serverManager = InstanceFinder.ServerManager;
            var isServer = serverManager is not null && serverManager.OneServerStarted();
            var clientManager = InstanceFinder.ClientManager;
            var isClient = clientManager is not null && clientManager.Started;
            var isHost = CensusAuthorityProjection.IsHost(isServer, isClient);
            var timeManager = TimeManager.Instance;
            if (timeManager is null)
                return false;

            var dateTime = timeManager.GetDateTime();
            var players = UnityEngine.Object.FindObjectsOfType<Player>(includeInactive: true);
            var hostPlayers = players
                .Where(player => player is not null && player.IsServerInitialized)
                .ToArray();
            var hostCodes = hostPlayers
                .Select(player => player.PlayerCode?.Trim())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToArray();
            var saveFolderPresent = !string.IsNullOrWhiteSpace(
                LoadManager.Instance?.LoadedGameFolderPath);

            snapshot = new LocalPressureRuntimeCensusSnapshot(
                IsServer: isServer,
                IsHost: isHost,
                TotalGameMinutes: dateTime.GetMinSum(),
                ElapsedDays: dateTime.elapsedDays,
                Time24h: dateTime.time,
                CurrentTime: timeManager.CurrentTime,
                IsSleepInProgress: timeManager.IsSleepInProgress,
                SaveFolderPresent: saveFolderPresent,
                PlayerCount: players.Length,
                HostOwnedPlayerCodePresent: hostCodes.Length > 0,
                HostOwnedPlayerCodeUnique: hostCodes.Length == hostCodes.Distinct(StringComparer.Ordinal).Count(),
                ConnectionPresent: players.Any(player => player is not null && player.Connection is not null));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void HandlePreLoad()
    {
        UnsubscribeTimeEvents();
        Capture("GameLifecycle.OnPreLoad");
    }
    private void HandleLoadComplete()
    {
        TrySubscribeTimeEvents();
        Capture("GameLifecycle.OnLoadComplete");
    }

    private void HandlePreSceneChange()
    {
        UnsubscribeTimeEvents();
        Capture("GameLifecycle.OnPreSceneChange");
    }
    private void HandleSaveInfoLoaded() => Capture("GameLifecycle.OnSaveInfoLoaded");
    private void HandleSaveStart() => Capture("GameLifecycle.OnSaveStart");
    private void HandleSaveComplete() => Capture("GameLifecycle.OnSaveComplete");
    private void HandleHourPass() => Capture("TimeManager.onHourPass");
    private void HandleDayPass() => Capture("TimeManager.onDayPass");
    private void HandleSleepEnd() => Capture("TimeManager.onSleepEnd");

    private void Capture(string callbackName)
    {
        var observer = _observer;
        if (observer is null)
            return;

        var previousCount = observer.Rows.Count;
        observer.Observe(callbackName, this);
        if (observer.Rows.Count == previousCount)
        {
            if (observer.CapacityReached && _capacityNotice.ShouldReport(capacityReached: true))
                MelonLogger.Warning("[OC-30 Census] capacity reached; subsequent rows are not retained or logged.");
            return;
        }

        var row = observer.Rows[^1];
        MelonLogger.Msg($"[OC-30 Census] {JsonSerializer.Serialize(row)}");
    }

    private void TrySubscribeTimeEvents()
    {
        if (_timeEventsSubscribed)
            return;

        try
        {
            var timeManager = TimeManager.Instance;
            if (timeManager is null)
            {
                MelonLogger.Warning("[OC-30 Census] TimeManager unavailable; time-event binding deferred.");
                return;
            }

            if (_timeEventsSubscribed)
            {
                if (_timeBindingState.IsBoundTo(timeManager))
                    return;

                UnsubscribeTimeEvents();
            }

            _timeManager = timeManager;
            _hourPassHandler = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(HandleHourPass));
            _dayPassHandler = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(HandleDayPass));
            _sleepEndHandler = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(HandleSleepEnd));
            _timeManager.onHourPass += _hourPassHandler;
            _timeManager.onDayPass += _dayPassHandler;
            _timeManager.onSleepEnd += _sleepEndHandler;
            _timeBindingState.MarkBound(timeManager);
            _timeEventsSubscribed = true;
        }
        catch (Exception exception)
        {
            _timeManager = null;
            _timeEventsSubscribed = false;
            _timeBindingState.Clear();
            MelonLogger.Warning($"[OC-30 Census] TimeManager event binding failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void UnsubscribeTimeEvents()
    {
        if (!_timeEventsSubscribed || _timeManager is null)
            return;

        try
        {
            if (_hourPassHandler is not null)
                _timeManager.onHourPass -= _hourPassHandler;
            if (_dayPassHandler is not null)
                _timeManager.onDayPass -= _dayPassHandler;
            if (_sleepEndHandler is not null)
                _timeManager.onSleepEnd -= _sleepEndHandler;
        }
        catch
        {
            // Census teardown is best-effort and never mutates game state.
        }

        _timeManager = null;
        _timeEventsSubscribed = false;
        _timeBindingState.Clear();
        _hourPassHandler = null;
        _dayPassHandler = null;
        _sleepEndHandler = null;
    }
}
