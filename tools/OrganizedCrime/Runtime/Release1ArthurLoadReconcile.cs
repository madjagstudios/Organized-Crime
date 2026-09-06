#if OC_OWNER_SPIKES
namespace OrganizedCrime.Runtime;

/// <summary>
/// What <see cref="Release1ArthurLoadReconcile.Pump"/> reports on a pass that actually has something
/// to log. Every other pass returns null: still waiting for the next throttled attempt, still
/// retrying a non-ready read that was already reported once, or the contact was proven absent.
/// </summary>
public enum Release1ArthurLoadReconcileOutcome
{
    Parked,
    ReadFailed,
    BoundExpired
}

public sealed record Release1ArthurLoadReconcileStep(
    Release1ArthurLoadReconcileOutcome Outcome,
    Release1StagingHarnessResult? ParkResult,
    string? Detail = null);

/// <summary>
/// OC-69 fix for a live defect, then a second live defect in the fix itself, then a lifecycle change
/// (a seventh spec review round) once a third live defect showed the fix itself was undoing a complete
/// contact only to have F4 rebuild an incomplete one. The one-shot design read once at load-complete and
/// gave up for the rest of the session on anything other than an immediate Ready read; the first
/// replacement retried every update pass, bounded at thirty passes. Live evidence on 2026-09-05 showed
/// the bounded-passes replacement was still the wrong unit: it burned all thirty passes in well under
/// half a second (13:37:36.215 to 13:37:36.619), logging thirty <c>OC-69 field contact read failed</c>
/// lines along the way, while a manual F3 press about a minute later read the very same contact
/// <c>Ready</c>. A frame is not a reliable clock for how long the native game plugin's own load time
/// reflection sweep needs before a freshly network registered contact's underlying engine object is
/// readable; the contact needs real, wall clock time, not a frame count.
///
/// This class now throttles its own attempts to <see cref="RetryIntervalSeconds"/> of wall clock
/// between reads, driven by an injected <c>realtimeSinceStartup</c> delegate rather than a frame
/// counter, matching the same injected time source pattern <c>FishWarehouseEmployeeReplayHost</c> and
/// <c>SyndicateHqRuntimeService</c> already use so a test can fake it. <see cref="BeginAfterLoad"/>
/// arms the pump once, and <see cref="Pump"/> is then called once per update pass, unconditionally,
/// never behind the owner QA keys gate, matching Decision 11's own correction that the load-time
/// construction this reconciles is not gated by that preference either. It keeps retrying until the
/// contact resolves Ready or is proven absent, bounded by <see cref="MaxSeconds"/> of wall clock since
/// <see cref="BeginAfterLoad"/> so a permanently faulting or genuinely absent contact does not retry
/// forever.
///
/// OC-69 lifecycle change, 2026-09-05 (a seventh spec review round). Live evidence, build 12347e1: the
/// Arthur the game plugin's own load-time reflection sweep constructs comes up complete, read Ready
/// after three seconds, present, physical, visible, health 100 of 100, conscious, aggressiveness 0.1, before any
/// key is ever pressed. Despawning that complete contact, only for F4's own on-demand construct path
/// to then rebuild an incomplete one from nothing (missing its avatar donor, and throwing a
/// <c>NullReferenceException</c> reviving health on a component that does not exist on a post-load
/// construction), was strictly worse than doing nothing. <see cref="Pump"/> now calls
/// <see cref="Release1ArthurFieldContactHarness.TryPark"/> instead of despawning, through the exact
/// same world boundary member family (<see cref="IRelease1SmallCourtesyWorld.TryReadFieldContact"/>,
/// then the boundary's park mutation) so the reconcile still carries the same <c>HasAuthority</c> guard
/// every other field contact seam already has. It still parks at most once per armed pass:
/// <see cref="Pump"/> clears its own pending flag the moment it gets a Ready read (present or not) or
/// the deadline expires, so a caller that keeps calling <see cref="Pump"/> every frame never
/// re-attempts after either outcome. A non-ready read is reported exactly once per armed pass too, the
/// first time it is seen, so a persistent fault logs one line instead of one line per attempt; every
/// attempt after that stays quiet until the contact resolves or the deadline expires.
/// </summary>
public sealed class Release1ArthurLoadReconcile
{
    /// <summary>The minimum wall clock gap between two read attempts.</summary>
    public const float RetryIntervalSeconds = 1.0f;

    /// <summary>
    /// Bounded so a permanently faulting or genuinely absent read does not retry forever. Ninety
    /// seconds of wall clock is well past the roughly one minute the live evidence's own manual F3
    /// read needed to succeed, without leaving the reconcile pending indefinitely.
    /// </summary>
    public const float MaxSeconds = 90f;

    /// <summary>The most that is ever kept of a read failure's own message text for a log line.</summary>
    private const int MaxDetailLength = 160;

    private readonly Func<float> _realtimeSinceStartup;
    private bool _pending;
    private bool _failureReported;
    private float _deadline;
    private float _nextAttemptAt;

    /// <param name="realtimeSinceStartup">
    /// A wall clock source, monotonically non-decreasing across a session. Production passes the
    /// engine's own realtime-since-startup clock; this file stays free of that engine reference so it
    /// stays test linkable, matching <see cref="Release1ArthurLoadReconcileTests"/>'s own source guard.
    /// </param>
    public Release1ArthurLoadReconcile(Func<float> realtimeSinceStartup)
    {
        _realtimeSinceStartup = realtimeSinceStartup ?? throw new ArgumentNullException(nameof(realtimeSinceStartup));
    }

    /// <summary>Arms the pump. Called once, unconditionally, from <c>HandleLoadComplete</c>.</summary>
    public void BeginAfterLoad()
    {
        var start = _realtimeSinceStartup();
        _pending = true;
        _failureReported = false;
        _deadline = start + MaxSeconds;
        _nextAttemptAt = start;
    }

    /// <summary>
    /// Called once per update pass while pending. Returns null on every pass with nothing new to
    /// report: waiting for the next throttled attempt, retrying a non-ready read already reported
    /// once, or the contact was proven absent. A null <paramref name="world"/> is treated the same way
    /// the rest of this boundary treats an unavailable world: the pump stays armed and does not spend
    /// an attempt, or even read the clock, waiting for a world to be composed.
    /// </summary>
    public Release1ArthurLoadReconcileStep? Pump(IRelease1SmallCourtesyWorld? world)
    {
        if (!_pending || world is null) return null;

        var now = _realtimeSinceStartup();
        if (now >= _deadline)
        {
            _pending = false;
            return new Release1ArthurLoadReconcileStep(Release1ArthurLoadReconcileOutcome.BoundExpired, null);
        }

        if (now < _nextAttemptAt) return null;
        _nextAttemptAt = now + RetryIntervalSeconds;

        Release1SmallCourtesyWorldReadStatus readStatus;
        Release1FieldContactSnapshot? snapshot;
        string? failureDetail = null;
        try
        {
            readStatus = world.TryReadFieldContact(Release1ArthurFieldContactHarness.ContactId, out snapshot);
        }
        catch (Exception ex)
        {
            // The world boundary is documented to never let an exception out; this is defence in
            // depth only, the same belt-and-suspenders wrapping Release1ArthurFieldContactHarness's
            // own Run and TryReport already put around every world call. If it ever does fire, the
            // exception's own message is the most useful thing to report.
            readStatus = Release1SmallCourtesyWorldReadStatus.Faulted;
            snapshot = null;
            failureDetail = Truncate(ex.Message, MaxDetailLength);
        }

        if (readStatus == Release1SmallCourtesyWorldReadStatus.Ready && snapshot is not null)
        {
            _pending = false;
            if (!snapshot.Present) return null;
            return new Release1ArthurLoadReconcileStep(
                Release1ArthurLoadReconcileOutcome.Parked,
                Release1ArthurFieldContactHarness.TryPark(world));
        }

        if (_failureReported) return null;
        _failureReported = true;
        failureDetail ??= Truncate($"the field contact read status was {readStatus}.", MaxDetailLength);
        return new Release1ArthurLoadReconcileStep(Release1ArthurLoadReconcileOutcome.ReadFailed, null, failureDetail);
    }

    private static string Truncate(string? text, int maxLength)
    {
        text ??= string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
#endif
