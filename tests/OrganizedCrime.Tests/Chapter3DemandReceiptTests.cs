using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3DemandReceiptTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Multi_slot_burst_hides_receipts_until_quiet()
    {
        var harness = new Harness();
        harness.SeedLocker(50, 40);

        harness.Service.PollProductionDemand();
        Assert.Equal(50, harness.Service.State!.ActiveDemand!.Delivered);
        Assert.Empty(harness.Receipts());
        harness.Seconds += 1f;
        harness.Service.PollProductionDemand();
        Assert.Equal(90, harness.Service.State.ActiveDemand!.Delivered);
        Assert.Equal(2, harness.World.Changes.Count);
        Assert.Equal(new[] { 1_250, 1_000 }, harness.Wallet.Credits);
        harness.Seconds += 2.75f;
        Assert.Empty(harness.Receipts());
        harness.Seconds += 0.25f;
        var receipt = Assert.Single(harness.Receipts());
        Assert.Equal("Got 90. 160 to go.", receipt.Text);
        Assert.Equal(Chapter3PresentationReceipt.Correlation(PlayerId, "demand-d1-receipt-2"), receipt.CorrelationId);
        Assert.True(harness.Service.TryRecordPresentationReceipt(receipt.CorrelationId));
        harness.Seconds += 10f;
        Assert.Empty(harness.Receipts());
        Assert.Equal(2, harness.World.Changes.Count);
        Assert.Equal(new[] { 1_250, 1_000 }, harness.Wallet.Credits);
    }

    [Fact]
    public void Single_slot_receipt_waits_three_seconds_with_a_long_running_clock()
    {
        var harness = new Harness { Seconds = 7 * 24 * 60 * 60 };
        harness.SeedLocker(100);

        harness.Service.PollProductionDemand();

        Assert.Equal(100, harness.Service.State!.ActiveDemand!.Delivered);
        Assert.Equal(new[] { 2_500 }, harness.Wallet.Credits);
        Assert.Empty(harness.Receipts());
        harness.Seconds += 2.75f;
        Assert.Empty(harness.Receipts());
        harness.Seconds += 0.25f;
        Assert.Equal("Got 100. 150 to go.", Assert.Single(harness.Receipts()).Text);
    }

    [Fact]
    public void Separate_bursts_reset_the_sum_only_after_the_previous_receipt_is_shown()
    {
        var harness = new Harness();
        harness.SeedLocker(50);
        harness.Service.PollProductionDemand();
        harness.Seconds += 3.001f;
        var firstReceipt = Assert.Single(harness.Receipts());
        Assert.Equal("Got 50. 200 to go.", firstReceipt.Text);
        Assert.True(harness.Service.TryRecordPresentationReceipt(firstReceipt.CorrelationId));

        harness.SeedLocker(40, 30);
        harness.Service.PollProductionDemand();
        harness.Seconds += 1f;
        harness.Service.PollProductionDemand();
        Assert.Empty(harness.Receipts());
        harness.Seconds += 3.001f;

        var secondReceipt = Assert.Single(harness.Receipts());
        Assert.Equal("Got 70. 130 to go.", secondReceipt.Text);
        Assert.Equal(Chapter3PresentationReceipt.Correlation(PlayerId, "demand-d1-receipt-3"), secondReceipt.CorrelationId);
        Assert.NotEqual(firstReceipt.CorrelationId, secondReceipt.CorrelationId);
        Assert.Equal(120, harness.Service.State!.ActiveDemand!.Delivered);
        Assert.Equal(new[] { 1_250, 1_000, 750 }, harness.Wallet.Credits);
    }

    [Fact]
    public void Completion_during_a_burst_sends_only_the_existing_completion_text()
    {
        var harness = new Harness();
        harness.SeedLocker(100, 150);
        harness.Service.PollProductionDemand();
        Assert.Empty(harness.Receipts());
        harness.Seconds += 1f;
        harness.Service.PollProductionDemand();

        Assert.Null(harness.Service.State!.ActiveDemand);
        Assert.True(harness.Service.State.LastDemandOutcome!.Completed);
        Assert.Equal(new[] { 2_500, 3_750 }, harness.Wallet.Credits);
        var completion = Assert.Single(harness.DemandMessages());
        Assert.Equal("That's the order. Family's happy, for now.", completion.Text);
        Assert.Equal(Chapter3PresentationReceipt.Correlation(PlayerId, "demand-d1-completed"), completion.CorrelationId);
        Assert.Empty(harness.Receipts());
        harness.Seconds += 3.001f;
        Assert.Equal(completion, Assert.Single(harness.DemandMessages()));
        Assert.True(harness.Service.TryRecordPresentationReceipt(completion.CorrelationId));
        Assert.Empty(harness.DemandMessages());
    }

    [Fact]
    public void Save_reload_preserves_unsent_burst_and_dedupes_the_shown_receipt()
    {
        var harness = new Harness();
        harness.SeedLocker(50, 40);
        harness.Service.PollProductionDemand();
        harness.Seconds += 1f;
        harness.Service.PollProductionDemand();
        Assert.Empty(harness.Receipts());
        harness.Save();

        Assert.Equal(90, harness.Repository.Stored!.ActiveDemand!.LastTakeUnits);
        Assert.Equal(2, harness.Repository.Stored.ActiveDemand.HandOverCount);
        Assert.Equal(90, harness.Repository.Stored.ActiveDemand.Delivered);
        Assert.Equal(2_250, harness.Repository.Stored.ActiveDemand.PaidTotal);
        harness.Load();
        var receipt = Assert.Single(harness.Receipts());
        Assert.Equal("Got 90. 160 to go.", receipt.Text);
        Assert.Equal(Chapter3PresentationReceipt.Correlation(PlayerId, "demand-d1-receipt-2"), receipt.CorrelationId);
        harness.Service.PollProductionDemand();
        Assert.Equal(2, harness.World.Changes.Count);
        Assert.Equal(new[] { 1_250, 1_000 }, harness.Wallet.Credits);

        Assert.True(harness.Service.TryRecordPresentationReceipt(receipt.CorrelationId));
        harness.Save();
        harness.Load();
        harness.Service.PollProductionDemand();
        Assert.Empty(harness.Receipts());
        Assert.Equal(90, harness.Service.State!.ActiveDemand!.Delivered);
        Assert.Equal(2_250, harness.Service.State.ActiveDemand.PaidTotal);
        Assert.Equal(2, harness.World.Changes.Count);
        Assert.Equal(new[] { 1_250, 1_000 }, harness.Wallet.Credits);
    }

    private sealed class Harness
    {
        public Chapter3ProductionDemandRuntimeTests.FakeDemandWorld World { get; } = new();
        public Repository Repository { get; } = new();
        public Wallet Wallet { get; } = new();
        public Chapter3RuntimeService Service { get; }
        public float Seconds { get; set; }

        public Harness()
        {
            Repository.Stored = new Chapter3State(
                PlayerId, Chapter3Phase.Active, 11, 11, 0,
                ActiveDemand: new Chapter3ProductionDemand(
                    "d1", "ogkush", "OG Kush", Chapter3DemandProductType.Weed,
                    250, 50, 0, 3, 250, 0, 0, 0, 0, 10, 0, 20, null));
            Service = new Chapter3RuntimeService(
                new Context(), Repository, new Gate(), new Clock(), _ => { });
            Service.DemandWorld = World;
            Service.Wallet = Wallet;
            Service.ArthurClock = () => Seconds;
            Load();
        }

        public void Load()
        {
            Assert.Equal(Chapter3RuntimeRejectReason.None, Service.OnPreLoad().RejectReason);
            Assert.Equal(Chapter3RuntimeRejectReason.None, Service.OnLoadComplete().RejectReason);
        }

        public void SeedLocker(params int[] quantities) => World.SeedLocker(
            quantities.Select((quantity, index) => new Chapter3DemandLockerSlot(
                index, "ogkush", quantity, false, "", 1)).ToArray());

        public void Save()
        {
            Assert.Equal(Chapter3RuntimeRejectReason.None, Service.OnSaveStart().RejectReason);
            Assert.Equal(Chapter3RuntimeRejectReason.None, Service.OnSaveComplete().RejectReason);
        }

        public Chapter3DesiredMessage[] DemandMessages() => Service.GetPresentationPlan(true)
            .Messages.Where(message => message.CorrelationId.Contains("demand-d1-", StringComparison.Ordinal)).ToArray();

        public Chapter3DesiredMessage[] Receipts() => Service.GetPresentationPlan(true)
            .Messages.Where(message => message.CorrelationId.Contains("-receipt-", StringComparison.Ordinal)).ToArray();
    }

    private sealed class Repository : IChapter3Repository, IChapter3SaveFolderBoundRepository
    {
        public string BoundSaveFolder => System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public Chapter3State? Stored { get; set; }
        public Chapter3StoreLoadResult Load() => new(true, Chapter3StoreLoadStatus.Loaded,
            new Chapter3SaveEnvelope(Chapter3SaveCodec.CurrentSchemaVersion, Stored),
            Chapter3StoreFailureReason.None, "Loaded.");
        public Chapter3StoreUpdateResult Update(Chapter3State? state)
        {
            Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var write), write.Message);
            Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var read), read.Message);
            Stored = envelope!.State;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "Saved.");
        }
    }

    private sealed class Wallet : IChapter3Wallet
    {
        public List<int> Credits { get; } = new();
        public Chapter3WalletCredit TryCredit(int amount)
        {
            Credits.Add(amount);
            return Chapter3WalletCredit.Credited;
        }
        public Chapter3WalletDebit TryDebit(int amount) => throw new NotImplementedException();
    }

    private sealed class Context : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = new(Guid.Parse("66666666-6666-6666-6666-666666666666"), 1, PlayerId, System.IO.Path.GetTempPath());
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class Gate : IChapter3StoryGate
    {
        public bool IsEnvelopeSatisfied() => true;
    }

    private sealed class Clock : IChapter3DayClock
    {
        public event Action? DayPassed { add { } remove { } }
        public event Action? WeekPassed { add { } remove { } }
        public bool TryReadElapsedDays(out int elapsedDays)
        {
            elapsedDays = 11;
            return true;
        }
    }
}
