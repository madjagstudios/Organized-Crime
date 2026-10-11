using System.Reflection;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;
using DeadDropFakeWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

// OC-147 characterization: delivery holds report once per job and reason,
// re-arm at the load boundaries, and are keyed per job (the same reason under
// two jobs logs twice). Pinned before OnceLog replaces the delivery set;
// passes unchanged after.
public sealed class DeliveryHoldWarnOnceTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Emergency_clock_hold_rearms_across_a_save_load_round_trip()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.CanonicalMinutesStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;
        int Count() => harness.Logs.Count(line => line.Contains("game clock was unreadable", StringComparison.Ordinal));

        harness.Service.PollEmergencyExpiry();
        Assert.Equal(1, Count());
        harness.Service.PollEmergencyExpiry();
        Assert.Equal(1, Count());

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        harness.Service.OnPreLoad();
        harness.Service.OnLoadComplete();

        harness.Service.PollEmergencyExpiry();
        harness.Service.PollEmergencyExpiry();
        Assert.Equal(2, Count());
    }

    [Fact]
    public void Same_delivery_hold_under_two_jobs_logs_once_per_job()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var firstId = harness.Service.State!.JobBoard.ActiveJob!.JobId;
        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 1));
        harness.World.FireDropClosed("drop-a");
        int Holding() => harness.Logs.Count(line => line.Contains("holding", StringComparison.Ordinal));
        Assert.Equal(1, Holding());

        // Fail job 1 at its deadline through the day tick, which (unlike the
        // completion paths) does not clear the hold set.
        harness.TickTo(14);
        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);

        // Accept the next DeadDrop job, declining anything else.
        for (var day = 15; day <= 60 && harness.Service.State!.JobBoard.ActiveJob is null; day++)
        {
            harness.TickTo(day);
            var offer = harness.Service.State!.JobBoard.OpenOffer;
            if (offer is null)
                continue;
            if (offer.JobType == Chapter3JobType.DeadDrop)
                Assert.True(harness.Service.TryAcceptOffer().Applied);
            else
                Assert.True(harness.Service.TryDeclineOffer().Applied);
        }
        var second = harness.Service.State!.JobBoard.ActiveJob;
        Assert.NotNull(second);
        Assert.NotEqual(firstId, second!.JobId);

        harness.World.SeedSlots("drop-a", Slot(0, "rich", "brick", 1));
        harness.World.FireDropClosed("drop-a");

        Assert.Equal(2, Holding());
        Assert.Single(harness.Logs, line => line.Contains("holding", StringComparison.Ordinal) &&
            line.Contains($"job {firstId} ", StringComparison.Ordinal));
        Assert.Single(harness.Logs, line => line.Contains("holding", StringComparison.Ordinal) &&
            line.Contains($"job {second.JobId} ", StringComparison.Ordinal));
    }

    [Fact]
    public void Delivery_hold_prune_drops_old_job_keys_and_keeps_current_suppressed()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        int Unreadable() => harness.Logs.Count(line => line.Contains("the drop was unreadable", StringComparison.Ordinal));

        // Accept a DeadDrop job first, declining anything else: only a
        // deadline-failed DeadDrop leaves the hold set untouched (an
        // EmergencySupply expiry clears it).
        var acceptedDay = 0;
        var day = 12;
        for (; day <= 60 && harness.Service.State!.JobBoard.ActiveJob is null; day++)
        {
            harness.TickTo(day);
            var offer = harness.Service.State!.JobBoard.OpenOffer;
            if (offer is null)
                continue;
            if (offer.JobType == Chapter3JobType.DeadDrop)
            {
                Assert.True(harness.Service.TryAcceptOffer().Applied);
                acceptedDay = day;
            }
            else
            {
                Assert.True(harness.Service.TryDeclineOffer().Applied);
            }
        }
        var first = harness.Service.State!.JobBoard.ActiveJob;
        Assert.NotNull(first);
        Assert.Equal(Chapter3JobType.DeadDrop, first!.JobType);

        // Job 1 holds on an unreadable drop and logs once.
        harness.World.SlotsStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;
        harness.World.FireDropClosed("drop-a");
        Assert.Equal(1, Unreadable());
        Assert.True(DeliveryHoldReported(harness.Service, $"{first.JobId}:unreadable"));

        // Fail job 1 at its deadline through the day tick, which (unlike the
        // completion paths) does not clear the hold set; the repeated checks
        // stay silent.
        for (; day <= acceptedDay + 6 && harness.Service.State!.JobBoard.ActiveJob is not null; day++)
            harness.TickTo(day);
        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Equal(1, Unreadable());
        Assert.True(DeliveryHoldReported(harness.Service, $"{first.JobId}:unreadable"));

        // Accept the next DeadDrop job, declining anything else.
        harness.World.SlotsStatus = Release1SmallCourtesyWorldReadStatus.Ready;
        for (; day <= acceptedDay + 60 && harness.Service.State!.JobBoard.ActiveJob is null; day++)
        {
            harness.TickTo(day);
            var offer = harness.Service.State!.JobBoard.OpenOffer;
            if (offer is null)
                continue;
            if (offer.JobType == Chapter3JobType.DeadDrop)
                Assert.True(harness.Service.TryAcceptOffer().Applied);
            else
                Assert.True(harness.Service.TryDeclineOffer().Applied);
        }
        var second = harness.Service.State!.JobBoard.ActiveJob;
        Assert.NotNull(second);
        Assert.NotEqual(first.JobId, second!.JobId);

        // Job 2's first delivery check prunes job 1's keys, then holds on the
        // same unreadable drop under its own key.
        harness.World.SlotsStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;
        harness.World.FireDropClosed("drop-a");
        Assert.Equal(2, Unreadable());
        Assert.False(DeliveryHoldReported(harness.Service, $"{first.JobId}:unreadable"));
        Assert.True(DeliveryHoldReported(harness.Service, $"{second.JobId}:unreadable"));

        // A second check on the same job stays suppressed: the prune keeps the
        // current job's keys.
        harness.World.FireDropClosed("drop-a");
        Assert.Equal(2, Unreadable());
        Assert.True(DeliveryHoldReported(harness.Service, $"{second.JobId}:unreadable"));
        harness.World.SlotsStatus = Release1SmallCourtesyWorldReadStatus.Ready;
    }

    // Reads the private delivery-hold set through the service's context. The
    // OnceLog answers Contains(string); JobIds never repeat within a loaded
    // board, so a pruned old key can never log again: absence from the set is
    // the only observable proof the prune ran.
    private static bool DeliveryHoldReported(Chapter3RuntimeService service, string key)
    {
        var context = (Chapter3FeatureContext)typeof(Chapter3RuntimeService).GetField(
            "_ctx", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        var set = context.LoggedDeliveryHolds;
        Assert.NotNull(set);
        var contains = set!.GetType().GetMethod("Contains", new[] { typeof(string) });
        Assert.NotNull(contains);
        return (bool)contains!.Invoke(set, new object[] { key })!;
    }

    private static void SeedFreezableWorld(DeadDropFakeWorld world)
    {
        world.SeedProducts(
            new Release1SmallCourtesyProductCandidate("cheap", "Cheap", 3_000d, true),
            new Release1SmallCourtesyProductCandidate("rich", "Rich", 5_000d, true));
        world.SeedDrops(new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "description", 1d, 2d, 3d, true));
    }

    private static Release1SmallCourtesySlotSnapshot Slot(int index, string? productId, string? packagingId, int quantity) =>
        new(index, productId, packagingId, quantity, quantity > 0, 0f);

    private sealed class Harness
    {
        public DeadDropFakeWorld World { get; } = new();
        public FakeRepository Repository { get; } = new();
        public FakeClock Clock { get; } = new() { ElapsedDays = 11 };
        public List<string> Logs { get; } = new();
        public Chapter3RuntimeService Service { get; }

        public Harness()
        {
            Service = new Chapter3RuntimeService(
                new FakeContext(), Repository, new FakeGate { Satisfied = true }, Clock, Logs.Add);
            Service.Wallet = new Chapter3Wallet(World);
            Service.DeadDropWorld = World;
        }

        public void Activate()
        {
            Service.OnPreLoad();
            Service.OnLoadComplete();
            Service.OnDayPassed();
        }

        public void TickTo(int elapsedDays)
        {
            Clock.ElapsedDays = elapsedDays;
            Service.OnDayPassed();
        }
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("66666666-6666-6666-6666-666666666666"), 1, PlayerId, Path.GetTempPath());

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class FakeRepository : IChapter3Repository, IChapter3SaveFolderBoundRepository, IChapter3RawContentPreservingRepository
    {
        public string BoundSaveFolder { get; } = Path.GetFullPath(Path.GetTempPath());
        public Chapter3State? Stored { get; set; }

        public Chapter3StoreLoadResult Load() => Stored is null
            ? new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "Fake empty sidecar.")
            : new(true, Chapter3StoreLoadStatus.Loaded, new Chapter3SaveEnvelope(Chapter3SaveCodec.CurrentSchemaVersion, Stored), Chapter3StoreFailureReason.None, "Fake loaded sidecar.");

        public Chapter3StoreUpdateResult Update(Chapter3State? state)
        {
            Stored = state;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake stored.");
        }

        public Chapter3StoreUpdateResult PreserveRawContents(string rawContents) =>
            new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Fake preserved.");
    }

    private sealed class FakeGate : IChapter3StoryGate
    {
        public bool Satisfied { get; set; }
        public bool IsEnvelopeSatisfied() => Satisfied;
    }

    private sealed class FakeClock : IChapter3DayClock
    {
        public int ElapsedDays { get; set; }
        public bool Readable { get; set; } = true;
        public event Action? DayPassed;
        public event Action? WeekPassed;

        public bool TryReadElapsedDays(out int elapsedDays)
        {
            elapsedDays = ElapsedDays;
            return Readable;
        }

        public void RaiseDay() => DayPassed?.Invoke();

        public void RaiseWeek() => WeekPassed?.Invoke();
    }
}
