using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3DeadDropDeliveryTests
{
    private const string ProductId = "big-monkey";
    private const string PackagingId = "brick";

    [Theory]
    [InlineData(15_000, 2)]
    [InlineData(17_000, 2)]
    [InlineData(17_500, 3)]
    [InlineData(20_000, 3)]
    public void Brick_count_scales_with_the_reward(int reward, int expected)
    {
        Assert.Equal(expected, Chapter3JobTuning.BricksForReward(reward));
    }

    [Fact]
    public void Freeze_picks_the_best_discovered_product_and_a_deterministic_empty_drop()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate("cheap", "Cheap", 500d, true),
            new Release1SmallCourtesyProductCandidate("rich-undiscovered", "Rich", 50_000d, false),
            new Release1SmallCourtesyProductCandidate("best", "Best", 5_000d, true)
        };
        var drops = new[]
        {
            Drop("drop-b", "Drop B", false),
            Drop("drop-a", "Drop A", true),
            Drop("drop-c", "Drop C", true)
        };

        var frozen = Chapter3DeadDropFreeze.TryFreeze(
            products, drops, new Release1SmallCourtesyPackagingCandidate(PackagingId, "Brick"),
            "chapter3-dead-drop|player|1", 2, out var first, out var status);

        Assert.True(frozen);
        Assert.Equal(Chapter3DeadDropFreezeStatus.Frozen, status);
        Assert.NotNull(first);
        Assert.Equal("best", first!.ProductId);
        Assert.Equal("Best", first.ProductName);
        Assert.Equal(PackagingId, first.PackagingId);
        Assert.Equal(2, first.BrickCount);
        Assert.False(first.RemovalConfirmed);

        var again = Chapter3DeadDropFreeze.TryFreeze(
            products, drops, new Release1SmallCourtesyPackagingCandidate(PackagingId, "Brick"),
            "chapter3-dead-drop|player|1", 2, out var second, out _);

        Assert.True(again);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Freeze_breaks_product_ties_by_product_id()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate("b-product", "B", 5_000d, true),
            new Release1SmallCourtesyProductCandidate("a-product", "A", 5_000d, true)
        };

        var frozen = Chapter3DeadDropFreeze.TryFreeze(
            products, new[] { Drop("drop-a", "Drop A", true) },
            new Release1SmallCourtesyPackagingCandidate(PackagingId, "Brick"),
            "chapter3-dead-drop|player|1", 2, out var delivery, out _);

        Assert.True(frozen);
        Assert.Equal("a-product", delivery!.ProductId);
    }

    [Fact]
    public void Freeze_refuses_without_a_discovered_product()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate("rich", "Rich", 50_000d, false)
        };

        var frozen = Chapter3DeadDropFreeze.TryFreeze(
            products, new[] { Drop("drop-a", "Drop A", true) },
            new Release1SmallCourtesyPackagingCandidate(PackagingId, "Brick"),
            "chapter3-dead-drop|player|1", 2, out var delivery, out var status);

        Assert.False(frozen);
        Assert.Null(delivery);
        Assert.Equal(Chapter3DeadDropFreezeStatus.NoDiscoveredProduct, status);
    }

    [Fact]
    public void Freeze_refuses_without_an_empty_drop()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate(ProductId, "Big Monkey", 5_000d, true)
        };

        var frozen = Chapter3DeadDropFreeze.TryFreeze(
            products, new[] { Drop("drop-a", "Drop A", false) },
            new Release1SmallCourtesyPackagingCandidate(PackagingId, "Brick"),
            "chapter3-dead-drop|player|1", 2, out var delivery, out var status);

        Assert.False(frozen);
        Assert.Null(delivery);
        Assert.Equal(Chapter3DeadDropFreezeStatus.NoEmptyDeadDrop, status);
    }

    [Fact]
    public void Freeze_rejects_invalid_input()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate(ProductId, "Big Monkey", 5_000d, true)
        };
        var drops = new[] { Drop("drop-a", "Drop A", true) };
        var packaging = new Release1SmallCourtesyPackagingCandidate(PackagingId, "Brick");

        Assert.False(Chapter3DeadDropFreeze.TryFreeze(
            null, drops, packaging, "c", 2, out _, out var s1));
        Assert.Equal(Chapter3DeadDropFreezeStatus.InvalidInput, s1);
        Assert.False(Chapter3DeadDropFreeze.TryFreeze(
            products, null, packaging, "c", 2, out _, out var s2));
        Assert.Equal(Chapter3DeadDropFreezeStatus.InvalidInput, s2);
        Assert.False(Chapter3DeadDropFreeze.TryFreeze(
            products, drops, null, "c", 2, out _, out var s3));
        Assert.Equal(Chapter3DeadDropFreezeStatus.InvalidInput, s3);
        Assert.False(Chapter3DeadDropFreeze.TryFreeze(
            products, drops, packaging, "c", 0, out _, out var s4));
        Assert.Equal(Chapter3DeadDropFreezeStatus.InvalidInput, s4);
    }

    [Fact]
    public void FreezeNamed_keeps_the_named_product_and_chooses_a_deterministic_empty_drop()
    {
        var drops = new[]
        {
            Drop("drop-b", "Drop B", false),
            Drop("drop-a", "Drop A", true),
            Drop("drop-c", "Drop C", true)
        };

        var frozen = Chapter3DeadDropFreeze.TryFreezeNamed(
            "cheap", "Cheap", drops, new Release1SmallCourtesyPackagingCandidate(PackagingId, "Brick"),
            "chapter3-emergency-supply|player|1", 2, out var first, out var status);

        Assert.True(frozen);
        Assert.Equal(Chapter3DeadDropFreezeStatus.Frozen, status);
        Assert.NotNull(first);
        Assert.Equal("cheap", first!.ProductId);
        Assert.Equal("Cheap", first.ProductName);
        Assert.Equal(PackagingId, first.PackagingId);
        Assert.Equal(2, first.BrickCount);
        Assert.False(first.RemovalConfirmed);

        var again = Chapter3DeadDropFreeze.TryFreezeNamed(
            "cheap", "Cheap", drops, new Release1SmallCourtesyPackagingCandidate(PackagingId, "Brick"),
            "chapter3-emergency-supply|player|1", 2, out var second, out _);

        Assert.True(again);
        Assert.Equal(first, second);
    }

    [Fact]
    public void FreezeNamed_refuses_without_an_empty_drop()
    {
        var frozen = Chapter3DeadDropFreeze.TryFreezeNamed(
            ProductId, "Big Monkey", new[] { Drop("drop-a", "Drop A", false) },
            new Release1SmallCourtesyPackagingCandidate(PackagingId, "Brick"),
            "chapter3-emergency-supply|player|1", 2, out var delivery, out var status);

        Assert.False(frozen);
        Assert.Null(delivery);
        Assert.Equal(Chapter3DeadDropFreezeStatus.NoEmptyDeadDrop, status);
    }

    [Fact]
    public void FreezeNamed_rejects_invalid_input()
    {
        var drops = new[] { Drop("drop-a", "Drop A", true) };
        var packaging = new Release1SmallCourtesyPackagingCandidate(PackagingId, "Brick");

        Assert.False(Chapter3DeadDropFreeze.TryFreezeNamed(
            "  ", "Big Monkey", drops, packaging, "c", 2, out _, out var s1));
        Assert.Equal(Chapter3DeadDropFreezeStatus.InvalidInput, s1);
        Assert.False(Chapter3DeadDropFreeze.TryFreezeNamed(
            ProductId, null, drops, packaging, "c", 2, out _, out var s2));
        Assert.Equal(Chapter3DeadDropFreezeStatus.InvalidInput, s2);
        Assert.False(Chapter3DeadDropFreeze.TryFreezeNamed(
            ProductId, "Big Monkey", null, packaging, "c", 2, out _, out var s3));
        Assert.Equal(Chapter3DeadDropFreezeStatus.InvalidInput, s3);
        Assert.False(Chapter3DeadDropFreeze.TryFreezeNamed(
            ProductId, "Big Monkey", drops, null, "c", 2, out _, out var s4));
        Assert.Equal(Chapter3DeadDropFreezeStatus.InvalidInput, s4);
        Assert.False(Chapter3DeadDropFreeze.TryFreezeNamed(
            ProductId, "Big Monkey", drops, packaging, "c", 0, out _, out var s5));
        Assert.Equal(Chapter3DeadDropFreezeStatus.InvalidInput, s5);
    }

    [Fact]
    public void Exact_count_in_one_slot_completes()
    {
        var verdict = Chapter3DeadDropDeliveryClassifier.Classify(
            new[] { Slot(0, ProductId, PackagingId, 2), Slot(1, null, null, 0) },
            ProductId, PackagingId, 2);

        var complete = Assert.IsType<Chapter3DeliveryComplete>(verdict);
        Assert.Equal(0, complete.SlotIndex);
    }

    [Fact]
    public void More_than_enough_in_one_slot_completes()
    {
        var verdict = Chapter3DeadDropDeliveryClassifier.Classify(
            new[] { Slot(3, ProductId, PackagingId, 5) },
            ProductId, PackagingId, 2);

        var complete = Assert.IsType<Chapter3DeliveryComplete>(verdict);
        Assert.Equal(3, complete.SlotIndex);
    }

    [Fact]
    public void Other_items_elsewhere_do_not_block_completion()
    {
        var verdict = Chapter3DeadDropDeliveryClassifier.Classify(
            new[]
            {
                Slot(0, "other-product", "jar", 4),
                Slot(1, ProductId, PackagingId, 3)
            },
            ProductId, PackagingId, 3);

        var complete = Assert.IsType<Chapter3DeliveryComplete>(verdict);
        Assert.Equal(1, complete.SlotIndex);
    }

    [Fact]
    public void Split_over_two_slots_holds_as_spread()
    {
        var verdict = Chapter3DeadDropDeliveryClassifier.Classify(
            new[]
            {
                Slot(0, ProductId, PackagingId, 1),
                Slot(1, ProductId, PackagingId, 1)
            },
            ProductId, PackagingId, 2);

        var hold = Assert.IsType<Chapter3DeliveryHold>(verdict);
        Assert.Equal(Chapter3DeliveryHoldReason.Spread, hold.Reason);
    }

    [Fact]
    public void Short_slot_holds_as_short()
    {
        var verdict = Chapter3DeadDropDeliveryClassifier.Classify(
            new[] { Slot(2, ProductId, PackagingId, 1) },
            ProductId, PackagingId, 3);

        var hold = Assert.IsType<Chapter3DeliveryHold>(verdict);
        Assert.Equal(Chapter3DeliveryHoldReason.Short, hold.Reason);
        Assert.Contains("slot 2", hold.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_product_holds()
    {
        var verdict = Chapter3DeadDropDeliveryClassifier.Classify(
            new[] { Slot(0, "other-product", PackagingId, 3) },
            ProductId, PackagingId, 3);

        var hold = Assert.IsType<Chapter3DeliveryHold>(verdict);
        Assert.Equal(Chapter3DeliveryHoldReason.WrongProduct, hold.Reason);
    }

    [Fact]
    public void Wrong_packaging_holds()
    {
        var verdict = Chapter3DeadDropDeliveryClassifier.Classify(
            new[] { Slot(0, ProductId, "jar", 3) },
            ProductId, PackagingId, 3);

        var hold = Assert.IsType<Chapter3DeliveryHold>(verdict);
        Assert.Equal(Chapter3DeliveryHoldReason.WrongPackaging, hold.Reason);
    }

    [Fact]
    public void Empty_drop_holds_as_empty()
    {
        var verdict = Chapter3DeadDropDeliveryClassifier.Classify(
            new[] { Slot(0, null, null, 0), Slot(1, null, null, 0) },
            ProductId, PackagingId, 2);

        var hold = Assert.IsType<Chapter3DeliveryHold>(verdict);
        Assert.Equal(Chapter3DeliveryHoldReason.Empty, hold.Reason);
    }

    [Fact]
    public void Zero_slots_is_never_empty()
    {
        Assert.Equal(
            Chapter3DeliveryHoldReason.Uninitialized,
            Assert.IsType<Chapter3DeliveryHold>(Chapter3DeadDropDeliveryClassifier.Classify(
                Array.Empty<Release1SmallCourtesySlotSnapshot>(), ProductId, PackagingId, 2)).Reason);
        Assert.Equal(
            Chapter3DeliveryHoldReason.Uninitialized,
            Assert.IsType<Chapter3DeliveryHold>(Chapter3DeadDropDeliveryClassifier.Classify(
                null, ProductId, PackagingId, 2)).Reason);
    }

    private static Release1SmallCourtesyDropCandidate Drop(string guid, string name, bool empty) =>
        new(guid, name, "A vanilla dead drop.", 1d, 2d, 3d, empty);

    private static Release1SmallCourtesySlotSnapshot Slot(int index, string? productId, string? packagingId, int quantity) =>
        new(index, productId, packagingId, quantity, quantity > 0, 0f);
}
