using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class Release1ShortNoticeMissionService : IDisposable
{
    private const string TermsVersion = "short-notice-v1";
    private const double TimedStageDurationHours = 12d;
    private const string EffectScope = "oc-short-notice";
    private const string RewardDestination = "player-cash";
    private const string ArthurCallerLabel = "Arthur Selby";
    private static readonly string[] ArthurWarningStages =
    {
        "The order did not land and the window is closed. If you are trying to pull a fast one, I will fuck you up.",
        "Nell has arranged one more run. Two bricks, twelve hours from the moment you accept it.",
        "Do not make her ask twice. You are pissing me off."
    };

    private readonly Release1StoryRuntimeService _story;
    private readonly IRelease1SmallCourtesyWorld _world;
    private readonly Release1PhoneCallService? _phone;
    private readonly Action<string> _log;
    private bool _loadActive;
    private bool _saving;
    private bool _dismissed;
    private bool _disposed;
    private Release1ShortNoticeQuote? _reviewedQuote;
    private readonly HashSet<string> _reportedHolds = new(StringComparer.Ordinal);
    private IRelease1SmallCourtesyDropSubscription? _dropSubscription;
    private string? _boundDropGuid;
    private string? _lockedDropGuid;
    private int _lockedSlotIndex = -1;

    public Release1ShortNoticeMissionService(
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

    public Release1ShortNoticeOfferStatus OfferStatus { get; private set; } = Release1ShortNoticeOfferStatus.Inactive;
    public Release1ShortNoticeQuote? ReviewedQuote => _reviewedQuote;

    public void OnLoadComplete()
    {
        if (_disposed || _loadActive) return;
        _loadActive = true;
        _saving = false;
        _dismissed = false;
        _reviewedQuote = null;
        _reportedHolds.Clear();
        RefreshOfferStatus();
        Converge();
    }

    public void OnPreLoad()
    {
        if (_disposed) return;
        ReleaseSlotLock();
        DisposeDropSubscription();
        _loadActive = false;
        _saving = false;
        _dismissed = false;
        _reviewedQuote = null;
        _reportedHolds.Clear();
        OfferStatus = Release1ShortNoticeOfferStatus.Inactive;
    }

    public void OnSaveStart()
    {
        if (_disposed || !_loadActive) return;
        _saving = true;
        OfferStatus = Release1ShortNoticeOfferStatus.Saving;
    }

    public void OnSaveComplete()
    {
        if (_disposed || !_loadActive) return;
        _saving = false;
        RefreshOfferStatus();
        Converge();
    }

    /// <summary>
    /// Fails the active stage once the twelve-hour window has lapsed, exactly the way every timed
    /// Release 1 mission does: the same required-failure and make-good-failure transitions, gated on
    /// no consumption effect already existing for the attempt so a lapsed window can never fail a
    /// stage the player already delivered.
    /// </summary>
    public void Update()
    {
        if (_disposed || !_loadActive || _saving) return;
        var contextStatus = ReadMatchingContext(out _);
        if (contextStatus != Release1ShortNoticeReviewStatus.Ready)
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
            !HasConsumptionEffect(mission.Attempt))
        {
            var transition = mission.State == Release1MissionState.Active
                ? Release1TransitionKind.RequiredFailure
                : Release1TransitionKind.MakeGoodFailed;
            var receipt = $"short-notice-deadline-{DeadlineToken(mission.State)}-v1-a{mission.Attempt}";
            _story.TryExecuteDurably(CreateCommand(transition, mission.Attempt, receipt, null, null, null));
            RefreshOfferStatus();
        }

        Converge();
    }

    public Release1ShortNoticeReviewResult TryReview()
    {
        if (_disposed) return Review(Release1ShortNoticeReviewStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive) return Review(Release1ShortNoticeReviewStatus.Inactive, "No load epoch is active.");
        if (_saving) return Review(Release1ShortNoticeReviewStatus.Saving, "Mission review is deferred during saving.");
        if (_dismissed) return Review(Release1ShortNoticeReviewStatus.Dismissed, "Offer was dismissed for this load.");

        var status = TryBuildQuote(out var quote);
        if (status != Release1ShortNoticeReviewStatus.Ready)
        {
            _reviewedQuote = null;
            OfferStatus = Map(status);
            return Review(status, Message(status));
        }

        _reviewedQuote = quote;
        OfferStatus = Release1ShortNoticeOfferStatus.Available;
        return new(Release1ShortNoticeReviewStatus.Ready, quote, "Short Notice terms are ready for review.");
    }

    public Release1ShortNoticeDecisionResult TryAccept()
    {
        if (_disposed) return Decision(Release1ShortNoticeDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive || _dismissed) return Decision(Release1ShortNoticeDecisionStatus.Inactive, "No active offer is available.");
        if (_saving) return Decision(Release1ShortNoticeDecisionStatus.PersistenceDeferred, "Acceptance is deferred during saving.");
        if (_reviewedQuote is null) return Decision(Release1ShortNoticeDecisionStatus.ReviewRequired, "Review the current terms before accepting.");

        var status = TryBuildQuote(out var currentQuote);
        if (status == Release1ShortNoticeReviewStatus.NoEmptyDeadDrop)
        {
            _reviewedQuote = null;
            RefreshOfferStatus();
            return Decision(Release1ShortNoticeDecisionStatus.NoEmptyDeadDrop, Message(status));
        }
        if (status != Release1ShortNoticeReviewStatus.Ready || currentQuote != _reviewedQuote)
        {
            _reviewedQuote = null;
            RefreshOfferStatus();
            return Decision(Release1ShortNoticeDecisionStatus.QuoteChanged, "The available product, package, or drop changed; review again.");
        }
        if (!TryReadGameHours(out var acceptedHours))
            return Decision(Release1ShortNoticeDecisionStatus.Rejected, "Canonical game time was unavailable.");

        var assignment = currentQuote!.Assignment;
        var transition = assignment.Mode switch
        {
            Release1ShortNoticeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1ShortNoticeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1ShortNoticeAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new InvalidOperationException("Assignment mode was not defined.")
        };
        var command = CreateCommand(
            transition,
            assignment.Attempt,
            AcceptanceReceipt(assignment.Mode, assignment.Attempt),
            assignment.Mode == Release1ShortNoticeAssignmentMode.Primary ? TermsVersion : null,
            acceptedHours,
            assignment.Mode == Release1ShortNoticeAssignmentMode.Recovery ? null : acceptedHours + TimedStageDurationHours);
        var result = _story.TryExecuteShortNoticeAcceptanceDurably(command, assignment);
        if (result.Status == Release1StoryCommandStatus.DeferredSaving)
            return Decision(Release1ShortNoticeDecisionStatus.PersistenceDeferred, result.Message);
        if (!result.Accepted)
            return Decision(Release1ShortNoticeDecisionStatus.Rejected, result.Message);

        _reviewedQuote = null;
        RefreshOfferStatus();
        Converge();
        return Decision(Release1ShortNoticeDecisionStatus.Accepted, "Short Notice stage was accepted.");
    }

    public Release1ShortNoticeDecisionResult TryDefer()
    {
        if (_disposed) return Decision(Release1ShortNoticeDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive || _saving || ReadMatchingContext(out _) != Release1ShortNoticeReviewStatus.Ready)
            return Decision(Release1ShortNoticeDecisionStatus.Inactive, "No active offer is available.");
        var state = _story.State;
        var mission = state is null ? null : Mission(state);
        if (state?.RelationshipState != Release1RelationshipState.Accepted || mission?.State != Release1MissionState.Offered)
            return TryDismiss();

        var receipt = $"short-notice-defer-v1-a{mission.Attempt}";
        var result = _story.TryExecuteDurably(CreateCommand(
            Release1TransitionKind.MissionDeferred, mission.Attempt, receipt, null, null, null));
        if (result.Status == Release1StoryCommandStatus.DeferredSaving)
            return Decision(Release1ShortNoticeDecisionStatus.PersistenceDeferred, result.Message);
        if (!result.Accepted)
            return Decision(Release1ShortNoticeDecisionStatus.Rejected, result.Message);

        DismissForLoad();
        return Decision(Release1ShortNoticeDecisionStatus.Deferred, "Short Notice was deferred until a later load.");
    }

    public Release1ShortNoticeDecisionResult TryDismiss()
    {
        if (_disposed) return Decision(Release1ShortNoticeDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive) return Decision(Release1ShortNoticeDecisionStatus.Inactive, "No load epoch is active.");
        DismissForLoad();
        return Decision(Release1ShortNoticeDecisionStatus.Dismissed, "Short Notice was dismissed for this load.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        OnPreLoad();
        _disposed = true;
        OfferStatus = Release1ShortNoticeOfferStatus.Disposed;
    }

    private Release1ShortNoticeReviewStatus TryBuildQuote(out Release1ShortNoticeQuote? quote)
    {
        try { return TryBuildQuoteCore(out quote); }
        catch
        {
            quote = null;
            return Release1ShortNoticeReviewStatus.Unavailable;
        }
    }

    private Release1ShortNoticeReviewStatus TryBuildQuoteCore(out Release1ShortNoticeQuote? quote)
    {
        quote = null;
        var contextStatus = ReadMatchingContext(out var context);
        if (contextStatus != Release1ShortNoticeReviewStatus.Ready) return contextStatus;
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted)
            return Release1ShortNoticeReviewStatus.Ineligible;
        var roomWithNoName = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)];
        if (roomWithNoName.State != Release1MissionState.Satisfied)
            return Release1ShortNoticeReviewStatus.Ineligible;

        var mission = Mission(state);
        var mode = mission.State switch
        {
            Release1MissionState.Offered => Release1ShortNoticeAssignmentMode.Primary,
            Release1MissionState.MakeGoodOffered => Release1ShortNoticeAssignmentMode.MakeGood,
            Release1MissionState.RecoveryAvailable => Release1ShortNoticeAssignmentMode.Recovery,
            _ => (Release1ShortNoticeAssignmentMode?)null
        };
        if (mode is null) return Release1ShortNoticeReviewStatus.Ineligible;

        var attempt = mode == Release1ShortNoticeAssignmentMode.Primary ? mission.Attempt : mission.Attempt + 1;
        var transition = mode switch
        {
            Release1ShortNoticeAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1ShortNoticeAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1ShortNoticeAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new InvalidOperationException("Assignment mode was not defined.")
        };
        var correlation = Release1LogicalCorrelation.Create(
            context.PlayerId,
            Release1MissionCatalog.ShortNotice,
            attempt,
            transition,
            AcceptanceReceipt(mode.Value, attempt)).Value;

        // Short Notice packages every stage as a brick; there is no jar step the way Room With No
        // Name and Wrong Address widen into after a required failure.
        if (_world.TryReadPackaging(Release1SmallCourtesyPackageKind.Brick, out var packaging) != Release1SmallCourtesyWorldReadStatus.Ready)
            return Release1ShortNoticeReviewStatus.Unavailable;
        try { packaging.Validate(); }
        catch (ArgumentException) { return Release1ShortNoticeReviewStatus.Unavailable; }

        IReadOnlyList<Release1SmallCourtesyProductCandidate> products;
        if (mode == Release1ShortNoticeAssignmentMode.Primary)
        {
            var productStatus = _world.TryReadProducts(out products);
            if (productStatus != Release1SmallCourtesyWorldReadStatus.Ready) return Map(productStatus);
        }
        else
        {
            var frozen = state.ShortNoticeAssignments.OrderBy(candidate => candidate.Attempt).FirstOrDefault();
            if (frozen is null) return Release1ShortNoticeReviewStatus.Ineligible;
            products = new[] { new Release1SmallCourtesyProductCandidate(frozen.ProductId, frozen.ProductName, 0d, true) };
        }

        var dropStatus = _world.TryReadDeadDrops(out var drops);
        if (dropStatus != Release1SmallCourtesyWorldReadStatus.Ready) return Map(dropStatus);
        if (!Release1ShortNoticeAssignmentSelector.TrySelect(
                products, drops, mode.Value, attempt, correlation,
                packaging.PackagingId, packaging.PackagingName,
                Release1ShortNoticeValueRule.Observed,
                out var assignment, out var selectionStatus))
            return Map(selectionStatus);

        quote = new(assignment!, mode == Release1ShortNoticeAssignmentMode.Recovery ? null : TimedStageDurationHours);
        quote.Validate();
        return Release1ShortNoticeReviewStatus.Ready;
    }

    private void RefreshOfferStatus()
    {
        if (_disposed) { OfferStatus = Release1ShortNoticeOfferStatus.Disposed; return; }
        if (!_loadActive) { OfferStatus = Release1ShortNoticeOfferStatus.Inactive; return; }
        if (_saving) { OfferStatus = Release1ShortNoticeOfferStatus.Saving; return; }
        if (_dismissed) { OfferStatus = Release1ShortNoticeOfferStatus.Dismissed; return; }
        OfferStatus = Map(TryBuildQuote(out _));
    }

    private Release1ShortNoticeReviewStatus ReadMatchingContext(out Release1StoryHostContextSnapshot context)
    {
        context = default;
        if (!_story.TryGetActiveContext(out var storyContext, out _)) return Release1ShortNoticeReviewStatus.Inactive;
        try
        {
            var status = _world.TryReadContext(out var worldContext);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready) return Map(status);
            if (!SameContext(storyContext, worldContext)) return Release1ShortNoticeReviewStatus.Inactive;
            context = storyContext;
            return Release1ShortNoticeReviewStatus.Ready;
        }
        catch
        {
            return Release1ShortNoticeReviewStatus.Unavailable;
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
            Release1MissionCatalog.ShortNotice,
            attempt,
            transition,
            receipt,
            Release1LogicalCorrelation.Create(context.PlayerId, Release1MissionCatalog.ShortNotice, attempt, transition, receipt).Value,
            termsVersion,
            acceptedHours,
            deadlineHours);
    }

    private void DismissForLoad()
    {
        _dismissed = true;
        _reviewedQuote = null;
        OfferStatus = Release1ShortNoticeOfferStatus.Dismissed;
    }

    private static Release1MissionRecord Mission(Release1StoryState state) =>
        state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)];

    private static string AcceptanceReceipt(Release1ShortNoticeAssignmentMode mode, int attempt) =>
        $"short-notice-{mode.ToString().ToLowerInvariant()}-accept-v1-a{attempt}";

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

    private static Release1ShortNoticeReviewResult Review(Release1ShortNoticeReviewStatus status, string message) =>
        new(status, null, message);

    private static Release1ShortNoticeDecisionResult Decision(Release1ShortNoticeDecisionStatus status, string message) =>
        new(status, message);

    private static Release1ShortNoticeReviewStatus Map(Release1SmallCourtesyWorldReadStatus status) => status switch
    {
        Release1SmallCourtesyWorldReadStatus.Pending or Release1SmallCourtesyWorldReadStatus.NotAuthoritative => Release1ShortNoticeReviewStatus.Pending,
        _ => Release1ShortNoticeReviewStatus.Unavailable
    };

    private static Release1ShortNoticeOfferStatus Map(Release1ShortNoticeReviewStatus status) => status switch
    {
        Release1ShortNoticeReviewStatus.Ready => Release1ShortNoticeOfferStatus.Available,
        Release1ShortNoticeReviewStatus.Inactive => Release1ShortNoticeOfferStatus.Inactive,
        Release1ShortNoticeReviewStatus.Ineligible => Release1ShortNoticeOfferStatus.Ineligible,
        Release1ShortNoticeReviewStatus.Pending => Release1ShortNoticeOfferStatus.Pending,
        Release1ShortNoticeReviewStatus.Saving => Release1ShortNoticeOfferStatus.Saving,
        Release1ShortNoticeReviewStatus.NoDiscoveredProduct => Release1ShortNoticeOfferStatus.NoDiscoveredProduct,
        Release1ShortNoticeReviewStatus.NoEmptyDeadDrop => Release1ShortNoticeOfferStatus.NoEmptyDeadDrop,
        Release1ShortNoticeReviewStatus.Dismissed => Release1ShortNoticeOfferStatus.Dismissed,
        Release1ShortNoticeReviewStatus.Disposed => Release1ShortNoticeOfferStatus.Disposed,
        _ => Release1ShortNoticeOfferStatus.Unavailable
    };

    private static Release1ShortNoticeReviewStatus Map(Release1ShortNoticeSelectionStatus status) => status switch
    {
        Release1ShortNoticeSelectionStatus.NoDiscoveredProduct => Release1ShortNoticeReviewStatus.NoDiscoveredProduct,
        Release1ShortNoticeSelectionStatus.NoEmptyDeadDrop => Release1ShortNoticeReviewStatus.NoEmptyDeadDrop,
        _ => Release1ShortNoticeReviewStatus.Unavailable
    };

    private static string Message(Release1ShortNoticeReviewStatus status) => status switch
    {
        Release1ShortNoticeReviewStatus.NoDiscoveredProduct => "No discovered product is available for assignment.",
        Release1ShortNoticeReviewStatus.NoEmptyDeadDrop => "One clear dead drop is required before a hand off can be assigned.",
        Release1ShortNoticeReviewStatus.Pending => "World eligibility is still pending.",
        Release1ShortNoticeReviewStatus.Ineligible => "Short Notice is not currently offerable.",
        Release1ShortNoticeReviewStatus.Inactive => "The canonical story context is inactive.",
        _ => "Short Notice world data is unavailable."
    };

    /// <summary>
    /// Reads the handoff drop and reconciles the observed manifest against the assignment's required
    /// quantity. A zero-length read is never treated as empty: it means the drop has not finished
    /// initializing, so nothing is recorded. Everything else classifies by identity alone (quantity
    /// ignored), matching the spec's observation table row by row.
    /// </summary>
    public Release1ShortNoticeObservationStatus ReconcileObservation()
    {
        if (_disposed || !_loadActive || _saving || ReadMatchingContext(out _) != Release1ShortNoticeReviewStatus.Ready)
            return Release1ShortNoticeObservationStatus.NoWork;
        if (!TryGetActiveStage(out var mission, out var assignment, out _))
            return Release1ShortNoticeObservationStatus.NoWork;
        if (HasConsumptionEffect(mission.Attempt)) return Release1ShortNoticeObservationStatus.NoWork;

        if (!TryReadSlots(assignment.HandoffDropGuid, out var slots))
            return Release1ShortNoticeObservationStatus.Unavailable;
        if (slots.Count == 0)
        {
            ReportHoldOnce(assignment.Attempt, "the handoff drop reported no slots at all; it has not finished initializing.");
            return Release1ShortNoticeObservationStatus.Unavailable;
        }

        var fingerprint = Fingerprint(assignment);
        var matches = Release1ConsignmentClassifier.CountIdentityMatches(slots, fingerprint, out var single);
        if (matches > 1)
        {
            SetProgress(assignment.Attempt, spreadNoticed: true);
            return Release1ShortNoticeObservationStatus.Spread;
        }
        if (matches == 0)
        {
            // lastShortfallNoticed: null is deliberately a no-op here, not a clear: see SetProgress's
            // doc comment. The observation table's "No identity match" row only calls for
            // ObservedQuantity to reset to 0; LastShortfallNoticed is left at whatever it last was.
            SetProgress(assignment.Attempt, observedQuantity: 0, lastShortfallNoticed: null);
            return Release1ShortNoticeObservationStatus.NoWork;
        }

        var observed = single!.Quantity;
        if (observed >= assignment.RequiredQuantity)
        {
            SetProgress(assignment.Attempt, observedQuantity: observed);
            return Release1ShortNoticeObservationStatus.Ready;
        }

        SetProgress(
            assignment.Attempt,
            observedQuantity: observed,
            lastShortfallNoticed: assignment.RequiredQuantity - observed);
        return Release1ShortNoticeObservationStatus.Shortfall;
    }

    /// <summary>
    /// The dead drop OnClosed entry point: reconciles observation for the exact handoff drop and, if
    /// the manifest just became ready, immediately reconciles the deposit in the same pass. A guid
    /// that is not the current stage's handoff drop is ignored.
    /// </summary>
    public Release1ShortNoticeObservationStatus TryHandleDropClosed(string deadDropGuid)
    {
        if (_disposed || !_loadActive || _saving) return Release1ShortNoticeObservationStatus.NoWork;
        if (!TryGetActiveStage(out _, out var assignment, out _) ||
            !string.Equals(assignment.HandoffDropGuid, deadDropGuid, StringComparison.Ordinal))
            return Release1ShortNoticeObservationStatus.NoWork;
        var observation = ReconcileObservation();
        if (observation == Release1ShortNoticeObservationStatus.Ready) ReconcileDeposit();
        return observation;
    }

    /// <summary>
    /// Consumes an observed-complete manifest and settles the reward, once, in one pass: the exact
    /// N units required are removed from the single matching slot, any surplus is left untouched, and
    /// the 175 percent reward is quoted from the value rule's summed consumed value and paid in the
    /// same pass. This is a mission-specific copy of
    /// <see cref="Release1WrongAddressMissionService"/>'s delivery transaction (see that type's own
    /// remarks for why generalizing it would change Wrong Address behaviour): the match is identity
    /// only and the run condition is at least N, the post-state verify never compares the slot's
    /// monetary value (a surviving surplus reads a different value under the per-stack convention),
    /// and the reward basis quotes from the value rule's summed consumed value rather than a single
    /// deposited reading.
    /// </summary>
    public Release1ShortNoticeDepositStatus ReconcileDeposit()
    {
        if (_disposed || !_loadActive || _saving || ReadMatchingContext(out _) != Release1ShortNoticeReviewStatus.Ready)
            return Release1ShortNoticeDepositStatus.NoWork;
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted)
            return Release1ShortNoticeDepositStatus.NoWork;
        var mission = Mission(state);
        var attempt = mission.Attempt;
        var assignment = state.ShortNoticeAssignments.SingleOrDefault(candidate => candidate.Attempt == attempt);
        if (assignment is null) return Release1ShortNoticeDepositStatus.NoWork;

        var effect = state.NativeEffects.FirstOrDefault(candidate => candidate.EffectId == ConsumptionEffectId(attempt));
        if (effect is null)
        {
            if (!TryGetActiveStage(out _, out _, out var authorization)) return Release1ShortNoticeDepositStatus.NoWork;
            return BeginDeposit(mission, assignment, authorization);
        }

        return ContinueDeposit(mission, assignment, effect);
    }

    private Release1ShortNoticeDepositStatus BeginDeposit(
        Release1MissionRecord mission,
        Release1ShortNoticeAssignment assignment,
        string authorization)
    {
        if (!TryReadSlots(assignment.HandoffDropGuid, out var slots))
            return Release1ShortNoticeDepositStatus.Rejected;
        if (slots.Count == 0) return Release1ShortNoticeDepositStatus.NoWork;

        var matches = Release1ConsignmentClassifier.CountIdentityMatches(slots, Fingerprint(assignment), out var slot);
        if (matches != 1) return Release1ShortNoticeDepositStatus.NoWork;
        if (slot!.Quantity < assignment.RequiredQuantity) return Release1ShortNoticeDepositStatus.NoWork;

        Release1SmallCourtesyCargoIdentity identity;
        try
        {
            identity = new(
                assignment.ProductId,
                assignment.PackagingId,
                slot.SlotIndex,
                slot.Quantity,
                slot.Quantity - assignment.RequiredQuantity,
                slot.MonetaryValue);
        }
        catch (ArgumentException)
        {
            return Release1ShortNoticeDepositStatus.Rejected;
        }

        var prepared = new Release1NativeEffectJournalEntry(
            ConsumptionEffectId(assignment.Attempt),
            Release1MissionCatalog.ShortNotice,
            assignment.Attempt,
            "CargoTransfer",
            assignment.HandoffDropGuid,
            EffectScope,
            identity.Serialize(),
            Release1NativeEffectPhase.Prepared,
            null,
            _story.State!.Revision + 1,
            AuthorizedStoryCorrelationId: authorization,
            AuthorizedMissionRevision: mission.Revision);
        if (!_story.TryPrepareNativeEffect(prepared).Accepted)
            return Release1ShortNoticeDepositStatus.Rejected;

        var freshEffect = _story.State!.NativeEffects.Single(candidate => candidate.EffectId == prepared.EffectId);
        if (!ConsumeManifest(assignment, identity, freshEffect))
            return Release1ShortNoticeDepositStatus.Ambiguous;

        return FinishDeposit(Mission(_story.State!), assignment, freshEffect, identity, authorization);
    }

    private Release1ShortNoticeDepositStatus ContinueDeposit(
        Release1MissionRecord mission,
        Release1ShortNoticeAssignment assignment,
        Release1NativeEffectJournalEntry effect)
    {
        var authorization = effect.AuthorizedStoryCorrelationId;
        if (string.IsNullOrEmpty(authorization) ||
            !Release1SmallCourtesyCargoIdentity.TryParse(effect.AmountOrCargoIdentity, out var identity) ||
            identity is null ||
            effect.Attempt != mission.Attempt ||
            !string.Equals(effect.EffectKind, "CargoTransfer", StringComparison.Ordinal) ||
            !string.Equals(effect.SourceIdentity, assignment.HandoffDropGuid, StringComparison.Ordinal) ||
            !string.Equals(effect.DestinationIdentity, EffectScope, StringComparison.Ordinal) ||
            !string.Equals(identity.ProductId, assignment.ProductId, StringComparison.Ordinal) ||
            !string.Equals(identity.PackageId, assignment.PackagingId, StringComparison.Ordinal))
            return BlockConsumption(effect);
        if (effect.ExecutionBlocked) return Release1ShortNoticeDepositStatus.Ambiguous;

        if (effect.Phase == Release1NativeEffectPhase.Prepared)
        {
            if (!TryReadSlot(assignment.HandoffDropGuid, identity.SlotIndex, out var slot))
                return Release1ShortNoticeDepositStatus.Rejected;
            var exactPre = MatchesCargoSlot(slot, identity, identity.PreQuantity);
            var exactPost = MatchesCargoSlot(slot, identity, identity.PostQuantity);
            if (exactPost)
            {
                if (!MarkConsumptionApplied(effect)) return Release1ShortNoticeDepositStatus.Rejected;
            }
            else if (!exactPre)
            {
                return BlockConsumption(effect);
            }
            else if (!ConsumeManifest(assignment, identity, effect))
            {
                return Release1ShortNoticeDepositStatus.Ambiguous;
            }
        }

        return FinishDeposit(mission, assignment, effect, identity, authorization);
    }

    // Completes the mission and settles the reward for a consumption effect that is now Applied
    // (either freshly this pass or already Applied from a prior, unsaved pass). The story revision
    // is captured once, right here, before either effect's commit runs this pass: both the reward's
    // commit (inside ReconcileReward) and the consumption's commit below are gated on this one
    // frozen snapshot rather than the live, in-memory revision, which would otherwise advance by one
    // with each commit the pass itself performs and wrongly block the second of the two. When both
    // effects were already Applied and durably saved before this pass began, that snapshot equals
    // LastPersistedRevision, so both commits land together in the same post-save pass. Nothing
    // mutates the world beyond this point; only in-memory story bookkeeping happens here.
    private Release1ShortNoticeDepositStatus FinishDeposit(
        Release1MissionRecord mission,
        Release1ShortNoticeAssignment assignment,
        Release1NativeEffectJournalEntry effect,
        Release1SmallCourtesyCargoIdentity identity,
        string authorization)
    {
        var capturedRevision = _story.State!.Revision;
        var applied = _story.State!.NativeEffects.Single(candidate => candidate.EffectId == effect.EffectId);
        if (applied.Phase == Release1NativeEffectPhase.Prepared) return Release1ShortNoticeDepositStatus.Ambiguous;
        if (!TryCompleteAndPrepareReward(applied, assignment, identity)) return Release1ShortNoticeDepositStatus.Rejected;

        var rewardStatus = ReconcileReward(assignment.Attempt, capturedRevision);
        if (rewardStatus != Release1ShortNoticeDepositStatus.Paid &&
            rewardStatus != Release1ShortNoticeDepositStatus.Committed &&
            rewardStatus != Release1ShortNoticeDepositStatus.AwaitingAppliedSave)
            return rewardStatus;

        if (applied.Phase == Release1NativeEffectPhase.Applied &&
            capturedRevision <= _story.LastPersistedRevision)
        {
            var commitResult = _story.TryCommitNativeEffect(applied.EffectId, authorization, capturedRevision);
            if (!commitResult.Accepted)
                ReportHoldOnce(assignment.Attempt, $"the consumption commit after a save was rejected: {commitResult.Message}");
        }

        return rewardStatus switch
        {
            Release1ShortNoticeDepositStatus.Paid => Release1ShortNoticeDepositStatus.Paid,
            Release1ShortNoticeDepositStatus.Committed => Release1ShortNoticeDepositStatus.Committed,
            _ => Release1ShortNoticeDepositStatus.AwaitingAppliedSave
        };
    }

    private bool ConsumeManifest(
        Release1ShortNoticeAssignment assignment,
        Release1SmallCourtesyCargoIdentity identity,
        Release1NativeEffectJournalEntry effect)
    {
        if (!TryAcquireSlotLock(assignment.HandoffDropGuid, identity.SlotIndex))
        {
            BlockConsumption(effect);
            return false;
        }
        try
        {
            Release1SmallCourtesyWorldMutationStatus changed;
            try { changed = _world.TryChangeSlotQuantity(assignment.HandoffDropGuid, identity.SlotIndex, -identity.ConsumedCount); }
            catch { BlockConsumption(effect); return false; }
            if (changed != Release1SmallCourtesyWorldMutationStatus.Succeeded) { BlockConsumption(effect); return false; }
            if (!TryReadSlot(assignment.HandoffDropGuid, identity.SlotIndex, out var post) ||
                !MatchesCargoSlot(post, identity, identity.PostQuantity) ||
                !Release1ShortNoticeValueRule.AgreesWithObservation(
                    assignment.ValueConvention, identity.PreQuantity, identity.DepositedMonetaryValue,
                    post.Quantity, post.MonetaryValue))
            {
                BlockConsumption(effect);
                return false;
            }
            return MarkConsumptionApplied(effect);
        }
        finally
        {
            ReleaseSlotLock();
        }
    }

    private bool MarkConsumptionApplied(Release1NativeEffectJournalEntry effect) =>
        _story.TryMarkNativeEffectApplied(
            effect.EffectId,
            $"short-notice-cargo-native-v1-a{effect.Attempt}",
            Release1NativeEffectPersistenceMode.RevertTolerant).Accepted;

    private bool TryCompleteAndPrepareReward(
        Release1NativeEffectJournalEntry consumption,
        Release1ShortNoticeAssignment assignment,
        Release1SmallCourtesyCargoIdentity identity)
    {
        var state = _story.State;
        if (state is null) return false;
        var mission = Mission(state);
        var existingReward = state.NativeEffects.FirstOrDefault(effect => effect.EffectId == RewardEffectId(mission.Attempt));
        if (mission.State == Release1MissionState.Satisfied) return existingReward is not null;
        if (mission.State is not (Release1MissionState.Active or Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive) ||
            consumption.Phase != Release1NativeEffectPhase.Applied ||
            consumption.ExecutionBlocked ||
            consumption.Attempt != mission.Attempt ||
            existingReward is not null ||
            !TryReadCashBalance(out var baseline))
            return false;

        if (!TryReadSlot(assignment.HandoffDropGuid, identity.SlotIndex, out var post)) return false;
        var summed = Release1ShortNoticeValueRule.SummedConsumedValue(
            assignment.ValueConvention,
            identity.PreQuantity,
            identity.DepositedMonetaryValue,
            post.Quantity,
            post.MonetaryValue,
            identity.ConsumedCount);
        if (summed is not float deliveredValue) return false;
        var quoted = Release1RewardMath.QuoteWholeDollars(deliveredValue, (decimal)assignment.RewardMultiplier);
        if (quoted is not float amount) return false;
        Release1SmallCourtesyRewardIdentity rewardIdentity;
        try
        {
            rewardIdentity = new(baseline, amount, baseline + amount);
        }
        catch (ArgumentException) { return false; }

        if (!_story.TryGetActiveContext(out var context, out _)) return false;
        var completionReceipt = $"short-notice-complete-v1-a{mission.Attempt}";
        var completionCorrelation = Release1LogicalCorrelation.Create(
            context.PlayerId, Release1MissionCatalog.ShortNotice, mission.Attempt,
            Release1TransitionKind.MissionCompleted, completionReceipt).Value;
        var reward = new Release1NativeEffectJournalEntry(
            RewardEffectId(mission.Attempt),
            Release1MissionCatalog.ShortNotice,
            mission.Attempt,
            "Reward",
            EffectScope,
            RewardDestination,
            rewardIdentity.Serialize(),
            Release1NativeEffectPhase.Prepared,
            null,
            state.Revision + 1,
            AuthorizedStoryCorrelationId: completionCorrelation,
            AuthorizedMissionRevision: mission.Revision);
        var command = new Release1StoryCommand(
            context.SessionEpoch,
            context.LoadEpoch,
            context.PlayerId,
            Release1MissionCatalog.ShortNotice,
            mission.Attempt,
            Release1TransitionKind.MissionCompleted,
            completionReceipt,
            completionCorrelation,
            CompletionTiming: mission.State == Release1MissionState.Active
                ? Release1CompletionTiming.OnTime
                : Release1CompletionTiming.Late,
            RewardAuthorizationReceiptId: $"short-notice-reward-auth-v1-a{mission.Attempt}",
            PreparedNativeEffect: reward);
        var completionResult = _story.TryExecute(command);
        if (!completionResult.Accepted)
        {
            ReportHoldOnce(mission.Attempt, $"completing the mission after the manifest was consumed was rejected: {completionResult.Message}");
            return false;
        }
        return true;
    }

    private Release1ShortNoticeDepositStatus ReconcileReward(int attempt, long capturedRevision)
    {
        var state = _story.State;
        var effect = state?.NativeEffects.FirstOrDefault(candidate => candidate.EffectId == RewardEffectId(attempt));
        if (state is null || effect is null) return Release1ShortNoticeDepositStatus.Rejected;
        if (!Release1SmallCourtesyRewardIdentity.TryParse(effect.AmountOrCargoIdentity, out var identity) ||
            identity is null ||
            !string.Equals(effect.EffectKind, "Reward", StringComparison.Ordinal) ||
            !string.Equals(effect.SourceIdentity, EffectScope, StringComparison.Ordinal) ||
            !string.Equals(effect.DestinationIdentity, RewardDestination, StringComparison.Ordinal))
            return BlockReward(effect);
        if (effect.ExecutionBlocked) return Release1ShortNoticeDepositStatus.Ambiguous;
        if (!TryReadCashBalance(out var balance)) return Release1ShortNoticeDepositStatus.Rejected;

        var atBaseline = WithinCashTolerance(balance, identity.BaselineCash, identity.VerificationTolerance);
        var atExpected = WithinCashTolerance(balance, identity.ExpectedCash, identity.VerificationTolerance);

        if (effect.Phase == Release1NativeEffectPhase.Prepared)
        {
            if (atExpected) return MarkRewardApplied(effect);
            if (!atBaseline) return BlockReward(effect);
            Release1SmallCourtesyWorldMutationStatus changed;
            try { changed = _world.TryChangeCashBalance(identity.WholeDollarAmount); }
            catch { return BlockReward(effect); }
            if (changed != Release1SmallCourtesyWorldMutationStatus.Succeeded) return BlockReward(effect);
            if (!TryReadCashBalance(out var post) ||
                !WithinCashTolerance(post, identity.ExpectedCash, identity.VerificationTolerance))
                return BlockReward(effect);
            return MarkRewardApplied(effect);
        }

        if (effect.Phase == Release1NativeEffectPhase.Applied)
        {
            if (!atExpected) return Release1ShortNoticeDepositStatus.Ambiguous;
            if (capturedRevision > _story.LastPersistedRevision)
                return Release1ShortNoticeDepositStatus.AwaitingAppliedSave;
            var authorization = effect.AuthorizedStoryCorrelationId;
            if (string.IsNullOrEmpty(authorization)) return Release1ShortNoticeDepositStatus.Ambiguous;
            return _story.TryCommitNativeEffect(effect.EffectId, authorization, capturedRevision).Accepted
                ? Release1ShortNoticeDepositStatus.Committed
                : Release1ShortNoticeDepositStatus.Rejected;
        }

        return Release1ShortNoticeDepositStatus.Committed;
    }

    private Release1ShortNoticeDepositStatus MarkRewardApplied(Release1NativeEffectJournalEntry effect) =>
        _story.TryMarkNativeEffectApplied(
            effect.EffectId,
            $"short-notice-reward-native-v1-a{effect.Attempt}",
            Release1NativeEffectPersistenceMode.RevertTolerant).Accepted
            ? Release1ShortNoticeDepositStatus.Paid
            : Release1ShortNoticeDepositStatus.Rejected;

    private Release1ShortNoticeDepositStatus BlockConsumption(Release1NativeEffectJournalEntry effect)
    {
        if (effect.Phase == Release1NativeEffectPhase.Prepared && !effect.ExecutionBlocked)
            _story.TryRecordNativeEffectIssuance(effect.EffectId, Release1NativeEffectIssuanceOutcome.Ambiguous);
        ReleaseSlotLock();
        return Release1ShortNoticeDepositStatus.Ambiguous;
    }

    private Release1ShortNoticeDepositStatus BlockReward(Release1NativeEffectJournalEntry effect)
    {
        if (effect.Phase == Release1NativeEffectPhase.Prepared && !effect.ExecutionBlocked)
            _story.TryRecordNativeEffectIssuance(effect.EffectId, Release1NativeEffectIssuanceOutcome.Ambiguous);
        return Release1ShortNoticeDepositStatus.Ambiguous;
    }

    private bool TryReadCashBalance(out float balance)
    {
        balance = 0f;
        try
        {
            return _world.TryReadCashBalance(out balance) == Release1SmallCourtesyWorldReadStatus.Ready &&
                float.IsFinite(balance) && balance >= 0f;
        }
        catch
        {
            return false;
        }
    }

    private bool TryAcquireSlotLock(string deadDropGuid, int slotIndex)
    {
        if (_lockedDropGuid is not null)
            return string.Equals(_lockedDropGuid, deadDropGuid, StringComparison.Ordinal) && _lockedSlotIndex == slotIndex;
        _lockedDropGuid = deadDropGuid;
        _lockedSlotIndex = slotIndex;
        try
        {
            var status = _world.TrySetSlotLocked(deadDropGuid, slotIndex, true);
            if (status != Release1SmallCourtesyWorldMutationStatus.Succeeded)
            {
                if (status == Release1SmallCourtesyWorldMutationStatus.Ambiguous) ReleaseSlotLock();
                else { _lockedDropGuid = null; _lockedSlotIndex = -1; }
                return false;
            }
            return true;
        }
        catch
        {
            ReleaseSlotLock();
            return false;
        }
    }

    private void ReleaseSlotLock()
    {
        if (_lockedDropGuid is null || _lockedSlotIndex < 0) return;
        try
        {
            if (_world.TrySetSlotLocked(_lockedDropGuid, _lockedSlotIndex, false) != Release1SmallCourtesyWorldMutationStatus.Succeeded)
                return;
            _lockedDropGuid = null;
            _lockedSlotIndex = -1;
        }
        catch
        {
            // Retain the exact lock identity so the next lifecycle teardown can retry it.
        }
    }

    private static bool WithinCashTolerance(float actual, float expected, float tolerance) =>
        float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance;

    /// <summary>
    /// The post-state verify. Product, packaging, and quantity only: the slot's monetary value is
    /// deliberately not compared, because a surviving remainder reads a different value under the
    /// per stack convention and the same value under the per unit one, and this method must not
    /// prejudge which. The value agreement is a separate, convention aware check.
    /// </summary>
    private static bool MatchesCargoSlot(
        Release1SmallCourtesySlotSnapshot slot,
        Release1SmallCourtesyCargoIdentity identity,
        int expectedQuantity)
    {
        if (slot.SlotIndex != identity.SlotIndex || slot.Quantity != expectedQuantity) return false;
        if (expectedQuantity == 0) return true;
        return slot.IsPackaged &&
            string.Equals(slot.ProductId, identity.ProductId, StringComparison.Ordinal) &&
            string.Equals(slot.PackagingId, identity.PackageId, StringComparison.Ordinal);
    }

    private bool TryReadSlot(string deadDropGuid, int slotIndex, out Release1SmallCourtesySlotSnapshot slot)
    {
        slot = null!;
        if (!TryReadSlots(deadDropGuid, out var slots)) return false;
        slot = slots.SingleOrDefault(candidate => candidate.SlotIndex == slotIndex)!;
        return slot is not null;
    }

    private static string RewardEffectId(int attempt) => $"short-notice-reward-v1-a{attempt}";

    /// <summary>
    /// Arthur's warning call for a Short Notice make-good, queued exactly once when a required
    /// failure has just put the stage into MakeGoodOffered. There is no clean-completion call and no
    /// Nell prerequisite check beyond what the phone-call request itself enforces: unlike Wrong
    /// Address and Room With No Name, Short Notice's own presentation records the prerequisite
    /// receipt this call authorizes against.
    /// </summary>
    private void TryQueueArthur()
    {
        if (_phone is null || _disposed || !_loadActive || _saving) return;
        var state = _story.State;
        if (state is null) return;
        if (!_story.TryGetActiveContext(out var context, out _)) return;
        var mission = Mission(state);

        if (mission.State != Release1MissionState.MakeGoodOffered || mission.LastOutcome != Release1MissionOutcome.RequiredFailure)
            return;

        try
        {
            var request = Release1PhoneCallRequest.Create(
                context,
                Release1MissionCatalog.ShortNotice,
                mission.Attempt,
                Release1PhoneCallCorrelation.Create(
                    context.PlayerId, Release1MissionCatalog.ShortNotice, mission.Attempt,
                    Release1PhoneCallRole.Arthur, "sn-warning-v1"),
                Release1PhoneCallRole.Arthur,
                ArthurCallerLabel,
                ArthurWarningStages,
                Release1LogicalCorrelation.Create(
                    context.PlayerId, Release1MissionCatalog.ShortNotice, mission.Attempt,
                    Release1TransitionKind.MissionAccepted, "presentation-nell-sn-accepted-v1").Value);
            _phone.TryQueue(request);
        }
        catch (Exception exception)
        {
            _log($"Short Notice Arthur request was not published: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// Exactly one dead drop subscription is held at a time and it is always the handoff drop, from
    /// acceptance until the stage leaves an active state. There is no source drop and no hold, so
    /// the binding never moves within a stage.
    /// </summary>
    private void RefreshDropBinding()
    {
        if (!_loadActive || _disposed || !TryGetActiveStage(out _, out var assignment, out _))
        {
            DisposeDropSubscription();
            return;
        }
        var target = assignment.HandoffDropGuid;
        if (_dropSubscription is not null && string.Equals(_boundDropGuid, target, StringComparison.Ordinal)) return;
        DisposeDropSubscription();
        try
        {
            var status = _world.TrySubscribeDeadDropClosed(target, OnDropClosed, out var subscription);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready || subscription is null ||
                !string.Equals(subscription.DeadDropGuid, target, StringComparison.Ordinal))
            {
                subscription?.Dispose();
                return;
            }
            _dropSubscription = subscription;
            _boundDropGuid = target;
        }
        catch
        {
            DisposeDropSubscription();
        }
    }

    private void OnDropClosed(string deadDropGuid) => _ = TryHandleDropClosed(deadDropGuid);

    private void DisposeDropSubscription()
    {
        try { _dropSubscription?.Dispose(); }
        catch { }
        _dropSubscription = null;
        _boundDropGuid = null;
    }

    private void Converge()
    {
        RefreshDropBinding();
        ReconcileObservation();
        ReconcileDeposit();
        TryQueueArthur();
    }

    /// <summary>
    /// The assignment mode a persisted assignment must carry for the given mission state, when one is
    /// expected to already exist for the current attempt. Mirrors
    /// <see cref="Release1RoomWithNoNameMissionService.ExpectedAssignmentMode"/> exactly; the two
    /// cannot be one method because the assignment types differ.
    /// </summary>
    internal static Release1ShortNoticeAssignmentMode? ExpectedAssignmentMode(Release1MissionState missionState) => missionState switch
    {
        Release1MissionState.Accepted or Release1MissionState.Active => Release1ShortNoticeAssignmentMode.Primary,
        Release1MissionState.MakeGoodOffered or Release1MissionState.MakeGoodActive => Release1ShortNoticeAssignmentMode.MakeGood,
        Release1MissionState.RecoveryAvailable or Release1MissionState.RecoveryActive => Release1ShortNoticeAssignmentMode.Recovery,
        _ => null
    };

    private bool TryGetActiveStage(
        out Release1MissionRecord mission,
        out Release1ShortNoticeAssignment assignment,
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
        assignment = state.ShortNoticeAssignments.SingleOrDefault(candidate =>
            candidate.Attempt == missionAttempt && candidate.Mode == expectedMode.Value)!;
        if (assignment is null) return false;
        authorization = expectedMode == Release1ShortNoticeAssignmentMode.Primary
            ? mission.AcceptedLogicalCorrelations.LastOrDefault(value =>
                Release1LogicalCorrelation.TryParse(value, out var correlation) &&
                correlation.TransitionKind == Release1TransitionKind.MissionActivated) ?? string.Empty
            : assignment.AuthorizationCorrelationId;
        return !string.IsNullOrEmpty(authorization);
    }

    private bool HasConsumptionEffect(int attempt) =>
        _story.State?.NativeEffects.Any(effect => effect.EffectId == ConsumptionEffectId(attempt)) == true;

    private static string ConsumptionEffectId(int attempt) => $"short-notice-cargo-v1-a{attempt}";

    private Release1ShortNoticeProgress? Progress(int attempt) =>
        _story.State?.ShortNoticeProgress.SingleOrDefault(progress => progress.Attempt == attempt);

    /// <summary>
    /// Writes the changed fields of this attempt's progress row in memory. Absent arguments keep
    /// their current value. lastShortfallNoticed is a nested option, <c>Option&lt;int?&gt;?</c>, so a
    /// caller can in principle clear the recorded shortfall by passing an explicit
    /// <c>Option&lt;int?&gt;</c> wrapping a null payload, e.g. <c>(Option&lt;int?&gt;)(int?)null</c>.
    /// A bare <c>lastShortfallNoticed: null</c> does not do this: C#'s predefined null-literal
    /// conversion binds the literal directly to the outer <c>Nullable&lt;Option&lt;int?&gt;&gt;</c> as
    /// "argument not provided" before the compiler ever considers <see cref="Option{T}"/>'s
    /// user-defined implicit operator, so it is a no-op, not a clear. The observation table's
    /// "No identity match" call site below relies on exactly that no-op: the table only specifies
    /// ObservedQuantity set to 0 for that row, and leaves LastShortfallNoticed at its prior value.
    /// </summary>
    private bool SetProgress(
        int attempt,
        int? observedQuantity = null,
        Option<int?>? lastShortfallNoticed = null,
        bool? spreadNoticed = null)
    {
        var existing = Progress(attempt) ?? Release1ShortNoticeProgress.Fresh(attempt);
        var next = existing with
        {
            ObservedQuantity = observedQuantity ?? existing.ObservedQuantity,
            LastShortfallNoticed = lastShortfallNoticed is { } option ? option.Value : existing.LastShortfallNoticed,
            SpreadNoticed = spreadNoticed ?? existing.SpreadNoticed
        };
        if (next == existing) return true;
        var result = _story.TrySetShortNoticeProgress(next);
        if (!result.Accepted && result.Status != Release1StoryCommandStatus.NoOp)
        {
            ReportHoldOnce(attempt, $"recording the deposit progress was rejected: {result.Message}");
            return false;
        }
        return true;
    }

    /// <summary>A one field carrier that distinguishes "leave this alone" from "set this to null".</summary>
    internal readonly record struct Option<T>(T Value)
    {
        public static implicit operator Option<T>(T value) => new(value);
    }

    private bool TryReadSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
    {
        slots = Array.Empty<Release1SmallCourtesySlotSnapshot>();
        try
        {
            if (_world.TryReadDeadDropSlots(deadDropGuid, out var read) != Release1SmallCourtesyWorldReadStatus.Ready || read is null)
                return false;
            var copy = read.ToArray();
            foreach (var slot in copy) slot.Validate();
            if (copy.Select(slot => slot.SlotIndex).Distinct().Count() != copy.Length) return false;
            slots = copy;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ReportHoldOnce(int attempt, string message)
    {
        if (!_reportedHolds.Add($"a{attempt}:{message}")) return;
        _log($"Short Notice attempt {attempt} is holding: {message}");
    }

    private static Release1ConsignmentFingerprint Fingerprint(Release1ShortNoticeAssignment assignment) =>
        new(assignment.ProductId, assignment.PackagingId, assignment.RequiredQuantity);
}
