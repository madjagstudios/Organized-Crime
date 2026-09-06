namespace OrganizedCrime.Model;

public sealed record FishWarehouseEmployeePointExclusionRegion(
    string Name,
    float MinimumX,
    float MaximumX,
    float MinimumZ,
    float MaximumZ)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) &&
        float.IsFinite(MinimumX) &&
        float.IsFinite(MaximumX) &&
        float.IsFinite(MinimumZ) &&
        float.IsFinite(MaximumZ) &&
        MinimumX < MaximumX &&
        MinimumZ < MaximumZ;

    public bool Contains(float x, float z) =>
        IsValid &&
        x >= MinimumX &&
        x <= MaximumX &&
        z >= MinimumZ &&
        z <= MaximumZ;
}

public sealed record FishWarehouseEmployeePointLayoutRules(
    float MinimumPairwiseSpacing,
    IReadOnlyList<FishWarehouseEmployeePointExclusionRegion> WallExclusions,
    IReadOnlyList<FishWarehouseEmployeePointExclusionRegion> ApertureExclusions)
{
    public static FishWarehouseEmployeePointLayoutRules Default { get; } = new(
        MinimumPairwiseSpacing: 0.75f,
        WallExclusions: new[]
        {
            new FishWarehouseEmployeePointExclusionRegion(
                "west-wall-clearance",
                -FishWarehouseInteriorRoomDefinition.HalfWidth,
                -FishWarehouseInteriorRoomDefinition.HalfWidth + FishWarehouseEmployeeInfrastructureDefinition.WallMargin,
                -FishWarehouseInteriorRoomDefinition.HalfDepth,
                FishWarehouseInteriorRoomDefinition.HalfDepth),
            new FishWarehouseEmployeePointExclusionRegion(
                "east-wall-clearance",
                FishWarehouseInteriorRoomDefinition.HalfWidth - FishWarehouseEmployeeInfrastructureDefinition.WallMargin,
                FishWarehouseInteriorRoomDefinition.HalfWidth,
                -FishWarehouseInteriorRoomDefinition.HalfDepth,
                FishWarehouseInteriorRoomDefinition.HalfDepth),
            new FishWarehouseEmployeePointExclusionRegion(
                "south-wall-clearance",
                -FishWarehouseInteriorRoomDefinition.HalfWidth,
                FishWarehouseInteriorRoomDefinition.HalfWidth,
                -FishWarehouseInteriorRoomDefinition.HalfDepth,
                -FishWarehouseInteriorRoomDefinition.HalfDepth + FishWarehouseEmployeeInfrastructureDefinition.WallMargin),
            new FishWarehouseEmployeePointExclusionRegion(
                "north-wall-clearance",
                -FishWarehouseInteriorRoomDefinition.HalfWidth,
                FishWarehouseInteriorRoomDefinition.HalfWidth,
                FishWarehouseInteriorRoomDefinition.HalfDepth - FishWarehouseEmployeeInfrastructureDefinition.WallMargin,
                FishWarehouseInteriorRoomDefinition.HalfDepth)
        },
        ApertureExclusions: new[]
        {
            new FishWarehouseEmployeePointExclusionRegion(
                "garage-aperture-clearance",
                -FishWarehouseInteriorRoomDefinition.HalfWidth,
                FishWarehouseInteriorRoomDefinition.HalfWidth,
                FishWarehouseInteriorRoomDefinition.HalfDepth,
                FishWarehouseInteriorRoomDefinition.HalfDepth + 0.90f),
            new FishWarehouseEmployeePointExclusionRegion(
                "personnel-aperture-clearance",
                -FishWarehouseInteriorRoomDefinition.HalfWidth,
                FishWarehouseInteriorRoomDefinition.HalfWidth,
                -FishWarehouseInteriorRoomDefinition.HalfDepth - 0.90f,
                -FishWarehouseInteriorRoomDefinition.HalfDepth)
        });
}

public sealed record FishWarehouseEmployeePointLayoutValidationResult(
    bool Succeeded,
    IReadOnlyList<string> FailureReasons);

public static class FishWarehouseEmployeePointLayoutValidator
{
    private const string IdlePointNamePrefix = "OC_FishWarehouse_EmployeeIdle_";

    public static FishWarehouseEmployeePointLayoutValidationResult Validate(
        int capacity,
        IEnumerable<FishWarehouseEmployeePointDefinition?>? points,
        FishWarehouseEmployeePointLayoutRules? rules)
    {
        var failures = new List<string>();
        AddFailureIf(failures, capacity <= 0, "invalid-capacity");

        if (!IsValidRules(rules))
            AddFailure(failures, "malformed-layout-rules");

        if (points is null)
        {
            AddFailure(failures, "missing-idle-points");
            return Complete(failures);
        }

        FishWarehouseEmployeePointDefinition?[] pointArray;
        try
        {
            pointArray = points.ToArray();
        }
        catch (Exception)
        {
            AddFailure(failures, "malformed-idle-points");
            return Complete(failures);
        }

        if (pointArray.Length < capacity)
            AddFailure(failures, "too-few-idle-points");
        if (pointArray.Length > capacity)
            AddFailure(failures, "too-many-idle-points");
        if (pointArray.Length != capacity)
            AddFailure(failures, "capacity-count-mismatch");

        for (var index = 0; index < pointArray.Length; index++)
        {
            FishWarehouseEmployeePointDefinition? point = pointArray[index];
            if (point is null)
            {
                AddFailure(failures, "malformed-idle-point");
                continue;
            }

            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                AddFailure(failures, "non-finite-idle-point");

            if (string.IsNullOrWhiteSpace(point.Name))
            {
                AddFailure(failures, "malformed-idle-point");
                continue;
            }

            if (!TryGetIndex(point.Name, out var actualIndex) || actualIndex < 0 || actualIndex >= capacity)
                AddFailure(failures, "invalid-idle-point-index");

            if (point.Name != GetExpectedName(index))
                AddFailure(failures, "idle-point-order");
        }

        var duplicateName = pointArray
            .Where(point => point is not null && !string.IsNullOrWhiteSpace(point.Name))
            .GroupBy(point => point!.Name, StringComparer.Ordinal)
            .Any(group => group.Count() > 1);
        AddFailureIf(failures, duplicateName, "duplicate-idle-point-name");

        if (IsValidRules(rules))
        {
            for (var left = 0; left < pointArray.Length; left++)
            {
                if (!HasFiniteCoordinates(pointArray[left]))
                    continue;

                for (var right = left + 1; right < pointArray.Length; right++)
                {
                    if (!HasFiniteCoordinates(pointArray[right]))
                        continue;

                    var dx = pointArray[left]!.X - pointArray[right]!.X;
                    var dz = pointArray[left]!.Z - pointArray[right]!.Z;
                    var distanceSquared = (dx * dx) + (dz * dz);
                    if (distanceSquared < rules!.MinimumPairwiseSpacing * rules.MinimumPairwiseSpacing)
                        AddFailure(failures, "idle-point-spacing");
                }
            }

            foreach (FishWarehouseEmployeePointDefinition point in pointArray.OfType<FishWarehouseEmployeePointDefinition>())
            {
                if (!HasFiniteCoordinates(point))
                    continue;

                if (rules!.WallExclusions.Any(region => region.Contains(point.X, point.Z)))
                    AddFailure(failures, "wall-clearance");
                if (rules.ApertureExclusions.Any(region => region.Contains(point.X, point.Z)))
                    AddFailure(failures, "aperture-clearance");
            }
        }

        return Complete(failures);
    }

    private static bool IsValidRules(FishWarehouseEmployeePointLayoutRules? rules) =>
        rules is not null &&
        float.IsFinite(rules.MinimumPairwiseSpacing) &&
        rules.MinimumPairwiseSpacing > 0f &&
        IsValidRegions(rules.WallExclusions) &&
        IsValidRegions(rules.ApertureExclusions);

    private static bool IsValidRegions(IReadOnlyList<FishWarehouseEmployeePointExclusionRegion>? regions) =>
        regions is not null && regions.All(region => region is not null && region.IsValid);

    private static bool HasFiniteCoordinates(FishWarehouseEmployeePointDefinition? point) =>
        point is not null &&
        float.IsFinite(point.X) &&
        float.IsFinite(point.Y) &&
        float.IsFinite(point.Z);

    private static string GetExpectedName(int index) => $"{IdlePointNamePrefix}{index}";

    private static bool TryGetIndex(string name, out int index)
    {
        index = -1;
        return name.StartsWith(IdlePointNamePrefix, StringComparison.Ordinal) &&
               int.TryParse(name[IdlePointNamePrefix.Length..], out index);
    }

    private static void AddFailureIf(ICollection<string> failures, bool condition, string reason)
    {
        if (condition)
            AddFailure(failures, reason);
    }

    private static void AddFailure(ICollection<string> failures, string reason)
    {
        if (!failures.Contains(reason))
            failures.Add(reason);
    }

    private static FishWarehouseEmployeePointLayoutValidationResult Complete(List<string> failures) =>
        new(failures.Count == 0, failures.AsReadOnly());
}
