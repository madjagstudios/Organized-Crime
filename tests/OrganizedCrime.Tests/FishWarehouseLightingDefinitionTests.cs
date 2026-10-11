using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseLightingDefinitionTests
{
    [Fact]
    public void Default_has_four_lamps_at_the_expected_positions()
    {
        var lamps = FishWarehouseLightingDefinition.Default.Lamps;

        Assert.Equal(4, lamps.Count);
        Assert.Contains(lamps, lamp => lamp.X == -5.75f && lamp.Z == -3.45f);
        Assert.Contains(lamps, lamp => lamp.X == -5.75f && lamp.Z == 3.45f);
        Assert.Contains(lamps, lamp => lamp.X == 5.75f && lamp.Z == -3.45f);
        Assert.Contains(lamps, lamp => lamp.X == 5.75f && lamp.Z == 3.45f);
        Assert.All(lamps, lamp =>
        {
            Assert.Equal(4.5f, lamp.HeightY);
            Assert.Equal(0, lamp.Yaw);
            Assert.Equal(2f, lamp.Intensity);
            Assert.Equal(7f, lamp.Range);
        });
    }

    [Fact]
    public void Default_layout_passes_validation()
    {
        Assert.True(FishWarehouseLightingDefinition.TryCreate(
            FishWarehouseLightingDefinition.Default.Lamps,
            out var definition));
        Assert.NotNull(definition);
        Assert.Equal(4, definition!.Lamps.Count);
    }

    [Theory]
    [InlineData(10.6f, 0f, 0, true)] // Bar half-length 1.0 touches the east wall.
    [InlineData(10.61f, 0f, 0, false)]
    [InlineData(0f, 6.65f, 0, true)] // Bar half-width 0.25 touches the north wall.
    [InlineData(0f, 6.66f, 0, false)]
    [InlineData(11.35f, 0f, 90, true)] // Yaw 90 swaps length and width.
    [InlineData(11.36f, 0f, 90, false)]
    [InlineData(0f, 5.9f, 90, true)]
    [InlineData(0f, 5.91f, 90, false)]
    public void Lamp_footprint_must_fit_inside_the_walls(float x, float z, int yaw, bool expected)
    {
        var lamp = new FishWarehouseLightingLamp(x, z, 4.5f, yaw, 2f, 7f);

        Assert.Equal(expected, FishWarehouseLightingDefinition.TryCreate(new[] { lamp }, out _));
    }

    [Theory]
    [InlineData(5.49f, true)]
    [InlineData(5.5f, false)] // CeilingY 5.8 minus the 0.3 allowance is exclusive.
    [InlineData(5.8f, false)]
    public void Lamp_height_must_stay_below_the_ceiling_allowance(float height, bool expected)
    {
        var lamp = new FishWarehouseLightingLamp(0f, 0f, height, 0, 2f, 7f);

        Assert.Equal(expected, FishWarehouseLightingDefinition.TryCreate(new[] { lamp }, out _));
    }

    [Fact]
    public void Lamps_must_not_overlap()
    {
        var lamp = new FishWarehouseLightingLamp(0f, 0f, 4.5f, 0, 2f, 7f);

        Assert.False(FishWarehouseLightingDefinition.TryCreate(new[] { lamp, lamp }, out _));
    }

    [Theory]
    [InlineData(2f, true)] // Touching edges share no area.
    [InlineData(1.99f, false)]
    public void Lamp_spacing_is_measured_edge_to_edge(float xDistance, bool expected)
    {
        var left = new FishWarehouseLightingLamp(-xDistance / 2f, 0f, 4.5f, 0, 2f, 7f);
        var right = new FishWarehouseLightingLamp(xDistance / 2f, 0f, 4.5f, 0, 2f, 7f);

        Assert.Equal(expected, FishWarehouseLightingDefinition.TryCreate(new[] { left, right }, out _));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(90, true)]
    [InlineData(45, false)]
    [InlineData(180, false)]
    public void Yaw_must_be_zero_or_ninety(int yaw, bool expected)
    {
        var lamp = new FishWarehouseLightingLamp(0f, 0f, 4.5f, yaw, 2f, 7f);

        Assert.Equal(expected, FishWarehouseLightingDefinition.TryCreate(new[] { lamp }, out _));
    }

    [Theory]
    [InlineData(0f, 7f, false)]
    [InlineData(2f, 0f, false)]
    [InlineData(-1f, 7f, false)]
    [InlineData(2f, -7f, false)]
    [InlineData(0.25f, 7f, true)]
    [InlineData(6f, 7f, true)]
    public void Light_parameters_must_be_positive(float intensity, float range, bool expected)
    {
        var lamp = new FishWarehouseLightingLamp(0f, 0f, 4.5f, 0, intensity, range);

        Assert.Equal(expected, FishWarehouseLightingDefinition.TryCreate(new[] { lamp }, out _));
    }

    [Fact]
    public void Rejects_non_finite_lamp_values()
    {
        var lamp = new FishWarehouseLightingLamp(float.NaN, 0f, 4.5f, 0, 2f, 7f);

        Assert.False(FishWarehouseLightingDefinition.TryCreate(new[] { lamp }, out _));
    }

    [Fact]
    public void Rejects_an_empty_layout()
    {
        Assert.False(FishWarehouseLightingDefinition.TryCreate(
            Array.Empty<FishWarehouseLightingLamp>(),
            out _));
    }
}
