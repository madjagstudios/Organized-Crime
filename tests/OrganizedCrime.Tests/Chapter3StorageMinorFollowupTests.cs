using OrganizedCrime.Core.Diagnostics;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;
using FakeWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

public sealed class Chapter3StorageMinorFollowupTests
{
    [Fact]
    public void Failed_accept_marks_refusal_and_preserves_the_open_quote()
    {
        var h = new Harness();
        h.Freeze();
        var quote = h.Offer;
        var revision = h.Ctx.State!.Revision;
        h.World.Drops.RemoveAll(drop => drop.Guid == quote.StorageRun!.Route.Handoff.Guid);

        Assert.False(h.Offers.AcceptOfferLocked(quote, 0).Applied);

        Assert.Equal(quote, h.Ctx.State.JobBoard.OpenOffer);
        Assert.Equal(quote.OfferId, h.Ctx.State.JobBoard.LastRefusedOfferId);
        Assert.Equal(revision + 1, h.Ctx.State.Revision);
        Assert.Null(h.Ctx.State.JobBoard.ActiveJob);
        Assert.Empty(h.World.Inserts);
    }

    [Fact]
    public void Not_ready_storage_does_not_block_day_cuts_make_goods_or_demands()
    {
        var h = new Harness();
        h.Accept();
        var ledger = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty,
            99, Chapter3JobType.DeadDrop, 15_000, 0).Ledger;
        var state = h.Ctx.State! with
        {
            Cuts = new Chapter3WeeklyCuts(new[] { new Chapter3WeeklyCut(1, 30_000, 0, 2, Chapter3WeeklyCutStatus.Due) }, 0),
            Ledger = ledger,
            ActiveDemand = new Chapter3ProductionDemand("d1", "rich", "Rich", Chapter3DemandProductType.Weed,
                10, 100, 0, 2, 0, 0, 0, 0, 0, null, null, null, null)
        };
        var clock = new Clock();
        using var service = new Chapter3RuntimeService(h.Context, new Repository(state), new Gate(), clock, h.Logs.Add)
        {
            DeadDropWorld = h.World, Wallet = new Chapter3Wallet(h.World)
        };
        service.WireStorageRoom(() => h.Room, () => null);
        service.OnPreLoad();
        service.OnLoadComplete();
        h.Context.Ready = false;
        clock.Day = 3;

        service.OnDayPassed();

        Assert.Equal(3, service.State!.LastProcessedElapsedDay);
        Assert.Equal(3, service.State.JobBoard.LastProcessedDay);
        Assert.Equal(h.Run, service.State.JobBoard.ActiveJob!.StorageRun);
        Assert.Equal(Chapter3WeeklyCutStatus.Missed, Assert.Single(service.State.Cuts.Entries).Status);
        Assert.Equal(Chapter3MakeGoodStatus.Overdue,
            Assert.Single(service.State.Ledger.Entries, e => e.Source is Chapter3FailedJobSource).Status);
        Assert.Contains(service.State.Ledger.Entries, e => e.Source == new Chapter3MissedCutSource(1) && e.Amount == 37_500);
        Assert.Null(service.State.ActiveDemand);
        Assert.False(service.State.LastDemandOutcome!.Completed);
        Assert.Contains(service.State.Ledger.Entries, e => e.Source == new Chapter3MissedDemandSource("d1") && e.Amount == 1_000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unreadable_stage_drop_reports_unknown_instead_of_room_zero(bool deliver)
    {
        var h = new Harness();
        h.Accept();
        if (deliver) h.Release();
        h.World.SlotsStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;

        var status = h.Storage.Status();

        Assert.Contains(status, line => line.Contains(deliver ? "stage deliver" : "stage collect"));
        Assert.Contains(status, line => line.Contains("observed unreadable/ambiguous"));
        Assert.DoesNotContain(status, line => line.Contains("observed 0;"));
        Assert.Empty(h.World.QuantityChanges);
        Assert.Empty(h.World.CashChanges);
    }

    [Theory]
    [InlineData("pickup-unreadable")]
    [InlineData("awaiting-pickup")]
    [InlineData("handoff-unreadable")]
    [InlineData("handoff-missing")]
    [InlineData("unpaid")]
    public void Custody_handoff_and_payment_holds_log_once_per_reason(string reason)
    {
        var h = new Harness();
        h.Accept();
        if (reason.StartsWith("handoff", StringComparison.Ordinal) || reason == "unpaid") h.Release();
        if (reason.EndsWith("unreadable", StringComparison.Ordinal))
            h.World.SlotsStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;
        if (reason == "unpaid")
        {
            h.World.SeedSlots(h.Run.Route.Handoff.Guid, Chapter3StorageRuntimeTests.Slot(2));
            h.World.CashStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
        h.Logs.Clear();
        h.Ctx.LoggedDeliveryHolds.ClearAll();

        h.Poll();
        h.Poll();

        var hold = Assert.Single(h.Logs, line => line.Contains("storage holding:"));
        Assert.Contains(reason == "unpaid" ? "unpaid" : reason.StartsWith("handoff", StringComparison.Ordinal) ? "handoff" : "pickup", hold);
        Assert.NotNull(h.Ctx.State!.JobBoard.ActiveJob);
    }

    [Fact]
    public void Unreadable_pickup_cleanup_reports_skip_without_touching_stock()
    {
        var h = new Harness();
        h.Accept();
        h.World.SlotsStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;
        h.Logs.Clear();

        h.Storage.Fail(0);

        Assert.Contains(h.Logs, line => line.Contains("cleanup skipped:") && line.Contains("pickup drop was unreadable"));
        Assert.Empty(h.World.QuantityChanges);
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob);
    }

    private sealed class Harness
    {
        internal readonly FakeWorld World = new();
        internal readonly HostContext Context = new();
        internal readonly List<string> Logs = new();
        internal readonly Chapter3FeatureContext Ctx;
        internal readonly Chapter3StorageFeature Storage;
        internal readonly Chapter3OffersFeature Offers;
        internal Release1HoldRoomSnapshot Room;
        internal Chapter3JobOffer Offer => Ctx.State!.JobBoard.OpenOffer!;
        internal Chapter3StorageRun Run => Ctx.State!.JobBoard.ActiveJob!.StorageRun!;

        internal Harness()
        {
            World.SeedProducts(new Release1SmallCourtesyProductCandidate("rich", "Rich", 100, true));
            World.SeedDrops(new("a", "A", "Drop A", 1, 2, 3, true), new("b", "B", "Drop B", 4, 5, 6, true));
            World.SeedSlots("a", Chapter3StorageRuntimeTests.Slot(0));
            World.SeedSlots("b", Chapter3StorageRuntimeTests.Slot(0));
            Room = new(Release1HoldRoomReadiness.Ready, SyndicateHqStorageContract.Definitions.Select(d =>
                new Release1HoldRoomClosetSnapshot(d.Guid.ToString(), 1, new[] { Chapter3StorageRuntimeTests.Slot(0) })).ToArray());
            Ctx = new Chapter3FeatureContext
            {
                Context = Context, Phase = Chapter3RuntimePhase.Active,
                State = Chapter3State.CreateInitial("player") with
                {
                    Phase = Chapter3Phase.Active, StartElapsedDay = 0, LastProcessedElapsedDay = 0,
                    JobBoard = Chapter3JobBoard.Empty with { OpenOffer = new(1, Chapter3JobType.TemporaryStorageSafehouse, 16_000, 0, 1) }
                },
                DeadDropWorld = World, Wallet = new Chapter3Wallet(World), Log = Logs.Add,
                LoggedDeliveryHolds = new OnceLog(Logs.Add), StorageRoomReader = () => Room
            };
            var delivery = new Chapter3DeliveryFeature(Ctx, _ => Poll());
            Storage = new Chapter3StorageFeature(Ctx, delivery);
            Ctx.Storage = Storage;
            Offers = new Chapter3OffersFeature(Ctx, delivery);
        }

        internal void Freeze()
        {
            Assert.True(Storage.TryFreezeOffer(Offer, out var quote));
            Ctx.State = Ctx.State! with { JobBoard = Ctx.State.JobBoard with { OpenOffer = quote } };
        }

        internal void Accept() { Freeze(); Assert.True(Offers.AcceptOfferLocked(Offer, 0).Applied); }
        internal void Poll() => Storage.Poll(0);
        internal void Release()
        {
            World.SeedSlots(Run.Route.Pickup.Guid, Chapter3StorageRuntimeTests.Slot(0));
            var closets = Room.Closets.ToArray();
            closets[0] = closets[0] with { Slots = new[] { Chapter3StorageRuntimeTests.Slot(2) } };
            Room = Room with { Closets = closets };
            World.CanonicalMinutes = 100;
            Poll();
            World.CanonicalMinutes = 820;
            Poll();
            Assert.True(Run.Released);
            Room = Room with { Closets = Room.Closets.Select(c => c with { Slots = new[] { Chapter3StorageRuntimeTests.Slot(0) } }).ToArray() };
        }
    }

    private sealed class HostContext : IRelease1StoryHostContext
    {
        internal bool Ready = true;
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = new(Guid.Parse("66666666-6666-6666-6666-666666666666"), 1, "player", System.IO.Path.GetTempPath());
            return Ready ? Release1StoryHostContextReadStatus.Ready : Release1StoryHostContextReadStatus.NotAuthoritative;
        }
    }

    private sealed class Gate : IChapter3StoryGate { public bool IsEnvelopeSatisfied() => true; }
    private sealed class Clock : IChapter3DayClock
    {
        internal int Day;
        public event Action? DayPassed { add { } remove { } }
        public event Action? WeekPassed { add { } remove { } }
        public bool TryReadElapsedDays(out int days) { days = Day; return true; }
    }

    private sealed class Repository : IChapter3Repository, IChapter3SaveFolderBoundRepository
    {
        private readonly Chapter3State _state;
        internal Repository(Chapter3State state) => _state = state;
        public string BoundSaveFolder => System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public Chapter3StoreLoadResult Load() =>
            new(true, Chapter3StoreLoadStatus.Loaded, new Chapter3SaveEnvelope(17, _state), Chapter3StoreFailureReason.None, "");
        public Chapter3StoreUpdateResult Update(Chapter3State? state) =>
            new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "");
    }
}
