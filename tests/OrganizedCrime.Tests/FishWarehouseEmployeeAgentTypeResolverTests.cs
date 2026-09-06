using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseEmployeeAgentTypeResolverTests
{
    [Fact]
    public void Valid_persisted_id_wins_and_records_an_arbitrary_observed_settings_name()
    {
        FishWarehouseEmployeeAgentTypeResolutionResult result = Resolve(
            persistedId: 47,
            settings: new[]
            {
                new FishWarehouseNativeNavigationAgentSetting(47, "Warehouse Runner"),
                new FishWarehouseNativeNavigationAgentSetting(9, "Employee")
            },
            propertyDonorIds: new[] { 9 },
            globalDonorIds: new[] { 9 });

        Assert.Equal(FishWarehouseEmployeeAgentTypeResolutionStatus.Succeeded, result.Status);
        Assert.Equal(
            new FishWarehouseEmployeeAgentTypeResolution(
                47,
                "Warehouse Runner",
                FishWarehouseEmployeeAgentTypeResolutionSource.PersistedId),
            result.Resolution);
    }

    [Fact]
    public void Unique_Employee_settings_name_bootstraps_without_any_live_employee()
    {
        FishWarehouseEmployeeAgentTypeResolutionResult result = Resolve(
            persistedId: null,
            settings: new[] { new FishWarehouseNativeNavigationAgentSetting(27, "Employee") },
            propertyDonorIds: Array.Empty<int>(),
            globalDonorIds: Array.Empty<int>());

        Assert.Equal(FishWarehouseEmployeeAgentTypeResolutionStatus.Succeeded, result.Status);
        Assert.Equal(
            new FishWarehouseEmployeeAgentTypeResolution(
                27,
                "Employee",
                FishWarehouseEmployeeAgentTypeResolutionSource.SettingsName),
            result.Resolution);
    }

    [Fact]
    public void Absent_persisted_id_is_discarded_before_settings_name_bootstrap()
    {
        FishWarehouseEmployeeAgentTypeResolutionResult result = Resolve(
            persistedId: 99,
            settings: new[] { new FishWarehouseNativeNavigationAgentSetting(31, "Employee") },
            propertyDonorIds: Array.Empty<int>(),
            globalDonorIds: Array.Empty<int>());

        Assert.Equal(FishWarehouseEmployeeAgentTypeResolutionStatus.Succeeded, result.Status);
        Assert.Equal(
            new FishWarehouseEmployeeAgentTypeResolution(
                31,
                "Employee",
                FishWarehouseEmployeeAgentTypeResolutionSource.SettingsName),
            result.Resolution);
    }

    [Fact]
    public void Unique_property_assigned_live_donor_wins_over_global_donors()
    {
        FishWarehouseEmployeeAgentTypeResolutionResult result = Resolve(
            persistedId: null,
            settings: new[]
            {
                new FishWarehouseNativeNavigationAgentSetting(15, "Warehouse Worker"),
                new FishWarehouseNativeNavigationAgentSetting(8, "Other Worker")
            },
            propertyDonorIds: new[] { 15, 15 },
            globalDonorIds: new[] { 8 });

        Assert.Equal(FishWarehouseEmployeeAgentTypeResolutionStatus.Succeeded, result.Status);
        Assert.Equal(
            new FishWarehouseEmployeeAgentTypeResolution(
                15,
                "Warehouse Worker",
                FishWarehouseEmployeeAgentTypeResolutionSource.PropertyDonor),
            result.Resolution);
    }

    [Fact]
    public void Unique_global_live_donor_is_used_when_the_property_has_no_donor()
    {
        FishWarehouseEmployeeAgentTypeResolutionResult result = Resolve(
            persistedId: null,
            settings: new[] { new FishWarehouseNativeNavigationAgentSetting(62, "Contractor") },
            propertyDonorIds: Array.Empty<int>(),
            globalDonorIds: new[] { 62, 62 });

        Assert.Equal(FishWarehouseEmployeeAgentTypeResolutionStatus.Succeeded, result.Status);
        Assert.Equal(
            new FishWarehouseEmployeeAgentTypeResolution(
                62,
                "Contractor",
                FishWarehouseEmployeeAgentTypeResolutionSource.GlobalDonor),
            result.Resolution);
    }

    [Fact]
    public void Missing_settings_and_live_donors_remain_pending()
    {
        FishWarehouseEmployeeAgentTypeResolutionResult result = Resolve(
            persistedId: null,
            settings: Array.Empty<FishWarehouseNativeNavigationAgentSetting>(),
            propertyDonorIds: Array.Empty<int>(),
            globalDonorIds: Array.Empty<int>());

        Assert.Equal(FishWarehouseEmployeeAgentTypeResolutionStatus.Pending, result.Status);
        Assert.Null(result.Resolution);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Ambiguous_Employee_settings_entries_fail_with_every_candidate_id()
    {
        FishWarehouseEmployeeAgentTypeResolutionResult result = Resolve(
            persistedId: null,
            settings: new[]
            {
                new FishWarehouseNativeNavigationAgentSetting(7, "Employee"),
                new FishWarehouseNativeNavigationAgentSetting(3, "Employee")
            },
            propertyDonorIds: Array.Empty<int>(),
            globalDonorIds: Array.Empty<int>());

        Assert.Equal(FishWarehouseEmployeeAgentTypeResolutionStatus.Failed, result.Status);
        Assert.Contains("3,7", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Ambiguous_property_live_donors_fail_with_every_candidate_id()
    {
        FishWarehouseEmployeeAgentTypeResolutionResult result = Resolve(
            persistedId: null,
            settings: new[]
            {
                new FishWarehouseNativeNavigationAgentSetting(7, "Worker A"),
                new FishWarehouseNativeNavigationAgentSetting(3, "Worker B")
            },
            propertyDonorIds: new[] { 7, 3, 7 },
            globalDonorIds: Array.Empty<int>());

        Assert.Equal(FishWarehouseEmployeeAgentTypeResolutionStatus.Failed, result.Status);
        Assert.Contains("3,7", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Mixed_recognized_and_unrecognized_property_donors_fail_with_the_complete_census()
    {
        FishWarehouseEmployeeAgentTypeResolutionResult result = Resolve(
            persistedId: null,
            settings: new[] { new FishWarehouseNativeNavigationAgentSetting(7, "Worker A") },
            propertyDonorIds: new[] { 7, 99 },
            globalDonorIds: Array.Empty<int>());

        Assert.Equal(FishWarehouseEmployeeAgentTypeResolutionStatus.Failed, result.Status);
        Assert.Contains("7,99", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Ambiguous_global_live_donors_fail_with_every_candidate_id()
    {
        FishWarehouseEmployeeAgentTypeResolutionResult result = Resolve(
            persistedId: null,
            settings: new[]
            {
                new FishWarehouseNativeNavigationAgentSetting(11, "Worker A"),
                new FishWarehouseNativeNavigationAgentSetting(5, "Worker B")
            },
            propertyDonorIds: Array.Empty<int>(),
            globalDonorIds: new[] { 11, 5, 11 });

        Assert.Equal(FishWarehouseEmployeeAgentTypeResolutionStatus.Failed, result.Status);
        Assert.Contains("5,11", result.FailureReason, StringComparison.Ordinal);
    }

    private static FishWarehouseEmployeeAgentTypeResolutionResult Resolve(
        int? persistedId,
        IReadOnlyList<FishWarehouseNativeNavigationAgentSetting> settings,
        IReadOnlyList<int> propertyDonorIds,
        IReadOnlyList<int> globalDonorIds) =>
        FishWarehouseEmployeeAgentTypeResolver.Resolve(
            persistedId,
            settings,
            propertyDonorIds,
            globalDonorIds);
}
