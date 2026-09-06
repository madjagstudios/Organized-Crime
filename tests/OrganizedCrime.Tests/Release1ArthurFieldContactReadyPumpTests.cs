using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-69 fix for a live defect, 2026-09-05: a field contact pressed for a provoke, or a presence decide
/// before F4 can toggle park versus unpark, both have the same several-second not-ready read window a
/// load constructed contact already has (see <see cref="Release1ArthurLoadReconcileTests"/> for that
/// original defect and its evidence). These tests drive <see cref="Release1ArthurFieldContactReadyPump"/>
/// against a fake, controllable wall clock, proving the retry is throttled the same way the load
/// reconcile's own is, and that a decide-then-park continuation, and a provoke, each run exactly once,
/// only once a read finally resolves Ready, never once per faulting attempt and never blind.
/// </summary>
public sealed class Release1ArthurFieldContactReadyPumpTests
{
    private static Release1FieldContactSnapshot PresentSnapshot() => new(
        true, true, true, 4.5f, true,
        100f, 100f, true, false, false,
        90f, 100f, false, false, false, false,
        Release1FieldContactPursuitLevel.None, false, false, false,
        2, 1,
        0.5f, 20f, 8f, string.Empty);

    [Fact]
    public void Pump_retries_a_faulting_read_before_the_f4_decision_for_several_seconds_then_decides_once()
    {
        // The F4 decide retry: a presence read that faults for a few seconds retries on the wall clock
        // window, and, the first time it goes Ready, decides exactly once (parks, since the snapshot
        // the pump hands back is present) and reports exactly once.
        var clock = new ManualClock();
        var world = new TimeGatedFieldContactWorld(clock, readyAtSeconds: 3f);
        var pump = new Release1ArthurFieldContactReadyPump(clock.RealtimeSinceStartup);
        var parked = false;
        pump.Begin((w, snapshot) => Release1ArthurFieldContactHarness.ToggleFromSnapshot(w, snapshot, ref parked));

        var failureSteps = new List<Release1ArthurReadyPumpStep>();
        Release1ArthurReadyPumpStep? readyStep = null;
        for (var second = 0; second <= 3; second++)
        {
            clock.Seconds = second;
            var step = pump.Pump(world);
            // A second update pass inside the very same wall clock second must not spend another
            // attempt: the throttle is on wall clock time, not on how many times Pump is called.
            Assert.Null(pump.Pump(world));

            if (step is null) continue;
            if (step.Outcome == Release1ArthurReadyPumpOutcome.ReadFailed) failureSteps.Add(step);
            else if (step.Outcome == Release1ArthurReadyPumpOutcome.Ready) readyStep = step;
        }

        Assert.Single(failureSteps);
        Assert.NotNull(readyStep);
        Assert.NotNull(readyStep!.Result);
        Assert.Equal(Release1StagingHarnessStatus.Succeeded, readyStep.Result!.Status);
        Assert.True(parked);
        Assert.Equal(1, world.ParkCallCount);

        // Pending is cleared the moment a Ready read lands: a further pass makes no further attempt and
        // never runs the continuation again.
        var readsBeforeExtraPump = world.ReadCallCount;
        clock.Seconds = 4f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(readsBeforeExtraPump, world.ReadCallCount);
        Assert.Equal(1, world.ParkCallCount);
    }

    [Fact]
    public void Pump_retries_a_faulting_read_after_a_provoke_press_for_several_seconds_then_provokes_exactly_once()
    {
        // F5 must not provoke blind. The pump retries the read until Ready and only then runs the
        // provoke continuation, exactly once.
        var clock = new ManualClock();
        var world = new TimeGatedFieldContactWorld(clock, readyAtSeconds: 2f);
        var pump = new Release1ArthurFieldContactReadyPump(clock.RealtimeSinceStartup);
        pump.Begin((w, snapshot) => Release1ArthurFieldContactHarness.TryProvoke(w));

        Release1ArthurReadyPumpStep? readyStep = null;
        for (var second = 0; second <= 2; second++)
        {
            clock.Seconds = second;
            var step = pump.Pump(world);
            if (step is not null && step.Outcome == Release1ArthurReadyPumpOutcome.Ready) readyStep = step;
        }

        Assert.NotNull(readyStep);
        Assert.NotNull(readyStep!.Result);
        Assert.Equal(Release1StagingHarnessStatus.Succeeded, readyStep.Result!.Status);
        Assert.Equal(1, world.ProvokeCallCount);
    }

    [Fact]
    public void Pump_makes_at_most_one_attempt_per_second_of_wall_clock()
    {
        var clock = new ManualClock();
        var world = new SequencedFieldContactWorld();
        var pump = new Release1ArthurFieldContactReadyPump(clock.RealtimeSinceStartup);
        var runCount = 0;
        pump.Begin((w, s) => { runCount++; return new Release1StagingHarnessResult(Release1StagingHarnessStatus.Succeeded, new[] { "ran" }); });

        clock.Seconds = 0f;
        Assert.NotNull(pump.Pump(world)); // the first attempt reports the one failure line.
        Assert.Equal(1, world.ReadCallCount);

        Assert.Null(pump.Pump(world));
        Assert.Null(pump.Pump(world));
        Assert.Equal(1, world.ReadCallCount);

        clock.Seconds = 0.99f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(1, world.ReadCallCount);

        clock.Seconds = 1.0f;
        Assert.Null(pump.Pump(world)); // already reported once; stays quiet.
        Assert.Equal(2, world.ReadCallCount);
        Assert.Equal(0, runCount);
    }

    [Fact]
    public void Pump_reports_the_deadline_exactly_once_at_ninety_seconds_and_never_runs_the_continuation()
    {
        var clock = new ManualClock();
        var world = new SequencedFieldContactWorld();
        var pump = new Release1ArthurFieldContactReadyPump(clock.RealtimeSinceStartup);
        var runCount = 0;
        pump.Begin((w, s) => { runCount++; return new Release1StagingHarnessResult(Release1StagingHarnessStatus.Succeeded, new[] { "ran" }); });

        Release1ArthurReadyPumpStep? failureStep = null;
        for (var second = 0; second < Release1ArthurFieldContactReadyPump.MaxSeconds; second++)
        {
            clock.Seconds = second;
            var step = pump.Pump(world);
            if (step is not null)
            {
                Assert.Null(failureStep);
                Assert.Equal(Release1ArthurReadyPumpOutcome.ReadFailed, step.Outcome);
                failureStep = step;
            }
        }
        Assert.NotNull(failureStep);

        clock.Seconds = Release1ArthurFieldContactReadyPump.MaxSeconds;
        var finalStep = pump.Pump(world);
        Assert.NotNull(finalStep);
        Assert.Equal(Release1ArthurReadyPumpOutcome.BoundExpired, finalStep!.Outcome);
        Assert.Null(finalStep.Result);
        Assert.Equal(0, runCount);
        Assert.False(pump.IsPending);
    }

    [Fact]
    public void Cancel_discards_a_pending_continuation_without_running_it()
    {
        var clock = new ManualClock();
        var world = new SequencedFieldContactWorld((Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot()));
        var pump = new Release1ArthurFieldContactReadyPump(clock.RealtimeSinceStartup);
        var runCount = 0;
        pump.Begin((w, s) => { runCount++; return new Release1StagingHarnessResult(Release1StagingHarnessStatus.Succeeded, new[] { "ran" }); });

        Assert.True(pump.IsPending);
        pump.Cancel();
        Assert.False(pump.IsPending);

        clock.Seconds = 5f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(0, runCount);
        Assert.Equal(0, world.ReadCallCount);
    }

    [Fact]
    public void Pump_does_nothing_before_begin_is_called()
    {
        var clock = new ManualClock();
        var world = new SequencedFieldContactWorld((Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot()));
        var pump = new Release1ArthurFieldContactReadyPump(clock.RealtimeSinceStartup);

        Assert.False(pump.IsPending);
        Assert.Null(pump.Pump(world));
        Assert.Equal(0, world.ReadCallCount);
    }

    [Fact]
    public void Pump_stays_pending_and_makes_no_attempt_and_reads_no_clock_when_the_world_is_null()
    {
        var clock = new ManualClock();
        var pump = new Release1ArthurFieldContactReadyPump(clock.RealtimeSinceStartup);
        pump.Begin((w, s) => new Release1StagingHarnessResult(Release1StagingHarnessStatus.Succeeded, new[] { "ran" }));

        Assert.Null(pump.Pump(null));

        var world = new SequencedFieldContactWorld((Release1SmallCourtesyWorldReadStatus.Ready, PresentSnapshot()));
        Assert.NotNull(pump.Pump(world));
    }

    [Fact]
    public void The_pump_source_holds_no_unity_and_no_s1api_reference()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ArthurFieldContactReadyPump.cs"));
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
    /// reads earlier than <paramref name="readyAtSeconds"/>, then Ready and present from that second on
    /// and forever after (matching how a real not-ready window resolves and then stays resolved).
    /// Tracks park and provoke call counts so a test can assert each ran exactly once.
    /// </summary>
    private sealed class TimeGatedFieldContactWorld : IRelease1SmallCourtesyWorld
    {
        private readonly ManualClock _clock;
        private readonly float _readyAtSeconds;

        public TimeGatedFieldContactWorld(ManualClock clock, float readyAtSeconds)
        {
            _clock = clock;
            _readyAtSeconds = readyAtSeconds;
        }

        public int ReadCallCount { get; private set; }
        public int ParkCallCount { get; private set; }
        public int ProvokeCallCount { get; private set; }

        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
        {
            ReadCallCount++;
            if (_clock.Seconds < _readyAtSeconds)
            {
                snapshot = Release1FieldContactSnapshot.Unavailable();
                return Release1SmallCourtesyWorldReadStatus.Faulted;
            }
            snapshot = PresentSnapshot();
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
        { ParkCallCount++; reason = "parked on retry"; return Release1SmallCourtesyWorldMutationStatus.Succeeded; }

        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)
        { ProvokeCallCount++; reason = "provoked"; return Release1SmallCourtesyWorldMutationStatus.Succeeded; }

        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason) => throw new InvalidOperationException();

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
    /// last entry (or a faulted, absent default when no sequence was given at all).
    /// </summary>
    private sealed class SequencedFieldContactWorld : IRelease1SmallCourtesyWorld
    {
        private readonly (Release1SmallCourtesyWorldReadStatus Status, Release1FieldContactSnapshot Snapshot)[] _sequence;
        private int _index;

        public SequencedFieldContactWorld(params (Release1SmallCourtesyWorldReadStatus Status, Release1FieldContactSnapshot Snapshot)[] sequence)
        {
            _sequence = sequence.Length == 0
                ? new[] { (Release1SmallCourtesyWorldReadStatus.Faulted, Release1FieldContactSnapshot.Unavailable()) }
                : sequence;
        }

        public int ReadCallCount { get; private set; }

        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
        {
            ReadCallCount++;
            var entry = _sequence[Math.Min(_index, _sequence.Length - 1)];
            if (_index < _sequence.Length - 1) _index++;
            snapshot = entry.Snapshot;
            return entry.Status;
        }

        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason) => throw new InvalidOperationException();
        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason) => throw new InvalidOperationException();

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
