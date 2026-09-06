using OrganizedCrime.Runtime;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseOwnershipTests
{
    [Fact]
    public void Ownership_unlock_is_rejected_until_runtime_property_is_spawned()
    {
        var host = new OwnershipHost(startSucceeds: true, ownershipSucceeds: true, cleanupSucceeds: true);
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.Update();
        var resultBeforeSpawn = service.TryUnlockDeveloperLifecycle();

        Assert.False(resultBeforeSpawn);
        Assert.Equal(0, host.OwnershipCalls);
    }

    [Fact]
    public void Ownership_unlock_is_forwarded_after_runtime_property_is_spawned()
    {
        var host = new OwnershipHost(startSucceeds: true, ownershipSucceeds: true, cleanupSucceeds: true);
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.Update();
        var start = service.ToggleDeveloperLifecycle();
        var result = service.TryUnlockDeveloperLifecycle();

        Assert.True(start.Succeeded);
        Assert.True(result);
        Assert.Equal(1, host.OwnershipCalls);
        Assert.Equal(RuntimePropertyLifecycleState.Spawned, service.State);
        Assert.Equal(FishWarehouseSaveState.OwnedState(), service.CaptureSaveState());
    }

    [Fact]
    public void Ownership_unlock_failure_cleans_up_and_enters_terminal_state()
    {
        var host = new OwnershipHost(startSucceeds: true, ownershipSucceeds: false, cleanupSucceeds: true);
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.Update();
        service.ToggleDeveloperLifecycle();
        var result = service.TryUnlockDeveloperLifecycle();

        Assert.False(result);
        Assert.Equal(1, host.OwnershipCalls);
        Assert.Equal(1, host.StopCalls);
        Assert.Equal(RuntimePropertyLifecycleState.Failed, service.State);
    }

    [Fact]
    public void Loaded_owned_state_is_recorded_without_starting_runtime_property()
    {
        var host = new OwnershipHost(startSucceeds: true, ownershipSucceeds: true, cleanupSucceeds: true);
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.ApplyLoadedSaveState(FishWarehouseSaveState.OwnedState());

        Assert.Equal(RuntimePropertyLifecycleState.Unavailable, service.State);
        Assert.Equal(FishWarehouseSaveState.OwnedState(), service.CaptureSaveState());
        Assert.Equal(0, host.OwnershipCalls);
        Assert.Equal(0, host.StartCalls);
    }

    [Fact]
    public void Loaded_owned_state_restores_only_after_runtime_readiness()
    {
        var ready = false;
        var host = new OwnershipHost(startSucceeds: true, ownershipSucceeds: true, cleanupSucceeds: true);
        var service = new FishWarehouseRuntimeService(host, () => ready);

        service.ApplyLoadedSaveState(FishWarehouseSaveState.OwnedState());
        service.RequestLoadedOwnedRestore();

        service.Update();
        Assert.Equal(RuntimePropertyLifecycleState.Unavailable, service.State);
        Assert.Equal(0, host.StartCalls);
        Assert.Equal(0, host.OwnershipCalls);

        ready = true;
        service.Update();

        Assert.Equal(RuntimePropertyLifecycleState.Spawned, service.State);
        Assert.Equal(1, host.StartCalls);
        Assert.Equal(1, host.OwnershipCalls);
        Assert.True(service.OwnershipUnlocked);
    }

    [Fact]
    public void Unowned_state_does_not_request_automatic_restore()
    {
        var host = new OwnershipHost(startSucceeds: true, ownershipSucceeds: true, cleanupSucceeds: true);
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.ApplyLoadedSaveState(FishWarehouseSaveState.UnownedState());
        service.RequestLoadedOwnedRestore();
        service.Update();

        Assert.Equal(RuntimePropertyLifecycleState.Ready, service.State);
        Assert.Equal(0, host.StartCalls);
        Assert.Equal(0, host.OwnershipCalls);
    }

    [Fact]
    public void Loaded_owned_state_restores_when_load_completes_after_readiness()
    {
        var host = new OwnershipHost(startSucceeds: true, ownershipSucceeds: true, cleanupSucceeds: true);
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.Update();
        service.ApplyLoadedSaveState(FishWarehouseSaveState.OwnedState());
        service.RequestLoadedOwnedRestore();
        service.Update();

        Assert.Equal(RuntimePropertyLifecycleState.Spawned, service.State);
        Assert.Equal(1, host.StartCalls);
        Assert.Equal(1, host.OwnershipCalls);
    }

    [Fact]
    public void Preload_unloads_existing_runtime_before_the_next_save_load_restore()
    {
        var host = new OwnershipHost(startSucceeds: true, ownershipSucceeds: true, cleanupSucceeds: true);
        var service = new FishWarehouseRuntimeService(host, () => true);

        service.Update();
        Assert.True(service.ToggleDeveloperLifecycle().Succeeded);
        Assert.True(service.TryUnlockDeveloperLifecycle());

        Assert.True(service.PrepareForLoad());
        Assert.Equal(RuntimePropertyLifecycleState.Unavailable, service.State);
        Assert.Equal(1, host.StopCalls);

        service.ApplyLoadedSaveState(FishWarehouseSaveState.OwnedState());
        service.RequestLoadedOwnedRestore();
        service.Update();

        Assert.Equal(RuntimePropertyLifecycleState.Spawned, service.State);
        Assert.Equal(2, host.StartCalls);
        Assert.Equal(2, host.OwnershipCalls);
    }

    [Fact]
    public void Repeated_ownership_unlock_is_idempotent_after_success()
    {
        var host = new OwnershipHost(startSucceeds: true, ownershipSucceeds: true, cleanupSucceeds: true);
        var messages = new List<string>();
        var service = new FishWarehouseRuntimeService(host, () => true, log: messages.Add);

        service.Update();
        service.ToggleDeveloperLifecycle();

        Assert.True(service.TryUnlockDeveloperLifecycle());
        Assert.True(service.TryUnlockDeveloperLifecycle());
        Assert.Equal(1, host.OwnershipCalls);
        Assert.Equal(RuntimePropertyLifecycleState.Spawned, service.State);
        Assert.DoesNotContain(messages, message => message.Contains("already owned in memory", StringComparison.Ordinal));
    }

    [Fact]
    public void Employee_home_placement_is_rejected_before_ownership_and_forwarded_afterward()
    {
        var placementCalls = 0;
        var host = new OwnershipHost(startSucceeds: true, ownershipSucceeds: true, cleanupSucceeds: true);
        var service = new FishWarehouseRuntimeService(
            host,
            () => true,
            () =>
            {
                placementCalls++;
                return true;
            });

        service.Update();
        service.ToggleDeveloperLifecycle();
        Assert.False(service.TryPlaceEmployeeHome());

        Assert.True(service.TryUnlockDeveloperLifecycle());
        Assert.True(service.TryPlaceEmployeeHome());
        Assert.Equal(1, placementCalls);
    }

    [Fact]
    public void Loaded_employee_home_state_is_restored_after_owned_property_readiness()
    {
        var placementCalls = 0;
        var host = new OwnershipHost(startSucceeds: true, ownershipSucceeds: true, cleanupSucceeds: true);
        var service = new FishWarehouseRuntimeService(
            host,
            () => true,
            () =>
            {
                placementCalls++;
                return true;
            });

        service.ApplyLoadedSaveState(FishWarehouseSaveState.OwnedState() with { EmployeeHomePlaced = true });
        service.RequestLoadedOwnedRestore();
        service.Update();

        Assert.Equal(1, placementCalls);
        Assert.True(service.CaptureSaveState().EmployeeHomePlaced);
    }

    private sealed class OwnershipHost : IRuntimePropertyHost
    {
        private readonly bool _startSucceeds;
        private readonly bool _ownershipSucceeds;
        private readonly bool _cleanupSucceeds;

        public OwnershipHost(bool startSucceeds, bool ownershipSucceeds, bool cleanupSucceeds)
        {
            _startSucceeds = startSucceeds;
            _ownershipSucceeds = ownershipSucceeds;
            _cleanupSucceeds = cleanupSucceeds;
        }

        public int OwnershipCalls { get; private set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public FishWarehouseContinuousNavigationState EmployeeNavigationState =>
            FishWarehouseContinuousNavigationState.Inactive;
        public bool EmployeeNavigationReady => false;
        public int? EmployeeNavigationAgentTypeId => null;
        public bool PersistenceRuntimeReady => true;

        public void ConfigurePersistenceReplay(bool objectReplayRequired, int? employeeAgentTypeId)
        {
        }

        public void MarkPersistenceObjectsReplayed()
        {
        }

        public bool TryAdoptRestoredEmployeeHome() => false;

        public RuntimePropertyHostResult Start(OrganizedCrime.Model.RuntimePropertyDefinition definition) =>
            StartCore();

        private RuntimePropertyHostResult StartCore()
        {
            StartCalls++;
            return new(
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

        public bool TrySetOwned()
        {
            OwnershipCalls++;
            return _ownershipSucceeds;
        }

        public void UpdateOwnedFeatures()
        {
        }

        public RuntimePropertyHostResult Stop()
        {
            StopCalls++;
            return new(
                Succeeded: false,
                Stage: "cleanup",
                PropertyRegistered: false,
                NetworkRegistrationPassed: true,
                SpawnPassed: true,
                DespawnPassed: _cleanupSucceeds,
                CleanupPassed: _cleanupSucceeds,
                AuthoredCollectionUnchanged: true,
                DocksNetworkIdentityUnchanged: true,
                FailureReason: _cleanupSucceeds ? null : "fake cleanup failure");
        }
    }
}
