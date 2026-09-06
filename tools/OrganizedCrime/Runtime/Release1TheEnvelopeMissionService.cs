using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class Release1TheEnvelopeMissionService : IDisposable
{
    private const string TermsVersion = "the-envelope-v1";
    private const double TimedStageDurationHours = 24d;
    private const string EffectScope = "oc-the-envelope";
    // A cash-balance read is float precision while the plan's frozen balances are double whole
    // dollars; this bounds the float round-trip error, far below one cent, without loosening the
    // whole-dollar exactness the plan itself already enforces.
    private const double CashReadTolerance = 0.01d;
    private const string ArthurCallerLabel = "Arthur Selby";
    private static readonly string[] ArthurWarningStages =
    {
        "The envelope did not land and the window is closed. You really want me to be your enemy?",
        "Nell has arranged one more chance. A smaller number, twenty four hours from acceptance.",
        "Do not make her ask twice."
    };
    private static readonly string[] ArthurEndingStages =
    {
        "You might have stumbled your way through this, but now we see if you can really handle the fucking big times."
    };

    private readonly Release1StoryRuntimeService _story;
    private readonly IRelease1SmallCourtesyWorld _world;
    private readonly Release1PhoneCallService? _phone;
    private readonly Action<string> _log;
    private bool _loadActive;
    private bool _saving;
    private bool _dismissed;
    private bool _disposed;
    private Release1TheEnvelopeQuote? _reviewedQuote;
    private double? _lastConvergenceGameMinute;
    private long _lastConvergenceRevision = -1;
    private readonly HashSet<string> _reportedHolds = new(StringComparer.Ordinal);
    private string? _lockedClosetGuid;
    private readonly List<int> _lockedSlotIndexes = new();

    public Release1TheEnvelopeMissionService(
        Release1StoryRuntimeService story,
        IRelease1SmallCourtesyWorld world,
        Release1PhoneCallService? phone = null,
        Action<string>? log = null)
    {
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _phone = phone;
        _log = log ?? (_ => { });
    }

    public Release1TheEnvelopeOfferStatus OfferStatus { get; private set; } = Release1TheEnvelopeOfferStatus.Inactive;
    public Release1TheEnvelopeQuote? ReviewedQuote => _reviewedQuote;

    /// <summary>
    /// The timing seam the deposit receipt reports through. Assigned by the mod shell after
    /// construction; the default disabled seam changes nothing.
    /// </summary>
    public OrganizedCrimeTimingReceipts Timing { get; set; } = OrganizedCrimeTimingReceipts.Disabled;

    public void OnLoadComplete()
    {
        if (_disposed || _loadActive) return;
        _loadActive = true;
        _saving = false;
        _dismissed = false;
        _reviewedQuote = null;
        _lastConvergenceGameMinute = null;
        _reportedHolds.Clear();
        RefreshOfferStatus();
        Converge();
    }

    public void OnPreLoad()
    {
        if (_disposed) return;
        ReleaseSlotLocks();
        _loadActive = false;
        _saving = false;
        _dismissed = false;
        _reviewedQuote = null;
        _lastConvergenceGameMinute = null;
        _reportedHolds.Clear();
        OfferStatus = Release1TheEnvelopeOfferStatus.Inactive;
    }

    public void OnSaveStart()
    {
        if (_disposed || !_loadActive) return;
        _saving = true;
        _lastConvergenceGameMinute = null;
        OfferStatus = Release1TheEnvelopeOfferStatus.Saving;
    }

    public void OnSaveComplete()
    {
        if (_disposed || !_loadActive) return;
        _saving = false;
        _lastConvergenceGameMinute = null;
        RefreshOfferStatus();
        Converge();
    }

    /// <summary>
    /// The per frame convergence pass. Everything past the throttle gate reads the world through
    /// IL2CPP, which is far too expensive to repeat at frame rate. The mission's decision table is
    /// expressed in game time (the deadline in game hours), and a cash deposit the player makes
    /// mid minute is still picked up on the next pass, so one pass per game minute observes every
    /// state the table can distinguish. Every lifecycle boundary clears the stamp, and so does an
    /// accepted player decision, so those always converge on their own pass.
    /// </summary>
    public void Update()
    {
        if (_disposed || !_loadActive || _saving) return;
        if (!TryBeginConvergencePass()) return;
        var contextStatus = ReadMatchingContext(out _);
        if (contextStatus != Release1TheEnvelopeReviewStatus.Ready)
        {
            OfferStatus = Map(contextStatus);
            return;
        }

        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted) return;
        var mission = Mission(state);
        if (mission.State is (Release1MissionState.Active or Release1MissionState.MakeGoodActive) &&
            mission.DeadlineGameTimeHours is not null &&
            TryReadGameHours(out var gameHours) &&
            gameHours >= mission.DeadlineGameTimeHours.Value &&
            !TryReconcileExecutedConsumptionEffect(mission))
        {
            var transition = mission.State == Release1MissionState.Active
                ? Release1TransitionKind.RequiredFailure
                : Release1TransitionKind.MakeGoodFailed;
            var receipt = $"the-envelope-deadline-{DeadlineToken(mission.State)}-v1-a{mission.Attempt}";
            _story.TryExecuteDurably(CreateCommand(transition, mission.Attempt, receipt, null, null, null));
            RefreshOfferStatus();
        }

        Converge();
    }

    public Release1TheEnvelopeReviewResult TryReview()
    {
        if (_disposed) return Review(Release1TheEnvelopeReviewStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive) return Review(Release1TheEnvelopeReviewStatus.Inactive, "No load epoch is active.");
        if (_saving) return Review(Release1TheEnvelopeReviewStatus.Saving, "Mission review is deferred during saving.");
        if (_dismissed) return Review(Release1TheEnvelopeReviewStatus.Dismissed, "Offer was dismissed for this load.");

        var status = TryBuildQuote(out var quote);
        if (status != Release1TheEnvelopeReviewStatus.Ready)
        {
            _reviewedQuote = null;
            OfferStatus = Map(status);
            return Review(status, Message(status));
        }

        _reviewedQuote = quote;
        OfferStatus = Release1TheEnvelopeOfferStatus.Available;
        return new(Release1TheEnvelopeReviewStatus.Ready, quote, "The Envelope terms are ready for review.");
    }

    public Release1TheEnvelopeDecisionResult TryAccept()
    {
        if (_disposed) return Decision(Release1TheEnvelopeDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive || _dismissed) return Decision(Release1TheEnvelopeDecisionStatus.Inactive, "No active offer is available.");
        if (_saving) return Decision(Release1TheEnvelopeDecisionStatus.PersistenceDeferred, "Acceptance is deferred during saving.");
        if (_reviewedQuote is null) return Decision(Release1TheEnvelopeDecisionStatus.ReviewRequired, "Review the current terms before accepting.");

        var status = TryBuildQuote(out var currentQuote);
        if (status != Release1TheEnvelopeReviewStatus.Ready || currentQuote != _reviewedQuote)
        {
            _reviewedQuote = null;
            RefreshOfferStatus();
            return Decision(Release1TheEnvelopeDecisionStatus.QuoteChanged, "The terms changed; review again.");
        }
        if (!TryReadGameHours(out var acceptedHours))
            return Decision(Release1TheEnvelopeDecisionStatus.Rejected, "Canonical game time was unavailable.");

        var assignment = currentQuote!.Assignment;
        var transition = assignment.Mode switch
        {
            Release1TheEnvelopeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1TheEnvelopeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1TheEnvelopeAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new InvalidOperationException("Assignment mode was not defined.")
        };
        var command = CreateCommand(
            transition,
            assignment.Attempt,
            AcceptanceReceipt(assignment.Mode, assignment.Attempt),
            assignment.Mode == Release1TheEnvelopeAssignmentMode.Primary ? TermsVersion : null,
            acceptedHours,
            assignment.Mode == Release1TheEnvelopeAssignmentMode.Recovery ? null : acceptedHours + TimedStageDurationHours);
        var result = _story.TryExecuteTheEnvelopeAcceptanceDurably(command, assignment);
        if (result.Status == Release1StoryCommandStatus.DeferredSaving)
            return Decision(Release1TheEnvelopeDecisionStatus.PersistenceDeferred, result.Message);
        if (!result.Accepted)
            return Decision(Release1TheEnvelopeDecisionStatus.Rejected, result.Message);

        _reviewedQuote = null;
        _lastConvergenceGameMinute = null;
        RefreshOfferStatus();
        Converge();
        return Decision(Release1TheEnvelopeDecisionStatus.Accepted, "The Envelope stage was accepted.");
    }

    public Release1TheEnvelopeDecisionResult TryDefer()
    {
        if (_disposed) return Decision(Release1TheEnvelopeDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive || _saving || ReadMatchingContext(out _) != Release1TheEnvelopeReviewStatus.Ready)
            return Decision(Release1TheEnvelopeDecisionStatus.Inactive, "No active offer is available.");
        var state = _story.State;
        var mission = state is null ? null : Mission(state);
        if (state?.RelationshipState != Release1RelationshipState.Accepted || mission?.State != Release1MissionState.Offered)
            return TryDismiss();

        var receipt = $"the-envelope-defer-v1-a{mission.Attempt}";
        var result = _story.TryExecuteDurably(CreateCommand(
            Release1TransitionKind.MissionDeferred, mission.Attempt, receipt, null, null, null));
        if (result.Status == Release1StoryCommandStatus.DeferredSaving)
            return Decision(Release1TheEnvelopeDecisionStatus.PersistenceDeferred, result.Message);
        if (!result.Accepted)
            return Decision(Release1TheEnvelopeDecisionStatus.Rejected, result.Message);

        DismissForLoad();
        return Decision(Release1TheEnvelopeDecisionStatus.Deferred, "The Envelope was deferred until a later load.");
    }

    public Release1TheEnvelopeDecisionResult TryDismiss()
    {
        if (_disposed) return Decision(Release1TheEnvelopeDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive) return Decision(Release1TheEnvelopeDecisionStatus.Inactive, "No load epoch is active.");
        DismissForLoad();
        return Decision(Release1TheEnvelopeDecisionStatus.Dismissed, "The Envelope was dismissed for this load.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        OnPreLoad();
        _disposed = true;
        OfferStatus = Release1TheEnvelopeOfferStatus.Disposed;
    }

    private Release1TheEnvelopeReviewStatus TryBuildQuote(out Release1TheEnvelopeQuote? quote)
    {
        try { return TryBuildQuoteCore(out quote); }
        catch
        {
            quote = null;
            return Release1TheEnvelopeReviewStatus.Unavailable;
        }
    }

    private Release1TheEnvelopeReviewStatus TryBuildQuoteCore(out Release1TheEnvelopeQuote? quote)
    {
        quote = null;
        var contextStatus = ReadMatchingContext(out var context);
        if (contextStatus != Release1TheEnvelopeReviewStatus.Ready) return contextStatus;
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted)
            return Release1TheEnvelopeReviewStatus.Ineligible;
        var keepTheLightsOff = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];
        if (keepTheLightsOff.State != Release1MissionState.Satisfied)
            return Release1TheEnvelopeReviewStatus.Ineligible;

        var mission = Mission(state);
        var mode = mission.State switch
        {
            Release1MissionState.Offered => Release1TheEnvelopeAssignmentMode.Primary,
            Release1MissionState.MakeGoodOffered => Release1TheEnvelopeAssignmentMode.MakeGood,
            Release1MissionState.RecoveryAvailable => Release1TheEnvelopeAssignmentMode.Recovery,
            _ => (Release1TheEnvelopeAssignmentMode?)null
        };
        if (mode is null) return Release1TheEnvelopeReviewStatus.Ineligible;

        var attempt = mode == Release1TheEnvelopeAssignmentMode.Primary ? mission.Attempt : mission.Attempt + 1;
        var transition = mode switch
        {
            Release1TheEnvelopeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1TheEnvelopeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1TheEnvelopeAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new InvalidOperationException("Assignment mode was not defined.")
        };
        var correlation = Release1LogicalCorrelation.Create(
            context.PlayerId,
            Release1MissionCatalog.TheEnvelope,
            attempt,
            transition,
            AcceptanceReceipt(mode.Value, attempt)).Value;

        if (!Release1TheEnvelopeAssignmentSelector.TrySelect(mode.Value, attempt, correlation, out var assignment, out _))
            return Release1TheEnvelopeReviewStatus.Unavailable;

        quote = new(assignment!, mode == Release1TheEnvelopeAssignmentMode.Recovery ? null : TimedStageDurationHours);
        quote.Validate();
        return Release1TheEnvelopeReviewStatus.Ready;
    }

    private void RefreshOfferStatus()
    {
        if (_disposed) { OfferStatus = Release1TheEnvelopeOfferStatus.Disposed; return; }
        if (!_loadActive) { OfferStatus = Release1TheEnvelopeOfferStatus.Inactive; return; }
        if (_saving) { OfferStatus = Release1TheEnvelopeOfferStatus.Saving; return; }
        if (_dismissed) { OfferStatus = Release1TheEnvelopeOfferStatus.Dismissed; return; }
        OfferStatus = Map(TryBuildQuote(out _));
    }

    private Release1TheEnvelopeReviewStatus ReadMatchingContext(out Release1StoryHostContextSnapshot context)
    {
        context = default;
        if (!_story.TryGetActiveContext(out var storyContext, out _)) return Release1TheEnvelopeReviewStatus.Inactive;
        try
        {
            var status = _world.TryReadContext(out var worldContext);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready) return Map(status);
            if (!SameContext(storyContext, worldContext)) return Release1TheEnvelopeReviewStatus.Inactive;
            context = storyContext;
            return Release1TheEnvelopeReviewStatus.Ready;
        }
        catch
        {
            return Release1TheEnvelopeReviewStatus.Unavailable;
        }
    }

    private bool TryReadGameHours(out double gameHours)
    {
        gameHours = 0;
        try
        {
            if (_world.TryReadCanonicalTotalMinutes(out var totalMinutes) != Release1SmallCourtesyWorldReadStatus.Ready ||
                !double.IsFinite(totalMinutes) || totalMinutes < 0)
                return false;
            gameHours = totalMinutes / 60d;
            return double.IsFinite(gameHours);
        }
        catch
        {
            return false;
        }
    }

    private Release1StoryCommand CreateCommand(
        Release1TransitionKind transition,
        int attempt,
        string receipt,
        string? termsVersion,
        double? acceptedHours,
        double? deadlineHours)
    {
        if (!_story.TryGetActiveContext(out var context, out _))
            throw new InvalidOperationException("Story context was not active.");
        return new(
            context.SessionEpoch,
            context.LoadEpoch,
            context.PlayerId,
            Release1MissionCatalog.TheEnvelope,
            attempt,
            transition,
            receipt,
            Release1LogicalCorrelation.Create(context.PlayerId, Release1MissionCatalog.TheEnvelope, attempt, transition, receipt).Value,
            termsVersion,
            acceptedHours,
            deadlineHours);
    }

    private void DismissForLoad()
    {
        _dismissed = true;
        _reviewedQuote = null;
        OfferStatus = Release1TheEnvelopeOfferStatus.Dismissed;
    }

    internal static Release1MissionRecord Mission(Release1StoryState state) =>
        state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope)];

    private static string AcceptanceReceipt(Release1TheEnvelopeAssignmentMode mode, int attempt) =>
        $"the-envelope-{mode.ToString().ToLowerInvariant()}-accept-v1-a{attempt}";

    private static string DeadlineToken(Release1MissionState state) =>
        state == Release1MissionState.Active ? "primary" : "make-good";

    private static bool SameContext(Release1StoryHostContextSnapshot left, Release1StoryHostContextSnapshot right) =>
        left.SessionEpoch == right.SessionEpoch &&
        left.LoadEpoch == right.LoadEpoch &&
        string.Equals(left.PlayerId, right.PlayerId, StringComparison.Ordinal) &&
        string.Equals(
            Path.GetFullPath(left.ActiveSaveFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right.ActiveSaveFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static Release1TheEnvelopeReviewResult Review(Release1TheEnvelopeReviewStatus status, string message) =>
        new(status, null, message);

    private static Release1TheEnvelopeDecisionResult Decision(Release1TheEnvelopeDecisionStatus status, string message) =>
        new(status, message);

    private static Release1TheEnvelopeReviewStatus Map(Release1SmallCourtesyWorldReadStatus status) => status switch
    {
        Release1SmallCourtesyWorldReadStatus.Pending or Release1SmallCourtesyWorldReadStatus.NotAuthoritative => Release1TheEnvelopeReviewStatus.Pending,
        _ => Release1TheEnvelopeReviewStatus.Unavailable
    };

    private static Release1TheEnvelopeOfferStatus Map(Release1TheEnvelopeReviewStatus status) => status switch
    {
        Release1TheEnvelopeReviewStatus.Ready => Release1TheEnvelopeOfferStatus.Available,
        Release1TheEnvelopeReviewStatus.Inactive => Release1TheEnvelopeOfferStatus.Inactive,
        Release1TheEnvelopeReviewStatus.Ineligible => Release1TheEnvelopeOfferStatus.Ineligible,
        Release1TheEnvelopeReviewStatus.Pending => Release1TheEnvelopeOfferStatus.Pending,
        Release1TheEnvelopeReviewStatus.Saving => Release1TheEnvelopeOfferStatus.Saving,
        Release1TheEnvelopeReviewStatus.Dismissed => Release1TheEnvelopeOfferStatus.Dismissed,
        Release1TheEnvelopeReviewStatus.Disposed => Release1TheEnvelopeOfferStatus.Disposed,
        _ => Release1TheEnvelopeOfferStatus.Unavailable
    };

    private static string Message(Release1TheEnvelopeReviewStatus status) => status switch
    {
        Release1TheEnvelopeReviewStatus.Pending => "World eligibility is still pending.",
        Release1TheEnvelopeReviewStatus.Ineligible => "The Envelope is not currently offerable.",
        Release1TheEnvelopeReviewStatus.Inactive => "The canonical story context is inactive.",
        _ => "The Envelope world data is unavailable."
    };

    /// <summary>
    /// Reads the whole Syndicate HQ hold room, classifies the cash it holds against the frozen
    /// assignment amount, and either records a shortfall/spread observation or hands a sufficient
    /// closet's cash to the plan builder. Nothing here mutates the world; <see cref="BeginTransaction"/>
    /// (Task 4) is the only member allowed to lock or decrement a closet slot.
    /// </summary>
    public Release1TheEnvelopeDepositStatus ReconcileDeposit()
    {
        if (_disposed || !_loadActive || _saving || ReadMatchingContext(out _) != Release1TheEnvelopeReviewStatus.Ready)
            return Release1TheEnvelopeDepositStatus.NoWork;

        // A stuck lock only ever means an earlier pass's own release attempt failed; ConsumeEnvelope
        // always takes and drops its locks within the one pass that acquired them, so a non-null
        // _lockedClosetGuid on entry here can never be a currently-open transaction, only an
        // abandoned one waiting to be retried. Retrying it opportunistically at the top of every pass,
        // rather than only from OnPreLoad, both clears the pin the moment the world lets it (even
        // while the blocked effect that caused it stays permanently Ambiguous and never calls
        // ConsumeEnvelope again to retry it itself) and guarantees a later deposit is never pinned out
        // of a different closet by a stale, already-abandoned lock on this one.
        if (_lockedClosetGuid is not null) ReleaseSlotLocks();

        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted)
            return Release1TheEnvelopeDepositStatus.NoWork;
        var mission = Mission(state);
        var assignment = state.TheEnvelopeAssignments.SingleOrDefault(candidate => candidate.Attempt == mission.Attempt);
        if (assignment is null) return Release1TheEnvelopeDepositStatus.NoWork;

        // Once an effect exists, ContinueDeposit reconciles it regardless of the mission's current
        // state: completion (TryComplete, inside FinishDeposit) fires the moment the effect is
        // Applied, well before a save durably persists it, so the post-save commit pass still has to
        // reach ContinueDeposit even after the mission has already moved on to Satisfied. Starting a
        // brand new deposit is the only path that requires an active stage (TryGetActiveStage), since
        // only that gate proves the authorization the fresh Prepared effect would be issued against.
        var effect = state.NativeEffects.FirstOrDefault(candidate => candidate.EffectId == ConsumptionEffectId(mission.Attempt));
        if (effect is null)
        {
            if (!TryGetActiveStage(out _, out _, out var authorization)) return Release1TheEnvelopeDepositStatus.NoWork;
            return BeginDeposit(mission, assignment, authorization);
        }
        return ContinueDeposit(mission, assignment, effect);
    }

    /// <summary>
    /// One room read per pass, as Release1RoomWithNoNameMissionService.ReconcileHold does it, plus one
    /// cash read per occupied slot. A cash read that is neither Ready nor Unavailable makes that whole
    /// closet unreadable, so a transient fault is never read as "no cash"; non cash items read
    /// Unavailable and are simply absent from the cash list.
    /// </summary>
    private bool TryReadRoomCash(
        Release1TheEnvelopeAssignment assignment,
        out Release1HoldRoomSnapshot? room,
        out IReadOnlyList<Release1TheEnvelopeClosetCash> closets)
    {
        room = null;
        closets = Array.Empty<Release1TheEnvelopeClosetCash>();
        try
        {
            if (_world.TryReadHoldRoom(out var read) != Release1SmallCourtesyWorldReadStatus.Ready || read is null) return false;
            room = read;
            if (read.Readiness != Release1HoldRoomReadiness.Ready || read.Closets.Count != assignment.ExpectedClosetCount)
                return true;

            var built = new List<Release1TheEnvelopeClosetCash>(read.Closets.Count);
            foreach (var closet in read.Closets.OrderBy(candidate => candidate.ClosetGuid, StringComparer.Ordinal))
            {
                var cash = new List<Release1TheEnvelopeCashSlot>();
                var readable = true;
                foreach (var slot in closet.Slots.Where(candidate => candidate.Quantity > 0).OrderBy(candidate => candidate.SlotIndex))
                {
                    var status = _world.TryReadHoldRoomSlotCashBalance(closet.ClosetGuid, slot.SlotIndex, out var balance);
                    if (status == Release1SmallCourtesyWorldReadStatus.Ready) cash.Add(new(slot.SlotIndex, balance));
                    else if (status != Release1SmallCourtesyWorldReadStatus.Unavailable) readable = false;
                }
                built.Add(new(closet.ClosetGuid, readable, cash));
            }
            closets = built;
            return true;
        }
        catch
        {
            room = null;
            closets = Array.Empty<Release1TheEnvelopeClosetCash>();
            return false;
        }
    }

    private Release1TheEnvelopeDepositStatus BeginDeposit(
        Release1MissionRecord mission,
        Release1TheEnvelopeAssignment assignment,
        string authorization)
    {
        if (!TryReadRoomCash(assignment, out var room, out var closets))
        {
            ReportHoldOnce(assignment.Attempt, "the hold room could not be read this pass; nothing was changed.");
            return Release1TheEnvelopeDepositStatus.Unavailable;
        }

        var observation = Release1TheEnvelopeClosetClassifier.Classify(
            room, closets, assignment.ExpectedClosetCount, assignment.AmountWholeDollars,
            out var cashClosetGuid, out var cashSum);

        switch (observation)
        {
            case Release1TheEnvelopeClosetObservation.RoomNotReady:
                ReportHoldOnce(assignment.Attempt, "the hold room did not read ready with nine closets; the pass changed nothing.");
                return Release1TheEnvelopeDepositStatus.Unavailable;
            case Release1TheEnvelopeClosetObservation.ClosetUnreadable:
                ReportHoldOnce(assignment.Attempt, "a closet read back empty or unreadable; a zero length read is not treated as empty.");
                return Release1TheEnvelopeDepositStatus.Unavailable;
            case Release1TheEnvelopeClosetObservation.NoCash:
                return Release1TheEnvelopeDepositStatus.NoWork;
            case Release1TheEnvelopeClosetObservation.Spread:
                SetProgress(assignment.Attempt, spreadNoticed: true);
                ReportHoldOnce(assignment.Attempt, "cash is in more than one closet; the deposit will consume none of it.");
                return Release1TheEnvelopeDepositStatus.Held;
            case Release1TheEnvelopeClosetObservation.Shortfall:
                SetProgress(assignment.Attempt, observedBalance: cashSum,
                    lastShortfallNoticed: assignment.AmountWholeDollars - cashSum);
                return Release1TheEnvelopeDepositStatus.NoWork;
        }

        var cashSlots = closets
            .Single(closet => string.Equals(closet.ClosetGuid, cashClosetGuid, StringComparison.Ordinal))
            .CashSlots;
        if (!Release1TheEnvelopeClosetPlan.TryBuild(cashSlots, cashClosetGuid!, assignment.AmountWholeDollars, out var plan) ||
            plan is null)
        {
            SetProgress(assignment.Attempt, observedBalance: cashSum);
            ReportHoldOnce(assignment.Attempt, "the closet's cash could not be planned within the effect identity bound; nothing was consumed.");
            return Release1TheEnvelopeDepositStatus.Held;
        }

        return BeginTransaction(mission, assignment, authorization, plan);
    }

    private Release1TheEnvelopeDepositStatus BeginTransaction(
        Release1MissionRecord mission,
        Release1TheEnvelopeAssignment assignment,
        string authorization,
        Release1TheEnvelopeClosetPlan plan)
    {
        var prepared = new Release1NativeEffectJournalEntry(
            ConsumptionEffectId(assignment.Attempt),
            Release1MissionCatalog.TheEnvelope,
            assignment.Attempt,
            "CashTransfer",
            plan.ClosetGuid,
            EffectScope,
            plan.Serialize(),
            Release1NativeEffectPhase.Prepared,
            null,
            _story.State!.Revision + 1,
            AuthorizedStoryCorrelationId: authorization,
            AuthorizedMissionRevision: mission.Revision);
        if (!_story.TryPrepareNativeEffect(prepared).Accepted)
            return Release1TheEnvelopeDepositStatus.Rejected;

        var freshEffect = _story.State!.NativeEffects.Single(candidate => candidate.EffectId == prepared.EffectId);
        var consumeResult = ConsumeEnvelope(plan, freshEffect);
        if (consumeResult is { } blockedOrHeld) return blockedOrHeld;
        return FinishDeposit(assignment, freshEffect, authorization);
    }

    /// <summary>
    /// Takes every planned slot's lock before the first decrement, so the failure window is one slot
    /// wide, then decrements in ascending slot index order, verifying each slot's exact post state
    /// before touching the next. A slot already at its planned post balance is skipped, which is what
    /// lets a surviving Prepared effect drain only the slots still at pre. A failed lock take is not a
    /// mid plan failure: no slot has been touched yet, so every lock taken so far is released, the
    /// effect stays Prepared and unblocked, and the pass holds for a later retry rather than blocking
    /// forever (per OC-61 decision 7, no cash has moved). Any failure once a decrement has actually
    /// been attempted stops the loop, blocks the effect, and leaves every later slot untouched. There
    /// is no insertion path, so slots already drained stay drained: the one irreversible outcome,
    /// bounded by the per slot verify. Returns null on success (the caller should proceed to
    /// <see cref="FinishDeposit"/>), or the status the caller should return immediately otherwise.
    /// </summary>
    private Release1TheEnvelopeDepositStatus? ConsumeEnvelope(Release1TheEnvelopeClosetPlan plan, Release1NativeEffectJournalEntry effect)
    {
        if (!TryAcquireSlotLocks(plan.ClosetGuid, plan.Slots))
        {
            ReleaseSlotLocks();
            ReportHoldOnce(effect.Attempt, "a closet slot lock could not be taken this pass; the deposit will retry once the lock is free, and no cash has moved.");
            return Release1TheEnvelopeDepositStatus.Held;
        }
        try
        {
            foreach (var slot in plan.Slots)
            {
                if (MatchesSlotBalance(plan.ClosetGuid, slot.SlotIndex, slot.PostBalance)) continue;
                if (!MatchesSlotBalance(plan.ClosetGuid, slot.SlotIndex, slot.PreBalance)) { BlockConsumption(effect); return Release1TheEnvelopeDepositStatus.Ambiguous; }

                var take = (float)(slot.PreBalance - slot.PostBalance);
                Release1SmallCourtesyWorldMutationStatus changed;
                try { changed = _world.TryChangeHoldRoomSlotCashBalance(plan.ClosetGuid, slot.SlotIndex, -take); }
                catch { BlockConsumption(effect); return Release1TheEnvelopeDepositStatus.Ambiguous; }
                if (changed != Release1SmallCourtesyWorldMutationStatus.Succeeded) { BlockConsumption(effect); return Release1TheEnvelopeDepositStatus.Ambiguous; }
                if (!MatchesSlotBalance(plan.ClosetGuid, slot.SlotIndex, slot.PostBalance)) { BlockConsumption(effect); return Release1TheEnvelopeDepositStatus.Ambiguous; }
            }
            return MarkConsumptionApplied(effect, plan.Slots.Sum(slot => slot.PostBalance))
                ? null
                : Release1TheEnvelopeDepositStatus.Ambiguous;
        }
        finally
        {
            ReleaseSlotLocks();
        }
    }

    /// <summary>
    /// True when the slot reads exactly <paramref name="expected"/> within
    /// <see cref="CashReadTolerance"/>: a cash balance read of that value when it is positive, or a
    /// positively confirmed Unavailable read when it is zero, since a drained stack may vanish or may
    /// survive at a zero balance and both are correct. Faulted, Pending, and NotAuthoritative are
    /// never a match; only Unavailable positively confirms an emptied slot.
    /// </summary>
    private bool MatchesSlotBalance(string closetGuid, int slotIndex, double expected)
    {
        Release1SmallCourtesyWorldReadStatus status;
        float read;
        try { status = _world.TryReadHoldRoomSlotCashBalance(closetGuid, slotIndex, out read); }
        catch { return false; }
        return expected == 0d
            ? status == Release1SmallCourtesyWorldReadStatus.Unavailable
            : status == Release1SmallCourtesyWorldReadStatus.Ready && Math.Abs(read - expected) <= CashReadTolerance;
    }

    private bool TryAcquireSlotLocks(string closetGuid, IReadOnlyList<Release1TheEnvelopePlannedSlot> slots)
    {
        if (_lockedClosetGuid is not null && !string.Equals(_lockedClosetGuid, closetGuid, StringComparison.Ordinal)) return false;
        _lockedClosetGuid = closetGuid;
        foreach (var slot in slots)
        {
            if (_lockedSlotIndexes.Contains(slot.SlotIndex)) continue;
            try
            {
                if (_world.TrySetHoldRoomSlotLocked(closetGuid, slot.SlotIndex, true) != Release1SmallCourtesyWorldMutationStatus.Succeeded)
                    return false;
            }
            catch
            {
                return false;
            }
            _lockedSlotIndexes.Add(slot.SlotIndex);
        }
        return true;
    }

    // Clearing LastShortfallNoticed lives here, at the exact step that writes the Applied phase
    // (whether freshly consumed this pass or inferred applied from an exact post-state match on a
    // reconstructed service), rather than in BeginDeposit's shortfall branch alone: a shortfall
    // recorded on an earlier pass would otherwise ride forever once the player tops the slot back up
    // to or above the frozen amount and the deposit is actually consumed. postSum is what remains in
    // the plan's own slots after consumption (the partial last stack's residual, or zero once every
    // planned slot is fully drained), so ObservedBalance tracks the closet honestly even though the
    // deposit itself is already settled.
    private bool MarkConsumptionApplied(Release1NativeEffectJournalEntry effect, double postSum)
    {
        if (!_story.TryMarkNativeEffectApplied(
                effect.EffectId,
                $"the-envelope-cash-native-v1-a{effect.Attempt}",
                Release1NativeEffectPersistenceMode.RevertTolerant).Accepted)
            return false;
        SetProgress(effect.Attempt, observedBalance: postSum);
        return true;
    }

    private Release1TheEnvelopeDepositStatus BlockConsumption(Release1NativeEffectJournalEntry effect)
    {
        if (effect.Phase == Release1NativeEffectPhase.Prepared && !effect.ExecutionBlocked)
            _story.TryRecordNativeEffectIssuance(effect.EffectId, Release1NativeEffectIssuanceOutcome.Ambiguous);
        ReleaseSlotLocks();
        return Release1TheEnvelopeDepositStatus.Ambiguous;
    }

    private Release1TheEnvelopeDepositStatus ContinueDeposit(
        Release1MissionRecord mission,
        Release1TheEnvelopeAssignment assignment,
        Release1NativeEffectJournalEntry effect)
    {
        var authorization = effect.AuthorizedStoryCorrelationId;
        var closetGuid = effect.SourceIdentity ?? string.Empty;
        if (string.IsNullOrEmpty(authorization) ||
            !Release1TheEnvelopeClosetPlan.TryParse(effect.AmountOrCargoIdentity, closetGuid, out var plan) ||
            plan is null ||
            effect.Attempt != mission.Attempt ||
            !string.Equals(effect.EffectKind, "CashTransfer", StringComparison.Ordinal) ||
            !string.Equals(effect.DestinationIdentity, EffectScope, StringComparison.Ordinal) ||
            plan.ConsumedAmount != assignment.AmountWholeDollars)
            return BlockConsumption(effect);
        if (effect.ExecutionBlocked) return Release1TheEnvelopeDepositStatus.Ambiguous;

        if (effect.Phase == Release1NativeEffectPhase.Prepared)
        {
            // The same guard BeginDeposit applies: a room that cannot be read, or a closet that reads
            // back with zero slots or is absent, confirms nothing, and no cash call runs until a
            // genuine read succeeds.
            if (!TryReadRoomCash(assignment, out var room, out _) ||
                room is null ||
                room.Readiness != Release1HoldRoomReadiness.Ready ||
                room.Closets.Count != assignment.ExpectedClosetCount ||
                room.Closets.All(closet => !string.Equals(closet.ClosetGuid, closetGuid, StringComparison.Ordinal)) ||
                room.Closets.Any(closet => closet.SlotCount == 0))
            {
                ReportHoldOnce(assignment.Attempt, "the hold room could not be read back to reconcile the deposit; a zero length or uninitialized read is not treated as confirmation.");
                return Release1TheEnvelopeDepositStatus.Unavailable;
            }

            // Controller decision: a Prepared effect whose every planned slot already reads its post
            // balance is marked Applied here with no further decrement, never re-drained. Prepare,
            // decrement, and mark all run inside one ConsumeEnvelope pass, and a freshly Prepared
            // effect is never itself persisted on its own (BeginTransaction only returns once
            // ConsumeEnvelope has already run its course), so the only way a Prepared effect can
            // survive to a later pass with every planned slot already at post is that an earlier
            // pass's own decrements already ran and only the Applied mark failed to record (see
            // MarkConsumptionApplied's caller in ConsumeEnvelope). Marking it Applied now, with zero
            // further world mutation, completes that same interrupted pass rather than re-executing
            // it. Symmetrically, a planned slot that instead reads back at its pre balance again,
            // after the effect once reached this post state, can only mean the world itself reverted
            // (a quit without saving, per test 13): there is no insertion path that ever puts cash
            // back into an already emptied slot, so treating that as "re-run the plan from pre" below
            // is always the correct, and only possible, explanation, not a double drain.
            if (plan.Slots.All(slot => MatchesSlotBalance(closetGuid, slot.SlotIndex, slot.PostBalance)))
            {
                if (!MarkConsumptionApplied(effect, plan.Slots.Sum(slot => slot.PostBalance)))
                    return Release1TheEnvelopeDepositStatus.Rejected;
            }
            else
            {
                var reconcilable = plan.Slots.All(slot =>
                    MatchesSlotBalance(closetGuid, slot.SlotIndex, slot.PreBalance) ||
                    MatchesSlotBalance(closetGuid, slot.SlotIndex, slot.PostBalance));
                if (!reconcilable) return BlockConsumption(effect);

                // A stage that lapsed its deadline on this very pass (Update runs the deadline check
                // before Converge) still leaves this attempt's Prepared effect sitting here unblocked;
                // consuming it now would take cash for a stage that just failed. Hold instead: the
                // effect stays Prepared, untouched, exactly as decision 7 requires no cash to have
                // moved.
                if (mission.State is not (Release1MissionState.Active or Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive))
                {
                    ReportHoldOnce(assignment.Attempt, "the stage is no longer active for this attempt; the deposit will not consume the closet's cash.");
                    return Release1TheEnvelopeDepositStatus.Held;
                }

                var consumeResult = ConsumeEnvelope(plan, effect);
                if (consumeResult is { } status) return status;
            }
        }

        return FinishDeposit(assignment, effect, authorization);
    }

    // The captured revision gates the post-save commit exactly as Wrong Address's FinishDelivery
    // does for its two effects; The Envelope has only one effect to commit here, since there is no
    // reward effect to also settle in the same pass.
    private Release1TheEnvelopeDepositStatus FinishDeposit(
        Release1TheEnvelopeAssignment assignment,
        Release1NativeEffectJournalEntry effect,
        string authorization)
    {
        var capturedRevision = _story.State!.Revision;
        var applied = _story.State!.NativeEffects.Single(candidate => candidate.EffectId == effect.EffectId);
        if (applied.Phase == Release1NativeEffectPhase.Prepared) return Release1TheEnvelopeDepositStatus.Ambiguous;

        if (!TryComplete(applied, assignment)) return Release1TheEnvelopeDepositStatus.Rejected;

        if (applied.Phase == Release1NativeEffectPhase.Applied && capturedRevision <= _story.LastPersistedRevision)
        {
            var commitResult = _story.TryCommitNativeEffect(applied.EffectId, authorization, capturedRevision);
            if (!commitResult.Accepted)
            {
                ReportHoldOnce(assignment.Attempt, $"the consumption commit after a save was rejected: {commitResult.Message}");
                return Release1TheEnvelopeDepositStatus.Rejected;
            }
            return Release1TheEnvelopeDepositStatus.Committed;
        }

        if (applied.Phase == Release1NativeEffectPhase.Committed) return Release1TheEnvelopeDepositStatus.NoWork;

        return Release1TheEnvelopeDepositStatus.AwaitingAppliedSave;
    }

    /// <summary>
    /// Completes the mission directly once the consumption effect is Applied. No reward is prepared:
    /// <c>RewardAuthorizationReceiptId</c> is supplied only because <c>CompleteMission</c> requires a
    /// non-empty completion correlation, and this mission is the final one, so its own completion
    /// supplies the distinct <c>RecognitionReceiptId</c> that fires <c>Release1Recognized</c>.
    /// </summary>
    private bool TryComplete(Release1NativeEffectJournalEntry consumption, Release1TheEnvelopeAssignment assignment)
    {
        var state = _story.State;
        if (state is null) return false;
        var mission = Mission(state);
        if (mission.State == Release1MissionState.Satisfied) return true;
        if (mission.State is not (Release1MissionState.Active or Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive) ||
            consumption.Phase != Release1NativeEffectPhase.Applied ||
            consumption.ExecutionBlocked ||
            consumption.Attempt != mission.Attempt)
            return false;

        if (!_story.TryGetActiveContext(out var context, out _)) return false;
        var completionReceipt = $"the-envelope-complete-v1-a{mission.Attempt}";
        var completionCorrelation = Release1LogicalCorrelation.Create(
            context.PlayerId, Release1MissionCatalog.TheEnvelope, mission.Attempt,
            Release1TransitionKind.MissionCompleted, completionReceipt).Value;
        var recognitionReceipt = $"the-envelope-recognition-v1-a{mission.Attempt}";
        var command = new Release1StoryCommand(
            context.SessionEpoch,
            context.LoadEpoch,
            context.PlayerId,
            Release1MissionCatalog.TheEnvelope,
            mission.Attempt,
            Release1TransitionKind.MissionCompleted,
            completionReceipt,
            completionCorrelation,
            CompletionTiming: mission.State == Release1MissionState.Active
                ? Release1CompletionTiming.OnTime
                : Release1CompletionTiming.Late,
            RewardAuthorizationReceiptId: $"the-envelope-reward-auth-v1-a{mission.Attempt}",
            RecognitionReceiptId: recognitionReceipt);
        var completionResult = _story.TryExecute(command);
        if (!completionResult.Accepted)
        {
            ReportHoldOnce(mission.Attempt, $"completing the mission after the deposit was consumed was rejected: {completionResult.Message}");
            return false;
        }
        return true;
    }

    private void TryQueueArthur()
    {
        if (_phone is null || _disposed || !_loadActive || _saving) return;
        var state = _story.State;
        if (state is null) return;
        if (!_story.TryGetActiveContext(out var context, out _)) return;
        var mission = Mission(state);

        if (mission.State == Release1MissionState.Satisfied)
        {
            TryQueueArthurOnce(context, mission, "te-recognition-v1", ArthurEndingStages);
            return;
        }
        if (mission.State == Release1MissionState.MakeGoodOffered && mission.LastOutcome == Release1MissionOutcome.RequiredFailure)
            TryQueueArthurOnce(context, mission, "te-warning-v1", ArthurWarningStages);
    }

    private void TryQueueArthurOnce(
        Release1StoryHostContextSnapshot context,
        Release1MissionRecord mission,
        string receipt,
        IReadOnlyList<string> stages)
    {
        try
        {
            var request = Release1PhoneCallRequest.Create(
                context,
                Release1MissionCatalog.TheEnvelope,
                mission.Attempt,
                Release1PhoneCallCorrelation.Create(
                    context.PlayerId, Release1MissionCatalog.TheEnvelope, mission.Attempt,
                    Release1PhoneCallRole.Arthur, receipt),
                Release1PhoneCallRole.Arthur,
                ArthurCallerLabel,
                stages,
                Release1LogicalCorrelation.Create(
                    context.PlayerId, Release1MissionCatalog.TheEnvelope, mission.Attempt,
                    Release1TransitionKind.MissionAccepted, "presentation-nell-te-accepted-v1").Value);
            _phone!.TryQueue(request);
        }
        catch (Exception exception)
        {
            _log($"The Envelope Arthur request was not published: {exception.GetType().Name}");
        }
    }

    private void SetProgress(int attempt, double? observedBalance = null, double? lastShortfallNoticed = null, bool spreadNoticed = false)
    {
        var current = _story.State?.TheEnvelopeProgress.SingleOrDefault(progress => progress.Attempt == attempt)
            ?? Release1TheEnvelopeProgress.Fresh(attempt);
        var next = current with
        {
            ObservedBalance = observedBalance ?? current.ObservedBalance,
            LastShortfallNoticed = lastShortfallNoticed,
            SpreadNoticed = current.SpreadNoticed || spreadNoticed
        };
        if (next != current) _story.TrySetTheEnvelopeProgress(next);
    }

    private static string ConsumptionEffectId(int attempt) => $"the-envelope-cash-v1-a{attempt}";

    /// <summary>
    /// The assignment mode that a persisted <see cref="Release1TheEnvelopeAssignment"/> must carry
    /// for the given mission state, when one is expected to already exist for the current attempt.
    /// </summary>
    internal static Release1TheEnvelopeAssignmentMode? ExpectedAssignmentMode(Release1MissionState missionState) => missionState switch
    {
        Release1MissionState.Accepted or Release1MissionState.Active => Release1TheEnvelopeAssignmentMode.Primary,
        Release1MissionState.MakeGoodOffered or Release1MissionState.MakeGoodActive => Release1TheEnvelopeAssignmentMode.MakeGood,
        Release1MissionState.RecoveryAvailable or Release1MissionState.RecoveryActive => Release1TheEnvelopeAssignmentMode.Recovery,
        _ => null
    };

    private bool TryGetActiveStage(
        out Release1MissionRecord mission,
        out Release1TheEnvelopeAssignment assignment,
        out string authorization)
    {
        mission = null!;
        assignment = null!;
        authorization = string.Empty;
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted) return false;
        mission = Mission(state);
        var expectedMode = ExpectedAssignmentMode(mission.State);
        if (expectedMode is null) return false;
        var missionAttempt = mission.Attempt;
        assignment = state.TheEnvelopeAssignments.SingleOrDefault(candidate =>
            candidate.Attempt == missionAttempt && candidate.Mode == expectedMode.Value)!;
        if (assignment is null) return false;
        authorization = expectedMode == Release1TheEnvelopeAssignmentMode.Primary
            ? mission.AcceptedLogicalCorrelations.LastOrDefault(value =>
                Release1LogicalCorrelation.TryParse(value, out var correlation) &&
                correlation.TransitionKind == Release1TransitionKind.MissionActivated) ?? string.Empty
            : assignment.AuthorizationCorrelationId;
        return !string.IsNullOrEmpty(authorization);
    }

    // Keyed on the effect having reached Applied, not merely existing: a Prepared effect that has
    // never executed (a lock failure held it per decision 7, or it is still awaiting its first
    // decrement) has moved no cash, so a lapsed deadline must still fail the stage rather than being
    // suppressed forever by an effect that can never itself retry the deadline check.
    //
    // The one exception is a Prepared effect whose every planned slot already reads its post
    // balance: the decrement itself already ran to completion on an earlier pass and only the
    // Applied mark failed to record (see MarkConsumptionApplied's caller in ConsumeEnvelope), so the
    // cash is already gone and there is no cash left for a deadline failure to protect. Re-marking it
    // here, ahead of the deadline decision, lets that mark catch up on this very pass whenever the
    // story now accepts it; if it fails again the effect still reports executed, so the deadline
    // keeps deferring to ContinueDeposit's own retry (run right after, through this same pass's
    // Converge() call at the bottom of Update()) rather than failing a stage whose deposit already
    // landed. This never begins or continues a decrement itself, and so can never move cash for a
    // stage the deadline is about to fail: that stays ContinueDeposit's job, gated on mission.State
    // exactly as before.
    private bool TryReconcileExecutedConsumptionEffect(Release1MissionRecord mission)
    {
        var effect = _story.State?.NativeEffects.FirstOrDefault(candidate => candidate.EffectId == ConsumptionEffectId(mission.Attempt));
        if (effect is null) return false;
        if (effect.Phase != Release1NativeEffectPhase.Prepared) return true;
        if (effect.ExecutionBlocked) return false;

        var assignment = _story.State!.TheEnvelopeAssignments.SingleOrDefault(candidate => candidate.Attempt == mission.Attempt);
        var closetGuid = effect.SourceIdentity ?? string.Empty;
        if (assignment is null ||
            !Release1TheEnvelopeClosetPlan.TryParse(effect.AmountOrCargoIdentity, closetGuid, out var plan) ||
            plan is null ||
            effect.Attempt != mission.Attempt ||
            !string.Equals(effect.EffectKind, "CashTransfer", StringComparison.Ordinal) ||
            !string.Equals(effect.DestinationIdentity, EffectScope, StringComparison.Ordinal) ||
            plan.ConsumedAmount != assignment.AmountWholeDollars ||
            !plan.Slots.All(slot => MatchesSlotBalance(closetGuid, slot.SlotIndex, slot.PostBalance)))
            return false;

        MarkConsumptionApplied(effect, plan.Slots.Sum(slot => slot.PostBalance));
        return true;
    }

    private void ReportHoldOnce(int attempt, string message)
    {
        if (!_reportedHolds.Add($"a{attempt}:{message}")) return;
        _log($"The Envelope attempt {attempt} is holding: {message}");
    }

    // A slot whose unlock fails or throws keeps its exact lock identity in the list, so the next
    // retry (ReconcileDeposit's own opportunistic retry on every pass, or a lifecycle teardown via
    // OnPreLoad) tries it again, exactly as the shipped single slot ReleaseSlotLock did.
    private void ReleaseSlotLocks()
    {
        if (_lockedClosetGuid is null) return;
        foreach (var slotIndex in _lockedSlotIndexes.ToArray())
        {
            var released = false;
            try { released = _world.TrySetHoldRoomSlotLocked(_lockedClosetGuid, slotIndex, false) == Release1SmallCourtesyWorldMutationStatus.Succeeded; }
            catch { released = false; }
            if (released) _lockedSlotIndexes.Remove(slotIndex);
        }
        if (_lockedSlotIndexes.Count == 0) _lockedClosetGuid = null;
    }

    // Closets have no close event to subscribe to, so The Envelope reconciles purely on the existing
    // Update() polling loop, the same way Release1RoomWithNoNameMissionService already does for its
    // own closet reads.
    private Release1TheEnvelopeDepositStatus Converge()
    {
        var depositStatus = ReconcileDeposit();
        TryQueueArthur();
        return depositStatus;
    }

    /// <summary>
    /// The once per game minute gate on the per frame convergence pass. A clock that cannot be
    /// read fails open, so the pass behaves exactly as it did before the gate existed whenever
    /// canonical game time is unavailable.
    /// </summary>
    private bool TryBeginConvergencePass()
    {
        var revision = _story.State?.Revision ?? -1L;
        if (!TryReadGameHours(out var gameHours))
        {
            _lastConvergenceGameMinute = null;
            _lastConvergenceRevision = revision;
            return true;
        }
        var minute = Math.Floor(gameHours * 60d);
        if (_lastConvergenceGameMinute is not null &&
            _lastConvergenceGameMinute.Value == minute &&
            _lastConvergenceRevision == revision)
            return false;
        _lastConvergenceGameMinute = minute;
        _lastConvergenceRevision = revision;
        return true;
    }
}
