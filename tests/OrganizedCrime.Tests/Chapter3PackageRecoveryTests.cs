using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3PackageRecoveryTests
{
    private static readonly Release1SmallCourtesyPackagingCandidate Brick = new("brick", "Brick");

    [Fact]
    public void Freeze_picks_the_best_product_and_a_distinct_empty_pair()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate("cheap", "Cheap", 3_000d, true),
            new Release1SmallCourtesyProductCandidate("rich", "Rich", 5_000d, true),
            new Release1SmallCourtesyProductCandidate("richer", "Richer", 50_000d, false)
        };
        var drops = new[]
        {
            Drop("drop-a", "Drop A", 1d, 2d, 3d, true),
            Drop("drop-b", "Drop B", 4d, 5d, 6d, true),
            Drop("drop-full", "Drop Full", 7d, 8d, 9d, false)
        };

        var frozen = Chapter3PackageRecoveryFreeze.TryFreeze(
            products, drops, Brick, "chapter3-recovery|player|2", 2,
            out var recovery, out var status);

        Assert.True(frozen);
        Assert.Equal(Chapter3PackageRecoveryFreezeStatus.Frozen, status);
        Assert.Equal("rich", recovery!.ProductId);
        Assert.Equal("brick", recovery.PackagingId);
        Assert.Equal(2, recovery.BrickCount);
        Assert.NotEqual(recovery.Pickup.Guid, recovery.Handoff.Guid);
        Assert.Contains(recovery.Pickup.Guid, new[] { "drop-a", "drop-b" });
        Assert.Contains(recovery.Handoff.Guid, new[] { "drop-a", "drop-b" });
        Assert.False(recovery.Staged);
        Assert.False(recovery.Custody);
        Assert.Equal(-1, recovery.StagedSlotIndex);
        recovery.Validate();
    }

    [Fact]
    public void Freeze_is_deterministic_for_the_same_correlation()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate("rich", "Rich", 5_000d, true)
        };
        var drops = new[]
        {
            Drop("drop-a", "Drop A", 1d, 2d, 3d, true),
            Drop("drop-b", "Drop B", 4d, 5d, 6d, true),
            Drop("drop-c", "Drop C", 7d, 8d, 9d, true)
        };

        Chapter3PackageRecoveryFreeze.TryFreeze(
            products, drops, Brick, "chapter3-recovery|player|2", 2,
            out var first, out _);
        Chapter3PackageRecoveryFreeze.TryFreeze(
            products, drops, Brick, "chapter3-recovery|player|2", 2,
            out var second, out _);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Freeze_prefers_the_best_discovered_product_on_price_then_id()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate("b-id", "B", 5_000d, true),
            new Release1SmallCourtesyProductCandidate("a-id", "A", 5_000d, true)
        };
        var drops = new[]
        {
            Drop("drop-a", "Drop A", 1d, 2d, 3d, true),
            Drop("drop-b", "Drop B", 4d, 5d, 6d, true)
        };

        var frozen = Chapter3PackageRecoveryFreeze.TryFreeze(
            products, drops, Brick, "chapter3-recovery|player|2", 2,
            out var recovery, out _);

        Assert.True(frozen);
        Assert.Equal("a-id", recovery!.ProductId);
    }

    [Fact]
    public void Freeze_refuses_without_a_discovered_product()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate("rich", "Rich", 50_000d, false)
        };
        var drops = new[]
        {
            Drop("drop-a", "Drop A", 1d, 2d, 3d, true),
            Drop("drop-b", "Drop B", 4d, 5d, 6d, true)
        };

        var frozen = Chapter3PackageRecoveryFreeze.TryFreeze(
            products, drops, Brick, "chapter3-recovery|player|2", 2,
            out var recovery, out var status);

        Assert.False(frozen);
        Assert.Null(recovery);
        Assert.Equal(Chapter3PackageRecoveryFreezeStatus.NoDiscoveredProduct, status);
    }

    [Fact]
    public void Freeze_refuses_without_two_empty_drops()
    {
        var products = new[]
        {
            new Release1SmallCourtesyProductCandidate("rich", "Rich", 5_000d, true)
        };
        var drops = new[]
        {
            Drop("drop-a", "Drop A", 1d, 2d, 3d, true),
            Drop("drop-full", "Drop Full", 7d, 8d, 9d, false)
        };

        var frozen = Chapter3PackageRecoveryFreeze.TryFreeze(
            products, drops, Brick, "chapter3-recovery|player|2", 2,
            out var recovery, out var status);

        Assert.False(frozen);
        Assert.Null(recovery);
        Assert.Equal(Chapter3PackageRecoveryFreezeStatus.NoEmptyDeadDropPair, status);
    }

    [Fact]
    public void Freeze_rejects_invalid_input()
    {
        Assert.False(Chapter3PackageRecoveryFreeze.TryFreeze(
            null, Array.Empty<Release1SmallCourtesyDropCandidate>(), Brick, "c", 2,
            out _, out var status));
        Assert.Equal(Chapter3PackageRecoveryFreezeStatus.InvalidInput, status);
    }

    [Fact]
    public void Recovery_rejects_custody_before_staging_and_removal_before_custody()
    {
        var pickup = new Chapter3RecoveryDrop("a", "A", 1d, 2d, 3d);
        var handoff = new Chapter3RecoveryDrop("b", "B", 4d, 5d, 6d);

        Assert.Throws<ArgumentException>(() => new Chapter3PackageRecovery(
            pickup, handoff, "rich", "Rich", "brick", "Brick", 2, Custody: true).Validate());
        Assert.Throws<ArgumentException>(() => new Chapter3PackageRecovery(
            pickup, handoff, "rich", "Rich", "brick", "Brick", 2, Staged: true, RemovalConfirmed: true).Validate());
    }

    [Fact]
    public void Recovery_rejects_indistinct_drops()
    {
        var drop = new Chapter3RecoveryDrop("a", "A", 1d, 2d, 3d);

        Assert.Throws<ArgumentException>(() => new Chapter3PackageRecovery(
            drop, drop with { }, "rich", "Rich", "brick", "Brick", 2).Validate());
    }

    private static Release1SmallCourtesyDropCandidate Drop(
        string guid, string name, double x, double y, double z, bool isEmpty) =>
        new(guid, name, "description", x, y, z, isEmpty);
}
