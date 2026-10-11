using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using OrganizedCrime.Runtime.Release1.World;
using Xunit;
using Harness = OrganizedCrime.Tests.Chapter3CashRuntimeTests.Harness;
using Locker = OrganizedCrime.Tests.Chapter3CashRuntimeTests.Locker;

namespace OrganizedCrime.Tests;

public sealed class Chapter3CashServiceTests
{
    [Fact]
    public void Console_offer_accept_every_save_reload_reprompt_and_paid_receipt()
    {
        var h = new Harness(); var repository = new Repository(); var clock = new Clock();
        Chapter3RuntimeService Create()
        {
            var service = new Chapter3RuntimeService(h.Host, repository, new Gate(), clock, h.Logs.Add)
            { DeadDropWorld = h.Wallet, Wallet = new Chapter3Wallet(h.Wallet) };
            service.WireCashWorld(h.Room);
            service.WireStorageRoom(() => null, () => new Release1WorldPoint(10, 20, 30));
            service.OnPreLoad(); service.OnLoadComplete(); return service;
        }
        var current = Create(); current.OnDayPassed();
        Assert.Contains("queued", Assert.Single(Chapter3QaCommand.Execute(current, new[] { "offer", "cash" }, true)));
        current.PollEmergencyExpiry();
        var decision = current.GetPresentationPlan(false).Decision!;
        Assert.StartsWith("Cash movement.", decision.Prompt);
        Assert.True(current.TryRecordPresentationReceipt(decision.PromptCorrelationId!));
        current.OnSaveStart(); current.OnSaveComplete(); current.OnSaveStart(); current.OnSaveComplete();
        Assert.Equal(2, repository.Writes); Assert.Contains("\"schemaVersion\": 20", repository.Json!);
        current.Dispose(); current = Create();
        Assert.Equal(decision.Id, current.GetPresentationPlan(false).Decision!.Id);
        Assert.Equal(decision.Prompt, current.GetPresentationPlan(false).Decision!.Prompt);
        Assert.False(string.IsNullOrEmpty(current.GetPresentationPlan(false).Decision!.RepromptText));
        Assert.Contains("accepted", Assert.Single(Chapter3QaCommand.Execute(current, new[] { "accept" }, true)));
        Assert.Equal(new Release1WorldPoint(10, 20, 30), current.GetPresentationPlan(false).Quest!.Entries[0].Marker);
        var stacks = current.State!.JobBoard.ActiveJob!.CashMovement!.Stacks;
        h.Deposit(stacks - 1); current.PollEmergencyExpiry();
        current.OnSaveStart(); current.OnSaveComplete(); current.Dispose(); current = Create();
        Assert.Equal("waiting", current.State!.JobBoard.ActiveJob!.CashMovement!.Plan is null ? "waiting" : "consuming");
        h.Deposit(stacks); current.PollEmergencyExpiry();
        Assert.Null(current.State.JobBoard.ActiveJob); Assert.Single(h.Wallet.CashChanges);
        Assert.Contains(Chapter3QaCommand.Execute(current, new[] { "status" }, true), s => s.Contains("plan state paid"));
        current.OnSaveStart(); current.OnSaveComplete(); current.Dispose(); current = Create(); current.PollEmergencyExpiry();
        Assert.True(current.State!.JobBoard.LastCompletedJob!.CashMovement!.Paid); Assert.Single(h.Wallet.CashChanges); current.Dispose();
    }

    [Fact]
    public void Cash_readiness_never_prevents_other_day_tick_work()
    {
        var h = new Harness(); h.Accept(); var repository = new Repository { Json = h.Serialize() }; var clock = new Clock { Day = 14 };
        var service = new Chapter3RuntimeService(h.Host, repository, new Gate(), clock)
        { DeadDropWorld = h.Wallet, Wallet = new Chapter3Wallet(h.Wallet) };
        service.WireCashWorld(h.Room); service.OnPreLoad(); service.OnLoadComplete(); h.Room.Readable = false;
        service.OnDayPassed();
        Assert.Equal(14, service.State!.LastProcessedElapsedDay);
        Assert.Null(service.State.JobBoard.ActiveJob);
        Assert.Equal(16000, Assert.Single(service.State.Ledger.Entries).Amount); service.Dispose();
    }

    [Fact]
    public void Console_expiry_preserves_cash_and_client_commands_do_nothing()
    {
        var h = new Harness(); h.Accept(); h.Deposit(2);
        var repository = new Repository { Json = h.Serialize() };
        var service = new Chapter3RuntimeService(h.Host, repository, new Gate(), new Clock()) { DeadDropWorld = h.Wallet };
        service.WireCashWorld(h.Room); service.OnPreLoad(); service.OnLoadComplete();
        h.Host.Ready = false;
        Assert.Contains("host", Assert.Single(Chapter3QaCommand.Execute(service, new[] { "cash", "expire" }, true)));
        Assert.NotNull(service.State!.JobBoard.ActiveJob);
        h.Host.Ready = true;
        Assert.Contains("queued", Assert.Single(Chapter3QaCommand.Execute(service, new[] { "cash", "expire" }, true)));
        service.PollEmergencyExpiry(); Assert.Null(service.State.JobBoard.ActiveJob);
        Assert.Equal(2, h.Room.Cash.Count); Assert.Equal(16000, Assert.Single(service.State.Ledger.Entries).Amount); service.Dispose();
    }

    [Fact]
    public void Open_demand_and_cash_share_J_and_only_take_their_own_items()
    {
        var h = new Harness(); h.Accept(); h.Deposit(5); h.Room.ProductSlots.Add(7); h.Room.ProductSlots.Add(8);
        var demandWorld = new DemandWorld(h.Room);
        h.Ctx.DemandWorld = demandWorld;
        h.Ctx.State = h.Ctx.State! with { ActiveDemand = new("demand-1", "rich", "Rich", Chapter3DemandProductType.Weed,
            250, 10, 0, 3, 250, 0, 0, 0, 0, 1, 2, 3, null) };
        var demand = new Chapter3DemandFeature(h.Ctx);
        demand.PollDemandLocked();
        Assert.Equal(5, h.Room.Cash.Count); Assert.Equal(100, h.Ctx.State.ActiveDemand!.Delivered);
        h.Poll();
        Assert.Empty(h.Room.Cash); Assert.Contains(8, h.Room.ProductSlots); Assert.Equal(100, h.Ctx.State.ActiveDemand!.Delivered);
        demand.PollDemandLocked();
        Assert.Null(h.Ctx.State.ActiveDemand); Assert.Equal(new[] { 500f, 21000f, 750f }, h.Wallet.CashChanges);
        Assert.Equal(new[] { 7, 8 }, demandWorld.Changes); Assert.Empty(h.Ctx.State.Ledger.Entries);
    }

    [Fact]
    public void Native_adapter_keeps_zero_cash_read_back_available()
    {
        var h = new Harness(); var storage = new Storage(h.Room);
        h.Ctx.CashWorld = new Chapter3CashStorageWorld(storage); h.Accept(); h.Deposit(5); h.Room.KeepZeroCash = true;
        h.Poll(); Assert.Null(h.Ctx.State!.JobBoard.ActiveJob); Assert.Single(h.Wallet.CashChanges);
        Assert.Equal(5, h.Room.Changes.Count); Assert.All(h.Room.Cash.Values, v => Assert.Equal(0, v));
    }

    private sealed class DemandWorld : IChapter3ProductionDemandWorld
    {
        private readonly Locker _room;
        internal readonly List<int> Changes = new();
        internal DemandWorld(Locker room) => _room = room;
        public Release1SmallCourtesyWorldReadStatus TryReadWarehouseStations(out IReadOnlyList<Chapter3DemandStationSnapshot> stations)
        { stations = Array.Empty<Chapter3DemandStationSnapshot>(); return Release1SmallCourtesyWorldReadStatus.Ready; }
        public Release1SmallCourtesyWorldReadStatus TryReadProductInfo(string id, out Chapter3DemandWorldProductInfo? info)
        { info = null; return Release1SmallCourtesyWorldReadStatus.Unavailable; }
        public Release1SmallCourtesyWorldReadStatus TryReadPackagingUnits(string id, out int units)
        { units = 1; return Release1SmallCourtesyWorldReadStatus.Ready; }
        public Release1SmallCourtesyWorldReadStatus TryReadFamilyLockerSlots(out IReadOnlyList<Chapter3DemandLockerSlot> slots)
        {
            slots = Enumerable.Range(0, _room.SlotCount).Select(i => new Chapter3DemandLockerSlot(i,
                _room.Cash.ContainsKey(i) ? "cash" : _room.ProductSlots.Contains(i) ? "rich" : "",
                _room.Cash.ContainsKey(i) ? 1 : _room.ProductSlots.Contains(i) ? i == 7 ? 100 : 150 : 0, false, "", 1)).ToArray();
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }
        public Release1SmallCourtesyWorldMutationStatus TrySetFamilyLockerSlotLocked(int slot, bool locked) =>
            _room.TrySetHoldRoomSlotLocked(Chapter3CashMovement.FamilyLockerGuid, slot, locked);
        public Release1SmallCourtesyWorldMutationStatus TryChangeFamilyLockerSlotQuantity(int slot, int amount)
        {
            Assert.Contains(slot, _room.ProductSlots); Assert.Equal(slot == 7 ? -100 : -150, amount);
            Changes.Add(slot); _room.ProductSlots.Remove(slot); return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }
        public Release1SmallCourtesyWorldMutationStatus TryEnsureFamilyLockerName() => Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public bool TryReadHqDoor(out float x, out float y, out float z) { x = y = z = 1; return true; }
    }

    private sealed class Storage : ISyndicateHqStorageRuntime
    {
        private readonly Locker _room;
        internal Storage(Locker room) => _room = room;
        public SyndicateHqStorageResult CurrentResult => throw new NotSupportedException();
        public SyndicateHqStorageResult BeginLoad(SyndicateHqStorageContext context) => throw new NotSupportedException();
        public SyndicateHqStorageResult PrepareAtVanillaLoadBoundary() => throw new NotSupportedException();
        public SyndicateHqStorageResult CompleteVanillaLoadBoundary() => throw new NotSupportedException();
        public SyndicateHqStorageResult PlaceAtPocket(SyndicateHqVector3 point) => throw new NotSupportedException();
        public Release1HoldRoomSnapshot ReadHoldRoom() { _room.TryReadHoldRoom(out var room); return room; }
        public Release1SmallCourtesyWorldReadStatus TryReadClosetSlotCashBalance(string id, int slot, out float balance) => _room.TryReadHoldRoomSlotCashBalance(id, slot, out balance);
        public Release1SmallCourtesyWorldMutationStatus TrySetClosetSlotLocked(string id, int slot, bool locked) => _room.TrySetHoldRoomSlotLocked(id, slot, locked);
        public Release1SmallCourtesyWorldMutationStatus TryChangeClosetSlotCashBalance(string id, int slot, float amount) => _room.TryChangeHoldRoomSlotCashBalance(id, slot, amount);
        public void DisableInteractions() { }
        public void MarkTeardown() { }
        public void Dispose() { }
    }
    private sealed class Gate : IChapter3StoryGate { public bool IsEnvelopeSatisfied() => true; }
    private sealed class Clock : IChapter3DayClock
    {
        internal int Day = 11;
        public event Action? DayPassed { add { } remove { } }
        public event Action? WeekPassed { add { } remove { } }
        public bool TryReadElapsedDays(out int day) { day = Day; return true; }
    }
    private sealed class Repository : IChapter3Repository, IChapter3SaveFolderBoundRepository
    {
        public string BoundSaveFolder => System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        internal string? Json;
        internal int Writes;
        public Chapter3StoreLoadResult Load()
        {
            if (Json is null) return new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "");
            Assert.True(Chapter3SaveCodec.TryDeserialize(Json, out var loaded, out var read), read.Message);
            return new(true, Chapter3StoreLoadStatus.Loaded, loaded, Chapter3StoreFailureReason.None, "");
        }
        public Chapter3StoreUpdateResult Update(Chapter3State? state)
        {
            Writes++; Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var write), write.Message); Json = json;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "");
        }
    }
}
