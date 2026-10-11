using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Release1.World;

namespace OrganizedCrime.Runtime.Chapter3;

// Called under the coordinator's existing gate. No native writes to closets.
internal sealed class Chapter3StorageFeature
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _qa = new();
    private readonly Chapter3FeatureContext _ctx;
    private readonly Chapter3DeliveryFeature _delivery;

    internal Chapter3StorageFeature(Chapter3FeatureContext ctx, Chapter3DeliveryFeature delivery)
    {
        _ctx = ctx;
        _delivery = delivery;
    }

    internal void ClearQaForLoad()
    {
        while (_qa.TryDequeue(out _)) { }
    }

    internal bool Ready()
    {
        if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null || _ctx.State.Phase != Chapter3Phase.Active)
            return false;
        try
        {
            return _ctx.Context.TryRead(out var host) == Release1StoryHostContextReadStatus.Ready &&
                host.PlayerId == _ctx.State.PlayerId;
        }
        catch { return false; }
    }

    private Release1HoldRoomSnapshot? ReadRoom()
    {
        try { return _ctx.StorageRoomReader?.Invoke(); }
        catch { return null; }
    }

    internal bool TryFreezeOffer(Chapter3JobOffer offer, out Chapter3JobOffer frozen)
    {
        frozen = offer;
        if (!Ready() || _ctx.DeadDropWorld is not { } world) return false;
        try
        {
            var room = ReadRoom();
            if (room is null || room.Readiness != Release1HoldRoomReadiness.Ready) return false;
            room.Validate();
            if (!Chapter3StorageRun.PlayerClosetIds.All(id => room.Closets.Any(c => c.ClosetGuid == id))) return false;
            if (world.TryReadProducts(out var products) != Release1SmallCourtesyWorldReadStatus.Ready ||
                world.TryReadDeadDrops(out var drops) != Release1SmallCourtesyWorldReadStatus.Ready ||
                world.TryReadPackaging(Release1SmallCourtesyPackageKind.Brick, out var packaging) != Release1SmallCourtesyWorldReadStatus.Ready)
                return false;
            if (!Chapter3PackageRecoveryFreeze.TryFreeze(products, drops, packaging,
                $"chapter3-storage|{_ctx.State!.PlayerId}|{offer.OfferId}", Chapter3JobTuning.BricksForReward(offer.Reward),
                out var route, out _)) return false;
            frozen = offer with
            {
                JobType = Chapter3JobType.TemporaryStorageSafehouse,
                ProductId = null, ProductName = null, Shutdown = null,
                StorageRun = new Chapter3StorageRun(route!, Chapter3StorageRun.HoldForReward(offer.Reward))
            };
            frozen.StorageRun.Validate(frozen.Reward, offered: true);
            return true;
        }
        catch { return false; }
    }

    internal bool TryAccept(Chapter3JobOffer offer, out Chapter3StorageRun? run)
    {
        run = null;
        if (!Ready() || offer.StorageRun is not { } quoted || _ctx.DeadDropWorld is not { } world) return false;
        try
        {
            if (Chapter3StorageRun.Observe(ReadRoom(), quoted, out _) == Release1HoldObservation.RoomNotReady ||
                !_ctx.TryReadCanonicalMinutesLocked(out _) ||
                world.TryReadProducts(out var products) != Release1SmallCourtesyWorldReadStatus.Ready ||
                world.TryReadDeadDrops(out var drops) != Release1SmallCourtesyWorldReadStatus.Ready ||
                world.TryReadPackaging(Release1SmallCourtesyPackageKind.Brick, out var packaging) != Release1SmallCourtesyWorldReadStatus.Ready)
                return false;
            var route = quoted.Route;
            if (!products.Any(p => p.IsDiscovered && p.ProductId == route.ProductId) || packaging.PackagingId != route.PackagingId ||
                drops.Count(d => d.Guid == route.Pickup.Guid) != 1 || drops.Count(d => d.Guid == route.Handoff.Guid && d.IsEmpty) != 1)
                return false;
            // An earlier ambiguous stage may already have landed; adopt only its exact fingerprint.
            if (!_delivery.TryReadSlotsLocked(route.Pickup.Guid, out var slots) ||
                !_delivery.TryReadSlotsLocked(route.Handoff.Guid, out var handoff) || handoff.Any(s => s.Quantity > 0)) return false;
            var occupied = slots.Where(s => s.Quantity > 0).ToArray();
            var fingerprint = Fingerprint(route);
            if (occupied.Length > 0 && (occupied.Length != 1 || occupied[0].SlotIndex != 0 || !fingerprint.Matches(occupied[0]))) return false;
            if (!_delivery.TryStageRecoveryLocked(route, out var staged, out _)) return false;
            run = quoted with { Route = staged };
            return true;
        }
        catch { return false; }
    }

    internal Chapter3DeliveryFeature.Chapter3DeliveryCheckOutcome Poll(int chapterDay)
    {
        if (!Ready()) return Chapter3DeliveryFeature.Chapter3DeliveryCheckOutcome.NoWork;
        while (_qa.TryDequeue(out var command)) ApplyQa(command, chapterDay);
        if (!Ready() || _ctx.State!.JobBoard.ActiveJob is not { StorageRun: { } run } job)
            return Chapter3DeliveryFeature.Chapter3DeliveryCheckOutcome.NoWork;
        _ctx.LoggedDeliveryHolds.ClearWhere(entry => !entry.StartsWith($"{job.JobId}:", StringComparison.Ordinal));
        if (_ctx.DeadDropWorld is null)
            return Hold(job.JobId, "world-unreadable", "the dead-drop world is unavailable");
        if (!_ctx.TryReadCanonicalMinutesLocked(out var now))
            return Hold(job.JobId, "clock-unreadable", "the game clock was unreadable");
        var route = run.Route;
        if (!route.Staged)
            return Hold(job.JobId, "staging", "the pickup bricks have not been staged");
        if (!route.Custody)
        {
            if (!_delivery.TryReadSlotsLocked(route.Pickup.Guid, out var slots) ||
                !slots.Any(s => s.SlotIndex == route.StagedSlotIndex))
                return Hold(job.JobId, "pickup-unreadable", "the pickup drop was unreadable");
            if (slots.Any(Fingerprint(route).MatchesIdentity))
                return Hold(job.JobId, "awaiting-pickup", "the staged bricks are still in the pickup drop");
            run = run with { Route = route with { Custody = true } };
            Set(run);
            _delivery.RefreshDropSubscriptionsLocked();
        }
        if (!run.Released)
        {
            var observation = Chapter3StorageRun.Observe(ReadRoom(), run, out var closet);
            var decision = Release1ConditionWindow.Decide(observation switch
            {
                Release1HoldObservation.Held => Release1WindowObservation.Clean,
                Release1HoldObservation.Missing => Release1WindowObservation.Breached,
                _ => Release1WindowObservation.Unreadable
            }, new Release1WindowProgress(run.StowedAt, run.MissingSince, run.Released), now,
                new Release1WindowProfile(run.HoldMinutes, 60d, Release1WindowHealPolicy.HealBeforeElapse));
            switch (decision)
            {
                case Release1WindowDecision.OpenWindow:
                    run = run with { StowedAt = now, ClosetGuid = closet, MissingSince = null }; break;
                case Release1WindowDecision.ClearBreach:
                    run = run with { MissingSince = null, ClosetGuid = closet }; break;
                case Release1WindowDecision.RecordBreach:
                    run = run with { MissingSince = now }; break;
                case Release1WindowDecision.Elapsed:
                    run = run with { Released = true, ClosetGuid = closet, MissingSince = null }; break;
                case Release1WindowDecision.Fail:
                    Fail(chapterDay); return Chapter3DeliveryFeature.Chapter3DeliveryCheckOutcome.Held;
                case Release1WindowDecision.None when observation == Release1HoldObservation.Held:
                    run = run with { ClosetGuid = closet }; break;
            }
            Set(run);
            if (!run.Released)
                return Hold(job.JobId, $"room-{observation}", observation switch
                {
                    Release1HoldObservation.Held => "the HQ custody hold has not elapsed",
                    Release1HoldObservation.Missing => "the whole stack is missing from HQ player closets; awaiting stow or breach confirmation",
                    _ => $"the HQ player closet read was {observation}; custody cannot be verified"
                });
        }
        _delivery.RefreshDropSubscriptionsLocked();
        if (!run.Route.RemovalConfirmed && !_delivery.TryStorageHandoffLocked())
            return Chapter3DeliveryFeature.Chapter3DeliveryCheckOutcome.Held;
        run = _ctx.State!.JobBoard.ActiveJob!.StorageRun!;
        if (!TryPay(run, job.JobId, job.Reward)) return Chapter3DeliveryFeature.Chapter3DeliveryCheckOutcome.Held;
        var completed = Chapter3JobScheduler.Complete(_ctx.State.JobBoard, job.JobId, true, chapterDay);
        _ctx.State = _ctx.State with { JobBoard = _delivery.CountPostWarehouseSuccessLocked(completed.Board), Revision = _ctx.State.Revision + 1 };
        _delivery.DisposeDropSubscriptionsLocked();
        _ctx.PendingRemoval = null;
        _ctx.LoggedDeliveryHolds.ClearAll();
        _ctx.Log($"Chapter 3 storage job {job.JobId} paid ${job.Reward:N0} and completed.");
        return Chapter3DeliveryFeature.Chapter3DeliveryCheckOutcome.Completed;
    }

    private Chapter3DeliveryFeature.Chapter3DeliveryCheckOutcome Hold(int jobId, string key, string reason)
    {
        _ctx.LoggedDeliveryHolds.Report($"{jobId}:{key}", $"Chapter 3 job {jobId} storage holding: {reason}.");
        return Chapter3DeliveryFeature.Chapter3DeliveryCheckOutcome.Held;
    }

    private bool TryPay(Chapter3StorageRun run, int jobId, int reward)
    {
        bool Unpaid(string key, string reason)
        {
            Hold(jobId, key, $"reward ${reward:N0} unpaid; {reason}");
            return false;
        }
        if (_ctx.DeadDropWorld is not IWalletWorld walletWorld)
            return Unpaid("wallet-unavailable", "the wallet world is unavailable");
        try
        {
            if (walletWorld.TryReadCashBalance(out var balance) != Release1SmallCourtesyWorldReadStatus.Ready ||
                !float.IsFinite(balance) || balance < 0)
                return Unpaid("wallet-unreadable", "the cash balance was unreadable");
            if (run.CreditBefore.HasValue)
            {
                if (balance == (float)(run.CreditBefore.Value + reward)) return true;
                if (balance != (float)run.CreditBefore.Value)
                    return Unpaid("credit-diverged", "the cash balance diverged from the saved credit receipt; awaiting reconciliation");
            }
            Set(run with { CreditBefore = balance });
            // Success completes under this gate; ambiguous outcomes retain the balance receipt.
            var credited = _ctx.CreditLocked(reward);
            return credited == Chapter3WalletCredit.Credited ||
                Unpaid($"credit-{credited}", $"credit was {credited.ToString().ToLowerInvariant()}; retrying without re-removing the bricks");
        }
        catch (Exception exception)
        {
            return Unpaid("credit-fault", $"the wallet faulted ({exception.GetType().Name}); awaiting reconciliation");
        }
    }

    internal void Set(Chapter3StorageRun run)
    {
        var state = _ctx.State!;
        if (state.JobBoard.ActiveJob!.StorageRun == run) return;
        _ctx.State = state with
        {
            JobBoard = state.JobBoard with { ActiveJob = state.JobBoard.ActiveJob with { StorageRun = run } },
            Revision = state.Revision + 1
        };
    }

    internal void Fail(int chapterDay)
    {
        if (!Ready() || _ctx.State!.JobBoard.ActiveJob is not { StorageRun: { } run } job || run.Reconciling) return;
        var failed = Chapter3JobScheduler.Complete(_ctx.State.JobBoard, job.JobId, false, chapterDay);
        _ctx.State = _ctx.State with { JobBoard = failed.Board, Revision = _ctx.State.Revision + 1 };
        _delivery.ApplyLedgerTickLocked(failed.Effects, chapterDay);
        _delivery.CleanupStorageLocked(job.JobId, run.Route);
        _delivery.DisposeDropSubscriptionsLocked();
        _ctx.LoggedDeliveryHolds.ClearAll();
        _ctx.Log($"Chapter 3 storage job {job.JobId} failed; reward-sized make-good opened.");
    }

    private static Release1ConsignmentFingerprint Fingerprint(Chapter3PackageRecovery route) =>
        new(route.ProductId, route.PackagingId, route.BrickCount);

    internal Chapter3QaResult Queue(string command)
    {
        if (!Ready()) return new(false, "Chapter 3 storage QA refused: canonical host Chapter 3 is not active.");
        if (command != "offer" && command != "expire" && command != "done" &&
            (!double.TryParse(command, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                out var remaining) || !double.IsFinite(remaining) || remaining < 0))
            return new(false, "Usage: oc_ch3 storage hold <minutes|done> | expire | status");
        _qa.Enqueue(command);
        return new(true, $"Chapter 3 storage QA queued: {command}; applies on the next host poll.");
    }

    private void ApplyQa(string command, int chapterDay)
    {
        if (command == "offer")
        {
            var board = _ctx.State!.JobBoard;
            if (board.ActiveJob is not null) { _ctx.Log("Storage QA offer refused: a job is active."); return; }
            var offer = board.OpenOffer ?? new Chapter3JobOffer(board.NextOfferId, Chapter3JobType.TemporaryStorageSafehouse,
                15_000 + ((board.NextOfferId - 1) % 11) * 500, chapterDay, chapterDay + 1);
            if (!TryFreezeOffer(offer, out var frozen)) { _ctx.Log("Storage QA offer refused: prerequisites unreadable or unavailable."); return; }
            _ctx.State = _ctx.State with
            {
                JobBoard = board with
                {
                    OpenOffer = frozen, NextOfferId = Math.Max(board.NextOfferId, frozen.OfferId + 1),
                    LastTypeOffered = frozen.JobType,
                    TypesSeen = board.TypesSeen.Contains(frozen.JobType) ? board.TypesSeen : board.TypesSeen.Append(frozen.JobType).ToArray(),
                    LastRefusedOfferId = null
                },
                Revision = _ctx.State.Revision + 1
            };
            _ctx.Log($"Storage QA offer {frozen.OfferId}: {Chapter3NellCopy.StorageOfferText(frozen.StorageRun!, frozen.Reward)}");
            return;
        }
        var run = _ctx.State!.JobBoard.ActiveJob?.StorageRun;
        if (run is null) { _ctx.Log("Storage QA refused: no active storage run."); return; }
        if (command == "expire")
        {
            // Same failure transition as the ordinary deadline; a pending transaction always wins.
            Fail(chapterDay);
            _ctx.Log(run.Reconciling ? "Storage QA expiry suppressed: payout reconciliation has begun." : "Storage QA deadline expired.");
            return;
        }
        if (run.StowedAt is null || run.Released || !_ctx.TryReadCanonicalMinutesLocked(out var now))
        { _ctx.Log("Storage QA hold refused: stow the whole stack first, with a readable clock."); return; }
        var remaining = command == "done" ? 0d : double.Parse(command, System.Globalization.CultureInfo.InvariantCulture);
        var stowedAt = now - run.HoldMinutes + remaining;
        if (remaining > run.HoldMinutes || stowedAt < 0)
        { _ctx.Log("Storage QA hold refused: requested timer is outside the canonical clock range."); return; }
        Set(run with { StowedAt = stowedAt });
        _ctx.Log($"Storage QA hold remaining set to {remaining:0.##} game minutes.");
    }

    internal IReadOnlyList<string> Status()
    {
        if (!Ready()) return new[] { "storage: canonical host Chapter 3 is not active." };
        var job = _ctx.State!.JobBoard.ActiveJob;
        var run = job?.StorageRun ?? _ctx.State.JobBoard.OpenOffer?.StorageRun;
        if (run is null) return new[] { "storage: none." };
        var stage = job is null ? "offer" : run.Reconciling ? "payout reconciliation" : run.Released ? "deliver" :
            run.Route.Custody ? "hold" : "collect";
        var remaining = run.Released ? "0" : run.StowedAt is { } start && _ctx.TryReadCanonicalMinutesLocked(out var now)
            ? Math.Max(0, start + run.HoldMinutes - now).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "not started/unreadable";
        var observation = Chapter3StorageRun.Observe(ReadRoom(), run, out _);
        int? observed = observation == Release1HoldObservation.Held ? run.Route.BrickCount :
            observation == Release1HoldObservation.Missing ? 0 : null;
        if (stage is "collect" or "deliver")
            observed = _delivery.TryReadSlotsLocked(
                stage == "collect" ? run.Route.Pickup.Guid : run.Route.Handoff.Guid, out var slots)
                ? slots.Where(Fingerprint(run.Route).MatchesIdentity).Sum(s => s.Quantity)
                : null;
        return new[]
        {
            $"storage: stage {stage}; pickup {run.Route.Pickup.Name} ({run.Route.Pickup.Guid}); handoff {run.Route.Handoff.Name} ({run.Route.Handoff.Guid})",
            $"storage: bricks expected {run.Route.BrickCount}, observed {(observed.HasValue ? observed.ToString() : "unreadable/ambiguous")}; room {observation}; closet {run.ClosetGuid ?? "none"}",
            $"storage: stow time {run.StowedAt?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "none"}; hold remaining {remaining} minutes; deadline {(job is null ? "three days after accept" : $"day {job.DeadlineDay}")}; released {run.Released}"
        };
    }

    internal Chapter3PresentationPlan Project(Chapter3PresentationPlan plan)
    {
        if (_ctx.State?.JobBoard.ActiveJob?.StorageRun is null || plan.Quest is not { } quest || quest.Entries.Count < 3)
            return plan;
        try
        {
            var marker = _ctx.StorageRoomMarker?.Invoke();
            if (marker is null || !float.IsFinite(marker.X) || !float.IsFinite(marker.Y) || !float.IsFinite(marker.Z)) return plan;
            var entries = quest.Entries.ToArray();
            entries[1] = entries[1] with { Marker = marker };
            return plan with { Quest = quest with { Entries = entries } };
        }
        catch { return plan; }
    }
}

internal interface IChapter3StorageQaTarget
{
    Chapter3QaResult StorageCommand(string command);
    IReadOnlyList<string> StorageStatus();
}
