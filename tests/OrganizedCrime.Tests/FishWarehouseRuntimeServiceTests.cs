using OrganizedCrime.Runtime;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseRuntimeServiceTests
{
    [Fact]
    public void Capture_save_state_uses_v2_without_name_only_employee_assignment()
    {
        var host = new FakeHost(startSucceeds: true, cleanupSucceeds: true);
        var service = new FishWarehouseRuntimeService(host, () => true);

        var state = service.CaptureSaveState();

        Assert.Equal(2, state.SchemaVersion);
        Assert.Null(state.EmployeeAgentTypeId);
        Assert.Null(state.Replay);
        Assert.Empty(state.UnsupportedEmployees);
    }

    [Fact]
    public void Capture_save_state_forwards_the_validated_navigation_agent_id_after_navigation_is_ready()
    {
        var host = new FakeHost(startSucceeds: true, cleanupSucceeds: true, useNavigationCoordinator: true)
        {
            ResolvedEmployeeNavigationAgentTypeId = 47
        };
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.Update();
        Assert.True(service.ToggleDeveloperLifecycle().Succeeded);
        Assert.True(service.TryUnlockDeveloperLifecycle());
        service.Update();
        service.Update();

        Assert.True(service.EmployeeNavigationReady);
        Assert.Equal(47, service.CaptureSaveState().EmployeeAgentTypeId);
    }

    [Fact]
    public void Loaded_employee_agent_id_is_configured_before_owned_restore_starts_the_host()
    {
        var host = new FakeHost(startSucceeds: true, cleanupSucceeds: true, useNavigationCoordinator: true);
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.ApplyLoadedSaveState(FishWarehouseSaveState.OwnedState() with { EmployeeAgentTypeId = 47 });
        service.RequestLoadedOwnedRestore();
        service.Update();

        Assert.Equal(47, host.ConfiguredEmployeeAgentTypeId);
        Assert.Equal(47, host.EmployeeAgentTypeIdWhenStarted);
    }

    [Fact]
    public void Loaded_replay_keeps_native_navigation_gated_until_objects_are_marked_replayed()
    {
        var host = new FakeHost(startSucceeds: true, cleanupSucceeds: true, useNavigationCoordinator: true);
        var service = new FishWarehouseRuntimeService(host, () => true);
        var checkpoint = new FishWarehouseReplayCheckpoint(
            "generation",
            "folder",
            FishWarehouseReplayPhase.Captured,
            "OrganizedCrime/fish-warehouse.snapshot.json",
            "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
            1,
            1);

        service.ApplyLoadedSaveState(FishWarehouseSaveState.OwnedState() with { Replay = checkpoint });
        service.RequestLoadedOwnedRestore();
        service.Update();
        service.Update();
        service.Update();

        Assert.True(host.ConfiguredObjectReplayRequired);
        Assert.Equal(FishWarehouseContinuousNavigationState.Inactive, service.EmployeeNavigationState);

        service.MarkPersistenceObjectsReplayed();
        service.Update();

        Assert.Equal(FishWarehouseContinuousNavigationState.Settling, service.EmployeeNavigationState);
    }

    [Fact]
    public void Service_waits_for_readiness_then_starts_and_stops_host()
    {
        var ready = false;
        var host = new FakeHost(startSucceeds: true, cleanupSucceeds: true);
        var service = new FishWarehouseRuntimeService(host, () => ready);

        service.Update();
        Assert.Equal(RuntimePropertyLifecycleState.Unavailable, service.State);

        ready = true;
        service.Update();
        Assert.Equal(RuntimePropertyLifecycleState.Ready, service.State);

        var start = service.ToggleDeveloperLifecycle();
        Assert.True(start.Succeeded);
        Assert.Equal(RuntimePropertyLifecycleState.Spawned, service.State);

        var stop = service.ToggleDeveloperLifecycle();
        Assert.True(stop.CleanupPassed);
        Assert.Equal(RuntimePropertyLifecycleState.Unavailable, service.State);
        Assert.Equal(1, host.StartCalls);
        Assert.Equal(1, host.StopCalls);
    }

    [Fact]
    public void Service_enters_terminal_failed_state_when_start_cleanup_is_unproven()
    {
        var host = new FakeHost(startSucceeds: false, cleanupSucceeds: false);
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.Update();
        var result = service.ToggleDeveloperLifecycle();

        Assert.False(result.Succeeded);
        Assert.Equal(RuntimePropertyLifecycleState.Failed, service.State);
        Assert.Equal(RuntimePropertyLifecycleState.Failed, service.State);
    }

    [Fact]
    public void Occupied_loading_dock_refuses_unload_without_making_service_terminal()
    {
        var host = new FakeHost(
            startSucceeds: true,
            cleanupSucceeds: true,
            stopStage: "occupied-loading-docks");
        var messages = new List<string>();
        var service = new FishWarehouseRuntimeService(host, () => true, log: messages.Add);

        service.Update();
        service.ToggleDeveloperLifecycle();
        var stop = service.ToggleDeveloperLifecycle();

        Assert.False(stop.Succeeded);
        Assert.Equal("occupied-loading-docks", stop.Stage);
        Assert.Equal(RuntimePropertyLifecycleState.Spawned, service.State);
        Assert.Empty(host.StopSequence);
        Assert.Contains(messages, message => message.Contains("unload refused", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Owned_property_remains_spawned_when_employee_navigation_fails()
    {
        var host = new FakeHost(
            startSucceeds: true,
            cleanupSucceeds: true,
            useNavigationCoordinator: true,
            navigationBuildSucceeds: false);
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.Update();
        Assert.True(service.ToggleDeveloperLifecycle().Succeeded);
        Assert.True(service.TryUnlockDeveloperLifecycle());

        service.Update();
        service.Update();

        Assert.Equal(RuntimePropertyLifecycleState.Spawned, service.State);
        Assert.True(service.OwnershipUnlocked);
        Assert.Equal(FishWarehouseContinuousNavigationState.Failed, service.EmployeeNavigationState);
        Assert.False(service.EmployeeNavigationReady);
        Assert.Equal(2, host.UpdateOwnedFeaturesCalls);
        Assert.Equal(
            new[] { "navigation-remove", "garage-restore", "room-dispose" },
            host.StopSequence);
    }

    [Fact]
    public void Employee_navigation_becomes_ready_only_after_a_ready_tick()
    {
        var host = new FakeHost(startSucceeds: true, cleanupSucceeds: true, useNavigationCoordinator: true);
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.Update();
        Assert.True(service.ToggleDeveloperLifecycle().Succeeded);
        Assert.True(service.TryUnlockDeveloperLifecycle());

        service.Update();
        Assert.Equal(FishWarehouseContinuousNavigationState.Settling, service.EmployeeNavigationState);
        Assert.False(service.EmployeeNavigationReady);

        service.Update();

        Assert.True(service.EmployeeNavigationReady);
        Assert.Equal(FishWarehouseContinuousNavigationState.Ready, service.EmployeeNavigationState);
        Assert.Equal(2, host.UpdateOwnedFeaturesCalls);
    }

    [Fact]
    public void Unload_delegates_navigation_removal_before_existing_host_cleanup()
    {
        var host = new FakeHost(startSucceeds: true, cleanupSucceeds: true, useNavigationCoordinator: true);
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.Update();
        Assert.True(service.ToggleDeveloperLifecycle().Succeeded);
        Assert.True(service.TryUnlockDeveloperLifecycle());
        service.Update();
        service.Update();
        Assert.True(service.EmployeeNavigationReady);

        Assert.True(service.ToggleDeveloperLifecycle().CleanupPassed);

        Assert.Equal(
            new[] { "navigation-remove", "garage-restore", "room-dispose", "existing-host-cleanup" },
            host.StopSequence);
        Assert.Equal(RuntimePropertyLifecycleState.Unavailable, service.State);
    }

    [Fact]
    public void Pending_native_navigation_cleanup_keeps_the_spawned_host_available_for_an_explicit_stop_retry()
    {
        var host = new FakeHost(
            startSucceeds: true,
            cleanupSucceeds: false,
            stopStage: "pending-native-navigation-cleanup");
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.Update();
        Assert.True(service.ToggleDeveloperLifecycle().Succeeded);
        Assert.True(service.TryUnlockDeveloperLifecycle());

        RuntimePropertyHostResult stop = service.ToggleDeveloperLifecycle();

        Assert.False(stop.CleanupPassed);
        Assert.Equal("pending-native-navigation-cleanup", stop.Stage);
        Assert.Equal(RuntimePropertyLifecycleState.Spawned, service.State);
    }

    [Fact]
    public void Fresh_load_resets_a_failed_navigation_lifecycle_without_retrying_the_previous_run()
    {
        var host = new FakeHost(
            startSucceeds: true,
            cleanupSucceeds: true,
            useNavigationCoordinator: true,
            navigationBuildSucceeds: false);
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.Update();
        Assert.True(service.ToggleDeveloperLifecycle().Succeeded);
        Assert.True(service.TryUnlockDeveloperLifecycle());
        service.Update();
        service.Update();

        Assert.Equal(1, host.StartCalls);
        Assert.Equal(2, host.UpdateOwnedFeaturesCalls);
        Assert.Equal(FishWarehouseContinuousNavigationState.Failed, service.EmployeeNavigationState);

        Assert.True(service.PrepareForLoad());
        host.NavigationBuildSucceeds = true;
        service.ApplyLoadedSaveState(FishWarehouseSaveState.OwnedState());
        service.RequestLoadedOwnedRestore();
        service.Update();

        Assert.Equal(2, host.StartCalls);
        Assert.Equal(2, host.OwnershipCalls);
        Assert.Equal(RuntimePropertyLifecycleState.Spawned, service.State);
        Assert.Equal(FishWarehouseContinuousNavigationState.Inactive, service.EmployeeNavigationState);

        service.Update();
        service.Update();

        Assert.Equal(4, host.UpdateOwnedFeaturesCalls);
        Assert.True(service.EmployeeNavigationReady);
    }

    private sealed class FakeHost : IRuntimePropertyHost
    {
        private readonly bool _startSucceeds;
        private readonly bool _cleanupSucceeds;
        private readonly string _stopStage;
        private readonly bool _useNavigationCoordinator;
        private FishWarehouseContinuousNavigationCoordinator? _navigationCoordinator;
        private float _navigationTime;

        public FakeHost(
            bool startSucceeds,
            bool cleanupSucceeds,
            string stopStage = "cleanup",
            bool useNavigationCoordinator = false,
            bool navigationBuildSucceeds = true)
        {
            _startSucceeds = startSucceeds;
            _cleanupSucceeds = cleanupSucceeds;
            _stopStage = stopStage;
            _useNavigationCoordinator = useNavigationCoordinator;
            NavigationBuildSucceeds = navigationBuildSucceeds;
        }

        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int OwnershipCalls { get; private set; }
        public int UpdateOwnedFeaturesCalls { get; private set; }
        public List<string> StopSequence { get; } = new();
        public FishWarehouseContinuousNavigationState NavigationState { get; set; }
        public bool NavigationBuildSucceeds { get; set; }
        public int? ResolvedEmployeeNavigationAgentTypeId { get; set; }
        public int? ConfiguredEmployeeAgentTypeId { get; private set; }
        public int? EmployeeAgentTypeIdWhenStarted { get; private set; }
        public bool ConfiguredObjectReplayRequired { get; private set; }
        private bool _objectsReplayed;

        public FishWarehouseContinuousNavigationState EmployeeNavigationState =>
            _navigationCoordinator?.State ?? NavigationState;
        public bool EmployeeNavigationReady =>
            EmployeeNavigationState == FishWarehouseContinuousNavigationState.Ready;
        public int? EmployeeNavigationAgentTypeId =>
            EmployeeNavigationReady ? ResolvedEmployeeNavigationAgentTypeId : null;
        public bool PersistenceRuntimeReady => true;

        public void ConfigurePersistenceReplay(bool objectReplayRequired, int? employeeAgentTypeId)
        {
            ConfiguredObjectReplayRequired = objectReplayRequired;
            ConfiguredEmployeeAgentTypeId = employeeAgentTypeId;
        }

        public void MarkPersistenceObjectsReplayed()
        {
            _objectsReplayed = true;
        }

        public bool TryAdoptRestoredEmployeeHome() => false;

        public bool TrySetOwned()
        {
            OwnershipCalls++;
            return true;
        }

        public void UpdateOwnedFeatures()
        {
            UpdateOwnedFeaturesCalls++;
            if (ConfiguredObjectReplayRequired && !_objectsReplayed)
                return;
            if (_navigationCoordinator is null)
                return;

            if (_navigationCoordinator.State == FishWarehouseContinuousNavigationState.Inactive)
            {
                _navigationCoordinator.Start(_navigationTime);
                return;
            }

            _navigationTime += 0.5f;
            _navigationCoordinator.Tick(_navigationTime);
        }

        public RuntimePropertyHostResult Start(OrganizedCrime.Model.RuntimePropertyDefinition definition)
        {
            StartCalls++;
            EmployeeAgentTypeIdWhenStarted = ConfiguredEmployeeAgentTypeId;
            if (_useNavigationCoordinator)
            {
                _navigationTime = 0f;
                _navigationCoordinator = new FishWarehouseContinuousNavigationCoordinator(
                    new FakeNavigationActions(StopSequence)
                    {
                        BuildSucceeds = NavigationBuildSucceeds
                    });
                NavigationState = FishWarehouseContinuousNavigationState.Inactive;
            }
            return new RuntimePropertyHostResult(
                Succeeded: _startSucceeds,
                Stage: _startSucceeds ? "spawn" : "cleanup",
                PropertyRegistered: _startSucceeds,
                NetworkRegistrationPassed: _startSucceeds,
                SpawnPassed: _startSucceeds,
                DespawnPassed: _cleanupSucceeds,
                CleanupPassed: _cleanupSucceeds,
                AuthoredCollectionUnchanged: true,
                DocksNetworkIdentityUnchanged: true,
                FailureReason: _startSucceeds ? null : "fake start failure");
        }

        public RuntimePropertyHostResult Stop()
        {
            StopCalls++;
            if (_stopStage == "occupied-loading-docks")
                return BuildStopResult();

            _navigationCoordinator?.Stop();
            _navigationCoordinator = null;
            StopSequence.Add("existing-host-cleanup");
            return BuildStopResult();
        }

        private RuntimePropertyHostResult BuildStopResult()
        {
            return new RuntimePropertyHostResult(
                Succeeded: false,
                Stage: _stopStage,
                PropertyRegistered: false,
                NetworkRegistrationPassed: true,
                SpawnPassed: true,
                DespawnPassed: _stopStage == "occupied-loading-docks" ? false : _cleanupSucceeds,
                CleanupPassed: _stopStage == "occupied-loading-docks" || _cleanupSucceeds,
                AuthoredCollectionUnchanged: true,
                DocksNetworkIdentityUnchanged: true,
                FailureReason: _stopStage == "occupied-loading-docks"
                    ? "A Fish Warehouse loading dock is occupied or reserved by an active delivery."
                    : _cleanupSucceeds ? null : "fake cleanup failure");
        }

        private sealed class FakeNavigationActions : IFishWarehouseContinuousNavigationActions
        {
            private readonly List<string> _teardownCalls;

            public FakeNavigationActions(List<string> teardownCalls)
            {
                _teardownCalls = teardownCalls;
            }

            public bool BuildSucceeds { get; init; } = true;

            public bool TryPreflightGeometry() => true;
            public bool TryActivateRoom() => true;
            public bool TryOpenGarage() => true;
            public string GetBuildableAndConfigurableSignature() => "stable";
            public FishWarehouseNativeNavigationBuildResult TryBuildNavigation() =>
                BuildSucceeds
                    ? FishWarehouseNativeNavigationBuildResult.Succeeded
                    : FishWarehouseNativeNavigationBuildResult.Failed;
            public bool ValidateNavigation() => true;
            public bool RemoveNavigation() { _teardownCalls.Add("navigation-remove"); return true; }
            public void RestoreGarage() => _teardownCalls.Add("garage-restore");
            public void DisposeRoom() => _teardownCalls.Add("room-dispose");
        }
    }
}
