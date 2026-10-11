using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

// OC-152: the Chapter 3 delivery feature (dead-drop delivery, recovery,
// brick removal, and the job-outcome ledger tick). Moved verbatim out of
// Chapter3RuntimeService; only the receiver changed. Called only while the
// coordinator holds its gate; takes no locks itself. The drop-closed
// callback routes back through the coordinator so the lock path is
// unchanged; the ledger and post-warehouse counters live here because the
// delivery checks feed them.
internal sealed class Chapter3DeliveryFeature
{
    private readonly Chapter3FeatureContext _ctx;
    private readonly Action<string> _onDeadDropClosed;

    public Chapter3DeliveryFeature(Chapter3FeatureContext ctx, Action<string> onDeadDropClosed)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _onDeadDropClosed = onDeadDropClosed ?? throw new ArgumentNullException(nameof(onDeadDropClosed));
    }

    /// <summary>
    /// Stages the frozen brick count into the pickup drop's first slot and
    /// verifies the read-back before the accept lands. Idempotent: a slot that
    /// already holds exactly the staged stack (an ambiguous earlier insert
    /// that landed) is adopted without inserting again, so a retry never
    /// doubles the bricks.
    /// </summary>
    internal bool TryStageRecoveryLocked(
        Chapter3PackageRecovery recovery, out Chapter3PackageRecovery staged, out string reason)
    {
        staged = recovery;
        reason = string.Empty;
        if (!TryReadSlotsLocked(recovery.Pickup.Guid, out var slots))
        {
            reason = "the pickup drop was unreadable";
            return false;
        }
        var slot = slots.SingleOrDefault(candidate => candidate.SlotIndex == 0);
        if (slot is not null && slot.Quantity > 0)
        {
            if (slot.Quantity == recovery.BrickCount && slot.IsPackaged &&
                string.Equals(slot.ProductId, recovery.ProductId, StringComparison.Ordinal) &&
                string.Equals(slot.PackagingId, recovery.PackagingId, StringComparison.Ordinal))
            {
                staged = recovery with { StagedSlotIndex = 0, Staged = true };
                return true;
            }
            reason = "the pickup drop changed before staging";
            return false;
        }
        Release1SmallCourtesyWorldMutationStatus inserted;
        try
        {
            inserted = _ctx.DeadDropWorld!.TryInsertPackagedProduct(
                recovery.Pickup.Guid, 0, recovery.ProductId, recovery.PackagingId,
                recovery.BrickCount, out _);
        }
        catch (Exception exception)
        {
            reason = $"staging faulted ({exception.GetType().Name})";
            return false;
        }
        if (TryReadSlotLocked(recovery.Pickup.Guid, 0, out var post) && post.Quantity == recovery.BrickCount &&
            post.IsPackaged &&
            string.Equals(post.ProductId, recovery.ProductId, StringComparison.Ordinal) &&
            string.Equals(post.PackagingId, recovery.PackagingId, StringComparison.Ordinal))
        {
            staged = recovery with { StagedSlotIndex = 0, Staged = true };
            return true;
        }
        reason = inserted == Release1SmallCourtesyWorldMutationStatus.Succeeded
            ? "staging could not be verified"
            : $"staging was {inserted.ToString().ToLowerInvariant()}";
        return false;
    }

    internal enum Chapter3DeliveryCheckOutcome
    {
        NoWork,
        Held,
        Completed
    }

    internal void OnDeadDropClosedLocked(string deadDropGuid)
    {
        try
        {
            if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return;
            var job = _ctx.State.JobBoard.ActiveJob;
            var isDeliveryDrop = job is not null &&
                (job.JobType is Chapter3JobType.DeadDrop or Chapter3JobType.EmergencySupply) &&
                job.Delivery is not null &&
                string.Equals(job.Delivery.DropGuid, deadDropGuid, StringComparison.Ordinal);
            var isRecoveryDrop = job is not null && job.JobType == Chapter3JobType.PackageRecovery &&
                job.Recovery is not null &&
                (string.Equals(job.Recovery.Pickup.Guid, deadDropGuid, StringComparison.Ordinal) ||
                 string.Equals(job.Recovery.Handoff.Guid, deadDropGuid, StringComparison.Ordinal));
            var isStorageDrop = job?.StorageRun is { } storage &&
                (storage.Route.Pickup.Guid == deadDropGuid || storage.Route.Handoff.Guid == deadDropGuid);
            if (!isDeliveryDrop && !isRecoveryDrop && !isStorageDrop)
                return;
            var chapterDay = _ctx.State.ChapterDayAt(_ctx.State.LastProcessedElapsedDay);
            if (!chapterDay.HasValue)
                return;
            if (isDeliveryDrop)
                CheckDeadDropDeliveryLocked(chapterDay.Value);
            else if (isStorageDrop)
                _ctx.Storage.Poll(chapterDay.Value);
            else
                CheckRecoveryLocked(chapterDay.Value);
        }
        catch (Exception exception)
        {
            _ctx.Log($"Chapter 3 delivery check faulted: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// One delivery pass over the frozen job: reconcile any pending removal
    /// first, else classify a fresh slot read, remove exactly N under the slot
    /// lock, verify the post-quantity, pay the fixed reward, then complete. An
    /// Emergency Supply job runs this same pass but completes with no pay: its
    /// reward is nominal. A confirmed removal skips straight to the credit so
    /// a retry never removes twice; every hold logs once per job and reason
    /// and records nothing.
    /// </summary>
    internal Chapter3DeliveryCheckOutcome CheckDeadDropDeliveryLocked(int chapterDay)
    {
        var job = _ctx.State!.JobBoard.ActiveJob;
        var delivery = job?.Delivery;
        if (job is null || delivery is null ||
            job.JobType is not (Chapter3JobType.DeadDrop or Chapter3JobType.EmergencySupply) ||
            _ctx.DeadDropWorld is null)
            return Chapter3DeliveryCheckOutcome.NoWork;
        PruneDeliveryHoldsLocked(job.JobId);
        if (_ctx.PendingRemoval is not null && _ctx.PendingRemoval.JobId != job.JobId)
            _ctx.PendingRemoval = null;
        if (!delivery.RemovalConfirmed)
        {
            if (!TryReadSlotsLocked(delivery.DropGuid, out var slots))
            {
                ReportDeliveryHoldOnceLocked(job.JobId, "unreadable",
                    $"Chapter 3 job {job.JobId} delivery holding: the drop was unreadable.");
                return Chapter3DeliveryCheckOutcome.Held;
            }
            var confirmed = false;
            if (_ctx.PendingRemoval is not null)
            {
                var pendingOutcome = ReconcilePendingRemovalLocked(job, delivery.BrickCount, slots);
                if (pendingOutcome == Chapter3PendingRemovalOutcome.Held)
                    return Chapter3DeliveryCheckOutcome.Held;
                if (pendingOutcome == Chapter3PendingRemovalOutcome.Confirmed)
                {
                    ConfirmRemovalLocked(job);
                    confirmed = true;
                }
                else
                {
                    _ctx.PendingRemoval = null;
                }
            }
            if (!confirmed && !ClassifyAndRemoveLocked(job, delivery, slots))
                return Chapter3DeliveryCheckOutcome.Held;
        }
        if (job.JobType == Chapter3JobType.EmergencySupply)
        {
            _ctx.Log($"Chapter 3 job {job.JobId} completed with no pay.");
        }
        else if (_ctx.CreditLocked(job.Reward) != Chapter3WalletCredit.Credited)
        {
            ReportDeliveryHoldOnceLocked(job.JobId, "unpaid",
                $"Chapter 3 job {job.JobId} reward ${job.Reward:N0} unpaid; retrying without re-removing the bricks.");
            return Chapter3DeliveryCheckOutcome.Held;
        }
        else
        {
            _ctx.Log($"Chapter 3 job {job.JobId} paid ${job.Reward:N0}.");
        }
        var completed = Chapter3JobScheduler.Complete(_ctx.State.JobBoard, job.JobId, succeeded: true, chapterDay);
        _ctx.State = _ctx.State with { JobBoard = CountPostWarehouseSuccessLocked(completed.Board), Revision = _ctx.State.Revision + 1 };
        foreach (var effect in completed.Effects)
            _ctx.Log(Chapter3FeatureContext.FormatEffect(effect));
        DisposeDropSubscriptionsLocked();
        _ctx.LoggedDeliveryHolds.ClearAll();
        _ctx.PendingRemoval = null;
        return Chapter3DeliveryCheckOutcome.Completed;
    }

    /// <summary>
    /// <summary>
    /// One Emergency Supply expiry pass over the active job: at or past the
    /// frozen 12-hour mark the job fails like any failure, opening a make-good
    /// at the nominal value through the existing failed-job path. An unreadable
    /// clock skips the pass; the day-tick deadline still backstops it.
    /// Anything else is not this check's job.
    /// </summary>
    internal void CheckEmergencyExpiryLocked(int chapterDay)
    {
        var job = _ctx.State!.JobBoard.ActiveJob;
        if (job is null || job.JobType != Chapter3JobType.EmergencySupply || !job.ExpiresGameMinutes.HasValue)
            return;
        if (!_ctx.TryReadCanonicalMinutesLocked(out var now))
        {
            ReportDeliveryHoldOnceLocked(job.JobId, "clock-unreadable",
                $"Chapter 3 job {job.JobId} expiry holding: the game clock was unreadable.");
            return;
        }
        if (now < job.ExpiresGameMinutes.Value)
            return;
        _ctx.Log($"Chapter 3 job {job.JobId} expired: the 12-hour window passed with nothing delivered.");
        var completed = Chapter3JobScheduler.Complete(_ctx.State.JobBoard, job.JobId, succeeded: false, chapterDay);
        _ctx.State = _ctx.State with { JobBoard = completed.Board, Revision = _ctx.State.Revision + 1 };
        foreach (var effect in completed.Effects)
            _ctx.Log(Chapter3FeatureContext.FormatEffect(effect));
        foreach (var ledgerEffect in ApplyLedgerTickLocked(completed.Effects, chapterDay))
            _ctx.Log(Chapter3FeatureContext.FormatLedgerEffect(ledgerEffect));
        DisposeDropSubscriptionsLocked();
        _ctx.LoggedDeliveryHolds.ClearAll();
        _ctx.PendingRemoval = null;
    }

    /// One recovery pass over the frozen job: notice custody once the staged
    /// stack leaves the pickup slot, then verify the handoff, remove exactly N
    /// under the slot lock, pay the fixed reward, and complete. Every hold
    /// logs once per job and reason and records nothing.
    /// </summary>
    internal Chapter3DeliveryCheckOutcome CheckRecoveryLocked(int chapterDay)
    {
        var job = _ctx.State!.JobBoard.ActiveJob;
        var recovery = job?.Recovery;
        if (job is null || recovery is null || job.JobType != Chapter3JobType.PackageRecovery || _ctx.DeadDropWorld is null)
            return Chapter3DeliveryCheckOutcome.NoWork;
        PruneDeliveryHoldsLocked(job.JobId);
        if (_ctx.PendingRemoval is not null && _ctx.PendingRemoval.JobId != job.JobId)
            _ctx.PendingRemoval = null;
        if (!recovery.Staged)
        {
            if (!TryStageRecoveryLocked(recovery, out var staged, out var reason))
            {
                ReportDeliveryHoldOnceLocked(job.JobId, "staging",
                    $"Chapter 3 job {job.JobId} recovery holding: {reason}.");
                return Chapter3DeliveryCheckOutcome.Held;
            }
            _ctx.State = _ctx.State with
            {
                JobBoard = _ctx.State.JobBoard with { ActiveJob = job with { Recovery = staged } },
                Revision = _ctx.State.Revision + 1
            };
            job = _ctx.State.JobBoard.ActiveJob!;
            recovery = staged;
            _ctx.Log($"Chapter 3 job {job.JobId} staged {recovery.BrickCount} bricks of {recovery.ProductName} at {recovery.Pickup.Name}.");
        }
        if (!recovery.Custody && !CheckRecoveryCustodyLocked(job, recovery))
            return Chapter3DeliveryCheckOutcome.Held;
        job = _ctx.State!.JobBoard.ActiveJob!;
        recovery = job.Recovery!;
        if (!recovery.RemovalConfirmed)
        {
            if (!TryReadSlotsLocked(recovery.Handoff.Guid, out var slots))
            {
                ReportDeliveryHoldOnceLocked(job.JobId, "handoff-unreadable",
                    $"Chapter 3 job {job.JobId} recovery holding: the handoff drop was unreadable.");
                return Chapter3DeliveryCheckOutcome.Held;
            }
            var confirmed = false;
            if (_ctx.PendingRemoval is not null)
            {
                var pendingOutcome = ReconcilePendingRemovalLocked(job, recovery.BrickCount, slots);
                if (pendingOutcome == Chapter3PendingRemovalOutcome.Held)
                    return Chapter3DeliveryCheckOutcome.Held;
                if (pendingOutcome == Chapter3PendingRemovalOutcome.Confirmed)
                {
                    ConfirmRecoveryRemovalLocked(job);
                    confirmed = true;
                }
                else
                {
                    _ctx.PendingRemoval = null;
                }
            }
            if (!confirmed && !ClassifyAndRemoveHandoffLocked(job, recovery, slots))
                return Chapter3DeliveryCheckOutcome.Held;
        }
        if (_ctx.CreditLocked(job.Reward) != Chapter3WalletCredit.Credited)
        {
            ReportDeliveryHoldOnceLocked(job.JobId, "unpaid",
                $"Chapter 3 job {job.JobId} reward ${job.Reward:N0} unpaid; retrying without re-removing the bricks.");
            return Chapter3DeliveryCheckOutcome.Held;
        }
        _ctx.Log($"Chapter 3 job {job.JobId} paid ${job.Reward:N0}.");
        var completed = Chapter3JobScheduler.Complete(_ctx.State.JobBoard, job.JobId, succeeded: true, chapterDay);
        _ctx.State = _ctx.State with { JobBoard = CountPostWarehouseSuccessLocked(completed.Board), Revision = _ctx.State.Revision + 1 };
        foreach (var effect in completed.Effects)
            _ctx.Log(Chapter3FeatureContext.FormatEffect(effect));
        DisposeDropSubscriptionsLocked();
        _ctx.LoggedDeliveryHolds.ClearAll();
        _ctx.PendingRemoval = null;
        return Chapter3DeliveryCheckOutcome.Completed;
    }

    /// <summary>
    /// Custody is the staged stack leaving the staged slot: any of the frozen
    /// product in brick packaging still sitting there means the player has not
    /// picked it up yet. Something else in the slot (or nothing) means they
    /// have; the handoff check still demands the full count before it pays.
    /// </summary>
    private bool CheckRecoveryCustodyLocked(Chapter3ActiveJob job, Chapter3PackageRecovery recovery)
    {
        if (!TryReadSlotsLocked(recovery.Pickup.Guid, out var slots))
        {
            ReportDeliveryHoldOnceLocked(job.JobId, "pickup-unreadable",
                $"Chapter 3 job {job.JobId} recovery holding: the pickup drop was unreadable.");
            return false;
        }
        var slot = slots.SingleOrDefault(candidate => candidate.SlotIndex == recovery.StagedSlotIndex);
        if (slot is null)
        {
            ReportDeliveryHoldOnceLocked(job.JobId, "pickup-unreadable",
                $"Chapter 3 job {job.JobId} recovery holding: the pickup drop was unreadable.");
            return false;
        }
        var matching = slot.IsPackaged &&
            string.Equals(slot.ProductId, recovery.ProductId, StringComparison.Ordinal) &&
            string.Equals(slot.PackagingId, recovery.PackagingId, StringComparison.Ordinal)
            ? slot.Quantity
            : 0;
        if (matching > 0)
        {
            ReportDeliveryHoldOnceLocked(job.JobId, "awaiting-pickup",
                $"Chapter 3 job {job.JobId} recovery holding: the staged bricks are still in the pickup drop.");
            return false;
        }
        _ctx.State = _ctx.State! with
        {
            JobBoard = _ctx.State.JobBoard with { ActiveJob = job with { Recovery = recovery with { Custody = true } } },
            Revision = _ctx.State.Revision + 1
        };
        _ctx.Log($"Chapter 3 job {job.JobId} in custody: the staged bricks left {recovery.Pickup.Name}.");
        RefreshDropSubscriptionsLocked();
        return true;
    }

    /// <summary>
    /// A removal whose outcome is unknown: the mutation returned
    /// <see cref="Release1SmallCourtesyWorldMutationStatus.Ambiguous"/>, threw,
    /// or landed but failed its post-read. The next pass reconciles it against
    /// a fresh read instead of classifying, so landed bricks are never lost
    /// unpaid. In-memory only; a reload re-reads the world from scratch.
    /// </summary>
    internal sealed record Chapter3PendingRemoval(int JobId, int SlotIndex, int PreQuantity);

    private enum Chapter3PendingRemovalOutcome
    {
        Confirmed,
        Retry,
        Held
    }

    internal bool TryStorageHandoffLocked()
    {
        var job = _ctx.State!.JobBoard.ActiveJob!;
        var run = job.StorageRun!;
        var route = run.Route;
        if (!TryReadSlotsLocked(route.Handoff.Guid, out var slots))
        {
            ReportDeliveryHoldOnceLocked(job.JobId, "handoff-unreadable",
                $"Chapter 3 job {job.JobId} storage holding: the handoff drop was unreadable.");
            return false;
        }
        if (run.PendingSlot is { } pending)
        {
            var post = slots.SingleOrDefault(s => s.SlotIndex == pending);
            if (post is null)
            {
                ReportDeliveryHoldOnceLocked(job.JobId, "handoff-unreadable",
                    $"Chapter 3 job {job.JobId} storage holding: the handoff slot was unreadable.");
                return false;
            }
            if (post.Quantity == run.PendingPreQuantity - route.BrickCount)
            {
                _ctx.Storage.Set(run with { Route = route with { RemovalConfirmed = true }, PendingSlot = null, PendingPreQuantity = null });
                _ctx.PendingRemoval = null;
                return true;
            }
            var fingerprint = new Release1ConsignmentFingerprint(route.ProductId, route.PackagingId, route.BrickCount);
            if (post.Quantity != run.PendingPreQuantity || !fingerprint.Matches(post))
            {
                ReportDeliveryHoldOnceLocked(job.JobId, "pending-diverged",
                    $"Chapter 3 job {job.JobId} storage holding: the handoff slot diverged from the saved removal receipt; awaiting reconciliation.");
                return false;
            }
        }
        // Exact count only: duplicate or merged handoff stacks wait unchanged.
        var handoffRoom = new Release1HoldRoomSnapshot(Release1HoldRoomReadiness.Ready,
            new[] { new Release1HoldRoomClosetSnapshot(route.Handoff.Guid, slots.Count, slots) });
        var observation = Release1HoldRoomClassifier.Classify(handoffRoom,
            new Release1ConsignmentFingerprint(route.ProductId, route.PackagingId, route.BrickCount), 1, out _);
        if (observation != Release1HoldObservation.Held)
        {
            ReportDeliveryHoldOnceLocked(job.JobId, $"handoff-{observation}",
                $"Chapter 3 job {job.JobId} storage holding: the handoff stack was {observation}; waiting for the exact frozen brick count.");
            return false;
        }
        var slot = slots.Single(s => new Release1ConsignmentFingerprint(route.ProductId, route.PackagingId, route.BrickCount).Matches(s));
        run = run with { PendingSlot = slot.SlotIndex, PendingPreQuantity = slot.Quantity };
        if (!TryRemoveHandoffBricksLocked(job, route, slot.SlotIndex, slot.Quantity)) return false;
        _ctx.Storage.Set(run with { Route = route with { RemovalConfirmed = true }, PendingSlot = null, PendingPreQuantity = null });
        _ctx.PendingRemoval = null;
        return true;
    }

    private bool ClassifyAndRemoveLocked(
        Chapter3ActiveJob job, Chapter3DeadDropDelivery delivery, IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
    {
        var verdict = Chapter3DeadDropDeliveryClassifier.Classify(
            slots, delivery.ProductId, delivery.PackagingId, delivery.BrickCount);
        if (verdict is not Chapter3DeliveryComplete complete)
        {
            var hold = (Chapter3DeliveryHold)verdict;
            ReportDeliveryHoldOnceLocked(job.JobId, $"classify-{hold.Reason}",
                $"Chapter 3 job {job.JobId} delivery holding: {hold.Detail}.");
            return false;
        }
        var slot = slots.SingleOrDefault(candidate => candidate.SlotIndex == complete.SlotIndex);
        if (slot is null || slot.Quantity < delivery.BrickCount)
        {
            ReportDeliveryHoldOnceLocked(job.JobId, "vanished",
                $"Chapter 3 job {job.JobId} delivery holding: the delivery slot changed between reads.");
            return false;
        }
        if (!TryRemoveBricksLocked(job, delivery, complete.SlotIndex, slot.Quantity))
            return false;
        ConfirmRemovalLocked(job);
        return true;
    }

    private bool ClassifyAndRemoveHandoffLocked(
        Chapter3ActiveJob job, Chapter3PackageRecovery recovery, IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
    {
        var verdict = Chapter3DeadDropDeliveryClassifier.Classify(
            slots, recovery.ProductId, recovery.PackagingId, recovery.BrickCount);
        if (verdict is not Chapter3DeliveryComplete complete)
        {
            var hold = (Chapter3DeliveryHold)verdict;
            ReportDeliveryHoldOnceLocked(job.JobId, $"handoff-{hold.Reason}",
                $"Chapter 3 job {job.JobId} recovery holding: {hold.Detail}.");
            return false;
        }
        var slot = slots.SingleOrDefault(candidate => candidate.SlotIndex == complete.SlotIndex);
        if (slot is null || slot.Quantity < recovery.BrickCount)
        {
            ReportDeliveryHoldOnceLocked(job.JobId, "handoff-vanished",
                $"Chapter 3 job {job.JobId} recovery holding: the handoff slot changed between reads.");
            return false;
        }
        if (!TryRemoveHandoffBricksLocked(job, recovery, complete.SlotIndex, slot.Quantity))
            return false;
        ConfirmRecoveryRemovalLocked(job);
        return true;
    }

    private void ConfirmRecoveryRemovalLocked(Chapter3ActiveJob job)
    {
        _ctx.State = _ctx.State! with
        {
            JobBoard = _ctx.State.JobBoard with { ActiveJob = job with { Recovery = job.Recovery! with { RemovalConfirmed = true } } },
            Revision = _ctx.State.Revision + 1
        };
        _ctx.PendingRemoval = null;
    }

    private void ConfirmRemovalLocked(Chapter3ActiveJob job)
    {
        _ctx.State = _ctx.State! with
        {
            JobBoard = _ctx.State.JobBoard with { ActiveJob = job with { Delivery = job.Delivery! with { RemovalConfirmed = true } } },
            Revision = _ctx.State.Revision + 1
        };
        _ctx.PendingRemoval = null;
    }

    private Chapter3PendingRemovalOutcome ReconcilePendingRemovalLocked(
        Chapter3ActiveJob job, int brickCount, IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
    {
        var pending = _ctx.PendingRemoval!;
        var slot = slots.SingleOrDefault(candidate => candidate.SlotIndex == pending.SlotIndex);
        if (slot is not null && slot.Quantity == pending.PreQuantity - brickCount)
        {
            _ctx.Log($"Chapter 3 job {job.JobId} removal confirmed on re-read: slot {pending.SlotIndex} holds {slot.Quantity}.");
            return Chapter3PendingRemovalOutcome.Confirmed;
        }
        if (slot is not null && slot.Quantity == pending.PreQuantity)
            return Chapter3PendingRemovalOutcome.Retry;
        ReportDeliveryHoldOnceLocked(job.JobId, "pending-diverged",
            $"Chapter 3 job {job.JobId} delivery holding: the pending removal slot changed unexpectedly; the reward waits.");
        return Chapter3PendingRemovalOutcome.Held;
    }

    private bool TryRemoveBricksLocked(
        Chapter3ActiveJob job, Chapter3DeadDropDelivery delivery, int slotIndex, int preQuantity)
    {
        if (!TryAcquireSlotLockLocked(delivery.DropGuid, slotIndex))
        {
            ReportDeliveryHoldOnceLocked(job.JobId, "lock",
                $"Chapter 3 job {job.JobId} delivery holding: the delivery slot is busy.");
            return false;
        }
        try
        {
            Release1SmallCourtesyWorldMutationStatus changed;
            try { changed = _ctx.DeadDropWorld!.TryChangeSlotQuantity(delivery.DropGuid, slotIndex, -delivery.BrickCount); }
            catch
            {
                _ctx.PendingRemoval = new Chapter3PendingRemoval(job.JobId, slotIndex, preQuantity);
                ReportDeliveryHoldOnceLocked(job.JobId, "removal-fault",
                    $"Chapter 3 job {job.JobId} delivery holding: the removal faulted; will reconcile on the next pass.");
                return false;
            }
            if (changed == Release1SmallCourtesyWorldMutationStatus.Ambiguous)
            {
                _ctx.PendingRemoval = new Chapter3PendingRemoval(job.JobId, slotIndex, preQuantity);
                ReportDeliveryHoldOnceLocked(job.JobId, "removal-ambiguous",
                    $"Chapter 3 job {job.JobId} delivery holding: the removal was ambiguous; will reconcile on the next pass.");
                return false;
            }
            if (changed != Release1SmallCourtesyWorldMutationStatus.Succeeded)
            {
                ReportDeliveryHoldOnceLocked(job.JobId, $"removal-{changed}",
                    $"Chapter 3 job {job.JobId} delivery holding: the removal was {changed.ToString().ToLowerInvariant()}; the bricks stay yours.");
                return false;
            }
            if (!TryReadSlotLocked(delivery.DropGuid, slotIndex, out var post) ||
                post.Quantity != preQuantity - delivery.BrickCount)
            {
                _ctx.PendingRemoval = new Chapter3PendingRemoval(job.JobId, slotIndex, preQuantity);
                ReportDeliveryHoldOnceLocked(job.JobId, "post-mismatch",
                    $"Chapter 3 job {job.JobId} delivery holding: the removal could not be verified; will reconcile on the next pass.");
                return false;
            }
            _ctx.Log($"Chapter 3 job {job.JobId} removed {delivery.BrickCount} bricks from {delivery.DropName} slot {slotIndex}.");
            return true;
        }
        finally
        {
            ReleaseSlotLockLocked();
        }
    }

    /// <summary>
    /// The handoff twin of <see cref="TryRemoveBricksLocked"/>: removes exactly
    /// the frozen count from the handoff slot under the slot lock and verifies
    /// the post-quantity. The holds name the recovery, and the bricks stay in
    /// the drop on failure: they are the family's staged bricks, not the
    /// player's deposit.
    /// </summary>
    private bool TryRemoveHandoffBricksLocked(
        Chapter3ActiveJob job, Chapter3PackageRecovery recovery, int slotIndex, int preQuantity)
    {
        if (!TryAcquireSlotLockLocked(recovery.Handoff.Guid, slotIndex))
        {
            ReportDeliveryHoldOnceLocked(job.JobId, "lock",
                $"Chapter 3 job {job.JobId} recovery holding: the handoff slot is busy.");
            return false;
        }
        try
        {
            if (job.StorageRun is { } storage)
                _ctx.Storage.Set(storage with { PendingSlot = slotIndex, PendingPreQuantity = preQuantity });
            Release1SmallCourtesyWorldMutationStatus changed;
            try { changed = _ctx.DeadDropWorld!.TryChangeSlotQuantity(recovery.Handoff.Guid, slotIndex, -recovery.BrickCount); }
            catch
            {
                _ctx.PendingRemoval = new Chapter3PendingRemoval(job.JobId, slotIndex, preQuantity);
                ReportDeliveryHoldOnceLocked(job.JobId, "removal-fault",
                    $"Chapter 3 job {job.JobId} recovery holding: the removal faulted; will reconcile on the next pass.");
                return false;
            }
            if (changed == Release1SmallCourtesyWorldMutationStatus.Ambiguous)
            {
                _ctx.PendingRemoval = new Chapter3PendingRemoval(job.JobId, slotIndex, preQuantity);
                ReportDeliveryHoldOnceLocked(job.JobId, "removal-ambiguous",
                    $"Chapter 3 job {job.JobId} recovery holding: the removal was ambiguous; will reconcile on the next pass.");
                return false;
            }
            if (changed == Release1SmallCourtesyWorldMutationStatus.Rejected && job.StorageRun is { } rejectedStorage)
                _ctx.Storage.Set(rejectedStorage with { PendingSlot = null, PendingPreQuantity = null });
            if (changed != Release1SmallCourtesyWorldMutationStatus.Succeeded)
            {
                ReportDeliveryHoldOnceLocked(job.JobId, $"removal-{changed}",
                    $"Chapter 3 job {job.JobId} recovery holding: the removal was {changed.ToString().ToLowerInvariant()}; the bricks stay in the drop.");
                return false;
            }
            if (!TryReadSlotLocked(recovery.Handoff.Guid, slotIndex, out var post) ||
                post.Quantity != preQuantity - recovery.BrickCount)
            {
                _ctx.PendingRemoval = new Chapter3PendingRemoval(job.JobId, slotIndex, preQuantity);
                ReportDeliveryHoldOnceLocked(job.JobId, "post-mismatch",
                    $"Chapter 3 job {job.JobId} recovery holding: the removal could not be verified; will reconcile on the next pass.");
                return false;
            }
            _ctx.Log($"Chapter 3 job {job.JobId} removed {recovery.BrickCount} bricks from {recovery.Handoff.Name} slot {slotIndex}.");
            return true;
        }
        finally
        {
            ReleaseSlotLockLocked();
        }
    }

    internal bool TryReadSlotsLocked(
        string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
    {
        slots = Array.Empty<Release1SmallCourtesySlotSnapshot>();
        try
        {
            if (_ctx.DeadDropWorld!.TryReadDeadDropSlots(deadDropGuid, out var read) != Release1SmallCourtesyWorldReadStatus.Ready ||
                read is null)
                return false;
            var copy = read.ToArray();
            foreach (var slot in copy)
            {
                if (slot is null)
                    return false;
                slot.Validate();
            }
            if (copy.Select(slot => slot.SlotIndex).Distinct().Count() != copy.Length)
                return false;
            slots = copy;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadSlotLocked(
        string deadDropGuid, int slotIndex, out Release1SmallCourtesySlotSnapshot slot)
    {
        slot = null!;
        if (!TryReadSlotsLocked(deadDropGuid, out var slots))
            return false;
        slot = slots.SingleOrDefault(candidate => candidate.SlotIndex == slotIndex)!;
        return slot is not null;
    }

    private bool TryAcquireSlotLockLocked(string deadDropGuid, int slotIndex)
    {
        if (_ctx.LockedDropGuid is not null)
            return string.Equals(_ctx.LockedDropGuid, deadDropGuid, StringComparison.Ordinal) &&
                _ctx.LockedSlotIndex == slotIndex;
        _ctx.LockedDropGuid = deadDropGuid;
        _ctx.LockedSlotIndex = slotIndex;
        try
        {
            var status = _ctx.DeadDropWorld!.TrySetSlotLocked(deadDropGuid, slotIndex, true);
            if (status != Release1SmallCourtesyWorldMutationStatus.Succeeded)
            {
                if (status == Release1SmallCourtesyWorldMutationStatus.Ambiguous)
                    ReleaseSlotLockLocked();
                else
                {
                    _ctx.LockedDropGuid = null;
                    _ctx.LockedSlotIndex = -1;
                }
                return false;
            }
            return true;
        }
        catch
        {
            ReleaseSlotLockLocked();
            return false;
        }
    }

    /// <summary>
    /// The recovery cleanup rule: when a frozen recovery ends without success,
    /// up to the frozen count of matching bricks leaves the staged pickup
    /// slot, so staged bricks never linger as free product. Custody does not
    /// matter (remnants clean the same taken or not); a slot holding something
    /// else is left alone, and an unreadable drop only logs. The handoff drop
    /// is never touched: anything there was the player's deposit to lose.
    /// Best-effort with no retry (the job record is already gone), so every
    /// outcome logs exactly what happened.
    /// </summary>
    internal void CleanupRecoveryLocked(int jobId, Chapter3PackageRecovery recovery)
    {
        if (_ctx.DeadDropWorld is null || !recovery.Staged || recovery.StagedSlotIndex < 0)
            return;
        if (!TryReadSlotsLocked(recovery.Pickup.Guid, out var slots))
        {
            _ctx.Log($"Chapter 3 job {jobId} cleanup skipped: the pickup drop was unreadable.");
            return;
        }
        var slot = slots.SingleOrDefault(candidate => candidate.SlotIndex == recovery.StagedSlotIndex);
        var matching = slot is not null && slot.IsPackaged &&
            string.Equals(slot.ProductId, recovery.ProductId, StringComparison.Ordinal) &&
            string.Equals(slot.PackagingId, recovery.PackagingId, StringComparison.Ordinal)
            ? slot.Quantity
            : 0;
        var remove = Math.Min(matching, recovery.BrickCount);
        if (remove <= 0)
        {
            _ctx.Log($"Chapter 3 job {jobId} cleanup: nothing to remove from {recovery.Pickup.Name}.");
            return;
        }
        Release1SmallCourtesyWorldMutationStatus cleaned;
        try
        {
            cleaned = _ctx.DeadDropWorld.TryChangeSlotQuantity(recovery.Pickup.Guid, recovery.StagedSlotIndex, -remove);
        }
        catch (Exception exception)
        {
            _ctx.Log($"Chapter 3 job {jobId} cleanup faulted: {exception.GetType().Name}.");
            return;
        }
        _ctx.Log(cleaned == Release1SmallCourtesyWorldMutationStatus.Succeeded
            ? $"Chapter 3 job {jobId} cleaned {remove} staged bricks from {recovery.Pickup.Name}."
            : $"Chapter 3 job {jobId} cleanup was {cleaned.ToString().ToLowerInvariant()}.");
    }

    internal void CleanupStorageLocked(int jobId, Chapter3PackageRecovery route)
    {
        if (!route.Staged) return;
        if (!TryReadSlotsLocked(route.Pickup.Guid, out var slots))
        {
            _ctx.Log($"Chapter 3 job {jobId} cleanup skipped: the pickup drop was unreadable.");
            return;
        }
        var left = route.BrickCount;
        var fingerprint = new Release1ConsignmentFingerprint(route.ProductId, route.PackagingId, route.BrickCount);
        foreach (var slot in slots.Where(fingerprint.MatchesIdentity).OrderBy(s => s.SlotIndex))
        {
            var take = Math.Min(left, slot.Quantity);
            if (take <= 0) break;
            try
            {
                var status = _ctx.DeadDropWorld!.TryChangeSlotQuantity(route.Pickup.Guid, slot.SlotIndex, -take);
                _ctx.Log($"Chapter 3 storage job {jobId} pickup cleanup slot {slot.SlotIndex}: {status}.");
                // Never continue after an unknown outcome; carried and closet stock are never touched.
                if (status != Release1SmallCourtesyWorldMutationStatus.Succeeded) break;
                if (!TryReadSlotLocked(route.Pickup.Guid, slot.SlotIndex, out var post) || post.Quantity != slot.Quantity - take)
                {
                    _ctx.Log($"Chapter 3 storage job {jobId} pickup cleanup could not be verified; stopped.");
                    break;
                }
                left -= take;
            }
            catch { _ctx.Log($"Chapter 3 storage job {jobId} pickup cleanup unreadable; stopped."); break; }
        }
    }

    internal void ReleaseSlotLockLocked()
    {
        if (_ctx.LockedDropGuid is null || _ctx.LockedSlotIndex < 0)
            return;
        try
        {
            if (_ctx.DeadDropWorld!.TrySetSlotLocked(_ctx.LockedDropGuid, _ctx.LockedSlotIndex, false) != Release1SmallCourtesyWorldMutationStatus.Succeeded)
                return;
            _ctx.LockedDropGuid = null;
            _ctx.LockedSlotIndex = -1;
        }
        catch
        {
            // Retain the exact lock identity so the next lifecycle teardown can retry it.
        }
    }

    /// <summary>
    /// Binds one drop-closed callback to the active job's current stage: the
    /// delivery drop (a DeadDrop or EmergencySupply named drop), the recovery
    /// pickup until custody, or the handoff after custody. A failed bind
    /// retries on the next refresh.
    /// </summary>
    internal void RefreshDropSubscriptionsLocked()
    {
        var job = _ctx.State?.JobBoard.ActiveJob;
        string? desiredDropGuid = null;
        if (_ctx.DeadDropWorld is not null && job is not null)
        {
            if ((job.JobType is Chapter3JobType.DeadDrop or Chapter3JobType.EmergencySupply) && job.Delivery is not null)
                desiredDropGuid = job.Delivery.DropGuid;
            else if (job.JobType == Chapter3JobType.PackageRecovery && job.Recovery is { } recovery)
                desiredDropGuid = recovery.Custody ? recovery.Handoff.Guid : recovery.Pickup.Guid;
            else if (job.StorageRun is { } storage)
                desiredDropGuid = storage.Route.Custody ? storage.Route.Handoff.Guid : storage.Route.Pickup.Guid;
        }

        if (_ctx.DropSubscription is not null &&
            string.Equals(_ctx.BoundDropGuid, desiredDropGuid, StringComparison.Ordinal))
            return;

        DisposeDropSubscriptionsLocked();
        var world = _ctx.DeadDropWorld;
        if (world is null || desiredDropGuid is null)
            return;
        try
        {
            var status = world.TrySubscribeDeadDropClosed(desiredDropGuid, _onDeadDropClosed, out var subscription);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready || subscription is null ||
                !string.Equals(subscription.DeadDropGuid, desiredDropGuid, StringComparison.Ordinal))
            {
                try { subscription?.Dispose(); }
                catch { }
                return;
            }

            _ctx.BoundDropGuid = desiredDropGuid;
            _ctx.DropSubscription = subscription;
        }
        catch
        {
        }
    }

    internal void DisposeDropSubscriptionsLocked()
    {
        var subscription = _ctx.DropSubscription;
        _ctx.DropSubscription = null;
        _ctx.BoundDropGuid = null;
        try { subscription?.Dispose(); }
        catch { }
    }

    private void ReportDeliveryHoldOnceLocked(int jobId, string key, string line)
    {
        _ctx.LoggedDeliveryHolds.Report($"{jobId}:{key}", line);
    }

    private void PruneDeliveryHoldsLocked(int jobId) =>
        _ctx.LoggedDeliveryHolds.ClearWhere(entry => !entry.StartsWith($"{jobId}:", StringComparison.Ordinal));

    /// <summary>
    /// Counts a success toward the post-warehouse counter once the warehouse
    /// is purchased. Keys off the story purchase, not live ownership: a
    /// dev-owned warehouse without a purchase counts nothing.
    /// </summary>
    internal Chapter3JobBoard CountPostWarehouseSuccessLocked(Chapter3JobBoard board) =>
        _ctx.State?.WarehouseOffer?.Purchased == true
            ? board with { SuccessfulJobsSinceWarehousePurchase = board.SuccessfulJobsSinceWarehousePurchase + 1 }
            : board;

    /// <summary>
    /// Feeds the scheduler's MakeGoodRequired effects into the ledger and runs the
    /// ledger's day tick after the scheduler, on every processed chapter day.
    /// </summary>
    internal List<Chapter3MakeGoodEffect> ApplyLedgerTickLocked(IReadOnlyList<Chapter3JobEffect> schedulerEffects, int? chapterDay)
    {
        var ledgerEffects = new List<Chapter3MakeGoodEffect>();
        if (!chapterDay.HasValue)
            return ledgerEffects;
        var ledger = _ctx.State!.Ledger;
        foreach (var required in schedulerEffects.OfType<Chapter3MakeGoodRequiredEffect>())
        {
            var opened = Chapter3MakeGoods.OpenForFailedJob(ledger, required.JobId, required.JobType, required.ReferenceAmount, chapterDay.Value);
            ledger = opened.Ledger;
            ledgerEffects.AddRange(opened.Effects);
        }
        var ticked = Chapter3MakeGoods.OnDayTick(ledger, chapterDay.Value);
        _ctx.State = _ctx.State with { Ledger = ticked.Ledger };
        ledgerEffects.AddRange(ticked.Effects);
        return ledgerEffects;
    }
}
