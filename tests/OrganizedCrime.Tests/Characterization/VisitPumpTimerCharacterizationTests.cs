using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-149: timer-boundary pins for Chapter3ArthurVisitPump. The visit machine,
// grace periods, bounds-by-walk and attack incidents are pinned by
// Chapter3ArthurVisitPumpTests; this file pins the exact retry/deadline/HQ
// throttle boundaries (exactly at, just before, just after), the backwards
// clock, and the BeginAfterLoad re-arm. Read/place counts distinguish a pass
// that stays quiet from a pass that never attempted.
public sealed class VisitPumpTimerCharacterizationTests
{
    [Fact]
    public void Retry_passes_at_start_throttles_just_before_and_passes_exactly_at_one_second()
    {
        var now = 0f;
        var world = new TimerWorld { Status = Chapter3ArthurReadStatus.Faulted };
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();

        Assert.NotNull(pump.Pump(world));
        Assert.Equal(1, world.Reads);

        now = 0.99f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(1, world.Reads);

        now = 1.0f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(2, world.Reads);
    }

    [Fact]
    public void A_backwards_clock_throttles_without_reading_until_time_catches_up()
    {
        var now = 10f;
        var world = new TimerWorld { Status = Chapter3ArthurReadStatus.Faulted };
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();

        now = 5f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(0, world.Reads);

        now = 10f;
        Assert.NotNull(pump.Pump(world));
        Assert.Equal(1, world.Reads);
    }

    [Fact]
    public void Deadline_attempts_just_before_and_expires_exactly_at_ninety_seconds_without_a_read()
    {
        var now = 0f;
        var world = new TimerWorld { Status = Chapter3ArthurReadStatus.Faulted };
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();

        Assert.NotNull(pump.Pump(world));
        Assert.Equal(1, world.Reads);

        now = 89.999f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(2, world.Reads);

        now = Chapter3ArthurVisitPump.MaxSeconds;
        var expired = pump.Pump(world);
        Assert.NotNull(expired);
        Assert.Equal(Chapter3ArthurVisitOutcome.BoundExpired, expired!.Outcome);
        Assert.Equal(2, world.Reads);
    }

    [Fact]
    public void BeginAfterLoad_re_arms_attempts_and_the_failure_report_after_expiry()
    {
        var now = 0f;
        var world = new TimerWorld { Status = Chapter3ArthurReadStatus.Faulted };
        var pump = new Chapter3ArthurVisitPump(() => now);
        pump.BeginAfterLoad();

        Assert.NotNull(pump.Pump(world));
        now = Chapter3ArthurVisitPump.MaxSeconds;
        Assert.NotNull(pump.Pump(world));
        Assert.Equal(1, world.Reads);

        pump.BeginAfterLoad();
        var rearmed = pump.Pump(world);
        Assert.NotNull(rearmed);
        Assert.Equal(Chapter3ArthurVisitOutcome.ReadFailed, rearmed!.Outcome);
        Assert.Equal(2, world.Reads);
    }

    [Fact]
    public void Hq_attempt_throttles_just_before_and_passes_exactly_at_one_second()
    {
        var now = 0f;
        var world = new TimerWorld
        {
            Status = Chapter3ArthurReadStatus.Ready,
            Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, 0f, 0f), 10f, 1),
            PlaceStatus = Chapter3ArthurMutationStatus.Rejected,
        };
        var pump = new Chapter3ArthurVisitPump(() => now) { HqPresence = new InsidePresence { ChapterActive = true, PlayerInsideHq = true } };
        pump.BeginAfterLoad();

        Assert.NotNull(pump.Pump(world));
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);

        now = 5f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(1, world.Places);

        now = 5.5f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(1, world.Places);

        now = 6.0f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(2, world.Places);
    }

    [Fact]
    public void Hq_maintain_skips_just_before_and_places_exactly_at_a_quarter_second()
    {
        var now = 0f;
        var world = new TimerWorld
        {
            Status = Chapter3ArthurReadStatus.Ready,
            Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, 0f, 0f), 10f, 1),
        };
        var pump = new Chapter3ArthurVisitPump(() => now) { HqPresence = new InsidePresence { ChapterActive = true, PlayerInsideHq = true } };
        pump.BeginAfterLoad();

        Assert.NotNull(pump.Pump(world));
        now = 5f;
        var placed = pump.Pump(world);
        Assert.NotNull(placed);
        Assert.Equal(Chapter3ArthurVisitOutcome.HqPlaced, placed!.Outcome);
        Assert.Equal(Chapter3ArthurVisitState.AtHq, pump.State);
        Assert.Equal(1, world.Places);

        now = 5.24f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(1, world.Places);

        now = 5.25f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(2, world.Places);
    }

    [Fact]
    public void Visit_end_with_failed_hq_place_defers_the_next_attempt_by_the_interval()
    {
        var now = 0f;
        var world = new TimerWorld
        {
            Status = Chapter3ArthurReadStatus.Ready,
            Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, 0f, 0f), 10f, 1),
            PlaceStatus = Chapter3ArthurMutationStatus.Rejected,
        };
        var presence = new InsidePresence { ChapterActive = true, PlayerInsideHq = false };
        var pump = new Chapter3ArthurVisitPump(() => now) { HqPresence = presence };
        pump.BeginAfterLoad();

        Assert.NotNull(pump.Pump(world));
        Assert.True(pump.BeginCome());
        presence.PlayerInsideHq = true;

        now = 5f;
        var ended = pump.Pump(world);
        Assert.NotNull(ended);
        Assert.Equal(Chapter3ArthurVisitOutcome.Parked, ended!.Outcome);
        Assert.Equal(Chapter3ArthurVisitState.Parked, pump.State);
        Assert.Equal(1, world.Places);

        now = 5.5f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(1, world.Places);

        now = 6.0f;
        Assert.Null(pump.Pump(world));
        Assert.Equal(2, world.Places);
    }

    [Fact]
    public void Retry_at_fractional_start_passes_exactly_at_the_float_rounded_deadline()
    {
        float start = 0.3f;
        float deadline = start + Chapter3ArthurVisitPump.RetryIntervalSeconds;
        float justBefore = MathF.BitDecrement(deadline);
        float justAfter = MathF.BitIncrement(deadline);

        var nowA = start;
        var worldA = new TimerWorld { Status = Chapter3ArthurReadStatus.Faulted };
        var pumpA = new Chapter3ArthurVisitPump(() => nowA);
        pumpA.BeginAfterLoad();

        Assert.NotNull(pumpA.Pump(worldA));
        Assert.Equal(1, worldA.Reads);

        nowA = justBefore;
        Assert.Null(pumpA.Pump(worldA));
        Assert.Equal(1, worldA.Reads);

        nowA = deadline;
        Assert.Null(pumpA.Pump(worldA));
        Assert.Equal(2, worldA.Reads);

        var nowB = start;
        var worldB = new TimerWorld { Status = Chapter3ArthurReadStatus.Faulted };
        var pumpB = new Chapter3ArthurVisitPump(() => nowB);
        pumpB.BeginAfterLoad();

        Assert.NotNull(pumpB.Pump(worldB));

        nowB = justAfter;
        Assert.Null(pumpB.Pump(worldB));
        Assert.Equal(2, worldB.Reads);
    }

    [Fact]
    public void Hq_attempt_at_fractional_start_passes_exactly_at_the_float_rounded_deadline()
    {
        float start = 0.3f;
        float deadline = start + Chapter3ArthurVisitPump.HqAttemptIntervalSeconds;
        float justBefore = MathF.BitDecrement(deadline);
        float justAfter = MathF.BitIncrement(deadline);

        var nowA = start;
        var worldA = new TimerWorld
        {
            Status = Chapter3ArthurReadStatus.Ready,
            Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, 0f, 0f), 10f, 1),
            PlaceStatus = Chapter3ArthurMutationStatus.Rejected,
        };
        var pumpA = new Chapter3ArthurVisitPump(() => nowA) { HqPresence = new InsidePresence { ChapterActive = true, PlayerInsideHq = true } };
        pumpA.BeginAfterLoad();

        Assert.NotNull(pumpA.Pump(worldA));
        Assert.Equal(0, worldA.Places);

        Assert.Null(pumpA.Pump(worldA));
        Assert.Equal(1, worldA.Places);

        nowA = justBefore;
        Assert.Null(pumpA.Pump(worldA));
        Assert.Equal(1, worldA.Places);

        nowA = deadline;
        Assert.Null(pumpA.Pump(worldA));
        Assert.Equal(2, worldA.Places);

        var nowB = start;
        var worldB = new TimerWorld
        {
            Status = Chapter3ArthurReadStatus.Ready,
            Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, 0f, 0f), 10f, 1),
            PlaceStatus = Chapter3ArthurMutationStatus.Rejected,
        };
        var pumpB = new Chapter3ArthurVisitPump(() => nowB) { HqPresence = new InsidePresence { ChapterActive = true, PlayerInsideHq = true } };
        pumpB.BeginAfterLoad();

        Assert.NotNull(pumpB.Pump(worldB));
        Assert.Null(pumpB.Pump(worldB));
        Assert.Equal(1, worldB.Places);

        nowB = justAfter;
        Assert.Null(pumpB.Pump(worldB));
        Assert.Equal(2, worldB.Places);
    }

    [Fact]
    public void Hq_maintain_at_fractional_start_passes_exactly_at_the_float_rounded_deadline()
    {
        float placedAt = 0.15f;
        float firstMaintainAt = placedAt + Chapter3ArthurHqSpot.MaintainIntervalSeconds;
        float deadline = firstMaintainAt + Chapter3ArthurHqSpot.MaintainIntervalSeconds;
        float justBefore = MathF.BitDecrement(deadline);
        float justAfter = MathF.BitIncrement(deadline);

        var nowA = placedAt;
        var worldA = new TimerWorld
        {
            Status = Chapter3ArthurReadStatus.Ready,
            Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, 0f, 0f), 10f, 1),
        };
        var pumpA = new Chapter3ArthurVisitPump(() => nowA) { HqPresence = new InsidePresence { ChapterActive = true, PlayerInsideHq = true } };
        pumpA.BeginAfterLoad();

        Assert.NotNull(pumpA.Pump(worldA));

        var placed = pumpA.Pump(worldA);
        Assert.NotNull(placed);
        Assert.Equal(Chapter3ArthurVisitOutcome.HqPlaced, placed!.Outcome);
        Assert.Equal(1, worldA.Places);

        nowA = firstMaintainAt;
        Assert.Null(pumpA.Pump(worldA));
        Assert.Equal(2, worldA.Places);

        nowA = justBefore;
        Assert.Null(pumpA.Pump(worldA));
        Assert.Equal(2, worldA.Places);

        nowA = deadline;
        Assert.Null(pumpA.Pump(worldA));
        Assert.Equal(3, worldA.Places);

        var nowB = placedAt;
        var worldB = new TimerWorld
        {
            Status = Chapter3ArthurReadStatus.Ready,
            Next = new Chapter3ArthurSnapshot(true, true, true, false, true, (0f, 0f, 0f), 10f, 1),
        };
        var pumpB = new Chapter3ArthurVisitPump(() => nowB) { HqPresence = new InsidePresence { ChapterActive = true, PlayerInsideHq = true } };
        pumpB.BeginAfterLoad();

        Assert.NotNull(pumpB.Pump(worldB));
        Assert.NotNull(pumpB.Pump(worldB));
        Assert.Equal(1, worldB.Places);

        nowB = firstMaintainAt;
        Assert.Null(pumpB.Pump(worldB));
        Assert.Equal(2, worldB.Places);

        nowB = justAfter;
        Assert.Null(pumpB.Pump(worldB));
        Assert.Equal(3, worldB.Places);
    }

    private sealed class InsidePresence : IChapter3HqPresence
    {
        public bool ChapterActive { get; set; }
        public bool PlayerInsideHq { get; set; }
    }

    private sealed class TimerWorld : IChapter3ArthurWorld
    {
        public bool HasAuthority => true;
        public Chapter3ArthurReadStatus Status = Chapter3ArthurReadStatus.Ready;
        public Chapter3ArthurSnapshot? Next = Chapter3ArthurSnapshot.Absent();
        public Chapter3ArthurMutationStatus PlaceStatus = Chapter3ArthurMutationStatus.Succeeded;
        public int Reads;
        public int Places;

        public bool PollArthurImpact() => false;

        public Chapter3ArthurReadStatus TryReadArthur(out Chapter3ArthurSnapshot snapshot)
        {
            Reads++;
            snapshot = Next ?? Chapter3ArthurSnapshot.Absent();
            return Status;
        }

        public Chapter3ArthurMutationStatus TryMoveOutOfSight(out string reason)
        {
            reason = "moved out of sight";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryParkArthur(out string reason)
        {
            reason = "parked";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryVerifyAppearancePaths(out string reason)
        {
            reason = "paths verified";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryComeArthur(out string reason) => throw new InvalidOperationException();
        public Chapter3ArthurMutationStatus TrySayArthur(string text, out string reason) => throw new InvalidOperationException();
        public Chapter3ArthurMutationStatus TryLeaveArthur(out string reason) => throw new InvalidOperationException();
        public Chapter3ArthurMutationStatus TryFacePlayer(out string reason) => throw new InvalidOperationException();

        public Chapter3ArthurMutationStatus TryFacePlayerAtHq(out string reason)
        {
            reason = "faced directly";
            return Chapter3ArthurMutationStatus.Succeeded;
        }

        public Chapter3ArthurMutationStatus TryCaptureHqSpot(out (float X, float Y, float Z) local, out float yawDegrees, out string reason) => throw new InvalidOperationException();

        public Chapter3ArthurMutationStatus TryPlaceAtHq(out string reason)
        {
            Places++;
            reason = PlaceStatus == Chapter3ArthurMutationStatus.Succeeded ? "placed at the HQ spot" : "the HQ interior is not created.";
            return PlaceStatus;
        }

        public Chapter3ArthurMutationStatus TryResumeWalk(out string reason) => throw new InvalidOperationException();
        public Chapter3ArthurMutationStatus TryDedupeArthur(out string reason) => throw new InvalidOperationException();
        public Chapter3ArthurMutationStatus TryPlayAnimationTrigger(string trigger, out string reason) => throw new InvalidOperationException();
    }
}
