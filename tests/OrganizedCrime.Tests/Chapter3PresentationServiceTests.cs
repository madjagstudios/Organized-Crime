using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3PresentationServiceTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void TryAcceptOffer_applies_and_raises_effects()
    {
        var service = StartedService(wallet: new FakeWallet());
        var before = service.State!.Revision;

        var result = service.TryAcceptOffer();

        Assert.True(result.Applied);
        Assert.NotNull(service.State!.JobBoard.ActiveJob);
        Assert.Equal(service.State!.JobBoard.ActiveJob!.JobId, result.JobId);
        Assert.Equal(before + 1, service.State!.Revision);
    }

    [Fact]
    public void TryAcceptOffer_without_an_offer_is_stale()
    {
        var service = StartedService(wallet: new FakeWallet());
        Assert.True(service.TryAcceptOffer().Applied);

        var again = service.TryAcceptOffer();

        Assert.False(again.Applied);
        Assert.Null(again.JobId);
    }

    [Fact]
    public void TryDeclineOffer_records_the_decline()
    {
        var service = StartedService(wallet: new FakeWallet());

        var result = service.TryDeclineOffer();

        Assert.True(result.Applied);
        Assert.Equal(1, result.OfferId);
        Assert.Equal(1, service.State!.JobBoard.LastDeclinedOfferId);
    }

    [Fact]
    public void TryPayMakeGood_debits_then_records()
    {
        var wallet = new FakeWallet { Balance = 100_000 };
        var service = StartedService(wallet);
        service.TryAcceptOffer();
        Assert.True(service.QaFailActiveJob().Ok);
        var entry = Assert.Single(service.State!.Ledger.Entries);

        var result = service.TryPayMakeGood(entry.Id);

        Assert.Equal(Chapter3MoneyOutcome.Applied, result.Outcome);
        Assert.Equal(entry.Amount, result.Amount);
        Assert.Equal(entry.Amount, 100_000 - wallet.Balance);
        Assert.Equal(Chapter3MakeGoodStatus.Paid, Assert.Single(service.State!.Ledger.Entries).Status);
    }

    [Fact]
    public void TryPayMakeGood_short_records_a_shortfall_and_keeps_the_debt()
    {
        var wallet = new FakeWallet { Balance = 10 };
        var service = StartedService(wallet);
        service.TryAcceptOffer();
        Assert.True(service.QaFailActiveJob().Ok);
        var entry = Assert.Single(service.State!.Ledger.Entries);

        var result = service.TryPayMakeGood(entry.Id);

        Assert.Equal(Chapter3MoneyOutcome.Short, result.Outcome);
        Assert.Equal(10, wallet.Balance);
        Assert.Equal(1, service.State!.PaymentShortfallCount);
        Assert.Equal(Chapter3MakeGoodStatus.Open, Assert.Single(service.State!.Ledger.Entries).Status);
    }

    [Fact]
    public void TryPayMakeGood_stale_when_missing_or_paid()
    {
        var wallet = new FakeWallet { Balance = 100_000 };
        var service = StartedService(wallet);

        Assert.Equal(Chapter3MoneyOutcome.NotPayable, service.TryPayMakeGood(7).Outcome);

        service.TryAcceptOffer();
        Assert.True(service.QaFailActiveJob().Ok);
        var entry = Assert.Single(service.State!.Ledger.Entries);
        Assert.Equal(Chapter3MoneyOutcome.Applied, service.TryPayMakeGood(entry.Id).Outcome);
        Assert.Equal(Chapter3MoneyOutcome.NotPayable, service.TryPayMakeGood(entry.Id).Outcome);
        Assert.Equal(100_000 - entry.Amount, wallet.Balance);
    }

    [Fact]
    public void TryPayMakeGood_unavailable_wallet_records_nothing()
    {
        var service = StartedService(wallet: null);
        service.TryAcceptOffer();
        Assert.True(service.QaFailActiveJob().Ok);
        var entry = Assert.Single(service.State!.Ledger.Entries);

        var result = service.TryPayMakeGood(entry.Id);

        Assert.Equal(Chapter3MoneyOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, service.State!.PaymentShortfallCount);
        Assert.Equal(Chapter3MakeGoodStatus.Open, Assert.Single(service.State!.Ledger.Entries).Status);
    }

    [Fact]
    public void TryPayCut_unknown_week_refuses_without_debit()
    {
        var wallet = new FakeWallet { Balance = 500_000 };
        var service = StartedService(wallet);
        Assert.Equal(250_000, Chapter3PaymentTuning.MaximumPayment);

        var result = service.TryPayCut(weekIndex: 99);

        Assert.Equal(Chapter3MoneyOutcome.NotPayable, result.Outcome);
        Assert.Equal(500_000, wallet.Balance);
    }

    [Fact]
    public void TryPayCut_debits_then_records()
    {
        var wallet = new FakeWallet { Balance = 100_000 };
        var service = StartedService(wallet, new FakeOwnership { Owned = true });
        Assert.True(service.QaSimulateWeekPass().Ok);

        var result = service.TryPayCut(weekIndex: 1);

        Assert.Equal(Chapter3MoneyOutcome.Applied, result.Outcome);
        Assert.Equal(30_000, result.Amount);
        Assert.Equal(70_000, wallet.Balance);
        Assert.Equal(Chapter3WeeklyCutStatus.Paid, Assert.Single(service.State!.Cuts.Entries).Status);
    }

    [Fact]
    public void TryBuyTime_debits_the_extension_cost()
    {
        var wallet = new FakeWallet { Balance = 100_000 };
        var service = ExtensionEligibleService(wallet);
        var job = service.State!.JobBoard.ActiveJob!;
        var cost = Chapter3JobScheduler.ExtensionCost(job.Reward);

        var result = service.TryBuyTime();

        Assert.Equal(Chapter3MoneyOutcome.Applied, result.Outcome);
        Assert.Equal(cost, result.Amount);
        Assert.Equal(100_000 - cost, wallet.Balance);
        Assert.True(service.State!.JobBoard.ActiveJob!.ExtensionUsed);
    }

    [Fact]
    public void TryBuyTime_ineligible_without_debit()
    {
        var wallet = new FakeWallet { Balance = 100_000 };
        var service = StartedService(wallet);

        var result = service.TryBuyTime();

        Assert.Equal(Chapter3MoneyOutcome.NotPayable, result.Outcome);
        Assert.Equal(100_000, wallet.Balance);
        Assert.Empty(wallet.Debits);
    }

    [Fact]
    public void TryRecordPresentationReceipt_records_and_bumps_once()
    {
        var service = StartedService(wallet: new FakeWallet());
        var before = service.State!.Revision;

        Assert.True(service.TryRecordPresentationReceipt("oc11-ch3/v1/76561190000000001/offer-1"));
        Assert.Equal(new[] { "oc11-ch3/v1/76561190000000001/offer-1" },
            service.State!.PresentationReceipts.Select(r => r.CorrelationId).ToArray());
        Assert.Equal(before + 1, service.State!.Revision);

        Assert.True(service.TryRecordPresentationReceipt("oc11-ch3/v1/76561190000000001/offer-1"));
        Assert.Equal(before + 1, service.State!.Revision);
    }

    [Fact]
    public void TryRecordPresentationReceipt_rejects_foreign_and_malformed_ids()
    {
        var service = StartedService(wallet: new FakeWallet());

        Assert.False(service.TryRecordPresentationReceipt("oc11-ch3/v1/76561190000000002/offer-1"));
        Assert.False(service.TryRecordPresentationReceipt("not-a-correlation"));
        Assert.False(service.TryRecordPresentationReceipt(""));
        Assert.Empty(service.State!.PresentationReceipts);
    }

    [Fact]
    public void GetPresentationPlan_shows_pending_messages_and_buttons()
    {
        var service = StartedService(wallet: new FakeWallet());

        var plan = service.GetPresentationPlan(release1HasNellDecision: false);

        Assert.NotNull(plan.Decision);
        Assert.Equal(new[] { "Accept", "Decline" }, plan.Decision!.Options.Select(o => o.Label).ToArray());
    }

    [Fact]
    public void GetPresentationPlan_threads_the_protection_snapshot_into_the_quest()
    {
        var service = StartedService(wallet: new FakeWallet());
        service.ProtectionSnapshot = () => new Release1ChiefProtectionSnapshot(Offered: true, ProtectedUntilDay: 19, CoveredNow: true);

        var plan = service.GetPresentationPlan(release1HasNellDecision: false);

        Assert.NotNull(plan.Quest);
        var chapterDay = service.State!.ChapterDayAt(19);
        Assert.NotNull(chapterDay);
        Assert.Contains(plan.Quest!.Entries, entry => entry.Text == $"Campbell's protection: until day {chapterDay}");
    }

    [Fact]
    public void Protection_qa_without_a_wired_chief_refuses()
    {
        var service = StartedService(wallet: new FakeWallet());

        Assert.False(service.QaProtectionOffer().Ok);
        Assert.False(service.QaProtectionLapse().Ok);
        Assert.Null(service.QaProtectionSnapshot());
    }

    [Fact]
    public void Protection_qa_delegates_to_the_wired_chief()
    {
        var service = StartedService(wallet: new FakeWallet());
        var chief = new FakeChiefProtectionQa();
        service.ChiefProtectionQa = chief;

        Assert.True(service.QaProtectionOffer().Ok);
        Assert.True(service.QaProtectionLapse().Ok);
        Assert.NotNull(service.QaProtectionSnapshot());
        Assert.Equal(new[] { "offer", "lapse", "snapshot" }, chief.Calls);
    }

    private sealed class FakeChiefProtectionQa : IRelease1ChiefProtectionQa
    {
        public List<string> Calls { get; } = new();

        public Chapter3QaResult QaProtectionOffer()
        {
            Calls.Add("offer");
            return new(true, "offered");
        }

        public Chapter3QaResult QaProtectionLapse()
        {
            Calls.Add("lapse");
            return new(true, "lapsed");
        }

        public Release1ChiefProtectionSnapshot QaProtectionSnapshot()
        {
            Calls.Add("snapshot");
            return new(false, null, false);
        }
    }

    [Fact]
    public void GetPresentationPlan_without_a_protection_snapshot_shows_no_protection_line()
    {
        var service = StartedService(wallet: new FakeWallet());

        var plan = service.GetPresentationPlan(release1HasNellDecision: false);

        Assert.DoesNotContain(
            plan.Quest?.Entries ?? Array.Empty<Release1DesiredEntry>(),
            entry => entry.Text.StartsWith("Campbell's protection", StringComparison.Ordinal));
    }


    private static Chapter3RuntimeService StartedService(FakeWallet? wallet, FakeOwnership? ownership = null)
    {
        var service = new Chapter3RuntimeService(
            new FakeContext(), new FakeRepository(), new FakeGate { Satisfied = true },
            new FakeClock { ElapsedDays = 11 }, null, ownership ?? new FakeOwnership { Owned = false });
        service.Wallet = wallet;
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        return service;
    }

    private static Chapter3RuntimeService ExtensionEligibleService(FakeWallet wallet)
    {
        var job = new Chapter3ActiveJob(7, 6, Chapter3JobType.DeadDrop, 20_000, 0, 2, false);
        var board = new Chapter3JobBoard(
            null, job, null, 1, Chapter3JobType.DeadDrop,
            new[] { Chapter3JobType.DeadDrop }, 0, false, false, 3, 0, 7, 8);
        var repository = new FakeRepository
        {
            Stored = new Chapter3State(PlayerId, Chapter3Phase.Active, 10, 11, 3, board)
        };
        var service = new Chapter3RuntimeService(
            new FakeContext(), repository, new FakeGate { Satisfied = true },
            new FakeClock { ElapsedDays = 11 });
        service.Wallet = wallet;
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        return service;
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), 1, PlayerId, System.IO.Path.GetTempPath());

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
}
