using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseLoadingDockDefinitionTests
{
    [Fact]
    public void Docks_are_not_exposed_as_a_mutable_array()
    {
        Assert.False(FishWarehouseLoadingDockDefinition.Docks is FishWarehouseDockDefinition[]);
    }

    [Fact]
    public void Defines_exactly_two_stable_unique_off_street_bays()
    {
        var docks = FishWarehouseLoadingDockDefinition.Docks;

        Assert.Equal(new[] { 0, 1 }, docks.Select(dock => dock.Index));
        Assert.Equal(2, docks.Count);
        Assert.Equal(4, docks.SelectMany(dock => new[] { dock.DockGuid, dock.ParkingLotGuid }).Distinct().Count());
        Assert.All(docks, dock =>
        {
            Assert.True(Guid.TryParse(dock.DockGuid, out _));
            Assert.True(Guid.TryParse(dock.ParkingLotGuid, out _));
            Assert.True(FishWarehouseLoadingDockDefinition.IsInsideFrontageEnvelope(dock));
            Assert.Equal(90f, dock.YawDegrees);
        });
        Assert.False(FishWarehouseLoadingDockDefinition.Overlaps(docks[0], docks[1]));
        Assert.True(FishWarehouseLoadingDockDefinition.HasValidLayout());
    }

    [Fact]
    public void Places_bays_at_the_mapped_road_side_frontage()
    {
        var docks = FishWarehouseLoadingDockDefinition.Docks;

        Assert.Equal(new[] { 6.82f, 6.82f }, docks.Select(dock => dock.CenterX));
        Assert.All(docks, dock =>
        {
            Assert.Equal(1.5f, dock.HalfWidth);
            Assert.Equal(2.6f, dock.HalfLength);
            Assert.Equal(90f, dock.YawDegrees);
        });
        Assert.Equal(new[] { 12.285f, 9.195f }, docks.Select(dock => dock.CenterZ));
        Assert.Equal(4.10f, FishWarehouseLoadingDockDefinition.FrontageMinX);
        Assert.Equal(9.55f, FishWarehouseLoadingDockDefinition.FrontageMaxX);
        Assert.Equal(7.55f, FishWarehouseLoadingDockDefinition.FrontageMinZ);
        Assert.Equal(13.95f, FishWarehouseLoadingDockDefinition.FrontageMaxZ);
    }

    [Fact]
    public void Uses_rotated_detector_extents_for_the_mapped_side_by_side_layout()
    {
        var extents = FishWarehouseLoadingDockDefinition.GetAxisAlignedHalfExtents(
            FishWarehouseLoadingDockDefinition.Docks[0]);

        Assert.Equal(2.6f, extents.X, 3);
        Assert.Equal(1.5f, extents.Z, 3);
        Assert.False(FishWarehouseLoadingDockDefinition.Overlaps(
            FishWarehouseLoadingDockDefinition.Docks[0],
            FishWarehouseLoadingDockDefinition.Docks[1]));
    }

    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(1, 1, false)]
    [InlineData(2, 1, false)]
    [InlineData(3, 3, false)]
    public void Atomic_publication_requires_both_valid_docks(int staged, int valid, bool expected)
    {
        Assert.Equal(expected, FishWarehouseLoadingDockDefinition.CanPublish(staged, valid));
    }

    [Fact]
    public void Unload_is_rejected_for_any_native_occupancy_or_delivery_association()
    {
        Assert.True(FishWarehouseLoadingDockDefinition.CanUnload(new[]
        {
            new FishWarehouseDockOccupancySnapshot(false, false, false),
            new FishWarehouseDockOccupancySnapshot(false, false, false)
        }));
        Assert.False(FishWarehouseLoadingDockDefinition.CanUnload(new[]
        {
            new FishWarehouseDockOccupancySnapshot(true, false, false),
            new FishWarehouseDockOccupancySnapshot(false, false, false)
        }));
        Assert.False(FishWarehouseLoadingDockDefinition.CanUnload(new[]
        {
            new FishWarehouseDockOccupancySnapshot(false, true, false),
            new FishWarehouseDockOccupancySnapshot(false, false, false)
        }));
        Assert.False(FishWarehouseLoadingDockDefinition.CanUnload(new[]
        {
            new FishWarehouseDockOccupancySnapshot(false, false, true),
            new FishWarehouseDockOccupancySnapshot(false, false, false)
        }));
    }

    [Fact]
    public void Binds_one_clone_owned_box_collider_when_inactive_detector_has_no_runtime_references()
    {
        var nativeBox = new BoxColliderProjection("clone-box");
        var baseWrapper = new ColliderCandidate("base-wrapper", nativeBox, IsCloneOwned: true);

        var bound = FishWarehouseLoadingDockDefinition.TryBindDetectorCollider(
            runtimeReferences: Array.Empty<ColliderCandidate>(),
            hierarchyColliders: new[] { baseWrapper },
            tryCastBoxCollider: candidate => candidate.NativeBoxCollider,
            isCloneOwned: candidate => candidate.IsCloneOwned,
            selected: out var selected,
            diagnostics: out var diagnostics);

        Assert.True(bound);
        Assert.Same(nativeBox, selected);
        Assert.Equal(0, diagnostics.RuntimeReferenceCount);
        Assert.Equal(1, diagnostics.HierarchyColliderCount);
        Assert.Equal(1, diagnostics.OwnedBoxColliderCount);
    }

    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    public void Rejects_a_non_clone_owned_or_non_box_detector_candidate(
        bool hasNativeBoxCollider,
        bool isCloneOwned,
        int expectedOwnedBoxColliderCount)
    {
        var candidate = new ColliderCandidate(
            "candidate",
            hasNativeBoxCollider ? new BoxColliderProjection("native-box") : null,
            isCloneOwned);

        var bound = FishWarehouseLoadingDockDefinition.TryBindDetectorCollider(
            runtimeReferences: Array.Empty<ColliderCandidate>(),
            hierarchyColliders: new[] { candidate },
            tryCastBoxCollider: item => item.NativeBoxCollider,
            isCloneOwned: item => item.IsCloneOwned,
            selected: out _,
            diagnostics: out var diagnostics);

        Assert.False(bound);
        Assert.Equal(expectedOwnedBoxColliderCount, diagnostics.OwnedBoxColliderCount);
    }

    [Fact]
    public void Rejects_ambiguous_clone_owned_box_colliders()
    {
        var candidates = new[]
        {
            new ColliderCandidate("base-wrapper-1", new BoxColliderProjection("clone-box-1"), IsCloneOwned: true),
            new ColliderCandidate("base-wrapper-2", new BoxColliderProjection("clone-box-2"), IsCloneOwned: true)
        };

        var bound = FishWarehouseLoadingDockDefinition.TryBindDetectorCollider(
            runtimeReferences: Array.Empty<ColliderCandidate>(),
            hierarchyColliders: candidates,
            tryCastBoxCollider: candidate => candidate.NativeBoxCollider,
            isCloneOwned: candidate => candidate.IsCloneOwned,
            selected: out _,
            diagnostics: out var diagnostics);

        Assert.False(bound);
        Assert.Equal(2, diagnostics.OwnedBoxColliderCount);
    }

    private sealed record ColliderCandidate(
        string Name,
        BoxColliderProjection? NativeBoxCollider,
        bool IsCloneOwned);

    private sealed record BoxColliderProjection(string Name);
}
