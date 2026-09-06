using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseEmployeeReplayFallbackTests
{
    [Fact]
    public void Replay_times_out_primary_then_attempts_fallback_once_and_bounds_configuration_verification_to_three_seconds()
    {
        var now = 0f;
        var adapter = new FallbackAdapter();
        var host = new FishWarehouseEmployeeReplayHost(
            adapter,
            new[]
            {
                new FishWarehouseEmployeeReplayDescriptor(
                    "00000000-0000-0000-0000-000000000777",
                    "PackagerData",
                    "Packager",
                    RawPackagerJson)
            },
            Array.Empty<OrganizedCrime.Persistence.FishWarehouseUnsupportedEmployeeRecord>(),
            () => now);

        Assert.Equal(FishWarehouseEmployeeReplayState.Pending, host.Replay(navigationReady: true).State);

        now = 3f;
        var afterPrimaryTimeout = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Pending, afterPrimaryTimeout.State);
        Assert.Contains("primary-timeout", afterPrimaryTimeout.PrimaryFailureReason, StringComparison.Ordinal);
        Assert.Equal(1, adapter.FallbackCreationCalls);

        now = 5.9f;
        Assert.Equal(FishWarehouseEmployeeReplayState.Pending, host.Replay(navigationReady: true).State);
        Assert.Equal(1, adapter.FallbackCreationCalls);

        now = 6f;
        var timedOut = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Failed, timedOut.State);
        Assert.Contains("fallback-configuration-timeout", timedOut.FailureReason, StringComparison.Ordinal);
        Assert.Equal(1, adapter.FallbackCreationCalls);
    }

    [Fact]
    public void Replay_keeps_fallback_configuration_pending_during_initialization_then_times_out_at_three_seconds()
    {
        var now = 0f;
        var adapter = new FallbackAdapter
        {
            ConfigurationResult = FishWarehouseEmployeeFallbackConfigurationResult.Pending
        };
        var host = new FishWarehouseEmployeeReplayHost(
            adapter,
            new[]
            {
                new FishWarehouseEmployeeReplayDescriptor(
                    "00000000-0000-0000-0000-000000000777",
                    "PackagerData",
                    "Packager",
                    RawPackagerJson)
            },
            Array.Empty<OrganizedCrime.Persistence.FishWarehouseUnsupportedEmployeeRecord>(),
            () => now);

        Assert.Equal(FishWarehouseEmployeeReplayState.Pending, host.Replay(navigationReady: true).State);

        now = 2.99f;
        Assert.Equal(FishWarehouseEmployeeReplayState.Pending, host.Replay(navigationReady: true).State);

        now = 3f;
        Assert.Equal(FishWarehouseEmployeeReplayState.Pending, host.Replay(navigationReady: true).State);

        now = 5.99f;
        Assert.Equal(FishWarehouseEmployeeReplayState.Pending, host.Replay(navigationReady: true).State);

        now = 6f;
        var timedOut = host.Replay(navigationReady: true);

        Assert.Equal(FishWarehouseEmployeeReplayState.Failed, timedOut.State);
        Assert.Contains("fallback-configuration-timeout", timedOut.FailureReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void Native_adapter_treats_unavailable_fallback_configuration_surfaces_as_pending(
        bool configurationAvailable,
        bool homeAvailable,
        bool stationsAvailable,
        bool routesAvailable)
    {
        Assert.Equal(
            FishWarehouseEmployeeFallbackConfigurationResult.Pending,
            FishWarehouseNativeEmployeeReplayAdapter.GetFallbackConfigurationSurfaceResult(
                configurationAvailable,
                homeAvailable,
                stationsAvailable,
                routesAvailable));
    }

    [Fact]
    public void Native_adapter_treats_all_fallback_configuration_surfaces_available_as_configured()
    {
        Assert.Equal(
            FishWarehouseEmployeeFallbackConfigurationResult.Configured,
            FishWarehouseNativeEmployeeReplayAdapter.GetFallbackConfigurationSurfaceResult(
                configurationAvailable: true,
                homeAvailable: true,
                stationsAvailable: true,
                routesAvailable: true));
    }

    [Fact]
    public void Native_adapter_rejects_a_malformed_saved_guid_before_guid_interop()
    {
        var interopCalls = 0;

        var invoked = FishWarehouseNativeEmployeeReplayAdapter.TryInvokeGuidInterop(
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

    private sealed class FallbackAdapter : IFishWarehouseNativeEmployeeReplayAdapter
    {
        public IReadOnlyCollection<string> InventoryRestoredEmployeeGuids { get; } = Array.Empty<string>();
        public int FallbackCreationCalls { get; private set; }
        public FishWarehouseEmployeeFallbackConfigurationResult ConfigurationResult { get; set; } = FishWarehouseEmployeeFallbackConfigurationResult.Configured;

        public bool TryIsGuidRegistered(string savedGuid, out bool isRegistered, out string? failureReason)
        {
            isRegistered = false;
            failureReason = null;
            return true;
        }

        public FishWarehouseEmployeePrimaryLoadResult TryLoadPrimary(
            FishWarehouseEmployeeReplayDescriptor descriptor,
            out string? failureReason)
        {
            failureReason = null;
            return FishWarehouseEmployeePrimaryLoadResult.Started;
        }

        public FishWarehouseEmployeeObservationResult TryObserveEmployee(
            string savedGuid,
            out FishWarehouseEmployeeReplayObservedState? observation,
            out string? failureReason)
        {
            observation = null;
            failureReason = null;
            return FishWarehouseEmployeeObservationResult.Pending;
        }

        public FishWarehouseEmployeeFallbackCreationResult TryCreateFallback(
            FishWarehouseEmployeeReplayDescriptor descriptor,
            FishWarehouseEmployeeReplayExpectedState expected,
            out string? failureReason)
        {
            FallbackCreationCalls++;
            failureReason = null;
            return FishWarehouseEmployeeFallbackCreationResult.Started;
        }

        public FishWarehouseEmployeeFallbackConfigurationResult TryConfigureFallback(
            FishWarehouseEmployeeReplayDescriptor descriptor,
            FishWarehouseEmployeeReplayExpectedState expected,
            out string? failureReason)
        {
            failureReason = null;
            return ConfigurationResult;
        }

        public FishWarehouseEmployeeActiveMoveRestoreResult TryRestoreActiveMove(
            FishWarehouseEmployeeReplayDescriptor descriptor,
            FishWarehouseEmployeeReplayExpectedState expected,
            out string? failureReason)
        {
            failureReason = null;
            return FishWarehouseEmployeeActiveMoveRestoreResult.NotRequired;
        }
    }

    private const string RawPackagerJson = """
        {
          "DataType":"PackagerData",
          "BaseData":{
            "GUID":"00000000-0000-0000-0000-000000000777",
            "Identity":"Packager",
            "ID":"worker-777",
            "FirstName":"Rae",
            "LastName":"Reed",
            "IsMale":true,
            "AppearanceIndex":3,
            "Position":{"x":1,"y":2,"z":3},
            "Rotation":{"x":0,"y":0,"z":0,"w":1},
            "PropertyCode":"oc_fishwarehouse",
            "PaidForToday":false,
            "BedGUID":"00000000-0000-0000-0000-000000000778"
          },
          "AdditionalDatas":[
            {"Name":"Configuration","Contents":{"Bed":{"ObjectGUID":"00000000-0000-0000-0000-000000000778"},"Stations":{"ObjectGUIDs":["00000000-0000-0000-0000-000000000779","00000000-0000-0000-0000-000000000780","00000000-0000-0000-0000-000000000781"]}}}
          ]
        }
        """;
}
