using OrganizedCrime.Runtime;
using Xunit;
using NativeLocation = Il2CppScheduleOne.Law.CheckpointManager.ECheckpointLocation;

namespace OrganizedCrime.Tests;

[Collection("OC-168 checkpoint swap map")]
public sealed class OcCheckpointSwapStateTests
{
    public OcCheckpointSwapStateTests()
    {
        OcCheckpointSwapState.Clear();
    }

    [Fact]
    public void Unmapped_clone_passes_through_without_touching_the_field()
    {
        var touched = false;
        var swap = OcCheckpointSwapState.BeginSwap(
            12345,
            NativeLocation.Western,
            readField: () => { touched = true; return null; },
            writeField: _ => touched = true);

        Assert.False(swap.Swapped);
        Assert.False(touched);
    }

    [Fact]
    public void Mapped_clone_with_another_location_passes_through()
    {
        var mine = new object();
        OcCheckpointSwapState.MapClone(12345, mine);
        var touched = false;

        var swap = OcCheckpointSwapState.BeginSwap(
            12345,
            NativeLocation.Docks,
            readField: () => { touched = true; return null; },
            writeField: _ => touched = true);

        Assert.False(swap.Swapped);
        Assert.False(touched);
    }

    [Fact]
    public void Mapped_clone_with_western_swaps_and_captures_the_original()
    {
        var mine = new object();
        var native = new object();
        OcCheckpointSwapState.MapClone(12345, mine);
        object? field = native;

        var swap = OcCheckpointSwapState.BeginSwap(
            12345,
            NativeLocation.Western,
            readField: () => field,
            writeField: value => field = value);

        Assert.True(swap.Swapped);
        Assert.Same(native, swap.Original);
        Assert.Same(mine, field);
    }

    [Fact]
    public void BeginSwap_fails_open_when_the_field_throws()
    {
        var mine = new object();
        OcCheckpointSwapState.MapClone(12345, mine);
        object? field = new object();

        var readThrows = OcCheckpointSwapState.BeginSwap(
            12345,
            NativeLocation.Western,
            readField: () => throw new InvalidOperationException("read"),
            writeField: value => field = value);
        Assert.False(readThrows.Swapped);

        var writeThrows = OcCheckpointSwapState.BeginSwap(
            12345,
            NativeLocation.Western,
            readField: () => field,
            writeField: _ => throw new InvalidOperationException("write"));
        Assert.False(writeThrows.Swapped);
    }

    [Fact]
    public void EndSwap_restores_the_original()
    {
        var mine = new object();
        var native = new object();
        OcCheckpointSwapState.MapClone(12345, mine);
        object? field = native;
        var swap = OcCheckpointSwapState.BeginSwap(
            12345,
            NativeLocation.Western,
            readField: () => field,
            writeField: value => field = value);

        var restored = OcCheckpointSwapState.EndSwap(swap, value => field = value);

        Assert.True(restored);
        Assert.Same(native, field);
    }

    [Fact]
    public void EndSwap_is_a_noop_when_nothing_swapped()
    {
        var restored = OcCheckpointSwapState.EndSwap(
            OcCheckpointSwap.None,
            _ => throw new InvalidOperationException("must not run"));

        Assert.True(restored);
    }

    [Fact]
    public void EndSwap_reports_false_when_the_restore_throws()
    {
        var restored = OcCheckpointSwapState.EndSwap(
            new OcCheckpointSwap(Swapped: true, Original: new object()),
            _ => throw new InvalidOperationException("restore"));

        Assert.False(restored);
    }

    [Fact]
    public void Unmap_removes_the_clone()
    {
        var mine = new object();
        OcCheckpointSwapState.MapClone(12345, mine);
        OcCheckpointSwapState.UnmapClone(12345);

        Assert.False(OcCheckpointSwapState.TryGetCheckpoint(12345, out _));
    }

    [Fact]
    public void Clear_empties_the_map()
    {
        OcCheckpointSwapState.MapClone(1, new object());
        OcCheckpointSwapState.MapClone(2, new object());
        OcCheckpointSwapState.Clear();

        Assert.False(OcCheckpointSwapState.TryGetCheckpoint(1, out _));
        Assert.False(OcCheckpointSwapState.TryGetCheckpoint(2, out _));
    }

    [Fact]
    public void Map_replaces_the_checkpoint_for_a_remapped_clone()
    {
        var first = new object();
        var second = new object();
        OcCheckpointSwapState.MapClone(12345, first);
        OcCheckpointSwapState.MapClone(12345, second);

        Assert.True(OcCheckpointSwapState.TryGetCheckpoint(12345, out var current));
        Assert.Same(second, current);
    }

    [Fact]
    public void Map_ignores_a_zero_pointer_and_a_null_checkpoint()
    {
        OcCheckpointSwapState.MapClone(0, new object());
        OcCheckpointSwapState.MapClone(12345, null!);

        Assert.False(OcCheckpointSwapState.TryGetCheckpoint(0, out _));
        Assert.False(OcCheckpointSwapState.TryGetCheckpoint(12345, out _));
    }
}
