using OrganizedCrime.Core.Scheduling;

namespace OrganizedCrime.Runtime;

/// <summary>
/// What <see cref="Chapter3ArthurVisitPump.Pump"/> reports on a pass that
/// actually has something to log. Every other pass returns null.
/// </summary>
public enum Chapter3ArthurVisitOutcome
{
    Parked,
    Arrived,
    SpawnPending,
    ReadFailed,
    BoundExpired,
    WalkReissued,
    HqPlaced,
    Attacked
}

public sealed record Chapter3ArthurVisitStep(
    Chapter3ArthurVisitOutcome Outcome,
    IReadOnlyList<string> Lines);

/// <summary>
/// OC-130: Arthur's visit state machine plus the bounded post-load
/// retry. The machine is parked -> coming -> arrived -> leaving ->
/// parked; a reload always resolves to parked, because
/// <see cref="BeginAfterLoad"/> resets the state and arms the
/// reconcile. The reconcile's Ready means spawned, not merely
/// constructed: the framework's spawn guard drops a custom NPC whose Avatar
/// is inactive, so the reconcile only moves a pending spawn out of
/// sight (no deactivate, no visibility write) and parks fully once
/// the snapshot reports spawned. Past the bound it disarms and
/// leaves him untouched. The reconcile keeps the spike's own shape
/// (<c>Release1ArthurLoadReconcile</c>): wall-clock throttled
/// attempts through an injected clock, a first-failure report,
/// and a bound, since the NPC is not Ready at load complete.
/// OC-132: arrivals are distance-based with a grace period, because
/// "not moving" reads true before the agent starts: coming resolves
/// to arrived within <see cref="ArrivedMetres"/> on the horizontal
/// plane, and leaving resolves to parked past
/// <see cref="LeaveParkMetres"/> or out of the player's view. Walks
/// re-issue a bounded number of times when he stalls, and both
/// bounds park or face him where he is. The HQ presence runs past
/// the reconcile: while Chapter 3 is active and the player is inside
/// the HQ he stands at the HQ spot instead of parking. A visit
/// overrides the presence, entering the HQ ends a visit (park, then
/// place), and a reload still resolves to parked before the presence
/// takes over, so he is never mid-visit after a load. Attack checks
/// run on every visit and HQ read: a health drop logs once per
/// incident and does nothing else.
/// </summary>
public sealed class Chapter3ArthurVisitPump
{
    /// <summary>The minimum wall clock gap between two reconcile reads.</summary>
    public const float RetryIntervalSeconds = 1.0f;

    /// <summary>
    /// Bounds the load reconcile, matching the spike's own ninety
    /// seconds of wall clock: well past the minute a live read once
    /// needed, without retrying forever.
    /// </summary>
    public const float MaxSeconds = 90f;

    /// <summary>
    /// Bounds the leave walk: thirty seconds of wall clock, then the
    /// pump parks him wherever he is.
    /// </summary>
    public const float LeaveSeconds = 30f;

    /// <summary>
    /// Bounds the come walk: thirty seconds of wall clock, then the
    /// pump stops him and faces the player where he is.
    /// </summary>
    public const float ComeSeconds = 30f;

    /// <summary>
    /// Coming resolves to arrived at or inside this distance on the
    /// horizontal plane. "Not moving" is never an arrival on its own:
    /// it reads true before the agent starts.
    /// </summary>
    public const float ArrivedMetres = 2.5f;

    /// <summary>
    /// Leaving resolves to parked at or past this distance on the
    /// horizontal plane, so a leave visibly walks away before the
    /// park. Out of the player's view parks sooner.
    /// </summary>
    public const float LeaveParkMetres = 25f;

    /// <summary>
    /// After a walk is issued, "not moving" is ignored for this long:
    /// the agent needs a moment to start, and the same-pass read
    /// would otherwise end the walk where it began.
    /// </summary>
    public const float WalkGraceSeconds = 1.0f;

    /// <summary>
    /// How many times a stalled walk re-issues before the bound ends
    /// it. A re-issue only fires past the grace period, while he is
    /// stopped short of the target.
    /// </summary>
    public const int MaxWalkResumes = 2;

    /// <summary>
    /// The minimum wall clock gap between two HQ placement attempts
    /// from parked, so an interior that is not there yet (or a spawn
    /// that is still pending) retries quietly instead of spamming.
    /// </summary>
    public const float HqAttemptIntervalSeconds = 1.0f;

    /// <summary>
    /// A health drop of at least this much opens an attack incident.
    /// </summary>
    public const float AttackDropHealth = 1.0f;

    /// <summary>
    /// An open attack incident closes after this long without
    /// further damage, re-based to the new health, so a later punch
    /// logs again. Healing back to the baseline closes it sooner.
    /// </summary>
    public const float AttackIncidentSeconds = 10f;

    private const int MaxDetailLength = 160;

    private readonly Func<float> _realtimeSinceStartup;
    private bool _reconcilePending;
    private bool _failureReported;
    private bool _verifyDone;
    private bool _preSpawnMoveLogged;
    private float _deadline;
    private readonly IntervalGate _retryGate;
    private float _leaveDeadline;
    private float _comeDeadline;
    private float _walkIssuedAt;
    private int _resumesLeft = MaxWalkResumes;
    private readonly IntervalGate _hqAttemptGate;
    private readonly IntervalGate _hqMaintainGate;
    private bool _hqInteriorAbsent;
    private float _attackBaseline = float.NaN;
    private float _attackLastHealth = float.NaN;
    private float _attackLastDropAt;
    private bool _attackIncidentOpen;

    public Chapter3ArthurVisitPump(Func<float> realtimeSinceStartup)
    {
        _realtimeSinceStartup = realtimeSinceStartup ?? throw new ArgumentNullException(nameof(realtimeSinceStartup));
        _retryGate = new IntervalGate(RetryIntervalSeconds);
        _hqAttemptGate = new IntervalGate(HqAttemptIntervalSeconds);
        _hqMaintainGate = new IntervalGate(Chapter3ArthurHqSpot.MaintainIntervalSeconds);
    }

    public Chapter3ArthurVisitState State { get; private set; } = Chapter3ArthurVisitState.Parked;

    /// <summary>
    /// OC-132: the HQ presence input, fed by the Chapter 3 service
    /// every pass. Null means no HQ information, which reads as
    /// outside: the pump parks exactly like OC-130.
    /// </summary>
    public IChapter3HqPresence? HqPresence { get; set; }

    /// <summary>
    /// OC-132: the owner-QA HQ override, behind
    /// <c>oc_ch3 arthur hq</c>. Auto is the shipped behaviour.
    /// </summary>
    public Chapter3ArthurHqForce HqForce { get; set; } = Chapter3ArthurHqForce.Auto;

    /// <summary>
    /// A reload resolves the machine to parked and arms the
    /// reconcile. Called once, unconditionally, from load complete:
    /// the framework's own sweep constructs the NPC whether or not
    /// the QA keys are on, so the reconcile cannot be gated either.
    /// The HQ presence takes over past the reconcile, so a reload
    /// still never resolves mid-visit.
    /// </summary>
    public void BeginAfterLoad()
    {
        State = Chapter3ArthurVisitState.Parked;
        var start = _realtimeSinceStartup();
        _reconcilePending = true;
        _failureReported = false;
        _verifyDone = false;
        _preSpawnMoveLogged = false;
        _deadline = start + MaxSeconds;
        _retryGate.Reset(start);
        _resumesLeft = MaxWalkResumes;
        _hqAttemptGate.Reset(start);
        _hqMaintainGate.Reset(start);
        _hqInteriorAbsent = false;
        _attackBaseline = float.NaN;
        _attackLastHealth = float.NaN;
        _attackIncidentOpen = false;
    }

    /// <summary>
    /// The harness calls this after <c>come</c> succeeds. False
    /// unless parked or at the HQ: a second come mid-visit is
    /// refused, not stacked, while a visit overrides the presence.
    /// Arms the walk grace, the come bound, and the re-issues.
    /// </summary>
    public bool BeginCome()
    {
        if (State != Chapter3ArthurVisitState.Parked && State != Chapter3ArthurVisitState.AtHq) return false;
        State = Chapter3ArthurVisitState.Coming;
        var start = _realtimeSinceStartup();
        _walkIssuedAt = start;
        _comeDeadline = start + ComeSeconds;
        _resumesLeft = MaxWalkResumes;
        return true;
    }

    /// <summary>
    /// The harness calls this after <c>leave</c> succeeds. False
    /// unless coming or arrived: leaving while parked (or from the
    /// HQ spot) is refused. Arms the walk grace, the leave bound,
    /// and the re-issues.
    /// </summary>
    public bool BeginLeave()
    {
        if (State != Chapter3ArthurVisitState.Coming && State != Chapter3ArthurVisitState.Arrived) return false;
        State = Chapter3ArthurVisitState.Leaving;
        var start = _realtimeSinceStartup();
        _walkIssuedAt = start;
        _leaveDeadline = start + LeaveSeconds;
        _resumesLeft = MaxWalkResumes;
        return true;
    }

    /// <summary>
    /// OC-135: the beating coordinator parked Arthur through the
    /// world itself (the player woke elsewhere), so the pump just
    /// drops back to parked with fresh attack baselines.
    /// </summary>
    public void ParkAfterBeating()
    {
        State = Chapter3ArthurVisitState.Parked;
        _resumesLeft = MaxWalkResumes;
        _attackBaseline = float.NaN;
        _attackLastHealth = float.NaN;
        _attackIncidentOpen = false;
    }

    public void CancelLoadReconcile() => _reconcilePending = false;

    /// <summary>
    /// Called once per update pass. Returns null on every pass with
    /// nothing new to report. A null world or missing authority keeps
    /// the reconcile armed without spending an attempt.
    /// </summary>
    public Chapter3ArthurVisitStep? Pump(IChapter3ArthurWorld? world)
    {
        if (world is null || !world.HasAuthority) return null;
        if (_reconcilePending) return PumpReconcile(world);
        var wantHq = WantHq();
        if (State is Chapter3ArthurVisitState.Coming or Chapter3ArthurVisitState.Arrived or Chapter3ArthurVisitState.Leaving)
        {
            if (wantHq) return PumpEndVisitForHq(world);
            return State switch
            {
                Chapter3ArthurVisitState.Coming => PumpComing(world),
                Chapter3ArthurVisitState.Arrived => PumpArrived(world),
                _ => PumpLeaving(world)
            };
        }
        if (State == Chapter3ArthurVisitState.AtHq)
        {
            if (!wantHq)
            {
                State = Chapter3ArthurVisitState.Parked;
                var park = world.TryParkArthur(out var parkReason);
                return new Chapter3ArthurVisitStep(
                    Chapter3ArthurVisitOutcome.Parked,
                    new[] { $"left the HQ spot; park {park}: {parkReason}" });
            }
            return PumpAtHq(world);
        }
        if (wantHq) return PumpParkedWantHq(world);
        // Between visits a recorded hit belongs to nobody: drain it so a
        // stale impact cannot open the next visit as an attack.
        world.PollArthurImpact();
        return null;
    }

    private Chapter3ArthurVisitStep? PumpReconcile(IChapter3ArthurWorld world)
    {
        var now = _realtimeSinceStartup();
        if (now >= _deadline)
        {
            _reconcilePending = false;
            return new Chapter3ArthurVisitStep(
                Chapter3ArthurVisitOutcome.BoundExpired,
                new[] { $"gave up after {MaxSeconds} seconds; Arthur never spawned, left untouched." });
        }

        if (!_retryGate.TryPass(now)) return null;

        var readStatus = TryRead(world, out var snapshot);
        if (readStatus != Chapter3ArthurReadStatus.Ready || snapshot is null)
            return ReportFailureOnce($"the Arthur read status was {readStatus}.");
        if (!snapshot.Present)
            return ReportFailureOnce("no Arthur contact constructed yet.");

        var lines = new List<string>();
        if (!_verifyDone)
        {
            _verifyDone = true;
            var verify = world.TryVerifyAppearancePaths(out var verifyReason);
            lines.Add($"appearance paths {verify}: {verifyReason}");
        }

        if (!snapshot.Spawned)
        {
            // The spawn guard drops a custom NPC whose Avatar is
            // inactive, so a pending spawn is never deactivated: only
            // moved out of sight, which touches no active state.
            var move = world.TryMoveOutOfSight(out var moveReason);
            if (!_preSpawnMoveLogged)
            {
                _preSpawnMoveLogged = true;
                lines.Add($"pre-spawn move {move}: {moveReason}");
            }
            if (lines.Count == 0) return null;
            return new Chapter3ArthurVisitStep(Chapter3ArthurVisitOutcome.SpawnPending, lines);
        }

        _reconcilePending = false;
        State = Chapter3ArthurVisitState.Parked;
        if (snapshot.RegisteredCount > 1)
        {
            var dedupe = world.TryDedupeArthur(out var dedupeReason);
            lines.Add($"dedupe {dedupe}: {dedupeReason}");
        }
        var park = world.TryParkArthur(out var parkReason);
        lines.Add($"park {park}: {parkReason}");
        return new Chapter3ArthurVisitStep(Chapter3ArthurVisitOutcome.Parked, lines);
    }

    private Chapter3ArthurVisitStep? ReportFailureOnce(string line)
    {
        if (_failureReported) return null;
        _failureReported = true;
        return new Chapter3ArthurVisitStep(
            Chapter3ArthurVisitOutcome.ReadFailed,
            new[] { Truncate(line) });
    }

    private Chapter3ArthurVisitStep? PumpComing(IChapter3ArthurWorld world)
    {
        var now = _realtimeSinceStartup();
        if (TryRead(world, out var snapshot) != Chapter3ArthurReadStatus.Ready || snapshot is null || !snapshot.Present || !snapshot.Spawned)
            return null;
        var attacked = CheckAttacked(snapshot, "visit", now, world.PollArthurImpact());
        if (attacked is not null) return attacked;
        // The grace holds everything except the attack check: the
        // same-pass "not moving" read must end nothing.
        if (now < _walkIssuedAt + WalkGraceSeconds) return null;
        if (snapshot.DistanceToPlayer <= ArrivedMetres)
        {
            State = Chapter3ArthurVisitState.Arrived;
            var face = world.TryFacePlayer(out var faceReason);
            return new Chapter3ArthurVisitStep(
                Chapter3ArthurVisitOutcome.Arrived,
                new[] { $"arrived {snapshot.DistanceToPlayer:0.0} m from the player; face {face}: {faceReason}" });
        }
        if (now >= _comeDeadline)
        {
            State = Chapter3ArthurVisitState.Arrived;
            var face = world.TryFacePlayer(out var faceReason);
            return new Chapter3ArthurVisitStep(
                Chapter3ArthurVisitOutcome.Arrived,
                new[] { $"come bound expired after {ComeSeconds} seconds; stopped and faced the player where he is ({snapshot.DistanceToPlayer:0.0} m); face {face}: {faceReason}" });
        }
        if (!snapshot.Moving && _resumesLeft > 0)
        {
            _resumesLeft--;
            var resume = world.TryResumeWalk(out var resumeReason);
            return new Chapter3ArthurVisitStep(
                Chapter3ArthurVisitOutcome.WalkReissued,
                new[] { $"stopped {snapshot.DistanceToPlayer:0.0} m out; walk re-issue {resume}: {resumeReason} ({_resumesLeft} left)" });
        }
        return null;
    }

    private Chapter3ArthurVisitStep? PumpArrived(IChapter3ArthurWorld world)
    {
        if (TryRead(world, out var snapshot) != Chapter3ArthurReadStatus.Ready || snapshot is null || !snapshot.Present || !snapshot.Spawned)
            return null;
        return CheckAttacked(snapshot, "visit", _realtimeSinceStartup(), world.PollArthurImpact());
    }

    private Chapter3ArthurVisitStep? PumpLeaving(IChapter3ArthurWorld world)
    {
        var now = _realtimeSinceStartup();
        var readStatus = TryRead(world, out var snapshot);
        var readable = readStatus == Chapter3ArthurReadStatus.Ready && snapshot is not null && snapshot.Present && snapshot.Spawned;
        if (readable)
        {
            var attacked = CheckAttacked(snapshot!, "visit", now, world.PollArthurImpact());
            if (attacked is not null) return attacked;
            // The grace holds the park too: a leave whose target or
            // starting point already reads as gone must still visibly
            // walk away first.
            if (now < _walkIssuedAt + WalkGraceSeconds) return null;
            if (snapshot!.DistanceToPlayer >= LeaveParkMetres)
            {
                State = Chapter3ArthurVisitState.Parked;
                var far = world.TryParkArthur(out var farReason);
                return new Chapter3ArthurVisitStep(
                    Chapter3ArthurVisitOutcome.Parked,
                    new[] { $"reached {snapshot.DistanceToPlayer:0.0} m from the player; park {far}: {farReason}" });
            }
            if (!snapshot.InPlayerView)
            {
                State = Chapter3ArthurVisitState.Parked;
                var hidden = world.TryParkArthur(out var hiddenReason);
                return new Chapter3ArthurVisitStep(
                    Chapter3ArthurVisitOutcome.Parked,
                    new[] { $"out of the player's view; park {hidden}: {hiddenReason}" });
            }
        }
        if (now >= _leaveDeadline)
        {
            State = Chapter3ArthurVisitState.Parked;
            var park = world.TryParkArthur(out var parkReason);
            return new Chapter3ArthurVisitStep(
                Chapter3ArthurVisitOutcome.Parked,
                new[] { $"leave bound expired; park {park}: {parkReason}" });
        }
        if (readable && now >= _walkIssuedAt + WalkGraceSeconds && !snapshot!.Moving && _resumesLeft > 0)
        {
            _resumesLeft--;
            var resume = world.TryResumeWalk(out var resumeReason);
            return new Chapter3ArthurVisitStep(
                Chapter3ArthurVisitOutcome.WalkReissued,
                new[] { $"stopped {snapshot.DistanceToPlayer:0.0} m out; walk re-issue {resume}: {resumeReason} ({_resumesLeft} left)" });
        }
        return null;
    }

    private Chapter3ArthurVisitStep? PumpEndVisitForHq(IChapter3ArthurWorld world)
    {
        var park = world.TryParkArthur(out var parkReason);
        var place = world.TryPlaceAtHq(out var placeReason);
        if (place == Chapter3ArthurMutationStatus.Succeeded)
        {
            State = Chapter3ArthurVisitState.AtHq;
            _hqInteriorAbsent = false;
            _hqMaintainGate.Reset(_realtimeSinceStartup() + Chapter3ArthurHqSpot.MaintainIntervalSeconds);
            return new Chapter3ArthurVisitStep(
                Chapter3ArthurVisitOutcome.HqPlaced,
                new[] { $"visit ended: the player entered the HQ; park {park}: {parkReason}; place {place}: {placeReason}" });
        }
        State = Chapter3ArthurVisitState.Parked;
        _hqAttemptGate.Reset(_realtimeSinceStartup() + HqAttemptIntervalSeconds);
        return new Chapter3ArthurVisitStep(
            Chapter3ArthurVisitOutcome.Parked,
            new[] { $"visit ended: the player entered the HQ; park {park}: {parkReason}; HQ place {place}: {placeReason}" });
    }

    private Chapter3ArthurVisitStep? PumpParkedWantHq(IChapter3ArthurWorld world)
    {
        var now = _realtimeSinceStartup();
        if (!_hqAttemptGate.TryPass(now)) return null;
        // The spawn rule holds for the HQ too: a pending spawn is
        // never placed, so the read gates the place and failures
        // retry quietly.
        if (TryRead(world, out var snapshot) != Chapter3ArthurReadStatus.Ready || snapshot is null || !snapshot.Present || !snapshot.Spawned)
            return null;
        var place = world.TryPlaceAtHq(out var placeReason);
        if (place != Chapter3ArthurMutationStatus.Succeeded) return null;
        State = Chapter3ArthurVisitState.AtHq;
        _hqInteriorAbsent = false;
        _hqMaintainGate.Reset(now + Chapter3ArthurHqSpot.MaintainIntervalSeconds);
        return new Chapter3ArthurVisitStep(
            Chapter3ArthurVisitOutcome.HqPlaced,
            new[] { $"placed at the HQ spot: {placeReason}" });
    }

    private Chapter3ArthurVisitStep? PumpAtHq(IChapter3ArthurWorld world)
    {
        var now = _realtimeSinceStartup();
        if (!_hqMaintainGate.TryPass(now)) return null;
        if (TryRead(world, out var snapshot) != Chapter3ArthurReadStatus.Ready || snapshot is null)
            return null;
        if (!snapshot.Present || !snapshot.Spawned)
        {
            State = Chapter3ArthurVisitState.Parked;
            var park = world.TryParkArthur(out var parkReason);
            return new Chapter3ArthurVisitStep(
                Chapter3ArthurVisitOutcome.Parked,
                new[] { $"lost the HQ contact; park {park}: {parkReason}" });
        }
        var attacked = CheckAttacked(snapshot, "HQ", now, world.PollArthurImpact());
        if (attacked is not null) return attacked;
        // Idempotent by construction: re-places him after a save-time
        // interior rebuild, and holds the last spot while the
        // interior is gone. Never walks: the pocket room has no
        // navmesh.
        var place = world.TryPlaceAtHq(out var placeReason);
        if (place == Chapter3ArthurMutationStatus.Succeeded)
        {
            Chapter3ArthurVisitStep? rebuilt = null;
            if (_hqInteriorAbsent)
            {
                _hqInteriorAbsent = false;
                rebuilt = new Chapter3ArthurVisitStep(
                    Chapter3ArthurVisitOutcome.HqPlaced,
                    new[] { $"re-placed after the interior rebuild: {placeReason}" });
            }
            // The agent is off at the HQ, so this faces with the
            // transform only: never the agent-backed face.
            if (PlayerInside() && snapshot.DistanceToPlayer <= Chapter3ArthurHqSpot.FaceRangeMetres)
                world.TryFacePlayerAtHq(out _);
            return rebuilt;
        }
        if (_hqInteriorAbsent) return null;
        _hqInteriorAbsent = true;
        return new Chapter3ArthurVisitStep(
            Chapter3ArthurVisitOutcome.HqPlaced,
            new[] { $"the HQ interior is gone; holding position ({placeReason})" });
    }

    /// <summary>
    /// Logs an attack once per incident and does nothing else: the
    /// suppressed-impact signal opens it, a health drop without a
    /// recorded impact is the fallback, and the service starts the
    /// beating and logs the exact sentences.
    /// Mid-incident hits stay quiet; healing or ten quiet seconds re-arms.
    /// </summary>
    private Chapter3ArthurVisitStep? CheckAttacked(Chapter3ArthurSnapshot snapshot, string context, float now, bool impactObserved)
    {
        if (float.IsNaN(snapshot.Health)) return null;
        if (float.IsNaN(_attackBaseline))
        {
            _attackBaseline = snapshot.Health;
            _attackLastHealth = snapshot.Health;
            return null;
        }
        if (impactObserved)
        {
            _attackLastDropAt = now;
            _attackLastHealth = snapshot.Health;
            if (_attackIncidentOpen) return null;
            _attackIncidentOpen = true;
            return new Chapter3ArthurVisitStep(
                Chapter3ArthurVisitOutcome.Attacked,
                new[]
                {
                    $"Chapter 3 Arthur was attacked ({context}).",
                    "Chapter 3 Arthur native reaction suppressed (ImpactReceived).",
                });
        }
        if (snapshot.Health < _attackLastHealth - AttackDropHealth)
        {
            _attackLastDropAt = now;
            _attackLastHealth = snapshot.Health;
            if (_attackIncidentOpen) return null;
            _attackIncidentOpen = true;
            return new Chapter3ArthurVisitStep(
                Chapter3ArthurVisitOutcome.Attacked,
                new[] { $"Chapter 3 Arthur was attacked ({context})." });
        }
        _attackLastHealth = snapshot.Health;
        if (snapshot.Health > _attackBaseline)
            _attackBaseline = snapshot.Health;
        if (snapshot.Health >= _attackBaseline)
        {
            _attackIncidentOpen = false;
            return null;
        }
        if (_attackIncidentOpen && now - _attackLastDropAt >= AttackIncidentSeconds)
        {
            _attackIncidentOpen = false;
            _attackBaseline = snapshot.Health;
        }
        return null;
    }

    private bool WantHq()
    {
        if (HqForce == Chapter3ArthurHqForce.On) return true;
        if (HqForce == Chapter3ArthurHqForce.Off) return false;
        try
        {
            return HqPresence is not null && HqPresence.ChapterActive && HqPresence.PlayerInsideHq;
        }
        catch
        {
            return false;
        }
    }

    private bool PlayerInside()
    {
        if (HqForce == Chapter3ArthurHqForce.On) return true;
        if (HqForce == Chapter3ArthurHqForce.Off) return false;
        try
        {
            return HqPresence is not null && HqPresence.PlayerInsideHq;
        }
        catch
        {
            return false;
        }
    }

    private static Chapter3ArthurReadStatus TryRead(IChapter3ArthurWorld world, out Chapter3ArthurSnapshot? snapshot)
    {
        try
        {
            return world.TryReadArthur(out snapshot);
        }
        catch
        {
            // The boundary is documented total; defence in depth only.
            snapshot = null;
            return Chapter3ArthurReadStatus.Faulted;
        }
    }

    private static string Truncate(string text)
    {
        text ??= string.Empty;
        return text.Length <= MaxDetailLength ? text : text[..MaxDetailLength];
    }
}
