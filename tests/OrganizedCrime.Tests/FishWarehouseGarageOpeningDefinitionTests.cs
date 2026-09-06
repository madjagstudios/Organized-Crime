using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseGarageOpeningDefinitionTests
{
    [Fact]
    public void Projects_the_approved_green_garage_panel_to_the_north_wall()
    {
        var candidates = ApprovedCandidates();

        Assert.True(FishWarehouseGarageOpeningDefinition.TryProject(candidates, out var projection));
        FishWarehouseGarageOpeningProjection result = projection!;

        Assert.Equal(-11.60205f, result.RawMinimumX, 5);
        Assert.Equal(-7.91795f, result.RawMaximumX, 5);
        Assert.Equal(-11.60f, result.Opening.HorizontalMinimum, 5);
        Assert.Equal(-7.91795f, result.Opening.HorizontalMaximum, 5);
        Assert.Equal(3.68205f, result.Opening.Width, 5);
        Assert.Equal(-9.75898f, result.Opening.Center, 5);
        Assert.Equal(FishWarehouseInteriorDoorSide.North, result.Opening.Wall);
        Assert.Equal(0f, result.Opening.Bottom, 5);
        Assert.Equal(5.35f, result.Opening.Top, 5);
        Assert.Equal(5.35f, result.Opening.Height, 5);
        Assert.Equal(0.45f, result.Lintel, 5);
        Assert.Equal(0.60f, result.TransitionPrism.Depth, 5);
        Assert.Equal(0f, result.NavigationBounds.MinimumX, 5);
        Assert.Equal(3.68205f, result.NavigationBounds.MaximumX, 5);
        Assert.Equal(1.84103f, result.NavigationCenterX, 5);
    }

    [Fact]
    public void Exact_allowlist_accepts_only_the_two_approved_root_panel_identities()
    {
        Assert.True(FishWarehouseGarageOpeningDefinition.TryProject(ApprovedCandidates(), out _));
    }

    [Theory]
    [MemberData(nameof(InvalidAllowlistCases))]
    public void Exact_allowlist_rejects_missing_partial_duplicate_and_wrong_bay_matches(
        IReadOnlyList<FishWarehouseGarageOpeningCandidate> candidates)
    {
        Assert.False(FishWarehouseGarageOpeningDefinition.TryProject(candidates, out _));
    }

    [Fact]
    public void Clamps_a_panel_that_reaches_below_the_floor_to_floor_y()
    {
        var candidates = ApprovedCandidates(panelBounds: new(
            -11.50205f, -8.01795f, -0.40f, 5.25f, 7.40f, 7.50f));

        Assert.True(FishWarehouseGarageOpeningDefinition.TryProject(candidates, out var projection));
        Assert.Equal(FishWarehouseInteriorRoomDefinition.FloorY, projection!.Opening.Bottom, 5);
    }

    [Theory]
    [MemberData(nameof(InvalidGeometryCases))]
    public void Rejects_invalid_aperture_geometry(
        IReadOnlyList<FishWarehouseGarageOpeningCandidate> candidates)
    {
        Assert.False(FishWarehouseGarageOpeningDefinition.TryProject(candidates, out _));
    }

    public static IEnumerable<object[]> InvalidAllowlistCases()
    {
        var approved = ApprovedCandidates();
        yield return new object[] { approved.Take(1).ToArray() };
        yield return new object[]
        {
            new[]
            {
                approved[0],
                approved[0] with { PanelIdentity = "GarageDoor (1) Copy" }
            }
        };
        yield return new object[]
        {
            new[]
            {
                approved[0],
                approved[0] with { RootPath = FishWarehouseGarageOpeningDefinition.ApprovedAlternateRootPath }
            }
        };
        yield return new object[]
        {
            new[]
            {
                approved[0],
                approved[1] with { RootPath = approved[1].RootPath + "/Partial" }
            }
        };
    }

    public static IEnumerable<object[]> InvalidGeometryCases()
    {
        var approved = ApprovedCandidates();
        yield return new object[]
        {
            WithPrimary(approved, approved[0] with
            {
                SurvivingNonTargetColliders = new[] { new FishWarehouseAnchorLocalBounds(-10f, -9f, 0.2f, 0.9f, 6.9f, 7.5f) }
            })
        };
        yield return new object[] { WithPrimary(approved, approved[0] with { HasFloorSupport = false }) };
        yield return new object[]
        {
            WithPrimary(approved, approved[0] with
            {
                PhysicalPanelPlane = PlaneAt(7.95f)
            })
        };
        yield return new object[]
        {
            WithPrimary(approved, approved[0] with
            {
                PanelBounds = new(-11.50205f, -8.01795f, 0f, 5.60f, 7.40f, 7.50f)
            })
        };
        yield return new object[]
        {
            WithPrimary(approved, approved[0] with
            {
                PanelBounds = new(-11.10f, -10.90f, 0f, 3.00f, 7.40f, 7.50f)
            })
        };
        yield return new object[] { WithPrimary(approved, approved[0] with { PhysicalPanelPlane = new(new(-9.76f, 2.625f, 7.5f), new(1f, 0f, 0f)) }) };
    }

    private static FishWarehouseGarageOpeningCandidate[] ApprovedCandidates(
        FishWarehouseAnchorLocalBounds? panelBounds = null)
    {
        var bounds = panelBounds ?? new(-11.50205f, -8.01795f, 0f, 5.25f, 7.40f, 7.50f);
        FishWarehousePhysicalPanelPlane plane = new(
            new FishWarehouseGarageVector(
                (bounds.MinimumX + bounds.MaximumX) / 2f,
                (bounds.MinimumY + bounds.MaximumY) / 2f,
                7.5f),
            new FishWarehouseGarageVector(0f, 0f, 1f));
        return new[]
        {
            new FishWarehouseGarageOpeningCandidate(
                FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath,
                "GarageDoor (1)",
                bounds,
                plane,
                SurvivingNonTargetColliders: Array.Empty<FishWarehouseAnchorLocalBounds>(),
                HasFloorSupport: true),
            new FishWarehouseGarageOpeningCandidate(
                FishWarehouseGarageOpeningDefinition.ApprovedAlternateRootPath,
                "GarageDoor_Other",
                bounds,
                plane,
                SurvivingNonTargetColliders: Array.Empty<FishWarehouseAnchorLocalBounds>(),
                HasFloorSupport: true)
        };
    }

    private static FishWarehousePhysicalPanelPlane PlaneAt(float z) =>
        new(new FishWarehouseGarageVector(-9.76f, 2.625f, z), new FishWarehouseGarageVector(0f, 0f, 1f));

    private static FishWarehouseGarageOpeningCandidate[] WithPrimary(
        FishWarehouseGarageOpeningCandidate[] candidates,
        FishWarehouseGarageOpeningCandidate primary)
    {
        var result = candidates.ToArray();
        result[0] = primary;
        return result;
    }
}
