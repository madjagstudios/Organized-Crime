using OrganizedCrime.Core.Scheduling;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Release1.World;

namespace OrganizedCrime.Runtime.Chapter3;

// Coordinator gate owns all progress; commands enqueue without adding lock sites.
internal sealed class Chapter3ShutdownFeature
{
    private readonly Chapter3FeatureContext _ctx;
    private readonly Chapter3DeliveryFeature _delivery;
    private readonly GameHourGate _convergence = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _qa = new();
    private static readonly Release1WindowProfile Profile = new(
        Chapter3Shutdown.HoldMinutes, Chapter3Shutdown.GraceMinutes, Release1WindowHealPolicy.HealBeforeElapse);

    internal Chapter3ShutdownFeature(Chapter3FeatureContext ctx, Chapter3DeliveryFeature delivery)
    { _ctx = ctx; _delivery = delivery; }

    internal bool Ready()
    {
        if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State?.Phase != Chapter3Phase.Active) return false;
        try { return _ctx.Context.TryRead(out var host) == Release1StoryHostContextReadStatus.Ready && host.PlayerId == _ctx.State.PlayerId; }
        catch { return false; }
    }

    private Release1ProductionActivitySnapshot? Read()
    {
        try
        {
            if (_ctx.ShutdownWorld is not { } world ||
                world.TryReadProductionActivity(out var snapshot) != Release1SmallCourtesyWorldReadStatus.Ready || snapshot is null) return null;
            snapshot.Validate();
            return snapshot;
        }
        catch { return null; }
    }

    internal bool TryFreezeOffer(Chapter3JobOffer offer, out Chapter3JobOffer frozen)
    {
        frozen = offer;
        if (!Ready()) return false;
        var run = Chapter3Shutdown.Select(Read(), _ctx.State!.PlayerId, offer.OfferId);
        if (run is null) return false;
        try { run.Validate(offer.Reward, offered: true); }
        catch (ArgumentException) { return false; }
        frozen = offer with { JobType = Chapter3JobType.OperationalShutdown, ProductId = null, ProductName = null,
            CashMovement = null, StorageRun = null, Shutdown = run };
        return true;
    }

    // The target may already be quiet at Accept; never replace the quoted property.
    internal bool TryAccept(Chapter3JobOffer offer) => Ready() && offer.CashMovement is null && offer.Shutdown is { } run &&
        Chapter3ShutdownClassifier.Classify(Read(), run.PropertyCode, out _) is Chapter3ShutdownObservation.Quiet or Chapter3ShutdownObservation.Working;

    private bool Hold(int jobId, string key, string reason)
    {
        _ctx.LoggedDeliveryHolds.Report($"{jobId}:shutdown-{key}", $"Chapter 3 job {jobId} shutdown holding: {reason}.");
        return false;
    }

    private void Set(Chapter3Shutdown run)
    {
        var state = _ctx.State!;
        if (state.JobBoard.ActiveJob!.Shutdown == run) return;
        _ctx.State = state with { JobBoard = state.JobBoard with
            { ActiveJob = state.JobBoard.ActiveJob with { Shutdown = run } }, Revision = state.Revision + 1 };
    }

    internal void Poll(int day)
    {
        if (!Ready()) return;
        if (_ctx.State!.JobBoard.ActiveJob is { Shutdown: not null, CashMovement: not null } ambiguous)
        { Hold(ambiguous.JobId, "terms", "cash and shutdown terms are ambiguous"); return; }
        // Away polling can precede the coordinator's day tick. Read the live
        // chapter day before allowing a new payout or applying QA mutations.
        if (_ctx.Clock is { } clock)
        {
            try
            {
                if (!clock.TryReadElapsedDays(out var elapsed) || _ctx.State!.ChapterDayAt(elapsed) is not { } currentDay) return;
                day = Math.Max(day, currentDay);
            }
            catch { return; }
        }
        while (_qa.TryDequeue(out var command)) { ApplyQa(command, day); _convergence.Clear(); }
        if (_ctx.State!.JobBoard.ActiveJob is not { Shutdown: { } run } job) return;
        _ctx.LoggedDeliveryHolds.ClearWhere(key => !key.StartsWith($"{job.JobId}:", StringComparison.Ordinal));
        // Once credit intent is saved, reconcile that receipt even if ownership
        // changes: the wallet effect may already have happened before a save.
        if (run.Reconciling && run.CreditBefore.HasValue) { Finish(job, run, day); return; }
        var clockReady = _ctx.TryReadCanonicalMinutesLocked(out var now);
        if (!_convergence.TryPass(clockReady ? now : null, _ctx.State.Revision)) return;
        var observation = Chapter3ShutdownClassifier.Classify(Read(), run.PropertyCode, out _);
        if (observation == Chapter3ShutdownObservation.NotOwned) { Cancel(job, day); return; }
        if (observation == Chapter3ShutdownObservation.Unreadable) { Hold(job.JobId, "unreadable", "target activity is unreadable"); return; }
        if (run.Reconciling) { Finish(job, run, day); return; }
        if (!clockReady) { Hold(job.JobId, "clock", "game clock is unreadable"); return; }
        if (day > job.DeadlineDay) return;
        var decision = Release1ConditionWindow.Decide(
            observation == Chapter3ShutdownObservation.Quiet ? Release1WindowObservation.Clean : Release1WindowObservation.Breached,
            new(run.QuietSince, run.BreachSince, run.QuietComplete), now, Profile);
        switch (decision)
        {
            case Release1WindowDecision.OpenWindow:
                Set(run with { QuietSince = now });
                _ctx.Log($"Chapter 3 shutdown job {job.JobId}: {run.PropertyName} quiet window started."); break;
            case Release1WindowDecision.RecordBreach:
                Set(run with { BreachSince = now, BreachObservations = 1 });
                _ctx.Log($"Chapter 3 shutdown job {job.JobId}: work observed at {now:0.##}; readable sustained work after 60 minutes fails.");
                break;
            case Release1WindowDecision.ClearBreach:
                Set(run with { BreachSince = null, BreachObservations = 0 }); break;
            case Release1WindowDecision.Fail:
                Set(run with { BreachObservations = run.BreachObservations + 1 }); Fail(day); break;
            case Release1WindowDecision.Elapsed:
                run = run with { QuietComplete = true }; Set(run); Finish(job, run, day); break;
            case Release1WindowDecision.None when observation == Chapter3ShutdownObservation.Working:
                Hold(job.JobId, "batches", "stop staff and let all monitored batches finish before the quiet window"); break;
        }
    }

    private void Finish(Chapter3ActiveJob job, Chapter3Shutdown run, int day)
    {
        if (!Pay(job.JobId, job.Reward, ref run)) return;
        var completed = Chapter3JobScheduler.Complete(_ctx.State!.JobBoard, job.JobId, true, day);
        _ctx.State = _ctx.State with { JobBoard = _delivery.CountPostWarehouseSuccessLocked(completed.Board), Revision = _ctx.State.Revision + 1 };
        _ctx.LoggedDeliveryHolds.ClearAll();
        _ctx.Log($"Chapter 3 shutdown job {job.JobId} paid ${job.Reward:N0} once; {run.PropertyName} can work again.");
    }

    private bool Pay(int jobId, int reward, ref Chapter3Shutdown run)
    {
        if (run.Paid) return true;
        if (_ctx.DeadDropWorld is not IWalletWorld wallet) return Hold(jobId, "wallet", "wallet world is unavailable");
        bool ReadBalance(out float balance)
        {
            balance = 0;
            try { return wallet.TryReadCashBalance(out balance) == Release1SmallCourtesyWorldReadStatus.Ready && float.IsFinite(balance) && balance >= 0; }
            catch { return false; }
        }
        if (!ReadBalance(out var before)) return Hold(jobId, "wallet-read", "wallet balance is unreadable");
        if (run.CreditBefore is { } saved)
        {
            if (before == (float)(saved + reward)) { run = run with { Paid = true, CreditAmbiguous = false }; Set(run); return true; }
            if (before != (float)saved) { run = run with { CreditAmbiguous = true }; Set(run); }
            if (run.CreditAmbiguous) return Hold(jobId, "credit-ambiguous", "wallet diverged from saved payment receipt");
        }
        var expected = (float)((double)before + reward);
        if (!float.IsFinite(expected) || expected <= before) return Hold(jobId, "wallet-range", "wallet cannot represent the payment");
        run = run with { CreditBefore = before }; Set(run);
        if (_ctx.CreditLocked(reward) == Chapter3WalletCredit.Credited)
        {
            run = run with { Paid = true }; Set(run); return true;
        }
        if (!ReadBalance(out var after)) return Hold(jobId, "credit-read", "payment read-back is unreadable; receipt retained");
        if (after == expected) { run = run with { Paid = true }; Set(run); return true; }
        if (after != before) { run = run with { CreditAmbiguous = true }; Set(run); }
        return Hold(jobId, "credit", "payment not confirmed; saved receipt retained");
    }

    internal void Fail(int day)
    {
        if (!Ready() || _ctx.State!.JobBoard.ActiveJob is not { Shutdown: { } run, CashMovement: null } job || run.Reconciling) return;
        var failed = Chapter3JobScheduler.Complete(_ctx.State.JobBoard, job.JobId, false, day);
        _ctx.State = _ctx.State with { JobBoard = failed.Board, Revision = _ctx.State.Revision + 1 };
        _delivery.ApplyLedgerTickLocked(failed.Effects, day); _ctx.LoggedDeliveryHolds.ClearAll();
        _ctx.Log($"Chapter 3 shutdown job {job.JobId} failed; reward-sized make-good opened.");
    }

    private void Cancel(Chapter3ActiveJob job, int day)
    {
        var state = _ctx.State!;
        _ctx.State = state with { JobBoard = state.JobBoard with
            { ActiveJob = null, CooldownUntilDay = day + Chapter3JobTuning.CooldownDays }, Revision = state.Revision + 1 };
        _ctx.LoggedDeliveryHolds.ClearAll();
        _ctx.Log($"Chapter 3 shutdown job {job.JobId} cancelled: {job.Shutdown!.PropertyName} is no longer owned; no debt.");
    }

    internal void ClearQaForLoad() { while (_qa.TryDequeue(out _)) { } _convergence.Clear(); }
    internal Chapter3QaResult Queue(string command)
    {
        if (!Ready()) return new(false, "shutdown QA refused: canonical host Chapter 3 is not active.");
        if (command is not ("offer" or "expire" or "done") &&
            (!double.TryParse(command, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var remaining) ||
                !double.IsFinite(remaining) || remaining < 0 || remaining > Chapter3Shutdown.HoldMinutes))
            return new(false, "Usage: oc_ch3 offer shutdown | shutdown hold <0-720|done> | shutdown expire | shutdown status");
        _qa.Enqueue(command); return new(true, $"shutdown QA queued: {command}; applies on the next host poll.");
    }

    private void ApplyQa(string command, int day)
    {
        if (command == "offer")
        {
            var board = _ctx.State!.JobBoard;
            if (board.ActiveJob is not null) { _ctx.Log("Shutdown QA offer refused: a job is active."); return; }
            var offer = board.OpenOffer ?? new Chapter3JobOffer(board.NextOfferId, Chapter3JobType.OperationalShutdown,
                15000 + ((board.NextOfferId - 1) % 11) * 500, day, day + 1);
            if (!TryFreezeOffer(offer, out var frozen)) { _ctx.Log("Shutdown QA offer refused: no readable operating owned property."); return; }
            _ctx.State = _ctx.State with { JobBoard = board with { OpenOffer = frozen, LastTypeOffered = frozen.JobType,
                NextOfferId = Math.Max(board.NextOfferId, frozen.OfferId + 1), LastRefusedOfferId = null,
                TypesSeen = board.TypesSeen.Contains(frozen.JobType) ? board.TypesSeen : board.TypesSeen.Append(frozen.JobType).ToArray() },
                Revision = _ctx.State.Revision + 1 };
            _ctx.Log($"Shutdown QA offer {frozen.OfferId}: {Chapter3NellCopy.ShutdownOfferText(frozen.Shutdown!, frozen.Reward)}"); return;
        }
        if (_ctx.State!.JobBoard.ActiveJob is not { Shutdown: { } run } job) { _ctx.Log("Shutdown QA refused: no active shutdown job."); return; }
        if (command == "expire")
        {
            if (run.Reconciling) { _ctx.Log("Shutdown QA expiry suppressed: payout reconciliation has begun."); return; }
            var observation = Chapter3ShutdownClassifier.Classify(Read(), run.PropertyCode, out _);
            if (observation == Chapter3ShutdownObservation.NotOwned) { Cancel(job, day); return; }
            Fail(day); _ctx.Log("Shutdown QA deadline expired."); return;
        }
        if (run.Reconciling || run.BreachSince.HasValue || day > job.DeadlineDay ||
            !_ctx.TryReadCanonicalMinutesLocked(out var now) ||
            Chapter3ShutdownClassifier.Classify(Read(), run.PropertyCode, out _) != Chapter3ShutdownObservation.Quiet)
        { _ctx.Log("Shutdown QA hold refused: readable quiet target, clock and unreconciled job required."); return; }
        var remaining = command == "done" ? 0d : double.Parse(command, System.Globalization.CultureInfo.InvariantCulture);
        var start = now - Chapter3Shutdown.HoldMinutes + remaining;
        if (start < 0) { _ctx.Log("Shutdown QA hold refused: adjusted start precedes game time zero."); return; }
        Set(run with { QuietSince = start });
        _ctx.Log($"Shutdown QA quiet remaining {remaining:0.##} minutes.");
    }

    internal IReadOnlyList<string> Status()
    {
        if (!Ready()) return new[] { "shutdown: canonical host Chapter 3 is not active." };
        var board = _ctx.State!.JobBoard; var job = board.ActiveJob;
        var run = job?.Shutdown ?? board.OpenOffer?.Shutdown ?? board.LastCompletedJob?.Shutdown;
        if (run is null) return new[] { "shutdown: none." };
        var observation = Chapter3ShutdownClassifier.Classify(Read(), run.PropertyCode, out var target);
        var clockReady = _ctx.TryReadCanonicalMinutesLocked(out var now);
        string Number(double? value) => value?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "none";
        var remaining = run.QuietComplete ? "0" : run.QuietSince is { } start && clockReady
            ? Number(Math.Max(0, start + Chapter3Shutdown.HoldMinutes - now)) : "unreadable/not started";
        var lines = new List<string>
        {
            $"shutdown: target {run.PropertyName} ({run.PropertyCode}); activity {observation.ToString().ToLowerInvariant()}",
            $"shutdown: quiet start {Number(run.QuietSince)}; remaining {remaining} minutes; complete {run.QuietComplete}; paid {run.Paid}",
            $"shutdown: breach since {Number(run.BreachSince)}; breach observations {run.BreachObservations}; deadline {(job?.Shutdown is null ? "two days after accept/closed" : $"day {job.DeadlineDay}")}",
            $"shutdown: credit before {Number(run.CreditBefore)}; ambiguous {run.CreditAmbiguous}"
        };
        if (target is not null)
        {
            lines.Add($"shutdown: employees working {target.Employees.Count(e => e.IsAnyWorkInProgress)}/{target.Employees.Count}");
            foreach (var kind in Enum.GetValues<Release1ProductionStationKind>())
            {
                var stations = target.Stations.Where(s => s.Kind == kind).ToArray();
                lines.Add($"shutdown: {kind}: {(stations.Any(s => s.Running) ? "running" : "idle")} ({stations.Count(s => s.Running)}/{stations.Length})");
            }
            lines.AddRange(target.Stations.Select(s => $"shutdown: {s.Kind} {s.StationGuid}: {(s.Running ? "running" : "idle")}"));
        }
        else lines.Add("shutdown: employees and monitored station kinds unreadable/unavailable");
        return lines;
    }
}

internal interface IChapter3ShutdownQaTarget
{
    Chapter3QaResult ShutdownCommand(string command);
    IReadOnlyList<string> ShutdownStatus();
}
