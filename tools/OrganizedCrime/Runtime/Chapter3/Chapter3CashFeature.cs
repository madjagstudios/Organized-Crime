using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Release1.World;

namespace OrganizedCrime.Runtime.Chapter3;

// Runs under the existing coordinator gate. The QA queue does not acquire it.
internal sealed class Chapter3CashFeature
{
    private readonly Chapter3FeatureContext _ctx;
    private readonly Chapter3DeliveryFeature _delivery;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _qa = new();
    internal Chapter3CashFeature(Chapter3FeatureContext ctx, Chapter3DeliveryFeature delivery)
    { _ctx = ctx; _delivery = delivery; }

    internal bool Ready()
    {
        if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State?.Phase != Chapter3Phase.Active) return false;
        try { return _ctx.Context.TryRead(out var host) == Release1StoryHostContextReadStatus.Ready && host.PlayerId == _ctx.State.PlayerId; }
        catch { return false; }
    }

    private bool TryLocker(out Release1HoldRoomClosetSnapshot locker)
    {
        locker = null!;
        try
        {
            return _ctx.CashWorld is { } world && world.TryReadHoldRoom(out var room) == Release1SmallCourtesyWorldReadStatus.Ready &&
                Chapter3CashMovement.TryReadLocker(room, out locker);
        }
        catch { return false; }
    }

    private bool Eligible(int stacks) => TryLocker(out var locker) && _ctx.CashWorld is { } world &&
        TryCashSlots(world, locker, out var cash) && cash.Count == 0 && locker.Slots.Count(s => s.Quantity == 0) >= stacks;

    private static bool TryCashSlots(IHoldRoomWorld world, Release1HoldRoomClosetSnapshot locker,
        out IReadOnlyList<Release1TheEnvelopeCashSlot> cash)
    {
        var slots = new List<Release1TheEnvelopeCashSlot>(); cash = slots;
        foreach (var slot in locker.Slots.Where(s => s.Quantity > 0).OrderBy(s => s.SlotIndex))
        {
            if (!TryClassifySlot(world, slot, out var balance)) return false;
            if (balance.HasValue) slots.Add(new(slot.SlotIndex, balance.Value));
        }
        return true;
    }

    // Like The Envelope, a Ready guarded cash read establishes the native type.
    // An unavailable conventional cash ID holds instead of becoming "no cash".
    private static bool TryClassifySlot(IHoldRoomWorld world, Release1SmallCourtesySlotSnapshot slot, out double? balance)
    {
        balance = null;
        try
        {
            var status = world.TryReadHoldRoomSlotCashBalance(Chapter3CashMovement.FamilyLockerGuid, slot.SlotIndex, out var amount);
            if (status == Release1SmallCourtesyWorldReadStatus.Unavailable) return !Chapter3CashMovement.IsCash(slot);
            if (status != Release1SmallCourtesyWorldReadStatus.Ready || !float.IsFinite(amount) || amount < 0) return false;
            balance = amount; return true;
        }
        catch { return false; }
    }

    internal bool TryFreezeOffer(Chapter3JobOffer offer, out Chapter3JobOffer frozen)
    {
        frozen = offer;
        var run = new Chapter3CashMovement(Chapter3CashMovement.AdvanceForReward(offer.Reward));
        try { run.Validate(offer.Reward, true); }
        catch (ArgumentException) { return false; }
        if (!Ready() || !Eligible(run.Stacks)) return false;
        frozen = offer with { JobType = Chapter3JobType.CashMovement, ProductId = null, ProductName = null, StorageRun = null, CashMovement = run, Shutdown = null };
        return true;
    }

    internal bool TryAccept(Chapter3JobOffer offer) => Ready() && offer.Shutdown is null && offer.CashMovement is { } run && Eligible(run.Stacks);

    private bool Hold(int jobId, string key, string reason)
    {
        _ctx.LoggedDeliveryHolds.Report($"{jobId}:cash-{key}", $"Chapter 3 job {jobId} cash holding: {reason}.");
        return false;
    }

    private void Set(Chapter3CashMovement run)
    {
        var state = _ctx.State!;
        if (state.JobBoard.ActiveJob!.CashMovement == run) return;
        _ctx.State = state with
        {
            JobBoard = state.JobBoard with { ActiveJob = state.JobBoard.ActiveJob with { CashMovement = run } },
            Revision = state.Revision + 1
        };
    }

    internal void Poll(int chapterDay)
    {
        if (!Ready()) return;
        if (_ctx.State!.JobBoard.ActiveJob is { CashMovement: not null, Shutdown: not null } ambiguous)
        { Hold(ambiguous.JobId, "terms", "cash and shutdown terms are ambiguous"); return; }
        while (_qa.TryDequeue(out var command)) ApplyQa(command, chapterDay);
        if (_ctx.State!.JobBoard.ActiveJob is not { CashMovement: { } run } job) return;
        _ctx.LoggedDeliveryHolds.ClearWhere(key => !key.StartsWith($"{job.JobId}:", StringComparison.Ordinal));
        // A new late deposit is never consumed, even when this poll precedes the day tick.
        if (!run.Reconciling && chapterDay > job.DeadlineDay) return;
        if (_ctx.CashWorld is not { } world || !TryLocker(out var locker)) { Hold(job.JobId, "unreadable", "family locker J was unreadable"); return; }
        if (run.Plan is null)
        {
            if (!TryCashSlots(world, locker, out var slots)) { Hold(job.JobId, "unreadable", "J cash reads were unreadable"); return; }
            if (slots.Any(s => s.Balance != 1000 || locker.Slots.Single(item => item.SlotIndex == s.SlotIndex).Quantity != 1))
            { Hold(job.JobId, "stacks", "cash requires full $1,000 stacks"); return; }
            var observation = Release1TheEnvelopeClosetClassifier.Classify(
                new(Release1HoldRoomReadiness.Ready, new[] { locker }),
                new[] { new Release1TheEnvelopeClosetCash(Chapter3CashMovement.FamilyLockerGuid, true, slots) },
                1, run.Advance, out _, out _);
            if (observation != Release1TheEnvelopeClosetObservation.Sufficient)
            { Hold(job.JobId, "waiting", $"waiting for {run.Stacks} full cash stacks; partial deposits stay yours"); return; }
            if (!Release1TheEnvelopeClosetPlan.TryBuild(slots, Chapter3CashMovement.FamilyLockerGuid, run.Advance, out var plan))
            { Hold(job.JobId, "waiting", $"waiting for {run.Stacks} full cash stacks; partial deposits stay yours"); return; }
            // Do not start reconciliation merely because a busy slot was observed.
            var first = plan!.Slots[0].SlotIndex;
            if (!Lock(world, first, true)) { Hold(job.JobId, "lock", "first cash slot is busy"); return; }
            try
            {
                if (!ReadBalance(world, first, out var pre) || pre != 1000) { Hold(job.JobId, "changed", "cash changed before the transaction"); return; }
                run = run with { Plan = plan.Serialize() }; Set(run);
            }
            finally { Lock(world, first, false); }
        }
        if (!Consume(world, job.JobId, ref run) || !Pay(job.JobId, job.Reward, ref run)) return;
        var completed = Chapter3JobScheduler.Complete(_ctx.State!.JobBoard, job.JobId, true, chapterDay);
        _ctx.State = _ctx.State with { JobBoard = _delivery.CountPostWarehouseSuccessLocked(completed.Board), Revision = _ctx.State.Revision + 1 };
        _ctx.LoggedDeliveryHolds.ClearAll();
        _ctx.Log($"Chapter 3 cash job {job.JobId} reimbursed ${run.Advance:N0} and paid ${job.Reward:N0} once.");
    }

    private bool Consume(IHoldRoomWorld world, int jobId, ref Chapter3CashMovement run)
    {
        if (!Release1TheEnvelopeClosetPlan.TryParse(run.Plan, Chapter3CashMovement.FamilyLockerGuid, out var plan))
        {
            run = run with { Ambiguous = true }; Set(run);
            return Hold(jobId, "plan-unreadable", "saved cash plan was unreadable; ambiguous transaction requires reconciliation");
        }
        foreach (var slot in plan!.Slots.Skip(run.ConfirmedSlots))
        {
            if (!Lock(world, slot.SlotIndex, true)) return Hold(jobId, "lock", "a planned cash slot is busy");
            try
            {
                if (!TryObserve(world, slot.SlotIndex, out var balance)) return Hold(jobId, "read", "planned cash slot read-back was unreadable");
                if (run.PendingDecrement && balance == slot.PostBalance)
                {
                    run = run with { ConfirmedSlots = run.ConfirmedSlots + 1, PendingDecrement = false, Ambiguous = false }; Set(run); continue;
                }
                if (run.Ambiguous) return Hold(jobId, "ambiguous", "saved partial consumption requires reconciliation; no repeat decrement");
                if (balance != slot.PreBalance)
                {
                    run = run with { Ambiguous = true }; Set(run);
                    return Hold(jobId, "diverged", "planned slot diverged from its saved pre/post balances");
                }
                run = run with { PendingDecrement = true }; Set(run);
                try { world.TryChangeHoldRoomSlotCashBalance(Chapter3CashMovement.FamilyLockerGuid, slot.SlotIndex, (float)(slot.PostBalance - slot.PreBalance)); }
                catch { /* The saved intent must be reconciled by read-back. */ }
                if (!TryObserve(world, slot.SlotIndex, out balance)) return Hold(jobId, "read", "cash decrement read-back was unreadable");
                if (balance != slot.PostBalance)
                {
                    if (balance != slot.PreBalance) { run = run with { Ambiguous = true }; Set(run); }
                    return Hold(jobId, "decrement", "cash decrement not confirmed; saved intent retained");
                }
                run = run with { ConfirmedSlots = run.ConfirmedSlots + 1, PendingDecrement = false }; Set(run);
            }
            finally { Lock(world, slot.SlotIndex, false); }
        }
        return true;
    }

    // Empty must be proven by a readable slot snapshot; failed cash reads are never zero.
    private bool TryObserve(IHoldRoomWorld world, int index, out double? balance)
    {
        balance = null;
        if (!TryLocker(out var locker)) return false;
        var slot = locker.Slots.SingleOrDefault(s => s.SlotIndex == index);
        if (slot is null) return false;
        if (slot.Quantity == 0) { balance = 0; return true; }
        // Readable replacement/merged items are divergence, not unreadability.
        if (slot.Quantity != 1) return true;
        return TryClassifySlot(world, slot, out balance);
    }

    private static bool ReadBalance(IHoldRoomWorld world, int slot, out double balance)
    {
        balance = 0;
        try
        {
            if (world.TryReadHoldRoomSlotCashBalance(Chapter3CashMovement.FamilyLockerGuid, slot, out var read) != Release1SmallCourtesyWorldReadStatus.Ready ||
                !float.IsFinite(read) || read < 0) return false;
            balance = read; return true;
        }
        catch { return false; }
    }
    private static bool Lock(IHoldRoomWorld world, int slot, bool locked)
    {
        try
        {
            var status = world.TrySetHoldRoomSlotLocked(Chapter3CashMovement.FamilyLockerGuid, slot, locked);
            if (locked && status == Release1SmallCourtesyWorldMutationStatus.Ambiguous) Lock(world, slot, false);
            return status == Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }
        catch
        {
            if (locked) Lock(world, slot, false);
            return false;
        }
    }

    private bool Pay(int jobId, int reward, ref Chapter3CashMovement run)
    {
        if (run.Paid) return true;
        if (_ctx.DeadDropWorld is not IWalletWorld wallet) return Hold(jobId, "wallet", "wallet world is unavailable");
        bool Read(out float balance)
        {
            balance = 0;
            try { return wallet.TryReadCashBalance(out balance) == Release1SmallCourtesyWorldReadStatus.Ready && float.IsFinite(balance) && balance >= 0; }
            catch { return false; }
        }
        if (!Read(out var before)) return Hold(jobId, "wallet-read", "wallet balance is unreadable");
        var total = run.Advance + reward;
        if (run.CreditBefore is { } saved)
        {
            if (before == (float)(saved + total)) { run = run with { Paid = true, Ambiguous = false }; Set(run); return true; }
            if (before != (float)saved) { run = run with { Ambiguous = true }; Set(run); }
            if (run.Ambiguous) return Hold(jobId, "credit-ambiguous", "wallet diverged from saved reimbursement/payment receipt");
        }
        // Require a distinguishable, finite float delta before any wallet write.
        var expected = (float)((double)before + total);
        if (!float.IsFinite(expected) || expected <= before) return Hold(jobId, "wallet-range", "wallet cannot represent the reimbursement/payment");
        run = run with { CreditBefore = before }; Set(run);
        _ctx.CreditLocked(total);
        if (!Read(out var after)) return Hold(jobId, "credit-read", "payment read-back is unreadable; saved receipt retained");
        if (after == expected) { run = run with { Paid = true }; Set(run); return true; }
        if (after != before) { run = run with { Ambiguous = true }; Set(run); }
        return Hold(jobId, "credit", "payment not confirmed; receipt retained without another drain");
    }

    internal void Fail(int day)
    {
        if (!Ready() || _ctx.State!.JobBoard.ActiveJob is not { CashMovement: { } run, Shutdown: null } job || run.Reconciling) return;
        var failed = Chapter3JobScheduler.Complete(_ctx.State.JobBoard, job.JobId, false, day);
        _ctx.State = _ctx.State with { JobBoard = failed.Board, Revision = _ctx.State.Revision + 1 };
        _delivery.ApplyLedgerTickLocked(failed.Effects, day); _ctx.LoggedDeliveryHolds.ClearAll();
        _ctx.Log($"Chapter 3 cash job {job.JobId} failed; reward-only make-good opened; locker untouched.");
    }

    internal void ClearQaForLoad() { while (_qa.TryDequeue(out _)) { } }
    internal Chapter3QaResult Queue(string command)
    {
        if (!Ready()) return new(false, "cash QA refused: canonical host Chapter 3 is not active.");
        if (command is not ("offer" or "expire")) return new(false, "Usage: oc_ch3 offer cash | cash expire | cash status");
        _qa.Enqueue(command); return new(true, $"cash QA queued: {command}; applies on the next host poll.");
    }
    private void ApplyQa(string command, int day)
    {
        if (command == "expire")
        {
            var run = _ctx.State!.JobBoard.ActiveJob?.CashMovement;
            if (run is null) { _ctx.Log("Cash QA expiry refused: no active cash job."); return; }
            Fail(day); _ctx.Log(run.Reconciling ? "Cash QA expiry suppressed: transaction reconciliation has begun." : "Cash QA deadline expired."); return;
        }
        var board = _ctx.State!.JobBoard;
        if (board.ActiveJob is not null) { _ctx.Log("Cash QA offer refused: a job is active."); return; }
        var offer = board.OpenOffer ?? new Chapter3JobOffer(board.NextOfferId, Chapter3JobType.CashMovement,
            15000 + ((board.NextOfferId - 1) % 11) * 500, day, day + 1);
        if (!TryFreezeOffer(offer, out var frozen)) { _ctx.Log("Cash QA offer refused: J has cash, insufficient free slots, or an unreadable HQ."); return; }
        _ctx.State = _ctx.State with
        {
            JobBoard = board with { OpenOffer = frozen, LastTypeOffered = frozen.JobType, NextOfferId = Math.Max(board.NextOfferId, frozen.OfferId + 1),
                TypesSeen = board.TypesSeen.Contains(frozen.JobType) ? board.TypesSeen : board.TypesSeen.Append(frozen.JobType).ToArray(), LastRefusedOfferId = null },
            Revision = _ctx.State.Revision + 1
        };
        _ctx.Log($"Cash QA offer {frozen.OfferId}: {Chapter3NellCopy.CashOfferText(frozen.CashMovement!, frozen.Reward)}");
    }

    internal IReadOnlyList<string> Status()
    {
        if (!Ready()) return new[] { "cash: canonical host Chapter 3 is not active." };
        var board = _ctx.State!.JobBoard;
        var job = board.ActiveJob;
        var run = job?.CashMovement ?? board.OpenOffer?.CashMovement ?? board.LastCompletedJob?.CashMovement;
        if (run is null) return new[] { "cash: none." };
        var reward = job?.CashMovement is not null ? job.Reward : board.OpenOffer?.CashMovement is not null ? board.OpenOffer.Reward : board.LastCompletedJob!.Reward;
        int? observed = null;
        if (TryLocker(out var locker) && _ctx.CashWorld is { } world && TryCashSlots(world, locker, out var cash))
            observed = cash.Count(s => s.Balance == 1000 && locker.Slots.Single(item => item.SlotIndex == s.SlotIndex).Quantity == 1);
        var lines = new List<string>
        {
            $"cash: advance ${run.Advance:N0}; reward ${reward:N0}; total ${run.Advance + reward:N0}; J {Chapter3CashMovement.FamilyLockerGuid}",
            $"cash: stacks expected {run.Stacks}, observed {(observed.HasValue ? observed.Value.ToString() : "unreadable")}; plan state {(run.Paid ? "paid" : run.Ambiguous ? "ambiguous" : run.Reconciling ? "consuming" : "waiting")}; deadline {(job?.CashMovement is null ? "two days after accept/closed" : $"day {job.DeadlineDay}")}",
            $"cash: credit receipt before {run.CreditBefore?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}; confirmed {run.Paid}; confirmed decrements {run.ConfirmedSlots}"
        };
        if (Release1TheEnvelopeClosetPlan.TryParse(run.Plan, Chapter3CashMovement.FamilyLockerGuid, out var plan))
            lines.AddRange(plan!.Slots.Select((slot, i) => $"cash: slot {slot.SlotIndex}: {slot.PreBalance:0} -> {slot.PostBalance:0}; {(i < run.ConfirmedSlots ? "confirmed" : i == run.ConfirmedSlots && run.PendingDecrement ? "pending" : "planned")}"));
        return lines;
    }

    internal Chapter3PresentationPlan Project(Chapter3PresentationPlan plan)
    {
        if (_ctx.State?.JobBoard.ActiveJob is not { CashMovement: not null } job ||
            plan.Quest is not { } quest || quest.Key != Chapter3QuestCopy.QuestKey) return plan;
        var depositScope = Chapter3QuestCopy.CashDepositScope(job.JobId);
        var paymentScope = Chapter3QuestCopy.CashPaymentScope(job.JobId);
        if (quest.Entries.Count(e => e.Scope == depositScope) != 1 ||
            quest.Entries.Count(e => e.Scope == paymentScope) != 1) return plan;
        try
        {
            var marker = _ctx.StorageRoomMarker?.Invoke();
            if (marker is null || !float.IsFinite(marker.X) || !float.IsFinite(marker.Y) || !float.IsFinite(marker.Z)) return plan;
            return plan with { Quest = quest with { Entries = quest.Entries.Select(e =>
                e.Scope == depositScope || e.Scope == paymentScope ? e with { Marker = marker } : e).ToArray() } };
        }
        catch { return plan; }
    }
}

internal interface IChapter3CashQaTarget
{
    Chapter3QaResult CashCommand(string command);
    IReadOnlyList<string> CashStatus();
}
