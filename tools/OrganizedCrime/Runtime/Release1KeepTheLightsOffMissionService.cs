using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Orchestrates Keep the Lights Off offer, terms, acceptance, defer, re-offer, the 72 hour deadline,
/// the shutdown window, and completion, all through the existing OC-10 story runtime and the
/// existing injected vanilla-world boundary. Unlike Short Notice and Room With No Name, this mission
/// never selects a location and never touches a product: the only thing frozen at acceptance is how
/// many properties the player owned at that moment
/// (<see cref="Release1KeepTheLightsOffAssignment"/>), so there is no dead-drop subscription and no
/// slot lock. Nell asks the player to go quiet across every owned property for one full in-game day;
/// completion pays nothing and leaves no native effect. Task 5 still owes the presentation layer's
/// shutdown copy.
/// </summary>
public sealed class Release1KeepTheLightsOffMissionService : IDisposable
{
    private const string TermsVersion = "keep-the-lights-off-v1";
    private const double TimedStageDurationHours = 72d;
    private const double BreachGraceGameMinutes = 60d;
    private const string ArthurCallerLabel = "Arthur Selby";

    private readonly Release1StoryRuntimeService _story;
    private readonly IRelease1SmallCourtesyWorld _world;
    private readonly Release1PhoneCallService? _phone;
    private readonly Action<string> _log;
    private bool _loadActive;
    private bool _saving;
    private bool _dismissed;
    private bool _disposed;
    private Release1KeepTheLightsOffQuote? _reviewedQuote;
    private readonly Release1ConvergenceThrottle _convergence = new Release1ConvergenceThrottle();
    private readonly HashSet<string> _reportedHolds = new(StringComparer.Ordinal);
    private IReadOnlyList<Release1ProductionStationFingerprint> _previousStations =
        Array.Empty<Release1ProductionStationFingerprint>();
    private bool _baselinePassPending = true;

    public Release1KeepTheLightsOffMissionService(
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

    public Release1KeepTheLightsOffOfferStatus OfferStatus { get; private set; } = Release1KeepTheLightsOffOfferStatus.Inactive;
    public Release1KeepTheLightsOffQuote? ReviewedQuote => _reviewedQuote;

    /// <summary>
    /// The timing seam the census receipt reports through. Assigned by the mod shell after
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
        _convergence.Clear();
        _baselinePassPending = true;
        _reportedHolds.Clear();
        RefreshOfferStatus();
        ReconcileCensus();
        TryQueueArthur();
    }

    public void OnPreLoad()
    {
        if (_disposed) return;
        _loadActive = false;
        _saving = false;
        _dismissed = false;
        _reviewedQuote = null;
        _convergence.Clear();
        _baselinePassPending = true;
        _previousStations = Array.Empty<Release1ProductionStationFingerprint>();
        _reportedHolds.Clear();
        OfferStatus = Release1KeepTheLightsOffOfferStatus.Inactive;
    }

    public void OnSaveStart()
    {
        if (_disposed || !_loadActive) return;
        _saving = true;
        _convergence.Clear();
        _baselinePassPending = true;
        OfferStatus = Release1KeepTheLightsOffOfferStatus.Saving;
    }

    public void OnSaveComplete()
    {
        if (_disposed || !_loadActive) return;
        _saving = false;
        _convergence.Clear();
        _baselinePassPending = true;
        RefreshOfferStatus();
        ReconcileCensus();
        TryQueueArthur();
    }

    /// <summary>
    /// The per frame convergence pass. Everything past the throttle gate reads the world: the
    /// census alone resolves every owned property's employees and production stations through
    /// IL2CPP, which is far too expensive to repeat at frame rate. The mission's whole decision
    /// table is expressed in game minutes (the shutdown window, its breach grace, and the deadline
    /// in game hours), so one pass per game minute observes every state the table can distinguish;
    /// a second pass inside the same game minute can only reach the identical decision. Every
    /// lifecycle boundary clears the stamp, so a save start, a save complete, a pre load, a load
    /// complete, and any accepted player decision always converge on their own pass.
    /// </summary>
    public void Update()
    {
        if (_disposed || !_loadActive || _saving) return;
        if (!TryBeginConvergencePass()) return;
        var contextStatus = ReadMatchingContext(out _);
        if (contextStatus != Release1KeepTheLightsOffReviewStatus.Ready)
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
            gameHours >= mission.DeadlineGameTimeHours.Value)
        {
            var transition = mission.State == Release1MissionState.Active
                ? Release1TransitionKind.RequiredFailure
                : Release1TransitionKind.MakeGoodFailed;
            var receipt = $"keep-the-lights-off-deadline-{DeadlineToken(mission.State)}-v1-a{mission.Attempt}";
            _story.TryExecuteDurably(CreateCommand(transition, mission.Attempt, receipt, null, null, null));
            RefreshOfferStatus();
        }

        // Decision 10: the offer is re evaluated every pass, not only at a lifecycle boundary. The
        // three mission states TryBuildQuoteCore can turn into a quote are exactly the ones where an
        // owner action the world reflects immediately (assigning or unassigning an employee at an
        // owned property) can flip eligibility with nobody having called TryReview or TryAccept since.
        if (mission.State is (Release1MissionState.Offered or Release1MissionState.MakeGoodOffered or
                Release1MissionState.RecoveryAvailable))
        {
            RefreshOfferStatus();
        }

        Timing.Measure("census/keep-the-lights-off", () => ReconcileCensus());
        TryQueueArthur();
    }

    /// <summary>
    /// The once per game minute gate on the per frame convergence pass. A clock that cannot be
    /// read fails open, so the pass behaves exactly as it did before the gate existed whenever
    /// canonical game time is unavailable.
    /// </summary>
    private bool TryBeginConvergencePass()
    {
        var revision = _story.State?.Revision ?? -1L;
        return _convergence.TryBegin(TryReadGameMinutes(out var minutes) ? minutes : null, revision);
    }

    public Release1KeepTheLightsOffReviewResult TryReview()
    {
        if (_disposed) return Review(Release1KeepTheLightsOffReviewStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive) return Review(Release1KeepTheLightsOffReviewStatus.Inactive, "No load epoch is active.");
        if (_saving) return Review(Release1KeepTheLightsOffReviewStatus.Saving, "Mission review is deferred during saving.");
        if (_dismissed) return Review(Release1KeepTheLightsOffReviewStatus.Dismissed, "Offer was dismissed for this load.");

        var status = TryBuildQuote(out var quote);
        if (status != Release1KeepTheLightsOffReviewStatus.Ready)
        {
            _reviewedQuote = null;
            OfferStatus = Map(status);
            return Review(status, Message(status));
        }

        _reviewedQuote = quote;
        OfferStatus = Release1KeepTheLightsOffOfferStatus.Available;
        return new(Release1KeepTheLightsOffReviewStatus.Ready, quote, "Keep the Lights Off terms are ready for review.");
    }

    public Release1KeepTheLightsOffDecisionResult TryAccept()
    {
        if (_disposed) return Decision(Release1KeepTheLightsOffDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive || _dismissed) return Decision(Release1KeepTheLightsOffDecisionStatus.Inactive, "No active offer is available.");
        if (_saving) return Decision(Release1KeepTheLightsOffDecisionStatus.PersistenceDeferred, "Acceptance is deferred during saving.");
        if (_reviewedQuote is null) return Decision(Release1KeepTheLightsOffDecisionStatus.ReviewRequired, "Review the current terms before accepting.");

        var status = TryBuildQuote(out var currentQuote);
        if (status != Release1KeepTheLightsOffReviewStatus.Ready || currentQuote != _reviewedQuote)
        {
            _reviewedQuote = null;
            RefreshOfferStatus();
            return Decision(Release1KeepTheLightsOffDecisionStatus.QuoteChanged, "The assignment terms changed; review again.");
        }
        if (!TryReadGameHours(out var acceptedHours))
            return Decision(Release1KeepTheLightsOffDecisionStatus.Rejected, "Canonical game time was unavailable.");

        var assignment = currentQuote!.Assignment;
        var transition = assignment.Mode switch
        {
            Release1KeepTheLightsOffAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1KeepTheLightsOffAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1KeepTheLightsOffAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new InvalidOperationException("Assignment mode was not defined.")
        };
        var command = CreateCommand(
            transition,
            assignment.Attempt,
            AcceptanceReceipt(assignment.Mode, assignment.Attempt),
            assignment.Mode == Release1KeepTheLightsOffAssignmentMode.Primary ? TermsVersion : null,
            acceptedHours,
            assignment.Mode == Release1KeepTheLightsOffAssignmentMode.Recovery ? null : acceptedHours + TimedStageDurationHours);
        var result = _story.TryExecuteKeepTheLightsOffAcceptanceDurably(command, assignment);
        if (result.Status == Release1StoryCommandStatus.DeferredSaving)
            return Decision(Release1KeepTheLightsOffDecisionStatus.PersistenceDeferred, result.Message);
        if (!result.Accepted)
            return Decision(Release1KeepTheLightsOffDecisionStatus.Rejected, result.Message);

        _reviewedQuote = null;
        _convergence.Clear();
        _baselinePassPending = true;
        RefreshOfferStatus();
        ReconcileCensus();
        TryQueueArthur();
        return Decision(Release1KeepTheLightsOffDecisionStatus.Accepted, "Keep the Lights Off stage was accepted.");
    }

    public Release1KeepTheLightsOffDecisionResult TryDefer()
    {
        if (_disposed) return Decision(Release1KeepTheLightsOffDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive || _saving || ReadMatchingContext(out _) != Release1KeepTheLightsOffReviewStatus.Ready)
            return Decision(Release1KeepTheLightsOffDecisionStatus.Inactive, "No active offer is available.");
        var state = _story.State;
        var mission = state is null ? null : Mission(state);
        if (state?.RelationshipState != Release1RelationshipState.Accepted || mission?.State != Release1MissionState.Offered)
            return TryDismiss();

        var receipt = $"keep-the-lights-off-defer-v1-a{mission.Attempt}";
        var result = _story.TryExecuteDurably(CreateCommand(
            Release1TransitionKind.MissionDeferred, mission.Attempt, receipt, null, null, null));
        if (result.Status == Release1StoryCommandStatus.DeferredSaving)
            return Decision(Release1KeepTheLightsOffDecisionStatus.PersistenceDeferred, result.Message);
        if (!result.Accepted)
            return Decision(Release1KeepTheLightsOffDecisionStatus.Rejected, result.Message);

        DismissForLoad();
        return Decision(Release1KeepTheLightsOffDecisionStatus.Deferred, "Keep the Lights Off was deferred until a later load.");
    }

    public Release1KeepTheLightsOffDecisionResult TryDismiss()
    {
        if (_disposed) return Decision(Release1KeepTheLightsOffDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive) return Decision(Release1KeepTheLightsOffDecisionStatus.Inactive, "No load epoch is active.");
        DismissForLoad();
        return Decision(Release1KeepTheLightsOffDecisionStatus.Dismissed, "Keep the Lights Off was dismissed for this load.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        OnPreLoad();
        _disposed = true;
        OfferStatus = Release1KeepTheLightsOffOfferStatus.Disposed;
    }

    private Release1KeepTheLightsOffReviewStatus TryBuildQuote(out Release1KeepTheLightsOffQuote? quote)
    {
        try { return TryBuildQuoteCore(out quote); }
        catch
        {
            quote = null;
            return Release1KeepTheLightsOffReviewStatus.Unavailable;
        }
    }

    private Release1KeepTheLightsOffReviewStatus TryBuildQuoteCore(out Release1KeepTheLightsOffQuote? quote)
    {
        quote = null;
        var contextStatus = ReadMatchingContext(out var context);
        if (contextStatus != Release1KeepTheLightsOffReviewStatus.Ready) return contextStatus;
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted)
            return Release1KeepTheLightsOffReviewStatus.Ineligible;
        var shortNotice = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)];
        if (shortNotice.State != Release1MissionState.Satisfied)
            return Release1KeepTheLightsOffReviewStatus.Ineligible;

        var mission = Mission(state);
        var mode = mission.State switch
        {
            Release1MissionState.Offered => Release1KeepTheLightsOffAssignmentMode.Primary,
            Release1MissionState.MakeGoodOffered => Release1KeepTheLightsOffAssignmentMode.MakeGood,
            Release1MissionState.RecoveryAvailable => Release1KeepTheLightsOffAssignmentMode.Recovery,
            _ => (Release1KeepTheLightsOffAssignmentMode?)null
        };
        if (mode is null) return Release1KeepTheLightsOffReviewStatus.Ineligible;

        var attempt = mode == Release1KeepTheLightsOffAssignmentMode.Primary ? mission.Attempt : mission.Attempt + 1;
        var transition = mode switch
        {
            Release1KeepTheLightsOffAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1KeepTheLightsOffAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1KeepTheLightsOffAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new InvalidOperationException("Assignment mode was not defined.")
        };
        var correlation = Release1LogicalCorrelation.Create(
            context.PlayerId, Release1MissionCatalog.KeepTheLightsOff, attempt, transition,
            AcceptanceReceipt(mode.Value, attempt)).Value;

        var activityStatus = _world.TryReadProductionActivity(out var activity);
        if (activityStatus != Release1SmallCourtesyWorldReadStatus.Ready) return Map(activityStatus);
        if (!Release1KeepTheLightsOffAssignmentSelector.TrySelect(
                activity, mode.Value, attempt, correlation, out var assignment, out _))
            return Release1KeepTheLightsOffReviewStatus.Ineligible;

        quote = new(assignment!, mode == Release1KeepTheLightsOffAssignmentMode.Recovery ? null : TimedStageDurationHours);
        quote.Validate();
        return Release1KeepTheLightsOffReviewStatus.Ready;
    }

    private void RefreshOfferStatus()
    {
        if (_disposed) { OfferStatus = Release1KeepTheLightsOffOfferStatus.Disposed; return; }
        if (!_loadActive) { OfferStatus = Release1KeepTheLightsOffOfferStatus.Inactive; return; }
        if (_saving) { OfferStatus = Release1KeepTheLightsOffOfferStatus.Saving; return; }
        if (_dismissed) { OfferStatus = Release1KeepTheLightsOffOfferStatus.Dismissed; return; }
        OfferStatus = Map(TryBuildQuote(out _));
    }

    private Release1KeepTheLightsOffReviewStatus ReadMatchingContext(out Release1StoryHostContextSnapshot context)
    {
        context = default;
        if (!_story.TryGetActiveContext(out var storyContext, out _)) return Release1KeepTheLightsOffReviewStatus.Inactive;
        try
        {
            var status = _world.TryReadContext(out var worldContext);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready) return Map(status);
            if (!SameContext(storyContext, worldContext)) return Release1KeepTheLightsOffReviewStatus.Inactive;
            context = storyContext;
            return Release1KeepTheLightsOffReviewStatus.Ready;
        }
        catch
        {
            return Release1KeepTheLightsOffReviewStatus.Unavailable;
        }
    }

    private bool TryReadGameHours(out double gameHours)
    {
        gameHours = 0d;
        if (!TryReadGameMinutes(out var minutes)) return false;
        gameHours = minutes / 60d;
        return double.IsFinite(gameHours);
    }

    private bool TryReadGameMinutes(out double totalMinutes)
    {
        totalMinutes = 0d;
        try
        {
            if (_world.TryReadCanonicalTotalMinutes(out var minutes) != Release1SmallCourtesyWorldReadStatus.Ready ||
                !double.IsFinite(minutes) || minutes < 0)
                return false;
            totalMinutes = minutes;
            return true;
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
            Release1MissionCatalog.KeepTheLightsOff,
            attempt,
            transition,
            receipt,
            Release1LogicalCorrelation.Create(context.PlayerId, Release1MissionCatalog.KeepTheLightsOff, attempt, transition, receipt).Value,
            termsVersion,
            acceptedHours,
            deadlineHours);
    }

    private void DismissForLoad()
    {
        _dismissed = true;
        _reviewedQuote = null;
        OfferStatus = Release1KeepTheLightsOffOfferStatus.Dismissed;
    }

    private static Release1MissionRecord Mission(Release1StoryState state) =>
        state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];

    private static string AcceptanceReceipt(Release1KeepTheLightsOffAssignmentMode mode, int attempt) =>
        $"keep-the-lights-off-{mode.ToString().ToLowerInvariant()}-accept-v1-a{attempt}";

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

    private static Release1KeepTheLightsOffReviewResult Review(Release1KeepTheLightsOffReviewStatus status, string message) =>
        new(status, null, message);

    private static Release1KeepTheLightsOffDecisionResult Decision(Release1KeepTheLightsOffDecisionStatus status, string message) =>
        new(status, message);

    private static Release1KeepTheLightsOffReviewStatus Map(Release1SmallCourtesyWorldReadStatus status) => status switch
    {
        Release1SmallCourtesyWorldReadStatus.Pending or Release1SmallCourtesyWorldReadStatus.NotAuthoritative => Release1KeepTheLightsOffReviewStatus.Pending,
        _ => Release1KeepTheLightsOffReviewStatus.Unavailable
    };

    private static Release1KeepTheLightsOffOfferStatus Map(Release1KeepTheLightsOffReviewStatus status) => status switch
    {
        Release1KeepTheLightsOffReviewStatus.Ready => Release1KeepTheLightsOffOfferStatus.Available,
        Release1KeepTheLightsOffReviewStatus.Inactive => Release1KeepTheLightsOffOfferStatus.Inactive,
        Release1KeepTheLightsOffReviewStatus.Ineligible => Release1KeepTheLightsOffOfferStatus.Ineligible,
        Release1KeepTheLightsOffReviewStatus.Pending => Release1KeepTheLightsOffOfferStatus.Pending,
        Release1KeepTheLightsOffReviewStatus.Saving => Release1KeepTheLightsOffOfferStatus.Saving,
        Release1KeepTheLightsOffReviewStatus.Dismissed => Release1KeepTheLightsOffOfferStatus.Dismissed,
        Release1KeepTheLightsOffReviewStatus.Disposed => Release1KeepTheLightsOffOfferStatus.Disposed,
        _ => Release1KeepTheLightsOffOfferStatus.Unavailable
    };

    private static string Message(Release1KeepTheLightsOffReviewStatus status) => status switch
    {
        Release1KeepTheLightsOffReviewStatus.Pending => "World eligibility is still pending.",
        Release1KeepTheLightsOffReviewStatus.Ineligible => "Keep the Lights Off is not currently offerable.",
        Release1KeepTheLightsOffReviewStatus.Inactive => "The canonical story context is inactive.",
        _ => "Keep the Lights Off world data is unavailable."
    };

    /// <summary>
    /// The shutdown census, taken every convergence pass: there is no callback to wait on, since
    /// nothing is ever chosen at any property, employee, or station for this mission. Reads the
    /// player's own production activity, classifies it as NotReady, Quiet, or Working through
    /// <see cref="Release1ProductionActivityClassifier"/>, maps that into the engine's own three
    /// valued vocabulary, and hands it to the shared <see cref="Release1ConditionWindow"/> decision
    /// table alongside the attempt's progress. Nothing here ever writes to a property, an employee,
    /// or a station.
    /// </summary>
    public Release1KeepTheLightsOffCensusStatus ReconcileCensus()
    {
        if (_disposed || !_loadActive || _saving || ReadMatchingContext(out _) != Release1KeepTheLightsOffReviewStatus.Ready)
            return Release1KeepTheLightsOffCensusStatus.NoWork;

        TryRestoreMigratedAssignment();
        if (!TryGetActiveStage(out var mission, out var assignment, out _))
            return Release1KeepTheLightsOffCensusStatus.NoWork;
        if (!TryReadGameMinutes(out var now)) return Release1KeepTheLightsOffCensusStatus.NoWork;

        var baseline = _baselinePassPending;
        var observation = ReadObservation(assignment, baseline);
        // The baseline exemption is only spent once it actually gets a Ready read: a NotReady pass
        // right after a lifecycle boundary (the world has not settled yet) must not burn the flag,
        // or the next, genuinely first, Ready pass would read every already-running station as new
        // and record a spurious breach across a reload (decision 8).
        if (observation.Observation != Release1ProductionObservation.NotReady)
            _baselinePassPending = false;
        _previousStations = observation.Stations;

        var progress = Progress(assignment.Attempt) ?? Release1KeepTheLightsOffProgress.Fresh(assignment.Attempt);
        var decision = Release1ConditionWindow.Decide(
            Release1ProductionActivityClassifier.ToWindowObservation(observation.Observation),
            // Satisfied is constant false: this mission has no satisfaction latch and is gated out
            // instead by its own mission state, which TryGetActiveStage checks above.
            new Release1WindowProgress(progress.ClearConfirmedAtGameMinutes, progress.BreachSincePassGameMinutes, false),
            now,
            new Release1WindowProfile(
                assignment.WindowDurationGameMinutes, BreachGraceGameMinutes, Release1WindowHealPolicy.HealBeforeElapse));

        switch (decision)
        {
            case Release1WindowDecision.OpenWindow:
                return SetProgress(assignment.Attempt, clearConfirmedAtGameMinutes: now, clearBreach: true)
                    ? Release1KeepTheLightsOffCensusStatus.WindowStarted
                    : Release1KeepTheLightsOffCensusStatus.Rejected;
            case Release1WindowDecision.ClearBreach:
                return SetProgress(assignment.Attempt, clearBreach: true)
                    ? Release1KeepTheLightsOffCensusStatus.Holding
                    : Release1KeepTheLightsOffCensusStatus.Rejected;
            case Release1WindowDecision.RecordBreach:
                return SetProgress(assignment.Attempt, breachSincePassGameMinutes: now)
                    ? Release1KeepTheLightsOffCensusStatus.BreachRecorded
                    : Release1KeepTheLightsOffCensusStatus.Rejected;
            case Release1WindowDecision.Fail:
                return FailWindow(mission, assignment.Attempt);
            case Release1WindowDecision.Elapsed:
                return RunCompletion(mission);
            case Release1WindowDecision.Hold:
                if (observation.Observation == Release1ProductionObservation.NotReady)
                {
                    ReportHoldOnce(assignment.Attempt, observation.Reason switch
                    {
                        Release1ProductionHoldReason.OwnedPropertyCountMismatch =>
                            "the owned property count no longer matches the count frozen at acceptance; the window is unchanged.",
                        _ => "production activity was not fully readable this pass; the window is unchanged."
                    });
                    return Release1KeepTheLightsOffCensusStatus.NotReady;
                }
                return Release1KeepTheLightsOffCensusStatus.Holding;
            default:
                return Release1KeepTheLightsOffCensusStatus.Holding;
        }
    }

    private Release1ProductionObservationResult ReadObservation(
        Release1KeepTheLightsOffAssignment assignment,
        bool isBaselinePass)
    {
        var status = Release1SmallCourtesyWorldReadStatus.Faulted;
        var snapshot = Release1ProductionActivitySnapshot.Empty;
        try { status = _world.TryReadProductionActivity(out snapshot); }
        catch { status = Release1SmallCourtesyWorldReadStatus.Faulted; snapshot = Release1ProductionActivitySnapshot.Empty; }
        try
        {
            return Release1ProductionActivityClassifier.Classify(
                snapshot, status, assignment.ExpectedOwnedPropertyCount, _previousStations, isBaselinePass);
        }
        catch (ArgumentException)
        {
            return new(Release1ProductionObservation.NotReady, Release1ProductionHoldReason.WorldNotReady, _previousStations);
        }
    }

    /// <summary>
    /// Re freezes the assignment for an attempt whose acceptance is on record but whose assignment the
    /// v9 to v10 migration could not translate. No transition, no Standing change, no progress: the
    /// shutdown window simply restarts from zero, which is the honest outcome for a condition that no
    /// longer exists in the form the old save described.
    /// </summary>
    private void TryRestoreMigratedAssignment()
    {
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted) return;
        var mission = Mission(state);
        var mode = ExpectedAssignmentMode(mission.State);
        if (mode is null) return;
        var missionAttempt = mission.Attempt;
        if (state.KeepTheLightsOffAssignments.Any(candidate => candidate.Attempt == missionAttempt)) return;

        var expected = mode.Value switch
        {
            Release1KeepTheLightsOffAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1KeepTheLightsOffAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            _ => Release1TransitionKind.RecoveryAccepted
        };
        var authorization = mission.AcceptedLogicalCorrelations.LastOrDefault(value =>
            Release1LogicalCorrelation.TryParse(value, out var parsed) &&
            parsed.Attempt == missionAttempt && parsed.TransitionKind == expected);
        if (string.IsNullOrEmpty(authorization)) return;
        if (_world.TryReadProductionActivity(out var activity) != Release1SmallCourtesyWorldReadStatus.Ready) return;
        // decision 10's assigned-employee eligibility precondition gates new offers only; an existing
        // attempt's acceptance is already on record, so restoring it here must not be blocked by the
        // current employee census (see the selector's own doc comment).
        if (!Release1KeepTheLightsOffAssignmentSelector.TrySelect(
                activity, mode.Value, missionAttempt, authorization, out var restored, out _,
                requireAssignedEmployee: false))
            return;

        var result = _story.TryRestoreKeepTheLightsOffAssignment(restored!);
        if (!result.Accepted && result.Status != Release1StoryCommandStatus.NoOp)
            ReportHoldOnce(missionAttempt, $"restoring the migrated assignment was rejected: {result.Message}");
    }

    /// <summary>
    /// Records a window breach through the same validated transitions a lapsed deadline uses. In
    /// recovery the transition is RecoveryFailed, which returns the mission to RecoveryAvailable and
    /// advances the attempt, so the next accepted stage starts its window exactly once.
    /// </summary>
    private Release1KeepTheLightsOffCensusStatus FailWindow(Release1MissionRecord mission, int attempt)
    {
        var (transition, token) = mission.State switch
        {
            Release1MissionState.Active => (Release1TransitionKind.RequiredFailure, "primary"),
            Release1MissionState.MakeGoodActive => (Release1TransitionKind.MakeGoodFailed, "make-good"),
            Release1MissionState.RecoveryActive => (Release1TransitionKind.RecoveryFailed, "recovery"),
            _ => (Release1TransitionKind.RequiredFailure, string.Empty)
        };
        if (token.Length == 0) return Release1KeepTheLightsOffCensusStatus.NoWork;

        var receipt = $"keep-the-lights-off-breach-{token}-v1-a{attempt}";
        var result = _story.TryExecuteDurably(CreateCommand(transition, attempt, receipt, null, null, null));
        if (result.Status == Release1StoryCommandStatus.DeferredSaving) return Release1KeepTheLightsOffCensusStatus.NoWork;
        if (!result.Accepted)
        {
            ReportHoldOnce(attempt, $"recording the breach failure was rejected: {result.Message}");
            return Release1KeepTheLightsOffCensusStatus.Rejected;
        }
        RefreshOfferStatus();
        return Release1KeepTheLightsOffCensusStatus.Failed;
    }

    private bool SetProgress(
        int attempt,
        double? clearConfirmedAtGameMinutes = null,
        double? breachSincePassGameMinutes = null,
        bool clearBreach = false)
    {
        var existing = Progress(attempt) ?? Release1KeepTheLightsOffProgress.Fresh(attempt);
        var next = existing with
        {
            ClearConfirmedAtGameMinutes = clearConfirmedAtGameMinutes ?? existing.ClearConfirmedAtGameMinutes,
            BreachSincePassGameMinutes = clearBreach ? null : breachSincePassGameMinutes ?? existing.BreachSincePassGameMinutes
        };
        if (next == existing) return true;
        var result = _story.TrySetKeepTheLightsOffProgress(next);
        if (!result.Accepted && result.Status != Release1StoryCommandStatus.NoOp)
        {
            ReportHoldOnce(attempt, $"recording the census progress was rejected: {result.Message}");
            return false;
        }
        return true;
    }

    private Release1KeepTheLightsOffProgress? Progress(int attempt) =>
        _story.State?.KeepTheLightsOffProgress.SingleOrDefault(progress => progress.Attempt == attempt);

    private void ReportHoldOnce(int attempt, string message)
    {
        if (!_reportedHolds.Add($"a{attempt}:{message}")) return;
        _log($"Keep the Lights Off attempt {attempt} is holding: {message}");
    }

    /// <summary>
    /// Runs the completion transaction once the shutdown window has fully elapsed. There is no
    /// reward: the offer terms are payment enough on their own (Nell's own words), so completion is a
    /// single durable transition and nothing else, guarded only by the same three state check every
    /// other completion path in this mission already uses.
    /// </summary>
    private Release1KeepTheLightsOffCensusStatus RunCompletion(Release1MissionRecord mission)
    {
        if (mission.State is not (Release1MissionState.Active or Release1MissionState.MakeGoodActive or
            Release1MissionState.RecoveryActive))
            return Release1KeepTheLightsOffCensusStatus.NoWork;
        if (!_story.TryGetActiveContext(out var context, out _)) return Release1KeepTheLightsOffCensusStatus.NoWork;

        var receipt = $"keep-the-lights-off-complete-v1-a{mission.Attempt}";
        var correlation = Release1LogicalCorrelation.Create(
            context.PlayerId, Release1MissionCatalog.KeepTheLightsOff, mission.Attempt,
            Release1TransitionKind.MissionCompleted, receipt).Value;
        var result = _story.TryExecute(new Release1StoryCommand(
            context.SessionEpoch, context.LoadEpoch, context.PlayerId,
            Release1MissionCatalog.KeepTheLightsOff, mission.Attempt,
            Release1TransitionKind.MissionCompleted, receipt, correlation,
            CompletionTiming: mission.State == Release1MissionState.Active
                ? Release1CompletionTiming.OnTime
                : Release1CompletionTiming.Late,
            RewardAuthorizationReceiptId: $"keep-the-lights-off-reward-auth-v1-a{mission.Attempt}"));
        if (!result.Accepted)
        {
            ReportHoldOnce(mission.Attempt, $"completing the mission after the window elapsed was rejected: {result.Message}");
            return Release1KeepTheLightsOffCensusStatus.Rejected;
        }
        RefreshOfferStatus();
        return Release1KeepTheLightsOffCensusStatus.Completed;
    }

    private static readonly string[] ArthurWarningStages =
    {
        "Arthur Selby. It did not stay clear and the day never happened.",
        "Nell has arranged one more run. Clear it again, and hold it for one more day. I'm not going to keep protecting you. Fucking do it right.",
        "Do not make her ask twice."
    };

    /// <summary>
    /// Arthur's warning call on the failure path, mirroring Room With No Name and Short Notice
    /// exactly: queued once per RequiredFailure, never on a clean completion, never on MakeGoodFailed.
    /// </summary>
    private void TryQueueArthur()
    {
        if (_phone is null || _disposed || !_loadActive || _saving) return;
        var state = _story.State;
        if (state is null) return;
        if (!_story.TryGetActiveContext(out var context, out _)) return;
        var mission = Mission(state);
        if (mission.State != Release1MissionState.MakeGoodOffered ||
            mission.LastOutcome != Release1MissionOutcome.RequiredFailure) return;

        try
        {
            var request = Release1PhoneCallRequest.Create(
                context,
                Release1MissionCatalog.KeepTheLightsOff,
                mission.Attempt,
                Release1PhoneCallCorrelation.Create(
                    context.PlayerId, Release1MissionCatalog.KeepTheLightsOff, mission.Attempt,
                    Release1PhoneCallRole.Arthur, "ktlo-warning-v1"),
                Release1PhoneCallRole.Arthur,
                ArthurCallerLabel,
                ArthurWarningStages,
                Release1LogicalCorrelation.Create(
                    context.PlayerId, Release1MissionCatalog.KeepTheLightsOff, mission.Attempt,
                    Release1TransitionKind.MissionAccepted, "presentation-nell-ktlo-accepted-v1").Value);
            _phone.TryQueue(request);
        }
        catch (Exception exception)
        {
            _log($"Keep the Lights Off Arthur request was not published: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// The assignment mode a persisted assignment must carry for the given mission state, when one is
    /// expected to already exist for the current attempt. Mirrors
    /// <see cref="Release1ShortNoticeMissionService.ExpectedAssignmentMode"/> exactly; the two cannot
    /// be one method because the assignment types differ. Exposed for the presenter, which uses it to
    /// find the accepted stage's own assignment regardless of its progress.
    /// </summary>
    internal static Release1KeepTheLightsOffAssignmentMode? ExpectedAssignmentMode(Release1MissionState missionState) => missionState switch
    {
        Release1MissionState.Accepted or Release1MissionState.Active => Release1KeepTheLightsOffAssignmentMode.Primary,
        Release1MissionState.MakeGoodOffered or Release1MissionState.MakeGoodActive => Release1KeepTheLightsOffAssignmentMode.MakeGood,
        Release1MissionState.RecoveryAvailable or Release1MissionState.RecoveryActive => Release1KeepTheLightsOffAssignmentMode.Recovery,
        _ => null
    };

    /// <summary>
    /// Resolves the current accepted stage's mission record, its own frozen assignment, and the
    /// logical correlation that authorized it. Mirrors
    /// <see cref="Release1ShortNoticeMissionService"/>'s own private helper of the same name exactly
    /// (mission key and assignment type substituted). False whenever the story is not accepted, the
    /// mission is not in one of the three active stage states, or no matching assignment is on file.
    /// </summary>
    private bool TryGetActiveStage(
        out Release1MissionRecord mission,
        out Release1KeepTheLightsOffAssignment assignment,
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
        assignment = state.KeepTheLightsOffAssignments.SingleOrDefault(candidate =>
            candidate.Attempt == missionAttempt && candidate.Mode == expectedMode.Value)!;
        if (assignment is null) return false;
        authorization = expectedMode == Release1KeepTheLightsOffAssignmentMode.Primary
            ? mission.AcceptedLogicalCorrelations.LastOrDefault(value =>
                Release1LogicalCorrelation.TryParse(value, out var correlation) &&
                correlation.TransitionKind == Release1TransitionKind.MissionActivated) ?? string.Empty
            : assignment.AuthorizationCorrelationId;
        return !string.IsNullOrEmpty(authorization);
    }
}
