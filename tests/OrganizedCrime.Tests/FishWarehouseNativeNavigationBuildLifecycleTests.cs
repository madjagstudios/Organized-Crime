using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseNativeNavigationBuildLifecycleTests
{
    [Fact]
    public void Settings_name_bootstrap_builds_without_a_live_employee_and_publishes_the_validated_id()
    {
        var adapter = new RecordingAdapter
        {
            AgentSettings = new[] { new FishWarehouseNativeNavigationAgentSetting(47, "Employee") },
            GlobalLiveEmployees = Array.Empty<FishWarehouseNativeNavigationLiveEmployeeAgent>()
        };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(adapter, Request);

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Succeeded, result);
        Assert.Equal(47, lifecycle.ResolvedEmployeeAgentTypeId);
    }

    [Fact]
    public void Validated_persisted_id_builds_without_a_live_employee_even_when_its_settings_name_is_not_Employee()
    {
        var adapter = new RecordingAdapter
        {
            AgentSettings = new[] { new FishWarehouseNativeNavigationAgentSetting(61, "Warehouse Runner") },
            GlobalLiveEmployees = Array.Empty<FishWarehouseNativeNavigationLiveEmployeeAgent>()
        };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(
            adapter,
            Request with { PreferredEmployeeAgentTypeId = 61 });

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Succeeded, result);
        Assert.Equal(61, lifecycle.ResolvedEmployeeAgentTypeId);
    }

    [Fact]
    public void Removing_a_built_graph_clears_the_validated_employee_agent_id()
    {
        var adapter = new RecordingAdapter
        {
            AgentSettings = new[] { new FishWarehouseNativeNavigationAgentSetting(47, "Employee") },
            GlobalLiveEmployees = Array.Empty<FishWarehouseNativeNavigationLiveEmployeeAgent>()
        };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Succeeded, lifecycle.TryBuild(adapter, Request));
        Assert.True(lifecycle.Remove());

        Assert.Null(lifecycle.ResolvedEmployeeAgentTypeId);
    }

    [Fact]
    public void Deduplicates_the_live_employee_and_default_graph_ids_before_probing_or_mutating()
    {
        var adapter = new RecordingAdapter
        {
            GlobalLiveEmployees = new[] { new FishWarehouseNativeNavigationLiveEmployeeAgent("Avery", 0) }
        };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(adapter, Request);

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Succeeded, result);
        Assert.True(lifecycle.IsBuilt);
        Assert.Equal(new[] { 0 }, adapter.GraphProbeAgentIds);
        Assert.Empty(adapter.SurfaceAgentIds);
        Assert.Empty(adapter.LinkCalls);
    }

    [Fact]
    public void Reuse_existing_path_performs_no_native_mutation()
    {
        var adapter = new RecordingAdapter();
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(adapter, Request);

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Succeeded, result);
        Assert.Empty(adapter.SurfaceAgentIds);
        Assert.Empty(adapter.LinkCalls);
        Assert.DoesNotContain("clear-cache", adapter.Operations);
    }

    [Fact]
    public void Add_link_only_never_adds_a_synthetic_surface()
    {
        var adapter = new RecordingAdapter();
        adapter.GraphProbes[7] = CompleteGraphProbe(7) with { PathComplete = false };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(adapter, Request);

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Succeeded, result);
        Assert.Empty(adapter.SurfaceAgentIds);
        Assert.Equal(new[] { 7 }, adapter.LinkCalls.Select(call => call.AgentTypeId));
    }

    [Fact]
    public void Add_surface_and_link_adds_the_surface_before_the_link()
    {
        var adapter = new RecordingAdapter();
        adapter.GraphProbes[7] = CompleteGraphProbe(7) with { InteriorSampled = false };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(adapter, Request);

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Succeeded, result);
        Assert.True(adapter.Operations.IndexOf("surface:7") < adapter.Operations.IndexOf("link:7"));
    }

    [Fact]
    public void Links_use_each_graphs_sampled_exterior_position_not_the_nominal_query_point()
    {
        var adapter = new RecordingAdapter();
        adapter.GraphProbes[0] = CompleteGraphProbe(0, new(20f, 0f, 30f)) with { InteriorSampled = false };
        adapter.GraphProbes[7] = CompleteGraphProbe(7, new(10f, 0f, 30f)) with { InteriorSampled = false };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(adapter, Request);

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Succeeded, result);
        Assert.Equal(new FishWarehouseNativeNavigationVector(20f, 0f, 30f), adapter.LinkCalls.Single(call => call.AgentTypeId == 0).ExteriorEndpoint);
        Assert.Equal(new FishWarehouseNativeNavigationVector(10f, 0f, 30f), adapter.LinkCalls.Single(call => call.AgentTypeId == 7).ExteriorEndpoint);
        Assert.DoesNotContain(Request.World.Link.NominalExteriorQueryPoint, adapter.LinkCalls.Select(call => call.ExteriorEndpoint));
    }

    [Fact]
    public void Clears_the_native_cache_after_all_additions_and_before_validation()
    {
        var adapter = new RecordingAdapter();
        adapter.GraphProbes[0] = CompleteGraphProbe(0) with { InteriorSampled = false };
        adapter.GraphProbes[7] = CompleteGraphProbe(7) with { InteriorSampled = false };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(adapter, Request);

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Succeeded, result);
        int cacheIndex = adapter.Operations.IndexOf("clear-cache");
        Assert.True(cacheIndex > adapter.Operations.IndexOf("link:0"));
        Assert.True(cacheIndex > adapter.Operations.IndexOf("link:7"));
        Assert.True(cacheIndex < adapter.Operations.IndexOf("validate"));
    }

    [Fact]
    public void Validation_failure_removes_owned_native_resources_once_without_publishing_built()
    {
        var adapter = new RecordingAdapter { Validation = PartialPath() };
        adapter.GraphProbes[7] = CompleteGraphProbe(7) with { PathComplete = false };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(adapter, Request);

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Failed, result);
        Assert.False(lifecycle.IsBuilt);
        Assert.Contains(adapter.RemoveCalls, operation =>
            operation.Kind == FishWarehouseNativeNavigationTeardownOperationKind.RemoveLink);
    }

    [Fact]
    public void Partial_build_exception_removes_links_before_data_and_clears_the_cache()
    {
        var adapter = new RecordingAdapter { ThrowWhenAddingLinkForAgentTypeId = 0 };
        adapter.GraphProbes[0] = CompleteGraphProbe(0) with { InteriorSampled = false };
        adapter.GraphProbes[7] = CompleteGraphProbe(7) with { InteriorSampled = false };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(adapter, Request);

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Failed, result);
        IReadOnlyList<FishWarehouseNativeNavigationTeardownOperation> teardown = adapter.RemoveCalls;
        Assert.Equal(FishWarehouseNativeNavigationTeardownOperationKind.RemoveLink, teardown[0].Kind);
        Assert.All(teardown.Skip(1).Take(2), operation =>
            Assert.Equal(FishWarehouseNativeNavigationTeardownOperationKind.RemoveData, operation.Kind));
        Assert.Equal(FishWarehouseNativeNavigationTeardownOperationKind.ClearNativeSampleCache, teardown[^1].Kind);
    }

    [Fact]
    public void Repeated_remove_is_safe()
    {
        var adapter = new RecordingAdapter();
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Succeeded, lifecycle.TryBuild(adapter, Request));
        lifecycle.Remove();
        lifecycle.Remove();

        Assert.False(lifecycle.IsBuilt);
        Assert.Empty(adapter.RemoveCalls);
    }

    [Fact]
    public void Removal_failure_keeps_native_teardown_operations_retryable_until_each_succeeds()
    {
        var adapter = new RecordingAdapter { ThrowOnFirstRemove = true, Validation = PartialPath() };
        adapter.GraphProbes[7] = CompleteGraphProbe(7) with { PathComplete = false };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Failed, lifecycle.TryBuild(adapter, Request));
        Assert.Equal(FishWarehouseNativeNavigationTeardownOperationKind.RemoveLink, adapter.RemoveCalls[0].Kind);

        Assert.True(lifecycle.Remove());

        Assert.Equal(adapter.RemoveCalls[0], adapter.RemoveCalls[1]);
        Assert.Equal(FishWarehouseNativeNavigationTeardownOperationKind.ClearNativeSampleCache, adapter.RemoveCalls[^1].Kind);
        Assert.False(lifecycle.IsBuilt);
    }

    [Fact]
    public void No_live_employee_agent_type_returns_pending_without_mutation()
    {
        var adapter = new RecordingAdapter { GlobalLiveEmployees = Array.Empty<FishWarehouseNativeNavigationLiveEmployeeAgent>() };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(adapter, Request);

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Pending, result);
        Assert.False(lifecycle.IsBuilt);
        Assert.Empty(adapter.Operations);
        Assert.Empty(adapter.RemoveCalls);
    }

    [Fact]
    public void Property_assigned_employee_agent_type_wins_over_an_unrelated_global_employee_type()
    {
        var adapter = new RecordingAdapter
        {
            PropertyAssignedLiveEmployees = new[]
            {
                new FishWarehouseNativeNavigationLiveEmployeeAgent("Avery", 7)
            },
            GlobalLiveEmployees = new[]
            {
                new FishWarehouseNativeNavigationLiveEmployeeAgent("Avery", 7),
                new FishWarehouseNativeNavigationLiveEmployeeAgent("Blair", 3)
            }
        };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(adapter, Request);

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Succeeded, result);
        Assert.Equal(new[] { 7, 0 }, adapter.GraphProbeAgentIds);
        Assert.Equal(
            new[] { FishWarehouseNativeNavigationEmployeeScope.PropertyAssigned },
            adapter.EmployeeSearchScopes);
    }

    [Fact]
    public void Multiple_live_employee_agent_types_fail_with_the_complete_id_census_without_mutation()
    {
        var adapter = new RecordingAdapter
        {
            GlobalLiveEmployees = new[]
            {
                new FishWarehouseNativeNavigationLiveEmployeeAgent("Avery", 7),
                new FishWarehouseNativeNavigationLiveEmployeeAgent("Blair", 3),
                new FishWarehouseNativeNavigationLiveEmployeeAgent("Casey", 7)
            }
        };
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(adapter, Request);

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Failed, result);
        Assert.Contains("3,7", lifecycle.LastFailure?.Message, StringComparison.Ordinal);
        Assert.Empty(adapter.Operations);
        Assert.Empty(adapter.RemoveCalls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Rejects_malformed_idle_point_counts_with_a_named_reason(int offset)
    {
        int actualCount = FishWarehouseEmployeeInfrastructureDefinition.Capacity + offset;
        var lifecycle = new FishWarehouseNativeNavigationBuildLifecycle();

        FishWarehouseNativeNavigationBuildResult result = lifecycle.TryBuild(
            new RecordingAdapter(),
            Request with { IdlePoints = CreateIdlePoints(actualCount) });

        Assert.Equal(FishWarehouseNativeNavigationBuildResult.Failed, result);
        Assert.Contains(
            offset < 0 ? "too-few-idle-points" : "too-many-idle-points",
            lifecycle.LastFailure?.Message,
            StringComparison.Ordinal);
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

    private static FishWarehouseNativeNavigationRuntimeGraphProbe CompleteGraphProbe(
        int agentTypeId,
        FishWarehouseNativeNavigationVector? exteriorPosition = null) =>
        new(agentTypeId, true, exteriorPosition ?? new(1f, 0f, 8f), true, true);

    private static FishWarehouseNativeNavigationPathObservation PartialPath() =>
        new(true, true, FishWarehouseNativeNavigationPathStatus.PathPartial);

    private sealed class RecordingAdapter : IFishWarehouseNativeNavigationAdapter
    {
        public IReadOnlyList<FishWarehouseNativeNavigationAgentSetting> AgentSettings { get; init; } =
            new[]
            {
                new FishWarehouseNativeNavigationAgentSetting(0, "Default"),
                new FishWarehouseNativeNavigationAgentSetting(3, "Worker B"),
                new FishWarehouseNativeNavigationAgentSetting(7, "Worker A")
            };
        public IReadOnlyList<FishWarehouseNativeNavigationLiveEmployeeAgent> PropertyAssignedLiveEmployees { get; init; } =
            Array.Empty<FishWarehouseNativeNavigationLiveEmployeeAgent>();
        public IReadOnlyList<FishWarehouseNativeNavigationLiveEmployeeAgent> GlobalLiveEmployees { get; init; } =
            new[] { new FishWarehouseNativeNavigationLiveEmployeeAgent("Avery", 7) };
        public Dictionary<int, FishWarehouseNativeNavigationRuntimeGraphProbe> GraphProbes { get; } = new();
        public FishWarehouseNativeNavigationPathObservation Validation { get; init; } =
            new(true, true, FishWarehouseNativeNavigationPathStatus.PathComplete);
        public int? ThrowWhenAddingLinkForAgentTypeId { get; init; }
        public bool ThrowOnFirstRemove { get; init; }
        public List<int> GraphProbeAgentIds { get; } = new();
        public List<int> SurfaceAgentIds { get; } = new();
        public List<(int AgentTypeId, FishWarehouseNativeNavigationVector ExteriorEndpoint)> LinkCalls { get; } = new();
        public List<string> Operations { get; } = new();
        public List<FishWarehouseNativeNavigationTeardownOperation> RemoveCalls { get; } = new();
        public List<FishWarehouseNativeNavigationEmployeeScope> EmployeeSearchScopes { get; } = new();
        private bool _removeThrown;

        public IReadOnlyList<FishWarehouseNativeNavigationAgentSetting> GetAgentSettings() => AgentSettings;

        public IReadOnlyList<FishWarehouseNativeNavigationLiveEmployeeAgent> FindLiveEmployeeAgents(
            FishWarehouseNativeNavigationEmployeeScope scope)
        {
            EmployeeSearchScopes.Add(scope);
            return scope == FishWarehouseNativeNavigationEmployeeScope.PropertyAssigned
                ? PropertyAssignedLiveEmployees
                : GlobalLiveEmployees;
        }

        public FishWarehouseNativeNavigationRuntimeGraphProbe ProbeGraph(
            int agentTypeId,
            FishWarehouseNativeNavigationWorldDescriptor world)
        {
            GraphProbeAgentIds.Add(agentTypeId);
            return GraphProbes.TryGetValue(agentTypeId, out FishWarehouseNativeNavigationRuntimeGraphProbe probe)
                ? probe
                : CompleteGraphProbe(agentTypeId);
        }

        public int AddSyntheticSurface(int agentTypeId, FishWarehouseNativeNavigationWorldDescriptor world)
        {
            SurfaceAgentIds.Add(agentTypeId);
            Operations.Add($"surface:{agentTypeId}");
            return 100 + agentTypeId;
        }

        public int AddLink(
            int agentTypeId,
            FishWarehouseNativeNavigationWorldDescriptor world,
            FishWarehouseNativeNavigationVector exteriorEndpoint)
        {
            if (ThrowWhenAddingLinkForAgentTypeId == agentTypeId)
                throw new InvalidOperationException("link creation failed");

            LinkCalls.Add((agentTypeId, exteriorEndpoint));
            Operations.Add($"link:{agentTypeId}");
            return 200 + agentTypeId;
        }

        public void ClearNativeSampleCache() => Operations.Add("clear-cache");

        public FishWarehouseNativeNavigationPathObservation ValidatePath(
            FishWarehouseNativeNavigationGraphRole graphRole,
            int agentTypeId,
            string targetName,
            FishWarehouseNativeNavigationVector start,
            FishWarehouseNativeNavigationVector end)
        {
            Operations.Add("validate");
            return Validation;
        }

        public void Remove(FishWarehouseNativeNavigationTeardownOperation operation)
        {
            RemoveCalls.Add(operation);
            if (ThrowOnFirstRemove && !_removeThrown)
            {
                _removeThrown = true;
                throw new InvalidOperationException("native removal failed");
            }
        }
    }
}
