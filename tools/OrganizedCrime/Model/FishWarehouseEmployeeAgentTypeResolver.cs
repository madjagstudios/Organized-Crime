namespace OrganizedCrime.Model;

public sealed record FishWarehouseNativeNavigationAgentSetting(int AgentTypeId, string Name);

public enum FishWarehouseEmployeeAgentTypeResolutionSource
{
    PersistedId,
    SettingsName,
    PropertyDonor,
    GlobalDonor
}

public sealed record FishWarehouseEmployeeAgentTypeResolution(
    int AgentTypeId,
    string ObservedSettingsName,
    FishWarehouseEmployeeAgentTypeResolutionSource Source);

public enum FishWarehouseEmployeeAgentTypeResolutionStatus
{
    Pending,
    Succeeded,
    Failed
}

public sealed record FishWarehouseEmployeeAgentTypeResolutionResult(
    FishWarehouseEmployeeAgentTypeResolutionStatus Status,
    FishWarehouseEmployeeAgentTypeResolution? Resolution,
    string? FailureReason)
{
    public static FishWarehouseEmployeeAgentTypeResolutionResult Pending() =>
        new(FishWarehouseEmployeeAgentTypeResolutionStatus.Pending, null, null);

    public static FishWarehouseEmployeeAgentTypeResolutionResult Succeeded(
        FishWarehouseEmployeeAgentTypeResolution resolution) =>
        new(FishWarehouseEmployeeAgentTypeResolutionStatus.Succeeded, resolution, null);

    public static FishWarehouseEmployeeAgentTypeResolutionResult Failed(string reason) =>
        new(FishWarehouseEmployeeAgentTypeResolutionStatus.Failed, null, reason);
}

public static class FishWarehouseEmployeeAgentTypeResolver
{
    private const string EmployeeSettingsName = "Employee";

    public static FishWarehouseEmployeeAgentTypeResolutionResult Resolve(
        int? persistedId,
        IReadOnlyList<FishWarehouseNativeNavigationAgentSetting> settings,
        IReadOnlyList<int> propertyDonorIds,
        IReadOnlyList<int> globalDonorIds)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(propertyDonorIds);
        ArgumentNullException.ThrowIfNull(globalDonorIds);

        if (persistedId is int persistedAgentTypeId)
        {
            FishWarehouseNativeNavigationAgentSetting? persistedSetting = settings
                .FirstOrDefault(setting => setting.AgentTypeId == persistedAgentTypeId);
            if (persistedSetting is not null)
            {
                return FishWarehouseEmployeeAgentTypeResolutionResult.Succeeded(
                    new FishWarehouseEmployeeAgentTypeResolution(
                        persistedSetting.AgentTypeId,
                        persistedSetting.Name,
                        FishWarehouseEmployeeAgentTypeResolutionSource.PersistedId));
            }
        }

        FishWarehouseNativeNavigationAgentSetting[] employeeNamedSettings = settings
            .Where(setting => string.Equals(setting.Name, EmployeeSettingsName, StringComparison.Ordinal))
            .ToArray();
        if (employeeNamedSettings.Length == 1)
        {
            FishWarehouseNativeNavigationAgentSetting setting = employeeNamedSettings[0];
            return FishWarehouseEmployeeAgentTypeResolutionResult.Succeeded(
                new FishWarehouseEmployeeAgentTypeResolution(
                    setting.AgentTypeId,
                    setting.Name,
                    FishWarehouseEmployeeAgentTypeResolutionSource.SettingsName));
        }

        if (employeeNamedSettings.Length > 1)
            return Ambiguous("settings named Employee", employeeNamedSettings.Select(setting => setting.AgentTypeId));

        FishWarehouseEmployeeAgentTypeResolutionResult propertyResolution = ResolveDonor(
            propertyDonorIds,
            settings,
            FishWarehouseEmployeeAgentTypeResolutionSource.PropertyDonor,
            "property-assigned live employee donors");
        if (propertyResolution.Status != FishWarehouseEmployeeAgentTypeResolutionStatus.Pending)
            return propertyResolution;

        return ResolveDonor(
            globalDonorIds,
            settings,
            FishWarehouseEmployeeAgentTypeResolutionSource.GlobalDonor,
            "global live employee donors");
    }

    private static FishWarehouseEmployeeAgentTypeResolutionResult ResolveDonor(
        IReadOnlyList<int> donorIds,
        IReadOnlyList<FishWarehouseNativeNavigationAgentSetting> settings,
        FishWarehouseEmployeeAgentTypeResolutionSource source,
        string sourceDescription)
    {
        int[] observedDistinctIds = donorIds
            .Distinct()
            .OrderBy(donorId => donorId)
            .ToArray();
        if (observedDistinctIds.Length == 0)
            return FishWarehouseEmployeeAgentTypeResolutionResult.Pending();

        if (observedDistinctIds.Length > 1)
            return Ambiguous(sourceDescription, observedDistinctIds);

        int agentTypeId = observedDistinctIds[0];
        FishWarehouseNativeNavigationAgentSetting? setting = settings
            .FirstOrDefault(candidate => candidate.AgentTypeId == agentTypeId);
        if (setting is null)
            return FishWarehouseEmployeeAgentTypeResolutionResult.Pending();

        return FishWarehouseEmployeeAgentTypeResolutionResult.Succeeded(
            new FishWarehouseEmployeeAgentTypeResolution(agentTypeId, setting.Name, source));
    }

    private static FishWarehouseEmployeeAgentTypeResolutionResult Ambiguous(
        string source,
        IEnumerable<int> agentTypeIds) =>
        FishWarehouseEmployeeAgentTypeResolutionResult.Failed(
            $"Fish Warehouse employee navigation agent settings were ambiguous for {source}; observed IDs={string.Join(',', agentTypeIds.Distinct().OrderBy(id => id))}.");
}
