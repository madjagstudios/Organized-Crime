namespace OrganizedCrime.Model;

public enum FishWarehouseNativeNavigationGraphRole
{
    Default,
    Employee
}

public enum FishWarehouseNativeNavigationPathStatus
{
    PathComplete,
    PathPartial,
    PathInvalid
}

public readonly record struct FishWarehouseNativeNavigationProbe(
    FishWarehouseNativeNavigationGraphRole GraphRole,
    int AgentTypeId,
    string TargetName,
    bool Required,
    bool TargetPresent,
    bool StartSampled,
    bool EndSampled,
    FishWarehouseNativeNavigationPathStatus PathStatus);

public readonly record struct FishWarehouseNativeNavigationAcceptedRequirement(
    int AgentTypeId,
    string TargetName,
    FishWarehouseNativeNavigationGraphRole GraphRole);

public sealed record FishWarehouseNativeNavigationAcceptanceResult(
    bool Succeeded,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Failures,
    IReadOnlyList<FishWarehouseNativeNavigationAcceptedRequirement> AcceptedRequirements)
{
    public int RequiredResourceCount => AcceptedRequirements.Count;
}

public static class FishWarehouseNativeNavigationAcceptanceDefinition
{
    public const string EmployeeExteriorToDoorwayTargetName = "employee-exterior-to-doorway";
    public const string DefaultExteriorToDoorwayTargetName = "default-exterior-to-doorway";

    private static IReadOnlyList<(FishWarehouseNativeNavigationGraphRole GraphRole, string TargetName)>
        RequiredPathInventory { get; } = CreateRequiredPathInventory();

    public static string DoorwayToIdleTargetName(int index)
    {
        if (index < 0 || index >= FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"The Fish Warehouse has {FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.Count} mandatory idle points.");
        }

        return $"doorway-to-idle-{index}";
    }

    private static IReadOnlyList<(FishWarehouseNativeNavigationGraphRole GraphRole, string TargetName)>
        CreateRequiredPathInventory()
    {
        var inventory = new List<(FishWarehouseNativeNavigationGraphRole GraphRole, string TargetName)>
        {
            (FishWarehouseNativeNavigationGraphRole.Employee, EmployeeExteriorToDoorwayTargetName)
        };
        for (int index = 0; index < FishWarehouseEmployeeInfrastructureDefinition.IdlePoints.Count; index++)
            inventory.Add((FishWarehouseNativeNavigationGraphRole.Employee, DoorwayToIdleTargetName(index)));

        inventory.Add((FishWarehouseNativeNavigationGraphRole.Default, DefaultExteriorToDoorwayTargetName));
        return inventory;
    }

    public static FishWarehouseNativeNavigationAcceptanceResult Evaluate(
        IEnumerable<FishWarehouseNativeNavigationProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);

        var warnings = new List<string>();
        var failures = new List<string>();
        var acceptedRequirements = new HashSet<FishWarehouseNativeNavigationAcceptedRequirement>();
        var presentRequiredProbes = new HashSet<(FishWarehouseNativeNavigationGraphRole GraphRole, string TargetName)>();

        foreach (FishWarehouseNativeNavigationProbe probe in probes)
        {
            if (probe.Required)
            {
                presentRequiredProbes.Add((probe.GraphRole, probe.TargetName));
            }

            if (!probe.TargetPresent)
            {
                AddIssue(
                    probe,
                    $"target={probe.TargetName} is unavailable",
                    warnings,
                    failures);
                continue;
            }

            if (!probe.StartSampled)
            {
                AddIssue(
                    probe,
                    $"target={probe.TargetName} start endpoint was not sampled",
                    warnings,
                    failures);
            }

            if (!probe.EndSampled)
            {
                AddIssue(
                    probe,
                    $"target={probe.TargetName} end endpoint was not sampled",
                    warnings,
                    failures);
            }

            if (probe.PathStatus != FishWarehouseNativeNavigationPathStatus.PathComplete)
            {
                AddIssue(
                    probe,
                    $"target={probe.TargetName} path status={probe.PathStatus}",
                    warnings,
                    failures);
            }

            if (probe.Required && probe.StartSampled && probe.EndSampled &&
                probe.PathStatus == FishWarehouseNativeNavigationPathStatus.PathComplete)
            {
                var acceptedRequirement = new FishWarehouseNativeNavigationAcceptedRequirement(
                    probe.AgentTypeId,
                    probe.TargetName,
                    probe.GraphRole);
                if (probe.AgentTypeId == 0)
                {
                    if (!acceptedRequirements.Any(requirement =>
                            requirement.AgentTypeId == 0 &&
                            requirement.TargetName == probe.TargetName))
                    {
                        acceptedRequirements.Add(acceptedRequirement);
                    }
                }
                else
                {
                    acceptedRequirements.Add(acceptedRequirement);
                }
            }
        }

        foreach ((FishWarehouseNativeNavigationGraphRole graphRole, string targetName) in RequiredPathInventory)
        {
            if (!presentRequiredProbes.Contains((graphRole, targetName)))
            {
                failures.Add(
                    $"missing required probe graph={graphRole.ToString().ToLowerInvariant()} target={targetName}");
            }
        }

        return new FishWarehouseNativeNavigationAcceptanceResult(
            failures.Count == 0,
            warnings,
            failures,
            acceptedRequirements
                .OrderBy(requirement => requirement.AgentTypeId)
                .ThenBy(requirement => requirement.TargetName, StringComparer.Ordinal)
                .ToArray());
    }

    private static void AddIssue(
        FishWarehouseNativeNavigationProbe probe,
        string message,
        ICollection<string> warnings,
        ICollection<string> failures)
    {
        string issue =
            $"graph={probe.GraphRole.ToString().ToLowerInvariant()} required={probe.Required} {message}";
        if (probe.Required)
            failures.Add(issue);
        else
            warnings.Add(issue);
    }
}
