using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseNativeNavigationAcceptanceDefinitionTests
{
    [Fact]
    public void Exposes_the_mandatory_exterior_path_identifiers_for_runtime_consumers()
    {
        Assert.Equal(
            "employee-exterior-to-doorway",
            FishWarehouseNativeNavigationAcceptanceDefinition.EmployeeExteriorToDoorwayTargetName);
        Assert.Equal(
            "default-exterior-to-doorway",
            FishWarehouseNativeNavigationAcceptanceDefinition.DefaultExteriorToDoorwayTargetName);
    }

    [Fact]
    public void Exposes_each_authoritative_doorway_to_idle_identifier_in_order()
    {
        for (int index = 0; index < FishWarehouseEmployeeInfrastructureDefinition.Capacity; index++)
        {
            Assert.Equal(
                $"doorway-to-idle-{index}",
                FishWarehouseNativeNavigationAcceptanceDefinition.DoorwayToIdleTargetName(index));
        }
    }

    [Theory]
    [InlineData(-1)]
    public void Rejects_negative_doorway_to_idle_indices(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FishWarehouseNativeNavigationAcceptanceDefinition.DoorwayToIdleTargetName(index));
    }

    [Fact]
    public void Rejects_the_first_doorway_to_idle_index_after_the_authoritative_inventory()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FishWarehouseNativeNavigationAcceptanceDefinition.DoorwayToIdleTargetName(
                FishWarehouseEmployeeInfrastructureDefinition.Capacity));
    }

    [Fact]
    public void Requires_employee_exterior_doorway_and_all_authoritative_doorway_to_idle_paths()
    {
        List<FishWarehouseNativeNavigationProbe> probes = CompleteRequiredPathSet();

        FishWarehouseNativeNavigationAcceptanceResult result =
            FishWarehouseNativeNavigationAcceptanceDefinition.Evaluate(probes);

        Assert.True(result.Succeeded);
        Assert.Equal(FishWarehouseEmployeeInfrastructureDefinition.Capacity + 2, result.RequiredResourceCount);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Requires_the_default_graph_exterior_to_doorway_path()
    {
        List<FishWarehouseNativeNavigationProbe> probes = CompleteRequiredPathSet();
        FishWarehouseNativeNavigationAcceptanceResult result =
            FishWarehouseNativeNavigationAcceptanceDefinition.Evaluate(probes);

        Assert.True(result.Succeeded);
        Assert.Equal(FishWarehouseEmployeeInfrastructureDefinition.Capacity + 2, result.RequiredResourceCount);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Rejects_an_empty_probe_set_with_each_named_mandatory_path_missing()
    {
        FishWarehouseNativeNavigationAcceptanceResult result =
            FishWarehouseNativeNavigationAcceptanceDefinition.Evaluate(
                Array.Empty<FishWarehouseNativeNavigationProbe>());

        Assert.False(result.Succeeded);
        Assert.Equal(FishWarehouseEmployeeInfrastructureDefinition.Capacity + 2, result.Failures.Count);
        Assert.Contains(result.Failures, failure =>
            failure.Contains(
                "missing required probe graph=employee target=employee-exterior-to-doorway",
                StringComparison.Ordinal));
        for (int index = 0; index < FishWarehouseEmployeeInfrastructureDefinition.Capacity; index++)
        {
            string targetName = $"doorway-to-idle-{index}";
            Assert.Contains(result.Failures, failure =>
                failure.Contains(
                    $"missing required probe graph=employee target={targetName}",
                    StringComparison.Ordinal));
        }
        Assert.Contains(result.Failures, failure =>
            failure.Contains(
                "missing required probe graph=default target=default-exterior-to-doorway",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_partial_required_probe_set_with_the_missing_default_path_named()
    {
        var probes = new List<FishWarehouseNativeNavigationProbe>
        {
            CompleteProbe(FishWarehouseNativeNavigationGraphRole.Employee, "employee-exterior-to-doorway")
        };
        probes.AddRange(CompleteRequiredIdleProbes());

        FishWarehouseNativeNavigationAcceptanceResult result =
            FishWarehouseNativeNavigationAcceptanceDefinition.Evaluate(probes);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures, failure =>
            failure.Contains(
                "missing required probe graph=default target=default-exterior-to-doorway",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Warns_with_named_optional_target_failures_without_failing_the_build()
    {
        List<FishWarehouseNativeNavigationProbe> probes = CompleteRequiredPathSet();
        probes.Add(MissingOptionalProbe("employee-locker"));
        probes.Add(UnreachableOptionalProbe("packaging-station"));

        FishWarehouseNativeNavigationAcceptanceResult result =
            FishWarehouseNativeNavigationAcceptanceDefinition.Evaluate(probes);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Failures);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("employee-locker", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("packaging-station", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(FishWarehouseNativeNavigationPathStatus.PathPartial)]
    [InlineData(FishWarehouseNativeNavigationPathStatus.PathInvalid)]
    public void Fails_required_probes_when_endpoint_sampling_or_path_status_is_not_complete(
        FishWarehouseNativeNavigationPathStatus pathStatus)
    {
        FishWarehouseNativeNavigationProbe probe = CompleteProbe(
            FishWarehouseNativeNavigationGraphRole.Employee,
            "doorway-to-idle-0") with
        {
            EndSampled = false,
            PathStatus = pathStatus
        };

        FishWarehouseNativeNavigationAcceptanceResult result =
            FishWarehouseNativeNavigationAcceptanceDefinition.Evaluate(new[] { probe });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures, failure =>
            failure.Contains("doorway-to-idle-0", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure =>
            failure.Contains(pathStatus.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void Reuses_one_successful_agent_zero_graph_requirement_for_employee_and_default_roles()
    {
        List<FishWarehouseNativeNavigationProbe> probes = CompleteRequiredPathSet();
        probes.Add(CompleteProbe(
            FishWarehouseNativeNavigationGraphRole.Employee,
            "exterior-to-doorway",
            agentTypeId: 0));
        probes.Add(CompleteProbe(
            FishWarehouseNativeNavigationGraphRole.Default,
            "exterior-to-doorway",
            agentTypeId: 0));

        FishWarehouseNativeNavigationAcceptanceResult result =
            FishWarehouseNativeNavigationAcceptanceDefinition.Evaluate(probes);

        Assert.True(result.Succeeded);
        Assert.Equal(FishWarehouseEmployeeInfrastructureDefinition.Capacity + 3, result.RequiredResourceCount);
        Assert.Contains(result.AcceptedRequirements, requirement =>
            requirement.AgentTypeId == 0 && requirement.TargetName == "exterior-to-doorway");
    }

    [Fact]
    public void Keeps_same_named_employee_and_default_requirements_distinct_for_nonzero_agent_types()
    {
        List<FishWarehouseNativeNavigationProbe> probes = CompleteRequiredPathSet();
        probes.Add(CompleteProbe(
            FishWarehouseNativeNavigationGraphRole.Employee,
            "exterior-to-doorway",
            agentTypeId: 7));
        probes.Add(CompleteProbe(
            FishWarehouseNativeNavigationGraphRole.Default,
            "exterior-to-doorway",
            agentTypeId: 7));

        FishWarehouseNativeNavigationAcceptanceResult result =
            FishWarehouseNativeNavigationAcceptanceDefinition.Evaluate(probes);

        Assert.True(result.Succeeded);
        Assert.Equal(FishWarehouseEmployeeInfrastructureDefinition.Capacity + 4, result.RequiredResourceCount);
        Assert.Equal(FishWarehouseEmployeeInfrastructureDefinition.Capacity + 4, result.AcceptedRequirements.Count);
    }

    private static List<FishWarehouseNativeNavigationProbe> CompleteRequiredPathSet() =>
        new[]
        {
            CompleteProbe(FishWarehouseNativeNavigationGraphRole.Employee, "employee-exterior-to-doorway")
        }
        .Concat(CompleteRequiredIdleProbes())
        .Append(CompleteProbe(
            FishWarehouseNativeNavigationGraphRole.Default,
            "default-exterior-to-doorway"))
        .ToList();

    private static IEnumerable<FishWarehouseNativeNavigationProbe> CompleteRequiredIdleProbes() =>
        Enumerable.Range(0, FishWarehouseEmployeeInfrastructureDefinition.Capacity)
            .Select(index => CompleteProbe(
                FishWarehouseNativeNavigationGraphRole.Employee,
                $"doorway-to-idle-{index}"));

    private static FishWarehouseNativeNavigationProbe CompleteProbe(
        FishWarehouseNativeNavigationGraphRole graphRole,
        string targetName,
        int agentTypeId = 7) =>
        new(
            graphRole,
            agentTypeId,
            targetName,
            Required: true,
            TargetPresent: true,
            StartSampled: true,
            EndSampled: true,
            FishWarehouseNativeNavigationPathStatus.PathComplete);

    private static FishWarehouseNativeNavigationProbe MissingOptionalProbe(string targetName) =>
        new(
            FishWarehouseNativeNavigationGraphRole.Employee,
            7,
            targetName,
            Required: false,
            TargetPresent: false,
            StartSampled: false,
            EndSampled: false,
            FishWarehouseNativeNavigationPathStatus.PathInvalid);

    private static FishWarehouseNativeNavigationProbe UnreachableOptionalProbe(string targetName) =>
        new(
            FishWarehouseNativeNavigationGraphRole.Employee,
            7,
            targetName,
            Required: false,
            TargetPresent: true,
            StartSampled: true,
            EndSampled: true,
            FishWarehouseNativeNavigationPathStatus.PathPartial);
}
