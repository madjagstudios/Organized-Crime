using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ShortNoticeQuantityHarnessTests
{
    [Fact]
    public void Harness_reports_faulted_when_the_world_is_null()
    {
        var result = Release1ShortNoticeQuantityHarness.TryDecrementByTwo(null!);

        Assert.Equal(Release1StagingHarnessStatus.Faulted, result.Status);
        Assert.Contains("the world was not available.", result.Lines);
    }

    [Fact]
    public void Harness_reports_unavailable_when_no_slot_holds_at_least_two()
    {
        var world = new FakeWorld();
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        world.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cocaine", "brick", 1, true, 500f),
            new(1, null, null, 0, false, 0f)
        };

        var result = Release1ShortNoticeQuantityHarness.TryDecrementByTwo(world);

        Assert.Equal(Release1StagingHarnessStatus.Unavailable, result.Status);
        Assert.Contains("no dead drop slot held at least 2 units.", result.Lines);
        Assert.Empty(world.LockCalls);
        Assert.Empty(world.ChangeCalls);
    }

    [Fact]
    public void Harness_locks_decrements_by_two_reads_back_and_unlocks()
    {
        var world = new FakeWorld();
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        world.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cocaine", "brick", 3, true, 1_000f)
        };

        var result = Release1ShortNoticeQuantityHarness.TryDecrementByTwo(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.Single(world.LockCalls, call => call.Locked);
        Assert.Single(world.LockCalls, call => !call.Locked);
        var change = Assert.Single(world.ChangeCalls);
        Assert.Equal(("drop-a", 0, -2), change);
        Assert.Contains(result.Lines, line =>
            line.Contains("pre", StringComparison.Ordinal) &&
            line.Contains("quantity 3", StringComparison.Ordinal) &&
            line.Contains("value", StringComparison.Ordinal));
        Assert.Contains(result.Lines, line =>
            line.Contains("post", StringComparison.Ordinal) &&
            line.Contains("quantity 1", StringComparison.Ordinal) &&
            line.Contains("value", StringComparison.Ordinal));
    }

    [Fact]
    public void Harness_reports_the_value_reading_it_observed()
    {
        var perUnitWorld = new FakeWorld();
        perUnitWorld.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        perUnitWorld.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cocaine", "brick", 3, true, 1_000f)
        };
        perUnitWorld.ValueAfterDecrement = (_, _, _) => 1_000f;

        var perUnitResult = Release1ShortNoticeQuantityHarness.TryDecrementByTwo(perUnitWorld);

        Assert.Contains(Release1ShortNoticeQuantityHarness.PerUnitLine, perUnitResult.Lines);
        Assert.DoesNotContain(Release1ShortNoticeQuantityHarness.PerStackLine, perUnitResult.Lines);

        var perStackWorld = new FakeWorld();
        perStackWorld.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        perStackWorld.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cocaine", "brick", 3, true, 1_000f)
        };
        perStackWorld.ValueAfterDecrement = (_, _, postQuantity) => postQuantity * (1_000f / 3f);

        var perStackResult = Release1ShortNoticeQuantityHarness.TryDecrementByTwo(perStackWorld);

        Assert.Contains(Release1ShortNoticeQuantityHarness.PerStackLine, perStackResult.Lines);
        Assert.DoesNotContain(Release1ShortNoticeQuantityHarness.PerUnitLine, perStackResult.Lines);
    }

    [Fact]
    public void Harness_faults_when_the_read_back_quantity_is_not_pre_minus_two()
    {
        var world = new FakeWorld();
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        world.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cocaine", "brick", 3, true, 1_000f)
        };
        world.QuantityAfterDecrement = (_, _, preQuantity) => preQuantity - 1;

        var result = Release1ShortNoticeQuantityHarness.TryDecrementByTwo(world);

        Assert.Equal(Release1StagingHarnessStatus.Faulted, result.Status);
        Assert.Contains("expected post quantity 1, observed 2", result.Lines);
        Assert.Single(world.LockCalls, call => call.Locked);
        Assert.Single(world.LockCalls, call => !call.Locked);
    }

    [Fact]
    public void Harness_releases_the_lock_when_the_decrement_throws()
    {
        var world = new FakeWorld();
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        world.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cocaine", "brick", 3, true, 1_000f)
        };
        world.ThrowOnChangeQuantity = true;

        var result = Release1ShortNoticeQuantityHarness.TryDecrementByTwo(world);

        Assert.Equal(Release1StagingHarnessStatus.Faulted, result.Status);
        Assert.Contains(result.Lines, line => line.Contains("an exception was thrown while decrementing", StringComparison.Ordinal));
        Assert.Single(world.LockCalls, call => call.Locked);
        Assert.Single(world.LockCalls, call => !call.Locked);
    }

    [Fact]
    public void Harness_never_calls_TryInsertPackagedProduct()
    {
        var world = new FakeWorld();
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false) };
        world.SlotsByDrop["drop-a"] = new List<Release1SmallCourtesySlotSnapshot>
        {
            new(0, "cocaine", "brick", 3, true, 1_000f)
        };

        var result = Release1ShortNoticeQuantityHarness.TryDecrementByTwo(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
    }

    private sealed class FakeWorld : IRelease1SmallCourtesyWorld
    {
        public IReadOnlyList<Release1SmallCourtesyDropCandidate> Drops { get; set; } =
            Array.Empty<Release1SmallCourtesyDropCandidate>();
        public Dictionary<string, List<Release1SmallCourtesySlotSnapshot>> SlotsByDrop { get; } = new();
        public bool ThrowOnChangeQuantity { get; set; }
        public Func<string, int, int, int>? QuantityAfterDecrement { get; set; }
        public Func<string, int, int, float>? ValueAfterDecrement { get; set; }
        public List<(string Guid, int SlotIndex, bool Locked)> LockCalls { get; } = new();
        public List<(string Guid, int SlotIndex, int Amount)> ChangeCalls { get; } = new();

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
            ChangeCalls.Add((deadDropGuid, slotIndex, amount));
            if (ThrowOnChangeQuantity) throw new InvalidOperationException("the native decrement failed.");
            if (SlotsByDrop.TryGetValue(deadDropGuid, out var list))
            {
                var index = list.FindIndex(candidate => candidate.SlotIndex == slotIndex);
                if (index >= 0)
                {
                    var slot = list[index];
                    var preQuantity = slot.Quantity;
                    var postQuantity = QuantityAfterDecrement?.Invoke(deadDropGuid, slotIndex, preQuantity) ?? preQuantity + amount;
                    var postValue = ValueAfterDecrement?.Invoke(deadDropGuid, slotIndex, postQuantity) ?? slot.MonetaryValue;
                    list[index] = slot with { Quantity = postQuantity, MonetaryValue = postValue };
                }
            }
            return Release1SmallCourtesyWorldMutationStatus.Succeeded;
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
            balance = 0f;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount) =>
            Release1SmallCourtesyWorldMutationStatus.Rejected;

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
