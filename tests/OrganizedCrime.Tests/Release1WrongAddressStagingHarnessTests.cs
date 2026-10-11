using OrganizedCrime.Runtime.Release1.World;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1WrongAddressStagingHarnessTests
{
    [Fact]
    public void Stage_selects_the_highest_priced_discovered_product_with_ordinal_tie_break_the_brick_packaging_and_the_first_empty_drop()
    {
        var world = new FakeWorld();
        world.Products = new[]
        {
            new Release1SmallCourtesyProductCandidate("meth", "Meth", 900d, true),
            new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true),
            new Release1SmallCourtesyProductCandidate("weed", "Weed", 1_000d, true),
            new Release1SmallCourtesyProductCandidate("goldbrick", "Undiscovered", 5_000d, false)
        };
        world.Drops = new[]
        {
            new Release1SmallCourtesyDropCandidate("drop-z", "Drop Z", "Not empty", 0, 0, 0, false),
            new Release1SmallCourtesyDropCandidate("drop-b", "Drop B", "Second empty", 0, 0, 0, true),
            new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "First empty", 0, 0, 0, true)
        };

        var result = Release1WrongAddressStagingHarness.TryStage(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        var call = Assert.Single(world.InsertCalls);
        Assert.Equal(("drop-a", 0, "cocaine", "brick", 1), call);
        Assert.Contains(result.Lines, line => line.Contains("cocaine", StringComparison.Ordinal));
        Assert.Contains(result.Lines, line => line.Contains("drop-a", StringComparison.Ordinal));
    }

    [Fact]
    public void Stage_refuses_when_no_empty_drop_exists_and_never_calls_insert()
    {
        var world = new FakeWorld();
        world.Products = new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, true) };
        world.Drops = new[]
        {
            new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "Full", 0, 0, 0, false),
            new Release1SmallCourtesyDropCandidate("drop-b", "Drop B", "Full", 0, 0, 0, false)
        };

        var result = Release1WrongAddressStagingHarness.TryStage(world);

        Assert.Equal(Release1StagingHarnessStatus.NoEmptyDeadDrop, result.Status);
        Assert.Empty(world.InsertCalls);
    }

    [Fact]
    public void Stage_refuses_when_no_discovered_product_exists_and_never_calls_insert()
    {
        var world = new FakeWorld();
        world.Products = new[] { new Release1SmallCourtesyProductCandidate("cocaine", "Cocaine", 1_000d, false) };
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "Empty", 0, 0, 0, true) };

        var result = Release1WrongAddressStagingHarness.TryStage(world);

        Assert.Equal(Release1StagingHarnessStatus.NoDiscoveredProduct, result.Status);
        Assert.Empty(world.InsertCalls);
    }

    [Fact]
    public void Dump_lists_only_non_empty_slots_with_drop_name_guid_slot_index_product_packaging_quantity_and_value()
    {
        var world = new FakeWorld();
        world.Drops = new[]
        {
            new Release1SmallCourtesyDropCandidate("drop-b", "Drop B", "B", 0, 0, 0, false),
            new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false)
        };
        var filledSlotA = new Release1SmallCourtesySlotSnapshot(1, "cocaine", "brick", 3, true, 2_500f);
        var filledSlotB = new Release1SmallCourtesySlotSnapshot(0, "meth", "jar", 1, true, 800f);
        world.SlotsByDrop["drop-a"] = new[]
        {
            new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f),
            filledSlotA
        };
        world.SlotsByDrop["drop-b"] = new[] { filledSlotB };

        var result = Release1WrongAddressStagingHarness.TryDump(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.Equal(new[]
        {
            "dead drops read: 2",
            "drop Drop A (drop-a) slots 2, occupied 1",
            $"drop Drop A (drop-a) slot {filledSlotA.SlotIndex} product {filledSlotA.ProductId} packaging {filledSlotA.PackagingId} quantity {filledSlotA.Quantity} value {filledSlotA.MonetaryValue}",
            "drop Drop B (drop-b) slots 1, occupied 1",
            $"drop Drop B (drop-b) slot {filledSlotB.SlotIndex} product {filledSlotB.ProductId} packaging {filledSlotB.PackagingId} quantity {filledSlotB.Quantity} value {filledSlotB.MonetaryValue}"
        }, result.Lines);
    }

    [Fact]
    public void Dump_reports_success_with_summary_and_drop_line_when_every_drop_is_empty()
    {
        var world = new FakeWorld();
        world.Drops = new[] { new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, true) };
        world.SlotsByDrop["drop-a"] = new[] { new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f) };

        var result = Release1WrongAddressStagingHarness.TryDump(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.Equal(new[]
        {
            "dead drops read: 1",
            "drop Drop A (drop-a) slots 1, occupied 0"
        }, result.Lines);
    }

    [Fact]
    public void Dump_reports_per_drop_read_status_when_slot_read_fails_for_one_drop()
    {
        var world = new FakeWorld();
        world.Drops = new[]
        {
            new Release1SmallCourtesyDropCandidate("drop-a", "Drop A", "A", 0, 0, 0, false),
            new Release1SmallCourtesyDropCandidate("drop-b", "Drop B", "B", 0, 0, 0, false)
        };
        world.SlotsByDrop["drop-a"] = new[] { new Release1SmallCourtesySlotSnapshot(0, null, null, 0, false, 0f) };

        var result = Release1WrongAddressStagingHarness.TryDump(world);

        Assert.Equal(Release1StagingHarnessStatus.Succeeded, result.Status);
        Assert.Equal(new[]
        {
            "dead drops read: 2",
            "drop Drop A (drop-a) slots 1, occupied 0",
            "drop Drop B (drop-b) slots Unavailable"
        }, result.Lines);
    }

    private sealed class FakeWorld : IDeadDropInventoryWorld
    {
        public IReadOnlyList<Release1SmallCourtesyProductCandidate> Products { get; set; } =
            Array.Empty<Release1SmallCourtesyProductCandidate>();
        public IReadOnlyList<Release1SmallCourtesyDropCandidate> Drops { get; set; } =
            Array.Empty<Release1SmallCourtesyDropCandidate>();
        public Dictionary<string, IReadOnlyList<Release1SmallCourtesySlotSnapshot>> SlotsByDrop { get; } = new();
        public Release1SmallCourtesyWorldMutationStatus InsertResult { get; set; } = Release1SmallCourtesyWorldMutationStatus.Succeeded;
        public string InsertReason { get; set; } = "staged.";
        public List<(string DropGuid, int SlotIndex, string ProductId, string PackagingId, int Quantity)> InsertCalls { get; } = new();

        public Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products)
        {
            products = Products;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops)
        {
            drops = Drops;
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadPackaging(
            Release1SmallCourtesyPackageKind kind,
            out Release1SmallCourtesyPackagingCandidate packaging)
        {
            packaging = kind == Release1SmallCourtesyPackageKind.Brick ? new("brick", "Brick") : new("jar", "Jar");
            return Release1SmallCourtesyWorldReadStatus.Ready;
        }

        public Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(
            string deadDropGuid,
            out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)
        {
            if (SlotsByDrop.TryGetValue(deadDropGuid, out var found))
            {
                slots = found;
                return Release1SmallCourtesyWorldReadStatus.Ready;
            }
            slots = Array.Empty<Release1SmallCourtesySlotSnapshot>();
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

        public Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked) =>
            Release1SmallCourtesyWorldMutationStatus.Unavailable;

        public Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount) =>
            Release1SmallCourtesyWorldMutationStatus.Unavailable;

        public Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(
            string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)
        {
            InsertCalls.Add((deadDropGuid, slotIndex, productId, packagingId, quantity));
            reason = InsertReason;
            return InsertResult;
        }

        public Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(
            string deadDropGuid,
            Action<string> callback,
            out IRelease1SmallCourtesyDropSubscription? subscription)
        {
            subscription = null;
            return Release1SmallCourtesyWorldReadStatus.Unavailable;
        }

    }
}
