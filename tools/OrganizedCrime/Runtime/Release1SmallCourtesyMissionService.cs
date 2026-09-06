using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class Release1SmallCourtesyMissionService : IDisposable
{
    private const string TermsVersion = "small-courtesy-v1";
    private const double TimedStageDurationHours = 24d;

    private readonly Release1StoryRuntimeService _story;
    private readonly IRelease1SmallCourtesyWorld _world;
    private bool _loadActive;
    private bool _saving;
    private bool _dismissed;
    private bool _disposed;
    private Release1SmallCourtesyQuote? _reviewedQuote;
    private IRelease1SmallCourtesyDropSubscription? _dropSubscription;
    private string? _boundDropGuid;
    private string? _lockedDropGuid;
    private int _lockedSlotIndex = -1;

    public Release1SmallCourtesyMissionService(
        Release1StoryRuntimeService story,
        IRelease1SmallCourtesyWorld world)
    {
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public Release1SmallCourtesyOfferStatus OfferStatus { get; private set; } = Release1SmallCourtesyOfferStatus.Inactive;
    public Release1SmallCourtesyQuote? ReviewedQuote => _reviewedQuote;

    public void OnLoadComplete()
    {
        if (_disposed || _loadActive) return;
        _loadActive = true;
        _saving = false;
        _dismissed = false;
        _reviewedQuote = null;
        RefreshOfferStatus();
        RefreshDepositBinding();
        ReconcileDeposit();
        ReconcileReward();
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
        OfferStatus = Release1SmallCourtesyOfferStatus.Inactive;
    }

    public void OnSaveStart()
    {
        if (_disposed || !_loadActive) return;
        _saving = true;
        OfferStatus = Release1SmallCourtesyOfferStatus.Saving;
    }

    public void OnSaveComplete()
    {
        if (_disposed || !_loadActive) return;
        _saving = false;
        RefreshOfferStatus();
        RefreshDepositBinding();
        ReconcileDeposit();
        ReconcileReward();
    }

    public void Update()
    {
        if (_disposed || !_loadActive || _saving) return;
        var contextStatus = ReadMatchingContext(out _);
        if (contextStatus != Release1SmallCourtesyReviewStatus.Ready)
        {
            OfferStatus = Map(contextStatus);
            return;
        }

        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted) return;
        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        if (mission.State is not (Release1MissionState.Active or Release1MissionState.MakeGoodActive) ||
            mission.DeadlineGameTimeHours is null)
        {
            RefreshOfferStatus();
            RefreshDepositBinding();
            ReconcileDeposit();
            ReconcileReward();
            return;
        }
        if (!TryReadGameHours(out var gameHours) || gameHours < mission.DeadlineGameTimeHours.Value)
        {
            RefreshDepositBinding();
            ReconcileDeposit();
            ReconcileReward();
            return;
        }

        if (HasCargoEffect(mission.Attempt))
        {
            RefreshDepositBinding();
            ReconcileDeposit();
            ReconcileReward();
            return;
        }

        var transition = mission.State == Release1MissionState.Active
            ? Release1TransitionKind.RequiredFailure
            : Release1TransitionKind.MakeGoodFailed;
        var receipt = $"small-courtesy-deadline-{ModeToken(mission.State)}-v1-a{mission.Attempt}";
        var command = CreateCommand(
            transition,
            mission.Attempt,
            receipt,
            termsVersion: null,
            acceptedHours: null,
            deadlineHours: null);
        _story.TryExecuteDurably(command);
        RefreshOfferStatus();
        RefreshDepositBinding();
        ReconcileDeposit();
        ReconcileReward();
    }

    public Release1SmallCourtesyReviewResult TryReview()
    {
        if (_disposed) return Review(Release1SmallCourtesyReviewStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive) return Review(Release1SmallCourtesyReviewStatus.Inactive, "No load epoch is active.");
        if (_saving) return Review(Release1SmallCourtesyReviewStatus.Saving, "Mission review is deferred during saving.");
        if (_dismissed) return Review(Release1SmallCourtesyReviewStatus.Dismissed, "Offer was dismissed for this load.");

        var status = TryBuildQuote(out var quote);
        if (status != Release1SmallCourtesyReviewStatus.Ready)
        {
            _reviewedQuote = null;
            OfferStatus = Map(status);
            return Review(status, Message(status));
        }

        _reviewedQuote = quote;
        OfferStatus = Release1SmallCourtesyOfferStatus.Available;
        return new(Release1SmallCourtesyReviewStatus.Ready, quote, "Small Courtesy terms are ready for review.");
    }

    public Release1SmallCourtesyDecisionResult TryAccept()
    {
        if (_disposed) return Decision(Release1SmallCourtesyDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive || _dismissed) return Decision(Release1SmallCourtesyDecisionStatus.Inactive, "No active offer is available.");
        if (_saving) return Decision(Release1SmallCourtesyDecisionStatus.PersistenceDeferred, "Acceptance is deferred during saving.");
        if (_reviewedQuote is null) return Decision(Release1SmallCourtesyDecisionStatus.ReviewRequired, "Review the current terms before accepting.");

        var status = TryBuildQuote(out var currentQuote);
        if (status != Release1SmallCourtesyReviewStatus.Ready || currentQuote != _reviewedQuote)
        {
            _reviewedQuote = null;
            RefreshOfferStatus();
            return Decision(Release1SmallCourtesyDecisionStatus.QuoteChanged, "The available product, package, or drop changed; review again.");
        }
        if (!TryReadGameHours(out var acceptedHours))
            return Decision(Release1SmallCourtesyDecisionStatus.Rejected, "Canonical game time was unavailable.");

        var assignment = currentQuote.Assignment;
        var transition = assignment.Mode switch
        {
            Release1SmallCourtesyAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1SmallCourtesyAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1SmallCourtesyAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new InvalidOperationException("Assignment mode was not defined.")
        };
        var receipt = AcceptanceReceipt(assignment.Mode, assignment.Attempt);
        var command = CreateCommand(
            transition,
            assignment.Attempt,
            receipt,
            assignment.Mode == Release1SmallCourtesyAssignmentMode.Primary ? TermsVersion : null,
            acceptedHours,
            assignment.Mode == Release1SmallCourtesyAssignmentMode.Recovery ? null : acceptedHours + TimedStageDurationHours);
        var result = _story.TryExecuteSmallCourtesyAcceptanceDurably(command, assignment);
        if (result.Status == Release1StoryCommandStatus.DeferredSaving)
            return Decision(Release1SmallCourtesyDecisionStatus.PersistenceDeferred, result.Message);
        if (!result.Accepted)
            return Decision(Release1SmallCourtesyDecisionStatus.Rejected, result.Message);

        _reviewedQuote = null;
        RefreshOfferStatus();
        RefreshDepositBinding();
        return Decision(Release1SmallCourtesyDecisionStatus.Accepted, "Small Courtesy stage was accepted.");
    }

    public Release1SmallCourtesyDecisionResult TryDefer()
    {
        if (_disposed) return Decision(Release1SmallCourtesyDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive || _saving || ReadMatchingContext(out _) != Release1SmallCourtesyReviewStatus.Ready)
            return Decision(Release1SmallCourtesyDecisionStatus.Inactive, "No active offer is available.");
        var mission = _story.State?.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        if (_story.State?.RelationshipState != Release1RelationshipState.Accepted || mission?.State != Release1MissionState.Offered)
            return TryDismiss();

        var receipt = $"small-courtesy-defer-v1-a{mission.Attempt}";
        var result = _story.TryExecuteDurably(CreateCommand(
            Release1TransitionKind.MissionDeferred,
            mission.Attempt,
            receipt,
            null,
            null,
            null));
        if (result.Status == Release1StoryCommandStatus.DeferredSaving)
            return Decision(Release1SmallCourtesyDecisionStatus.PersistenceDeferred, result.Message);
        if (!result.Accepted)
            return Decision(Release1SmallCourtesyDecisionStatus.Rejected, result.Message);

        DismissForLoad();
        return Decision(Release1SmallCourtesyDecisionStatus.Deferred, "Small Courtesy was deferred until a later load.");
    }

    public Release1SmallCourtesyDecisionResult TryDismiss()
    {
        if (_disposed) return Decision(Release1SmallCourtesyDecisionStatus.Disposed, "Mission service was disposed.");
        if (!_loadActive) return Decision(Release1SmallCourtesyDecisionStatus.Inactive, "No load epoch is active.");
        DismissForLoad();
        return Decision(Release1SmallCourtesyDecisionStatus.Dismissed, "Small Courtesy was dismissed for this load.");
    }

    public Release1SmallCourtesyDepositStatus TryHandleDropClosed(string deadDropGuid)
    {
        if (_disposed || !_loadActive || _saving || ReadMatchingContext(out _) != Release1SmallCourtesyReviewStatus.Ready)
            return Release1SmallCourtesyDepositStatus.Rejected;
        if (!TryGetCurrentDeposit(out var mission, out var assignment, out var authorization))
            return Release1SmallCourtesyDepositStatus.NoWork;
        if (!string.Equals(deadDropGuid, assignment.DeadDropGuid, StringComparison.Ordinal))
            return Release1SmallCourtesyDepositStatus.NoWork;
        if (mission.State is Release1MissionState.Active or Release1MissionState.MakeGoodActive &&
            (mission.DeadlineGameTimeHours is null ||
             !TryReadGameHours(out var currentGameHours) ||
             currentGameHours >= mission.DeadlineGameTimeHours.Value))
            return Release1SmallCourtesyDepositStatus.NoWork;

        var effectId = CargoEffectId(mission.Attempt);
        if (_story.State!.NativeEffects.Any(effect => effect.EffectId == effectId))
            return ReconcileDeposit();
        if (!TryReadSlots(assignment.DeadDropGuid, out var slots))
            return Release1SmallCourtesyDepositStatus.Rejected;
        var slot = slots
            .Where(candidate =>
                candidate.Quantity > 0 &&
                candidate.IsPackaged &&
                string.Equals(candidate.ProductId, assignment.ProductId, StringComparison.Ordinal) &&
                string.Equals(candidate.PackagingId, assignment.PackagingId, StringComparison.Ordinal))
            .OrderBy(candidate => candidate.SlotIndex)
            .FirstOrDefault();
        if (slot is null) return Release1SmallCourtesyDepositStatus.NoWork;

        var lockRetained = false;
        try
        {
            var identity = new Release1SmallCourtesyCargoIdentity(
                assignment.ProductId,
                assignment.PackagingId,
                slot.SlotIndex,
                slot.Quantity,
                slot.Quantity - 1,
                slot.MonetaryValue);
            var effect = new Release1NativeEffectJournalEntry(
                effectId,
                Release1MissionCatalog.SmallCourtesy,
                mission.Attempt,
                "CargoTransfer",
                assignment.DeadDropGuid,
                "oc-small-courtesy",
                identity.Serialize(),
                Release1NativeEffectPhase.Prepared,
                null,
                _story.State.Revision + 1,
                AuthorizedStoryCorrelationId: authorization,
                AuthorizedMissionRevision: mission.Revision);
            if (!TryAcquireSlotLock(assignment.DeadDropGuid, slot.SlotIndex))
                return Release1SmallCourtesyDepositStatus.Rejected;
            var result = _story.TryPrepareNativeEffect(effect);
            lockRetained = result.Accepted;
            return result.Accepted
                ? Release1SmallCourtesyDepositStatus.AwaitingPreparedSave
                : Release1SmallCourtesyDepositStatus.Rejected;
        }
        catch (ArgumentException)
        {
            return Release1SmallCourtesyDepositStatus.Rejected;
        }
        finally
        {
            if (!lockRetained) ReleaseSlotLock();
        }
    }

    public Release1SmallCourtesyDepositStatus ReconcileDeposit()
    {
        if (_disposed || !_loadActive || _saving || ReadMatchingContext(out _) != Release1SmallCourtesyReviewStatus.Ready)
            return Release1SmallCourtesyDepositStatus.NoWork;
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted)
            return Release1SmallCourtesyDepositStatus.NoWork;
        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        var effect = state.NativeEffects.FirstOrDefault(candidate => candidate.EffectId == CargoEffectId(mission.Attempt));
        if (effect is null) return Release1SmallCourtesyDepositStatus.NoWork;
        var assignment = state.SmallCourtesyAssignments.SingleOrDefault(candidate => candidate.Attempt == mission.Attempt);
        var authorization = effect.AuthorizedStoryCorrelationId;
        if (assignment is null || string.IsNullOrEmpty(authorization))
            return BlockPreparedEffect(effect);
        if (!Release1SmallCourtesyCargoIdentity.TryParse(effect.AmountOrCargoIdentity, out var identity) ||
            identity is null ||
            effect.Attempt != mission.Attempt ||
            !string.Equals(effect.EffectKind, "CargoTransfer", StringComparison.Ordinal) ||
            !string.Equals(effect.SourceIdentity, assignment.DeadDropGuid, StringComparison.Ordinal) ||
            !string.Equals(effect.DestinationIdentity, "oc-small-courtesy", StringComparison.Ordinal) ||
            !string.Equals(identity.ProductId, assignment.ProductId, StringComparison.Ordinal) ||
            !string.Equals(identity.PackageId, assignment.PackagingId, StringComparison.Ordinal))
            return BlockPreparedEffect(effect);
        if (effect.ExecutionBlocked) return Release1SmallCourtesyDepositStatus.Ambiguous;
        if (!TryReadSlot(assignment.DeadDropGuid, identity.SlotIndex, out var slot))
            return Release1SmallCourtesyDepositStatus.Rejected;

        var exactPre = MatchesCargoSlot(slot, identity, identity.PreQuantity);
        var exactPost = MatchesCargoSlot(slot, identity, identity.PostQuantity);
        if (effect.Phase == Release1NativeEffectPhase.Prepared)
        {
            if (effect.PreparedStoryRevision > _story.LastPersistedRevision)
                return Release1SmallCourtesyDepositStatus.AwaitingPreparedSave;
            if (exactPost)
            {
                var inferred = MarkCargoApplied(effect, authorization);
                ReleaseSlotLock();
                return inferred;
            }
            if (!exactPre)
                return BlockPreparedEffect(effect);
            return ApplyPreparedCargo(effect, assignment, identity, authorization);
        }
        if (effect.Phase == Release1NativeEffectPhase.Applied)
        {
            if (!exactPost) return Release1SmallCourtesyDepositStatus.Ambiguous;
            if (!TryCompleteMissionAndPrepareReward(effect, identity))
                return Release1SmallCourtesyDepositStatus.Rejected;
            if (_story.State!.Revision > _story.LastPersistedRevision)
                return Release1SmallCourtesyDepositStatus.AwaitingAppliedSave;
            var committed = _story.TryCommitNativeEffect(effect.EffectId, authorization);
            return committed.Accepted
                ? Release1SmallCourtesyDepositStatus.Committed
                : Release1SmallCourtesyDepositStatus.Rejected;
        }
        return Release1SmallCourtesyDepositStatus.Committed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        OnPreLoad();
        _disposed = true;
        OfferStatus = Release1SmallCourtesyOfferStatus.Disposed;
    }

    private Release1SmallCourtesyDepositStatus ApplyPreparedCargo(
        Release1NativeEffectJournalEntry effect,
        Release1SmallCourtesyAssignment assignment,
        Release1SmallCourtesyCargoIdentity identity,
        string authorization)
    {
        if (!TryAcquireSlotLock(assignment.DeadDropGuid, identity.SlotIndex))
            return Release1SmallCourtesyDepositStatus.Rejected;
        try
        {
            Release1SmallCourtesyWorldMutationStatus changed;
            try { changed = _world.TryChangeSlotQuantity(assignment.DeadDropGuid, identity.SlotIndex, -1); }
            catch { return BlockPreparedEffect(effect); }
            if (changed != Release1SmallCourtesyWorldMutationStatus.Succeeded)
                return changed == Release1SmallCourtesyWorldMutationStatus.Unavailable
                    ? Release1SmallCourtesyDepositStatus.Rejected
                    : BlockPreparedEffect(effect);
            if (!TryReadSlot(assignment.DeadDropGuid, identity.SlotIndex, out var post) ||
                !MatchesCargoSlot(post, identity, identity.PostQuantity))
                return BlockPreparedEffect(effect);
            return MarkCargoApplied(effect, authorization);
        }
        finally
        {
            ReleaseSlotLock();
        }
    }

    private Release1SmallCourtesyDepositStatus MarkCargoApplied(
        Release1NativeEffectJournalEntry effect,
        string authorization)
    {
        if (!string.Equals(effect.AuthorizedStoryCorrelationId, authorization, StringComparison.Ordinal))
            return BlockPreparedEffect(effect);
        var receipt = $"small-courtesy-cargo-native-v1-a{effect.Attempt}";
        var result = _story.TryMarkNativeEffectApplied(effect.EffectId, receipt);
        if (!result.Accepted)
            return Release1SmallCourtesyDepositStatus.Rejected;
        var applied = _story.State!.NativeEffects.Single(candidate => candidate.EffectId == effect.EffectId);
        if (Release1SmallCourtesyCargoIdentity.TryParse(applied.AmountOrCargoIdentity, out var identity) && identity is not null)
            _ = TryCompleteMissionAndPrepareReward(applied, identity);
        return Release1SmallCourtesyDepositStatus.Applied;
    }

    public Release1SmallCourtesyRewardStatus ReconcileReward()
    {
        if (_disposed || !_loadActive || _saving || ReadMatchingContext(out _) != Release1SmallCourtesyReviewStatus.Ready)
            return Release1SmallCourtesyRewardStatus.NoWork;
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted)
            return Release1SmallCourtesyRewardStatus.NoWork;
        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        var cargo = state.NativeEffects.FirstOrDefault(candidate => candidate.EffectId == CargoEffectId(mission.Attempt));
        if (cargo is null || cargo.ExecutionBlocked || cargo.Phase != Release1NativeEffectPhase.Committed)
            return Release1SmallCourtesyRewardStatus.NoWork;
        var effect = state.NativeEffects.FirstOrDefault(candidate => candidate.EffectId == RewardEffectId(mission.Attempt));
        if (effect is null) return Release1SmallCourtesyRewardStatus.NoWork;
        if (!Release1SmallCourtesyRewardIdentity.TryParse(effect.AmountOrCargoIdentity, out var identity) ||
            identity is null ||
            effect.Attempt != mission.Attempt ||
            !string.Equals(effect.EffectKind, "Reward", StringComparison.Ordinal) ||
            !string.Equals(effect.SourceIdentity, "oc-small-courtesy", StringComparison.Ordinal) ||
            !string.Equals(effect.DestinationIdentity, "player-cash", StringComparison.Ordinal))
            return BlockRewardEffect(effect);
        if (effect.ExecutionBlocked) return Release1SmallCourtesyRewardStatus.Ambiguous;
        if (!TryReadCashBalance(out var balance)) return Release1SmallCourtesyRewardStatus.Rejected;

        var atBaseline = WithinCashTolerance(balance, identity.BaselineCash, identity.VerificationTolerance);
        var atExpected = WithinCashTolerance(balance, identity.ExpectedCash, identity.VerificationTolerance);
        if (effect.Phase == Release1NativeEffectPhase.Prepared)
        {
            if (effect.PreparedStoryRevision > _story.LastPersistedRevision)
                return Release1SmallCourtesyRewardStatus.AwaitingPreparedSave;
            if (atExpected)
                return MarkRewardApplied(effect);
            if (!atBaseline)
                return BlockRewardEffect(effect);
            return ApplyPreparedReward(effect, identity);
        }
        if (effect.Phase == Release1NativeEffectPhase.Applied)
        {
            if (_story.State!.Revision > _story.LastPersistedRevision)
                return Release1SmallCourtesyRewardStatus.AwaitingAppliedSave;
            if (!atExpected) return Release1SmallCourtesyRewardStatus.Ambiguous;
            var authorization = effect.AuthorizedStoryCorrelationId;
            if (string.IsNullOrEmpty(authorization)) return Release1SmallCourtesyRewardStatus.Ambiguous;
            var committed = _story.TryCommitNativeEffect(effect.EffectId, authorization);
            return committed.Accepted
                ? Release1SmallCourtesyRewardStatus.Committed
                : Release1SmallCourtesyRewardStatus.Rejected;
        }
        return Release1SmallCourtesyRewardStatus.Committed;
    }

    private bool TryCompleteMissionAndPrepareReward(
        Release1NativeEffectJournalEntry cargoEffect,
        Release1SmallCourtesyCargoIdentity cargoIdentity)
    {
        var state = _story.State;
        if (state is null) return false;
        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        var existingReward = state.NativeEffects.FirstOrDefault(effect => effect.EffectId == RewardEffectId(mission.Attempt));
        if (mission.State == Release1MissionState.Satisfied)
            return existingReward is not null;
        if (mission.State is not (Release1MissionState.Active or Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive) ||
            cargoEffect.Phase != Release1NativeEffectPhase.Applied ||
            cargoEffect.ExecutionBlocked ||
            cargoEffect.Attempt != mission.Attempt ||
            existingReward is not null ||
            !TryReadCashBalance(out var baseline))
            return false;

        var quoted = Release1RewardMath.QuoteWholeDollars(cargoIdentity.DepositedMonetaryValue);
        if (quoted is not float amount)
            return false;
        Release1SmallCourtesyRewardIdentity rewardIdentity;
        try
        {
            rewardIdentity = new(baseline, amount, baseline + amount);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!_story.TryGetActiveContext(out var context, out _)) return false;
        var completionReceipt = $"small-courtesy-complete-v1-a{mission.Attempt}";
        var completionCorrelation = Release1LogicalCorrelation.Create(
            context.PlayerId,
            Release1MissionCatalog.SmallCourtesy,
            mission.Attempt,
            Release1TransitionKind.MissionCompleted,
            completionReceipt).Value;
        var reward = new Release1NativeEffectJournalEntry(
            RewardEffectId(mission.Attempt),
            Release1MissionCatalog.SmallCourtesy,
            mission.Attempt,
            "Reward",
            "oc-small-courtesy",
            "player-cash",
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
            Release1MissionCatalog.SmallCourtesy,
            mission.Attempt,
            Release1TransitionKind.MissionCompleted,
            completionReceipt,
            completionCorrelation,
            CompletionTiming: mission.State == Release1MissionState.Active
                ? Release1CompletionTiming.OnTime
                : Release1CompletionTiming.Late,
            RewardAuthorizationReceiptId: $"small-courtesy-reward-auth-v1-a{mission.Attempt}",
            PreparedNativeEffect: reward);
        return _story.TryExecute(command).Accepted;
    }

    private Release1SmallCourtesyRewardStatus ApplyPreparedReward(
        Release1NativeEffectJournalEntry effect,
        Release1SmallCourtesyRewardIdentity identity)
    {
        Release1SmallCourtesyWorldMutationStatus changed;
        try { changed = _world.TryChangeCashBalance(identity.WholeDollarAmount); }
        catch { return BlockRewardEffect(effect); }
        if (changed != Release1SmallCourtesyWorldMutationStatus.Succeeded)
            return changed == Release1SmallCourtesyWorldMutationStatus.Unavailable
                ? Release1SmallCourtesyRewardStatus.Rejected
                : BlockRewardEffect(effect);
        if (!TryReadCashBalance(out var post) ||
            !WithinCashTolerance(post, identity.ExpectedCash, identity.VerificationTolerance))
            return BlockRewardEffect(effect);
        return MarkRewardApplied(effect);
    }

    private Release1SmallCourtesyRewardStatus MarkRewardApplied(Release1NativeEffectJournalEntry effect)
    {
        var receipt = $"small-courtesy-reward-native-v1-a{effect.Attempt}";
        var result = _story.TryMarkNativeEffectApplied(effect.EffectId, receipt);
        return result.Accepted
            ? Release1SmallCourtesyRewardStatus.Applied
            : Release1SmallCourtesyRewardStatus.Rejected;
    }

    private Release1SmallCourtesyRewardStatus BlockRewardEffect(Release1NativeEffectJournalEntry effect)
    {
        if (effect.Phase == Release1NativeEffectPhase.Prepared && !effect.ExecutionBlocked)
            _story.TryRecordNativeEffectIssuance(effect.EffectId, Release1NativeEffectIssuanceOutcome.Ambiguous);
        return Release1SmallCourtesyRewardStatus.Ambiguous;
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

    private static bool WithinCashTolerance(float actual, float expected, float tolerance) =>
        float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance;

    private Release1SmallCourtesyDepositStatus BlockPreparedEffect(Release1NativeEffectJournalEntry effect)
    {
        if (effect.Phase == Release1NativeEffectPhase.Prepared && !effect.ExecutionBlocked)
            _story.TryRecordNativeEffectIssuance(effect.EffectId, Release1NativeEffectIssuanceOutcome.Ambiguous);
        ReleaseSlotLock();
        return Release1SmallCourtesyDepositStatus.Ambiguous;
    }

    private void RefreshDepositBinding()
    {
        if (!_loadActive || _disposed || !TryGetCurrentDeposit(out _, out var assignment, out _))
        {
            DisposeDropSubscription();
            return;
        }
        if (_dropSubscription is not null && string.Equals(_boundDropGuid, assignment.DeadDropGuid, StringComparison.Ordinal))
            return;
        DisposeDropSubscription();
        try
        {
            var status = _world.TrySubscribeDeadDropClosed(
                assignment.DeadDropGuid,
                TryHandleDropClosedCallback,
                out var subscription);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready || subscription is null ||
                !string.Equals(subscription.DeadDropGuid, assignment.DeadDropGuid, StringComparison.Ordinal))
            {
                subscription?.Dispose();
                return;
            }
            _dropSubscription = subscription;
            _boundDropGuid = assignment.DeadDropGuid;
        }
        catch
        {
            DisposeDropSubscription();
        }
    }

    private void TryHandleDropClosedCallback(string deadDropGuid) =>
        _ = TryHandleDropClosed(deadDropGuid);

    private bool TryGetCurrentDeposit(
        out Release1MissionRecord mission,
        out Release1SmallCourtesyAssignment assignment,
        out string authorization)
    {
        mission = null!;
        assignment = null!;
        authorization = string.Empty;
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted) return false;
        mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        var expectedMode = mission.State switch
        {
            Release1MissionState.Active => Release1SmallCourtesyAssignmentMode.Primary,
            Release1MissionState.MakeGoodActive => Release1SmallCourtesyAssignmentMode.MakeGood,
            Release1MissionState.RecoveryActive => Release1SmallCourtesyAssignmentMode.Recovery,
            _ => (Release1SmallCourtesyAssignmentMode?)null
        };
        if (expectedMode is null) return false;
        var missionAttempt = mission.Attempt;
        assignment = state.SmallCourtesyAssignments.SingleOrDefault(candidate =>
            candidate.Attempt == missionAttempt && candidate.Mode == expectedMode.Value)!;
        if (assignment is null) return false;
        if (expectedMode == Release1SmallCourtesyAssignmentMode.Primary)
        {
            authorization = mission.AcceptedLogicalCorrelations.LastOrDefault(value =>
                Release1LogicalCorrelation.TryParse(value, out var correlation) &&
                correlation.TransitionKind == Release1TransitionKind.MissionActivated) ?? string.Empty;
        }
        else
        {
            authorization = assignment.AuthorizationCorrelationId;
        }
        return !string.IsNullOrEmpty(authorization);
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

    private bool TryReadSlot(string deadDropGuid, int slotIndex, out Release1SmallCourtesySlotSnapshot slot)
    {
        slot = null!;
        if (!TryReadSlots(deadDropGuid, out var slots)) return false;
        slot = slots.SingleOrDefault(candidate => candidate.SlotIndex == slotIndex)!;
        return slot is not null;
    }

    private bool TryAcquireSlotLock(string deadDropGuid, int slotIndex)
    {
        if (_lockedDropGuid is not null)
            return string.Equals(_lockedDropGuid, deadDropGuid, StringComparison.Ordinal) &&
                _lockedSlotIndex == slotIndex;
        _lockedDropGuid = deadDropGuid;
        _lockedSlotIndex = slotIndex;
        try
        {
            var status = _world.TrySetSlotLocked(deadDropGuid, slotIndex, true);
            if (status != Release1SmallCourtesyWorldMutationStatus.Succeeded)
            {
                if (status == Release1SmallCourtesyWorldMutationStatus.Ambiguous)
                    ReleaseSlotLock();
                else
                {
                    _lockedDropGuid = null;
                    _lockedSlotIndex = -1;
                }
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

    private void DisposeDropSubscription()
    {
        try { _dropSubscription?.Dispose(); }
        catch { }
        _dropSubscription = null;
        _boundDropGuid = null;
    }

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

    private static string CargoEffectId(int attempt) => $"small-courtesy-cargo-v1-a{attempt}";

    private bool HasCargoEffect(int attempt) =>
        _story.State?.NativeEffects.Any(effect => effect.EffectId == CargoEffectId(attempt)) == true;

    private static string RewardEffectId(int attempt) => $"small-courtesy-reward-v1-a{attempt}";

    private Release1SmallCourtesyReviewStatus TryBuildQuote(out Release1SmallCourtesyQuote? quote)
    {
        try { return TryBuildQuoteCore(out quote); }
        catch
        {
            quote = null;
            return Release1SmallCourtesyReviewStatus.Unavailable;
        }
    }

    private Release1SmallCourtesyReviewStatus TryBuildQuoteCore(out Release1SmallCourtesyQuote? quote)
    {
        quote = null;
        var contextStatus = ReadMatchingContext(out var context);
        if (contextStatus != Release1SmallCourtesyReviewStatus.Ready) return contextStatus;
        var state = _story.State;
        if (state is null || state.RelationshipState != Release1RelationshipState.Accepted)
            return Release1SmallCourtesyReviewStatus.Ineligible;
        var mission = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
        var mode = mission.State switch
        {
            Release1MissionState.Offered => Release1SmallCourtesyAssignmentMode.Primary,
            Release1MissionState.MakeGoodOffered => Release1SmallCourtesyAssignmentMode.MakeGood,
            Release1MissionState.RecoveryAvailable => Release1SmallCourtesyAssignmentMode.Recovery,
            _ => (Release1SmallCourtesyAssignmentMode?)null
        };
        if (mode is null) return Release1SmallCourtesyReviewStatus.Ineligible;

        var attempt = mode == Release1SmallCourtesyAssignmentMode.Primary ? mission.Attempt : mission.Attempt + 1;
        var transition = mode switch
        {
            Release1SmallCourtesyAssignmentMode.Primary => Release1TransitionKind.MissionAccepted,
            Release1SmallCourtesyAssignmentMode.MakeGood => Release1TransitionKind.MakeGoodAccepted,
            Release1SmallCourtesyAssignmentMode.Recovery => Release1TransitionKind.RecoveryAccepted,
            _ => throw new InvalidOperationException("Assignment mode was not defined.")
        };
        var receipt = AcceptanceReceipt(mode.Value, attempt);
        var correlation = Release1LogicalCorrelation.Create(
            context.PlayerId,
            Release1MissionCatalog.SmallCourtesy,
            attempt,
            transition,
            receipt).Value;
        var packageKind = mode == Release1SmallCourtesyAssignmentMode.Primary
            ? Release1SmallCourtesyPackageKind.Brick
            : Release1SmallCourtesyPackageKind.Jar;
        if (_world.TryReadPackaging(packageKind, out var packaging) != Release1SmallCourtesyWorldReadStatus.Ready)
            return Release1SmallCourtesyReviewStatus.Unavailable;
        try { packaging.Validate(); }
        catch (ArgumentException) { return Release1SmallCourtesyReviewStatus.Unavailable; }

        IReadOnlyList<Release1SmallCourtesyProductCandidate> products;
        if (mode == Release1SmallCourtesyAssignmentMode.Primary)
        {
            var productStatus = _world.TryReadProducts(out products);
            if (productStatus != Release1SmallCourtesyWorldReadStatus.Ready) return Map(productStatus);
        }
        else
        {
            var frozen = state.SmallCourtesyAssignments.OrderBy(candidate => candidate.Attempt).FirstOrDefault();
            if (frozen is null) return Release1SmallCourtesyReviewStatus.Ineligible;
            products = new[]
            {
                new Release1SmallCourtesyProductCandidate(
                    frozen.ProductId,
                    frozen.ProductName,
                    frozen.SelectionAskingPrice,
                    true)
            };
        }

        var dropStatus = _world.TryReadDeadDrops(out var drops);
        if (dropStatus != Release1SmallCourtesyWorldReadStatus.Ready) return Map(dropStatus);
        if (!Release1SmallCourtesyAssignmentSelector.TrySelect(
                products,
                drops,
                mode.Value,
                attempt,
                correlation,
                packaging.PackagingId,
                packaging.PackagingName,
                out var assignment,
                out var selectionStatus))
            return selectionStatus switch
            {
                Release1SmallCourtesyAssignmentSelectionStatus.NoDiscoveredProduct => Release1SmallCourtesyReviewStatus.NoDiscoveredProduct,
                Release1SmallCourtesyAssignmentSelectionStatus.NoEmptyDeadDrop => Release1SmallCourtesyReviewStatus.NoEmptyDeadDrop,
                _ => Release1SmallCourtesyReviewStatus.Unavailable
            };

        quote = new(
            assignment!,
            mode == Release1SmallCourtesyAssignmentMode.Recovery ? null : TimedStageDurationHours);
        quote.Validate();
        return Release1SmallCourtesyReviewStatus.Ready;
    }

    private void RefreshOfferStatus()
    {
        if (_disposed) { OfferStatus = Release1SmallCourtesyOfferStatus.Disposed; return; }
        if (!_loadActive) { OfferStatus = Release1SmallCourtesyOfferStatus.Inactive; return; }
        if (_saving) { OfferStatus = Release1SmallCourtesyOfferStatus.Saving; return; }
        if (_dismissed) { OfferStatus = Release1SmallCourtesyOfferStatus.Dismissed; return; }
        OfferStatus = Map(TryBuildQuote(out _));
    }

    private Release1SmallCourtesyReviewStatus ReadMatchingContext(out Release1StoryHostContextSnapshot context)
    {
        context = default;
        if (!_story.TryGetActiveContext(out var storyContext, out _)) return Release1SmallCourtesyReviewStatus.Inactive;
        try
        {
            var status = _world.TryReadContext(out var worldContext);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready) return Map(status);
            if (!SameContext(storyContext, worldContext)) return Release1SmallCourtesyReviewStatus.Inactive;
            context = storyContext;
            return Release1SmallCourtesyReviewStatus.Ready;
        }
        catch
        {
            return Release1SmallCourtesyReviewStatus.Unavailable;
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
            Release1MissionCatalog.SmallCourtesy,
            attempt,
            transition,
            receipt,
            Release1LogicalCorrelation.Create(
                context.PlayerId,
                Release1MissionCatalog.SmallCourtesy,
                attempt,
                transition,
                receipt).Value,
            termsVersion,
            acceptedHours,
            deadlineHours);
    }

    private void DismissForLoad()
    {
        _dismissed = true;
        _reviewedQuote = null;
        OfferStatus = Release1SmallCourtesyOfferStatus.Dismissed;
    }

    private static string AcceptanceReceipt(Release1SmallCourtesyAssignmentMode mode, int attempt) =>
        $"small-courtesy-{mode.ToString().ToLowerInvariant()}-accept-v1-a{attempt}";

    private static string ModeToken(Release1MissionState state) =>
        state == Release1MissionState.Active ? "primary" : "make-good";

    private static bool SameContext(Release1StoryHostContextSnapshot left, Release1StoryHostContextSnapshot right) =>
        left.SessionEpoch == right.SessionEpoch &&
        left.LoadEpoch == right.LoadEpoch &&
        string.Equals(left.PlayerId, right.PlayerId, StringComparison.Ordinal) &&
        string.Equals(
            Path.GetFullPath(left.ActiveSaveFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right.ActiveSaveFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static Release1SmallCourtesyReviewResult Review(Release1SmallCourtesyReviewStatus status, string message) =>
        new(status, null, message);

    private static Release1SmallCourtesyDecisionResult Decision(Release1SmallCourtesyDecisionStatus status, string message) =>
        new(status, message);

    private static Release1SmallCourtesyReviewStatus Map(Release1SmallCourtesyWorldReadStatus status) => status switch
    {
        Release1SmallCourtesyWorldReadStatus.Pending or Release1SmallCourtesyWorldReadStatus.NotAuthoritative => Release1SmallCourtesyReviewStatus.Pending,
        _ => Release1SmallCourtesyReviewStatus.Unavailable
    };

    private static Release1SmallCourtesyOfferStatus Map(Release1SmallCourtesyReviewStatus status) => status switch
    {
        Release1SmallCourtesyReviewStatus.Ready => Release1SmallCourtesyOfferStatus.Available,
        Release1SmallCourtesyReviewStatus.Inactive => Release1SmallCourtesyOfferStatus.Inactive,
        Release1SmallCourtesyReviewStatus.Ineligible => Release1SmallCourtesyOfferStatus.Ineligible,
        Release1SmallCourtesyReviewStatus.Pending => Release1SmallCourtesyOfferStatus.Pending,
        Release1SmallCourtesyReviewStatus.Saving => Release1SmallCourtesyOfferStatus.Saving,
        Release1SmallCourtesyReviewStatus.NoDiscoveredProduct => Release1SmallCourtesyOfferStatus.NoDiscoveredProduct,
        Release1SmallCourtesyReviewStatus.NoEmptyDeadDrop => Release1SmallCourtesyOfferStatus.NoEmptyDeadDrop,
        Release1SmallCourtesyReviewStatus.Dismissed => Release1SmallCourtesyOfferStatus.Dismissed,
        Release1SmallCourtesyReviewStatus.Disposed => Release1SmallCourtesyOfferStatus.Disposed,
        _ => Release1SmallCourtesyOfferStatus.Unavailable
    };

    private static string Message(Release1SmallCourtesyReviewStatus status) => status switch
    {
        Release1SmallCourtesyReviewStatus.NoDiscoveredProduct => "No discovered product is available for assignment.",
        Release1SmallCourtesyReviewStatus.NoEmptyDeadDrop => "No empty dead drop is available for assignment.",
        Release1SmallCourtesyReviewStatus.Pending => "World eligibility is still pending.",
        Release1SmallCourtesyReviewStatus.Ineligible => "Small Courtesy is not currently offerable.",
        Release1SmallCourtesyReviewStatus.Inactive => "The canonical story context is inactive.",
        _ => "Small Courtesy world data is unavailable."
    };
}
