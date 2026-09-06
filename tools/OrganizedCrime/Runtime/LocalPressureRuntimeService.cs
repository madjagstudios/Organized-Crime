using OrganizedCrime.Model;
using OrganizedCrime.Persistence;

namespace OrganizedCrime.Runtime;

public sealed class LocalPressureRuntimeService : IDisposable, ILocalPressureCustodyEvidenceWriter
{
    private readonly ILocalPressureRuntimeHostAdapter _adapter;
    private readonly Func<string?, ILocalPressureStateRepository?> _repositoryFactory;
    private readonly LocalPressureProfile _profile;
    private readonly LocalPressureDecayEvaluator _decayEvaluator;
    private readonly LocalPressureEvidenceEvaluator _evidenceEvaluator;
    private readonly ILocalPressureTierTransitionSink? _tierTransitionSink;
    private readonly Action<string> _log;
    private readonly Action<string> _receiptLog;
    private readonly object _evidenceSync = new();
    private readonly Dictionary<string, LocalPressureState> _states = new(StringComparer.Ordinal);
    private readonly HashSet<string> _acceptedEvidenceCorrelations = new(StringComparer.Ordinal);

    private ILocalPressureStateRepository? _repository;
    private LocalPressureRuntimePhase _phase = LocalPressureRuntimePhase.Detached;
    private LocalPressureRuntimePhase? _phaseBeforeSave;
    private Dictionary<string, LocalPressureState>? _saveSnapshot;
    private bool _hasAcceptedClock;
    private bool _loadRetryPending;
    private long _lastAcceptedTotalGameMinutes;
    private uint? _lastAcceptedSequence;
    private bool _disposed;

    public LocalPressureRuntimeService(
        ILocalPressureRuntimeHostAdapter adapter,
        Func<string?, ILocalPressureStateRepository?> repositoryFactory,
        LocalPressureProfile? profile = null,
        LocalPressureDecayEvaluator? decayEvaluator = null,
        Action<string>? log = null,
        LocalPressureEvidenceEvaluator? evidenceEvaluator = null,
        ILocalPressureTierTransitionSink? tierTransitionSink = null,
        Action<string>? receiptLog = null)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _profile = profile ?? LocalPressureProfile.Moderate;
        _decayEvaluator = decayEvaluator ?? LocalPressureDecay.Evaluate;
        _evidenceEvaluator = evidenceEvaluator ?? LocalPressureTransitions.ApplyEvidence;
        _tierTransitionSink = tierTransitionSink;
        _log = log ?? (_ => { });
        _receiptLog = receiptLog ?? _log;
        SessionEpoch = Guid.NewGuid();

        _adapter.PreLoad += OnPreLoad;
        _adapter.LoadComplete += OnLoadComplete;
        _adapter.SaveStart += OnSaveStart;
        _adapter.SaveComplete += OnSaveComplete;
        _adapter.ClockBoundary += OnClockBoundary;
    }

    public Guid SessionEpoch { get; }
    public long LoadEpoch { get; private set; }
    public LocalPressureRuntimePhase Phase => _phase;
    public LocalPressureRuntimeRejectReason LastRejectReason { get; private set; }
    public long? AcceptedClockTotalGameMinutes => _hasAcceptedClock ? _lastAcceptedTotalGameMinutes : null;

    public bool TryGetState(string playerId, out LocalPressureState? state)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            state = null;
            return false;
        }

        return _states.TryGetValue(playerId, out state);
    }

    public bool TryGetActiveEvidenceSnapshot(out LocalPressureActiveEvidenceSnapshot snapshot)
    {
        lock (_evidenceSync)
        {
            snapshot = default;
            if (_disposed || _phase != LocalPressureRuntimePhase.Active)
                return false;

            var authorityStatus = ReadHostAuthority();
            if (authorityStatus != LocalPressureHostAuthorityReadStatus.Ready)
            {
                if (authorityStatus == LocalPressureHostAuthorityReadStatus.Pending)
                    Reject(LocalPressureRuntimeRejectReason.HostNotReady, quarantine: true);
                else if (authorityStatus == LocalPressureHostAuthorityReadStatus.NotAuthoritative)
                    Reject(LocalPressureRuntimeRejectReason.NotAuthoritativeHost, quarantine: true);
                return false;
            }

            if (!TryResolveSinglePlayer(out var player) || string.IsNullOrWhiteSpace(player.PlayerId))
                return false;

            snapshot = new LocalPressureActiveEvidenceSnapshot(
                SessionEpoch,
                LoadEpoch,
                player.PlayerId!,
                player.SourcePlayer);
            return true;
        }
    }

    public LocalPressureEvidenceWriteResult TryApplyCustodyEvidence(
        CustodyEntryEvidence evidence,
        Guid sessionEpoch,
        long loadEpoch)
    {
        LocalPressureTierTransitionNotification? notification;
        LocalPressureEvidenceWriteResult result;
        lock (_evidenceSync)
        {
            result = TryApplyCustodyEvidenceLocked(evidence, sessionEpoch, loadEpoch, out notification);
        }

        PublishTransition(notification);
        return result;
    }

    public void OnPreLoad()
    {
        if (!CanHandleLifecycle())
            return;

        if (_phase == LocalPressureRuntimePhase.AwaitingLoad)
        {
            ClearRejectReason();
            return;
        }

        LoadEpoch++;
        ResetTransitionSinkForEpoch();
        _repository = null;
        _loadRetryPending = false;
        _states.Clear();
        _acceptedEvidenceCorrelations.Clear();
        ClearAcceptedClock();
        _phaseBeforeSave = null;
        _saveSnapshot = null;
        SetPhase(LocalPressureRuntimePhase.AwaitingLoad);
        ClearRejectReason();
    }

    public void OnLoadComplete()
    {
        if (!CanHandleLifecycle())
            return;

        if (_phase == LocalPressureRuntimePhase.AwaitingHostBaseline ||
            _phase == LocalPressureRuntimePhase.Active ||
            _phase == LocalPressureRuntimePhase.Saving)
        {
            ClearRejectReason();
            return;
        }

        if (_phase != LocalPressureRuntimePhase.AwaitingLoad &&
            _phase != LocalPressureRuntimePhase.Detached)
        {
            Reject(LocalPressureRuntimeRejectReason.LifecycleTransition, quarantine: false);
            return;
        }

        TryCompleteLoad();
    }

    private void TryCompleteLoad()
    {
        _loadRetryPending = false;

        var authorityStatus = ReadHostAuthority();
        if (authorityStatus == LocalPressureHostAuthorityReadStatus.Pending)
        {
            _loadRetryPending = true;
            Reject(LocalPressureRuntimeRejectReason.HostNotReady, quarantine: false);
            return;
        }

        if (authorityStatus != LocalPressureHostAuthorityReadStatus.Ready)
        {
            Reject(
                authorityStatus == LocalPressureHostAuthorityReadStatus.NotAuthoritative
                    ? LocalPressureRuntimeRejectReason.NotAuthoritativeHost
                    : LocalPressureRuntimeRejectReason.AdapterFailure,
                quarantine: true);
            return;
        }

        ILocalPressureStateRepository? repository;
        try
        {
            repository = _repositoryFactory(_adapter.ActiveSaveFolder);
        }
        catch (Exception ex)
        {
            _log($"Local Pressure repository creation failed: {ex.Message}");
            _loadRetryPending = false;
            Reject(LocalPressureRuntimeRejectReason.AdapterFailure, quarantine: true);
            return;
        }

        if (repository is null)
        {
            _loadRetryPending = true;
            Reject(LocalPressureRuntimeRejectReason.SavePathUnavailable, quarantine: false);
            return;
        }

        LocalPressureStoreLoadResult load;
        try
        {
            load = repository.Load();
        }
        catch (Exception ex)
        {
            _log($"Local Pressure sidecar load failed: {ex.Message}");
            _loadRetryPending = false;
            Reject(LocalPressureRuntimeRejectReason.SidecarLoadFailed, quarantine: true);
            return;
        }

        if (!load.Succeeded || load.Envelope is null)
        {
            _log($"Local Pressure sidecar load was rejected: {load.Message}");
            _loadRetryPending = false;
            Reject(LocalPressureRuntimeRejectReason.SidecarLoadFailed, quarantine: true);
            return;
        }

        try
        {
            var loadedStates = load.Envelope.Players
                .ToDictionary(record => record.PlayerId, record => record.ToState(), StringComparer.Ordinal);
            _states.Clear();
            foreach (var pair in loadedStates)
                _states.Add(pair.Key, pair.Value);
        }
        catch (Exception ex)
        {
            _log($"Local Pressure sidecar state hydration failed: {ex.Message}");
            _states.Clear();
            _loadRetryPending = false;
            Reject(LocalPressureRuntimeRejectReason.SidecarLoadFailed, quarantine: true);
            return;
        }

        _repository = repository;
        _loadRetryPending = false;
        ClearAcceptedClock();
        SetPhase(LocalPressureRuntimePhase.AwaitingHostBaseline);
        ClearRejectReason();
        _receiptLog($"Local Pressure repository hydrated; players={_states.Count}; loadEpoch={LoadEpoch}.");
    }

    public void OnClockBoundary(LocalPressureClockBoundary boundary)
    {
        if (!CanHandleLifecycle())
            return;

        if (_phase == LocalPressureRuntimePhase.AwaitingHostBaseline)
        {
            if (boundary == LocalPressureClockBoundary.HostReady)
                TryEstablishBaseline();
            return;
        }

        if (_phase != LocalPressureRuntimePhase.Active ||
            (boundary != LocalPressureClockBoundary.Hour &&
             boundary != LocalPressureClockBoundary.SleepEnd))
            return;

        var authorityStatus = ReadHostAuthority();
        if (authorityStatus == LocalPressureHostAuthorityReadStatus.Pending)
        {
            Reject(LocalPressureRuntimeRejectReason.HostNotReady, quarantine: true);
            return;
        }

        if (authorityStatus != LocalPressureHostAuthorityReadStatus.Ready)
        {
            Reject(
                authorityStatus == LocalPressureHostAuthorityReadStatus.NotAuthoritative
                    ? LocalPressureRuntimeRejectReason.NotAuthoritativeHost
                    : LocalPressureRuntimeRejectReason.AdapterFailure,
                quarantine: true);
            return;
        }

        if (!TryReadClock(out var sample))
            return;

        if (!ValidateClock(sample))
            return;

        if (!TryResolveSinglePlayer(out var player))
            return;

        if (!ValidateClockCursor(sample))
            return;

        var state = GetOrCreateContextualState(player);

        LocalPressureDecayResult decay;
        try
        {
            decay = _decayEvaluator(
                new LocalPressureDecayInput(
                    state,
                    sample.TotalGameMinutes / 60.0,
                    // Foundation B v1 explicitly selects evidence-recency-only decay;
                    // this is policy input, not an observed ActivePursuit=false value.
                    ActivePursuit: false),
                _profile);
        }
        catch (Exception ex)
        {
            _log($"Local Pressure decay evaluation failed: {ex.Message}");
            Reject(LocalPressureRuntimeRejectReason.AdapterFailure, quarantine: true);
            return;
        }

        _states[player.PlayerId!] = decay.State;
        CommitClock(sample);
        ClearRejectReason();
    }

    public void PumpReadiness()
    {
        if (_phase == LocalPressureRuntimePhase.AwaitingLoad && _loadRetryPending)
            TryCompleteLoad();

        if (_phase == LocalPressureRuntimePhase.AwaitingHostBaseline)
            TryEstablishBaseline();
    }

    private void TryEstablishBaseline()
    {
        if (!CanHandleLifecycle() || _phase != LocalPressureRuntimePhase.AwaitingHostBaseline)
            return;

        LocalPressureClockBoundaryBindingStatus bindingStatus;
        try
        {
            bindingStatus = _adapter.EnsureClockBoundarySubscriptions();
        }
        catch (Exception exception)
        {
            _log($"Local Pressure clock-boundary subscription failed: {exception.Message}");
            Reject(LocalPressureRuntimeRejectReason.AdapterFailure, quarantine: true);
            return;
        }

        if (bindingStatus == LocalPressureClockBoundaryBindingStatus.Pending)
        {
            Reject(LocalPressureRuntimeRejectReason.HostNotReady, quarantine: false);
            return;
        }

        if (bindingStatus != LocalPressureClockBoundaryBindingStatus.Ready)
        {
            Reject(LocalPressureRuntimeRejectReason.AdapterFailure, quarantine: true);
            return;
        }

        var authorityStatus = ReadHostAuthority();
        if (authorityStatus == LocalPressureHostAuthorityReadStatus.Pending)
        {
            Reject(LocalPressureRuntimeRejectReason.HostNotReady, quarantine: false);
            return;
        }

        if (authorityStatus != LocalPressureHostAuthorityReadStatus.Ready)
        {
            Reject(
                authorityStatus == LocalPressureHostAuthorityReadStatus.NotAuthoritative
                    ? LocalPressureRuntimeRejectReason.NotAuthoritativeHost
                    : LocalPressureRuntimeRejectReason.AdapterFailure,
                quarantine: true);
            return;
        }

        if (!TryReadClock(out var sample) ||
            !ValidateClock(sample) ||
            !TryResolveSinglePlayer(out var player))
            return;

        if (!ValidateClockCursor(sample))
            return;

        CommitClock(sample);
        SetPhase(LocalPressureRuntimePhase.Active);
        ClearRejectReason();
        _receiptLog($"Local Pressure runtime Active; playerId={player.PlayerId}; loadEpoch={LoadEpoch}.");
    }

    public void OnSaveStart()
    {
        if (!CanHandleLifecycle())
            return;

        if (_phase == LocalPressureRuntimePhase.Saving)
        {
            ClearRejectReason();
            return;
        }

        if (_phase != LocalPressureRuntimePhase.Active &&
            _phase != LocalPressureRuntimePhase.AwaitingHostBaseline)
        {
            Reject(LocalPressureRuntimeRejectReason.LifecycleTransition, quarantine: false);
            return;
        }

        _phaseBeforeSave = _phase;
        _saveSnapshot = _states.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        SetPhase(LocalPressureRuntimePhase.Saving);
        ClearRejectReason();
        _log($"Local Pressure SaveStart snapshot: players={_saveSnapshot.Count}; loadEpoch={LoadEpoch}.");
    }

    public void OnSaveComplete()
    {
        if (!CanHandleLifecycle() || _phase != LocalPressureRuntimePhase.Saving)
            return;

        var returnPhase = _phaseBeforeSave ?? LocalPressureRuntimePhase.AwaitingLoad;
        _phaseBeforeSave = null;
        var saveSnapshot = _saveSnapshot ?? new Dictionary<string, LocalPressureState>(StringComparer.Ordinal);
        _saveSnapshot = null;

        if (returnPhase != LocalPressureRuntimePhase.Active)
        {
            SetPhase(returnPhase);
            ClearRejectReason();
            return;
        }

        var authorityStatus = ReadHostAuthority();
        if (authorityStatus != LocalPressureHostAuthorityReadStatus.Ready)
        {
            Reject(
                authorityStatus == LocalPressureHostAuthorityReadStatus.Pending
                    ? LocalPressureRuntimeRejectReason.HostNotReady
                    : authorityStatus == LocalPressureHostAuthorityReadStatus.NotAuthoritative
                        ? LocalPressureRuntimeRejectReason.NotAuthoritativeHost
                        : LocalPressureRuntimeRejectReason.AdapterFailure,
                quarantine: true);
            return;
        }

        if (_repository is null)
        {
            Reject(LocalPressureRuntimeRejectReason.SavePathUnavailable, quarantine: false);
            SetPhase(LocalPressureRuntimePhase.Active);
            return;
        }

        if (!TryResolveSinglePlayer(out var player))
        {
            SetPhase(LocalPressureRuntimePhase.Quarantined);
            return;
        }

        var state = GetOrCreateState(player.PlayerId!, saveSnapshot);
        var record = LocalPressurePlayerRecord.FromState(state);
        LocalPressureStoreUpdateResult update;
        try
        {
            update = _repository.Update(record);
        }
        catch (Exception ex)
        {
            _log($"Local Pressure sidecar save failed: {ex.Message}");
            Reject(LocalPressureRuntimeRejectReason.SidecarSaveFailed, quarantine: false);
            SetPhase(LocalPressureRuntimePhase.Active);
            return;
        }

        if (!update.Succeeded)
        {
            _log($"Local Pressure sidecar save was rejected: {update.Message}");
            Reject(LocalPressureRuntimeRejectReason.SidecarSaveFailed, quarantine: false);
            SetPhase(LocalPressureRuntimePhase.Active);
            return;
        }

        _states[player.PlayerId!] = state;
        SetPhase(LocalPressureRuntimePhase.Active);
        ClearRejectReason();
        _log($"Local Pressure sidecar write succeeded; playerId={player.PlayerId}; revision={state.Revision}; loadEpoch={LoadEpoch}.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SetPhase(LocalPressureRuntimePhase.Disposed);
        _adapter.PreLoad -= OnPreLoad;
        _adapter.LoadComplete -= OnLoadComplete;
        _adapter.SaveStart -= OnSaveStart;
        _adapter.SaveComplete -= OnSaveComplete;
        _adapter.ClockBoundary -= OnClockBoundary;
        _repository = null;
        _loadRetryPending = false;
        _states.Clear();
        _acceptedEvidenceCorrelations.Clear();
        ClearAcceptedClock();
        _saveSnapshot = null;

        try
        {
            _adapter.Dispose();
        }
        catch (Exception ex)
        {
            _log($"Local Pressure host adapter disposal failed: {ex.Message}");
        }
    }

    private bool CanHandleLifecycle()
    {
        if (!_disposed)
            return true;

        SetRejectReason(LocalPressureRuntimeRejectReason.Disposed);
        return false;
    }

    private LocalPressureHostAuthorityReadStatus ReadHostAuthority()
    {
        try
        {
            var status = _adapter.ReadHostAuthority();
            if (status == LocalPressureHostAuthorityReadStatus.Faulted)
            {
                _log("Local Pressure host authority read returned Faulted.");
                Reject(LocalPressureRuntimeRejectReason.AdapterFailure, quarantine: true);
            }

            return status;
        }
        catch (Exception ex)
        {
            _log($"Local Pressure host authority read failed: {ex.Message}");
            Reject(LocalPressureRuntimeRejectReason.AdapterFailure, quarantine: true);
            return LocalPressureHostAuthorityReadStatus.Faulted;
        }
    }

    private bool TryReadClock(out LocalPressureClockSample sample)
    {
        try
        {
            var status = _adapter.TryReadHostClock(out sample);
            if (status == LocalPressureClockReadStatus.Ready)
                return true;
            if (status == LocalPressureClockReadStatus.Faulted)
            {
                _log("Local Pressure host clock read returned Faulted.");
                Reject(LocalPressureRuntimeRejectReason.AdapterFailure, quarantine: true);
                return false;
            }
        }
        catch (Exception ex)
        {
            _log($"Local Pressure host clock read failed: {ex.Message}");
            Reject(LocalPressureRuntimeRejectReason.AdapterFailure, quarantine: true);
            sample = default;
            return false;
        }

        sample = default;
        Reject(LocalPressureRuntimeRejectReason.MissingClock, quarantine: false);
        return false;
    }

    private bool ValidateClock(LocalPressureClockSample sample)
    {
        if (sample.TotalGameMinutes < 0 ||
            sample.ElapsedDays < 0)
        {
            if (LastRejectReason != LocalPressureRuntimeRejectReason.InvalidClock)
            {
                _log(
                    $"Local Pressure invalid clock sample: totalGameMinutes={sample.TotalGameMinutes}; " +
                    $"elapsedDays={sample.ElapsedDays}; time24h={sample.Time24h}; loadEpoch={LoadEpoch}.");
            }

            Reject(LocalPressureRuntimeRejectReason.InvalidClock, quarantine: true);
            return false;
        }

        return true;
    }

    private bool ValidateClockCursor(LocalPressureClockSample sample)
    {
        if (!_hasAcceptedClock)
            return true;

        if (sample.TotalGameMinutes < _lastAcceptedTotalGameMinutes)
        {
            Reject(LocalPressureRuntimeRejectReason.ClockRewound, quarantine: true);
            return false;
        }

        if (sample.TotalGameMinutes == _lastAcceptedTotalGameMinutes)
        {
            Reject(LocalPressureRuntimeRejectReason.DuplicateSample, quarantine: false);
            return false;
        }

        if (sample.SourceSequence.HasValue &&
            _lastAcceptedSequence.HasValue &&
            sample.SourceSequence.Value <= _lastAcceptedSequence.Value)
        {
            Reject(LocalPressureRuntimeRejectReason.StaleSequence, quarantine: false);
            return false;
        }

        return true;
    }

    private void CommitClock(LocalPressureClockSample sample)
    {
        _hasAcceptedClock = true;
        _lastAcceptedTotalGameMinutes = sample.TotalGameMinutes;
        _lastAcceptedSequence = sample.SourceSequence;
    }

    private bool TryResolveSinglePlayer(out LocalPressurePlayerSample player)
    {
        player = default;
        IReadOnlyList<LocalPressurePlayerSample> players;
        try
        {
            var status = _adapter.TryReadSupportedPlayers(out players);
            if (status == LocalPressurePlayerReadStatus.Pending)
            {
                Reject(
                    LocalPressureRuntimeRejectReason.IdentityUnavailable,
                    quarantine: _phase == LocalPressureRuntimePhase.Active);
                return false;
            }

            if (status == LocalPressurePlayerReadStatus.UnsupportedMultiplayer)
            {
                Reject(LocalPressureRuntimeRejectReason.UnsupportedMultiplayer, quarantine: true);
                return false;
            }

            if (status == LocalPressurePlayerReadStatus.Faulted)
            {
                _log("Local Pressure player identity read returned Faulted.");
                Reject(LocalPressureRuntimeRejectReason.AdapterFailure, quarantine: true);
                return false;
            }

            if (players is null)
            {
                Reject(LocalPressureRuntimeRejectReason.IdentityUnavailable, quarantine: _phase == LocalPressureRuntimePhase.Active);
                return false;
            }
        }
        catch (Exception ex)
        {
            _log($"Local Pressure player identity read failed: {ex.Message}");
            Reject(LocalPressureRuntimeRejectReason.AdapterFailure, quarantine: true);
            return false;
        }

        if (players.Count != 1)
        {
            Reject(
                players.Count > 1
                    ? LocalPressureRuntimeRejectReason.UnsupportedMultiplayer
                    : LocalPressureRuntimeRejectReason.IdentityUnavailable,
                quarantine: players.Count > 1 || _phase == LocalPressureRuntimePhase.Active);
            return false;
        }

        player = players[0];
        if (!player.IsHostOwned)
        {
            Reject(LocalPressureRuntimeRejectReason.IdentityUnavailable, quarantine: _phase == LocalPressureRuntimePhase.Active);
            return false;
        }

        string? canonicalIdentity;
        try
        {
            canonicalIdentity = _adapter.CanonicalHostIdentity?.Trim();
        }
        catch (Exception ex)
        {
            _log($"Local Pressure canonical identity read failed: {ex.Message}");
            Reject(LocalPressureRuntimeRejectReason.AdapterFailure, quarantine: true);
            return false;
        }

        if (string.IsNullOrWhiteSpace(canonicalIdentity) || canonicalIdentity.Contains('\0'))
        {
            Reject(
                LocalPressureRuntimeRejectReason.IdentityUnavailable,
                quarantine: _phase == LocalPressureRuntimePhase.Active);
            return false;
        }

        var playerCode = player.PlayerCode?.Trim();
        if (LocalPressureHostLifecyclePolicies.IsPlaceholderPlayerCode(playerCode))
        {
            Reject(
                LocalPressureRuntimeRejectReason.IdentityUnavailable,
                quarantine: _phase == LocalPressureRuntimePhase.Active);
            return false;
        }

        if (!player.HasConnection)
        {
            Reject(
                LocalPressureRuntimeRejectReason.IdentityUnavailable,
                quarantine: _phase == LocalPressureRuntimePhase.Active);
            return false;
        }

        if (playerCode?.Contains('\0') == true)
        {
            Reject(LocalPressureRuntimeRejectReason.IdentityUnavailable, quarantine: true);
            return false;
        }

        if (LocalPressureHostLifecyclePolicies.IsCanonicalPlayerIdentity(playerCode) &&
            !string.Equals(canonicalIdentity, playerCode, StringComparison.Ordinal))
        {
            Reject(LocalPressureRuntimeRejectReason.IdentityAmbiguous, quarantine: true);
            return false;
        }

        player = player with { PlayerId = canonicalIdentity, PlayerCode = playerCode };
        return true;
    }

    private LocalPressureState GetOrCreateContextualState(LocalPressurePlayerSample player)
    {
        var playerId = player.PlayerId!;
        if (!_states.TryGetValue(playerId, out var state))
            state = LocalPressureState.Quiet(playerId);

        return new LocalPressureState(
            state.LocalHeat,
            state.KnownOffender,
            state.LastEvidenceGameTime,
            state.QuietGraceUntil,
            state.LastDecayEvaluation,
            playerId,
            player.Region ?? state.Region,
            player.PropertyCode ?? state.PropertyCode,
            state.Revision);
    }

    private LocalPressureState GetOrCreateState(
        string playerId,
        IReadOnlyDictionary<string, LocalPressureState>? states = null)
    {
        if ((states ?? _states).TryGetValue(playerId, out var state))
            return state;

        return LocalPressureState.Quiet(playerId);
    }

    private void ClearAcceptedClock()
    {
        _hasAcceptedClock = false;
        _lastAcceptedTotalGameMinutes = 0;
        _lastAcceptedSequence = null;
    }

    private LocalPressureEvidenceWriteResult MapIdentityReject()
    {
        var reason = LastRejectReason switch
        {
            LocalPressureRuntimeRejectReason.UnsupportedMultiplayer => LocalPressureEvidenceWriteRejectReason.UnsupportedMultiplayer,
            LocalPressureRuntimeRejectReason.IdentityAmbiguous => LocalPressureEvidenceWriteRejectReason.PlayerMismatch,
            LocalPressureRuntimeRejectReason.IdentityUnavailable => LocalPressureEvidenceWriteRejectReason.MissingIdentity,
            _ => LocalPressureEvidenceWriteRejectReason.AdapterFailure
        };
        return EvidenceReject(reason, "Supported host player identity was unavailable.");
    }

    private static bool TryValidateCorrelation(
        string correlationId,
        Guid sessionEpoch,
        long loadEpoch,
        string playerId)
    {
        var segments = correlationId.Split('/', StringSplitOptions.None);
        return segments.Length == 6 &&
            string.Equals(segments[0], "custody", StringComparison.Ordinal) &&
            string.Equals(segments[1], "v1", StringComparison.Ordinal) &&
            Guid.TryParse(segments[2], out var correlationSessionEpoch) &&
            correlationSessionEpoch == sessionEpoch &&
            long.TryParse(segments[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var correlationLoadEpoch) &&
            correlationLoadEpoch == loadEpoch &&
            string.Equals(segments[4], playerId.Trim(), StringComparison.Ordinal) &&
            long.TryParse(segments[5], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var episode) &&
            episode > 0;
    }

    private static (string? Region, string? PropertyCode) NormalizeEvidenceContext(
        string? region,
        string? propertyCode)
    {
        region = NormalizeContextValue(region);
        propertyCode = NormalizeContextValue(propertyCode);
        return region is null || propertyCode is null
            ? (null, null)
            : (region, propertyCode);
    }

    private static string? NormalizeContextValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (normalized.Length > 128 || normalized.Any(character => char.IsControl(character)))
            return null;

        return normalized;
    }

    private static LocalPressureEvidenceWriteResult EvidenceReject(
        LocalPressureEvidenceWriteRejectReason reason,
        string message) =>
        new(false, reason, null, message);

    private LocalPressureEvidenceWriteResult TryApplyCustodyEvidenceLocked(
        CustodyEntryEvidence evidence,
        Guid sessionEpoch,
        long loadEpoch,
        out LocalPressureTierTransitionNotification? notification)
    {
        notification = null;

        if (_disposed)
            return EvidenceReject(LocalPressureEvidenceWriteRejectReason.Disposed, "Local Pressure runtime was disposed.");

        if (_phase != LocalPressureRuntimePhase.Active)
            return EvidenceReject(LocalPressureEvidenceWriteRejectReason.RuntimeNotActive, "Local Pressure runtime was not Active.");

        if (sessionEpoch != SessionEpoch || loadEpoch != LoadEpoch)
            return EvidenceReject(LocalPressureEvidenceWriteRejectReason.StaleEpoch, "Custody evidence epoch was stale.");

        var authorityStatus = ReadHostAuthority();
        if (authorityStatus != LocalPressureHostAuthorityReadStatus.Ready)
        {
            if (authorityStatus == LocalPressureHostAuthorityReadStatus.Pending)
                Reject(LocalPressureRuntimeRejectReason.HostNotReady, quarantine: true);
            else if (authorityStatus == LocalPressureHostAuthorityReadStatus.NotAuthoritative)
                Reject(LocalPressureRuntimeRejectReason.NotAuthoritativeHost, quarantine: true);
            return authorityStatus == LocalPressureHostAuthorityReadStatus.Faulted
                ? EvidenceReject(LocalPressureEvidenceWriteRejectReason.AdapterFailure, "Custody evidence host-authority read failed.")
                : EvidenceReject(LocalPressureEvidenceWriteRejectReason.NotAuthoritativeHost, "Custody evidence was not host-authoritative.");
        }

        if (evidence is null || string.IsNullOrWhiteSpace(evidence.PlayerId) || evidence.PlayerId.Contains('\0'))
            return EvidenceReject(LocalPressureEvidenceWriteRejectReason.MissingIdentity, "Custody evidence player identity was unavailable.");

        if (!TryResolveSinglePlayer(out var player) || string.IsNullOrWhiteSpace(player.PlayerId))
            return MapIdentityReject();

        if (!string.Equals(player.PlayerId, evidence.PlayerId.Trim(), StringComparison.Ordinal))
            return EvidenceReject(LocalPressureEvidenceWriteRejectReason.PlayerMismatch, "Custody evidence player identity did not match the supported host player.");

        if (string.IsNullOrWhiteSpace(evidence.CorrelationId))
            return EvidenceReject(LocalPressureEvidenceWriteRejectReason.MissingCorrelation, "Custody evidence correlation was unavailable.");

        if (!TryValidateCorrelation(evidence.CorrelationId, sessionEpoch, loadEpoch, evidence.PlayerId))
            return EvidenceReject(LocalPressureEvidenceWriteRejectReason.InvalidCorrelation, "Custody evidence correlation was invalid for the current identity or epoch.");

        if (_acceptedEvidenceCorrelations.Contains(evidence.CorrelationId))
            return EvidenceReject(LocalPressureEvidenceWriteRejectReason.DuplicateCorrelation, "Custody evidence correlation was already accepted.");

        if (!_hasAcceptedClock)
            return EvidenceReject(LocalPressureEvidenceWriteRejectReason.ClockUnavailable, "Local Pressure host clock baseline was unavailable.");

        if (!TryReadClock(out var sample))
            return EvidenceReject(LocalPressureEvidenceWriteRejectReason.ClockUnavailable, "Local Pressure host clock could not be read.");

        if (!ValidateClock(sample) ||
            sample.TotalGameMinutes < _lastAcceptedTotalGameMinutes ||
            (sample.SourceSequence.HasValue &&
             _lastAcceptedSequence.HasValue &&
             sample.SourceSequence.Value <= _lastAcceptedSequence.Value))
        {
            return EvidenceReject(LocalPressureEvidenceWriteRejectReason.InvalidClock, "Custody evidence host clock was invalid or stale.");
        }

        var playerId = evidence.PlayerId.Trim();
        var state = GetOrCreateState(playerId);
        var (region, propertyCode) = NormalizeEvidenceContext(evidence.Region, evidence.PropertyCode);
        var gameTimeHours = sample.TotalGameMinutes / 60.0;
        if (state.LastEvidenceGameTime is not null && state.LastEvidenceGameTime.Value > gameTimeHours)
        {
            region = null;
            propertyCode = null;
        }

        var modelEvidence = new LocalPressureEvidenceEvent(
            LocalPressureReasonCode.Arrest,
            gameTimeHours,
            playerId,
            region,
            propertyCode,
            evidence.CorrelationId,
            gameTimeHours);

        LocalPressureTransitionResult transition;
        try
        {
            transition = _evidenceEvaluator(state, modelEvidence, _profile);
        }
        catch (Exception exception)
        {
            _log($"Local Pressure custody evidence transition failed: {exception.Message}");
            return EvidenceReject(LocalPressureEvidenceWriteRejectReason.AdapterFailure, "Custody evidence transition failed.");
        }

        _states[playerId] = transition.State;
        _acceptedEvidenceCorrelations.Add(evidence.CorrelationId);
        notification = new LocalPressureTierTransitionNotification(
            SessionEpoch,
            LoadEpoch,
            playerId,
            player.SourcePlayer,
            evidence.CorrelationId,
            transition.PreviousTier,
            transition.CurrentTier,
            region,
            propertyCode);
        return new LocalPressureEvidenceWriteResult(true, LocalPressureEvidenceWriteRejectReason.None, transition.State, "Custody evidence accepted.");
    }

    /// <summary>
    /// OC-73 spec decisions 15 and 16. Applies the Chief Campbell record wipe to this player's ledger
    /// under the same guards custody evidence uses (Active phase, matching epoch, authoritative host,
    /// the one supported player). Deliberately publishes no tier transition: a wipe is a falling
    /// change, OC-41 acts only on rising edges, and publishing one would be a new dispatch surface.
    /// Persistence rides the existing save path, exactly as an arrest write does.
    /// </summary>
    public LocalPressureEvidenceWriteResult TryApplyRecordWipe(string playerId, Guid sessionEpoch, long loadEpoch)
    {
        lock (_evidenceSync)
        {
            if (_disposed)
                return EvidenceReject(LocalPressureEvidenceWriteRejectReason.Disposed, "Local Pressure runtime was disposed.");

            if (_phase != LocalPressureRuntimePhase.Active)
                return EvidenceReject(LocalPressureEvidenceWriteRejectReason.RuntimeNotActive, "Local Pressure runtime was not Active.");

            if (sessionEpoch != SessionEpoch || loadEpoch != LoadEpoch)
                return EvidenceReject(LocalPressureEvidenceWriteRejectReason.StaleEpoch, "Record wipe epoch was stale.");

            var authorityStatus = ReadHostAuthority();
            if (authorityStatus != LocalPressureHostAuthorityReadStatus.Ready)
            {
                if (authorityStatus == LocalPressureHostAuthorityReadStatus.Pending)
                    Reject(LocalPressureRuntimeRejectReason.HostNotReady, quarantine: true);
                else if (authorityStatus == LocalPressureHostAuthorityReadStatus.NotAuthoritative)
                    Reject(LocalPressureRuntimeRejectReason.NotAuthoritativeHost, quarantine: true);
                return authorityStatus == LocalPressureHostAuthorityReadStatus.Faulted
                    ? EvidenceReject(LocalPressureEvidenceWriteRejectReason.AdapterFailure, "Record wipe host-authority read failed.")
                    : EvidenceReject(LocalPressureEvidenceWriteRejectReason.NotAuthoritativeHost, "Record wipe was not host-authoritative.");
            }

            if (string.IsNullOrWhiteSpace(playerId) || playerId.Contains('\0'))
                return EvidenceReject(LocalPressureEvidenceWriteRejectReason.MissingIdentity, "Record wipe player identity was unavailable.");

            if (!TryResolveSinglePlayer(out var player) || string.IsNullOrWhiteSpace(player.PlayerId))
                return MapIdentityReject();

            if (!string.Equals(player.PlayerId, playerId.Trim(), StringComparison.Ordinal))
                return EvidenceReject(LocalPressureEvidenceWriteRejectReason.PlayerMismatch, "Record wipe player identity did not match the supported host player.");

            var resolved = playerId.Trim();
            LocalPressureRecordWipeResult wipe;
            try { wipe = LocalPressureTransitions.ApplyRecordWipe(GetOrCreateState(resolved), _profile); }
            catch (Exception exception)
            {
                _log($"Local Pressure record wipe transition failed: {exception.Message}");
                return EvidenceReject(LocalPressureEvidenceWriteRejectReason.AdapterFailure, "Record wipe transition failed.");
            }

            _states[resolved] = wipe.State;
            return new LocalPressureEvidenceWriteResult(true, LocalPressureEvidenceWriteRejectReason.None, wipe.State, "Record wipe accepted.");
        }
    }

    private void PublishTransition(LocalPressureTierTransitionNotification? notification)
    {
        if (notification is null || _tierTransitionSink is null)
            return;

        try
        {
            _tierTransitionSink.Publish(notification);
        }
        catch (Exception exception)
        {
            _log($"Local Pressure tier-transition publication failed: {exception.Message}");
        }
    }

    private void ResetTransitionSinkForEpoch()
    {
        if (_tierTransitionSink is null)
            return;

        try
        {
            _tierTransitionSink.ResetForEpoch(SessionEpoch, LoadEpoch);
        }
        catch (Exception exception)
        {
            _log($"Local Pressure tier-transition reset failed: {exception.Message}");
        }
    }

    private void Reject(LocalPressureRuntimeRejectReason reason, bool quarantine)
    {
        SetRejectReason(reason);
        if (quarantine)
            SetPhase(LocalPressureRuntimePhase.Quarantined);
    }

    private void SetPhase(LocalPressureRuntimePhase phase)
    {
        if (_phase == phase)
            return;

        _phase = phase;
        _receiptLog($"Local Pressure phase changed: {phase}; loadEpoch={LoadEpoch}.");
    }

    private void SetRejectReason(LocalPressureRuntimeRejectReason reason)
    {
        if (LastRejectReason == reason)
            return;

        LastRejectReason = reason;
        _log($"Local Pressure readiness/reject reason changed: {reason}; phase={_phase}; loadEpoch={LoadEpoch}.");
    }

    private void ClearRejectReason() => SetRejectReason(LocalPressureRuntimeRejectReason.None);
}

public sealed class LocalPressureStateStoreRepository : ILocalPressureStateRepository
{
    private readonly LocalPressureStateStore _store;

    public LocalPressureStateStoreRepository(LocalPressureStateStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public LocalPressureStoreLoadResult Load() =>
        _store.TryLoad(out var result) ? result : result;

    public LocalPressureStoreUpdateResult Update(LocalPressurePlayerRecord record) =>
        _store.TryUpdate(record, out var result) ? result : result;
}
