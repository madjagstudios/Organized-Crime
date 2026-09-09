using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class Release1TransitionPublisherService : IDisposable
{
    private const string IntroAcceptedReceipt = "post-benzies-intro-accepted-v1";
    private const string IntroDeferredReceipt = "post-benzies-intro-deferred-v1";
    private const string NellPhoneReceipt = "intro-contact-v1";
    private static readonly string[] NellIntroStages =
    {
        "You have made some changes around town. My associates noticed.",
        "There is a small courtesy you can do for us. I will send the details."
    };
    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultWatchInterval = TimeSpan.FromSeconds(5);

    private readonly IRelease1PostBenziesUnlockReader _reader;
    private readonly Release1StoryRuntimeService _story;
    private readonly Release1PhoneCallService _phone;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _utcNow;
    private readonly TimeSpan _deadline;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _watchInterval;
    private readonly bool _publishIntroCall;
    private DateTime _startedUtc;
    private DateTime _nextPollUtc;
    private bool _loadCycleActive;
    private bool _observing;
    private bool _watching;
    private bool _hasEligibleContext;
    private Release1StoryHostContextSnapshot _eligibleContext;
    private bool _phoneAttemptedThisEpoch;
    private bool _disposed;

    public Release1TransitionPublisherService(
        IRelease1PostBenziesUnlockReader reader,
        Release1StoryRuntimeService story,
        Release1PhoneCallService phone,
        Action<string>? log = null,
        Func<DateTime>? utcNow = null,
        TimeSpan? deadline = null,
        TimeSpan? pollInterval = null,
        bool publishIntroCall = true,
        TimeSpan? watchInterval = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _phone = phone ?? throw new ArgumentNullException(nameof(phone));
        _log = log ?? (_ => { });
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _deadline = deadline ?? DefaultDeadline;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _watchInterval = watchInterval ?? DefaultWatchInterval;
        _publishIntroCall = publishIntroCall;
        if (_deadline <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(deadline));
        if (_pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        if (_watchInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(watchInterval));
    }

    public bool PromptVisible { get; private set; }

    /// <summary>
    /// True from <see cref="OnLoadComplete"/> until the intro eligibility observation completes,
    /// times out, or <see cref="OnPreLoad"/> runs. Callers building presentation inputs while this is
    /// true should treat the decision as still undetermined, since <see cref="PromptVisible"/> can be
    /// false during this window even though the intro decision is desired.
    ///
    /// This stays bounded on purpose: the projector skips decision reconciliation while it is true.
    /// A <c>Locked</c> result ends the observation but starts a slower watch for the rest of the load
    /// cycle (OC-79), which is not reported here because the decision is then determined: not offered
    /// yet, and re-evaluated when the cartel status flips.
    /// </summary>
    public bool EligibilityObserving => _observing;

    public void OnLoadComplete()
    {
        if (_disposed || _loadCycleActive) return;
        _loadCycleActive = true;
        var now = _utcNow();
        _startedUtc = now;
        _nextPollUtc = now;
        PromptVisible = false;
        _hasEligibleContext = false;
        _eligibleContext = default;
        _phoneAttemptedThisEpoch = false;
        _observing = true;
        _watching = false;
    }

    public void OnPreLoad()
    {
        _loadCycleActive = false;
        _observing = false;
        _watching = false;
        PromptVisible = false;
        _hasEligibleContext = false;
        _eligibleContext = default;
        _phoneAttemptedThisEpoch = false;
    }

    public void Update()
    {
        if (_disposed || !(_observing || _watching)) return;
        var now = _utcNow();
        if (_observing && now - _startedUtc >= _deadline)
        {
            _observing = false;
            return;
        }
        if (now < _nextPollUtc) return;
        _nextPollUtc = now + (_observing ? _pollInterval : _watchInterval);

        if (_story.Phase == Release1StoryRuntimePhase.AwaitingLoad)
            _story.OnLoadComplete();
        if (!_story.TryGetActiveContext(out var activeContext, out var rejectReason))
        {
            if (rejectReason is Release1StoryRuntimeRejectReason.Quarantined or Release1StoryRuntimeRejectReason.Disposed)
            {
                _observing = false;
                _watching = false;
            }
            return;
        }

        Release1PostBenziesUnlockReadStatus status;
        Release1PostBenziesUnlockSnapshot snapshot;
        try { status = _reader.TryRead(out snapshot); }
        catch (Exception exception)
        {
            _log($"Release 1 eligibility observation faulted: {exception.GetType().Name}");
            _observing = false;
            _watching = false;
            return;
        }

        if (status is Release1PostBenziesUnlockReadStatus.Pending or Release1PostBenziesUnlockReadStatus.NotAuthoritative)
            return;

        _observing = false;
        if (status == Release1PostBenziesUnlockReadStatus.Locked)
        {
            // The cartel is readable and not yet defeated. Keep a slow watch for the rest of the load
            // cycle: Unlocked is a one-way transition, and a player who defeats the cartel mid-session
            // must be offered the intro without reloading (OC-79). The deadline above bounds only the
            // undetermined observation; the watch has none.
            if (!_watching)
            {
                _watching = true;
                _nextPollUtc = now + _watchInterval;
            }
            return;
        }

        var resolvedByWatch = _watching;
        _watching = false;
        if (status != Release1PostBenziesUnlockReadStatus.Unlocked || snapshot.CartelStatus != Release1CartelStatus.Defeated || !SameContext(activeContext, snapshot.HostContext))
            return;
        if (resolvedByWatch)
            _log("Release 1 eligibility watch observed the cartel defeated mid-session.");

        _eligibleContext = snapshot.HostContext;
        _hasEligibleContext = true;
        var relationship = _story.State?.RelationshipState ?? Release1RelationshipState.Unstarted;
        if (relationship == Release1RelationshipState.Accepted)
        {
            if (_story.State is { } accepted && _story.LastPersistedRevision >= accepted.Revision)
                TryPublishNellOnce(snapshot.HostContext);
            return;
        }
        PromptVisible = relationship is Release1RelationshipState.Unstarted or Release1RelationshipState.Deferred;
    }

    public bool TryAccept()
    {
        if (!TryReadPromptContext(out var context)) return false;
        var relationship = _story.State?.RelationshipState ?? Release1RelationshipState.Unstarted;
        if (relationship is not (Release1RelationshipState.Unstarted or Release1RelationshipState.Deferred)) return false;

        var result = _story.TryExecuteDurably(CreateIntroCommand(context, Release1TransitionKind.IntroAccepted, IntroAcceptedReceipt));
        if (!IsDurablyAccepted(result)) return false;

        PromptVisible = false;
        TryPublishNellOnce(context);
        return true;
    }

    public bool TryDefer()
    {
        if (!TryReadPromptContext(out var context)) return false;
        if (_story.State?.RelationshipState == Release1RelationshipState.Deferred)
        {
            PromptVisible = false;
            return true;
        }
        if (_story.State is not null && _story.State.RelationshipState != Release1RelationshipState.Unstarted) return false;

        var result = _story.TryExecuteDurably(CreateIntroCommand(context, Release1TransitionKind.IntroDeferred, IntroDeferredReceipt));
        if (!IsDurablyAccepted(result)) return false;
        PromptVisible = false;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        OnPreLoad();
        _disposed = true;
    }

    private static bool SameContext(Release1StoryHostContextSnapshot left, Release1StoryHostContextSnapshot right) =>
        left.SessionEpoch == right.SessionEpoch &&
        left.LoadEpoch == right.LoadEpoch &&
        string.Equals(left.PlayerId, right.PlayerId, StringComparison.Ordinal) &&
        string.Equals(
            Path.GetFullPath(left.ActiveSaveFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right.ActiveSaveFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private bool TryReadPromptContext(out Release1StoryHostContextSnapshot context)
    {
        context = default;
        if (_disposed || !PromptVisible || !_hasEligibleContext) return false;
        if (!_story.TryGetActiveContext(out var current, out _) || !SameContext(current, _eligibleContext)) return false;
        context = current;
        return true;
    }

    private bool IsDurablyAccepted(Release1StoryRuntimeCommandResult result) =>
        result.Accepted && result.State is not null && result.State.Revision == _story.LastPersistedRevision;

    private void TryPublishNellOnce(Release1StoryHostContextSnapshot context)
    {
        if (!_publishIntroCall) return;
        if (_phoneAttemptedThisEpoch) return;
        _phoneAttemptedThisEpoch = true;
        try
        {
            _phone.TryQueue(CreateNellRequest(context));
        }
        catch (Exception exception)
        {
            _log($"Release 1 Nell request publication failed closed: {exception.GetType().Name}");
        }
    }

    private static Release1StoryCommand CreateIntroCommand(
        Release1StoryHostContextSnapshot context,
        Release1TransitionKind transition,
        string receipt) => new(
            context.SessionEpoch,
            context.LoadEpoch,
            context.PlayerId,
            Release1MissionCatalog.IntroScopeKey,
            0,
            transition,
            receipt,
            Release1LogicalCorrelation.Create(context.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, transition, receipt).Value);

    private static Release1PhoneCallRequest CreateNellRequest(Release1StoryHostContextSnapshot context) =>
        Release1PhoneCallRequest.Create(
            context,
            Release1MissionCatalog.SmallCourtesy,
            1,
            Release1PhoneCallCorrelation.Create(
                context.PlayerId,
                Release1MissionCatalog.SmallCourtesy,
                1,
                Release1PhoneCallRole.Nell,
                NellPhoneReceipt),
            Release1PhoneCallRole.Nell,
            "Nell Grey",
            NellIntroStages);
}
