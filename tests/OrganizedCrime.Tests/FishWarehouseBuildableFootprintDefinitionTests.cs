using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseBuildableFootprintDefinitionTests
{
    [Fact]
    public void Measured_footprint_is_derived_from_the_four_captured_corners()
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        Assert.Equal(-11.244f, footprint.MeasuredBounds.MinimumX, 3);
        Assert.Equal(11.244f, footprint.MeasuredBounds.MaximumX, 3);
        Assert.Equal(-6.526f, footprint.MeasuredBounds.MinimumZ, 3);
        Assert.Equal(6.772f, footprint.MeasuredBounds.MaximumZ, 3);
        Assert.Equal(0.5f, footprint.GridCellSize, 3);
        Assert.Equal(1, footprint.ClearanceCells);
    }

    [Theory]
    [InlineData(11.210f, -6.526f)]
    [InlineData(11.244f, 6.510f)]
    [InlineData(-10.797f, 6.772f)]
    [InlineData(-11.244f, -6.510f)]
    public void Keeps_each_captured_interior_corner_inside_the_measured_floor_envelope(float x, float z)
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        Assert.True(footprint.MeasuredBounds.Contains(x, z));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(-10.244f, -5.526f)]
    [InlineData(10.244f, 5.772f)]
    public void Accepts_center_points_at_least_one_native_cell_inside_the_measured_walls(float x, float z)
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        Assert.True(footprint.ContainsPlacementPoint(new FishWarehousePoint(x, 0f, z)));
    }

    [Theory]
    [InlineData(-11.244f, -6.526f)]
    [InlineData(11.244f, -6.526f)]
    [InlineData(-11.244f, 6.772f)]
    [InlineData(11.244f, 6.772f)]
    [InlineData(-10.9f, 0f)]
    [InlineData(10.9f, 0f)]
    [InlineData(0f, -6.1f)]
    [InlineData(0f, 6.3f)]
    public void Rejects_wall_and_one_cell_clearance_boundary_points(float x, float z)
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        Assert.False(footprint.ContainsPlacementPoint(new FishWarehousePoint(x, 0f, z)));
    }

    [Theory]
    [InlineData(-11.744f, 0f)]
    [InlineData(11.744f, 0f)]
    [InlineData(0f, -7.026f)]
    [InlineData(0f, 7.272f)]
    public void Rejects_measured_exterior_points(float x, float z)
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        Assert.False(footprint.ContainsPlacementPoint(new FishWarehousePoint(x, 0f, z)));
    }

    [Theory]
    [InlineData(-12.686f, 1.454f)]
    [InlineData(-4.542f, -7.975f)]
    [InlineData(12.655f, -3.794f)]
    public void Rejects_the_captured_exterior_wall_points(float x, float z)
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        Assert.False(footprint.MeasuredBounds.Contains(x, z));
        Assert.False(footprint.ContainsPlacementPoint(new FishWarehousePoint(x, 0f, z)));
    }

    [Theory]
    [InlineData(-10.244f, -5.526f, true)]
    [InlineData(10.244f, 5.772f, true)]
    [InlineData(-10.9f, 0f, false)]
    [InlineData(10.9f, 0f, false)]
    public void Requires_the_complete_native_cell_to_remain_inside_the_measured_floor(
        float centerX,
        float centerZ,
        bool expected)
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        Assert.Equal(
            expected,
            footprint.ContainsPlacementCell(new FishWarehousePoint(centerX, 0f, centerZ)));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-0.5f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Rejects_invalid_or_unsupported_grid_cell_sizes(float cellSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FishWarehouseBuildableFootprintDefinition.CreateMeasured(cellSize));
    }

    [Fact]
    public void Builds_the_measured_coordinate_set_relative_to_the_existing_zero_cell()
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        var coordinates = footprint.GetAllowedCellCoordinates(
            referenceCoordinateX: 0,
            referenceCoordinateY: 0,
            referenceCenterX: -3.75f,
            referenceCenterZ: -3.75f);

        Assert.Contains(new FishWarehouseGridCellCoordinate(-13, -4), coordinates);
        Assert.Contains(new FishWarehouseGridCellCoordinate(28, 20), coordinates);
        Assert.Contains(new FishWarehouseGridCellCoordinate(0, 0), coordinates);
        Assert.DoesNotContain(new FishWarehouseGridCellCoordinate(-14, 0), coordinates);
        Assert.DoesNotContain(new FishWarehouseGridCellCoordinate(0, 21), coordinates);
    }

    [Fact]
    public void Coordinate_set_preserves_existing_grid_cells_and_contains_only_safe_cells()
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        var coordinates = footprint.GetAllowedCellCoordinates(
            referenceCoordinateX: 0,
            referenceCoordinateY: 0,
            referenceCenterX: -3.75f,
            referenceCenterZ: -3.75f);

        Assert.Equal(42 * 25, coordinates.Count);
        Assert.All(coordinates, coordinate =>
        {
            var center = new FishWarehousePoint(
                -3.75f + (coordinate.X * 0.5f),
                0f,
                -3.75f + (coordinate.Y * 0.5f));
            Assert.True(footprint.ContainsPlacementCell(center));
        });
    }

    [Theory]
    [InlineData(-10.25f, -5.75f, -13, -4)]
    [InlineData(-0.25f, -0.25f, 7, 7)]
    [InlineData(10.25f, 6.25f, 28, 20)]
    public void Maps_local_build_points_to_the_preserved_native_coordinates(
        float pointX,
        float pointZ,
        int expectedX,
        int expectedY)
    {
        Assert.True(
            FishWarehouseBuildableFootprintDefinition.TryMapLocalPointToCoordinate(
                new FishWarehousePoint(pointX, 0f, pointZ),
                new FishWarehousePoint(-3.75f, 0f, -3.75f),
                0.5f,
                out var coordinate));

        Assert.Equal(new FishWarehouseGridCellCoordinate(expectedX, expectedY), coordinate);
    }

    [Fact]
    public void Rejects_non_finite_coordinate_mapping_inputs()
    {
        Assert.False(
            FishWarehouseBuildableFootprintDefinition.TryMapLocalPointToCoordinate(
                new FishWarehousePoint(float.NaN, 0f, 0f),
                new FishWarehousePoint(0f, 0f, 0f),
                0.5f,
                out _));
    }
}
