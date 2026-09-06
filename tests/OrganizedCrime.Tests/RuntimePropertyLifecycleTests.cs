using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class RuntimePropertyLifecycleTests
{
    [Fact]
    public void FishWarehouse_definition_has_stable_runtime_identity()
    {
        var definition = RuntimePropertyDefinition.FishWarehouse;

        Assert.Equal("oc_fishwarehouse", definition.PropertyCode);
        Assert.Equal("Syndicate Warehouse", definition.DisplayName);
        Assert.Equal("Fish Warehouse", definition.NativeName);
        Assert.Equal("Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse", definition.AnchorPath);
        Assert.Equal("OC_FishWarehouse_PropertyRoot", definition.RootName);
        Assert.Equal((ushort)65000, definition.RuntimeCollectionId);
        Assert.Equal("OC_FishWarehouse_BuildGrid", definition.BuildGridRootName);
        Assert.Equal("d8b7e1d4-3f1d-4cf0-9b7f-2e2fc8d6c6d1", definition.BuildGridGuid);
    }

    [Fact]
    public void FishWarehouse_definition_accepts_only_its_pinned_build_grid_identity()
    {
        var definition = RuntimePropertyDefinition.FishWarehouse;

        Assert.True(definition.HasBuildGridGuid("D8B7E1D4-3F1D-4CF0-9B7F-2E2FC8D6C6D1"));
        Assert.False(definition.HasBuildGridGuid("00000000-0000-0000-0000-000000000000"));
        Assert.False(definition.HasBuildGridGuid(null));
    }

    [Theory]
    [InlineData(RuntimePropertyLifecycleState.Unavailable, RuntimePropertyLifecycleState.Ready)]
    [InlineData(RuntimePropertyLifecycleState.Ready, RuntimePropertyLifecycleState.Starting)]
    [InlineData(RuntimePropertyLifecycleState.Starting, RuntimePropertyLifecycleState.Spawned)]
    [InlineData(RuntimePropertyLifecycleState.Starting, RuntimePropertyLifecycleState.Failed)]
    [InlineData(RuntimePropertyLifecycleState.Spawned, RuntimePropertyLifecycleState.Unloading)]
    [InlineData(RuntimePropertyLifecycleState.Unloading, RuntimePropertyLifecycleState.Spawned)]
    [InlineData(RuntimePropertyLifecycleState.Unloading, RuntimePropertyLifecycleState.Unavailable)]
    [InlineData(RuntimePropertyLifecycleState.Unloading, RuntimePropertyLifecycleState.Failed)]
    public void Lifecycle_allows_only_defined_transitions(
        RuntimePropertyLifecycleState from,
        RuntimePropertyLifecycleState to)
    {
        Assert.True(RuntimePropertyLifecycle.CanTransition(from, to));
    }

    [Theory]
    [InlineData(RuntimePropertyLifecycleState.Unavailable, RuntimePropertyLifecycleState.Starting)]
    [InlineData(RuntimePropertyLifecycleState.Ready, RuntimePropertyLifecycleState.Spawned)]
    [InlineData(RuntimePropertyLifecycleState.Spawned, RuntimePropertyLifecycleState.Starting)]
    [InlineData(RuntimePropertyLifecycleState.Unavailable, RuntimePropertyLifecycleState.Unloading)]
    [InlineData(RuntimePropertyLifecycleState.Failed, RuntimePropertyLifecycleState.Ready)]
    public void Lifecycle_rejects_duplicate_start_stop_before_start_and_failed_retry(
        RuntimePropertyLifecycleState from,
        RuntimePropertyLifecycleState to)
    {
        Assert.False(RuntimePropertyLifecycle.CanTransition(from, to));
    }
}
