using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseNativeNavigationResourceLedgerTests
{
    [Fact]
    public void Tears_down_links_before_data_and_clears_the_sample_cache_last()
    {
        var ledger = new FishWarehouseNativeNavigationResourceLedger();
        ledger.RecordData(FishWarehouseNativeNavigationGraphRole.Employee, 101);
        ledger.RecordData(FishWarehouseNativeNavigationGraphRole.Default, 102);
        ledger.RecordLink(FishWarehouseNativeNavigationGraphRole.Employee, 201);
        ledger.RecordLink(FishWarehouseNativeNavigationGraphRole.Default, 202);

        IReadOnlyList<FishWarehouseNativeNavigationTeardownOperation> operations =
            ledger.PendingTeardown();

        Assert.Equal(
            new[]
            {
                "remove-link:default",
                "remove-link:employee",
                "remove-data:default",
                "remove-data:employee",
                "clear-native-sample-cache"
            },
            operations.Select(operation => operation.ToString()));
        Assert.Equal(202, operations[0].ResourceKey);
        Assert.Equal(101, operations[3].ResourceKey);
    }

    [Fact]
    public void Acknowledges_each_teardown_operation_only_after_the_native_removal_succeeds()
    {
        var ledger = new FishWarehouseNativeNavigationResourceLedger();
        ledger.RecordData(FishWarehouseNativeNavigationGraphRole.Default, 102);

        IReadOnlyList<FishWarehouseNativeNavigationTeardownOperation> initial = ledger.PendingTeardown();
        ledger.Acknowledge(initial[0]);
        IReadOnlyList<FishWarehouseNativeNavigationTeardownOperation> afterData = ledger.PendingTeardown();
        ledger.Acknowledge(afterData[0]);

        Assert.Equal(FishWarehouseNativeNavigationTeardownOperationKind.RemoveData, initial[0].Kind);
        Assert.Single(afterData);
        Assert.Equal(FishWarehouseNativeNavigationTeardownOperationKind.ClearNativeSampleCache, afterData[0].Kind);
        Assert.Empty(ledger.PendingTeardown());
    }

    [Fact]
    public void Cleans_a_partial_build_with_one_data_instance_and_no_link()
    {
        var ledger = new FishWarehouseNativeNavigationResourceLedger();
        ledger.RecordData(FishWarehouseNativeNavigationGraphRole.Employee, 101);

        IReadOnlyList<FishWarehouseNativeNavigationTeardownOperation> operations =
            ledger.PendingTeardown();

        Assert.Equal(
            new[] { "remove-data:employee", "clear-native-sample-cache" },
            operations.Select(operation => operation.ToString()));
    }

    [Fact]
    public void Does_not_record_the_same_opaque_resource_key_twice()
    {
        var ledger = new FishWarehouseNativeNavigationResourceLedger();
        ledger.RecordLink(FishWarehouseNativeNavigationGraphRole.Default, 202);
        ledger.RecordLink(FishWarehouseNativeNavigationGraphRole.Default, 202);

        IReadOnlyList<FishWarehouseNativeNavigationTeardownOperation> operations = ledger.PendingTeardown();

        Assert.Equal(
            new[] { "remove-link:default", "clear-native-sample-cache" },
            operations.Select(operation => operation.ToString()));
    }
}
