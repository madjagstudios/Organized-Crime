using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class Release1RoomWithNoNameMissionService : IDisposable
{
    private const string TermsVersion = "room-with-no-name-v1";
    private const double TimedStageDurationHours = 72d;
    private const string EffectScope = "oc-room-with-no-name";
    private const string RewardDestination = "player-cash";
    private const string ArthurCallerLabel = "Arthur Selby";

    private readonly Release1StoryRuntimeService _story;
    private readonly IRelease1SmallCourtesyWorld _world;
    private readonly Release1PhoneCallService? _phone;
    private readonly Action<string> _log;
    private bool _loadActive;
    private bool _saving;
    private bool _dismissed;
    private bool _disposed;
    private Release1RoomWithNoNameQuote? _reviewedQuote;
    private readonly HashSet<string> _reportedHolds = new(StringComparer.Ordinal);
    private IRelease1SmallCourtesyDropSubscription? _dropSubscription;
    private string? _boundDropGuid;
    private string? _lockedDropGuid;
    private int _lockedSlotIndex = -1;

    public Release1RoomWithNoNameMissionService(
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

    public Release1RoomWithNoNameOfferStatus OfferStatus { get; private set; } = Release1RoomWithNoNameOfferStatus.Inactive;
    public Release1RoomWithNoNameQuote? ReviewedQuote => _reviewedQuote;

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
        OfferStatus = Release1RoomWithNoNameOfferStatus.Inactive;
    }

    public void OnSaveStart()
    {
        if (_disposed || !_loadActive) return;
        _saving = true;
        OfferStatus = Release1RoomWithNoNameOfferStatus.Saving;
    }

    public void OnSaveComplete()
    {
        if (_disposed || !_loadActive) return;
        _saving = false;
        RefreshOfferStatus();
        Converge();
    }

    public void Update()
    {
        if (_disposed || !_loadActive || _saving) return;
        var contextStatus = ReadMatchingContext(out _);
        if (contextStatus != Release1RoomWithNoNameReviewStatus.Ready)
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
            var receipt = $"room-with-no-name-deadline-{DeadlineToken(mission.State)}-v1-a{mission.Attempt}";
            _story.TryExecuteDurably(CreateCommand(transition, mission.Attempt, receipt, null, null, null));
            RefreshOfferStatus();
        }

        Converge();
    }

    public Release1RoomWithNoNameReviewResult TryReview()
    {
        if (_disposed) return Review(Release1RoomWithNoNameReviewStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive) return Review(Release1RoomWithNoNameReviewStatus.Inactive, "No load epoch is active.");
        if (_saving) return Review(Release1RoomWithNoNameReviewStatus.Saving, "Mission review is deferred during saving.");
        if (_dismissed) return Review(Release1RoomWithNoNameReviewStatus.Dismissed, "Offer was dismissed for this load.");

        var status = TryBuildQuote(out var quote);
        if (status != Release1RoomWithNoNameReviewStatus.Ready)
        {
            _reviewedQuote = null;
            OfferStatus = Map(status);
            return Review(status, Message(status));
        }

        _reviewedQuote = quote;
        OfferStatus = Release1RoomWithNoNameOfferStatus.Available;
        return new(Release1RoomWithNoNameReviewStatus.Ready, quote, "Room With No Name terms are ready for review.");
    }

    public Release1RoomWithNoNameDecisionResult TryAccept()
    {
        if (_disposed) return Decision(Release1RoomWithNoNameDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive || _dismissed) return Decision(Release1RoomWithNoNameDecisionStatus.Inactive, "No active offer is available.");
        if (_saving) return Decision(Release1RoomWithNoNameDecisionStatus.PersistenceDeferred, "Acceptance is deferred during saving.");
        if (_reviewedQuote is null) return Decision(Release1RoomWithNoNameDecisionStatus.ReviewRequired, "Review the current terms before accepting.");

        var status = TryBuildQuote(out var currentQuote);
        if (status == Release1RoomWithNoNameReviewStatus.InsufficientEmptyDeadDrops)
        {
            _reviewedQuote = null;
            RefreshOfferStatus();
            return Decision(Release1RoomWithNoNameDecisionStatus.InsufficientEmptyDeadDrops, Message(status));
        }
        if (status != Release1RoomWithNoNameReviewStatus.Ready || currentQuote != _reviewedQuote)
        {
            _reviewedQuote = null;
            RefreshOfferStatus();
            return Decision(Release1RoomWithNoNameDecisionStatus.QuoteChanged, "The available product, package, or drops changed; review again.");
        }
        if (!TryReadGameHours(out var acceptedHours))
            return Decision(Release1RoomWithNoNameDecisionStatus.Rejected, "Canonical game time was unavailable.");

        var assignment = currentQuote!.Assignment;
        var transition = assignment.Mode switch
        {
            Release1RoomWithNoNameAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1RoomWithNoNameAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1RoomWithNoNameAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new InvalidOperationException("Assignment mode was not defined.")
        };
        var command = CreateCommand(
            transition,
            assignment.Attempt,
            AcceptanceReceipt(assignment.Mode, assignment.Attempt),
            assignment.Mode == Release1RoomWithNoNameAssignmentMode.Primary ? TermsVersion : null,
            acceptedHours,
            assignment.Mode == Release1RoomWithNoNameAssignmentMode.Recovery ? null : acceptedHours + TimedStageDurationHours);
        var result = _story.TryExecuteRoomWithNoNameAcceptanceDurably(command, assignment);
        if (result.Status == Release1StoryCommandStatus.DeferredSaving)
            return Decision(Release1RoomWithNoNameDecisionStatus.PersistenceDeferred, result.Message);
        if (!result.Accepted)
            return Decision(Release1RoomWithNoNameDecisionStatus.Rejected, result.Message);

        _reviewedQuote = null;
        RefreshOfferStatus();
        Converge();
        return Decision(Release1RoomWithNoNameDecisionStatus.Accepted, "Room With No Name stage was accepted.");
    }

    public Release1RoomWithNoNameDecisionResult TryDefer()
    {
        if (_disposed) return Decision(Release1RoomWithNoNameDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive || _saving || ReadMatchingContext(out _) != Release1RoomWithNoNameReviewStatus.Ready)
            return Decision(Release1RoomWithNoNameDecisionStatus.Inactive, "No active offer is available.");
        var state = _story.State;
        var mission = state is null ? null : Mission(state);
        if (state?.RelationshipState != Release1RelationshipState.Accepted || mission?.State != Release1MissionState.Offered)
            return TryDismiss();

        var receipt = $"room-with-no-name-defer-v1-a{mission.Attempt}";
        var result = _story.TryExecuteDurably(CreateCommand(
            Release1TransitionKind.MissionDeferred, mission.Attempt, receipt, null, null, null));
        if (result.Status == Release1StoryCommandStatus.DeferredSaving)
            return Decision(Release1RoomWithNoNameDecisionStatus.PersistenceDeferred, result.Message);
        if (!result.Accepted)
            return Decision(Release1RoomWithNoNameDecisionStatus.Rejected, result.Message);

        DismissForLoad();
        return Decision(Release1RoomWithNoNameDecisionStatus.Deferred, "Room With No Name was deferred until a later load.");
    }

    public Release1RoomWithNoNameDecisionResult TryDismiss()
    {
        if (_disposed) return Decision(Release1RoomWithNoNameDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive) return Decision(Release1RoomWithNoNameDecisionStatus.Inactive, "No load epoch is active.");
        DismissForLoad();
        return Decision(Release1RoomWithNoNameDecisionStatus.Dismissed, "Room With No Name was dismissed for this load.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        OnPreLoad();
        _disposed = true;
        OfferStatus = Release1RoomWithNoNameOfferStatus.Disposed;
    }

    private Release1RoomWithNoNameReviewStatus TryBuildQuote(out Release1RoomWithNoNameQuote? quote)
    {
        try { return TryBuildQuoteCore(out quote); }
        catch
        {
            quote = null;
            return Release1RoomWithNoNameReviewStatus.Unavailable;
        }
    }

    private Release1RoomWithNoNameReviewStatus TryBuildQuoteCore(out Release1RoomWithNoNameQuote? quote)
    {
        quote = null;
        var contextStatus = ReadMatchingContext(out var context);
        if (contextStatus != Release1RoomWithNoNameReviewStatus.Ready) return contextStatus;
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted)
            return Release1RoomWithNoNameReviewStatus.Ineligible;
        var wrongAddress = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)];
        if (wrongAddress.State != Release1MissionState.Satisfied)
            return Release1RoomWithNoNameReviewStatus.Ineligible;

        var mission = Mission(state);
        var mode = mission.State switch
        {
            Release1MissionState.Offered => Release1RoomWithNoNameAssignmentMode.Primary,
            Release1MissionState.MakeGoodOffered => Release1RoomWithNoNameAssignmentMode.MakeGood,
            Release1MissionState.RecoveryAvailable => Release1RoomWithNoNameAssignmentMode.Recovery,
            _ => (Release1RoomWithNoNameAssignmentMode?)null
        };
        if (mode is null) return Release1RoomWithNoNameReviewStatus.Ineligible;

        var attempt = mode == Release1RoomWithNoNameAssignmentMode.Primary ? mission.Attempt : mission.Attempt + 1;
        var transition = mode switch
        {
            Release1RoomWithNoNameAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1RoomWithNoNameAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1RoomWithNoNameAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new InvalidOperationException("Assignment mode was not defined.")
        };
        var correlation = Release1LogicalCorrelation.Create(
            context.PlayerId,
            Release1MissionCatalog.RoomWithNoName,
            attempt,
            transition,
            AcceptanceReceipt(mode.Value, attempt)).Value;

        var packageKind = mode == Release1RoomWithNoNameAssignmentMode.Primary
            ? Release1SmallCourtesyPackageKind.Brick
            : Release1SmallCourtesyPackageKind.Jar;
        if (_world.TryReadPackaging(packageKind, out var packaging) != Release1SmallCourtesyWorldReadStatus.Ready)
            return Release1RoomWithNoNameReviewStatus.Unavailable;
        try { packaging.Validate(); }
        catch (ArgumentException) { return Release1RoomWithNoNameReviewStatus.Unavailable; }

        IReadOnlyList<Release1SmallCourtesyProductCandidate> products;
        if (mode == Release1RoomWithNoNameAssignmentMode.Primary)
        {
            var productStatus = _world.TryReadProducts(out products);
            if (productStatus != Release1SmallCourtesyWorldReadStatus.Ready) return Map(productStatus);
        }
        else
        {
            var frozen = state.RoomWithNoNameAssignments.OrderBy(candidate => candidate.Attempt).FirstOrDefault();
            if (frozen is null) return Release1RoomWithNoNameReviewStatus.Ineligible;
            products = new[] { new Release1SmallCourtesyProductCandidate(frozen.ProductId, frozen.ProductName, 0d, true) };
        }

        var dropStatus = _world.TryReadDeadDrops(out var drops);
        if (dropStatus != Release1SmallCourtesyWorldReadStatus.Ready) return Map(dropStatus);
        if (!Release1RoomWithNoNameAssignmentSelector.TrySelect(
                products, drops, mode.Value, attempt, correlation,
                packaging.PackagingId, packaging.PackagingName,
                out var assignment, out var selectionStatus))
            return selectionStatus switch
            {
                Release1RoomWithNoNameSelectionStatus.NoDiscoveredProduct => Release1RoomWithNoNameReviewStatus.NoDiscoveredProduct,
                Release1RoomWithNoNameSelectionStatus.InsufficientEmptyDeadDrops => Release1RoomWithNoNameReviewStatus.InsufficientEmptyDeadDrops,
                _ => Release1RoomWithNoNameReviewStatus.Unavailable
            };

        quote = new(assignment!, mode == Release1RoomWithNoNameAssignmentMode.Recovery ? null : TimedStageDurationHours);
        quote.Validate();
        return Release1RoomWithNoNameReviewStatus.Ready;
    }

    private void RefreshOfferStatus()
    {
        if (_disposed) { OfferStatus = Release1RoomWithNoNameOfferStatus.Disposed; return; }
        if (!_loadActive) { OfferStatus = Release1RoomWithNoNameOfferStatus.Inactive; return; }
        if (_saving) { OfferStatus = Release1RoomWithNoNameOfferStatus.Saving; return; }
        if (_dismissed) { OfferStatus = Release1RoomWithNoNameOfferStatus.Dismissed; return; }
        OfferStatus = Map(TryBuildQuote(out _));
    }

    private Release1RoomWithNoNameReviewStatus ReadMatchingContext(out Release1StoryHostContextSnapshot context)
    {
        context = default;
        if (!_story.TryGetActiveContext(out var storyContext, out _)) return Release1RoomWithNoNameReviewStatus.Inactive;
        try
        {
            var status = _world.TryReadContext(out var worldContext);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready) return Map(status);
            if (!SameContext(storyContext, worldContext)) return Release1RoomWithNoNameReviewStatus.Inactive;
            context = storyContext;
            return Release1RoomWithNoNameReviewStatus.Ready;
        }
        catch
        {
            return Release1RoomWithNoNameReviewStatus.Unavailable;
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
            Release1MissionCatalog.RoomWithNoName,
            attempt,
            transition,
            receipt,
            Release1LogicalCorrelation.Create(context.PlayerId, Release1MissionCatalog.RoomWithNoName, attempt, transition, receipt).Value,
            termsVersion,
            acceptedHours,
            deadlineHours);
    }

    private void DismissForLoad()
    {
        _dismissed = true;
        _reviewedQuote = null;
        OfferStatus = Release1RoomWithNoNameOfferStatus.Dismissed;
    }

    private static Release1MissionRecord Mission(Release1StoryState state) =>
        state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)];

    private static string AcceptanceReceipt(Release1RoomWithNoNameAssignmentMode mode, int attempt) =>
        $"room-with-no-name-{mode.ToString().ToLowerInvariant()}-accept-v1-a{attempt}";

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

    private static Release1RoomWithNoNameReviewResult Review(Release1RoomWithNoNameReviewStatus status, string message) =>
        new(status, null, message);

    private static Release1RoomWithNoNameDecisionResult Decision(Release1RoomWithNoNameDecisionStatus status, string message) =>
        new(status, message);

    private static Release1RoomWithNoNameReviewStatus Map(Release1SmallCourtesyWorldReadStatus status) => status switch
    {
        Release1SmallCourtesyWorldReadStatus.Pending or Release1SmallCourtesyWorldReadStatus.NotAuthoritative => Release1RoomWithNoNameReviewStatus.Pending,
        _ => Release1RoomWithNoNameReviewStatus.Unavailable
    };

    private static Release1RoomWithNoNameOfferStatus Map(Release1RoomWithNoNameReviewStatus status) => status switch
    {
        Release1RoomWithNoNameReviewStatus.Ready => Release1RoomWithNoNameOfferStatus.Available,
        Release1RoomWithNoNameReviewStatus.Inactive => Release1RoomWithNoNameOfferStatus.Inactive,
        Release1RoomWithNoNameReviewStatus.Ineligible => Release1RoomWithNoNameOfferStatus.Ineligible,
        Release1RoomWithNoNameReviewStatus.Pending => Release1RoomWithNoNameOfferStatus.Pending,
        Release1RoomWithNoNameReviewStatus.Saving => Release1RoomWithNoNameOfferStatus.Saving,
        Release1RoomWithNoNameReviewStatus.NoDiscoveredProduct => Release1RoomWithNoNameOfferStatus.NoDiscoveredProduct,
        Release1RoomWithNoNameReviewStatus.InsufficientEmptyDeadDrops => Release1RoomWithNoNameOfferStatus.InsufficientEmptyDeadDrops,
        Release1RoomWithNoNameReviewStatus.Dismissed => Release1RoomWithNoNameOfferStatus.Dismissed,
        Release1RoomWithNoNameReviewStatus.Disposed => Release1RoomWithNoNameOfferStatus.Disposed,
        _ => Release1RoomWithNoNameOfferStatus.Unavailable
    };

    private static string Message(Release1RoomWithNoNameReviewStatus status) => status switch
    {
        Release1RoomWithNoNameReviewStatus.NoDiscoveredProduct => "No discovered product is available for assignment.",
        Release1RoomWithNoNameReviewStatus.InsufficientEmptyDeadDrops => "Two clear dead drops are required before a hold can be assigned.",
        Release1RoomWithNoNameReviewStatus.Pending => "World eligibility is still pending.",
        Release1RoomWithNoNameReviewStatus.Ineligible => "Room With No Name is not currently offerable.",
        Release1RoomWithNoNameReviewStatus.Inactive => "The canonical story context is inactive.",
        _ => "Room With No Name world data is unavailable."
    };

    public Release1RoomWithNoNameStageStatus ReconcileStaging()
    {
        if (_disposed || !_loadActive || _saving || ReadMatchingContext(out _) != Release1RoomWithNoNameReviewStatus.Ready)
            return Release1RoomWithNoNameStageStatus.NoWork;
        if (!TryGetActiveStage(out var mission, out var assignment, out _))
            return Release1RoomWithNoNameStageStatus.NoWork;
        var progress = Progress(assignment.Attempt);
        if (progress?.Custody == true) return Release1RoomWithNoNameStageStatus.NoWork;
        if (HasConsumptionEffect(mission.Attempt)) return Release1RoomWithNoNameStageStatus.NoWork;

        if (!TryReadSlots(assignment.SourceDropGuid, out var slots))
            return Release1RoomWithNoNameStageStatus.Unavailable;

        var fingerprint = Fingerprint(assignment);
        var observation = Release1ConsignmentClassifier.Classify(slots, fingerprint, out _);
        if (observation == Release1ContainerObservation.ZeroLength)
        {
            ReportHoldOnce(assignment.Attempt, "the pick up drop read back with zero slots; a zero-length read is not treated as empty.");
            return Release1RoomWithNoNameStageStatus.Unavailable;
        }
        if (observation == Release1ContainerObservation.ExactlyOne)
        {
            if (progress?.Staged == true) return Release1RoomWithNoNameStageStatus.AlreadyStaged;
            return SetProgress(assignment.Attempt, staged: true)
                ? Release1RoomWithNoNameStageStatus.AlreadyStaged
                : Release1RoomWithNoNameStageStatus.Rejected;
        }
        if (observation != Release1ContainerObservation.Empty)
        {
            ReportHoldOnce(assignment.Attempt, "the pick up drop holds something other than exactly the staged consignment; Room With No Name will not mutate it.");
            return Release1RoomWithNoNameStageStatus.Held;
        }

        if (progress?.Staged == true)
            return SetProgress(assignment.Attempt, staged: true, custody: true)
                ? Release1RoomWithNoNameStageStatus.CustodyInferred
                : Release1RoomWithNoNameStageStatus.Rejected;

        Release1SmallCourtesyWorldMutationStatus inserted;
        string reason;
        try
        {
            // The storage entity chooses which slot receives the item; slotIndex is passed only
            // because the interface signature requires a value.
            inserted = _world.TryInsertPackagedProduct(
                assignment.SourceDropGuid,
                slotIndex: 0,
                assignment.ProductId,
                assignment.PackagingId,
                assignment.PackageQuantity,
                out reason);
        }
        catch (Exception exception)
        {
            ReportHoldOnce(assignment.Attempt, $"staging threw {exception.GetType().Name}; Room With No Name will not retry blindly.");
            return Release1RoomWithNoNameStageStatus.Held;
        }

        if (inserted != Release1SmallCourtesyWorldMutationStatus.Succeeded)
        {
            if (inserted == Release1SmallCourtesyWorldMutationStatus.Unavailable)
                return Release1RoomWithNoNameStageStatus.Unavailable;
            ReportHoldOnce(assignment.Attempt, $"staging was refused: {reason}");
            return Release1RoomWithNoNameStageStatus.Held;
        }

        if (!TryReadSlots(assignment.SourceDropGuid, out var post) ||
            Release1ConsignmentClassifier.Classify(post, fingerprint, out _) != Release1ContainerObservation.ExactlyOne)
        {
            ReportHoldOnce(assignment.Attempt, "the staged consignment did not read back as the exact single fingerprint in the drop.");
            return Release1RoomWithNoNameStageStatus.Held;
        }

        return SetProgress(assignment.Attempt, staged: true)
            ? Release1RoomWithNoNameStageStatus.Staged
            : Release1RoomWithNoNameStageStatus.Rejected;
    }

    public Release1RoomWithNoNameStageStatus TryHandleDropClosed(string deadDropGuid)
    {
        if (_disposed || !_loadActive || _saving) return Release1RoomWithNoNameStageStatus.NoWork;
        if (!TryGetActiveStage(out _, out var assignment, out _)) return Release1RoomWithNoNameStageStatus.NoWork;
        var progress = Progress(assignment.Attempt);
        var expected = progress?.HoldSatisfied == true ? assignment.HandoffDropGuid : assignment.SourceDropGuid;
        if (!string.Equals(deadDropGuid, expected, StringComparison.Ordinal))
            return Release1RoomWithNoNameStageStatus.NoWork;
        var stagingStatus = Converge();
        return progress?.Custody == true ? Release1RoomWithNoNameStageStatus.NoWork : stagingStatus;
    }

    /// <summary>
    /// Reads all nine HQ closets and advances the hold. Nothing here mutates the game, so no effect
    /// journal entry is written and no save gate applies; every write is an in-memory progress row
    /// that rides the next native save, or a ledger transition through the existing validated
    /// transitions.
    /// </summary>
    public Release1RoomWithNoNameHoldStatus ReconcileHold()
    {
        if (_disposed || !_loadActive || _saving || ReadMatchingContext(out _) != Release1RoomWithNoNameReviewStatus.Ready)
            return Release1RoomWithNoNameHoldStatus.NoWork;
        if (!TryGetActiveStage(out var mission, out var assignment, out _))
            return Release1RoomWithNoNameHoldStatus.NoWork;
        if (HasConsumptionEffect(assignment.Attempt)) return Release1RoomWithNoNameHoldStatus.NoWork;
        var progress = Progress(assignment.Attempt);
        if (progress is null || !progress.Custody) return Release1RoomWithNoNameHoldStatus.NoWork;
        if (!TryReadGameMinutes(out var now)) return Release1RoomWithNoNameHoldStatus.NoWork;

        Release1HoldRoomSnapshot? room = null;
        try
        {
            if (_world.TryReadHoldRoom(out var read) == Release1SmallCourtesyWorldReadStatus.Ready) room = read;
        }
        catch
        {
            room = null;
        }

        var observation = Release1HoldRoomClassifier.Classify(
            room, Fingerprint(assignment), assignment.ExpectedClosetCount, out var holdingClosetGuid);
        var decision = Release1HoldWindow.Decide(observation, progress, now, assignment.HoldDurationGameMinutes);

        switch (decision)
        {
            case Release1HoldDecision.RecordStow:
                if (holdingClosetGuid is null) return Release1RoomWithNoNameHoldStatus.RoomNotReady;
                return SetProgress(assignment.Attempt, stowed: true, stowedAtGameMinutes: now,
                        holdingClosetGuid: holdingClosetGuid, clearMissingSince: true)
                    ? Release1RoomWithNoNameHoldStatus.Stowed
                    : Release1RoomWithNoNameHoldStatus.Rejected;

            case Release1HoldDecision.SatisfyHold:
                return SetProgress(assignment.Attempt, holdSatisfied: true,
                        holdingClosetGuid: holdingClosetGuid, clearMissingSince: true)
                    ? Release1RoomWithNoNameHoldStatus.Released
                    : Release1RoomWithNoNameHoldStatus.Rejected;

            case Release1HoldDecision.ClearMissing:
                return SetProgress(assignment.Attempt, holdingClosetGuid: holdingClosetGuid, clearMissingSince: true)
                    ? Release1RoomWithNoNameHoldStatus.Holding
                    : Release1RoomWithNoNameHoldStatus.Rejected;

            case Release1HoldDecision.RecordMissing:
                return SetProgress(assignment.Attempt, missingSincePassGameMinutes: now)
                    ? Release1RoomWithNoNameHoldStatus.Missing
                    : Release1RoomWithNoNameHoldStatus.Rejected;

            case Release1HoldDecision.FailHold:
                return FailHold(mission, assignment.Attempt);

            case Release1HoldDecision.Hold:
                if (observation == Release1HoldObservation.RoomNotReady)
                {
                    ReportHoldOnce(assignment.Attempt, "the hold room was not readable this pass; the hold is unchanged.");
                    return Release1RoomWithNoNameHoldStatus.RoomNotReady;
                }
                if (observation == Release1HoldObservation.Ambiguous)
                {
                    ReportHoldOnce(assignment.Attempt, "the hold room holds more than one matching consignment; the hold is unchanged.");
                    return Release1RoomWithNoNameHoldStatus.Ambiguous;
                }
                return Release1RoomWithNoNameHoldStatus.Missing;

            default:
                if (observation != Release1HoldObservation.Held) return Release1RoomWithNoNameHoldStatus.NoWork;
                if (holdingClosetGuid is not null &&
                    !string.Equals(progress.HoldingClosetGuid, holdingClosetGuid, StringComparison.Ordinal) &&
                    !SetProgress(assignment.Attempt, holdingClosetGuid: holdingClosetGuid))
                    return Release1RoomWithNoNameHoldStatus.Rejected;
                return progress.HoldSatisfied
                    ? Release1RoomWithNoNameHoldStatus.NoWork
                    : Release1RoomWithNoNameHoldStatus.Holding;
        }
    }

    /// <summary>
    /// Records a hold miss through the same validated transitions a lapsed deadline uses. In recovery
    /// the transition is RecoveryFailed, which returns the mission to RecoveryAvailable and advances
    /// the attempt, so the next accepted stage stages exactly once: recovery never re-arms staging
    /// without a durable ledger transition.
    /// </summary>
    private Release1RoomWithNoNameHoldStatus FailHold(Release1MissionRecord mission, int attempt)
    {
        var (transition, token) = mission.State switch
        {
            Release1MissionState.Active => (Release1TransitionKind.RequiredFailure, "primary"),
            Release1MissionState.MakeGoodActive => (Release1TransitionKind.MakeGoodFailed, "make-good"),
            Release1MissionState.RecoveryActive => (Release1TransitionKind.RecoveryFailed, "recovery"),
            _ => (Release1TransitionKind.RequiredFailure, string.Empty)
        };
        if (token.Length == 0) return Release1RoomWithNoNameHoldStatus.NoWork;

        var receipt = $"room-with-no-name-hold-{token}-v1-a{attempt}";
        var result = _story.TryExecuteDurably(CreateCommand(transition, attempt, receipt, null, null, null));
        if (result.Status == Release1StoryCommandStatus.DeferredSaving) return Release1RoomWithNoNameHoldStatus.NoWork;
        if (!result.Accepted)
        {
            ReportHoldOnce(attempt, $"recording the hold miss was rejected: {result.Message}");
            return Release1RoomWithNoNameHoldStatus.Rejected;
        }
        RefreshOfferStatus();
        return Release1RoomWithNoNameHoldStatus.Failed;
    }

    public Release1RoomWithNoNameDeliveryStatus ReconcileDelivery()
    {
        if (_disposed || !_loadActive || _saving || ReadMatchingContext(out _) != Release1RoomWithNoNameReviewStatus.Ready)
            return Release1RoomWithNoNameDeliveryStatus.NoWork;
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted)
            return Release1RoomWithNoNameDeliveryStatus.NoWork;
        var mission = Mission(state);
        var attempt = mission.Attempt;
        var assignment = state.RoomWithNoNameAssignments.SingleOrDefault(candidate => candidate.Attempt == attempt);
        if (assignment is null) return Release1RoomWithNoNameDeliveryStatus.NoWork;

        var effect = state.NativeEffects.FirstOrDefault(candidate => candidate.EffectId == ConsumptionEffectId(attempt));
        if (effect is null)
        {
            // The hold gate: the consignment is only deliverable once Nell has called it in.
            if (Progress(attempt)?.HoldSatisfied != true) return Release1RoomWithNoNameDeliveryStatus.NoWork;
            if (!TryGetActiveStage(out _, out _, out var authorization)) return Release1RoomWithNoNameDeliveryStatus.NoWork;
            return BeginDelivery(mission, assignment, authorization);
        }

        return ContinueDelivery(mission, assignment, effect);
    }

    private Release1RoomWithNoNameDeliveryStatus BeginDelivery(
        Release1MissionRecord mission,
        Release1RoomWithNoNameAssignment assignment,
        string authorization)
    {
        if (!TryReadSlots(assignment.HandoffDropGuid, out var slots))
            return Release1RoomWithNoNameDeliveryStatus.Rejected;
        var matchCount = Release1ConsignmentClassifier.CountMatches(slots, Fingerprint(assignment), out var slot);
        if (matchCount == 0) return Release1RoomWithNoNameDeliveryStatus.NoWork;
        if (matchCount > 1)
        {
            ReportHoldOnce(assignment.Attempt, "the hand off drop holds more than one matching consignment; Room With No Name will consume none of them.");
            return Release1RoomWithNoNameDeliveryStatus.Ambiguous;
        }

        Release1SmallCourtesyCargoIdentity identity;
        try
        {
            identity = new(
                assignment.ProductId,
                assignment.PackagingId,
                slot!.SlotIndex,
                slot.Quantity,
                slot.Quantity - 1,
                slot.MonetaryValue);
        }
        catch (ArgumentException)
        {
            return Release1RoomWithNoNameDeliveryStatus.Rejected;
        }

        var prepared = new Release1NativeEffectJournalEntry(
            ConsumptionEffectId(assignment.Attempt),
            Release1MissionCatalog.RoomWithNoName,
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
            return Release1RoomWithNoNameDeliveryStatus.Rejected;

        var freshEffect = _story.State!.NativeEffects.Single(candidate => candidate.EffectId == prepared.EffectId);
        if (!ConsumeOneUnit(assignment, identity, freshEffect))
            return Release1RoomWithNoNameDeliveryStatus.Ambiguous;

        return FinishDelivery(Mission(_story.State!), assignment, freshEffect, identity, authorization);
    }

    private Release1RoomWithNoNameDeliveryStatus ContinueDelivery(
        Release1MissionRecord mission,
        Release1RoomWithNoNameAssignment assignment,
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
        if (effect.ExecutionBlocked) return Release1RoomWithNoNameDeliveryStatus.Ambiguous;

        if (effect.Phase == Release1NativeEffectPhase.Prepared)
        {
            if (!TryReadSlot(assignment.HandoffDropGuid, identity.SlotIndex, out var slot))
                return Release1RoomWithNoNameDeliveryStatus.Rejected;
            var exactPre = MatchesCargoSlot(slot, identity, identity.PreQuantity);
            var exactPost = MatchesCargoSlot(slot, identity, identity.PostQuantity);
            if (exactPost)
            {
                if (!MarkConsumptionApplied(effect)) return Release1RoomWithNoNameDeliveryStatus.Rejected;
            }
            else if (!exactPre)
            {
                return BlockConsumption(effect);
            }
            else if (!ConsumeOneUnit(assignment, identity, effect))
            {
                return Release1RoomWithNoNameDeliveryStatus.Ambiguous;
            }
        }

        return FinishDelivery(mission, assignment, effect, identity, authorization);
    }

    // Completes the mission and settles the reward for a consumption effect that is now Applied. The
    // story revision is captured once, right here, before either effect's commit runs this pass: both
    // commits are gated on that one frozen snapshot rather than the live in-memory revision, which
    // would otherwise advance by one with each commit the pass itself performs and wrongly block the
    // second of the two. Nothing mutates the world beyond this point.
    private Release1RoomWithNoNameDeliveryStatus FinishDelivery(
        Release1MissionRecord mission,
        Release1RoomWithNoNameAssignment assignment,
        Release1NativeEffectJournalEntry effect,
        Release1SmallCourtesyCargoIdentity identity,
        string authorization)
    {
        var capturedRevision = _story.State!.Revision;
        var applied = _story.State!.NativeEffects.Single(candidate => candidate.EffectId == effect.EffectId);
        if (applied.Phase == Release1NativeEffectPhase.Prepared) return Release1RoomWithNoNameDeliveryStatus.Ambiguous;
        if (!TryCompleteAndPrepareReward(applied, assignment, identity)) return Release1RoomWithNoNameDeliveryStatus.Rejected;

        var rewardStatus = ReconcileReward(assignment.Attempt, capturedRevision);
        if (rewardStatus != Release1RoomWithNoNameDeliveryStatus.Paid &&
            rewardStatus != Release1RoomWithNoNameDeliveryStatus.Committed &&
            rewardStatus != Release1RoomWithNoNameDeliveryStatus.AwaitingAppliedSave)
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
            Release1RoomWithNoNameDeliveryStatus.Paid => Release1RoomWithNoNameDeliveryStatus.Paid,
            Release1RoomWithNoNameDeliveryStatus.Committed => Release1RoomWithNoNameDeliveryStatus.Committed,
            _ => Release1RoomWithNoNameDeliveryStatus.AwaitingAppliedSave
        };
    }

    private bool ConsumeOneUnit(
        Release1RoomWithNoNameAssignment assignment,
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
            try { changed = _world.TryChangeSlotQuantity(assignment.HandoffDropGuid, identity.SlotIndex, -1); }
            catch { BlockConsumption(effect); return false; }
            if (changed != Release1SmallCourtesyWorldMutationStatus.Succeeded) { BlockConsumption(effect); return false; }
            if (!TryReadSlot(assignment.HandoffDropGuid, identity.SlotIndex, out var post) ||
                !MatchesCargoSlot(post, identity, identity.PostQuantity))
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
            $"room-with-no-name-cargo-native-v1-a{effect.Attempt}",
            Release1NativeEffectPersistenceMode.RevertTolerant).Accepted;

    private bool TryCompleteAndPrepareReward(
        Release1NativeEffectJournalEntry consumption,
        Release1RoomWithNoNameAssignment assignment,
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

        var quoted = Release1RewardMath.QuoteWholeDollars(identity.DepositedMonetaryValue, (decimal)assignment.RewardMultiplier);
        if (quoted is not float amount) return false;
        Release1SmallCourtesyRewardIdentity rewardIdentity;
        try
        {
            rewardIdentity = new(baseline, amount, baseline + amount);
        }
        catch (ArgumentException) { return false; }

        if (!_story.TryGetActiveContext(out var context, out _)) return false;
        var completionReceipt = $"room-with-no-name-complete-v1-a{mission.Attempt}";
        var completionCorrelation = Release1LogicalCorrelation.Create(
            context.PlayerId, Release1MissionCatalog.RoomWithNoName, mission.Attempt,
            Release1TransitionKind.MissionCompleted, completionReceipt).Value;
        var reward = new Release1NativeEffectJournalEntry(
            RewardEffectId(mission.Attempt),
            Release1MissionCatalog.RoomWithNoName,
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
            Release1MissionCatalog.RoomWithNoName,
            mission.Attempt,
            Release1TransitionKind.MissionCompleted,
            completionReceipt,
            completionCorrelation,
            CompletionTiming: mission.State == Release1MissionState.Active
                ? Release1CompletionTiming.OnTime
                : Release1CompletionTiming.Late,
            RewardAuthorizationReceiptId: $"room-with-no-name-reward-auth-v1-a{mission.Attempt}",
            PreparedNativeEffect: reward);
        var completionResult = _story.TryExecute(command);
        if (!completionResult.Accepted)
        {
            ReportHoldOnce(mission.Attempt, $"completing the mission after the unit was consumed was rejected: {completionResult.Message}");
            return false;
        }
        return true;
    }

    private Release1RoomWithNoNameDeliveryStatus ReconcileReward(int attempt, long capturedRevision)
    {
        var state = _story.State;
        var effect = state?.NativeEffects.FirstOrDefault(candidate => candidate.EffectId == RewardEffectId(attempt));
        if (state is null || effect is null) return Release1RoomWithNoNameDeliveryStatus.Rejected;
        if (!Release1SmallCourtesyRewardIdentity.TryParse(effect.AmountOrCargoIdentity, out var identity) ||
            identity is null ||
            !string.Equals(effect.EffectKind, "Reward", StringComparison.Ordinal) ||
            !string.Equals(effect.SourceIdentity, EffectScope, StringComparison.Ordinal) ||
            !string.Equals(effect.DestinationIdentity, RewardDestination, StringComparison.Ordinal))
            return BlockReward(effect);
        if (effect.ExecutionBlocked) return Release1RoomWithNoNameDeliveryStatus.Ambiguous;
        if (!TryReadCashBalance(out var balance)) return Release1RoomWithNoNameDeliveryStatus.Rejected;

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
            if (!atExpected) return Release1RoomWithNoNameDeliveryStatus.Ambiguous;
            if (capturedRevision > _story.LastPersistedRevision)
                return Release1RoomWithNoNameDeliveryStatus.AwaitingAppliedSave;
            var authorization = effect.AuthorizedStoryCorrelationId;
            if (string.IsNullOrEmpty(authorization)) return Release1RoomWithNoNameDeliveryStatus.Ambiguous;
            return _story.TryCommitNativeEffect(effect.EffectId, authorization, capturedRevision).Accepted
                ? Release1RoomWithNoNameDeliveryStatus.Committed
                : Release1RoomWithNoNameDeliveryStatus.Rejected;
        }

        return Release1RoomWithNoNameDeliveryStatus.Committed;
    }

    private Release1RoomWithNoNameDeliveryStatus MarkRewardApplied(Release1NativeEffectJournalEntry effect) =>
        _story.TryMarkNativeEffectApplied(
            effect.EffectId,
            $"room-with-no-name-reward-native-v1-a{effect.Attempt}",
            Release1NativeEffectPersistenceMode.RevertTolerant).Accepted
            ? Release1RoomWithNoNameDeliveryStatus.Paid
            : Release1RoomWithNoNameDeliveryStatus.Rejected;

    private Release1RoomWithNoNameDeliveryStatus BlockConsumption(Release1NativeEffectJournalEntry effect)
    {
        if (effect.Phase == Release1NativeEffectPhase.Prepared && !effect.ExecutionBlocked)
            _story.TryRecordNativeEffectIssuance(effect.EffectId, Release1NativeEffectIssuanceOutcome.Ambiguous);
        ReleaseSlotLock();
        return Release1RoomWithNoNameDeliveryStatus.Ambiguous;
    }

    private Release1RoomWithNoNameDeliveryStatus BlockReward(Release1NativeEffectJournalEntry effect)
    {
        if (effect.Phase == Release1NativeEffectPhase.Prepared && !effect.ExecutionBlocked)
            _story.TryRecordNativeEffectIssuance(effect.EffectId, Release1NativeEffectIssuanceOutcome.Ambiguous);
        return Release1RoomWithNoNameDeliveryStatus.Ambiguous;
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

    private static bool WithinCashTolerance(float actual, float expected, float tolerance) =>
        float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance;

    private static bool MatchesCargoSlot(
        Release1SmallCourtesySlotSnapshot slot,
        Release1SmallCourtesyCargoIdentity identity,
        int expectedQuantity)
    {
        if (slot.SlotIndex != identity.SlotIndex || slot.Quantity != expectedQuantity) return false;
        if (expectedQuantity == 0) return true;
        return slot.IsPackaged &&
            string.Equals(slot.ProductId, identity.ProductId, StringComparison.Ordinal) &&
            string.Equals(slot.PackagingId, identity.PackageId, StringComparison.Ordinal) &&
            slot.MonetaryValue == identity.DepositedMonetaryValue;
    }

    private bool TryReadSlot(string deadDropGuid, int slotIndex, out Release1SmallCourtesySlotSnapshot slot)
    {
        slot = null!;
        if (!TryReadSlots(deadDropGuid, out var slots)) return false;
        slot = slots.SingleOrDefault(candidate => candidate.SlotIndex == slotIndex)!;
        return slot is not null;
    }

    private Release1RoomWithNoNameStageStatus Converge()
    {
        var stagingStatus = ReconcileStaging();
        ReconcileHold();
        ReconcileDelivery();
        RefreshDropBinding();
        TryQueueArthur();
        return stagingStatus;
    }

    private static readonly string[] ArthurWarningStages =
    {
        "I am told the room is fucking empty and the day is not up. You don't want a visit from me. Fix it.",
        "Nell has arranged one more run. You have seventy two hours from the moment you accept it. She's a lot more fucking forgiving than I am, don't fuck this up.",
        "Do not make her ask twice."
    };

    // Arthur is on the failure path only; there is no clean call for this mission.
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
                Release1MissionCatalog.RoomWithNoName,
                mission.Attempt,
                Release1PhoneCallCorrelation.Create(
                    context.PlayerId, Release1MissionCatalog.RoomWithNoName, mission.Attempt,
                    Release1PhoneCallRole.Arthur, "rn-warning-v1"),
                Release1PhoneCallRole.Arthur,
                ArthurCallerLabel,
                ArthurWarningStages,
                Release1LogicalCorrelation.Create(
                    context.PlayerId, Release1MissionCatalog.RoomWithNoName, mission.Attempt,
                    Release1TransitionKind.MissionAccepted, "presentation-nell-rn-accepted-v1").Value);
            _phone.TryQueue(request);
        }
        catch (Exception exception)
        {
            _log($"Room With No Name Arthur request was not published: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// Exactly one dead drop subscription is held at a time and it follows progress: the pick up drop
    /// until custody, nothing during the hold (there is no drop to watch while the consignment sits in
    /// a closet, and OC never subscribes to a closet), and the hand off drop once the hold is
    /// satisfied.
    /// </summary>
    private void RefreshDropBinding()
    {
        if (!_loadActive || _disposed || !TryGetActiveStage(out _, out var assignment, out _))
        {
            DisposeDropSubscription();
            return;
        }
        var progress = Progress(assignment.Attempt);
        string? target =
            progress?.HoldSatisfied == true ? assignment.HandoffDropGuid :
            progress?.Custody == true ? null :
            assignment.SourceDropGuid;
        if (target is null)
        {
            DisposeDropSubscription();
            return;
        }
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

    /// <summary>
    /// The assignment mode a persisted assignment must carry for the given mission state, when one is
    /// expected to already exist for the current attempt. Mirrors
    /// <see cref="Release1WrongAddressMissionService.ExpectedAssignmentMode"/> exactly; the two cannot
    /// be one method because the assignment types differ.
    /// </summary>
    internal static Release1RoomWithNoNameAssignmentMode? ExpectedAssignmentMode(Release1MissionState missionState) => missionState switch
    {
        Release1MissionState.Accepted or Release1MissionState.Active => Release1RoomWithNoNameAssignmentMode.Primary,
        Release1MissionState.MakeGoodOffered or Release1MissionState.MakeGoodActive => Release1RoomWithNoNameAssignmentMode.MakeGood,
        Release1MissionState.RecoveryAvailable or Release1MissionState.RecoveryActive => Release1RoomWithNoNameAssignmentMode.Recovery,
        _ => null
    };

    private bool TryGetActiveStage(
        out Release1MissionRecord mission,
        out Release1RoomWithNoNameAssignment assignment,
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
        assignment = state.RoomWithNoNameAssignments.SingleOrDefault(candidate =>
            candidate.Attempt == missionAttempt && candidate.Mode == expectedMode.Value)!;
        if (assignment is null) return false;
        authorization = expectedMode == Release1RoomWithNoNameAssignmentMode.Primary
            ? mission.AcceptedLogicalCorrelations.LastOrDefault(value =>
                Release1LogicalCorrelation.TryParse(value, out var correlation) &&
                correlation.TransitionKind == Release1TransitionKind.MissionActivated) ?? string.Empty
            : assignment.AuthorizationCorrelationId;
        return !string.IsNullOrEmpty(authorization);
    }

    private Release1RoomWithNoNameProgress? Progress(int attempt) =>
        _story.State?.RoomWithNoNameProgress.SingleOrDefault(progress => progress.Attempt == attempt);

    private bool SetProgress(
        int attempt,
        bool? staged = null,
        bool? custody = null,
        bool? stowed = null,
        bool? holdSatisfied = null,
        double? stowedAtGameMinutes = null,
        string? holdingClosetGuid = null,
        double? missingSincePassGameMinutes = null,
        bool clearMissingSince = false)
    {
        var existing = Progress(attempt) ?? Release1RoomWithNoNameProgress.Fresh(attempt);
        var next = existing with
        {
            Staged = staged ?? existing.Staged,
            Custody = custody ?? existing.Custody,
            Stowed = stowed ?? existing.Stowed,
            HoldSatisfied = holdSatisfied ?? existing.HoldSatisfied,
            StowedAtGameMinutes = stowedAtGameMinutes ?? existing.StowedAtGameMinutes,
            HoldingClosetGuid = holdingClosetGuid ?? existing.HoldingClosetGuid,
            MissingSincePassGameMinutes = clearMissingSince ? null : missingSincePassGameMinutes ?? existing.MissingSincePassGameMinutes
        };
        var result = _story.TrySetRoomWithNoNameProgress(next);
        if (!result.Accepted && result.Status != Release1StoryCommandStatus.NoOp)
        {
            ReportHoldOnce(attempt, $"recording progress was rejected: {result.Message}");
            return false;
        }
        return true;
    }

    private bool HasConsumptionEffect(int attempt) =>
        _story.State?.NativeEffects.Any(effect => effect.EffectId == ConsumptionEffectId(attempt)) == true;

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

    private void ReportHoldOnce(int attempt, string message)
    {
        if (!_reportedHolds.Add($"a{attempt}:{message}")) return;
        _log($"Room With No Name attempt {attempt} is holding: {message}");
    }

    private static Release1ConsignmentFingerprint Fingerprint(Release1RoomWithNoNameAssignment assignment) =>
        new(assignment.ProductId, assignment.PackagingId, assignment.PackageQuantity);

    private static string ConsumptionEffectId(int attempt) => $"room-with-no-name-cargo-v1-a{attempt}";

    private static string RewardEffectId(int attempt) => $"room-with-no-name-reward-v1-a{attempt}";
}
