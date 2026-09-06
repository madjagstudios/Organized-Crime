namespace OrganizedCrime.PhoneCallProof;

public sealed class PhoneCallProofRuntime : IDisposable
{
    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromMinutes(15);
    private const int MaxEvidenceRows = 128;

    private readonly PhoneCallProofAdapter _adapter;
    private readonly Func<PhoneProofKey, bool> _isKeyDown;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _utcNow;
    private readonly TimeSpan _deadline;
    private readonly List<string> _evidenceRows = new();
    private DateTime _startedUtc;
    private PhoneProofProtocolState _state = PhoneProofProtocolState.Detached;
    private PhoneCallProofResult? _nellResult;
    private PhoneCallProofResult? _arthurResult;
    private PhoneProofOwnerObservations? _ownerObservations;
    private string? _stopReason;
    private int _queueAttempts;
    private bool _disposed;

    public PhoneCallProofRuntime(
        PhoneCallProofContext context,
        IPhoneCallQueue queue,
        Func<PhoneProofKey, bool> isKeyDown,
        Action<string>? log = null,
        Func<DateTime>? utcNow = null,
        TimeSpan? deadline = null)
        : this(() => context, queue, isKeyDown, log, utcNow, deadline)
    {
    }

    public PhoneCallProofRuntime(
        Func<PhoneCallProofContext> contextProvider,
        IPhoneCallQueue queue,
        Func<PhoneProofKey, bool> isKeyDown,
        Action<string>? log = null,
        Func<DateTime>? utcNow = null,
        TimeSpan? deadline = null)
    {
        _adapter = new PhoneCallProofAdapter(contextProvider, queue);
        _isKeyDown = isKeyDown ?? throw new ArgumentNullException(nameof(isKeyDown));
        _log = log ?? (_ => { });
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _deadline = deadline ?? DefaultDeadline;
    }

    public PhoneProofProtocolState State => _state;

    public PhoneProofEvidence Evidence => BuildEvidence();

    public void Initialize()
    {
        if (_disposed || _state != PhoneProofProtocolState.Detached)
            return;

        _startedUtc = _utcNow();
        _state = PhoneProofProtocolState.AwaitingNell;
        Emit("initialized");
    }

    public void Update()
    {
        if (_disposed || _state is PhoneProofProtocolState.Detached or PhoneProofProtocolState.Complete or PhoneProofProtocolState.Stop)
            return;
        if (_utcNow() - _startedUtc >= _deadline)
        {
            StopForBoundary("bounded fifteen-minute deadline elapsed");
            return;
        }

        if (_isKeyDown(PhoneProofKey.Nell) && _state == PhoneProofProtocolState.AwaitingNell)
        {
            _nellResult = TryQueue("oc-43.nell", PhoneCallProofRequest.Create(
                "Nell Grey",
                "This is a bounded OC-43 presentation proof."));
            if (_nellResult == PhoneCallProofResult.Queued)
                _state = PhoneProofProtocolState.NellQueued;
            Emit("nell-requested");
            return;
        }

        if (_isKeyDown(PhoneProofKey.Arthur) && _state == PhoneProofProtocolState.NellQueued)
        {
            _arthurResult = TryQueue("oc-43.arthur", PhoneCallProofRequest.Create(
                "Arthur Selby",
                "This is a bounded OC-43 follow-up presentation proof."));
            if (_arthurResult == PhoneCallProofResult.Queued)
                _state = PhoneProofProtocolState.ArthurQueued;
            Emit("arthur-requested");
            return;
        }

        if (_isKeyDown(PhoneProofKey.Classify))
        {
            Classify();
            Emit("classified");
        }
    }

    public bool ObserveCompleted(string correlation)
    {
        if (_disposed || _state is PhoneProofProtocolState.Stop or PhoneProofProtocolState.Detached)
            return false;

        var observed = _adapter.ObserveCompleted(correlation);
        if (observed)
            Emit("completion-observed");
        return observed;
    }

    public void RecordOwnerObservations(PhoneProofOwnerObservations observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (_disposed || _state == PhoneProofProtocolState.Stop)
            return;
        _ownerObservations = observations;
        Emit("owner-observations-recorded");
    }

    public void StopForBoundary(string reason)
    {
        if (_disposed || _state == PhoneProofProtocolState.Stop)
            return;

        _stopReason = reason;
        _state = PhoneProofProtocolState.Stop;
        Emit("stop");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_state is not PhoneProofProtocolState.Complete and not PhoneProofProtocolState.Stop)
            StopForBoundary("disposed");
        _disposed = true;
        _adapter.Dispose();
    }

    private PhoneCallProofResult TryQueue(string correlation, PhoneCallProofRequest request)
    {
        _queueAttempts++;
        return _adapter.TryRequest(correlation, request);
    }

    private void Classify()
    {
        if (_state == PhoneProofProtocolState.Stop)
            return;
        if (_nellResult != PhoneCallProofResult.Queued || _arthurResult != PhoneCallProofResult.Queued)
        {
            _state = PhoneProofProtocolState.Complete;
            return;
        }

        if (_ownerObservations is null)
        {
            _state = PhoneProofProtocolState.Complete;
            return;
        }

        if (!_ownerObservations.AllRequiredObservationsPresent)
        {
            StopForBoundary("owner observation recorded a failed proof condition");
            return;
        }

        _state = PhoneProofProtocolState.Complete;
    }

    private PhoneProofEvidence BuildEvidence()
    {
        var classification = _state == PhoneProofProtocolState.Stop
            ? PhoneProofClassification.Stop
            : _state == PhoneProofProtocolState.Complete &&
              _nellResult == PhoneCallProofResult.Queued &&
              _arthurResult == PhoneCallProofResult.Queued &&
              _ownerObservations?.AllRequiredObservationsPresent == true
                ? PhoneProofClassification.Pass
                : PhoneProofClassification.Inconclusive;

        return new PhoneProofEvidence(
            classification,
            _state,
            _nellResult,
            _arthurResult,
            _queueAttempts,
            _ownerObservations,
            _stopReason,
            StoryWrites: 0,
            StandingWrites: 0,
            RewardWrites: 0);
    }

    private void Emit(string eventKey)
    {
        if (_evidenceRows.Count >= MaxEvidenceRows)
            return;

        var row = System.Text.Json.JsonSerializer.Serialize(new
        {
            Event = eventKey,
            Evidence.Status,
            Evidence.State,
            Evidence.NellResult,
            Evidence.ArthurResult,
            Evidence.QueueAttempts,
            Evidence.StoryWrites,
            Evidence.StandingWrites,
            Evidence.RewardWrites,
            Evidence.StopReason
        });
        _evidenceRows.Add(row);
        _log(row);
    }
}
