using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// Fix for a live defect (2026-09-05), then a second live defect in that fix's own bounded-passes
/// retry, then a lifecycle change (a seventh spec review round) once a third live defect showed the
/// fix itself was undoing a complete contact only to have F4 rebuild an incomplete one. A frame is the
/// wrong unit for how long the native load time sweep needs; these tests drive
/// <see cref="Release1ArthurLoadReconcile"/> against a fake, controllable wall clock instead, proving
/// the retry is throttled to one attempt per second of wall clock, bounded at ninety seconds, logs a
/// read failure at most once per armed pass, and parks (never despawns) a contact it finds present.
/// </summary>
public sealed class Release1ArthurLoadReconcileTests
{
    private static Release1FieldContactSnapshot PresentSnapshot() => new(
        true, true, true, 4.5f, true,
        100f, 100f, true, false, false,
        90f, 100f, false, false, false, false,
        Release1FieldContactPursuitLevel.None, false, false, false,
        2, 1,
        0.5f, 20f, 8f, string.Empty);

    private static Release1FieldContactSnapshot AbsentSnapshot() => Release1FieldContactSnapshot.AbsentContact(
        90f, 100f, false, false, false, false,
        Release1FieldContactPursuitLevel.None, false, false, false, 2, 1);

    [Fact]
    public void Pump_retries_a_faulting_read_for_several_seconds_and_parks_exactly_once_and_reports_the_failure_exactly_once_once_ready()
    {
        var clock = new ManualClock();
        var world = new TimeGatedWorld(clock, readyAtSeconds: 5f);
        var reconcile = new Release1ArthurLoadReconcile(clock.RealtimeSinceStartup);
        reconcile.BeginAfterLoad();

        var failureSteps = new List<Release1ArthurLoadReconcileStep>();
        Release1ArthurLoadReconcileStep? parkStep = null;
        for (var second = 0; second <= 5; second++)
        {
            clock.Seconds = second;
            var step = reconcile.Pump(world);
            // A second update pass inside the very same wall clock second must not spend another
            // attempt: the throttle is on wall clock time, not on how many times Pump is called.
            Assert.Null(reconcile.Pump(world));

            if (step is not null)
            {
                if (step.Outcome == Release1ArthurLoadReconcileOutcome.ReadFailed) failureSteps.Add(step);
                else if (step.Outcome == Release1ArthurLoadReconcileOutcome.Parked) parkStep = step;
            }
        }

        Assert.Single(failureSteps);
        Assert.NotNull(parkStep);
        Assert.NotNull(parkStep!.ParkResult);
        Assert.Equal(Release1StagingHarnessStatus.Succeeded, parkStep.ParkResult!.Status);
        Assert.Equal(1, world.ParkCallCount);

        // Pending is cleared the moment a Ready read lands: a further pass makes no further attempt.
        var readsBeforeExtraPump = world.ReadCallCount;
        clock.Seconds = 6f;
        Assert.Null(reconcile.Pump(world));
        Assert.Equal(readsBeforeExtraPump, world.ReadCallCount);
        Assert.Equal(1, world.ParkCallCount);
    }

    [Fact]
    public void Pump_makes_at_most_one_attempt_per_second_of_wall_clock()
    {
        var clock = new ManualClock();
        var world = new SequencedWorld();
        var reconcile = new Release1ArthurLoadReconcile(clock.RealtimeSinceStartup);
        reconcile.BeginAfterLoad();

        clock.Seconds = 0f;
        Assert.NotNull(reconcile.Pump(world)); // the first attempt reports the one failure line.
        Assert.Equal(1, world.ReadCallCount);

        // A second, and third, pass in the same wall clock second must not attempt another read.
        Assert.Null(reconcile.Pump(world));
        Assert.Null(reconcile.Pump(world));
        Assert.Equal(1, world.ReadCallCount);

        // Just under a second later, still throttled.
        clock.Seconds = 0.99f;
        Assert.Null(reconcile.Pump(world));
        Assert.Equal(1, world.ReadCallCount);

        // A full second later, the next attempt is allowed.
        clock.Seconds = 1.0f;
        Assert.Null(reconcile.Pump(world)); // already reported once; stays quiet.
        Assert.Equal(2, world.ReadCallCount);
    }

    [Fact]
    public void Pump_parks_nothing_and_reports_nothing_when_the_contact_is_proven_absent()
    {
        var clock = new ManualClock();
        var world = new SequencedWorld((Release1SmallCourtesyWorldReadStatus.Ready, AbsentSnapshot()));
        var reconcile = new Release1ArthurLoadReconcile(clock.RealtimeSinceStartup);
        reconcile.BeginAfterLoad();

        Assert.Null(reconcile.Pump(world));
        Assert.Equal(0, world.ParkCallCount);
        Assert.Equal(1, world.ReadCallCount);

        // Pending is cleared once the contact is proven absent: a further pass reads nothing more.
        clock.Seconds = 1f;
        Assert.Null(reconcile.Pump(world));
        Assert.Equal(1, world.ReadCallCount);
    }

    [Fact]
    public void Pump_reports_the_deadline_exactly_once_at_ninety_seconds_and_makes_no_further_attempts()
    {
        var clock = new ManualClock();
        var world = new SequencedWorld();
        var reconcile = new Release1ArthurLoadReconcile(clock.RealtimeSinceStartup);
        reconcile.BeginAfterLoad();

        Release1ArthurLoadReconcileStep? failureStep = null;
        for (var second = 0; second < Release1ArthurLoadReconcile.MaxSeconds; second++)
        {
            clock.Seconds = second;
            var step = reconcile.Pump(world);
            if (step is not null)
            {
                Assert.Null(failureStep);
                Assert.Equal(Release1ArthurLoadReconcileOutcome.ReadFailed, step.Outcome);
                failureStep = step;
            }
        }
        Assert.NotNull(failureStep);
        Assert.Equal((int)Release1ArthurLoadReconcile.MaxSeconds, world.ReadCallCount);
        Assert.Equal(0, world.ParkCallCount);

        clock.Seconds = Release1ArthurLoadReconcile.MaxSeconds;
        var finalStep = reconcile.Pump(world);
        Assert.NotNull(finalStep);
        Assert.Equal(Release1ArthurLoadReconcileOutcome.BoundExpired, finalStep!.Outcome);
        Assert.Null(finalStep.ParkResult);
        // The deadline check runs before another attempt would be spent, so no extra read happened.
        Assert.Equal((int)Release1ArthurLoadReconcile.MaxSeconds, world.ReadCallCount);

        // The bound has expired and pending is cleared: no further attempts, ever.
        clock.Seconds = Release1ArthurLoadReconcile.MaxSeconds + 100f;
        Assert.Null(reconcile.Pump(world));
        Assert.Equal((int)Release1ArthurLoadReconcile.MaxSeconds, world.ReadCallCount);
    }

    [Fact]
    public void Pump_does_nothing_before_begin_after_load_is_called()
    {
        var clock = new ManualClock();
        var world = new SequencedWorld((Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot()));
        var reconcile = new Release1ArthurLoadReconcile(clock.RealtimeSinceStartup);

        Assert.Null(reconcile.Pump(world));
        Assert.Equal(0, world.ReadCallCount);
        Assert.Equal(0, world.ParkCallCount);
    }

    [Fact]
    public void Pump_stays_pending_and_makes_no_attempt_and_reads_no_clock_when_the_world_is_null()
    {
        var clock = new ManualClock();
        var reconcile = new Release1ArthurLoadReconcile(clock.RealtimeSinceStartup);
        reconcile.BeginAfterLoad();

        Assert.Null(reconcile.Pump(null));

        var world = new SequencedWorld((Release1SmallCourtesyWorldReadStatus.Ready, AbsentSnapshot()));
        Assert.Null(reconcile.Pump(world));
        Assert.Equal(1, world.ReadCallCount);
    }

    [Fact]
    public void The_reconcile_source_holds_no_unity_and_no_s1api_reference()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ArthurLoadReconcile.cs"));
        foreach (var forbidden in new[] { "UnityEngine", "S1API", "Il2Cpp", "Vector3", "MelonLoader" })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "tools", "OrganizedCrime"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    /// <summary>A fake, settable wall clock, standing in for <c>UnityEngine.Time.realtimeSinceStartup</c>.</summary>
    private sealed class ManualClock
    {
        public float Seconds;
        public float RealtimeSinceStartup() => Seconds;
    }

    /// <summary>
    /// A fake world whose field contact read is gated on the manual clock: faulted while the clock
    /// reads earlier than <paramref name="readyAtSeconds"/>, then Ready and present from that second
    /// on. Park always succeeds and reports the contact parked, matching what a real park does once
    /// the world finally reads the contact present.
    /// </summary>
    private sealed class TimeGatedWorld : IRelease1SmallCourtesyWorld
    {
        private readonly ManualClock _clock;
        private readonly float _readyAtSeconds;

        public TimeGatedWorld(ManualClock clock, float readyAtSeconds)
        {
            _clock = clock;
            _readyAtSeconds = readyAtSeconds;
        }

        public int ReadCallCount { get; private set; }
        public int ParkCallCount { get; private set; }

        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
        {
            ReadCallCount++;
            if (_clock.Seconds < _readyAtSeconds)
            {
                snapshot = Release1FieldContactSnapshot.Unavailable();
                return Release1SmallCourtesyWorldReadStatus.Faulted;
            }
            // Once parked, the contact still resolves present, exactly as a real parked contact does
            // (parking deactivates and hides it; it never removes it), so the confirm read Run appends
            // after the mutation itself sees the same present snapshot it already saw before parking.
            snapshot = PresentSnapshot();
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
        {
            ParkCallCount++;
            reason = "parked by the test fake";
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason) => throw new InvalidOperationException();

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(string deadDropGuid, Action<string> callback, out IRelease1SmallCourtesyDropSubscription? subscription) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason) { reason = string.Empty; throw new InvalidOperationException(); }
        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason) { reason = string.Empty; throw new InvalidOperationException(); }
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity) => throw new InvalidOperationException();
    }

    /// <summary>
    /// A fake world that hands back a fixed sequence of read results, one per call, then repeats its
    /// last entry (or a faulted, absent default when no sequence was given at all, matching the live
    /// evidence this fix addresses). Park always succeeds and reports the contact parked, matching
    /// what a real park does once the world finally reads the contact present.
    /// </summary>
    private sealed class SequencedWorld : IRelease1SmallCourtesyWorld
    {
        private readonly (Release1SmallCourtesyWorldReadStatus Status, Release1FieldContactSnapshot Snapshot)[] _sequence;
        private int _index;

        public SequencedWorld(params (Release1SmallCourtesyWorldReadStatus Status, Release1FieldContactSnapshot Snapshot)[] sequence)
        {
            _sequence = sequence.Length == 0
                ? new[] { (Release1SmallCourtesyWorldReadStatus.Faulted, Release1FieldContactSnapshot.Unavailable()) }
                : sequence;
        }

        public int ReadCallCount { get; private set; }
        public int ParkCallCount { get; private set; }

        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
        {
            ReadCallCount++;
            var entry = _sequence[Math.Min(_index, _sequence.Length - 1)];
            if (_index < _sequence.Length - 1) _index++;
            snapshot = entry.Snapshot;
            return entry.Status;
        }

        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
        {
            ParkCallCount++;
            reason = "parked by the test fake";
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason) => throw new InvalidOperationException();

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(string deadDropGuid, Action<string> callback, out IRelease1SmallCourtesyDropSubscription? subscription) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason) { reason = string.Empty; throw new InvalidOperationException(); }
        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason) { reason = string.Empty; throw new InvalidOperationException(); }
        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity) => throw new InvalidOperationException();
    }
}
