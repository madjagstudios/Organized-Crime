using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1TheEnvelopeCashGateHarnessTests
{
    [Fact]
    public void Dump_reports_faulted_when_the_world_is_null()
    {
        var result = Release1TheEnvelopeCashGateHarness.TryDumpCashSlots(null!);

        Assert.Equal(Release1StagingHarnessStatus.Faulted, result.Status);
        Assert.Contains("the world was not available.", result.Lines);
    }

    [Fact]
    public void Dump_reports_unavailable_when_no_drop_has_an_occupied_slot()
    {
        var world = new FakeWorld();
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, true) };
        world.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, null, null, 0, false, 0f)
        };

        var result = Release1TheEnvelopeCashGateHarness.TryDumpCashSlots(world);

        Assert.Equal(Release1StagingHarnessStatus.Unavailable, result.Status);
        Assert.Contains("no dead drop held an occupied slot.", result.Lines);
    }

    [Fact]
    public void Dump_lists_every_occupied_slot_in_the_first_qualifying_drop_with_its_legacy_monetary_value_and_its_new_cash_balance_read()
    {
        var world = new FakeWorld();
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        world.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cash", "unpackaged", 1, false, 0f)
        };
        world.CashBalances[("drop-a", 0)] = 2500f;

        var result = Release1TheEnvelopeCashGateHarness.TryDumpCashSlots(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.Contains(result.Lines, line =>
            line.Contains("legacy monetaryValue 0", StringComparison.Ordinal) &&
            line.Contains("cash balance read Ready value 2500", StringComparison.Ordinal));
    }

    [Fact]
    public void Dump_never_calls_TryChangeDeadDropSlotCashBalance()
    {
        var world = new FakeWorld { ThrowOnCashChange = true };
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        world.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cash", "unpackaged", 1, false, 0f)
        };
        world.CashBalances[("drop-a", 0)] = 2500f;

        var result = Release1TheEnvelopeCashGateHarness.TryDumpCashSlots(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
    }

    [Fact]
    public void Decrement_reports_unavailable_when_no_slot_holds_at_least_one_hundred()
    {
        var world = new FakeWorld();
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        world.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cash", "unpackaged", 1, false, 0f)
        };
        world.CashBalances[("drop-a", 0)] = 50f;

        var result = Release1TheEnvelopeCashGateHarness.TryDecrementFirstCashSlot(world);

        Assert.Equal(Release1StagingHarnessStatus.Unavailable, result.Status);
        Assert.Contains("no dead drop slot held a cash balance of at least 100.", result.Lines);
        Assert.Empty(world.LockCalls);
        Assert.Empty(world.CashChangeCalls);
    }

    [Fact]
    public void Decrement_locks_the_first_qualifying_slot_decrements_by_one_hundred_reads_back_and_unlocks()
    {
        var world = new FakeWorld();
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        world.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cash", "unpackaged", 1, false, 0f)
        };
        world.CashBalances[("drop-a", 0)] = 2500f;

        var result = Release1TheEnvelopeCashGateHarness.TryDecrementFirstCashSlot(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.Single(world.LockCalls, call => call.Locked);
        Assert.Single(world.LockCalls, call => !call.Locked);
        var change = Assert.Single(world.CashChangeCalls);
        Assert.Equal(("drop-a", 0, -100f), change);
        Assert.Contains(result.Lines, line => line.Contains("pre balance 2500", StringComparison.Ordinal));
        Assert.Contains(result.Lines, line => line.Contains("post balance read Ready value 2400", StringComparison.Ordinal));
    }

    [Fact]
    public void Decrement_faults_when_the_read_back_balance_is_not_pre_minus_one_hundred()
    {
        var world = new FakeWorld();
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        world.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cash", "unpackaged", 1, false, 0f)
        };
        world.CashBalances[("drop-a", 0)] = 2500f;
        world.BalanceAfterDecrement = (_, _, preBalance) => preBalance - 50f;

        var result = Release1TheEnvelopeCashGateHarness.TryDecrementFirstCashSlot(world);

        Assert.Equal(Release1StagingHarnessStatus.Faulted, result.Status);
        Assert.Single(world.LockCalls, call => call.Locked);
        Assert.Single(world.LockCalls, call => !call.Locked);
    }

    [Fact]
    public void Decrement_releases_the_lock_when_the_change_throws()
    {
        var world = new FakeWorld { ThrowOnCashChange = true };
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        world.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cash", "unpackaged", 1, false, 0f)
        };
        world.CashBalances[("drop-a", 0)] = 2500f;

        var result = Release1TheEnvelopeCashGateHarness.TryDecrementFirstCashSlot(world);

        Assert.Equal(Release1StagingHarnessStatus.Faulted, result.Status);
        Assert.Contains(result.Lines, line => line.Contains("an exception was thrown while decrementing", StringComparison.Ordinal));
        Assert.Single(world.LockCalls, call => call.Locked);
        Assert.Single(world.LockCalls, call => !call.Locked);
    }

    [Fact]
    public void Decrement_never_calls_TryInsertPackagedProduct_or_TryChangeSlotQuantity()
    {
        var world = new FakeWorld();
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        world.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cash", "unpackaged", 1, false, 0f)
        };
        world.CashBalances[("drop-a", 0)] = 2500f;

        var result = Release1TheEnvelopeCashGateHarness.TryDecrementFirstCashSlot(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
    }

    private sealed class FakeWorld : IRelease1SmallCourtesyWorld
    {
        public IReadOnlyList<Release1SmallCourtesyDropCandidate> Drops { get; set; } =
            Array.Empty<Release1SmallCourtesyDropCandidate>();
        public Dictionary<string, List<Release1SmallCourtesySlotSnapshot>> SlotsByDrop { get; } = new();
        public Dictionary<(string, int), float> CashBalances { get; } = new();
        public bool ThrowOnCashChange { get; set; }
        public Func<string, int, float, float>? BalanceAfterDecrement { get; set; }
        public List<(string Guid, int SlotIndex, bool Locked)> LockCalls { get; } = new();
        public List<(string Guid, int SlotIndex, float Amount)> CashChangeCalls { get; } = new();

        public Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context)
        {
            context = default;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)
        {
            totalMinutes = 0d;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products)
        {
            products = Array.Empty<Release1SmallCourtesyProductCandidate>();
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops)
        {
            drops = Drops;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(
            Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging)
        {
            packaging = kind == Release1SmallCourtesyPackageKind.Brick ? new("brick", "Brick") : new("jar", "Jar");
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(
            string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
        {
            if (SlotsByDrop.TryGetValue(deadDropGuid, out var found))
            {
                slots = found.ToArray();
                return Release1SmallCourtesyWorldReadStatus.Ready;
            }
            slots = Array.Empty<Release1SmallCourtesySlotSnapshot>();
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)
        {
            room = Release1HoldRoomSnapshot.Unavailable();
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked)
        {
            LockCalls.Add((deadDropGuid, slotIndex, locked));
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount)
        {
            throw new InvalidOperationException("TryChangeSlotQuantity should never be called by this harness.");
        }

        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(
            string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
        {
            throw new InvalidOperationException("TryInsertPackagedProduct should never be called by this harness.");
        }

        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(
            string deadDropGuid, Action<string> callback, out IRelease1SmallCourtesyDropSubscription? subscription)
        {
            subscription = null;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Unavailable;

        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Unavailable;

        public Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)
        {
            reason = string.Empty;
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)
        {
            reason = string.Empty;
            return Release1SmallCourtesyWorldMutationStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance)
        {
            balance = CashBalances.TryGetValue((deadDropGuid, slotIndex), out var value) ? value : 0f;
            return CashBalances.ContainsKey((deadDropGuid, slotIndex))
                ? Release1SmallCourtesyWorldReadStatus.Ready
                : Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount)
        {
            CashChangeCalls.Add((deadDropGuid, slotIndex, amount));
            if (ThrowOnCashChange) throw new InvalidOperationException("the native decrement failed.");
            if (!CashBalances.TryGetValue((deadDropGuid, slotIndex), out var preBalance))
                return Release1SmallCourtesyWorldMutationStatus.Unavailable;
            var postBalance = BalanceAfterDecrement?.Invoke(deadDropGuid, slotIndex, preBalance) ?? preBalance + amount;
            CashBalances[(deadDropGuid, slotIndex)] = postBalance;
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance)
        {
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        public Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

        // The mission under test never reads production activity.
        public Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)
        {
            activity = Release1ProductionActivitySnapshot.Empty;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }
        public Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)
        { snapshot = Release1FieldContactSnapshot.Unavailable(); return Release1SmallCourtesyWorldReadStatus.Unavailable; }

        public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason)
        { reason = "this fake never despawns a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }

        public Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)
        { reason = "this fake never provokes a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }
        public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)
        { reason = "this fake never parks a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }
        public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason)
        { reason = "this fake never unparks a field contact."; return Release1SmallCourtesyWorldMutationStatus.Rejected; }
    }
}
