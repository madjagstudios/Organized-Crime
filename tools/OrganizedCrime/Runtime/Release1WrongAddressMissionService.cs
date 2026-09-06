using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class Release1WrongAddressMissionService : IDisposable
{
    private const string TermsVersion = "wrong-address-v1";
    private const double TimedStageDurationHours = 24d;
    private const string EffectScope = "oc-wrong-address";
    private const string RewardDestination = "player-cash";
    private const string ArthurCallerLabel = "Arthur Selby";
    private static readonly string[] ArthurCleanStages =
    {
        "Oi! This is Arthur Selby. You will not have heard of me, which is how I prefer it.",
        "You cleaned up someone else's fuckup. Thank god for that.",
        "Nell has my number. Keep doing quiet work and you will hear it again."
    };
    private static readonly string[] ArthurWarningStages =
    {
        "I am told the package is still sitting where it should not be. Stop fucking around or I will have to make a visit.",
        "Nell has arranged one more run. You have twenty four hours from the moment you accept it.",
        "Do not fucking make her ask twice or you will be hearing from me."
    };

    private readonly Release1StoryRuntimeService _story;
    private readonly IRelease1SmallCourtesyWorld _world;
    private readonly Release1PhoneCallService? _phone;
    private readonly Action<string> _log;
    private bool _loadActive;
    private bool _saving;
    private bool _dismissed;
    private bool _disposed;
    private Release1WrongAddressQuote? _reviewedQuote;
    private readonly HashSet<string> _reportedHolds = new(StringComparer.Ordinal);
    private IRelease1SmallCourtesyDropSubscription? _dropSubscription;
    private string? _boundDropGuid;
    private string? _lockedDropGuid;
    private int _lockedSlotIndex = -1;

    public Release1WrongAddressMissionService(
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

    public Release1WrongAddressOfferStatus OfferStatus { get; private set; } = Release1WrongAddressOfferStatus.Inactive;
    public Release1WrongAddressQuote? ReviewedQuote => _reviewedQuote;

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
        OfferStatus = Release1WrongAddressOfferStatus.Inactive;
    }

    public void OnSaveStart()
    {
        if (_disposed || !_loadActive) return;
        _saving = true;
        OfferStatus = Release1WrongAddressOfferStatus.Saving;
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
        if (contextStatus != Release1WrongAddressReviewStatus.Ready)
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
            var receipt = $"wrong-address-deadline-{DeadlineToken(mission.State)}-v1-a{mission.Attempt}";
            _story.TryExecuteDurably(CreateCommand(transition, mission.Attempt, receipt, null, null, null));
            RefreshOfferStatus();
        }

        Converge();
    }

    public Release1WrongAddressReviewResult TryReview()
    {
        if (_disposed) return Review(Release1WrongAddressReviewStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive) return Review(Release1WrongAddressReviewStatus.Inactive, "No load epoch is active.");
        if (_saving) return Review(Release1WrongAddressReviewStatus.Saving, "Mission review is deferred during saving.");
        if (_dismissed) return Review(Release1WrongAddressReviewStatus.Dismissed, "Offer was dismissed for this load.");

        var status = TryBuildQuote(out var quote);
        if (status != Release1WrongAddressReviewStatus.Ready)
        {
            _reviewedQuote = null;
            OfferStatus = Map(status);
            return Review(status, Message(status));
        }

        _reviewedQuote = quote;
        OfferStatus = Release1WrongAddressOfferStatus.Available;
        return new(Release1WrongAddressReviewStatus.Ready, quote, "Wrong Address terms are ready for review.");
    }

    public Release1WrongAddressDecisionResult TryAccept()
    {
        if (_disposed) return Decision(Release1WrongAddressDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive || _dismissed) return Decision(Release1WrongAddressDecisionStatus.Inactive, "No active offer is available.");
        if (_saving) return Decision(Release1WrongAddressDecisionStatus.PersistenceDeferred, "Acceptance is deferred during saving.");
        if (_reviewedQuote is null) return Decision(Release1WrongAddressDecisionStatus.ReviewRequired, "Review the current terms before accepting.");

        var status = TryBuildQuote(out var currentQuote);
        if (status == Release1WrongAddressReviewStatus.InsufficientEmptyDeadDrops)
        {
            _reviewedQuote = null;
            RefreshOfferStatus();
            return Decision(Release1WrongAddressDecisionStatus.InsufficientEmptyDeadDrops, Message(status));
        }
        if (status != Release1WrongAddressReviewStatus.Ready || currentQuote != _reviewedQuote)
        {
            _reviewedQuote = null;
            RefreshOfferStatus();
            return Decision(Release1WrongAddressDecisionStatus.QuoteChanged, "The available product, package, or drops changed; review again.");
        }
        if (!TryReadGameHours(out var acceptedHours))
            return Decision(Release1WrongAddressDecisionStatus.Rejected, "Canonical game time was unavailable.");

        var assignment = currentQuote!.Assignment;
        var transition = assignment.Mode switch
        {
            Release1WrongAddressAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1WrongAddressAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1WrongAddressAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new InvalidOperationException("Assignment mode was not defined.")
        };
        var command = CreateCommand(
            transition,
            assignment.Attempt,
            AcceptanceReceipt(assignment.Mode, assignment.Attempt),
            assignment.Mode == Release1WrongAddressAssignmentMode.Primary ? TermsVersion : null,
            acceptedHours,
            assignment.Mode == Release1WrongAddressAssignmentMode.Recovery ? null : acceptedHours + TimedStageDurationHours);
        var result = _story.TryExecuteWrongAddressAcceptanceDurably(command, assignment);
        if (result.Status == Release1StoryCommandStatus.DeferredSaving)
            return Decision(Release1WrongAddressDecisionStatus.PersistenceDeferred, result.Message);
        if (!result.Accepted)
            return Decision(Release1WrongAddressDecisionStatus.Rejected, result.Message);

        _reviewedQuote = null;
        RefreshOfferStatus();
        Converge();
        return Decision(Release1WrongAddressDecisionStatus.Accepted, "Wrong Address stage was accepted.");
    }

    public Release1WrongAddressDecisionResult TryDefer()
    {
        if (_disposed) return Decision(Release1WrongAddressDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive || _saving || ReadMatchingContext(out _) != Release1WrongAddressReviewStatus.Ready)
            return Decision(Release1WrongAddressDecisionStatus.Inactive, "No active offer is available.");
        var state = _story.State;
        var mission = state is null ? null : Mission(state);
        if (state?.RelationshipState != Release1RelationshipState.Accepted || mission?.State != Release1MissionState.Offered)
            return TryDismiss();

        var receipt = $"wrong-address-defer-v1-a{mission.Attempt}";
        var result = _story.TryExecuteDurably(CreateCommand(
            Release1TransitionKind.MissionDeferred, mission.Attempt, receipt, null, null, null));
        if (result.Status == Release1StoryCommandStatus.DeferredSaving)
            return Decision(Release1WrongAddressDecisionStatus.PersistenceDeferred, result.Message);
        if (!result.Accepted)
            return Decision(Release1WrongAddressDecisionStatus.Rejected, result.Message);

        DismissForLoad();
        return Decision(Release1WrongAddressDecisionStatus.Deferred, "Wrong Address was deferred until a later load.");
    }

    public Release1WrongAddressDecisionResult TryDismiss()
    {
        if (_disposed) return Decision(Release1WrongAddressDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive) return Decision(Release1WrongAddressDecisionStatus.Inactive, "No load epoch is active.");
        DismissForLoad();
        return Decision(Release1WrongAddressDecisionStatus.Dismissed, "Wrong Address was dismissed for this load.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        OnPreLoad();
        _disposed = true;
        OfferStatus = Release1WrongAddressOfferStatus.Disposed;
    }

    private Release1WrongAddressReviewStatus TryBuildQuote(out Release1WrongAddressQuote? quote)
    {
        try { return TryBuildQuoteCore(out quote); }
        catch
        {
            quote = null;
            return Release1WrongAddressReviewStatus.Unavailable;
        }
    }

    private Release1WrongAddressReviewStatus TryBuildQuoteCore(out Release1WrongAddressQuote? quote)
    {
        quote = null;
        var contextStatus = ReadMatchingContext(out var context);
        if (contextStatus != Release1WrongAddressReviewStatus.Ready) return contextStatus;
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted)
            return Release1WrongAddressReviewStatus.Ineligible;
        var smallCourtesy = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        if (smallCourtesy.State != Release1MissionState.Satisfied)
            return Release1WrongAddressReviewStatus.Ineligible;

        var mission = Mission(state);
        var mode = mission.State switch
        {
            Release1MissionState.Offered => Release1WrongAddressAssignmentMode.Primary,
            Release1MissionState.MakeGoodOffered => Release1WrongAddressAssignmentMode.MakeGood,
            Release1MissionState.RecoveryAvailable => Release1WrongAddressAssignmentMode.Recovery,
            _ => (Release1WrongAddressAssignmentMode?)null
        };
        if (mode is null) return Release1WrongAddressReviewStatus.Ineligible;

        var attempt = mode == Release1WrongAddressAssignmentMode.Primary ? mission.Attempt : mission.Attempt + 1;
        var transition = mode switch
        {
            Release1WrongAddressAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1WrongAddressAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1WrongAddressAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new InvalidOperationException("Assignment mode was not defined.")
        };
        var correlation = Release1LogicalCorrelation.Create(
            context.PlayerId,
            Release1MissionCatalog.WrongAddress,
            attempt,
            transition,
            AcceptanceReceipt(mode.Value, attempt)).Value;

        var packageKind = mode == Release1WrongAddressAssignmentMode.Primary
            ? Release1SmallCourtesyPackageKind.Brick
            : Release1SmallCourtesyPackageKind.Jar;
        if (_world.TryReadPackaging(packageKind, out var packaging) != Release1SmallCourtesyWorldReadStatus.Ready)
            return Release1WrongAddressReviewStatus.Unavailable;
        try { packaging.Validate(); }
        catch (ArgumentException) { return Release1WrongAddressReviewStatus.Unavailable; }

        IReadOnlyList<Release1SmallCourtesyProductCandidate> products;
        if (mode == Release1WrongAddressAssignmentMode.Primary)
        {
            var productStatus = _world.TryReadProducts(out products);
            if (productStatus != Release1SmallCourtesyWorldReadStatus.Ready) return Map(productStatus);
        }
        else
        {
            var frozen = state.WrongAddressAssignments.OrderBy(candidate => candidate.Attempt).FirstOrDefault();
            if (frozen is null) return Release1WrongAddressReviewStatus.Ineligible;
            products = new[] { new Release1SmallCourtesyProductCandidate(frozen.ProductId, frozen.ProductName, 0d, true) };
        }

        var dropStatus = _world.TryReadDeadDrops(out var drops);
        if (dropStatus != Release1SmallCourtesyWorldReadStatus.Ready) return Map(dropStatus);
        if (!Release1WrongAddressAssignmentSelector.TrySelect(
                products, drops, mode.Value, attempt, correlation,
                packaging.PackagingId, packaging.PackagingName,
                out var assignment, out var selectionStatus))
            return selectionStatus switch
            {
                Release1WrongAddressSelectionStatus.NoDiscoveredProduct => Release1WrongAddressReviewStatus.NoDiscoveredProduct,
                Release1WrongAddressSelectionStatus.InsufficientEmptyDeadDrops => Release1WrongAddressReviewStatus.InsufficientEmptyDeadDrops,
                _ => Release1WrongAddressReviewStatus.Unavailable
            };

        quote = new(assignment!, mode == Release1WrongAddressAssignmentMode.Recovery ? null : TimedStageDurationHours);
        quote.Validate();
        return Release1WrongAddressReviewStatus.Ready;
    }

    private void RefreshOfferStatus()
    {
        if (_disposed) { OfferStatus = Release1WrongAddressOfferStatus.Disposed; return; }
        if (!_loadActive) { OfferStatus = Release1WrongAddressOfferStatus.Inactive; return; }
        if (_saving) { OfferStatus = Release1WrongAddressOfferStatus.Saving; return; }
        if (_dismissed) { OfferStatus = Release1WrongAddressOfferStatus.Dismissed; return; }
        OfferStatus = Map(TryBuildQuote(out _));
    }

    private Release1WrongAddressReviewStatus ReadMatchingContext(out Release1StoryHostContextSnapshot context)
    {
        context = default;
        if (!_story.TryGetActiveContext(out var storyContext, out _)) return Release1WrongAddressReviewStatus.Inactive;
        try
        {
            var status = _world.TryReadContext(out var worldContext);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready) return Map(status);
            if (!SameContext(storyContext, worldContext)) return Release1WrongAddressReviewStatus.Inactive;
            context = storyContext;
            return Release1WrongAddressReviewStatus.Ready;
        }
        catch
        {
            return Release1WrongAddressReviewStatus.Unavailable;
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
            Release1MissionCatalog.WrongAddress,
            attempt,
            transition,
            receipt,
            Release1LogicalCorrelation.Create(context.PlayerId, Release1MissionCatalog.WrongAddress, attempt, transition, receipt).Value,
            termsVersion,
            acceptedHours,
            deadlineHours);
    }

    private void DismissForLoad()
    {
        _dismissed = true;
        _reviewedQuote = null;
        OfferStatus = Release1WrongAddressOfferStatus.Dismissed;
    }

    private static Release1MissionRecord Mission(Release1StoryState state) =>
        state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)];

    private static string AcceptanceReceipt(Release1WrongAddressAssignmentMode mode, int attempt) =>
        $"wrong-address-{mode.ToString().ToLowerInvariant()}-accept-v1-a{attempt}";

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

    private static Release1WrongAddressReviewResult Review(Release1WrongAddressReviewStatus status, string message) =>
        new(status, null, message);

    private static Release1WrongAddressDecisionResult Decision(Release1WrongAddressDecisionStatus status, string message) =>
        new(status, message);

    private static Release1WrongAddressReviewStatus Map(Release1SmallCourtesyWorldReadStatus status) => status switch
    {
        Release1SmallCourtesyWorldReadStatus.Pending or Release1SmallCourtesyWorldReadStatus.NotAuthoritative => Release1WrongAddressReviewStatus.Pending,
        _ => Release1WrongAddressReviewStatus.Unavailable
    };

    private static Release1WrongAddressOfferStatus Map(Release1WrongAddressReviewStatus status) => status switch
    {
        Release1WrongAddressReviewStatus.Ready => Release1WrongAddressOfferStatus.Available,
        Release1WrongAddressReviewStatus.Inactive => Release1WrongAddressOfferStatus.Inactive,
        Release1WrongAddressReviewStatus.Ineligible => Release1WrongAddressOfferStatus.Ineligible,
        Release1WrongAddressReviewStatus.Pending => Release1WrongAddressOfferStatus.Pending,
        Release1WrongAddressReviewStatus.Saving => Release1WrongAddressOfferStatus.Saving,
        Release1WrongAddressReviewStatus.NoDiscoveredProduct => Release1WrongAddressOfferStatus.NoDiscoveredProduct,
        Release1WrongAddressReviewStatus.InsufficientEmptyDeadDrops => Release1WrongAddressOfferStatus.InsufficientEmptyDeadDrops,
        Release1WrongAddressReviewStatus.Dismissed => Release1WrongAddressOfferStatus.Dismissed,
        Release1WrongAddressReviewStatus.Disposed => Release1WrongAddressOfferStatus.Disposed,
        _ => Release1WrongAddressOfferStatus.Unavailable
    };

    private static string Message(Release1WrongAddressReviewStatus status) => status switch
    {
        Release1WrongAddressReviewStatus.NoDiscoveredProduct => "No discovered product is available for assignment.",
        Release1WrongAddressReviewStatus.InsufficientEmptyDeadDrops => "Two clear dead drops are required before a recovery run can be assigned.",
        Release1WrongAddressReviewStatus.Pending => "World eligibility is still pending.",
        Release1WrongAddressReviewStatus.Ineligible => "Wrong Address is not currently offerable.",
        Release1WrongAddressReviewStatus.Inactive => "The canonical story context is inactive.",
        _ => "Wrong Address world data is unavailable."
    };

    public Release1WrongAddressStageStatus ReconcileStaging()
    {
        if (_disposed || !_loadActive || _saving || ReadMatchingContext(out _) != Release1WrongAddressReviewStatus.Ready)
            return Release1WrongAddressStageStatus.NoWork;
        if (!TryGetActiveStage(out var mission, out var assignment, out _))
            return Release1WrongAddressStageStatus.NoWork;
        var progress = Progress(assignment.Attempt);
        if (progress?.Custody == true) return Release1WrongAddressStageStatus.NoWork;
        if (HasConsumptionEffect(mission.Attempt)) return Release1WrongAddressStageStatus.NoWork;

        if (!TryReadSlots(assignment.SourceDropGuid, out var slots))
            return Release1WrongAddressStageStatus.Unavailable;

        var observation = Release1ConsignmentClassifier.Classify(slots, Fingerprint(assignment), out _);
        if (observation == Release1ContainerObservation.ZeroLength)
        {
            ReportHoldOnce(assignment.Attempt, "the source drop read back with zero slots; a zero-length read is not treated as empty.");
            return Release1WrongAddressStageStatus.Unavailable;
        }
        if (observation == Release1ContainerObservation.ExactlyOne)
        {
            if (progress?.Staged == true) return Release1WrongAddressStageStatus.AlreadyStaged;
            return _story.TrySetWrongAddressStaged(assignment.Attempt).Accepted
                ? Release1WrongAddressStageStatus.AlreadyStaged
                : Release1WrongAddressStageStatus.Rejected;
        }
        if (observation != Release1ContainerObservation.Empty)
        {
            ReportHoldOnce(assignment.Attempt, "the source drop holds something other than exactly the staged package; Wrong Address will not mutate it.");
            return Release1WrongAddressStageStatus.Held;
        }

        if (progress?.Staged == true)
            return _story.TrySetWrongAddressCustody(assignment.Attempt).Accepted
                ? Release1WrongAddressStageStatus.CustodyInferred
                : Release1WrongAddressStageStatus.Rejected;

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
            ReportHoldOnce(assignment.Attempt, $"staging threw {exception.GetType().Name}; Wrong Address will not retry blindly.");
            return Release1WrongAddressStageStatus.Held;
        }

        if (inserted != Release1SmallCourtesyWorldMutationStatus.Succeeded)
        {
            if (inserted == Release1SmallCourtesyWorldMutationStatus.Unavailable)
                return Release1WrongAddressStageStatus.Unavailable;
            ReportHoldOnce(assignment.Attempt, $"staging was refused: {reason}");
            return Release1WrongAddressStageStatus.Held;
        }

        if (!TryReadSlots(assignment.SourceDropGuid, out var post))
        {
            ReportHoldOnce(assignment.Attempt, "the staged package could not be read back after insertion.");
            return Release1WrongAddressStageStatus.Held;
        }
        if (Release1ConsignmentClassifier.Classify(post, Fingerprint(assignment), out _) != Release1ContainerObservation.ExactlyOne)
        {
            ReportHoldOnce(assignment.Attempt, "the staged package did not read back as the exact single fingerprint match in the drop.");
            return Release1WrongAddressStageStatus.Held;
        }

        return _story.TrySetWrongAddressStaged(assignment.Attempt).Accepted
            ? Release1WrongAddressStageStatus.Staged
            : Release1WrongAddressStageStatus.Rejected;
    }

    public Release1WrongAddressStageStatus TryHandleDropClosed(string deadDropGuid)
    {
        if (_disposed || !_loadActive || _saving) return Release1WrongAddressStageStatus.NoWork;
        if (!TryGetActiveStage(out _, out var assignment, out _)) return Release1WrongAddressStageStatus.NoWork;
        var custody = Progress(assignment.Attempt)?.Custody == true;
        var expected = custody ? assignment.HandoffDropGuid : assignment.SourceDropGuid;
        if (!string.Equals(deadDropGuid, expected, StringComparison.Ordinal))
            return Release1WrongAddressStageStatus.NoWork;
        var stagingStatus = Converge();
        return custody ? Release1WrongAddressStageStatus.NoWork : stagingStatus;
    }

    private Release1WrongAddressStageStatus Converge()
    {
        var stagingStatus = ReconcileStaging();
        ReconcileDelivery();
        RefreshDropBinding();
        TryQueueArthur();
        return stagingStatus;
    }

    private void TryQueueArthur()
    {
        if (_phone is null || _disposed || !_loadActive || _saving) return;
        var state = _story.State;
        if (state is null) return;
        if (!_story.TryGetActiveContext(out var context, out _)) return;
        var mission = Mission(state);

        string receipt;
        IReadOnlyList<string> stages;
        if (mission.State == Release1MissionState.Satisfied &&
            mission.LastOutcome == Release1MissionOutcome.OnTime &&
            mission.MakeGoodFailures == 0 &&
            state.WrongAddressAssignments.Any(assignment =>
                assignment.Attempt == mission.Attempt && assignment.Mode == Release1WrongAddressAssignmentMode.Primary))
        {
            receipt = "wa-clean-v1";
            stages = ArthurCleanStages;
        }
        else if (mission.State == Release1MissionState.MakeGoodOffered &&
                 mission.LastOutcome == Release1MissionOutcome.RequiredFailure)
        {
            receipt = "wa-warning-v1";
            stages = ArthurWarningStages;
        }
        else
        {
            return;
        }

        try
        {
            var request = Release1PhoneCallRequest.Create(
                context,
                Release1MissionCatalog.WrongAddress,
                mission.Attempt,
                Release1PhoneCallCorrelation.Create(
                    context.PlayerId, Release1MissionCatalog.WrongAddress, mission.Attempt,
                    Release1PhoneCallRole.Arthur, receipt),
                Release1PhoneCallRole.Arthur,
                ArthurCallerLabel,
                stages,
                Release1LogicalCorrelation.Create(
                    context.PlayerId, Release1MissionCatalog.WrongAddress, mission.Attempt,
                    Release1TransitionKind.MissionAccepted, "presentation-nell-wa-accepted-v1").Value);
            _phone.TryQueue(request);
        }
        catch (Exception exception)
        {
            _log($"Wrong Address Arthur request was not published: {exception.GetType().Name}");
        }
    }

    public Release1WrongAddressDeliveryStatus ReconcileDelivery()
    {
        if (_disposed || !_loadActive || _saving || ReadMatchingContext(out _) != Release1WrongAddressReviewStatus.Ready)
            return Release1WrongAddressDeliveryStatus.NoWork;
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted)
            return Release1WrongAddressDeliveryStatus.NoWork;
        var mission = Mission(state);
        var attempt = mission.Attempt;
        var assignment = state.WrongAddressAssignments.SingleOrDefault(candidate => candidate.Attempt == attempt);
        if (assignment is null) return Release1WrongAddressDeliveryStatus.NoWork;

        var effect = state.NativeEffects.FirstOrDefault(candidate => candidate.EffectId == ConsumptionEffectId(attempt));
        if (effect is null)
        {
            if (Progress(attempt)?.Custody != true) return Release1WrongAddressDeliveryStatus.NoWork;
            if (!TryGetActiveStage(out _, out _, out var authorization)) return Release1WrongAddressDeliveryStatus.NoWork;
            return BeginDelivery(mission, assignment, authorization);
        }

        return ContinueDelivery(mission, assignment, effect);
    }

    private Release1WrongAddressDeliveryStatus BeginDelivery(
        Release1MissionRecord mission,
        Release1WrongAddressAssignment assignment,
        string authorization)
    {
        if (!TryReadSlots(assignment.HandoffDropGuid, out var slots))
            return Release1WrongAddressDeliveryStatus.Rejected;
        var matchCount = Release1ConsignmentClassifier.CountMatches(slots, Fingerprint(assignment), out var slot);
        if (matchCount == 0) return Release1WrongAddressDeliveryStatus.NoWork;
        if (matchCount > 1)
        {
            ReportHoldOnce(assignment.Attempt, "the handoff drop holds more than one matching package; Wrong Address will consume none of them.");
            return Release1WrongAddressDeliveryStatus.Ambiguous;
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
            return Release1WrongAddressDeliveryStatus.Rejected;
        }

        var prepared = new Release1NativeEffectJournalEntry(
            ConsumptionEffectId(assignment.Attempt),
            Release1MissionCatalog.WrongAddress,
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
            return Release1WrongAddressDeliveryStatus.Rejected;

        var freshEffect = _story.State!.NativeEffects.Single(candidate => candidate.EffectId == prepared.EffectId);
        if (!ConsumeOneUnit(assignment, identity, freshEffect))
            return Release1WrongAddressDeliveryStatus.Ambiguous;

        return FinishDelivery(Mission(_story.State!), assignment, freshEffect, identity, authorization);
    }

    private Release1WrongAddressDeliveryStatus ContinueDelivery(
        Release1MissionRecord mission,
        Release1WrongAddressAssignment assignment,
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
        if (effect.ExecutionBlocked) return Release1WrongAddressDeliveryStatus.Ambiguous;

        if (effect.Phase == Release1NativeEffectPhase.Prepared)
        {
            if (!TryReadSlot(assignment.HandoffDropGuid, identity.SlotIndex, out var slot))
                return Release1WrongAddressDeliveryStatus.Rejected;
            var exactPre = MatchesCargoSlot(slot, identity, identity.PreQuantity);
            var exactPost = MatchesCargoSlot(slot, identity, identity.PostQuantity);
            if (exactPost)
            {
                if (!MarkConsumptionApplied(effect)) return Release1WrongAddressDeliveryStatus.Rejected;
            }
            else if (!exactPre)
            {
                return BlockConsumption(effect);
            }
            else if (!ConsumeOneUnit(assignment, identity, effect))
            {
                return Release1WrongAddressDeliveryStatus.Ambiguous;
            }
        }

        return FinishDelivery(mission, assignment, effect, identity, authorization);
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
    private Release1WrongAddressDeliveryStatus FinishDelivery(
        Release1MissionRecord mission,
        Release1WrongAddressAssignment assignment,
        Release1NativeEffectJournalEntry effect,
        Release1SmallCourtesyCargoIdentity identity,
        string authorization)
    {
        var capturedRevision = _story.State!.Revision;
        var applied = _story.State!.NativeEffects.Single(candidate => candidate.EffectId == effect.EffectId);
        if (applied.Phase == Release1NativeEffectPhase.Prepared) return Release1WrongAddressDeliveryStatus.Ambiguous;
        if (!TryCompleteAndPrepareReward(applied, identity)) return Release1WrongAddressDeliveryStatus.Rejected;

        var rewardStatus = ReconcileReward(assignment.Attempt, capturedRevision);
        if (rewardStatus != Release1WrongAddressDeliveryStatus.Paid &&
            rewardStatus != Release1WrongAddressDeliveryStatus.Committed &&
            rewardStatus != Release1WrongAddressDeliveryStatus.AwaitingAppliedSave)
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
            Release1WrongAddressDeliveryStatus.Paid => Release1WrongAddressDeliveryStatus.Paid,
            Release1WrongAddressDeliveryStatus.Committed => Release1WrongAddressDeliveryStatus.Committed,
            _ => Release1WrongAddressDeliveryStatus.AwaitingAppliedSave
        };
    }

    private bool ConsumeOneUnit(
        Release1WrongAddressAssignment assignment,
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
            $"wrong-address-cargo-native-v1-a{effect.Attempt}",
            Release1NativeEffectPersistenceMode.RevertTolerant).Accepted;

    private bool TryCompleteAndPrepareReward(
        Release1NativeEffectJournalEntry consumption,
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

        var quoted = Release1RewardMath.QuoteWholeDollars(identity.DepositedMonetaryValue);
        if (quoted is not float amount) return false;
        Release1SmallCourtesyRewardIdentity rewardIdentity;
        try
        {
            rewardIdentity = new(baseline, amount, baseline + amount);
        }
        catch (ArgumentException) { return false; }

        if (!_story.TryGetActiveContext(out var context, out _)) return false;
        var completionReceipt = $"wrong-address-complete-v1-a{mission.Attempt}";
        var completionCorrelation = Release1LogicalCorrelation.Create(
            context.PlayerId, Release1MissionCatalog.WrongAddress, mission.Attempt,
            Release1TransitionKind.MissionCompleted, completionReceipt).Value;
        var reward = new Release1NativeEffectJournalEntry(
            RewardEffectId(mission.Attempt),
            Release1MissionCatalog.WrongAddress,
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
            Release1MissionCatalog.WrongAddress,
            mission.Attempt,
            Release1TransitionKind.MissionCompleted,
            completionReceipt,
            completionCorrelation,
            CompletionTiming: mission.State == Release1MissionState.Active
                ? Release1CompletionTiming.OnTime
                : Release1CompletionTiming.Late,
            RewardAuthorizationReceiptId: $"wrong-address-reward-auth-v1-a{mission.Attempt}",
            PreparedNativeEffect: reward);
        var completionResult = _story.TryExecute(command);
        if (!completionResult.Accepted)
        {
            ReportHoldOnce(mission.Attempt, $"completing the mission after the unit was consumed was rejected: {completionResult.Message}");
            return false;
        }
        return true;
    }

    private Release1WrongAddressDeliveryStatus ReconcileReward(int attempt, long capturedRevision)
    {
        var state = _story.State;
        var effect = state?.NativeEffects.FirstOrDefault(candidate => candidate.EffectId == RewardEffectId(attempt));
        if (state is null || effect is null) return Release1WrongAddressDeliveryStatus.Rejected;
        if (!Release1SmallCourtesyRewardIdentity.TryParse(effect.AmountOrCargoIdentity, out var identity) ||
            identity is null ||
            !string.Equals(effect.EffectKind, "Reward", StringComparison.Ordinal) ||
            !string.Equals(effect.SourceIdentity, EffectScope, StringComparison.Ordinal) ||
            !string.Equals(effect.DestinationIdentity, RewardDestination, StringComparison.Ordinal))
            return BlockReward(effect);
        if (effect.ExecutionBlocked) return Release1WrongAddressDeliveryStatus.Ambiguous;
        if (!TryReadCashBalance(out var balance)) return Release1WrongAddressDeliveryStatus.Rejected;

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
            if (!atExpected) return Release1WrongAddressDeliveryStatus.Ambiguous;
            if (capturedRevision > _story.LastPersistedRevision)
                return Release1WrongAddressDeliveryStatus.AwaitingAppliedSave;
            var authorization = effect.AuthorizedStoryCorrelationId;
            if (string.IsNullOrEmpty(authorization)) return Release1WrongAddressDeliveryStatus.Ambiguous;
            return _story.TryCommitNativeEffect(effect.EffectId, authorization, capturedRevision).Accepted
                ? Release1WrongAddressDeliveryStatus.Committed
                : Release1WrongAddressDeliveryStatus.Rejected;
        }

        return Release1WrongAddressDeliveryStatus.Committed;
    }

    private Release1WrongAddressDeliveryStatus MarkRewardApplied(Release1NativeEffectJournalEntry effect) =>
        _story.TryMarkNativeEffectApplied(
            effect.EffectId,
            $"wrong-address-reward-native-v1-a{effect.Attempt}",
            Release1NativeEffectPersistenceMode.RevertTolerant).Accepted
            ? Release1WrongAddressDeliveryStatus.Paid
            : Release1WrongAddressDeliveryStatus.Rejected;

    private Release1WrongAddressDeliveryStatus BlockConsumption(Release1NativeEffectJournalEntry effect)
    {
        if (effect.Phase == Release1NativeEffectPhase.Prepared && !effect.ExecutionBlocked)
            _story.TryRecordNativeEffectIssuance(effect.EffectId, Release1NativeEffectIssuanceOutcome.Ambiguous);
        ReleaseSlotLock();
        return Release1WrongAddressDeliveryStatus.Ambiguous;
    }

    private Release1WrongAddressDeliveryStatus BlockReward(Release1NativeEffectJournalEntry effect)
    {
        if (effect.Phase == Release1NativeEffectPhase.Prepared && !effect.ExecutionBlocked)
            _story.TryRecordNativeEffectIssuance(effect.EffectId, Release1NativeEffectIssuanceOutcome.Ambiguous);
        return Release1WrongAddressDeliveryStatus.Ambiguous;
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

    private static string RewardEffectId(int attempt) => $"wrong-address-reward-v1-a{attempt}";

    private void RefreshDropBinding()
    {
        if (!_loadActive || _disposed || !TryGetActiveStage(out _, out var assignment, out _))
        {
            DisposeDropSubscription();
            return;
        }
        var target = Progress(assignment.Attempt)?.Custody == true
            ? assignment.HandoffDropGuid
            : assignment.SourceDropGuid;
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
    /// The assignment mode that a persisted <see cref="Release1WrongAddressAssignment"/> must carry
    /// for the given mission state, when one is expected to already exist for the current attempt.
    /// Offer-type states (<c>MakeGoodOffered</c>, <c>RecoveryAvailable</c>) map to the mode of the
    /// stage that would be created on acceptance, not one already on file, so a stale prior-stage
    /// assignment at the same attempt number never matches here. States with no persisted assignment
    /// expectation (<c>Offered</c>, <c>Satisfied</c>, and the rest) return null; callers treat null as
    /// "match by attempt alone" or "no active stage", per their own needs.
    /// </summary>
    internal static Release1WrongAddressAssignmentMode? ExpectedAssignmentMode(Release1MissionState missionState) => missionState switch
    {
        Release1MissionState.Accepted or Release1MissionState.Active => Release1WrongAddressAssignmentMode.Primary,
        Release1MissionState.MakeGoodOffered or Release1MissionState.MakeGoodActive => Release1WrongAddressAssignmentMode.MakeGood,
        Release1MissionState.RecoveryAvailable or Release1MissionState.RecoveryActive => Release1WrongAddressAssignmentMode.Recovery,
        _ => null
    };

    private bool TryGetActiveStage(
        out Release1MissionRecord mission,
        out Release1WrongAddressAssignment assignment,
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
        assignment = state.WrongAddressAssignments.SingleOrDefault(candidate =>
            candidate.Attempt == missionAttempt && candidate.Mode == expectedMode.Value)!;
        if (assignment is null) return false;
        authorization = expectedMode == Release1WrongAddressAssignmentMode.Primary
            ? mission.AcceptedLogicalCorrelations.LastOrDefault(value =>
                Release1LogicalCorrelation.TryParse(value, out var correlation) &&
                correlation.TransitionKind == Release1TransitionKind.MissionActivated) ?? string.Empty
            : assignment.AuthorizationCorrelationId;
        return !string.IsNullOrEmpty(authorization);
    }

    private Release1WrongAddressProgress? Progress(int attempt) =>
        _story.State?.WrongAddressProgress.SingleOrDefault(progress => progress.Attempt == attempt);

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

    private void ReportHoldOnce(int attempt, string message)
    {
        if (!_reportedHolds.Add($"a{attempt}:{message}")) return;
        _log($"Wrong Address attempt {attempt} is holding: {message}");
    }

    private static Release1ConsignmentFingerprint Fingerprint(Release1WrongAddressAssignment assignment) =>
        new(assignment.ProductId, assignment.PackagingId, assignment.PackageQuantity);

    private static string ConsumptionEffectId(int attempt) => $"wrong-address-cargo-v1-a{attempt}";
}
