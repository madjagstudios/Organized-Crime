using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-135: what the player's body must do for Arthur's beating. The
/// live adapter is S1API plus the native seams phase 1 verified;
/// tests fake this. Every member is total: a bool plus a reason,
/// never an exception out.
/// </summary>
public interface IChapter3BeatingWorld
{
    bool IsHost { get; }
    bool IsPlayerSpawned { get; }
    bool TryReadHealth(out float health, out float maxHealth, out string reason);
    bool TrySetHealth(float health, out string reason);
    bool TryLock(out string reason);
    bool TryUnlock(out string reason);
    bool TryFadeOut(out string reason);
    bool TryFadeIn(out string reason);
    bool TryJoltCamera(out string reason);
    bool TryMoveToWake(out (float X, float Y, float Z) wokeAt, out string propertyCode, out string reason);
    bool TryOpenBill(out string reason);
}

/// <summary>
/// OC-135: the beating's HQ door. A narrow boundary so the
/// coordinator stays test linkable: the live adapter reads the HQ
/// service's own inside state and exits through it, and tests fake
/// both. Total like every other world boundary.
/// </summary>
public interface IChapter3BeatingHq
{
    /// <summary>The local player is inside the HQ pocket room.</summary>
    bool PlayerInsideHq { get; }

    /// <summary>
    /// Leaves through <c>SyndicateHqRuntimeService</c> so its inside
    /// state, prompts, and the HQ presence all read outside after.
    /// </summary>
    bool TryExit(out string reason);
}

/// <summary>
/// OC-135: the live <see cref="IChapter3BeatingHq"/>. Holds only
/// delegates, so it stays test linkable: Mod wires the HQ service's
/// own inside state on one side and <c>TryExit</c> on the other.
/// </summary>
public sealed class Chapter3SyndicateHqExit : IChapter3BeatingHq
{
    private readonly Func<bool> _playerInsideHq;
    private readonly Func<bool> _tryExit;

    public Chapter3SyndicateHqExit(Func<bool> playerInsideHq, Func<bool> tryExit)
    {
        _playerInsideHq = playerInsideHq ?? throw new ArgumentNullException(nameof(playerInsideHq));
        _tryExit = tryExit ?? throw new ArgumentNullException(nameof(tryExit));
    }

    public bool PlayerInsideHq
    {
        get
        {
            try { return _playerInsideHq(); }
            catch { return false; }
        }
    }

    public bool TryExit(out string reason)
    {
        reason = "the HQ exit did not run.";
        try
        {
            if (_tryExit())
            {
                reason = "left the HQ through its own exit.";
                return true;
            }
            reason = "the HQ service refused the exit.";
            return false;
        }
        catch (Exception exception)
        {
            reason = $"the HQ exit threw {exception.GetType().Name}.";
            return false;
        }
    }
}

public enum Chapter3BeatingKind
{
    Scheduled,
    Attack
}

/// <summary>
/// OC-135: Arthur's beating, staged like the native hospital wake-up
/// but without the death: lock, line, punches, fade out, HQ exit and
/// move, park, fade in, bill, unlock, record, Nell. One Tick per
/// frame from the service; each stage runs at most once, every step
/// is guarded, and every exit path unlocks through the idempotent
/// world unlock. Once the move succeeds the beating counts: later
/// faults still unlock, close the overlay, record, and queue Nell's
/// text. Before the move an abort records nothing, and a reload
/// mid-beating finds memory-only state, so nothing records and the
/// unlock runs from OnPreLoad; a reload after the move replays the
/// beating later.
/// </summary>
public sealed class Chapter3ArthurBeating
{
    public const int PunchCount = 4;
    public const float PunchIntervalSeconds = 1.1f;
    public const float LineSeconds = 2.0f;
    public const float FadeSeconds = 0.7f;
    public const float HealthFloorFraction = 0.25f;
    public const float TimeoutSeconds = 30f;

    private readonly IChapter3BeatingWorld _world;
    private readonly IChapter3ArthurWorld _arthur;
    private readonly IChapter3BeatingHq _hq;
    private readonly Action<string> _log;
    private readonly Action _record;
    private readonly Action _noteNell;
    private readonly Action _parkArthur;
    private readonly float _startedAt;
    private Chapter3ArthurVisitState _pumpAtStart;

    private Chapter3BeatingStage _stage;
    private float _stageDueAt;
    private int _punchesDone;
    private float _startHealth;
    private float _maxHealth;
    private bool _moved;
    private bool _terminalLogged;

    public Chapter3ArthurBeating(
        IChapter3BeatingWorld world,
        IChapter3ArthurWorld arthur,
        IChapter3BeatingHq hq,
        Chapter3BeatingKind kind,
        bool parkArthur,
        Chapter3ArthurVisitState pumpAtStart,
        float startedAt,
        Action<string> log,
        Action record,
        Action noteNell,
        Action parkArthurAction)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _arthur = arthur ?? throw new ArgumentNullException(nameof(arthur));
        _hq = hq ?? throw new ArgumentNullException(nameof(hq));
        Kind = kind;
        ParkArthur = parkArthur;
        _pumpAtStart = pumpAtStart;
        _startedAt = startedAt;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _noteNell = noteNell ?? throw new ArgumentNullException(nameof(noteNell));
        _parkArthur = parkArthurAction ?? throw new ArgumentNullException(nameof(parkArthurAction));
        _stage = Chapter3BeatingStage.Lock;
        _stageDueAt = startedAt;
    }

    public Chapter3BeatingKind Kind { get; }

    /// <summary>Whether step 6 parks Arthur; at the HQ he stays.</summary>
    public bool ParkArthur { get; }

    public bool IsDone => _stage is Chapter3BeatingStage.Done or Chapter3BeatingStage.Aborted or Chapter3BeatingStage.Faulted;

    public bool CompletedOk => _stage == Chapter3BeatingStage.Done;

    /// <summary>
    /// One frame of the beating. Runs at most one stage's due work.
    /// Before the move, a timeout, a pump state change, or any failed
    /// or throwing step aborts (unlocking, nothing recorded). After
    /// the move the beating counts: pump changes are expected, and a
    /// failure finishes with a fault, still recording.
    /// </summary>
    public void Tick(float now, Chapter3ArthurVisitState pumpState)
    {
        if (IsDone)
            return;
        if (now - _startedAt > TimeoutSeconds && now >= _startedAt)
        {
            Abort($"timed out after {TimeoutSeconds:0} s.");
            return;
        }
        if (!_moved && pumpState != _pumpAtStart)
        {
            // An attack mid-approach starts the beating while he is still
            // Coming; his arrival a moment later is expected, not a fault.
            if (_pumpAtStart == Chapter3ArthurVisitState.Coming && pumpState == Chapter3ArthurVisitState.Arrived)
            {
                _pumpAtStart = pumpState;
            }
            else
            {
                Abort($"the pump left {_pumpAtStart} for {pumpState} mid-beating.");
                return;
            }
        }
        if (now < _stageDueAt && now >= _startedAt)
            return;
        try
        {
            TickStage(now);
        }
        catch (Exception exception)
        {
            Abort($"threw {exception.GetType().Name}.");
        }
    }

    /// <summary>
    /// Aborts now from outside the tick: the service calls this when
    /// a world goes away. Before the move this unlocks and records
    /// nothing; after the move the beating counts, so it finishes
    /// with a fault, still recording. Idempotent.
    /// </summary>
    public void AbortOutside(string reason)
    {
        Abort(reason);
    }

    /// <summary>
    /// Unlocks now, records nothing, and marks the beating aborted:
    /// the service calls this when the scene unloads (reload or
    /// dispose), where an in-memory ledger change would never be
    /// saved. A reload after the move but before Record therefore
    /// replays the beating later; that is acceptable. Idempotent.
    /// </summary>
    public void AbortForUnload(string reason)
    {
        if (IsDone)
            return;
        _stage = Chapter3BeatingStage.Aborted;
        if (!_terminalLogged)
        {
            _terminalLogged = true;
            _log($"Chapter 3 Arthur beating aborted: {reason}");
        }
        UnlockBestEffort();
    }

    private void TickStage(float now)
    {
        switch (_stage)
        {
            case Chapter3BeatingStage.Lock:
                if (!Guarded(_world.TryLock, out var lockReason))
                {
                    Abort($"the lock failed: {lockReason}");
                    return;
                }
                if (!GuardedHealth(out var health, out var max, out var healthReason))
                {
                    Abort($"the health read failed: {healthReason}");
                    return;
                }
                if (max <= 0f || health <= 0f)
                {
                    Abort("the player is not up for a beating.");
                    return;
                }
                _startHealth = health;
                _maxHealth = max;
                _log($"Chapter 3 Arthur beating: locked (invincible, movement and items held) at {health:0} / {max:0} health.");
                Advance(Chapter3BeatingStage.Line, now);
                break;
            case Chapter3BeatingStage.Line:
                // At the HQ the agent is off, so this faces with the
                // transform only: never the agent-backed face.
                var face = ParkArthur
                    ? _arthur.TryFacePlayer(out var faceReason)
                    : _arthur.TryFacePlayerAtHq(out faceReason);
                if (face != Chapter3ArthurMutationStatus.Succeeded)
                {
                    Abort($"the face failed: {faceReason}");
                    return;
                }
                var line = Kind == Chapter3BeatingKind.Scheduled
                    ? Chapter3ArthurCopy.BeatingScheduledLine
                    : Chapter3ArthurCopy.BeatingAttackLine;
                if (_arthur.TrySayArthur(line, out var sayReason) != Chapter3ArthurMutationStatus.Succeeded)
                {
                    Abort($"the line failed: {sayReason}");
                    return;
                }
                _log($"Chapter 3 Arthur beating: Arthur says \"{line}\"");
                _stage = Chapter3BeatingStage.Punches;
                _stageDueAt = now + LineSeconds;
                break;
            case Chapter3BeatingStage.Punches:
                if (!ThrowPunch(now))
                    return;
                break;
            case Chapter3BeatingStage.FadeOut:
                if (!Guarded(_world.TryFadeOut, out var fadeReason))
                {
                    Abort($"the fade out failed: {fadeReason}");
                    return;
                }
                Advance(Chapter3BeatingStage.Move, now + FadeSeconds);
                break;
            case Chapter3BeatingStage.Move:
                if (ReadHqInside())
                {
                    if (!Guarded(_hq.TryExit, out var exitReason))
                    {
                        Abort($"the HQ exit failed: {exitReason}");
                        return;
                    }
                    _log("Chapter 3 Arthur beating: left the HQ through its own exit.");
                }
                if (!GuardedMove(out var wokeAt, out var code, out var moveReason))
                {
                    Abort($"the move failed: {moveReason}");
                    return;
                }
                _log($"Chapter 3 Arthur beating: woke at ({wokeAt.X:0.0}, {wokeAt.Y:0.0}, {wokeAt.Z:0.0}) ({code}).");
                _moved = true;
                Advance(Chapter3BeatingStage.Park, now);
                break;
            case Chapter3BeatingStage.Park:
                if (ParkArthur)
                {
                    _parkArthur();
                    _log("Chapter 3 Arthur beating: Arthur parked.");
                }
                else
                {
                    _log("Chapter 3 Arthur beating: no park; the pump owns Arthur at the HQ.");
                }
                if (!Guarded(_world.TryFadeIn, out var fadeInReason))
                {
                    Abort($"the fade in failed: {fadeInReason}");
                    return;
                }
                Advance(Chapter3BeatingStage.Bill, now + FadeSeconds);
                break;
            case Chapter3BeatingStage.Bill:
                if (!Guarded(_world.TryOpenBill, out var billReason))
                {
                    Abort($"the bill failed: {billReason}");
                    return;
                }
                _log("Chapter 3 Arthur beating: the hospital bill is open.");
                Advance(Chapter3BeatingStage.Unlock, now);
                break;
            case Chapter3BeatingStage.Unlock:
                if (!Guarded(_world.TryUnlock, out var unlockReason))
                {
                    Abort($"the unlock failed: {unlockReason}");
                    return;
                }
                _log("Chapter 3 Arthur beating: unlocked, exactly as it was.");
                Advance(Chapter3BeatingStage.Record, now);
                break;
            case Chapter3BeatingStage.Record:
                _record();
                Advance(Chapter3BeatingStage.Nell, now);
                break;
            case Chapter3BeatingStage.Nell:
                _noteNell();
                _stage = Chapter3BeatingStage.Done;
                _log("Chapter 3 Arthur beating: complete; Nell's text is queued.");
                break;
        }
    }

    private bool ThrowPunch(float now)
    {
        if (_arthur.TryPlayAnimationTrigger("Punch", out var animReason) != Chapter3ArthurMutationStatus.Succeeded)
        {
            Abort($"punch {_punchesDone + 1} animation failed: {animReason}");
            return false;
        }
        if (!Guarded(_world.TryJoltCamera, out var joltReason))
        {
            Abort($"punch {_punchesDone + 1} jolt failed: {joltReason}");
            return false;
        }
        var floor = _maxHealth * HealthFloorFraction;
        var next = _startHealth > floor
            ? Math.Max(floor, _startHealth - (_startHealth - floor) * (_punchesDone + 1) / PunchCount)
            : _startHealth;
        if (!GuardedSetHealth(next, out var healthReason))
        {
            Abort($"punch {_punchesDone + 1} health set failed: {healthReason}");
            return false;
        }
        _punchesDone++;
        _log($"Chapter 3 Arthur beating: punch {_punchesDone}/{PunchCount} lands at {next:0} health.");
        if (_punchesDone >= PunchCount)
            Advance(Chapter3BeatingStage.FadeOut, now);
        else
            _stageDueAt = now + PunchIntervalSeconds;
        return true;
    }

    private void Advance(Chapter3BeatingStage stage, float dueAt)
    {
        _stage = stage;
        _stageDueAt = dueAt;
    }

    private void Abort(string reason)
    {
        if (IsDone)
            return;
        if (_moved)
        {
            FinishWithFault(reason);
            return;
        }
        _stage = Chapter3BeatingStage.Aborted;
        if (!_terminalLogged)
        {
            _terminalLogged = true;
            _log($"Chapter 3 Arthur beating aborted: {reason}");
        }
        UnlockBestEffort();
    }

    /// <summary>
    /// A failure after the move: the player was already beaten and
    /// moved, so the beating counts. Unlocks, closes the overlay,
    /// then records and queues Nell's text exactly as the normal
    /// path does. Best effort throughout; never throws.
    /// </summary>
    private void FinishWithFault(string reason)
    {
        _stage = Chapter3BeatingStage.Faulted;
        UnlockBestEffort();
        try
        {
            if (!_world.TryFadeIn(out var fadeReason))
                _log($"Chapter 3 Arthur beating fault fade-in failed: {fadeReason}");
        }
        catch (Exception exception)
        {
            _log($"Chapter 3 Arthur beating fault fade-in threw {exception.GetType().Name}.");
        }
        try
        {
            _record();
        }
        catch (Exception exception)
        {
            _log($"Chapter 3 Arthur beating fault record threw {exception.GetType().Name}.");
        }
        try
        {
            _noteNell();
        }
        catch (Exception exception)
        {
            _log($"Chapter 3 Arthur beating fault note threw {exception.GetType().Name}.");
        }
        _log($"Chapter 3 Arthur beating finished with a fault after the wake: {reason}; recorded.");
    }

    private void UnlockBestEffort()
    {
        try
        {
            if (!_world.TryUnlock(out var unlockReason))
                _log($"Chapter 3 Arthur beating abort unlock failed: {unlockReason}");
        }
        catch (Exception exception)
        {
            _log($"Chapter 3 Arthur beating abort unlock threw {exception.GetType().Name}.");
        }
    }

    private bool Guarded(TryBeatingStep step, out string reason)
    {
        reason = "the step did not run.";
        try
        {
            return step(out reason);
        }
        catch (Exception exception)
        {
            reason = $"threw {exception.GetType().Name}.";
            return false;
        }
    }

    private bool GuardedHealth(out float health, out float maxHealth, out string reason)
    {
        health = 0f;
        maxHealth = 0f;
        reason = "the health read did not run.";
        try
        {
            return _world.TryReadHealth(out health, out maxHealth, out reason);
        }
        catch (Exception exception)
        {
            reason = $"threw {exception.GetType().Name}.";
            return false;
        }
    }

    private bool GuardedSetHealth(float health, out string reason)
    {
        reason = "the health set did not run.";
        try
        {
            return _world.TrySetHealth(health, out reason);
        }
        catch (Exception exception)
        {
            reason = $"threw {exception.GetType().Name}.";
            return false;
        }
    }

    private bool GuardedMove(out (float X, float Y, float Z) wokeAt, out string propertyCode, out string reason)
    {
        wokeAt = (0f, 0f, 0f);
        propertyCode = string.Empty;
        reason = "the move did not run.";
        try
        {
            return _world.TryMoveToWake(out wokeAt, out propertyCode, out reason);
        }
        catch (Exception exception)
        {
            reason = $"threw {exception.GetType().Name}.";
            return false;
        }
    }

    private bool ReadHqInside()
    {
        try
        {
            return _hq.PlayerInsideHq;
        }
        catch
        {
            return false;
        }
    }

    private delegate bool TryBeatingStep(out string reason);

    private enum Chapter3BeatingStage
    {
        Lock,
        Line,
        Punches,
        FadeOut,
        Move,
        Park,
        Bill,
        Unlock,
        Record,
        Nell,
        Done,
        Aborted,
        Faulted
    }
}
