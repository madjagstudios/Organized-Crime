using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3CheckpointPlacementTests
{
    [Fact]
    public void Symmetric_halves_need_no_correction()
    {
        var correction = Chapter3CheckpointPlacement.CorrectBarrierMidpoint(
            spotX: 10f, spotZ: 20f, rootX: 10f, rootZ: 20f,
            half1X: 8f, half1Z: 20f, half2X: 12f, half2Z: 20f);

        Assert.Equal(10f, correction.CorrectedX);
        Assert.Equal(20f, correction.CorrectedZ);
        Assert.Equal(0f, correction.LateralMetres);
    }

    [Fact]
    public void Offset_midpoint_shifts_the_root_so_the_midpoint_lands_on_the_spot()
    {
        var correction = Chapter3CheckpointPlacement.CorrectBarrierMidpoint(
            spotX: 10f, spotZ: 20f, rootX: 10f, rootZ: 20f,
            half1X: 10f, half1Z: 20f, half2X: 14f, half2Z: 20f);

        Assert.Equal(8f, correction.CorrectedX);
        Assert.Equal(20f, correction.CorrectedZ);
        Assert.Equal(2f, correction.LateralMetres, precision: 5);

        var shiftX = correction.CorrectedX - 10f;
        var shiftZ = correction.CorrectedZ - 20f;
        Assert.Equal(10f, (10f + shiftX + 14f + shiftX) / 2f, precision: 5);
        Assert.Equal(20f, (20f + shiftZ + 20f + shiftZ) / 2f, precision: 5);
    }

    [Fact]
    public void Root_starting_off_the_spot_still_lands_the_midpoint_on_it()
    {
        var correction = Chapter3CheckpointPlacement.CorrectBarrierMidpoint(
            spotX: 0f, spotZ: 0f, rootX: 1f, rootZ: -1f,
            half1X: 3f, half1Z: 1f, half2X: 5f, half2Z: 3f);

        var shiftX = correction.CorrectedX - 1f;
        var shiftZ = correction.CorrectedZ - -1f;
        Assert.Equal(0f, (3f + shiftX + 5f + shiftX) / 2f, precision: 5);
        Assert.Equal(0f, (1f + shiftZ + 3f + shiftZ) / 2f, precision: 5);
    }

    [Fact]
    public void Coincident_halves_align_their_centre()
    {
        var correction = Chapter3CheckpointPlacement.CorrectBarrierMidpoint(
            spotX: 10f, spotZ: 20f, rootX: 10f, rootZ: 20f,
            half1X: 13f, half1Z: 24f, half2X: 13f, half2Z: 24f);

        Assert.Equal(7f, correction.CorrectedX);
        Assert.Equal(16f, correction.CorrectedZ);
        Assert.Equal(5f, correction.LateralMetres, precision: 4);
    }

    [Fact]
    public void Nonfinite_inputs_apply_no_correction()
    {
        var correction = Chapter3CheckpointPlacement.CorrectBarrierMidpoint(
            spotX: 10f, spotZ: 20f, rootX: 10f, rootZ: 20f,
            half1X: float.NaN, half1Z: 20f, half2X: 12f, half2Z: 20f);

        Assert.Equal(10f, correction.CorrectedX);
        Assert.Equal(20f, correction.CorrectedZ);
        Assert.Equal(0f, correction.LateralMetres);
    }
}
