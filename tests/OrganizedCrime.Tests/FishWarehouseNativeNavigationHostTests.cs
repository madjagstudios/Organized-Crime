using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseNativeNavigationHostTests
{
    [Fact]
    public void Host_clears_the_validated_agent_id_on_remove_without_success_telemetry()
    {
        var adapter = new HostRecordingAdapter
        {
            AgentSettings = new[] { new FishWarehouseNativeNavigationAgentSetting(47, "Employee") }
        };
        var host = new FishWarehouseNativeNavigationHost();
        var messages = new List<string>();

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Succeeded, host.TryBuild(adapter, Request, messages.Add));

        Assert.Equal(47, host.ResolvedEmployeeAgentTypeId);
        Assert.Empty(messages);

        Assert.True(host.Remove());
        Assert.Null(host.ResolvedEmployeeAgentTypeId);
    }

    [Fact]
    public void Invalid_host_inputs_fail_closed_by_removing_an_already_built_native_graph()
    {
        var adapter = new HostRecordingAdapter();
        var host = new FishWarehouseNativeNavigationHost();

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Succeeded, host.TryBuild(adapter, Request));

        FishWarehouseNativeNavigationBuildResult result = host.TryBuild(
            null!, null!, null!, null!, null, null);

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Failed, result);
        Assert.False(host.IsBuilt);
        Assert.Equal(
            new[]
            {
                FishWarehouseNativeNavigationTeardownOperationKind.RemoveLink,
                FishWarehouseNativeNavigationTeardownOperationKind.RemoveData,
                FishWarehouseNativeNavigationTeardownOperationKind.ClearNativeSampleCache
            },
            adapter.Removals.Select(operation => operation.Kind));
    }

    [Fact]
    public void Failed_native_removal_is_retried_by_later_stop_before_room_teardown()
    {
        var adapter = new HostRecordingAdapter { RemovalFailuresRemaining = 1 };
        var actions = new CoordinatorActions(new FishWarehouseNativeNavigationHost(), adapter);
        var coordinator = new FishWarehouseContinuousNavigationCoordinator(actions);

        coordinator.Start(0f);
        coordinator.Tick(0.50f);

        Assert.False(coordinator.Stop());
        Assert.True(coordinator.HasPendingCleanup);
        Assert.Equal(new[] { "remove" }, actions.Calls);

        Assert.True(coordinator.Stop());

        Assert.False(coordinator.HasPendingCleanup);
        Assert.Equal(FishWarehouseContinuousNavigationState.Inactive, coordinator.State);
        Assert.Equal(
            new[] { "remove", "remove", "restore-garage", "dispose-room" },
            actions.Calls);
    }

    private static readonly FishWarehouseNativeNavigationBuildRequest Request = new(
        new FishWarehouseNativeNavigationWorldDescriptor(
            new FishWarehouseNativeNavigationSurface(new(0f, 0f, 0f), new(12f, 0.1f, 10f), 0, false),
            new FishWarehouseNativeNavigationBox(new(0f, 2f, 0f), new(13f, 5f, 11f)),
            new FishWarehouseNativeNavigationLink(new(0f, 0f, 4f), new(0f, 0f, 8f), 2f, 0.35f, true),
            new(0f, 0f, 3f),
            1f),
        CreateIdlePoints(FishWarehouseEmployeeInfrastructureDefinition.Capacity),
        LockerPoint: null,
        PackagingStationPoint: null);

    private static FishWarehouseNativeNavigationVector[] CreateIdlePoints(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new FishWarehouseNativeNavigationVector(-1f, 0f, index + 1f))
            .ToArray();

    private sealed class HostRecordingAdapter : IFishWarehouseNativeNavigationAdapter
    {
        public int RemovalFailuresRemaining { get; set; }
        public IReadOnlyList<FishWarehouseNativeNavigationAgentSetting> AgentSettings { get; init; } =
            new[] { new FishWarehouseNativeNavigationAgentSetting(7, "Employee") };
        public List<FishWarehouseNativeNavigationTeardownOperation> Removals { get; } = new();

        public IReadOnlyList<FishWarehouseNativeNavigationAgentSetting> GetAgentSettings() => AgentSettings;

        public IReadOnlyList<FishWarehouseNativeNavigationLiveEmployeeAgent> FindLiveEmployeeAgents(
            FishWarehouseNativeNavigationEmployeeScope scope) =>
            scope == FishWarehouseNativeNavigationEmployeeScope.PropertyAssigned
                ? Array.Empty<FishWarehouseNativeNavigationLiveEmployeeAgent>()
                : new[] { new FishWarehouseNativeNavigationLiveEmployeeAgent("Avery", 7) };

        public FishWarehouseNativeNavigationRuntimeGraphProbe ProbeGraph(
            int agentTypeId,
            FishWarehouseNativeNavigationWorldDescriptor world) =>
            new(agentTypeId, true, new(1f, 0f, 8f), agentTypeId == 0, agentTypeId == 0);

        public int AddSyntheticSurface(int agentTypeId, FishWarehouseNativeNavigationWorldDescriptor world) => 107;

        public int AddLink(
            int agentTypeId,
            FishWarehouseNativeNavigationWorldDescriptor world,
            FishWarehouseNativeNavigationVector exteriorEndpoint) => 207;

        public void ClearNativeSampleCache() { }

        public FishWarehouseNativeNavigationPathObservation ValidatePath(
            FishWarehouseNativeNavigationGraphRole graphRole,
            int agentTypeId,
            string targetName,
            FishWarehouseNativeNavigationVector start,
            FishWarehouseNativeNavigationVector end) =>
            new(true, true, FishWarehouseNativeNavigationPathStatus.PathComplete);

        public void Remove(FishWarehouseNativeNavigationTeardownOperation operation)
        {
            Removals.Add(operation);
            if (RemovalFailuresRemaining-- > 0)
                throw new InvalidOperationException("native removal failed");
        }
    }

    private sealed class CoordinatorActions : IFishWarehouseContinuousNavigationActions
    {
        private readonly FishWarehouseNativeNavigationHost _host;
        private readonly IFishWarehouseNativeNavigationAdapter _adapter;

        public CoordinatorActions(
            FishWarehouseNativeNavigationHost host,
            IFishWarehouseNativeNavigationAdapter adapter)
        {
            _host = host;
            _adapter = adapter;
        }

        public List<string> Calls { get; } = new();

        public bool TryPreflightGeometry() => true;
        public bool TryActivateRoom() => true;
        public bool TryOpenGarage() => true;
        public string GetBuildableAndConfigurableSignature() => "stable";
        public FishWarehouseNativeNavigationBuildResult TryBuildNavigation() => _host.TryBuild(_adapter, Request);
        public bool ValidateNavigation() => _host.IsBuilt;
        public bool RemoveNavigation()
        {
            Calls.Add("remove");
            return _host.Remove();
        }
        public void RestoreGarage() => Calls.Add("restore-garage");
        public void DisposeRoom() => Calls.Add("dispose-room");
    }
}
