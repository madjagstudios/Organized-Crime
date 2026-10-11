using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-141. The production officer world's swap-map seams run here against the
/// real <c>Il2CppOcOfficerWorld</c>: they touch only the static swap map and
/// never the native runtime, so no fake stands in for them.
/// </summary>
[Collection("OC-168 checkpoint swap map")]
public sealed class OcOfficerWorldSwapTests
{
    public OcOfficerWorldSwapTests()
    {
        OcCheckpointSwapState.Clear();
    }

    [Fact]
    public void TryClearSwapMap_clears_the_map_and_returns_ok()
    {
        OcCheckpointSwapState.MapClone(111, new object());
        OcCheckpointSwapState.MapClone(222, new object());
        var world = new Il2CppOcOfficerWorld();

        var status = world.TryClearSwapMap(out var detail);

        Assert.Equal(OcOfficerWorldStatus.Ok, status);
        Assert.False(string.IsNullOrWhiteSpace(detail));
        Assert.False(OcCheckpointSwapState.TryGetCheckpoint(111, out _));
        Assert.False(OcCheckpointSwapState.TryGetCheckpoint(222, out _));
    }

    [Fact]
    public void TryUnmapClone_removes_only_that_clone()
    {
        OcCheckpointSwapState.MapClone(111, new object());
        OcCheckpointSwapState.MapClone(222, new object());
        var world = new Il2CppOcOfficerWorld();

        var status = world.TryUnmapClone(new OcOfficerCloneRef(null, 111, 0), out var detail);

        Assert.Equal(OcOfficerWorldStatus.Ok, status);
        Assert.False(string.IsNullOrWhiteSpace(detail));
        Assert.False(OcCheckpointSwapState.TryGetCheckpoint(111, out _));
        Assert.True(OcCheckpointSwapState.TryGetCheckpoint(222, out _));
    }

    [Fact]
    public void TryReadNativeLeak_unknown_label_is_no_leak_without_a_host()
    {
        var world = new Il2CppOcOfficerWorld();

        foreach (var label in new string?[] { "Bogus", null, string.Empty })
        {
            var status = world.TryReadNativeLeak(label, out var leaked, out var detail);

            Assert.Equal(OcOfficerWorldStatus.Ok, status);
            Assert.False(leaked);
            Assert.False(string.IsNullOrWhiteSpace(detail));
        }
    }

    [Fact]
    public void TryReadNativeLeak_known_label_reaches_the_host_gate()
    {
        var world = new Il2CppOcOfficerWorld();

        foreach (var label in new[] { "Docks", "docks", "Western", "NorthResidential", "WestResidential" })
        {
            var status = world.TryReadNativeLeak(label, out var leaked, out _);

            Assert.Equal(OcOfficerWorldStatus.NotHost, status);
            Assert.False(leaked);
        }
    }
}
