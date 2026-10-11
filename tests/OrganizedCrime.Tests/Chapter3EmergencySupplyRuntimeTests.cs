using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;
using DeadDropFakeWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-124: the unpaid Emergency Supply job. The tick names a discovered
/// product into the offer (or falls back to a paid type); accept freezes one
/// drop plus the 12-hour window; delivery completes with no pay; a lapsed
/// window fails into a make-good at the nominal value.
/// </summary>
public sealed class Chapter3EmergencySupplyRuntimeTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Natural_emergency_offer_names_a_discovered_product()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        DeclineThrough(harness, 22);
        harness.TickTo(23);

        var offer = harness.Service.State!.JobBoard.OpenOffer!;
        Assert.Equal(5, offer.OfferId);
        Assert.Equal(Chapter3JobType.EmergencySupply, offer.JobType);
        var expected = Chapter3JobScheduler.SelectEmergencyProduct(harness.World.Products, PlayerId, 5)!;
        Assert.Equal(expected.ProductId, offer.ProductId);
        Assert.Equal(expected.ProductName, offer.ProductName);
        Assert.Equal(5, harness.Service.State.JobBoard.LastEmergencySupplyOfferId);
        Assert.Contains(Chapter3JobType.EmergencySupply, harness.Service.State.JobBoard.TypesSeen);
        Assert.Contains(harness.Logs, line => line.Contains("names", StringComparison.Ordinal));
    }

    [Fact]
    public void Emergency_offer_falls_back_to_a_paid_type_when_products_are_unreadable()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.ProductsStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;
        harness.Activate();
        DeclineThrough(harness, 22);
        harness.TickTo(23);

        AssertFallbackOffer(harness);
    }

    [Fact]
    public void Emergency_offer_falls_back_to_a_paid_type_when_nothing_is_discovered()
    {
        var harness = new Harness();
        harness.World.SeedProducts(new Release1SmallCourtesyProductCandidate("rich", "Rich", 5_000d, false));
        harness.World.SeedDrops(Drop("drop-a", "Drop A", true));
        harness.Activate();
        DeclineThrough(harness, 22);
        harness.TickTo(23);

        AssertFallbackOffer(harness);
    }

    private static void AssertFallbackOffer(Harness harness)
    {
        var board = harness.Service.State!.JobBoard;
        var offer = board.OpenOffer!;
        Assert.Equal(5, offer.OfferId);
        // The pre-tick last type was PackageRecovery, so the fallback keeps
        // the paid alternation with DeadDrop.
        Assert.Equal(Chapter3JobType.DeadDrop, offer.JobType);
        Assert.Null(offer.ProductId);
        Assert.Null(offer.ProductName);
        Assert.Equal(Chapter3JobType.DeadDrop, board.LastTypeOffered);
        Assert.DoesNotContain(Chapter3JobType.EmergencySupply, board.TypesSeen);
        Assert.Null(board.LastEmergencySupplyOfferId);
        Assert.Equal(6, board.NextOfferId);
        Assert.Contains(harness.Logs, line => line.Contains("fell back", StringComparison.Ordinal));
    }

    [Fact]
    public void QaOfferEmergency_forces_the_open_offer_and_names_a_product()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();

        var forced = harness.Service.QaOfferEmergency();

        Assert.True(forced.Ok, forced.Message);
        var offer = harness.Service.State!.JobBoard.OpenOffer!;
        Assert.Equal(1, offer.OfferId);
        Assert.Equal(Chapter3JobType.EmergencySupply, offer.JobType);
        var expected = Chapter3JobScheduler.SelectEmergencyProduct(harness.World.Products, PlayerId, 1)!;
        Assert.Equal(expected.ProductId, offer.ProductId);
        Assert.Equal(expected.ProductName, offer.ProductName);
        Assert.Equal(Chapter3JobType.EmergencySupply, harness.Service.State.JobBoard.LastTypeOffered);
        Assert.Contains(Chapter3JobType.EmergencySupply, harness.Service.State.JobBoard.TypesSeen);
        Assert.Equal(1, harness.Service.State.JobBoard.LastEmergencySupplyOfferId);
    }

    [Fact]
    public void QaOfferEmergency_refuses_without_a_product_and_touches_nothing()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.ProductsStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;
        harness.Activate();
        var before = harness.Service.State!;
        var revision = before.Revision;

        var forced = harness.Service.QaOfferEmergency();

        Assert.False(forced.Ok);
        Assert.Contains("no discovered product", forced.Message, StringComparison.Ordinal);
        Assert.Equal(before.JobBoard, harness.Service.State!.JobBoard);
        Assert.Equal(revision, harness.Service.State.Revision);
    }

    [Fact]
    public void QaOfferEmergency_arms_until_the_next_offer_then_releases()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryDeclineOffer().Applied);

        var armed = harness.Service.QaOfferEmergency();

        Assert.True(armed.Ok, armed.Message);
        harness.TickTo(12);
        harness.TickTo(13);
        harness.TickTo(14);
        var offer = harness.Service.State!.JobBoard.OpenOffer!;
        Assert.Equal(2, offer.OfferId);
        Assert.Equal(Chapter3JobType.EmergencySupply, offer.JobType);
        Assert.NotNull(offer.ProductId);

        // The flag is consumed: the offer after the forced one is natural.
        Assert.True(harness.Service.TryDeclineOffer().Applied);
        harness.TickTo(15);
        harness.TickTo(16);
        harness.TickTo(17);
        var next = harness.Service.State!.JobBoard.OpenOffer!;
        Assert.Equal(3, next.OfferId);
        Assert.Equal(Chapter3JobType.PackageRecovery, next.JobType);
    }

    [Fact]
    public void QaOfferEmergency_reports_an_already_emergency_offer()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);

        var again = harness.Service.QaOfferEmergency();

        Assert.True(again.Ok, again.Message);
        Assert.Contains("already Emergency Supply", again.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Declining_an_emergency_counts_as_a_refusal()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);

        var declined = harness.Service.TryDeclineOffer();

        Assert.True(declined.Applied);
        var board = harness.Service.State!.JobBoard;
        Assert.Null(board.OpenOffer);
        Assert.Null(board.ActiveJob);
        Assert.Equal(1, board.RefusalStreak);
        Assert.Equal(1, board.LastDeclinedOfferId);
        Assert.NotNull(board.CooldownUntilDay);
    }

    [Fact]
    public void Accept_freezes_the_named_drop_and_the_window()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        var offer = harness.Service.State!.JobBoard.OpenOffer!;

        var result = harness.Service.TryAcceptOffer();

        Assert.True(result.Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;
        Assert.Equal(Chapter3JobType.EmergencySupply, job.JobType);
        Assert.Equal(offer.ProductId, job.Delivery!.ProductId);
        Assert.Equal(offer.ProductName, job.Delivery.ProductName);
        Assert.Equal(Chapter3JobTuning.BricksForReward(offer.Reward), job.Delivery.BrickCount);
        Assert.Equal("drop-a", job.Delivery.DropGuid);
        Assert.Equal("Drop A", job.Delivery.DropName);
        Assert.Equal("brick", job.Delivery.PackagingId);
        Assert.False(job.Delivery.RemovalConfirmed);
        Assert.Equal(1_000d, job.AcceptedGameMinutes);
        Assert.Equal(1_720d, job.ExpiresGameMinutes);
        Assert.True(harness.World.DropClosedCallbacks.ContainsKey("drop-a"));
        Assert.Contains(harness.Logs, line => line.Contains("minutes to deliver", StringComparison.Ordinal));
    }

    [Fact]
    public void Accept_without_a_world_stays_unfrozen_and_windowless()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        harness.Service.DeadDropWorld = null;

        var result = harness.Service.TryAcceptOffer();

        Assert.True(result.Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;
        Assert.Null(job.Delivery);
        Assert.Null(job.AcceptedGameMinutes);
        Assert.Null(job.ExpiresGameMinutes);
        Assert.Empty(harness.World.DropClosedCallbacks);
    }

    [Fact]
    public void Accept_with_no_empty_drop_is_refused_and_keeps_the_offer()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.SeedDrops(Drop("drop-a", "Drop A", false));
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);

        var result = harness.Service.TryAcceptOffer();

        Assert.False(result.Applied);
        var board = harness.Service.State!.JobBoard;
        Assert.NotNull(board.OpenOffer);
        Assert.Null(board.ActiveJob);
        Assert.Equal(board.OpenOffer!.OfferId, board.LastRefusedOfferId);
        Assert.Contains(harness.Logs, line => line.Contains("no empty dead drop", StringComparison.Ordinal));
    }

    [Fact]
    public void Accept_with_an_unreadable_clock_is_refused_and_keeps_the_offer()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutesStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);

        var result = harness.Service.TryAcceptOffer();

        Assert.False(result.Applied);
        var board = harness.Service.State!.JobBoard;
        Assert.NotNull(board.OpenOffer);
        Assert.Null(board.ActiveJob);
        Assert.Equal(board.OpenOffer!.OfferId, board.LastRefusedOfferId);
        Assert.Contains(harness.Logs, line => line.Contains("game clock was unreadable", StringComparison.Ordinal));
    }

    [Fact]
    public void QaAccept_reports_the_window()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);

        var accepted = harness.Service.QaAcceptOpenOffer();

        Assert.True(accepted.Ok, accepted.Message);
        Assert.Contains("expires in 720 minutes", accepted.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Delivery_of_the_named_product_completes_with_no_pay()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;
        var nominal = job.Reward;

        harness.World.SeedSlots("drop-a", Slot(0, job.Delivery!.ProductId, "brick", job.Delivery.BrickCount));
        harness.World.FireDropClosed("drop-a");

        var board = harness.Service.State!.JobBoard;
        Assert.Null(board.ActiveJob);
        Assert.Equal(1, board.SuccessfulJobs);
        Assert.Equal(new Chapter3JobCompletion(1, Chapter3JobType.EmergencySupply, nominal), board.LastCompletedJob);
        Assert.Empty(harness.World.CashChanges);
        Assert.Equal(0f, harness.World.CashBalance);
        Assert.Empty(harness.World.DropClosedCallbacks);
        Assert.Contains(harness.Logs, line => line.Contains("completed with no pay", StringComparison.Ordinal));
    }

    [Fact]
    public void Delivery_holds_on_the_wrong_product()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;
        var other = job.Delivery!.ProductId == "cheap" ? "rich" : "cheap";

        harness.World.SeedSlots("drop-a", Slot(0, other, "brick", job.Delivery.BrickCount));
        harness.World.FireDropClosed("drop-a");

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Empty(harness.World.CashChanges);
        Assert.Contains(harness.Logs, line => line.Contains("holding", StringComparison.Ordinal));
    }

    [Fact]
    public void Delivery_holds_when_short()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;

        harness.World.SeedSlots("drop-a", Slot(0, job.Delivery!.ProductId, "brick", job.Delivery.BrickCount - 1));
        harness.World.FireDropClosed("drop-a");

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Empty(harness.World.CashChanges);
    }

    [Fact]
    public void Delivery_holds_when_spread()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var job = harness.Service.State!.JobBoard.ActiveJob!;

        harness.World.SeedSlots(
            "drop-a",
            Slot(0, job.Delivery!.ProductId, "brick", job.Delivery.BrickCount),
            Slot(1, job.Delivery.ProductId, "brick", job.Delivery.BrickCount));
        harness.World.FireDropClosed("drop-a");

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Empty(harness.World.CashChanges);
    }

    [Fact]
    public void QaDeliver_refuses_an_emergency_supply()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        Assert.True(harness.Service.TryAcceptOffer().Applied);

        var delivered = harness.Service.QaDeliverActiveJob();

        Assert.False(delivered.Ok);
        Assert.Contains("emergency supply run", delivered.Message, StringComparison.Ordinal);
        Assert.Empty(harness.World.Inserts);
        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
    }

    [Fact]
    public void Window_is_open_at_719_and_failed_at_720()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        var nominal = harness.Service.State!.JobBoard.ActiveJob!.Reward;

        harness.World.CanonicalMinutes = 1_719d;
        harness.Service.PollEmergencyExpiry();
        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);

        harness.World.CanonicalMinutes = 1_720d;
        harness.Service.PollEmergencyExpiry();

        var state = harness.Service.State!;
        Assert.Null(state.JobBoard.ActiveJob);
        Assert.Equal(0, state.JobBoard.SuccessfulJobs);
        var entry = Assert.Single(state.Ledger.Entries);
        var failed = Assert.IsType<Chapter3FailedJobSource>(entry.Source);
        Assert.Equal(1, failed.JobId);
        Assert.Equal(Chapter3JobType.EmergencySupply, failed.JobType);
        Assert.Equal(nominal, entry.Amount);
        Assert.Empty(harness.World.CashChanges);
        Assert.Empty(harness.World.DropClosedCallbacks);
        Assert.Contains(harness.Logs, line => line.Contains("expired", StringComparison.Ordinal));
    }

    [Fact]
    public void Expiry_runs_at_the_day_tick()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        Assert.True(harness.Service.TryAcceptOffer().Applied);

        harness.World.CanonicalMinutes = 2_000d;
        harness.TickTo(12);

        Assert.Null(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Single(harness.Service.State!.Ledger.Entries);
        Assert.Contains(harness.Logs, line => line.Contains("expired", StringComparison.Ordinal));
    }

    [Fact]
    public void Expiry_runs_after_load()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();
        Assert.True(
            Chapter3SaveCodec.TrySerialize(harness.Repository.Stored, out var json, out var serialized),
            serialized.Message);
        Assert.True(
            Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var deserialized),
            deserialized.Message);
        Assert.Equal(1_720d, envelope!.State!.JobBoard.ActiveJob!.ExpiresGameMinutes);

        var reloaded = new Harness();
        reloaded.Repository.Stored = envelope.State;
        reloaded.World.CanonicalMinutes = 1_720d;
        reloaded.Activate();

        Assert.Null(reloaded.Service.State!.JobBoard.ActiveJob);
        var entry = Assert.Single(reloaded.Service.State!.Ledger.Entries);
        Assert.Equal(Chapter3JobType.EmergencySupply, Assert.IsType<Chapter3FailedJobSource>(entry.Source).JobType);
    }

    [Fact]
    public void Poll_skips_quietly_when_the_clock_is_unreadable()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        harness.World.CanonicalMinutesStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;

        harness.Service.PollEmergencyExpiry();
        harness.Service.PollEmergencyExpiry();

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.Single(harness.Logs, line => line.Contains("game clock was unreadable", StringComparison.Ordinal));
    }

    [Fact]
    public void Poll_ignores_paid_jobs()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);

        harness.World.CanonicalMinutes = 99_000d;
        harness.Service.PollEmergencyExpiry();

        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
        Assert.DoesNotContain(harness.Logs, line => line.Contains("expired", StringComparison.Ordinal));
    }

    [Fact]
    public void Minutes_left_reports_whole_minutes_down()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.World.CanonicalMinutes = 1_000d;
        harness.Activate();
        Assert.True(harness.Service.QaOfferEmergency().Ok);
        Assert.True(harness.Service.TryAcceptOffer().Applied);

        Assert.Equal(720, harness.Service.QaEmergencyMinutesLeft());
        harness.World.CanonicalMinutes = 1_300.5d;
        Assert.Equal(419, harness.Service.QaEmergencyMinutesLeft());
    }

    [Fact]
    public void Minutes_left_is_null_without_a_readable_window()
    {
        var harness = new Harness();
        SeedFreezableWorld(harness.World);
        harness.Activate();
        Assert.True(harness.Service.TryAcceptOffer().Applied);
        Assert.Null(harness.Service.QaEmergencyMinutesLeft());

        var emergency = new Harness();
        SeedFreezableWorld(emergency.World);
        emergency.World.CanonicalMinutes = 1_000d;
        emergency.Activate();
        Assert.True(emergency.Service.QaOfferEmergency().Ok);
        Assert.True(emergency.Service.TryAcceptOffer().Applied);
        emergency.World.CanonicalMinutesStatus = Release1SmallCourtesyWorldReadStatus.Unavailable;
        Assert.Null(emergency.Service.QaEmergencyMinutesLeft());

        var windowless = new Harness();
        SeedFreezableWorld(windowless.World);
        windowless.Activate();
        Assert.True(windowless.Service.QaOfferEmergency().Ok);
        windowless.Service.DeadDropWorld = null;
        Assert.True(windowless.Service.TryAcceptOffer().Applied);
        Assert.Null(windowless.Service.QaEmergencyMinutesLeft());
    }

    private static void DeclineThrough(Harness harness, int lastDeclinedElapsedDays)
    {
        for (var elapsed = 11; elapsed <= lastDeclinedElapsedDays; elapsed++)
        {
            harness.TickTo(elapsed);
            if (harness.Service.State!.JobBoard.OpenOffer is not null)
                Assert.True(harness.Service.TryDeclineOffer().Applied);
        }
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

    private static Release1SmallCourtesyDropCandidate Drop(string guid, string name, bool isEmpty) =>
        new(guid, name, "description", 1d, 2d, 3d, isEmpty);

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
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("66666666-6666-6666-6666-666666666666"), 1, PlayerId, System.IO.Path.GetTempPath());

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class FakeRepository : IChapter3Repository, IChapter3SaveFolderBoundRepository, IChapter3RawContentPreservingRepository
    {
        public string BoundSaveFolder { get; } = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
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
