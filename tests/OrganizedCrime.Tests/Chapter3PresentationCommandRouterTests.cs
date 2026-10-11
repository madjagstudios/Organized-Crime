using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3PresentationCommandRouterTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Accept_routes_to_the_service()
    {
        var harness = new Harness();
        harness.Activate();

        var result = harness.Router.TryInvoke(new Chapter3AcceptCommand());

        Assert.True(result.Handled);
        Assert.Equal("job 1", result.Detail);
        Assert.NotNull(harness.Service.State!.JobBoard.ActiveJob);
    }

    [Fact]
    public void Decline_routes_to_the_service()
    {
        var harness = new Harness();
        harness.Activate();

        var result = harness.Router.TryInvoke(new Chapter3DeclineCommand());

        Assert.True(result.Handled);
        Assert.Equal("offer 1 declined", result.Detail);
        Assert.Equal(1, harness.Service.State!.JobBoard.LastDeclinedOfferId);
    }

    [Fact]
    public void Stale_accept_reports_stale()
    {
        var harness = new Harness();
        harness.Activate();
        Assert.True(harness.Router.TryInvoke(new Chapter3AcceptCommand()).Handled);

        var result = harness.Router.TryInvoke(new Chapter3AcceptCommand());

        Assert.False(result.Handled);
        Assert.Equal("stale", result.Detail);
    }

    [Fact]
    public void Pay_make_good_routes_to_the_service()
    {
        var harness = new Harness();
        harness.Activate();
        harness.Service.TryAcceptOffer();
        Assert.True(harness.Service.QaFailActiveJob().Ok);
        var entry = Assert.Single(harness.Service.State!.Ledger.Entries);

        var result = harness.Router.TryInvoke(new Chapter3PayMakeGoodCommand(entry.Id));

        Assert.True(result.Handled);
        Assert.Equal($"make-good {entry.Id}", result.Detail);
        Assert.Equal(Chapter3MakeGoodStatus.Paid, Assert.Single(harness.Service.State!.Ledger.Entries).Status);
    }

    [Fact]
    public void Short_pay_reports_short()
    {
        var harness = new Harness();
        harness.Activate();
        harness.Service.TryAcceptOffer();
        Assert.True(harness.Service.QaFailActiveJob().Ok);
        var entry = Assert.Single(harness.Service.State!.Ledger.Entries);
        harness.Wallet.Balance = 0;

        var result = harness.Router.TryInvoke(new Chapter3PayMakeGoodCommand(entry.Id));

        Assert.False(result.Handled);
        Assert.Equal($"make-good {entry.Id} (short)", result.Detail);
    }

    [Fact]
    public void Unknown_make_good_reports_stale()
    {
        var harness = new Harness();
        harness.Activate();

        var result = harness.Router.TryInvoke(new Chapter3PayMakeGoodCommand(7));

        Assert.False(result.Handled);
        Assert.Equal("stale", result.Detail);
    }

    [Fact]
    public void Pay_cut_routes_to_the_service()
    {
        var harness = new Harness(owned: true);
        harness.Activate();
        Assert.True(harness.Service.QaSimulateWeekPass().Ok);

        var result = harness.Router.TryInvoke(new Chapter3PayCutCommand(1));

        Assert.True(result.Handled);
        Assert.Equal("cut week 1", result.Detail);
        Assert.Equal(Chapter3WeeklyCutStatus.Paid, Assert.Single(harness.Service.State!.Cuts.Entries).Status);
    }

    [Fact]
    public void Unknown_cut_reports_stale()
    {
        var harness = new Harness();
        harness.Activate();

        var result = harness.Router.TryInvoke(new Chapter3PayCutCommand(99));

        Assert.False(result.Handled);
        Assert.Equal("stale", result.Detail);
    }

    [Fact]
    public void Ineligible_buy_time_reports_stale()
    {
        var harness = new Harness();
        harness.Activate();

        var result = harness.Router.TryInvoke(new Chapter3BuyTimeCommand());

        Assert.False(result.Handled);
        Assert.Equal("stale", result.Detail);
    }

    [Fact]
    public void Buy_warehouse_routes_to_the_service()
    {
        var harness = new Harness();
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        harness.Wallet.Balance = 400_000;

        var result = harness.Router.TryInvoke(new Chapter3BuyWarehouseCommand());

        Assert.True(result.Handled);
        Assert.Equal("warehouse bought", result.Detail);
        Assert.True(harness.Service.State!.WarehouseOffer!.Purchased);
        Assert.Equal(new[] { 300_000 }, harness.Wallet.Debits);
        Assert.Empty(harness.Acquisition.Calls);
        Assert.True(harness.Service.HasPendingWarehouseBuild);
    }

    [Fact]
    public void Short_warehouse_buy_reports_short()
    {
        var harness = new Harness();
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        harness.Wallet.Balance = 10_000;

        var result = harness.Router.TryInvoke(new Chapter3BuyWarehouseCommand());

        Assert.False(result.Handled);
        Assert.Equal("warehouse (short)", result.Detail);
        Assert.False(harness.Service.State!.WarehouseOffer!.Purchased);
    }

    [Fact]
    public void Warehouse_buy_without_an_offer_reports_stale()
    {
        var harness = new Harness();
        harness.Activate();

        var result = harness.Router.TryInvoke(new Chapter3BuyWarehouseCommand());

        Assert.False(result.Handled);
        Assert.Equal("stale", result.Detail);
    }

    [Fact]
    public void Unready_warehouse_buy_reports_unavailable()
    {
        var harness = new Harness();
        harness.Activate();
        Assert.True(harness.Service.QaOfferWarehouse().Ok);
        harness.Wallet.Balance = 400_000;
        harness.Acquisition.CanAcquireValue = false;

        var result = harness.Router.TryInvoke(new Chapter3BuyWarehouseCommand());

        Assert.False(result.Handled);
        Assert.Equal("warehouse (unavailable)", result.Detail);
        Assert.Empty(harness.Wallet.Debits);
    }

    [Fact]
    public void Unknown_command_reports_stale()
    {
        var harness = new Harness();
        harness.Activate();

        Assert.Equal(new Chapter3ReplyResult(false, "stale"), harness.Router.TryInvoke(new UnknownCommand()));
        Assert.Equal(new Chapter3ReplyResult(false, "stale"), harness.Router.TryInvoke(null!));
    }

    private sealed record UnknownCommand : Chapter3PresentationCommand;

    private sealed class Harness
    {
        public Chapter3RuntimeService Service { get; }
        public Chapter3PresentationCommandRouter Router { get; }
        public FakeWallet Wallet { get; } = new() { Balance = 100_000 };
        public FakeAcquisition Acquisition { get; } = new();

        public Harness(bool owned = false)
        {
            Service = new Chapter3RuntimeService(
                new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true },
                new FakeClock { ElapsedDays = 11 }, null, new FakeOwnership { Owned = owned });
            Service.Wallet = Wallet;
            Service.WarehouseAcquisition = Acquisition;
            Router = new Chapter3PresentationCommandRouter(Service);
        }

        public void Activate()
        {
            Service.OnPreLoad();
            Service.OnLoadComplete();
            Service.OnDayPassed();
        }
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("55555555-5555-5555-5555-555555555555"), 1, PlayerId, System.IO.Path.GetTempPath());

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

    private sealed class FakeOwnership : IChapter3WarehouseOwnership
    {
        public bool Owned { get; set; }
        public bool IsOwned() => Owned;
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

    private sealed class FakeWallet : IChapter3Wallet
    {
        public int Balance { get; set; }
        public List<int> Debits { get; } = new();

        public Chapter3WalletDebit TryDebit(int amount)
        {
            Debits.Add(amount);
            if (amount > Balance)
                return Chapter3WalletDebit.Short;
            Balance -= amount;
            return Chapter3WalletDebit.Debited;
        }
    }

    private sealed class FakeAcquisition : IChapter3WarehouseAcquisition
    {
        public bool Owned { get; set; }
        public bool CanAcquireValue { get; set; } = true;
        public bool AcquireResult { get; set; } = true;
        public List<string> Calls { get; } = new();

        public bool IsOwned() => Owned;
        public bool CanAcquire() => CanAcquireValue;

        public bool TryAcquire()
        {
            Calls.Add("acquire");
            if (AcquireResult)
                Owned = true;
            return AcquireResult;
        }
    }
}
