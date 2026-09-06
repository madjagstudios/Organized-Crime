using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehousePropertyReplayHostTests
{
    private const string FishWarehouseCode = "oc_fishwarehouse";
    private const string FirstGuid = "00000000-0000-0000-0000-000000000001";
    private const string SecondGuid = "00000000-0000-0000-0000-000000000002";
    private const string ThirdGuid = "00000000-0000-0000-0000-000000000003";
    private const string FourthGuid = "00000000-0000-0000-0000-000000000004";
    private const string CaseVariantGuid = "a0000000-0000-0000-0000-000000000005";

    [Fact]
    public void Replay_uses_loader_then_saved_then_dependency_then_payload_index_order()
    {
        var adapter = new RecordingAdapter(
            loaderOrders: new Dictionary<string, int>
            {
                ["CounterData"] = 10,
                ["StorageData"] = 5,
                ["ProceduralGridItemData"] = 10
            });
        var host = new FishWarehousePropertyReplayHost(
            adapter,
            new[]
            {
                Descriptor(0, "CounterData", FirstGuid, savedLoadOrder: 8),
                Descriptor(1, "ProceduralGridItemData", SecondGuid, savedLoadOrder: 2, isDependent: true),
                Descriptor(2, "StorageData", ThirdGuid, savedLoadOrder: 5),
                Descriptor(3, "CounterData", FourthGuid, savedLoadOrder: 2)
            });

        var result = host.Replay();

        Assert.Equal(FishWarehousePropertyReplayState.Succeeded, result.State);
        Assert.Equal(new[] { 2, 3, 1, 0 }, adapter.LoadedPayloadIndexes);
    }

    [Fact]
    public void Replay_loads_a_saved_guid_once_even_when_the_capture_has_duplicate_records()
    {
        var adapter = new RecordingAdapter(new Dictionary<string, int> { ["CounterData"] = 1 });
        var host = new FishWarehousePropertyReplayHost(
            adapter,
            new[]
            {
                Descriptor(0, "CounterData", FirstGuid, savedLoadOrder: 1),
                Descriptor(1, "CounterData", FirstGuid, savedLoadOrder: 2)
            });

        var result = host.Replay();

        Assert.Equal(FishWarehousePropertyReplayState.Succeeded, result.State);
        Assert.Equal(1, result.ExpectedObjectCount);
        Assert.Equal(new[] { 0 }, adapter.LoadedPayloadIndexes);
    }

    [Fact]
    public void Replay_uses_the_first_duplicate_guid_in_the_stable_replay_plan()
    {
        var adapter = new RecordingAdapter(
            new Dictionary<string, int>
            {
                ["LateLoaderData"] = 10,
                ["EarlyLoaderData"] = 5
            });
        var host = new FishWarehousePropertyReplayHost(
            adapter,
            new[]
            {
                Descriptor(0, "LateLoaderData", FirstGuid, savedLoadOrder: 1),
                Descriptor(1, "EarlyLoaderData", FirstGuid, savedLoadOrder: 9)
            });

        var result = host.Replay();

        Assert.Equal(FishWarehousePropertyReplayState.Succeeded, result.State);
        Assert.Equal(new[] { 1 }, adapter.LoadedPayloadIndexes);
    }

    [Fact]
    public void Replay_treats_equivalent_saved_guid_text_as_one_guid()
    {
        var adapter = new RecordingAdapter(new Dictionary<string, int> { ["CounterData"] = 1 });
        var host = new FishWarehousePropertyReplayHost(
            adapter,
            new[]
            {
                Descriptor(0, "CounterData", CaseVariantGuid.ToUpperInvariant(), savedLoadOrder: 1),
                Descriptor(1, "CounterData", CaseVariantGuid, savedLoadOrder: 2)
            });

        var result = host.Replay();

        Assert.Equal(FishWarehousePropertyReplayState.Succeeded, result.State);
        Assert.Equal(1, result.ExpectedObjectCount);
        Assert.Equal(new[] { 0 }, adapter.LoadedPayloadIndexes);
    }

    [Fact]
    public void Replay_treats_a_pre_registered_guid_as_restored_without_loading_it()
    {
        var adapter = new RecordingAdapter(
            new Dictionary<string, int> { ["CounterData"] = 1 },
            registeredGuids: new[] { FirstGuid });
        var host = new FishWarehousePropertyReplayHost(adapter, new[] { Descriptor(0, "CounterData", FirstGuid, 1) });

        var result = host.Replay();

        Assert.Equal(FishWarehousePropertyReplayState.Succeeded, result.State);
        Assert.Equal(1, result.ResolvedObjectCount);
        Assert.Empty(adapter.LoadedPayloadIndexes);
    }

    [Fact]
    public void Replay_reports_only_the_concise_summary_for_a_pre_registered_object()
    {
        var messages = new List<string>();
        var adapter = new RecordingAdapter(
            new Dictionary<string, int> { ["CounterData"] = 3 },
            registeredGuids: new[] { FirstGuid });
        var host = new FishWarehousePropertyReplayHost(
            adapter,
            new[] { Descriptor(4, "CounterData", FirstGuid, savedLoadOrder: 7) },
            log: messages.Add);

        Assert.Equal(FishWarehousePropertyReplayState.Succeeded, host.Replay().State);

        Assert.DoesNotContain(messages, message => message.Contains("native replay ordered", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("1/1", StringComparison.Ordinal));
    }

    [Fact]
    public void Replay_fails_before_loading_when_a_registry_loader_is_missing()
    {
        var adapter = new RecordingAdapter(new Dictionary<string, int>());
        var host = new FishWarehousePropertyReplayHost(adapter, new[] { Descriptor(7, "UnknownData", FirstGuid, 1) });

        var result = host.Replay();

        Assert.Equal(FishWarehousePropertyReplayState.Failed, result.State);
        Assert.Contains("missing-loader", result.FailureReason, StringComparison.Ordinal);
        Assert.Contains("UnknownData", result.FailureReason, StringComparison.Ordinal);
        Assert.Empty(adapter.LoadedPayloadIndexes);
    }

    [Fact]
    public void Replay_requires_every_resolved_object_to_belong_to_the_fish_warehouse()
    {
        var adapter = new RecordingAdapter(
            new Dictionary<string, int> { ["CounterData"] = 1 },
            observations: new Dictionary<string, FishWarehouseRestoredObjectObservation>
            {
                [FirstGuid] = new(true, "dockswarehouse", false)
            });
        var host = new FishWarehousePropertyReplayHost(adapter, new[] { Descriptor(0, "CounterData", FirstGuid, 1) });

        var result = host.Replay();

        Assert.Equal(FishWarehousePropertyReplayState.Failed, result.State);
        Assert.Contains("ParentPropertyCode", result.FailureReason, StringComparison.Ordinal);
        Assert.Equal(new[] { 0 }, adapter.LoadedPayloadIndexes);
    }

    [Fact]
    public void Fish_warehouse_definition_retains_the_captured_grid_guid()
    {
        Assert.Equal("d8b7e1d4-3f1d-4cf0-9b7f-2e2fc8d6c6d1", RuntimePropertyDefinition.FishWarehouse.BuildGridGuid);
    }

    [Fact]
    public void Replay_suppresses_the_fallback_when_the_capture_contains_a_saved_locker()
    {
        var fallbackCalls = 0;
        var adapter = new RecordingAdapter(new Dictionary<string, int> { ["CounterData"] = 1 });
        var host = new FishWarehousePropertyReplayHost(
            adapter,
            new[] { Descriptor(0, "CounterData", FirstGuid, 1, isSavedEmployeeHome: true) },
            () => fallbackCalls++);

        var result = host.Replay();

        Assert.Equal(FishWarehousePropertyReplayState.Succeeded, result.State);
        Assert.Equal(0, fallbackCalls);
    }

    [Fact]
    public void Replay_places_one_fallback_after_all_objects_complete_when_no_locker_was_saved_or_restored()
    {
        var adapter = new RecordingAdapter(new Dictionary<string, int> { ["CounterData"] = 1 });
        var events = new List<string>();
        adapter.OnLoad = index => events.Add($"load:{index}");
        var host = new FishWarehousePropertyReplayHost(
            adapter,
            new[]
            {
                Descriptor(0, "CounterData", FirstGuid, 1),
                Descriptor(1, "CounterData", SecondGuid, 2)
            },
            () => events.Add("fallback"));

        Assert.Equal(FishWarehousePropertyReplayState.Succeeded, host.Replay().State);
        Assert.Equal(FishWarehousePropertyReplayState.Succeeded, host.Replay().State);

        Assert.Equal(new[] { "load:0", "load:1", "fallback" }, events);
    }

    [Fact]
    public void Native_adapter_never_invokes_interop_for_a_malformed_saved_guid()
    {
        var interopCalls = 0;

        var invoked = FishWarehouseNativePropertyReplayAdapter.TryInvokeGuidInterop(
            "not-a-guid",
            _ =>
            {
                interopCalls++;
                return true;
            },
            out var ignored);

        Assert.False(invoked);
        Assert.False(ignored);
        Assert.Equal(0, interopCalls);
    }

    private static FishWarehouseSavedObjectDescriptor Descriptor(
        int payloadIndex,
        string dataType,
        string guid,
        int savedLoadOrder,
        bool isDependent = false,
        bool isSavedEmployeeHome = false) =>
        new(payloadIndex, dataType, guid, savedLoadOrder, isDependent, isSavedEmployeeHome);

    private sealed class RecordingAdapter : IFishWarehouseNativePropertyReplayAdapter
    {
        private readonly IReadOnlyDictionary<string, int> _loaderOrders;
        private readonly ISet<string> _registeredGuids;
        private readonly IReadOnlyDictionary<string, FishWarehouseRestoredObjectObservation> _observations;

        public RecordingAdapter(
            IReadOnlyDictionary<string, int> loaderOrders,
            IEnumerable<string>? registeredGuids = null,
            IReadOnlyDictionary<string, FishWarehouseRestoredObjectObservation>? observations = null)
        {
            _loaderOrders = loaderOrders;
            _registeredGuids = new HashSet<string>(registeredGuids ?? Array.Empty<string>(), StringComparer.Ordinal);
            _observations = observations ?? new Dictionary<string, FishWarehouseRestoredObjectObservation>();
        }

        public List<int> LoadedPayloadIndexes { get; } = new();
        public Action<int>? OnLoad { get; set; }

        public bool IsGuidRegistered(string guid) => _registeredGuids.Contains(guid);

        public bool TryGetObjectLoaderLoadOrder(string dataType, out int loadOrder) =>
            _loaderOrders.TryGetValue(dataType, out loadOrder);

        public void LoadObject(int payloadIndex)
        {
            LoadedPayloadIndexes.Add(payloadIndex);
            OnLoad?.Invoke(payloadIndex);
        }

        public FishWarehouseRestoredObjectObservation ObserveObject(string guid) =>
            _observations.TryGetValue(guid, out var observation)
                ? observation
                : new FishWarehouseRestoredObjectObservation(true, FishWarehouseCode, false);
    }
}
