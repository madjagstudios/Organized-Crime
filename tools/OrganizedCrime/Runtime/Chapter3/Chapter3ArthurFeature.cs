using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

// OC-152: the Chapter 3 Arthur feature (visits and beatings). Moved verbatim
// out of Chapter3RuntimeService; only the receiver changed. Called only
// while the coordinator holds its gate; takes no locks itself. The beating
// callbacks now close over this feature instead of the service: same call
// timing and targets, strictly less retained.
internal sealed class Chapter3ArthurFeature
{
    private readonly Chapter3FeatureContext _ctx;

    public Chapter3ArthurFeature(Chapter3FeatureContext ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    private void TickVisitActiveLocked(float now)
    {
        var progress = _ctx.VisitProgress!;
        if (_ctx.ArthurPump.State is Chapter3ArthurVisitState.Parked or Chapter3ArthurVisitState.AtHq)
        {
            if (progress.Phase == Chapter3ArthurVisitEnginePhase.Leaving)
            {
                _ctx.VisitProgress = null;
                return;
            }
            if (progress.Phase == Chapter3ArthurVisitEnginePhase.Lining)
            {
                // The pump ended the visit under us (the player entered
                // the HQ): mid-lines, that counts as walking off on him.
                ResolveVisitDeliveredLocked(progress, ranOff: true, now, "the player entered the HQ mid-visit");
                return;
            }
            _ctx.VisitProgress = null;
            _ctx.Log("Chapter 3 Arthur visit abandoned before he arrived: the pump is no longer in the visit.");
            return;
        }
        if (_ctx.ArthurPump.State == Chapter3ArthurVisitState.Leaving && progress.Phase != Chapter3ArthurVisitEnginePhase.Leaving)
        {
            _ctx.VisitProgress = null;
            _ctx.Log("Chapter 3 Arthur visit abandoned: the walk ended outside the visit engine.");
            return;
        }
        switch (progress.Phase)
        {
            case Chapter3ArthurVisitEnginePhase.Coming:
                TickVisitComingLocked(progress, now);
                break;
            case Chapter3ArthurVisitEnginePhase.Beating:
                return;
            case Chapter3ArthurVisitEnginePhase.Lining:
                TickVisitLiningLocked(progress, now);
                break;
            default:
                if (now - progress.LeavingSince > Chapter3ArthurVisitPump.LeaveSeconds + 5f)
                {
                    if (_ctx.ArthurPump.State is Chapter3ArthurVisitState.Coming or Chapter3ArthurVisitState.Arrived)
                    {
                        BeginVisitLeaveLocked(progress, now);
                        _ctx.Log("Chapter 3 Arthur visit: the leave walk never took; re-issued.");
                    }
                    else
                    {
                        _ctx.VisitProgress = null;
                    }
                }
                break;
        }
    }

    private void TickVisitComingLocked(Chapter3ArthurVisitProgress progress, float now)
    {
        if (progress.Kind == Chapter3ArthurVisitKind.Warning && !HasWarningDueLocked())
        {
            CancelVisitLocked(progress, now, "paid before he arrived");
            return;
        }
        if (progress.Kind == Chapter3ArthurVisitKind.Beating && !HasBeatingDueLocked())
        {
            CancelVisitLocked(progress, now, "paid before he arrived");
            return;
        }
        if (PlayerInVehicleLocked())
        {
            CancelVisitLocked(progress, now, "the player got into a vehicle");
            return;
        }
        if (_ctx.ArthurPump.State != Chapter3ArthurVisitState.Arrived)
            return;
        if (!TryReadArthurDistanceLocked(out var distance))
            return;
        if (distance > VisitRanOffMetres)
        {
            // The come bound faced him where he is, too far to talk to:
            // nobody heard anything, so this retries another evening.
            CancelVisitLocked(progress, now, $"he never got close ({distance:0.0} m)");
            return;
        }
        if (progress.Kind == Chapter3ArthurVisitKind.Beating)
        {
            StartScheduledBeatingLocked(progress, now);
            return;
        }
        SayVisitLineLocked(progress, now);
    }

    private void TickVisitLiningLocked(Chapter3ArthurVisitProgress progress, float now)
    {
        if (progress.Kind == Chapter3ArthurVisitKind.Warning && !progress.PaidMidVisit && !HasWarningDueLocked())
        {
            progress.PaidMidVisit = true;
            _ctx.Log("Chapter 3 Arthur warning visit: paid mid-visit; he finishes the lines.");
        }
        if (PlayerInVehicleLocked())
        {
            ResolveVisitDeliveredLocked(progress, ranOff: true, now, "the player got into a vehicle");
            return;
        }
        if (TryReadArthurDistanceLocked(out var distance) && distance > VisitRanOffMetres)
        {
            ResolveVisitDeliveredLocked(progress, ranOff: true, now, $"the player moved {distance:0.0} m away");
            return;
        }
        if (now < progress.NextLineAt)
            return;
        SayVisitLineLocked(progress, now);
    }

    private void SayVisitLineLocked(Chapter3ArthurVisitProgress progress, float now)
    {
        if (progress.NextLineIndex >= progress.Lines.Count)
        {
            ResolveVisitDeliveredLocked(progress, ranOff: false, now, "all lines delivered");
            return;
        }
        var world = _ctx.ArthurWorld!;
        Chapter3ArthurMutationStatus status;
        string reason;
        try { status = world.TrySayArthur(progress.Lines[progress.NextLineIndex], out reason); }
        catch (Exception exception) { status = Chapter3ArthurMutationStatus.Ambiguous; reason = $"threw {exception.GetType().Name}"; }
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            progress.NextLineAt = now + VisitTickIntervalSeconds;
            _ctx.Log($"Chapter 3 Arthur visit line {progress.NextLineIndex + 1} holding: {reason}");
            return;
        }
        progress.Phase = Chapter3ArthurVisitEnginePhase.Lining;
        progress.NextLineIndex++;
        progress.NextLineAt = now + VisitLineIntervalSeconds;
        _ctx.Log($"Chapter 3 Arthur visit says line {progress.NextLineIndex}/{progress.Lines.Count}: {reason}");
    }

    private void ResolveVisitDeliveredLocked(Chapter3ArthurVisitProgress progress, bool ranOff, float now, string why)
    {
        if (!progress.Resolved)
        {
            progress.Resolved = true;
            RecordVisitDeliveredLocked(progress.Kind, progress.ChapterDay, ranOff, progress.PaidMidVisit);
        }
        BeginVisitLeaveLocked(progress, now);
        if (progress.PaidMidVisit && progress.Kind == Chapter3ArthurVisitKind.Warning)
            _ctx.Log($"Chapter 3 Arthur warning visit finished after payment ({why}); nothing recorded.");
        else if (ranOff && progress.Kind == Chapter3ArthurVisitKind.Warning)
            _ctx.Log($"Chapter 3 Arthur warning visit delivered ({why}); Nell's ran-off text is queued.");
        else if (progress.Kind == Chapter3ArthurVisitKind.Warning)
            _ctx.Log($"Chapter 3 Arthur warning visit delivered ({why}); Nell's follow-up text is queued.");
        else
            _ctx.Log($"Chapter 3 Arthur cooperation visit delivered ({why}).");
    }

    private void CancelVisitLocked(Chapter3ArthurVisitProgress progress, float now, string why)
    {
        progress.Resolved = true;
        BeginVisitLeaveLocked(progress, now);
        _ctx.Log($"Chapter 3 Arthur {Chapter3FeatureContext.DescribeVisitKind(progress.Kind)} visit cancelled: {why}.");
    }

    private void BeginVisitLeaveLocked(Chapter3ArthurVisitProgress progress, float now)
    {
        progress.Phase = Chapter3ArthurVisitEnginePhase.Leaving;
        progress.LeavingSince = now;
        if (_ctx.ArthurPump.State is Chapter3ArthurVisitState.Coming or Chapter3ArthurVisitState.Arrived)
        {
            try
            {
                if (_ctx.ArthurWorld!.TryLeaveArthur(out var leaveReason) == Chapter3ArthurMutationStatus.Succeeded)
                {
                    _ctx.ArthurPump.BeginLeave();
                    _ctx.Log($"Chapter 3 Arthur leave walk issued: {leaveReason}");
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// OC-135: an attack starts the beating right there instead of
    /// the old record-and-leave. Host and Active only, idle without
    /// the beating worlds, and a running beating ignores further
    /// hits. At the HQ Arthur stays; on a visit the engine hands
    /// over and the coordinator parks him after the wake.
    /// </summary>
    internal void StartAttackBeatingLocked()
    {
        if (_ctx.Beating is not null)
            return;
        if (_ctx.ArthurPump.State is not (Chapter3ArthurVisitState.Coming or Chapter3ArthurVisitState.Arrived or Chapter3ArthurVisitState.AtHq))
            return;
        var atHq = _ctx.ArthurPump.State == Chapter3ArthurVisitState.AtHq;
        var chapterDay = _ctx.VisitProgress?.ChapterDay ?? 0;
        if (_ctx.VisitProgress is null && !TryReadVisitDayLocked(out chapterDay, out _))
            chapterDay = 0;
        BeginBeatingLocked(Chapter3BeatingKind.Attack, chapterDay, parkArthur: !atHq);
    }

    /// <summary>
    /// OC-135: a Beating-kind visit that reached the player hands
    /// over to the beating coordinator. The handover marks the visit
    /// resolved; the coordinator owns the ledger, the walk home, and
    /// the note to Nell.
    /// </summary>
    private void StartScheduledBeatingLocked(Chapter3ArthurVisitProgress progress, float now)
    {
        _ = now;
        BeginBeatingLocked(Chapter3BeatingKind.Scheduled, progress.ChapterDay, parkArthur: true);
    }

    internal void BeginBeatingLocked(Chapter3BeatingKind kind, int chapterDay, bool parkArthur)
    {
        if (_ctx.Beating is not null)
            return;
        if (_ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
            return;
        var world = _ctx.BeatingWorld;
        var hq = _ctx.BeatingHq;
        var arthur = _ctx.ArthurWorld;
        if (world is null || hq is null || arthur is null)
        {
            _ctx.Log("Chapter 3 Arthur beating refused: the beating worlds are not wired up.");
            return;
        }
        if (!world.IsHost)
        {
            _ctx.Log("Chapter 3 Arthur beating refused: host only.");
            return;
        }
        if (!world.IsPlayerSpawned)
        {
            _ctx.Log("Chapter 3 Arthur beating refused: the player is not spawned yet.");
            return;
        }
        var progress = _ctx.VisitProgress;
        if (progress is not null)
        {
            progress.Resolved = true;
            progress.Phase = Chapter3ArthurVisitEnginePhase.Beating;
        }
        var day = chapterDay;
        _ctx.Beating = new Chapter3ArthurBeating(
            world,
            arthur,
            hq,
            kind,
            parkArthur,
            _ctx.ArthurPump.State,
            _ctx.ArthurClock(),
            _ctx.Log,
            () => RecordBeatingLocked(kind, day),
            () => _ctx.Log("Chapter 3 Arthur beating: Nell's text is queued."),
            ParkBeatingArthurLocked);
        _ctx.Log($"Chapter 3 Arthur beating begins ({(kind == Chapter3BeatingKind.Scheduled ? "scheduled" : "attack")}).");
    }

    /// <summary>
    /// OC-135: one beating pass. A world that went away aborts (the
    /// coordinator unlocks); a finished beating clears the visit it
    /// took over, so the scheduler fires fresh afterwards.
    /// </summary>
    internal void TickBeatingLocked()
    {
        var beating = _ctx.Beating;
        if (beating is null)
            return;
        if (_ctx.BeatingWorld is null || _ctx.BeatingHq is null || _ctx.ArthurWorld is null)
        {
            AbortBeatingLocked("a beating world went away.");
            return;
        }
        beating.Tick(_ctx.ArthurClock(), _ctx.ArthurPump.State);
        if (!beating.IsDone)
            return;
        if (_ctx.VisitProgress is not null && _ctx.VisitProgress.Phase == Chapter3ArthurVisitEnginePhase.Beating)
            _ctx.VisitProgress = null;
        _ctx.Beating = null;
    }

    /// <summary>
    /// OC-135: the fail-safe when a beating world goes away. Unlocks
    /// through the coordinator (which is idempotent) and clears the
    /// taken-over visit. Before the move nothing records; after the
    /// move the beating counts, so the coordinator finishes with a
    /// fault, still recording.
    /// </summary>
    private void AbortBeatingLocked(string reason)
    {
        var beating = _ctx.Beating;
        if (beating is null)
            return;
        beating.AbortOutside(reason);
        if (_ctx.VisitProgress is not null && _ctx.VisitProgress.Phase == Chapter3ArthurVisitEnginePhase.Beating)
            _ctx.VisitProgress = null;
        _ctx.Beating = null;
    }

    /// <summary>
    /// OC-135: the fail-safe for scene unload (reload or dispose).
    /// Unlocks through the coordinator and clears the taken-over
    /// visit, but never records: an in-memory ledger change would
    /// never be saved.
    /// </summary>
    internal void AbortBeatingForUnloadLocked(string reason)
    {
        var beating = _ctx.Beating;
        if (beating is null)
            return;
        beating.AbortForUnload(reason);
        if (_ctx.VisitProgress is not null && _ctx.VisitProgress.Phase == Chapter3ArthurVisitEnginePhase.Beating)
            _ctx.VisitProgress = null;
        _ctx.Beating = null;
    }

    /// <summary>
    /// OC-135: the beating's record step, run by the coordinator only
    /// after the wake completes. A scheduled beating records every
    /// BeatingDue make-good; an attack beats every overdue debt at
    /// once. Either way the counter queues Nell's text through the
    /// planner.
    /// </summary>
    private void RecordBeatingLocked(Chapter3BeatingKind kind, int chapterDay)
    {
        if (_ctx.State is null)
            return;
        var ledger = _ctx.State.Ledger;
        if (kind == Chapter3BeatingKind.Scheduled)
        {
            foreach (var entry in ledger.Entries.Where(entry => entry.Stage == Chapter3MakeGoodStage.BeatingDue).ToList())
                ledger = Chapter3MakeGoods.RecordBeatingDelivered(ledger, entry.Id, chapterDay).Ledger;
        }
        else
        {
            ledger = Chapter3MakeGoods.RecordBeatingByAttack(ledger, chapterDay).Ledger;
        }
        _ctx.State = _ctx.State with { Ledger = ledger, ArthurBeatings = _ctx.State.ArthurBeatings + 1, Revision = _ctx.State.Revision + 1 };
        _ctx.Log($"Chapter 3 Arthur beating recorded ({(kind == Chapter3BeatingKind.Scheduled ? "scheduled" : "attack")}).");
    }

    /// <summary>
    /// OC-135: the beating's park step. The pump drops back to parked
    /// first (plain field sets, cannot fail), then Arthur parks
    /// through the world; a throwing park still aborts the beating
    /// through the coordinator's own guard, with the pump consistent.
    /// </summary>
    private void ParkBeatingArthurLocked()
    {
        _ctx.ArthurPump.ParkAfterBeating();
        _ctx.ArthurWorld!.TryParkArthur(out _);
    }

    internal enum Chapter3ArthurVisitEnginePhase { Coming, Lining, Leaving, Beating }

    internal sealed class Chapter3ArthurVisitProgress
    {
        public Chapter3ArthurVisitKind Kind;
        public Chapter3ArthurVisitEnginePhase Phase;
        public IReadOnlyList<string> Lines = Array.Empty<string>();
        public int NextLineIndex;
        public float NextLineAt;
        public float LeavingSince;
        public int AmountOwed;
        public int ChapterDay;
        public bool Resolved;
        public bool PaidMidVisit;
    }

    private const float VisitTickIntervalSeconds = 1.0f;
    private const float VisitLineIntervalSeconds = 4.5f;
    private const float VisitRanOffMetres = 15.0f;

    /// <summary>
    /// OC-134: one visit-engine pass, throttled to a second of wall
    /// clock: the ticket's "at most once per second of real time".
    /// Host only, Chapter 3 Active only, and idle without a world.
    /// </summary>
    internal void TickArthurVisitLocked()
    {
        if (_ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null || _ctx.ArthurWorld is null || !_ctx.ArthurWorld.HasAuthority)
            return;
        // OC-135: the visit engine pauses while a beating runs; the
        // beating finalizes the visit when it completes or aborts.
        if (_ctx.Beating is not null)
            return;
        var now = _ctx.ArthurClock();
        if (now - _ctx.LastVisitTickAt < VisitTickIntervalSeconds && now >= _ctx.LastVisitTickAt)
            return;
        _ctx.LastVisitTickAt = now;
        if (_ctx.VisitProgress is null)
            TickVisitIdleLocked();
        else
            TickVisitActiveLocked(now);
    }

    private void TickVisitIdleLocked()
    {
        // A pump-level visit (owner QA come) owns Arthur until it parks.
        if (_ctx.ArthurPump.State is Chapter3ArthurVisitState.Coming or Chapter3ArthurVisitState.Arrived or Chapter3ArthurVisitState.Leaving)
            return;
        // A completed job resets the streak's issue flag, which re-arms
        // the next streak's cooperation visit.
        if (!_ctx.State!.JobBoard.CooperationVisitIssued && _ctx.State.ArthurCooperationDelivered)
            _ctx.State = _ctx.State with { ArthurCooperationDelivered = false, Revision = _ctx.State.Revision + 1 };
        var desired = Chapter3ArthurVisitScheduler.DesiredPending(
            _ctx.State.Ledger, _ctx.State.JobBoard.CooperationVisitIssued && !_ctx.State.ArthurCooperationDelivered);
        if (desired == Chapter3ArthurVisitKind.None)
        {
            ClearVisitSeriesLocked();
            return;
        }
        if (!TryReadVisitDayLocked(out var chapterDay, out var minuteOfDay))
            return;
        var missedBefore = _ctx.State.ArthurVisitMissedEvenings;
        var lastBefore = _ctx.State.ArthurVisitLastAttemptedDay;
        var decision = Chapter3ArthurVisitScheduler.Decide(
            _ctx.State.ArthurVisitPending, missedBefore, lastBefore,
            desired, chapterDay, minuteOfDay, TryReadVisitConditionsLocked(out _));
        SetVisitSeriesLocked(decision.PendingKind, decision.MissedEvenings, decision.LastAttemptedDay);
        if (decision.MissedEvenings > missedBefore)
            _ctx.Log($"Chapter 3 Arthur {Chapter3FeatureContext.DescribeVisitKind(decision.PendingKind)} visit: the evening passed with no visit ({decision.MissedEvenings} missed).");
        if (decision.Action == Chapter3ArthurVisitAction.StartVisit)
        {
            if (!StartVisitLocked(decision.ActionKind, chapterDay, out _))
                SetVisitSeriesLocked(decision.PendingKind, decision.MissedEvenings, lastBefore);
        }
        else if (decision.Action == Chapter3ArthurVisitAction.PhoneFallback)
        {
            PhoneFallbackLocked(decision.ActionKind, chapterDay);
        }
    }

    private void ClearVisitSeriesLocked()
    {
        if (_ctx.State!.ArthurVisitPending == Chapter3ArthurVisitKind.None && _ctx.State.ArthurVisitMissedEvenings == 0 && _ctx.State.ArthurVisitLastAttemptedDay == -1)
            return;
        _ctx.State = _ctx.State with { ArthurVisitPending = Chapter3ArthurVisitKind.None, ArthurVisitMissedEvenings = 0, ArthurVisitLastAttemptedDay = -1, Revision = _ctx.State.Revision + 1 };
    }

    internal void SetVisitSeriesLocked(Chapter3ArthurVisitKind pending, int missed, int last)
    {
        if (_ctx.State!.ArthurVisitPending == pending && _ctx.State.ArthurVisitMissedEvenings == missed && _ctx.State.ArthurVisitLastAttemptedDay == last)
            return;
        _ctx.State = _ctx.State with { ArthurVisitPending = pending, ArthurVisitMissedEvenings = missed, ArthurVisitLastAttemptedDay = last, Revision = _ctx.State.Revision + 1 };
    }

    internal bool TryReadVisitDayLocked(out int chapterDay, out int minuteOfDay)
    {
        chapterDay = 0;
        minuteOfDay = 0;
        if (_ctx.State!.StartElapsedDay is null)
            return false;
        if (!_ctx.TryReadCanonicalMinutesLocked(out var totalMinutes))
            return false;
        var day = (int)Math.Floor(totalMinutes / 1440d) - _ctx.State.StartElapsedDay.Value;
        if (day < 0)
            return false;
        chapterDay = day;
        minuteOfDay = (int)(totalMinutes % 1440d);
        return true;
    }

    /// <summary>
    /// OC-134: the gated visit conditions. The HQ pocket room reads as
    /// outdoors to the property check (it is no property), so the
    /// presence the pump already uses closes that hole here too.
    /// </summary>
    internal bool TryReadVisitConditionsLocked(out string detail)
    {
        detail = "the visit conditions were not wired up.";
        var conditions = _ctx.VisitConditions;
        if (conditions is null)
            return false;
        Chapter3ArthurVisitConditions snapshot;
        try
        {
            if (!conditions.TryRead(out snapshot) || snapshot is null)
            {
                detail = "the visit conditions were unreadable.";
                return false;
            }
        }
        catch
        {
            detail = "the visit conditions threw.";
            return false;
        }
        bool insideHq;
        try { insideHq = _ctx.HqPresence is not null && _ctx.HqPresence.PlayerInsideHq; }
        catch { insideHq = true; }
        if (insideHq)
        {
            detail = "the player is inside the HQ.";
            return false;
        }
        detail = snapshot.Detail;
        return snapshot.Met;
    }

    /// <summary>
    /// OC-134: starts an engine visit through the pump's own come path.
    /// False leaves no progress: the caller restores the series.
    /// </summary>
    internal bool StartVisitLocked(Chapter3ArthurVisitKind kind, int chapterDay, out string reason)
    {
        reason = "Arthur is not parked.";
        if (_ctx.ArthurPump.State != Chapter3ArthurVisitState.Parked && _ctx.ArthurPump.State != Chapter3ArthurVisitState.AtHq)
            return false;
        var lines = VisitLinesLocked(kind, out var amount);
        if (lines is null)
        {
            reason = "nothing is due.";
            return false;
        }
        var world = _ctx.ArthurWorld!;
        Chapter3ArthurMutationStatus status;
        try { status = world.TryComeArthur(out reason); }
        catch (Exception exception) { status = Chapter3ArthurMutationStatus.Ambiguous; reason = $"threw {exception.GetType().Name}"; }
        if (status != Chapter3ArthurMutationStatus.Succeeded || !_ctx.ArthurPump.BeginCome())
        {
            if (status == Chapter3ArthurMutationStatus.Succeeded)
                reason = "the pump refused the come.";
            ReportVisitStartFailureLocked(kind, reason);
            return false;
        }
        _ctx.LastVisitStartFailure = null;
        _ctx.VisitProgress = new Chapter3ArthurVisitProgress
        {
            Kind = kind,
            Phase = Chapter3ArthurVisitEnginePhase.Coming,
            Lines = lines,
            ChapterDay = chapterDay,
            AmountOwed = amount
        };
        _ctx.Log($"Chapter 3 Arthur {Chapter3FeatureContext.DescribeVisitKind(kind)} visit started: {reason}");
        return true;
    }

    private void ReportVisitStartFailureLocked(Chapter3ArthurVisitKind kind, string reason)
    {
        var key = $"{kind}:{reason}";
        if (key == _ctx.LastVisitStartFailure)
            return;
        _ctx.LastVisitStartFailure = key;
        _ctx.Log($"Chapter 3 Arthur {Chapter3FeatureContext.DescribeVisitKind(kind)} visit holding: {reason}");
    }

    private IReadOnlyList<string>? VisitLinesLocked(Chapter3ArthurVisitKind kind, out int amount)
    {
        amount = 0;
        if (kind == Chapter3ArthurVisitKind.Cooperation)
            return Chapter3ArthurCopy.CooperationLines();
        if (kind == Chapter3ArthurVisitKind.Beating)
        {
            // The beating speaks its own line through the coordinator; the engine
            // carries no lines, only the validation that something is BeatingDue.
            var beaten = _ctx.State!.Ledger.Entries.Where(entry => entry.Stage == Chapter3MakeGoodStage.BeatingDue).ToList();
            if (beaten.Count == 0)
                return null;
            amount = beaten.Sum(entry => entry.Amount);
            return Array.Empty<string>();
        }
        var warned = _ctx.State!.Ledger.Entries.Where(entry => entry.Stage == Chapter3MakeGoodStage.WarningDue).ToList();
        if (warned.Count == 0)
            return null;
        amount = warned.Sum(entry => entry.Amount);
        return Chapter3ArthurCopy.WarningLines(amount);
    }

    internal bool PhoneFallbackLocked(Chapter3ArthurVisitKind kind, int chapterDay)
    {
        if (kind == Chapter3ArthurVisitKind.Beating)
        {
            _ctx.Log("Chapter 3 Arthur beating visit: no phone fallback, ever; the window widens instead.");
            return false;
        }
        var phone = _ctx.ArthurPhone;
        if (phone is null)
        {
            _ctx.Log($"Chapter 3 Arthur {Chapter3FeatureContext.DescribeVisitKind(kind)} visit: two evenings missed, but the phone was not wired up.");
            return false;
        }
        IReadOnlyList<string> stages;
        if (kind == Chapter3ArthurVisitKind.Cooperation)
        {
            stages = new[] { Chapter3ArthurCopy.PhoneCooperation };
        }
        else
        {
            var warned = _ctx.State!.Ledger.Entries.Where(entry => entry.Stage == Chapter3MakeGoodStage.WarningDue).ToList();
            if (warned.Count == 0)
                return false;
            stages = new[] { Chapter3ArthurCopy.PhoneWarning(warned.Sum(entry => entry.Amount)) };
        }
        string reason;
        bool called;
        try { called = phone.TryCall(kind, stages, chapterDay, out reason); }
        catch (Exception exception) { called = false; reason = $"threw {exception.GetType().Name}"; }
        if (!called)
        {
            _ctx.Log($"Chapter 3 Arthur {Chapter3FeatureContext.DescribeVisitKind(kind)} call failed: {reason}");
            return false;
        }
        RecordVisitDeliveredLocked(kind, chapterDay, ranOff: false);
        if (kind == Chapter3ArthurVisitKind.Warning)
            _ctx.Log($"Chapter 3 Arthur warning call delivered: {reason} Nell's follow-up text is queued.");
        else
            _ctx.Log($"Chapter 3 Arthur cooperation call delivered: {reason}");
        return true;
    }

    /// <summary>
    /// OC-134: records a delivered visit. A warning lands on every
    /// WarningDue make-good plus Nell's follow-up (or her ran-off
    /// text); a cooperation visit only flips its own delivered flag
    /// and never touches the ledger. Paid mid-visit warnings record
    /// nothing: the lines finished, but there is nothing left to enforce.
    /// </summary>
    private void RecordVisitDeliveredLocked(Chapter3ArthurVisitKind kind, int chapterDay, bool ranOff, bool paidMidVisit = false)
    {
        if (kind == Chapter3ArthurVisitKind.Cooperation)
        {
            if (!_ctx.State!.ArthurCooperationDelivered)
                _ctx.State = _ctx.State with { ArthurCooperationDelivered = true, Revision = _ctx.State.Revision + 1 };
            return;
        }
        if (kind == Chapter3ArthurVisitKind.Beating)
        {
            // The beating coordinator records the ledger and notes Nell itself;
            // the engine never records a beating visit.
            _ctx.Log("Chapter 3 Arthur beating visit resolved by the coordinator.");
            return;
        }
        if (paidMidVisit)
            return;
        var ledger = _ctx.State!.Ledger;
        foreach (var entry in ledger.Entries.Where(entry => entry.Stage == Chapter3MakeGoodStage.WarningDue).ToList())
            ledger = Chapter3MakeGoods.RecordWarningDelivered(ledger, entry.Id, chapterDay).Ledger;
        _ctx.State = ranOff
            ? _ctx.State with { Ledger = ledger, ArthurRanOffs = _ctx.State.ArthurRanOffs + 1, Revision = _ctx.State.Revision + 1 }
            : _ctx.State with { Ledger = ledger, ArthurWarningFollowups = _ctx.State.ArthurWarningFollowups + 1, Revision = _ctx.State.Revision + 1 };
    }

    internal bool HasWarningDueLocked() =>
        _ctx.State!.Ledger.Entries.Any(entry => entry.Stage == Chapter3MakeGoodStage.WarningDue);

    internal bool HasBeatingDueLocked() =>
        _ctx.State!.Ledger.Entries.Any(entry => entry.Stage == Chapter3MakeGoodStage.BeatingDue);

    private bool PlayerInVehicleLocked()
    {
        var conditions = _ctx.VisitConditions;
        if (conditions is null)
            return false;
        try
        {
            return conditions.TryRead(out var snapshot) && snapshot is not null && !snapshot.OnFoot;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadArthurDistanceLocked(out float distance)
    {
        distance = float.NaN;
        try
        {
            if (_ctx.ArthurWorld!.TryReadArthur(out var snapshot) != Chapter3ArthurReadStatus.Ready || snapshot is null)
                return false;
            if (float.IsNaN(snapshot.DistanceToPlayer))
                return false;
            distance = snapshot.DistanceToPlayer;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
