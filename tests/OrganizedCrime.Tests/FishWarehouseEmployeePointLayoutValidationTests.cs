using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseEmployeePointLayoutValidationTests
{
    [Fact]
    public void Authoritative_layout_has_no_failures_and_allows_outside_points()
    {
        var result = FishWarehouseEmployeePointLayoutValidator.Validate(
            FishWarehouseEmployeeInfrastructureDefinition.Capacity,
            FishWarehouseEmployeeInfrastructureDefinition.IdlePoints,
            FishWarehouseEmployeePointLayoutRules.Default);

        Assert.True(result.Succeeded, string.Join(", ", result.FailureReasons));
        Assert.Empty(result.FailureReasons);
    }

    [Fact]
    public void Nine_or_fewer_points_fail_with_too_few()
    {
        var result = Validate(FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.Take(9));

        Assert.False(result.Succeeded);
        Assert.Contains("too-few-idle-points", result.FailureReasons);
        Assert.Contains("capacity-count-mismatch", result.FailureReasons);
    }

    [Fact]
    public void More_than_ten_points_fail_with_too_many()
    {
        var points = FishWarehouseEmployeeInfrastructureDefinition.IdlePoints
            .Append(new FishWarehouseEmployeePointDefinition(
                "OC_FishWarehouse_EmployeeIdle_10",
                -3f,
                0f,
                12.5f));

        var result = Validate(points);

        Assert.False(result.Succeeded);
        Assert.Contains("too-many-idle-points", result.FailureReasons);
    }

    [Fact]
    public void Null_and_malformed_inputs_fail_closed_with_named_reasons()
    {
        var missing = FishWarehouseEmployeePointLayoutValidator.Validate(
            10,
            null,
            FishWarehouseEmployeePointLayoutRules.Default);
        var malformed = Validate(FishWarehouseEmployeeInfrastructureDefinition.IdlePoints
            .Take(9)
            .Append(null!));
        var malformedName = Validate(FishWarehouseEmployeeInfrastructureDefinition.IdlePoints
            .Select((point, index) => index == 0 ? point with { Name = null! } : point));

        Assert.False(missing.Succeeded);
        Assert.Contains("missing-idle-points", missing.FailureReasons);
        Assert.False(malformed.Succeeded);
        Assert.Contains("malformed-idle-point", malformed.FailureReasons);
        Assert.False(malformedName.Succeeded);
        Assert.Contains("malformed-idle-point", malformedName.FailureReasons);
    }

    [Fact]
    public void Duplicate_names_and_wrong_ordered_indices_fail_with_named_reasons()
    {
        var duplicate = FishWarehouseEmployeeInfrastructureDefinition.IdlePoints
            .Select((point, index) => index == 9
                ? point with { Name = FishWarehouseEmployeeInfrastructureDefinition.IdlePoints[0].Name }
                : point);
        var wrongOrder = FishWarehouseEmployeeInfrastructureDefinition.IdlePoints
            .Reverse()
            .ToArray();

        var duplicateResult = Validate(duplicate);
        var orderResult = Validate(wrongOrder);

        Assert.Contains("duplicate-idle-point-name", duplicateResult.FailureReasons);
        Assert.Contains("idle-point-order", orderResult.FailureReasons);
    }

    [Fact]
    public void Non_finite_coordinates_fail_with_named_reason()
    {
        var points = FishWarehouseEmployeeInfrastructureDefinition.IdlePoints
            .Select((point, index) => index == 4 ? point with { X = float.NaN } : point);

        var result = Validate(points);

        Assert.False(result.Succeeded);
        Assert.Contains("non-finite-idle-point", result.FailureReasons);
    }

    [Fact]
    public void Points_that_are_too_close_fail_with_named_spacing_reason()
    {
        var points = FishWarehouseEmployeeInfrastructureDefinition.IdlePoints
            .Select((point, index) => index == 9
                ? point with { X = -4.42f, Z = 10.04f }
                : point);

        var result = Validate(points);

        Assert.False(result.Succeeded);
        Assert.Contains("idle-point-spacing", result.FailureReasons);
    }

    [Fact]
    public void Wall_and_aperture_exclusions_fail_with_named_reasons()
    {
        var wallRules = RulesWith(
            wallExclusions: new[] { new FishWarehouseEmployeePointExclusionRegion("west-wall", -8f, -7f, 1f, 2f) });
        var apertureRules = RulesWith(
            apertureExclusions: new[] { new FishWarehouseEmployeePointExclusionRegion("garage-aperture", -8f, -7f, 1f, 2f) });

        var wallPoints = ReplacePoint(4, -7.5f, 0f, 1.5f);
        var aperturePoints = ReplacePoint(4, -7.5f, 0f, 1.5f);

        var wallResult = FishWarehouseEmployeePointLayoutValidator.Validate(10, wallPoints, wallRules);
        var apertureResult = FishWarehouseEmployeePointLayoutValidator.Validate(10, aperturePoints, apertureRules);

        Assert.Contains("wall-clearance", wallResult.FailureReasons);
        Assert.Contains("aperture-clearance", apertureResult.FailureReasons);
    }

    [Fact]
    public void Malformed_rules_fail_closed()
    {
        var rules = RulesWith(minimumPairwiseSpacing: float.NaN);

        var result = FishWarehouseEmployeePointLayoutValidator.Validate(
            10,
            FishWarehouseEmployeeInfrastructureDefinition.IdlePoints,
            rules);

        Assert.False(result.Succeeded);
        Assert.Contains("malformed-layout-rules", result.FailureReasons);
    }

    private static FishWarehouseEmployeePointLayoutValidationResult Validate(
        IEnumerable<FishWarehouseEmployeePointDefinition?> points) =>
        FishWarehouseEmployeePointLayoutValidator.Validate(
            FishWarehouseEmployeeInfrastructureDefinition.Capacity,
            points,
            FishWarehouseEmployeePointLayoutRules.Default);

    private static IReadOnlyList<FishWarehouseEmployeePointDefinition?> ReplacePoint(
        int index,
        float x,
        float y,
        float z) =>
        FishWarehouseEmployeeInfrastructureDefinition.IdlePoints
            .Select((point, pointIndex) => pointIndex == index
                ? point with { X = x, Y = y, Z = z }
                : point)
            .Cast<FishWarehouseEmployeePointDefinition?>()
            .ToArray();

    private static FishWarehouseEmployeePointLayoutRules RulesWith(
        float minimumPairwiseSpacing = 0.75f,
        IReadOnlyList<FishWarehouseEmployeePointExclusionRegion>? wallExclusions = null,
        IReadOnlyList<FishWarehouseEmployeePointExclusionRegion>? apertureExclusions = null) =>
        new(
            minimumPairwiseSpacing,
            wallExclusions ?? Array.Empty<FishWarehouseEmployeePointExclusionRegion>(),
            apertureExclusions ?? Array.Empty<FishWarehouseEmployeePointExclusionRegion>());
}
