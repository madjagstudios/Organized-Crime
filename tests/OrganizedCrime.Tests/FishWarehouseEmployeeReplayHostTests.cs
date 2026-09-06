using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseEmployeeReplayHostTests
{
    private const string EmployeeGuid = "00000000-0000-0000-0000-000000000111";
    private const string HomeGuid = "00000000-0000-0000-0000-000000000222";
    private const string StationOneGuid = "00000000-0000-0000-0000-000000000333";
    private const string StationTwoGuid = "00000000-0000-0000-0000-000000000444";
    private const string StationThreeGuid = "00000000-0000-0000-0000-000000000555";

    [Fact]
    public void Replay_remains_pending_until_navigation_is_ready()
    {
        var adapter = new FakeAdapter();
        var host = CreateHost(adapter);

        var result = host.Replay(navigationReady: false);

        Assert.Equal(FishWarehouseEmployeeReplayState.Pending, result.State);
        Assert.Equal(0, adapter.GuidRegistrationCalls);
        Assert.Equal(0, adapter.PrimaryLoadCalls);
    }

    [Fact]
    public void Property_replay_delegates_employee_replay_only_after_objects_complete()
    {
        var adapter = new FakeAdapter { Observation = MatchingObservation() };
        var employeeReplay = CreateHost(adapter);
        var propertyReplay = new FishWarehousePropertyReplayHost(
            new EmptyObjectReplayAdapter(),
            Array.Empty<FishWarehouseSavedObjectDescriptor>());

        Assert.Equal(
            FishWarehouseEmployeeReplayState.Pending,
            propertyReplay.ReplayEmployees(employeeReplay, navigationReady: true).State);
        Assert.Equal(0, adapter.GuidRegistrationCalls);

        Assert.Equal(FishWarehousePropertyReplayState.Succeeded, propertyReplay.Replay().State);
        Assert.Equal(
            FishWarehouseEmployeeReplayState.Pending,
            propertyReplay.ReplayEmployees(employeeReplay, navigationReady: false).State);
        Assert.Equal(
            FishWarehouseEmployeeReplayState.Succeeded,
            propertyReplay.ReplayEmployees(employeeReplay, navigationReady: true).State);
    }

    [Fact]
    public void Replay_deduplicates_a_registered_guid_without_invoking_the_loader()
    {
        var adapter = new FakeAdapter
        {
            IsGuidRegistered = true,
            Observation = MatchingObservation()
        };
        var host = CreateHost(adapter);

        var result = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Succeeded, result.State);
        Assert.Equal(1, result.ReplayedEmployeeCount);
        Assert.Equal(0, adapter.PrimaryLoadCalls);
        Assert.True(result.CanAdvanceEmployeesReplayed);
    }

    [Fact]
    public void Replay_adopts_a_registered_guid_when_configuration_can_make_it_match()
    {
        var now = 0f;
        var adapter = new FakeAdapter
        {
            IsGuidRegistered = true,
            Observation = MatchingObservation() with { Id = "stale-id" },
            ObservationAfterConfiguration = MatchingObservation()
        };
        var host = CreateHost(adapter, () => now);

        Assert.Equal(FishWarehouseEmployeeReplayState.Pending, host.Replay(navigationReady: true).State);
        now = FishWarehouseEmployeeReplayHost.ConfigurationVerificationTimeoutSeconds;
        Assert.Equal(FishWarehouseEmployeeReplayState.Pending, host.Replay(navigationReady: true).State);
        var result = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Succeeded, result.State);
        Assert.Equal(1, adapter.FallbackConfigurationCalls);
        Assert.Equal(0, adapter.FallbackCreationCalls);
    }

    [Fact]
    public void Replay_waits_for_active_move_restore_before_verifying_the_employee()
    {
        var adapter = new FakeAdapter
        {
            Observation = MatchingObservation(),
            ActiveMoveRestoreResults = new Queue<FishWarehouseEmployeeActiveMoveRestoreResult>(new[]
            {
                FishWarehouseEmployeeActiveMoveRestoreResult.Pending,
                FishWarehouseEmployeeActiveMoveRestoreResult.Restored
            })
        };
        var host = CreateHost(adapter);

        Assert.Equal(FishWarehouseEmployeeReplayState.Pending, host.Replay(navigationReady: true).State);
        var result = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Succeeded, result.State);
        Assert.Equal(2, adapter.ActiveMoveRestoreCalls);
    }

    [Fact]
    public void Replay_uses_the_primary_loader_once_and_requires_the_full_saved_packager_state()
    {
        var adapter = new FakeAdapter
        {
            Observation = MatchingObservation()
        };
        var host = CreateHost(adapter);

        var result = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Succeeded, result.State);
        Assert.Equal(1, adapter.PrimaryLoadCalls);
        Assert.Equal(EmployeeGuid, adapter.LastPrimaryDescriptor!.Guid);
        Assert.Equal("PackagerData", adapter.LastPrimaryDescriptor.DataType);
        Assert.Equal("Packager", adapter.LastPrimaryDescriptor.Identity);
        Assert.Equal(1, result.ReplayedEmployeeCount);
        Assert.True(result.CanAdvanceEmployeesReplayed);
    }

    [Fact]
    public void Replay_accepts_the_measured_native_packager_shape_and_reaches_the_loader()
    {
        var adapter = new FakeAdapter
        {
            Observation = MatchingObservation()
        };
        var host = new FishWarehouseEmployeeReplayHost(
            adapter,
            new[]
            {
                new FishWarehouseEmployeeReplayDescriptor(EmployeeGuid, "PackagerData", "Packager", MeasuredPackagerRawJson)
            },
            Array.Empty<FishWarehouseUnsupportedEmployeeRecord>(),
            () => 0f);

        var result = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Succeeded, result.State);
        Assert.Equal(1, adapter.PrimaryLoadCalls);
        Assert.Equal(1, result.ReplayedEmployeeCount);
        Assert.DoesNotContain(result.UnsupportedEmployees, record => record.RawJson == MeasuredPackagerRawJson);
    }

    [Fact]
    public void Replay_does_not_fail_when_native_employee_moves_after_spawn()
    {
        var adapter = new FakeAdapter
        {
            Observation = MatchingObservation() with
            {
                PositionX = MatchingObservation().PositionX + 4f,
                PositionZ = MatchingObservation().PositionZ - 3f
            }
        };
        var host = CreateHost(adapter);

        var result = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Succeeded, result.State);
        Assert.Equal(1, result.ReplayedEmployeeCount);
    }

    [Fact]
    public void Replay_accepts_an_employee_without_a_home_when_runtime_has_no_home()
    {
        var adapter = new FakeAdapter
        {
            Observation = MatchingObservation() with { HomeGuid = string.Empty }
        };
        var noHomeRawJson = PackagerRawJson.Replace(HomeGuid, string.Empty, StringComparison.Ordinal);
        var host = new FishWarehouseEmployeeReplayHost(
            adapter,
            new[]
            {
                new FishWarehouseEmployeeReplayDescriptor(EmployeeGuid, "PackagerData", "Packager", noHomeRawJson)
            },
            Array.Empty<FishWarehouseUnsupportedEmployeeRecord>(),
            () => 0f);

        var result = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Succeeded, result.State);
        Assert.Equal(1, result.ReplayedEmployeeCount);
        Assert.Empty(result.UnsupportedEmployees);
    }

    [Fact]
    public void Replay_restores_ten_employees_with_independent_configuration()
    {
        var adapter = new FakeAdapter();
        var descriptors = Enumerable.Range(0, 10)
            .Select(CreateDescriptor)
            .ToArray();
        adapter.Observations = Enumerable.Range(0, 10)
            .Select(CreateObservation)
            .ToDictionary(observation => observation.Guid, StringComparer.OrdinalIgnoreCase);

        var host = new FishWarehouseEmployeeReplayHost(
            adapter,
            descriptors,
            Array.Empty<FishWarehouseUnsupportedEmployeeRecord>(),
            () => 0f);

        var result = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Succeeded, result.State);
        Assert.Equal(10, result.CapturedEmployeeCount);
        Assert.Equal(10, result.SupportedCapturedEmployeeCount);
        Assert.Equal(10, result.ReplayedEmployeeCount);
        Assert.True(result.CanAdvanceEmployeesReplayed);
        Assert.Equal(descriptors.Select(descriptor => descriptor.Guid), adapter.LoadedGuids);
        Assert.Empty(result.UnsupportedEmployees);
    }

    [Fact]
    public void Replay_is_idempotent_after_all_ten_employees_are_replayed()
    {
        var adapter = new FakeAdapter();
        var descriptors = Enumerable.Range(0, 10).Select(CreateDescriptor).ToArray();
        adapter.Observations = Enumerable.Range(0, 10)
            .Select(CreateObservation)
            .ToDictionary(observation => observation.Guid, StringComparer.OrdinalIgnoreCase);
        var host = new FishWarehouseEmployeeReplayHost(
            adapter, descriptors, Array.Empty<FishWarehouseUnsupportedEmployeeRecord>(), () => 0f);

        var first = host.Replay(navigationReady: true);
        var loadedCount = adapter.LoadedGuids.Count;
        var second = host.Replay(navigationReady: true);

        Assert.Equal(first, second);
        Assert.Equal(loadedCount, adapter.LoadedGuids.Count);
        Assert.Equal(10, loadedCount);
    }

    [Fact]
    public void Replay_does_not_accept_a_primary_employee_with_a_different_saved_identity()
    {
        var now = 0f;
        var adapter = new FakeAdapter
        {
            Observation = MatchingObservation() with { Id = "worker-999" },
            FallbackCreationResult = FishWarehouseEmployeeFallbackCreationResult.Failed
        };
        var host = CreateHost(adapter, () => now);

        Assert.Equal(FishWarehouseEmployeeReplayState.Pending, host.Replay(navigationReady: true).State);

        now = 3f;
        var result = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Failed, result.State);
        Assert.Contains("primary-timeout", result.PrimaryFailureReason, StringComparison.Ordinal);
        Assert.Contains("id-mismatch", result.PrimaryFailureReason, StringComparison.Ordinal);
        Assert.Equal(1, adapter.FallbackCreationCalls);
    }

    [Fact]
    public void Replay_preserves_unsupported_records_verbatim_and_warns_for_each_load_host()
    {
        const string existingRawJson = "{\"older\":true,\"spacing\":\"must remain\"}";
        const string capturedRawJson = "{ \"newer\" : false }";
        var messages = new List<string>();
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            "00000000-0000-0000-0000-000000000666",
            "BotanistData",
            "Botanist",
            capturedRawJson);
        var existing = new FishWarehouseUnsupportedEmployeeRecord(
            descriptor.Guid,
            "LegacyEmployee",
            "Legacy",
            existingRawJson);
        var host = new FishWarehouseEmployeeReplayHost(
            new FakeAdapter(),
            new[] { descriptor },
            new[] { existing },
            () => 0f,
            messages.Add);

        var result = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Succeeded, result.State);
        var retained = Assert.Single(result.UnsupportedEmployees);
        Assert.Equal(existingRawJson, retained.RawJson);
        Assert.Contains(messages, message =>
            message.Contains("unsupported", StringComparison.OrdinalIgnoreCase) &&
            message.Contains(descriptor.Guid, StringComparison.Ordinal));
    }

    [Fact]
    public void Replay_cannot_advance_employee_phase_when_a_supported_record_has_no_usable_loader()
    {
        var adapter = new FakeAdapter
        {
            PrimaryLoadResult = FishWarehouseEmployeePrimaryLoadResult.LoaderUnavailable
        };
        var host = CreateHost(adapter);

        var result = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Succeeded, result.State);
        Assert.Equal(1, result.SupportedCapturedEmployeeCount);
        Assert.Equal(0, result.ReplayedEmployeeCount);
        Assert.False(result.CanAdvanceEmployeesReplayed);
        Assert.Empty(result.UnsupportedEmployees);
    }

    [Fact]
    public void Replay_failure_does_not_classify_supported_records_as_unsupported()
    {
        var adapter = new FakeAdapter
        {
            PrimaryLoadResult = FishWarehouseEmployeePrimaryLoadResult.Failed,
            FallbackCreationResult = FishWarehouseEmployeeFallbackCreationResult.Failed
        };
        var laterRawJson = PackagerRawJson.Replace(EmployeeGuid, "00000000-0000-0000-0000-000000000888", StringComparison.Ordinal);
        var host = new FishWarehouseEmployeeReplayHost(
            adapter,
            new[]
            {
                new FishWarehouseEmployeeReplayDescriptor(EmployeeGuid, "PackagerData", "Packager", PackagerRawJson),
                new FishWarehouseEmployeeReplayDescriptor("00000000-0000-0000-0000-000000000888", "PackagerData", "Packager", laterRawJson)
            },
            Array.Empty<FishWarehouseUnsupportedEmployeeRecord>(),
            () => 0f);

        var result = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Failed, result.State);
        Assert.Empty(result.UnsupportedEmployees);
    }

    private static FishWarehouseEmployeeReplayHost CreateHost(
        FakeAdapter adapter,
        Func<float>? realtimeSinceStartup = null) =>
        new(
            adapter,
            new[]
            {
                new FishWarehouseEmployeeReplayDescriptor(EmployeeGuid, "PackagerData", "Packager", PackagerRawJson)
            },
            Array.Empty<FishWarehouseUnsupportedEmployeeRecord>(),
            realtimeSinceStartup ?? (() => 0f));

    private static FishWarehouseEmployeeReplayObservedState MatchingObservation() =>
        new(
            IsAlive: true,
            Guid: EmployeeGuid,
            Identity: "Packager",
            Id: "worker-117",
            FirstName: "Mika",
            LastName: "Madsen",
            IsMale: false,
            AppearanceIndex: 7,
            PositionX: 10.25f,
            PositionY: 1.5f,
            PositionZ: -4.75f,
            RotationX: 0f,
            RotationY: 0.7071f,
            RotationZ: 0f,
            RotationW: 0.7071f,
            PropertyCode: "oc_fishwarehouse",
            PaidForToday: true,
            MoveItem: new FishWarehouseEmployeeMoveItemState("source-guid", "destination-guid", "{\"item\":\"fish\"}", 2),
            HomeGuid: HomeGuid,
            StationGuids: new[] { StationThreeGuid, StationOneGuid, StationTwoGuid });

    private static FishWarehouseEmployeeReplayDescriptor CreateDescriptor(int index)
    {
        var employeeGuid = GuidFor(0x111 + index);
        var homeGuid = GuidFor(0x211 + index);
        var stationGuids = new[] { GuidFor(0x311 + index * 3), GuidFor(0x312 + index * 3), GuidFor(0x313 + index * 3) };
        var sourceGuid = GuidFor(0x411 + index);
        var destinationGuid = GuidFor(0x511 + index);
        var rawJson = PackagerRawJson
            .Replace(EmployeeGuid, employeeGuid, StringComparison.Ordinal)
            .Replace(HomeGuid, homeGuid, StringComparison.Ordinal)
            .Replace(StationOneGuid, stationGuids[0], StringComparison.Ordinal)
            .Replace(StationTwoGuid, stationGuids[1], StringComparison.Ordinal)
            .Replace(StationThreeGuid, stationGuids[2], StringComparison.Ordinal)
            .Replace("source-guid", sourceGuid, StringComparison.Ordinal)
            .Replace("destination-guid", destinationGuid, StringComparison.Ordinal);
        return new FishWarehouseEmployeeReplayDescriptor(employeeGuid, "PackagerData", "Packager", rawJson);
    }

    private static FishWarehouseEmployeeReplayObservedState CreateObservation(int index)
    {
        var descriptor = CreateDescriptor(index);
        var expected = AssertParse(descriptor);
        return new FishWarehouseEmployeeReplayObservedState(
            IsAlive: true,
            Guid: expected.Guid,
            Identity: expected.Identity,
            Id: expected.Id,
            FirstName: expected.FirstName,
            LastName: expected.LastName,
            IsMale: expected.IsMale,
            AppearanceIndex: expected.AppearanceIndex,
            PositionX: expected.PositionX,
            PositionY: expected.PositionY,
            PositionZ: expected.PositionZ,
            RotationX: expected.RotationX,
            RotationY: expected.RotationY,
            RotationZ: expected.RotationZ,
            RotationW: expected.RotationW,
            PropertyCode: expected.PropertyCode,
            PaidForToday: expected.PaidForToday,
            MoveItem: expected.MoveItem,
            HomeGuid: expected.HomeGuid,
            StationGuids: expected.StationGuids);
    }

    private static FishWarehouseEmployeeReplayExpectedState AssertParse(
        FishWarehouseEmployeeReplayDescriptor descriptor)
    {
        Assert.True(descriptor.TryParseExpectedState(out var expected, out var failureReason), failureReason);
        return expected;
    }

    private static string GuidFor(int value) => $"00000000-0000-0000-0000-{value:000000000000}";

    private sealed class FakeAdapter : IFishWarehouseNativeEmployeeReplayAdapter
    {
        public IReadOnlyCollection<string> InventoryRestoredEmployeeGuids { get; } = Array.Empty<string>();
        public bool IsGuidRegistered { get; set; }
        public int GuidRegistrationCalls { get; private set; }
        public int PrimaryLoadCalls { get; private set; }
        public int FallbackCreationCalls { get; private set; }
        public FishWarehouseEmployeePrimaryLoadResult PrimaryLoadResult { get; set; } = FishWarehouseEmployeePrimaryLoadResult.Started;
        public FishWarehouseEmployeeFallbackCreationResult FallbackCreationResult { get; set; } = FishWarehouseEmployeeFallbackCreationResult.Started;
        public FishWarehouseEmployeeFallbackConfigurationResult FallbackConfigurationResult { get; set; } = FishWarehouseEmployeeFallbackConfigurationResult.Configured;
        public FishWarehouseEmployeeReplayObservedState? Observation { get; set; }
        public FishWarehouseEmployeeReplayObservedState? ObservationAfterConfiguration { get; set; }
        public Queue<FishWarehouseEmployeeActiveMoveRestoreResult>? ActiveMoveRestoreResults { get; set; }
        public int ActiveMoveRestoreCalls { get; private set; }
        public IReadOnlyDictionary<string, FishWarehouseEmployeeReplayObservedState> Observations { get; set; } =
            new Dictionary<string, FishWarehouseEmployeeReplayObservedState>(StringComparer.OrdinalIgnoreCase);
        public List<string> LoadedGuids { get; } = new();
        public int FallbackConfigurationCalls { get; private set; }
        public FishWarehouseEmployeeReplayDescriptor? LastPrimaryDescriptor { get; private set; }

        public bool TryIsGuidRegistered(string savedGuid, out bool isRegistered, out string? failureReason)
        {
            GuidRegistrationCalls++;
            isRegistered = IsGuidRegistered;
            failureReason = null;
            return true;
        }

        public FishWarehouseEmployeePrimaryLoadResult TryLoadPrimary(
            FishWarehouseEmployeeReplayDescriptor descriptor,
            out string? failureReason)
        {
            PrimaryLoadCalls++;
            LastPrimaryDescriptor = descriptor;
            LoadedGuids.Add(descriptor.Guid);
            failureReason = PrimaryLoadResult == FishWarehouseEmployeePrimaryLoadResult.Failed
                ? "loader threw"
                : null;
            return PrimaryLoadResult;
        }

        public FishWarehouseEmployeeObservationResult TryObserveEmployee(
            string savedGuid,
            out FishWarehouseEmployeeReplayObservedState? observation,
            out string? failureReason)
        {
            observation = Observations.TryGetValue(savedGuid, out var mappedObservation)
                ? mappedObservation
                : Observation;
            failureReason = null;
            return observation is null
                ? FishWarehouseEmployeeObservationResult.Pending
                : FishWarehouseEmployeeObservationResult.Ready;
        }

        public FishWarehouseEmployeeFallbackCreationResult TryCreateFallback(
            FishWarehouseEmployeeReplayDescriptor descriptor,
            FishWarehouseEmployeeReplayExpectedState expected,
            out string? failureReason)
        {
            FallbackCreationCalls++;
            failureReason = FallbackCreationResult == FishWarehouseEmployeeFallbackCreationResult.Failed
                ? "fallback creation failed"
                : null;
            return FallbackCreationResult;
        }

        public FishWarehouseEmployeeFallbackConfigurationResult TryConfigureFallback(
            FishWarehouseEmployeeReplayDescriptor descriptor,
            FishWarehouseEmployeeReplayExpectedState expected,
            out string? failureReason)
        {
            FallbackConfigurationCalls++;
            if (ObservationAfterConfiguration is not null)
                Observation = ObservationAfterConfiguration;
            failureReason = FallbackConfigurationResult == FishWarehouseEmployeeFallbackConfigurationResult.Failed
                ? "fallback configuration failed"
                : null;
            return FallbackConfigurationResult;
        }

        public FishWarehouseEmployeeActiveMoveRestoreResult TryRestoreActiveMove(
            FishWarehouseEmployeeReplayDescriptor descriptor,
            FishWarehouseEmployeeReplayExpectedState expected,
            out string? failureReason)
        {
            ActiveMoveRestoreCalls++;
            failureReason = null;
            return ActiveMoveRestoreResults is { Count: > 0 }
                ? ActiveMoveRestoreResults.Dequeue()
                : expected.MoveItem is null
                    ? FishWarehouseEmployeeActiveMoveRestoreResult.NotRequired
                    : FishWarehouseEmployeeActiveMoveRestoreResult.Restored;
        }
    }

    private sealed class EmptyObjectReplayAdapter : IFishWarehouseNativePropertyReplayAdapter
    {
        public bool IsGuidRegistered(string guid) => false;

        public bool TryGetObjectLoaderLoadOrder(string dataType, out int loadOrder)
        {
            loadOrder = 0;
            return false;
        }

        public void LoadObject(int payloadIndex) => throw new InvalidOperationException("No objects were captured.");

        public FishWarehouseRestoredObjectObservation ObserveObject(string guid) =>
            new(false, null, false);
    }

    private const string PackagerRawJson = """
        {
          "DataType":"PackagerData",
          "BaseData":{
            "GUID":"00000000-0000-0000-0000-000000000111",
            "Identity":"Packager",
            "ID":"worker-117",
            "FirstName":"Mika",
            "LastName":"Madsen",
            "IsMale":false,
            "AppearanceIndex":7,
            "Position":{"x":10.25,"y":1.5,"z":-4.75},
            "Rotation":{"x":0,"y":0.7071,"z":0,"w":0.7071},
            "PropertyCode":"oc_fishwarehouse",
            "PaidForToday":true,
            "BedGUID":"00000000-0000-0000-0000-000000000222",
            "MoveItemData":{"SourceGUID":"source-guid","DestinationGUID":"destination-guid","TemplateItemJSON":"{\"item\":\"fish\"}","GrabbedItemQuantity":2}
          },
          "AdditionalDatas":[
            {"Name":"Configuration","Contents":{"Bed":{"ObjectGUID":"00000000-0000-0000-0000-000000000222"},"Stations":{"ObjectGUIDs":["00000000-0000-0000-0000-000000000333","00000000-0000-0000-0000-000000000444","00000000-0000-0000-0000-000000000555"]}}}
          ]
        }
        """;

    private const string MeasuredPackagerRawJson = """
        {
          "DataType":"PackagerData",
          "BaseData":{
            "GUID":"00000000-0000-0000-0000-000000000111",
            "Identity":"Packager",
            "ID":"worker-117",
            "FirstName":"Mika",
            "LastName":"Madsen",
            "IsMale":false,
            "AppearanceIndex":7,
            "Position":{"x":10.25,"y":1.5,"z":-4.75},
            "Rotation":{"x":0,"y":0.7071,"z":0,"w":0.7071},
            "PropertyCode":"oc_fishwarehouse",
            "PaidForToday":true,
            "BedGUID":"00000000-0000-0000-0000-000000000222",
            "MoveItemData":{"SourceGUID":"source-guid","DestinationGUID":"destination-guid","TemplateItemJSON":"{\"item\":\"fish\"}","GrabbedItemQuantity":2}
          },
          "AdditionalDatas":[
            {"Name":"Configuration","Contents":{"StationGUIDs":["00000000-0000-0000-0000-000000000333","00000000-0000-0000-0000-000000000444","00000000-0000-0000-0000-000000000555"]}}
          ]
        }
        """;
}
