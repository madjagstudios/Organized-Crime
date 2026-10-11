using OrganizedCrime.Core.Diagnostics;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using OrganizedCrime.Runtime.Release1.World;
using Xunit;
using WalletWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

public sealed class Chapter3CashRuntimeTests
{
    [Theory]
    [InlineData(15000, 5000)]
    [InlineData(17000, 5000)]
    [InlineData(17500, 10000)]
    [InlineData(20000, 10000)]
    public void Quoted_tiers_and_exact_copy_survive_reload(int reward, int advance)
    {
        var h = new Harness(reward); h.Freeze();
        var quote = h.Ctx.State!.JobBoard.OpenOffer!;
        Assert.Equal(advance, quote.CashMovement!.Advance);
        var plan = Chapter3PresentationPlanner.Build(h.Ctx.State, false, false);
        Assert.Equal($"Cash movement. Front ${advance:N0} into the family locker at HQ. You get it back plus ${reward:N0}. Two days. Physical cash, don't fucking text me a balance.", plan.Decision!.Prompt);
        h.Reload(); Assert.Equal(quote, h.Ctx.State!.JobBoard.OpenOffer);
        h.Accept(); Assert.Equal(2, h.Job.DeadlineDay);
        Assert.Contains(Chapter3PresentationPlanner.Build(h.Ctx.State!, false, false).Messages,
            m => m.Text == "Whole amount in the family locker, a thousand to a stack. Don't spread it around.");
        Assert.NotNull(Chapter3PresentationPlanner.Build(h.Ctx.State!, false, false).Quest);
        Assert.Throws<ArgumentException>(() => h.Run.Validate(reward + 1, false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Offer_and_accept_require_no_cash_and_enough_free_slots(bool cash)
    {
        var h = new Harness();
        if (cash) h.Room.Cash[0] = 1000; else h.Room.SlotCount = 4;
        Assert.False(h.Cash.TryFreezeOffer(h.Offer, out _));
        h.Room.Cash.Clear(); h.Room.SlotCount = 12; h.Freeze();
        if (cash) h.Room.Cash[0] = 500; else h.Room.SlotCount = 4;
        Assert.False(h.Offers.AcceptOfferLocked(h.Offer, 0).Applied);
        Assert.Equal(h.Offer.OfferId, h.Ctx.State!.JobBoard.LastRefusedOfferId);
        Assert.Null(h.Ctx.State.JobBoard.ActiveJob); Assert.Empty(h.Room.Changes);
    }

    [Fact]
    public void Partial_deposit_and_late_full_deposit_are_untouched()
    {
        var h = new Harness(); h.Accept(); h.Deposit(4); h.Poll();
        Assert.Null(h.Run.Plan); Assert.Empty(h.Room.Changes);
        h.Deposit(5); h.Poll(3);
        Assert.Empty(h.Room.Changes); Assert.Empty(h.Wallet.CashChanges);
        var failed = Chapter3JobScheduler.OnDayTick(h.Ctx.State!.JobBoard, "player", 3);
        Assert.Equal(16000, Assert.Single(failed.Effects.OfType<Chapter3MakeGoodRequiredEffect>()).ReferenceAmount);
    }

    [Theory]
    [InlineData(16000, 5)]
    [InlineData(20000, 10)]
    public void Exact_ordered_drain_reimburses_plus_fee_once_preserving_surplus_and_product(int reward, int stacks)
    {
        var h = new Harness(reward); h.Accept();
        h.Room.ProductSlots.Add(1); h.Room.ProductSlots.Add(11);
        foreach (var slot in Enumerable.Range(0, 12).Where(i => !h.Room.ProductSlots.Contains(i)).Take(stacks)) h.Room.Cash[slot] = 1000;
        h.Room.SlotCount = 14; h.Room.Cash[13] = 1000;
        h.Poll();
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob);
        Assert.Equal(stacks, h.Room.Changes.Count);
        Assert.Equal(h.Room.Changes.OrderBy(i => i), h.Room.Changes);
        Assert.Equal(1000, h.Room.Cash[13]); Assert.Equal(2, h.Room.ProductSlots.Count);
        Assert.Equal(new[] { (float)(stacks * 1000 + reward) }, h.Wallet.CashChanges);
        var receipt = h.Ctx.State.JobBoard.LastCompletedJob!.CashMovement!;
        Assert.True(receipt.Paid); Assert.Equal(stacks, receipt.ConfirmedSlots);
        h.Reload(); h.Poll(); Assert.Single(h.Wallet.CashChanges); Assert.Equal(stacks, h.Room.Changes.Count);
        Assert.Contains(Chapter3PresentationPlanner.Build(h.Ctx.State!, false, false).Messages,
            m => m.Text == $"Received. Your ${stacks * 1000:N0} is back, and ${reward:N0}'s yours.");
    }

    [Fact]
    public void Non_full_stacks_never_start_consumption()
    {
        var h = new Harness(); h.Accept(); h.Deposit(6); h.Room.Cash[0] = 500;
        h.Poll(); Assert.Empty(h.Room.Changes); Assert.Null(h.Run.Plan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reload_inside_decrement_adopts_landed_effect_or_retries_unchanged_effect(bool landed)
    {
        var h = new Harness(); h.Accept(); h.Deposit(5);
        string? snapshot = null;
        h.Room.DuringChange = () => snapshot ??= h.Serialize();
        h.Poll(); Assert.NotNull(snapshot);
        Assert.True(Chapter3SaveCodec.TryDeserialize(snapshot, out var loaded, out var read), read.Message);
        h.Ctx.State = loaded!.State;
        // Restore the native snapshot at the chosen save boundary.
        h.Room.Cash.Clear(); h.Deposit(5); if (landed) h.Room.Cash.Remove(0);
        h.Wallet.CashBalance = 0; h.Wallet.CashChanges.Clear(); h.Room.Changes.Clear(); h.Room.DuringChange = null;
        h.Poll();
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob);
        Assert.Equal(landed ? 4 : 5, h.Room.Changes.Count); Assert.Single(h.Wallet.CashChanges);
    }

    [Fact]
    public void Reload_inside_credit_adopts_payment_without_re_draining_or_crediting()
    {
        var h = new Harness(); h.Accept(); h.Deposit(5); string? snapshot = null;
        h.Ctx.Wallet = new CaptureWallet(new Chapter3Wallet(h.Wallet), () => snapshot = h.Serialize());
        h.Poll();
        Assert.True(Chapter3SaveCodec.TryDeserialize(snapshot, out var loaded, out var read), read.Message);
        h.Ctx.State = loaded!.State; h.Poll();
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob); Assert.Equal(5, h.Room.Changes.Count); Assert.Single(h.Wallet.CashChanges);
    }

    [Fact]
    public void Partial_ambiguous_consumption_holds_without_redrain_or_make_good_even_after_reload()
    {
        var h = new Harness(); h.Accept(); h.Deposit(5); h.Room.BadSlot = 1;
        h.Poll(); Assert.True(h.Run.Ambiguous); Assert.Equal(1, h.Run.ConfirmedSlots);
        h.Reload(); h.Poll(4); h.Cash.Fail(4); h.Poll(5);
        Assert.Equal(new[] { 0, 1 }, h.Room.Changes); Assert.Empty(h.Ctx.State!.Ledger.Entries);
        Assert.NotNull(Chapter3JobScheduler.OnDayTick(h.Ctx.State.JobBoard, "player", 5).Board.ActiveJob);
        Assert.Empty(h.Wallet.CashChanges);
        // Even restoring the pre-value does not authorize another drain.
        h.Room.Cash[1] = 1000; h.Poll(); Assert.Equal(2, h.Room.Changes.Count);
    }

    [Fact]
    public void Failure_owes_only_fee_due_in_two_days_and_preserves_partial_cash()
    {
        var h = new Harness(20000); h.Accept(); h.Deposit(3); h.Cash.Fail(1);
        Assert.Equal(3, h.Room.Cash.Count); Assert.Empty(h.Room.Changes);
        var debt = Assert.Single(h.Ctx.State!.Ledger.Entries); Assert.Equal(20000, debt.Amount); Assert.Equal(3, debt.DueDay);
    }

    [Fact]
    public void Unreadable_and_client_are_no_ops_and_status_never_reports_zero_for_unreadable()
    {
        var h = new Harness(); h.Accept(); h.Deposit(5); h.Room.Readable = false;
        h.Poll(); Assert.Empty(h.Room.Changes); Assert.Contains(h.Cash.Status(), s => s.Contains("unreadable"));
        h.Room.Readable = true; h.Host.Ready = false; h.Poll(); h.Cash.Fail(4);
        Assert.False(h.Cash.Queue("offer").Ok); Assert.Empty(h.Room.Changes); Assert.Empty(h.Wallet.CashChanges);
        Assert.NotNull(h.Ctx.State!.JobBoard.ActiveJob);
    }

    [Fact]
    public void Readable_replacement_with_product_marks_ambiguous_without_touching_product()
    {
        var h = new Harness(); h.Accept(); h.Deposit(5);
        h.Room.DuringChange = () => { h.Room.Cash.Remove(0); h.Room.ProductSlots.Add(0); };
        h.Poll(); Assert.True(h.Run.Ambiguous); Assert.Contains(0, h.Room.ProductSlots);
        h.Reload(); h.Poll(4); Assert.Single(h.Room.Changes); Assert.Empty(h.Wallet.CashChanges);
    }

    [Fact]
    public void Unreadable_post_read_resumes_from_receipt_without_repeating_decrement()
    {
        var h = new Harness(); h.Accept(); h.Deposit(5); h.Room.AfterChange = () => h.Room.Readable = false;
        h.Poll(); Assert.True(h.Run.PendingDecrement); Assert.Single(h.Room.Changes);
        h.Reload(); h.Room.AfterChange = null; h.Room.Readable = true; h.Poll(5);
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob); Assert.Equal(5, h.Room.Changes.Count); Assert.Single(h.Wallet.CashChanges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Busy_or_ambiguous_first_lock_never_starts_a_transaction(bool ambiguous)
    {
        var h = new Harness(); h.Accept(); h.Deposit(5); h.Room.LockStatus = ambiguous
            ? Release1SmallCourtesyWorldMutationStatus.Ambiguous : Release1SmallCourtesyWorldMutationStatus.Rejected;
        h.Poll(); Assert.Null(h.Run.Plan); Assert.Empty(h.Room.Changes); Assert.Equal(0, h.Room.OwnedLocks);
        h.Cash.Fail(3); Assert.Single(h.Ctx.State!.Ledger.Entries);
    }

    [Fact]
    public void Failed_credit_retries_without_another_drain_and_diverged_credit_holds()
    {
        var h = new Harness(); h.Accept(); h.Deposit(5);
        h.Wallet.CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Unavailable;
        h.Poll(); Assert.Equal(0d, h.Run.CreditBefore); Assert.Equal(5, h.Room.Changes.Count);
        h.Reload(); h.Wallet.CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        h.Poll(); Assert.Null(h.Ctx.State!.JobBoard.ActiveJob); Assert.Equal(21000f, h.Wallet.CashBalance);
        Assert.Equal(5, h.Room.Changes.Count);
        var divergent = new Harness(); divergent.Accept(); divergent.Deposit(5);
        divergent.Wallet.CashChangeStatus = Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        divergent.Wallet.ApplyCashChangeOnAmbiguous = true;
        divergent.Ctx.Wallet = new PartialCreditWallet(divergent.Wallet);
        divergent.Poll(); Assert.True(divergent.Run.Ambiguous);
        divergent.Reload(); divergent.Poll(5); divergent.Cash.Fail(5);
        Assert.Single(divergent.Wallet.CashChanges); Assert.Equal(5, divergent.Room.Changes.Count);
        Assert.Empty(divergent.Ctx.State!.Ledger.Entries);
    }

    [Fact]
    public void Eligible_later_offer_promotes_without_refusal_and_unavailable_offer_keeps_sibling()
    {
        var h = new Harness();
        var before = Chapter3JobBoard.Empty with { NextOfferId = 7, LastTypeOffered = Chapter3JobType.PackageRecovery };
        var fresh = before with { OpenOffer = new(7, Chapter3JobType.DeadDrop, 17500, 0, 1) };
        var promoted = h.Offers.FreezeFreshCashOfferLocked(before, fresh);
        Assert.Equal(10000, promoted.OpenOffer!.CashMovement!.Advance); Assert.Equal(0, promoted.RefusalStreak);
        h.Room.Cash[0] = 1000; Assert.Same(fresh, h.Offers.FreezeFreshCashOfferLocked(before, fresh));
    }

    private sealed class PartialCreditWallet : IChapter3Wallet
    {
        private readonly WalletWorld _world;
        internal PartialCreditWallet(WalletWorld world) => _world = world;
        public Chapter3WalletDebit TryDebit(int amount) => Chapter3WalletDebit.Unavailable;
        public Chapter3WalletCredit TryCredit(int amount) { _world.TryChangeCashBalance(amount / 2f); return Chapter3WalletCredit.Unavailable; }
    }

    [Fact]
    public void Cash_classification_uses_guarded_cash_reads_rather_than_assuming_definition_id()
    {
        var h = new Harness(); h.Room.CashId = "banknotes"; h.Room.Cash[0] = 1000;
        Assert.False(h.Cash.TryFreezeOffer(h.Offer, out _));
        h.Room.Cash.Clear(); h.Accept(); h.Deposit(5); h.Poll();
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob); Assert.Single(h.Wallet.CashChanges);
    }

    [Fact]
    public void Client_accept_is_a_complete_state_no_op()
    {
        var h = new Harness(); h.Freeze(); var before = h.Ctx.State; h.Host.Ready = false;
        Assert.False(h.Offers.AcceptOfferLocked(h.Offer, 0).Applied);
        Assert.Equal(before, h.Ctx.State);
    }

    [Theory]
    [InlineData(Release1SmallCourtesyWorldReadStatus.Unavailable)]
    [InlineData(Release1SmallCourtesyWorldReadStatus.Faulted)]
    public void Unreadable_individual_cash_slot_is_never_reported_as_zero(Release1SmallCourtesyWorldReadStatus status)
    {
        var h = new Harness(); h.Accept(); h.Deposit(5); h.Room.CashReadStatus = status;
        h.Poll(); Assert.Null(h.Run.Plan); Assert.Empty(h.Room.Changes);
        Assert.Contains(h.Cash.Status(), s => s.Contains("observed unreadable"));
    }

    [Fact]
    public void Savings_in_other_closets_are_ignored()
    {
        var h = new Harness(); h.Room.OtherClosets = new[] { new Release1HoldRoomClosetSnapshot("savings", 1,
            new[] { new Release1SmallCourtesySlotSnapshot(0, "cash", "unpackaged", 1, false, 1000) }) };
        h.Accept(); h.Deposit(5); h.Poll();
        Assert.Null(h.Ctx.State!.JobBoard.ActiveJob); Assert.Single(h.Room.OtherClosets[0].Slots);
        Assert.Equal(1, h.Room.OtherClosets[0].Slots[0].Quantity);
    }

    internal sealed class Harness
    {
        internal readonly Locker Room = new();
        internal readonly WalletWorld Wallet = new() { CashBalance = 0 };
        internal readonly HostContext Host = new();
        internal readonly List<string> Logs = new();
        internal readonly Chapter3FeatureContext Ctx;
        internal readonly Chapter3OffersFeature Offers;
        internal readonly Chapter3CashFeature Cash;
        internal Chapter3JobOffer Offer => Ctx.State!.JobBoard.OpenOffer!;
        internal Chapter3ActiveJob Job => Ctx.State!.JobBoard.ActiveJob!;
        internal Chapter3CashMovement Run => Job.CashMovement!;
        internal Harness(int reward = 16000)
        {
            Ctx = new Chapter3FeatureContext
            {
                Context = Host, Phase = Chapter3RuntimePhase.Active, Log = Logs.Add,
                State = Chapter3State.CreateInitial("player") with
                {
                    Phase = Chapter3Phase.Active, StartElapsedDay = 0, LastProcessedElapsedDay = 0,
                    JobBoard = Chapter3JobBoard.Empty with { OpenOffer = new(1, Chapter3JobType.CashMovement, reward, 0, 1) }
                },
                DeadDropWorld = Wallet, CashWorld = Room, Wallet = new Chapter3Wallet(Wallet),
                LoggedDeliveryHolds = new OnceLog(Logs.Add)
            };
            var delivery = new Chapter3DeliveryFeature(Ctx, _ => { });
            Cash = new Chapter3CashFeature(Ctx, delivery); Ctx.Cash = Cash;
            Offers = new Chapter3OffersFeature(Ctx, delivery);
        }
        internal void Freeze() { Assert.True(Cash.TryFreezeOffer(Offer, out var quote)); Ctx.State = Ctx.State! with { JobBoard = Ctx.State!.JobBoard with { OpenOffer = quote } }; }
        internal void Accept() { if (Offer.CashMovement is null) Freeze(); Assert.True(Offers.AcceptOfferLocked(Offer, 0).Applied); }
        internal void Deposit(int count) { for (var i = 0; i < count; i++) Room.Cash[i] = 1000; }
        internal void Poll(int day = 0) => Cash.Poll(day);
        internal string Serialize() { Assert.True(Chapter3SaveCodec.TrySerialize(Ctx.State, out var json, out var write), write.Message); return json; }
        internal void Reload() { Assert.True(Chapter3SaveCodec.TryDeserialize(Serialize(), out var loaded, out var read), read.Message); Ctx.State = loaded!.State; }
    }
    internal sealed class HostContext : IRelease1StoryHostContext
    {
        internal bool Ready = true;
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot context)
        { context = new(Guid.Parse("66666666-6666-6666-6666-666666666666"), 1, "player", System.IO.Path.GetTempPath()); return Ready ? Release1StoryHostContextReadStatus.Ready : Release1StoryHostContextReadStatus.NotAuthoritative; }
    }
    internal sealed class Locker : IHoldRoomWorld
    {
        internal readonly Dictionary<int, float> Cash = new();
        internal readonly HashSet<int> ProductSlots = new();
        internal readonly List<int> Changes = new();
        internal bool Readable = true;
        internal int SlotCount = 12;
        internal string CashId = "cash";
        internal IReadOnlyList<Release1HoldRoomClosetSnapshot> OtherClosets = Array.Empty<Release1HoldRoomClosetSnapshot>();
        internal Release1SmallCourtesyWorldReadStatus CashReadStatus = Release1SmallCourtesyWorldReadStatus.Ready;
        internal int? BadSlot;
        internal Action? DuringChange;
        internal Action? AfterChange;
        internal Release1SmallCourtesyWorldMutationStatus LockStatus = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        internal int OwnedLocks => _locks.Count;
        internal bool KeepZeroCash;
        private readonly HashSet<int> _locks = new();
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
        {
            room = Readable ? new(Release1HoldRoomReadiness.Ready, new[]
            {
                new Release1HoldRoomClosetSnapshot(Chapter3CashMovement.FamilyLockerGuid, SlotCount,
                    Enumerable.Range(0, SlotCount).Select(i => new Release1SmallCourtesySlotSnapshot(i,
                        Cash.ContainsKey(i) ? CashId : ProductSlots.Contains(i) ? "rich" : null,
                        ProductSlots.Contains(i) ? "brick" : Cash.ContainsKey(i) ? "unpackaged" : null,
                        Cash.ContainsKey(i) || ProductSlots.Contains(i) ? 1 : 0, ProductSlots.Contains(i), 0)).ToArray())
            }.Concat(OtherClosets).ToArray()) : Release1HoldRoomSnapshot.NotReady();
            return Readable ? Release1SmallCourtesyWorldReadStatus.Ready : Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string guid, int slot, out float balance)
        {
            Assert.Equal(Chapter3CashMovement.FamilyLockerGuid, guid); balance = 0;
            if (CashReadStatus != Release1SmallCourtesyWorldReadStatus.Ready) return CashReadStatus;
            return Readable && Cash.TryGetValue(slot, out balance) ? Release1SmallCourtesyWorldReadStatus.Ready : Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string guid, int slot, bool locked)
        {
            Assert.Equal(Chapter3CashMovement.FamilyLockerGuid, guid);
            if (!locked) { _locks.Remove(slot); return Release1SmallCourtesyWorldMutationStatus.Succeeded; }
            if (LockStatus != Release1SmallCourtesyWorldMutationStatus.Rejected) _locks.Add(slot);
            return LockStatus;
        }
        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string guid, int slot, float amount)
        {
            Assert.Equal(Chapter3CashMovement.FamilyLockerGuid, guid); Assert.Contains(slot, _locks); Assert.Equal(-1000, amount);
            Changes.Add(slot); DuringChange?.Invoke();
            if (!ProductSlots.Contains(slot))
            { if (BadSlot == slot) Cash[slot] = 500; else if (KeepZeroCash) Cash[slot] = 0; else Cash.Remove(slot); }
            AfterChange?.Invoke();
            return BadSlot == slot ? Release1SmallCourtesyWorldMutationStatus.Ambiguous : Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }
    }
    private sealed class CaptureWallet : IChapter3Wallet
    {
        private readonly IChapter3Wallet _inner; private readonly Action _capture;
        internal CaptureWallet(IChapter3Wallet inner, Action capture) { _inner = inner; _capture = capture; }
        public Chapter3WalletDebit TryDebit(int amount) => _inner.TryDebit(amount);
        public Chapter3WalletCredit TryCredit(int amount) { _capture(); return _inner.TryCredit(amount); }
    }
}
