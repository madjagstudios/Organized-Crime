#if OC_OWNER_SPIKES
namespace OrganizedCrime.Runtime;

/// <summary>
/// What <see cref="Release1ArthurFieldContactReadyPump.Pump"/> reports on a pass that actually has
/// something to log. Every other pass returns null: still waiting for the next throttled attempt, or
/// still retrying a non-ready read that was already reported once this arming.
/// </summary>
public enum Release1ArthurReadyPumpOutcome
{
    Ready,
    ReadFailed,
    BoundExpired
}

public sealed record Release1ArthurReadyPumpStep(
    Release1ArthurReadyPumpOutcome Outcome,
    Release1StagingHarnessResult? Result,
    string? Detail = null);

/// <summary>
/// OC-69 fix for a live defect, 2026-09-05: a freshly spawned field contact has the same not-ready
/// window a load constructed one does (see <see cref="Release1ArthurLoadReconcile"/>'s own doc comment
/// for the original defect and its evidence), so a read taken right after a spawn or a provoke can
/// fault for several seconds before the contact's underlying engine object is reachable. Reading right
/// after mutating used to be synchronous everywhere on this harness (see <c>Run</c> in
/// <see cref="Release1ArthurFieldContactHarness"/>), which is exactly what let that fault reach the
/// owner's console instead of a clean report.
///
/// This pump holds one pending continuation at a time, retries <c>TryReadFieldContact</c> on the same
/// one-attempt-per-second, ninety-second wall clock window <see cref="Release1ArthurLoadReconcile"/>
/// uses, and runs the continuation exactly once, the first time the read resolves Ready, handing it the
/// ready snapshot so it never has to read again to get one. A non-ready read is reported at most once
/// per armed pass, matching the load reconcile's own quiet-retry behaviour. <see cref="Begin"/> arms a
/// new continuation (replacing any still pending), <see cref="Cancel"/> discards a pending one without
/// running it (used when a despawn press should win over a spawn's own still-pending follow-up), and
/// <see cref="Pump"/> is called once per update pass, matching the load reconcile's own calling
/// convention.
/// </summary>
public sealed class Release1ArthurFieldContactReadyPump
{
    /// <summary>The minimum wall clock gap between two read attempts, shared with the load reconcile.</summary>
    public const float RetryIntervalSeconds = Release1ArthurLoadReconcile.RetryIntervalSeconds;

    /// <summary>The wall clock bound on one armed pass, shared with the load reconcile.</summary>
    public const float MaxSeconds = Release1ArthurLoadReconcile.MaxSeconds;

    /// <summary>The most that is ever kept of a read failure's own message text for a log line.</summary>
    private const int MaxDetailLength = 160;

    private readonly Func<float> _realtimeSinceStartup;
    private Func<IRelease1SmallCourtesyWorld, Release1FieldContactSnapshot, Release1StagingHarnessResult>? _onReady;
    private bool _failureReported;
    private float _deadline;
    private float _nextAttemptAt;

    /// <param name="realtimeSinceStartup">
    /// A wall clock source, monotonically non-decreasing across a session. Production passes the
    /// engine's own realtime-since-startup clock; this file stays free of that engine reference so it
    /// stays test linkable, matching <see cref="Release1ArthurLoadReconcile"/>'s own source guard.
    /// </param>
    public Release1ArthurFieldContactReadyPump(Func<float> realtimeSinceStartup)
    {
        _realtimeSinceStartup = realtimeSinceStartup ?? throw new ArgumentNullException(nameof(realtimeSinceStartup));
    }

    /// <summary>True from <see cref="Begin"/> until the continuation runs, is cancelled, or the bound expires.</summary>
    public bool IsPending => _onReady is not null;

    /// <summary>
    /// Arms the pump with a continuation to run the first time a read resolves Ready. Replaces any
    /// continuation still pending from an earlier arming without running it.
    /// </summary>
    public void Begin(Func<IRelease1SmallCourtesyWorld, Release1FieldContactSnapshot, Release1StagingHarnessResult> onReady)
    {
        _onReady = onReady ?? throw new ArgumentNullException(nameof(onReady));
        var start = _realtimeSinceStartup();
        _failureReported = false;
        _deadline = start + MaxSeconds;
        _nextAttemptAt = start;
    }

    /// <summary>Discards a pending continuation without running it. A no-op when nothing is pending.</summary>
    public void Cancel() => _onReady = null;

    /// <summary>
    /// Called once per update pass while pending. Returns null on every pass with nothing new to
    /// report: waiting for the next throttled attempt, or retrying a non-ready read already reported
    /// once. A null <paramref name="world"/> is treated the same way the rest of this boundary treats an
    /// unavailable world: the pump stays armed and does not spend an attempt, or even read the clock,
    /// waiting for a world to be composed.
    /// </summary>
    public Release1ArthurReadyPumpStep? Pump(IRelease1SmallCourtesyWorld? world)
    {
        if (_onReady is null || world is null) return null;

        var now = _realtimeSinceStartup();
        if (now >= _deadline)
        {
            _onReady = null;
            return new Release1ArthurReadyPumpStep(Release1ArthurReadyPumpOutcome.BoundExpired, null);
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
            // depth only, the same belt-and-suspenders wrapping every other caller of a world member
            // already puts around its own call.
            readStatus = Release1SmallCourtesyWorldReadStatus.Faulted;
            snapshot = null;
            failureDetail = Truncate(ex.Message, MaxDetailLength);
        }

        if (readStatus == Release1SmallCourtesyWorldReadStatus.Ready && snapshot is not null)
        {
            var onReady = _onReady;
            _onReady = null;
            return new Release1ArthurReadyPumpStep(Release1ArthurReadyPumpOutcome.Ready, onReady(world, snapshot));
        }

        if (_failureReported) return null;
        _failureReported = true;
        failureDetail ??= Truncate($"the field contact read status was {readStatus}.", MaxDetailLength);
        return new Release1ArthurReadyPumpStep(Release1ArthurReadyPumpOutcome.ReadFailed, null, failureDetail);
    }

    private static string Truncate(string? text, int maxLength)
    {
        text ??= string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
#endif
