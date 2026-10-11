using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseBuildableFootprintDefinitionTests
{
    [Fact]
    public void Room_derived_footprint_uses_the_interior_walls_minus_the_allowance()
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        Assert.Equal(-11.55f, footprint.MeasuredBounds.MinimumX, 3);
        Assert.Equal(11.55f, footprint.MeasuredBounds.MaximumX, 3);
        Assert.Equal(-6.85f, footprint.MeasuredBounds.MinimumZ, 3);
        Assert.Equal(6.85f, footprint.MeasuredBounds.MaximumZ, 3);
        Assert.Equal(0.5f, footprint.GridCellSize, 3);
        Assert.Equal(0, footprint.ClearanceCells);
        Assert.Equal(0.05f, FishWarehouseBuildableFootprintDefinition.WallAllowance, 3);
    }

    [Theory]
    [InlineData(11.55f, -6.85f)]
    [InlineData(11.55f, 6.85f)]
    [InlineData(-11.55f, 6.85f)]
    [InlineData(-11.55f, -6.85f)]
    public void Keeps_the_room_walls_minus_the_allowance_inside_the_floor_envelope(float x, float z)
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        Assert.True(footprint.MeasuredBounds.Contains(x, z));
    }

    [Theory]
    [InlineData(0.25f, 0f)]
    [InlineData(-11.55f, -6.85f)]
    [InlineData(11.55f, 6.85f)]
    public void Accepts_center_points_inside_the_room_walls_minus_the_allowance(float x, float z)
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        Assert.True(footprint.ContainsPlacementPoint(new FishWarehousePoint(x, 0f, z)));
    }

    [Theory]
    [InlineData(-11.6f, 0f)]
    [InlineData(11.6f, 0f)]
    [InlineData(0f, -6.9f)]
    [InlineData(0f, 6.9f)]
    [InlineData(-11.6f, -6.9f)]
    [InlineData(11.6f, 6.9f)]
    [InlineData(11.56f, 0f)]
    [InlineData(0f, 6.86f)]
    public void Rejects_points_at_or_beyond_the_interior_walls(float x, float z)
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        Assert.False(footprint.ContainsPlacementPoint(new FishWarehousePoint(x, 0f, z)));
    }

    [Theory]
    [InlineData(-12.05f, 0f)]
    [InlineData(12.05f, 0f)]
    [InlineData(0f, -7.35f)]
    [InlineData(0f, 7.35f)]
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
    [InlineData(-11.25f, 0f, true)]
    [InlineData(11.25f, 0f, true)]
    [InlineData(0.25f, 6.5f, true)]
    [InlineData(0.25f, -6.5f, true)]
    [InlineData(-11.4f, 0f, false)]
    [InlineData(11.4f, 0f, false)]
    [InlineData(0.25f, 6.7f, false)]
    [InlineData(0.25f, -6.7f, false)]
    [InlineData(11.55f, 0f, false)]
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
    public void Fixed_origin_coordinate_set_includes_the_employee_home_and_matches_the_footprint_count()
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        var coordinates = footprint.GetAllowedCellCoordinates(
            referenceCoordinateX: 0,
            referenceCoordinateY: 0,
            referenceCenterX: FishWarehouseBuildableFootprintDefinition.GridOriginX,
            referenceCenterZ: FishWarehouseBuildableFootprintDefinition.GridOriginZ);

        Assert.Contains(new FishWarehouseGridCellCoordinate(0, 0), coordinates);
        Assert.Contains(
            new FishWarehouseGridCellCoordinate(
                FishWarehouseEmployeeHomeDefinition.Default.GridX,
                FishWarehouseEmployeeHomeDefinition.Default.GridY),
            coordinates);
        Assert.Equal(46 * 27, coordinates.Count); // 46 columns by 27 rows at 0.5 m.
    }

    [Fact]
    public void Fixed_layout_repeats_and_every_cell_center_fits_the_placement_bounds()
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        var coordinates = footprint.GetAllowedCellCoordinates(
            referenceCoordinateX: 0,
            referenceCoordinateY: 0,
            referenceCenterX: FishWarehouseBuildableFootprintDefinition.GridOriginX,
            referenceCenterZ: FishWarehouseBuildableFootprintDefinition.GridOriginZ);
        var repeatedCoordinates = footprint.GetAllowedCellCoordinates(
            referenceCoordinateX: 0,
            referenceCoordinateY: 0,
            referenceCenterX: FishWarehouseBuildableFootprintDefinition.GridOriginX,
            referenceCenterZ: FishWarehouseBuildableFootprintDefinition.GridOriginZ);

        Assert.Equal(coordinates, repeatedCoordinates);
        Assert.All(coordinates, coordinate =>
        {
            var center = footprint.GetPropertyLocalCellCenter(coordinate, planeHeight: 0f);
            Assert.True(footprint.ContainsPlacementCell(center));
        });
    }

    [Fact]
    public void Every_allowed_cell_edge_stays_inside_the_room_walls_minus_the_allowance()
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);
        float halfCell = 0.5f / 2f;
        float minimumX = -(FishWarehouseInteriorRoomDefinition.HalfWidth - 0.05f);
        float maximumX = FishWarehouseInteriorRoomDefinition.HalfWidth - 0.05f;
        float minimumZ = -(FishWarehouseInteriorRoomDefinition.HalfDepth - 0.05f);
        float maximumZ = FishWarehouseInteriorRoomDefinition.HalfDepth - 0.05f;

        var coordinates = footprint.GetAllowedCellCoordinates(
            referenceCoordinateX: 0,
            referenceCoordinateY: 0,
            referenceCenterX: FishWarehouseBuildableFootprintDefinition.GridOriginX,
            referenceCenterZ: FishWarehouseBuildableFootprintDefinition.GridOriginZ);

        Assert.NotEmpty(coordinates);
        Assert.All(coordinates, coordinate =>
        {
            var center = footprint.GetPropertyLocalCellCenter(coordinate, planeHeight: 0f);
            Assert.True(center.X - halfCell >= minimumX);
            Assert.True(center.X + halfCell <= maximumX);
            Assert.True(center.Z - halfCell >= minimumZ);
            Assert.True(center.Z + halfCell <= maximumZ);
        });
    }

    [Fact]
    public void Fixed_cell_coordinates_map_to_fixed_property_local_centers()
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        Assert.Equal(
            new FishWarehousePoint(0.25f, 0.02f, 0f),
            footprint.GetPropertyLocalCellCenter(new FishWarehouseGridCellCoordinate(0, 0), 0.02f));
        Assert.Equal(
            new FishWarehousePoint(1.25f, 0.02f, -1.5f),
            footprint.GetPropertyLocalCellCenter(new FishWarehouseGridCellCoordinate(2, -3), 0.02f));
    }

    [Fact]
    public void Reference_center_fifteen_meters_off_in_x_excludes_the_zero_cell()
    {
        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(0.5f);

        var coordinates = footprint.GetAllowedCellCoordinates(
            referenceCoordinateX: 0,
            referenceCoordinateY: 0,
            referenceCenterX: 15f,
            referenceCenterZ: 0f);

        Assert.DoesNotContain(new FishWarehouseGridCellCoordinate(0, 0), coordinates);
    }

    [Theory]
    [InlineData(-10.25f, -6f, -21, -12)]
    [InlineData(-0.25f, 0.5f, -1, 1)]
    [InlineData(10.75f, 6f, 21, 12)]
    public void Maps_property_local_build_points_to_the_fixed_native_coordinates(
        float pointX,
        float pointZ,
        int expectedX,
        int expectedY)
    {
        Assert.True(
            FishWarehouseBuildableFootprintDefinition.TryMapLocalPointToCoordinate(
                new FishWarehousePoint(pointX, 0f, pointZ),
                new FishWarehousePoint(
                    FishWarehouseBuildableFootprintDefinition.GridOriginX,
                    0f,
                    FishWarehouseBuildableFootprintDefinition.GridOriginZ),
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
