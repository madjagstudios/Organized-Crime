using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

// OC-152: the Chapter 3 production-demand feature (weekly issue, warehouse
// counting, and the family-locker hand-over). Moved verbatim out of
// Chapter3RuntimeService; only the receiver changed. Called only while the
// coordinator holds its gate; takes no locks itself.
internal sealed class Chapter3DemandFeature
{
    private readonly Chapter3FeatureContext _ctx;
    private float? _lastTakeAtSeconds;
    private string? _lastTakeDemandId;

    internal const float ReceiptQuietSeconds = 3f;

    // The durable burst and dedupe id already live in the demand and its
    // presentation ledger. A reload releases the unsent receipt immediately.
    internal void ResetReceiptDelayLocked()
    {
        _lastTakeAtSeconds = null;
        _lastTakeDemandId = null;
    }

    internal bool ReceiptReadyLocked() =>
        _lastTakeAtSeconds is null ||
        _ctx.State?.ActiveDemand?.DemandId != _lastTakeDemandId ||
        _ctx.ArthurClock() - _lastTakeAtSeconds.Value >= ReceiptQuietSeconds;

    public Chapter3DemandFeature(Chapter3FeatureContext ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    private enum Chapter3DemandPendingOutcome
    {
        Confirmed,
        Retry,
        Held
    }

    /// <summary>
    /// OC-136: one demand pass. Keeps
    /// the family locker's UI name first (cosmetic; never holds the
    /// pass), merges fresh warehouse output into the open demand and the
    /// eligibility set, then takes at most one hand-over step. Holds log
    /// once per reason and record nothing.
    /// </summary>
    internal void PollDemandLocked()
    {
        var world = _ctx.DemandWorld;
        if (world is null)
            return;
        EnsureDemandLockerNameLocked(world);
        if (!TryMergeDemandCountsLocked(world))
            return;
        if (_ctx.State!.ActiveDemand is not null)
            CheckDemandHandoverLocked(world);
    }

    private void EnsureDemandLockerNameLocked(IChapter3ProductionDemandWorld world)
    {
        Release1SmallCourtesyWorldMutationStatus named;
        try { named = world.TryEnsureFamilyLockerName(); }
        catch (Exception exception)
        {
            HoldDemandLocked("locker-name", $"Chapter 3 demand locker rename holding: it faulted ({exception.GetType().Name}).");
            return;
        }
        if (named != Release1SmallCourtesyWorldMutationStatus.Succeeded)
            HoldDemandLocked("locker-name", $"Chapter 3 demand locker rename holding: it was {named.ToString().ToLowerInvariant()}.");
        else
        {
            ClearDemandHoldLocked("locker-name");
            if (!_ctx.DemandLockerNameReceiptLogged)
            {
                _ctx.DemandLockerNameReceiptLogged = true;
                OrganizedCrimeLog.Diagnostic($"Chapter 3 demand locker named '{Chapter3DemandPolicy.FamilyLockerName}'.", _ctx.Log);
            }
        }
    }

    private bool TryMergeDemandCountsLocked(IChapter3ProductionDemandWorld world)
    {
        IReadOnlyList<Chapter3DemandStationSnapshot> current;
        try
        {
            if (world.TryReadWarehouseStations(out var read) != Release1SmallCourtesyWorldReadStatus.Ready || read is null)
            {
                HoldDemandLocked("count", "Chapter 3 demand holding: the warehouse stations were unreadable.");
                return false;
            }
            current = read.Where(station => station is not null).ToArray();
        }
        catch (Exception exception)
        {
            HoldDemandLocked("count", $"Chapter 3 demand holding: the station read faulted ({exception.GetType().Name}).");
            return false;
        }
        ClearDemandHoldLocked("count");
        var demand = _ctx.State!.ActiveDemand;
        var counted = Chapter3DemandCounter.Process(
            _ctx.DemandStationBaseline, current, demand?.ProductId, _ctx.State.MadeProductIds);
        _ctx.DemandStationBaseline = current;
        var state = _ctx.State;
        if (counted.EligibilityAdds.Count > 0)
        {
            state = state with { MadeProductIds = state.MadeProductIds.Concat(counted.EligibilityAdds).ToArray() };
            foreach (var added in counted.EligibilityAdds)
                _ctx.Log($"Chapter 3 demand eligibility: {added} was made in the warehouse.");
        }
        if (demand is not null && counted.FreshGain > 0)
        {
            var fresh = state.ActiveDemand!;
            state = state with { ActiveDemand = fresh with { FreshCount = fresh.FreshCount + counted.FreshGain } };
            _ctx.Log($"Chapter 3 demand {fresh.DemandId}: +{counted.FreshGain} fresh {fresh.ProductName} ({fresh.FreshCount + counted.FreshGain}/{fresh.Quantity}).");
        }
        if (!ReferenceEquals(state, _ctx.State))
            _ctx.State = state with { Revision = state.Revision + 1 };
        foreach (var verdict in counted.Verdicts)
            _ctx.Log($"Chapter 3 demand count: {verdict}");
        return true;
    }

    /// <summary>
    /// OC-136: the day tick's demand step, after the cuts it anchors on:
    /// close the open demand when it completed or missed, then issue the
    /// week's demand when its day has come.
    /// </summary>
    internal void ApplyDemandTickLocked(int? chapterDay)
    {
        if (!chapterDay.HasValue)
            return;
        EvaluateDemandLocked(chapterDay.Value);
        var cut = LatestCutLocked();
        if (cut is null)
            return;
        TryIssueDemandLocked(cut.WeekIndex, chapterDay.Value, cut, quiet: false, out _);
    }

    private Chapter3WeeklyCut? LatestCutLocked()
    {
        Chapter3WeeklyCut? latest = null;
        foreach (var cut in _ctx.State!.Cuts.Entries)
        {
            if (cut is not null && (latest is null || cut.WeekIndex > latest.WeekIndex))
                latest = cut;
        }
        return latest;
    }

    /// <summary>
    /// OC-136: closes the open demand when it completed (paid in full
    /// through hand-overs) or missed (past its due day, short at full
    /// unit value through the existing make-good path, which feeds
    /// Arthur's ladder). The hand-over completes immediately too; this
    /// is the backstop the tick and QA share.
    /// </summary>
    internal void EvaluateDemandLocked(int chapterDay, bool forceDeadline = false)
    {
        var demand = _ctx.State!.ActiveDemand;
        if (demand is null)
            return;
        if (demand.Delivered >= demand.Quantity)
        {
            CompleteDemandLocked(demand);
            return;
        }
        // OC-136 owner QA: demand expire forces the deadline now; the
        // make-good still opens on the real chapter day.
        if (!forceDeadline && chapterDay <= demand.DueDay)
            return;
        var shortfall = demand.Quantity - demand.Delivered;
        var opened = Chapter3MakeGoods.OpenForMissedDemand(_ctx.State.Ledger, demand.DemandId, shortfall, demand.UnitValue, chapterDay);
        _ctx.State = _ctx.State with
        {
            ActiveDemand = null,
            Ledger = opened.Ledger,
            LastDemandOutcome = new Chapter3DemandOutcome(demand.DemandId, demand.ProductName, shortfall, shortfall * demand.UnitValue, Completed: false),
            Revision = _ctx.State.Revision + 1
        };
        foreach (var effect in opened.Effects)
            _ctx.Log(Chapter3FeatureContext.FormatLedgerEffect(effect));
        _ctx.Log($"Chapter 3 demand {demand.DemandId} missed: {shortfall} {demand.ProductName} short at full price.");
    }

    private void CompleteDemandLocked(Chapter3ProductionDemand demand)
    {
        _ctx.State = _ctx.State! with
        {
            ActiveDemand = null,
            LastDemandOutcome = new Chapter3DemandOutcome(demand.DemandId, demand.ProductName, LeftUnits: 0, demand.PaidTotal, Completed: true),
            DemandsCompleted = _ctx.State.DemandsCompleted + 1,
            Revision = _ctx.State.Revision + 1
        };
        _ctx.Log($"Chapter 3 demand {demand.DemandId} complete: {demand.Quantity} {demand.ProductName} handed over, paid ${demand.PaidTotal:N0}.");
    }

    /// <summary>
    /// OC-136: issues the week's demand. The tick anchors on its cut
    /// (issue day is the cut's due day plus
    /// <see cref="Chapter3DemandPolicy.IssueOffsetDays"/>, one demand per
    /// cut week); QA passes no anchor and forces the timing. Either way
    /// the warehouse must be owned, something must have been made, the
    /// product and the HQ door must read, and the drug type must be a
    /// known table row. Routine gates stay silent; world failures hold
    /// with a line unless quiet (QA prints the message itself).
    /// </summary>
    internal bool TryIssueDemandLocked(int weekIndex, int chapterDay, Chapter3WeeklyCut? anchorCut, bool quiet, out string message)
    {
        message = string.Empty;
        if (_ctx.State!.ActiveDemand is not null)
        {
            message = $"Chapter 3 QA demand refused: demand {_ctx.State.ActiveDemand.DemandId} is still open.";
            return false;
        }
        if (_ctx.State.MadeProductIds.Count == 0)
        {
            message = "Chapter 3 QA demand refused: nothing has been made in the warehouse yet.";
            return false;
        }
        var world = _ctx.DemandWorld;
        if (world is null)
        {
            message = "Chapter 3 QA demand refused: the demand world is not wired up.";
            return false;
        }
        if (!_ctx.ReadOwnership())
        {
            message = "Chapter 3 QA demand refused: the warehouse is not owned.";
            return false;
        }
        if (anchorCut is not null)
        {
            if (_ctx.State.LastDemandWeek == weekIndex || chapterDay < anchorCut.DueDay + Chapter3DemandPolicy.IssueOffsetDays)
                return false;
        }
        if (!Chapter3DemandPolicy.TryPickProduct(_ctx.State.MadeProductIds, weekIndex, out var productId) || productId is null)
        {
            message = "Chapter 3 QA demand refused: the eligibility set picked nothing.";
            return false;
        }
        Chapter3DemandWorldProductInfo? info;
        try
        {
            if (world.TryReadProductInfo(productId, out info) != Release1SmallCourtesyWorldReadStatus.Ready || info is null)
            {
                HoldDemandIssueLocked(quiet, "product", $"{productId} would not read.", out message);
                return false;
            }
        }
        catch (Exception exception)
        {
            HoldDemandIssueLocked(quiet, "product", $"the product read faulted ({exception.GetType().Name}).", out message);
            return false;
        }
        if (!Chapter3DemandPolicy.TryParseProductType(info.DrugTypeName, out var productType))
        {
            HoldDemandIssueLocked(quiet, "drug-type", $"{info.ProductName} has an unknown drug type ({info.DrugTypeName}).", out message);
            return false;
        }
        if (string.IsNullOrWhiteSpace(info.ProductName) || info.UnitValue < 0)
        {
            HoldDemandIssueLocked(quiet, "product", $"{productId} read back an unusable name or price.", out message);
            return false;
        }
        float doorX, doorY, doorZ;
        try
        {
            if (!world.TryReadHqDoor(out doorX, out doorY, out doorZ))
            {
                HoldDemandIssueLocked(quiet, "door", "the HQ door would not read.", out message);
                return false;
            }
        }
        catch (Exception exception)
        {
            HoldDemandIssueLocked(quiet, "door", $"the HQ door read faulted ({exception.GetType().Name}).", out message);
            return false;
        }
        var quantity = Chapter3DemandPolicy.QuantityFor(productType);
        var demand = new Chapter3ProductionDemand(
            DemandId: "d" + _ctx.State.NextDemandId,
            ProductId: productId,
            ProductName: info.ProductName,
            ProductType: productType,
            Quantity: quantity,
            UnitValue: info.UnitValue,
            IssueDay: chapterDay,
            DueDay: chapterDay + Chapter3DemandPolicy.DurationDays,
            FreshCount: 0,
            Delivered: 0,
            PaidTotal: 0,
            LastTakeUnits: 0,
            HandOverCount: 0,
            HqDoorX: doorX,
            HqDoorY: doorY,
            HqDoorZ: doorZ,
            PendingRemoval: null);
        _ctx.State = _ctx.State with
        {
            ActiveDemand = demand,
            LastDemandWeek = weekIndex,
            NextDemandId = _ctx.State.NextDemandId + 1,
            Revision = _ctx.State.Revision + 1
        };
        ClearDemandIssueHoldsLocked();
        _ctx.Log($"Chapter 3 demand {demand.DemandId} issued: {quantity} {info.ProductName} due day {demand.DueDay}.");
        message = $"Chapter 3 QA: demand {demand.DemandId} issued: {quantity} {info.ProductName}, due day {demand.DueDay}.";
        return true;
    }

    private void HoldDemandIssueLocked(bool quiet, string reason, string detail, out string message)
    {
        message = $"Chapter 3 QA demand refused: {detail}";
        if (!quiet)
            HoldDemandLocked("issue-" + reason, $"Chapter 3 demand holding: {detail}");
    }

    private void ClearDemandIssueHoldsLocked()
    {
        if (_ctx.DemandHoldKey is not null && _ctx.DemandHoldKey.StartsWith("issue-", StringComparison.Ordinal))
            _ctx.DemandHoldKey = null;
    }

    /// <summary>
    /// OC-136: one hand-over step over the family locker: reconcile a
    /// saved pending removal first, else plan fresh takes capped at
    /// min(remaining, fresh minus delivered) and remove exactly one
    /// take's packages under the slot lock, verify the post-quantity,
    /// then credit half the unit value. A credited take records and may
    /// complete; every hold retries without re-removing.
    /// </summary>
    private void CheckDemandHandoverLocked(IChapter3ProductionDemandWorld world)
    {
        var demand = _ctx.State!.ActiveDemand;
        if (demand is null)
            return;
        if (!TryReadDemandLockerLocked(world, out var slots))
        {
            HoldDemandHandoverLocked("unreadable", $"Chapter 3 demand {demand.DemandId} holding: the family locker was unreadable.");
            return;
        }
        if (demand.PendingRemoval is not null)
        {
            var pendingOutcome = ReconcileDemandPendingLocked(demand, slots);
            if (pendingOutcome == Chapter3DemandPendingOutcome.Held)
                return;
            if (pendingOutcome == Chapter3DemandPendingOutcome.Confirmed)
            {
                CreditAndRecordTakeLocked(demand, demand.PendingRemoval.Units);
                return;
            }
            demand = ClearDemandPendingLocked(demand);
        }
        var remaining = demand.Quantity - demand.Delivered;
        var freshAvailable = Math.Max(0, demand.FreshCount - demand.Delivered);
        var cap = Math.Min(remaining, freshAvailable);
        if (cap < 1)
        {
            ClearDemandHandoverHoldsLocked();
            return;
        }
        var takes = Chapter3DemandHandover.PlanTakes(slots, demand.ProductId, cap);
        if (takes.Count == 0)
        {
            ClearDemandHandoverHoldsLocked();
            return;
        }
        var take = takes[0];
        var slot = slots.FirstOrDefault(candidate => candidate.SlotIndex == take.SlotIndex);
        if (slot is null || slot.Quantity < take.Packages ||
            !string.Equals(slot.ProductId, demand.ProductId, StringComparison.Ordinal))
        {
            HoldDemandHandoverLocked("vanished", $"Chapter 3 demand {demand.DemandId} holding: the locker slot changed between reads.");
            return;
        }
        if (!TryRemoveDemandTakeLocked(world, demand, slot.Quantity, take))
            return;
        CreditAndRecordTakeLocked(_ctx.State.ActiveDemand!, take.Units);
    }

    private bool TryReadDemandLockerLocked(
        IChapter3ProductionDemandWorld world, out IReadOnlyList<Chapter3DemandLockerSlot> slots)
    {
        slots = Array.Empty<Chapter3DemandLockerSlot>();
        try
        {
            if (world.TryReadFamilyLockerSlots(out var read) != Release1SmallCourtesyWorldReadStatus.Ready || read is null)
                return false;
            slots = read.Where(slot => slot is not null).ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Chapter3DemandPendingOutcome ReconcileDemandPendingLocked(
        Chapter3ProductionDemand demand, IReadOnlyList<Chapter3DemandLockerSlot> slots)
    {
        var pending = demand.PendingRemoval!;
        var slot = slots.FirstOrDefault(candidate => candidate.SlotIndex == pending.SlotIndex);
        if (slot is not null && slot.Quantity == pending.PreQuantity - pending.PackageCount)
        {
            _ctx.Log($"Chapter 3 demand {demand.DemandId} removal confirmed on re-read: locker slot {pending.SlotIndex} holds {slot.Quantity}.");
            return Chapter3DemandPendingOutcome.Confirmed;
        }
        if (slot is not null && slot.Quantity == pending.PreQuantity)
            return Chapter3DemandPendingOutcome.Retry;
        HoldDemandHandoverLocked("pending-diverged", $"Chapter 3 demand {demand.DemandId} holding: the pending removal slot changed unexpectedly; the credit waits.");
        return Chapter3DemandPendingOutcome.Held;
    }

    private Chapter3ProductionDemand ClearDemandPendingLocked(Chapter3ProductionDemand demand)
    {
        var cleared = demand with { PendingRemoval = null };
        _ctx.State = _ctx.State! with { ActiveDemand = cleared, Revision = _ctx.State.Revision + 1 };
        return cleared;
    }

    private bool TryRemoveDemandTakeLocked(
        IChapter3ProductionDemandWorld world, Chapter3ProductionDemand demand, int preQuantity, Chapter3DemandSlotTake take)
    {
        if (!TryAcquireDemandSlotLockLocked(world, take.SlotIndex))
        {
            HoldDemandHandoverLocked("lock", $"Chapter 3 demand {demand.DemandId} holding: the locker slot is busy.");
            return false;
        }
        try
        {
            _ctx.State = _ctx.State! with
            {
                ActiveDemand = demand with { PendingRemoval = new Chapter3DemandPendingRemoval(take.SlotIndex, preQuantity, take.Packages, take.Units) },
                Revision = _ctx.State.Revision + 1
            };
            Release1SmallCourtesyWorldMutationStatus changed;
            try { changed = world.TryChangeFamilyLockerSlotQuantity(take.SlotIndex, -take.Packages); }
            catch
            {
                HoldDemandHandoverLocked("removal-fault", $"Chapter 3 demand {demand.DemandId} holding: the removal faulted; will reconcile on the next pass.");
                return false;
            }
            if (changed == Release1SmallCourtesyWorldMutationStatus.Ambiguous)
            {
                HoldDemandHandoverLocked("removal-ambiguous", $"Chapter 3 demand {demand.DemandId} holding: the removal was ambiguous; will reconcile on the next pass.");
                return false;
            }
            if (changed != Release1SmallCourtesyWorldMutationStatus.Succeeded)
            {
                ClearDemandPendingLocked(_ctx.State.ActiveDemand!);
                HoldDemandHandoverLocked($"removal-{changed.ToString().ToLowerInvariant()}", $"Chapter 3 demand {demand.DemandId} holding: the removal was {changed.ToString().ToLowerInvariant()}; the product stays yours.");
                return false;
            }
            if (!TryReadDemandLockerLocked(world, out var post) ||
                post.FirstOrDefault(candidate => candidate.SlotIndex == take.SlotIndex)?.Quantity != preQuantity - take.Packages)
            {
                HoldDemandHandoverLocked("post-mismatch", $"Chapter 3 demand {demand.DemandId} holding: the removal could not be verified; will reconcile on the next pass.");
                return false;
            }
            _ctx.Log($"Chapter 3 demand {demand.DemandId} removed {take.Packages} packages ({take.Units} units) from the family locker slot {take.SlotIndex}.");
            return true;
        }
        finally
        {
            ReleaseDemandSlotLockLocked();
        }
    }

    /// <summary>
    /// OC-136: credits one removed take at half the frozen unit value
    /// (integer math rounds down; a zero-value take records without
    /// touching the wallet) and records it. A refused credit holds
    /// without re-removing; a recorded take that fills the demand
    /// completes it on the spot.
    /// </summary>
    private void CreditAndRecordTakeLocked(Chapter3ProductionDemand demand, int units)
    {
        var pay = Math.Max(0, units * demand.UnitValue / 2);
        if (pay > 0 && _ctx.CreditLocked(pay) != Chapter3WalletCredit.Credited)
        {
            HoldDemandHandoverLocked("unpaid", $"Chapter 3 demand {demand.DemandId} holding: ${pay:N0} unpaid; retrying without re-removing the product.");
            return;
        }
        var delivered = Math.Min(demand.Quantity, demand.Delivered + units);
        var previousReceiptId = Chapter3PresentationReceipt.Correlation(
            _ctx.State!.PlayerId, $"demand-{demand.DemandId}-receipt-{demand.HandOverCount}");
        var previousReceiptShown = _ctx.State.PresentationReceipts.Any(
            receipt => receipt.CorrelationId == previousReceiptId);
        var burstUnits = demand.HandOverCount > 0 && !previousReceiptShown
            ? demand.LastTakeUnits + units
            : units;
        var recorded = demand with
        {
            Delivered = delivered,
            PaidTotal = demand.PaidTotal + pay,
            LastTakeUnits = burstUnits,
            HandOverCount = demand.HandOverCount + 1,
            PendingRemoval = null
        };
        _ctx.State = _ctx.State! with { ActiveDemand = recorded, Revision = _ctx.State.Revision + 1 };
        _lastTakeAtSeconds = _ctx.ArthurClock();
        _lastTakeDemandId = demand.DemandId;
        ClearDemandHandoverHoldsLocked();
        _ctx.Log($"Chapter 3 demand {demand.DemandId} hand-over: {units} {demand.ProductName}, paid ${pay:N0} ({delivered}/{demand.Quantity}).");
        if (delivered >= demand.Quantity)
            CompleteDemandLocked(recorded);
    }

    private bool TryAcquireDemandSlotLockLocked(IChapter3ProductionDemandWorld world, int slotIndex)
    {
        if (_ctx.DemandLockedSlotIndex >= 0)
            return _ctx.DemandLockedSlotIndex == slotIndex;
        _ctx.DemandLockedSlotIndex = slotIndex;
        try
        {
            var status = world.TrySetFamilyLockerSlotLocked(slotIndex, true);
            if (status != Release1SmallCourtesyWorldMutationStatus.Succeeded)
            {
                if (status == Release1SmallCourtesyWorldMutationStatus.Ambiguous)
                    ReleaseDemandSlotLockLocked();
                else
                    _ctx.DemandLockedSlotIndex = -1;
                return false;
            }
            return true;
        }
        catch
        {
            ReleaseDemandSlotLockLocked();
            return false;
        }
    }

    internal void ReleaseDemandSlotLockLocked()
    {
        if (_ctx.DemandLockedSlotIndex < 0)
            return;
        var world = _ctx.DemandWorld;
        if (world is null)
        {
            _ctx.DemandLockedSlotIndex = -1;
            return;
        }
        try
        {
            if (world.TrySetFamilyLockerSlotLocked(_ctx.DemandLockedSlotIndex, false) != Release1SmallCourtesyWorldMutationStatus.Succeeded)
                return;
            _ctx.DemandLockedSlotIndex = -1;
        }
        catch
        {
            // Retain the exact lock identity so the next lifecycle teardown can retry it.
        }
    }

    private void HoldDemandLocked(string key, string line)
    {
        if (string.Equals(_ctx.DemandHoldKey, key, StringComparison.Ordinal))
            return;
        _ctx.DemandHoldKey = key;
        _ctx.Log(line);
    }

    private void ClearDemandHoldLocked(string key)
    {
        if (string.Equals(_ctx.DemandHoldKey, key, StringComparison.Ordinal))
            _ctx.DemandHoldKey = null;
    }

    private void HoldDemandHandoverLocked(string reason, string line) =>
        HoldDemandLocked("handover-" + reason, line);

    private void ClearDemandHandoverHoldsLocked()
    {
        if (_ctx.DemandHoldKey is not null && _ctx.DemandHoldKey.StartsWith("handover-", StringComparison.Ordinal))
            _ctx.DemandHoldKey = null;
    }
}
