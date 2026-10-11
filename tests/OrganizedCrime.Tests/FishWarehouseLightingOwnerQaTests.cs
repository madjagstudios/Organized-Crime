using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseLightingOwnerQaTests
{
    [Fact]
    public void Lighting_keys_refuse_before_the_warehouse_is_spawned()
    {
        var logs = new List<string>();
        var lighting = new FakeLighting { LightingActive = true };
        var service = new FishWarehouseRuntimeService(new LightingGateHost(), () => true, log: logs.Add, lighting: lighting);

        Assert.All(RunAllLightingKeys(service), Assert.False);
        Assert.Equal(0, lighting.TotalCalls);
        Assert.Equal(8, logs.Count);
        Assert.All(logs, line => Assert.StartsWith("OC-109", line));
    }

    [Fact]
    public void Lighting_keys_refuse_when_spawned_but_not_owned()
    {
        var logs = new List<string>();
        var lighting = new FakeLighting { LightingActive = true };
        var service = new FishWarehouseRuntimeService(new LightingGateHost(), () => true, log: logs.Add, lighting: lighting);
        service.Update();
        service.ToggleDeveloperLifecycle();

        Assert.All(RunAllLightingKeys(service), Assert.False);
        Assert.Equal(0, lighting.TotalCalls);
    }

    [Fact]
    public void Lighting_keys_refuse_when_the_room_is_inactive()
    {
        var lighting = new FakeLighting { LightingActive = false };
        var service = SpawnOwnedService(lighting);

        Assert.All(RunAllLightingKeys(service), Assert.False);
        Assert.Equal(0, lighting.TotalCalls);
    }

    [Fact]
    public void Lighting_keys_refuse_without_a_lighting_backend()
    {
        var service = new FishWarehouseRuntimeService(new LightingGateHost(), () => true);
        service.Update();
        service.ToggleDeveloperLifecycle();
        service.TryUnlockDeveloperLifecycle();

        Assert.All(RunAllLightingKeys(service), Assert.False);
    }

    [Fact]
    public void Lighting_keys_delegate_when_spawned_owned_and_active()
    {
        var lighting = new FakeLighting { LightingActive = true };
        var service = SpawnOwnedService(lighting);

        Assert.All(RunAllLightingKeys(service), Assert.True);
        Assert.Equal(1, lighting.AddCalls);
        Assert.Equal(1, lighting.RemoveCalls);
        Assert.Equal(1, lighting.YawCalls);
        Assert.Equal(1, lighting.LayoutCalls);
        Assert.Equal(new[] { 0.25f, -0.25f }, lighting.HeightDeltas);
        Assert.Equal(new[] { -0.25f, 0.25f }, lighting.IntensityDeltas);
    }

    private static FishWarehouseRuntimeService SpawnOwnedService(FakeLighting lighting)
    {
        var service = new FishWarehouseRuntimeService(new LightingGateHost(), () => true, lighting: lighting);
        service.Update();
        service.ToggleDeveloperLifecycle();
        service.TryUnlockDeveloperLifecycle();
        return service;
    }

    private static IReadOnlyList<bool> RunAllLightingKeys(FishWarehouseRuntimeService service) =>
        new[]
        {
            service.TryLightingAddLamp(),
            service.TryLightingRemoveLamp(),
            service.TryLightingRaiseLamp(),
            service.TryLightingLowerLamp(),
            service.TryLightingDimLamps(),
            service.TryLightingBrightenLamps(),
            service.TryLightingToggleLampYaw(),
            service.TryLightingLogLayout(),
        };

    private sealed class LightingGateHost : IRuntimePropertyHost
    {
        public RuntimePropertyHostResult Start(RuntimePropertyDefinition definition) =>
            new(true, "spawn", true, true, true, false, true, true, true, null);

        public bool TrySetOwned() => true;

        public bool PersistenceRuntimeReady => true;

        public void ConfigurePersistenceReplay(bool objectReplayRequired, int? employeeAgentTypeId)
        {
        }

        public void MarkPersistenceObjectsReplayed()
        {
        }

        public bool TryAdoptRestoredEmployeeHome() => false;

        public void UpdateOwnedFeatures()
        {
        }

        public FishWarehouseContinuousNavigationState EmployeeNavigationState =>
            FishWarehouseContinuousNavigationState.Inactive;

        public bool EmployeeNavigationReady => false;

        public int? EmployeeNavigationAgentTypeId => null;

        public RuntimePropertyHostResult Stop() =>
            new(true, "cleanup", true, true, true, true, true, true, true, null);
    }

    private sealed class FakeLighting : IFishWarehouseLightingOwnerQa
    {
        public bool LightingActive { get; set; }
        public int AddCalls { get; private set; }
        public int RemoveCalls { get; private set; }
        public int YawCalls { get; private set; }
        public int LayoutCalls { get; private set; }
        public List<float> HeightDeltas { get; } = new();
        public List<float> IntensityDeltas { get; } = new();
        public int TotalCalls => AddCalls + RemoveCalls + YawCalls + LayoutCalls + HeightDeltas.Count + IntensityDeltas.Count;

        public bool TryAddLampAbovePlayer()
        {
            AddCalls++;
            return true;
        }

        public bool TryRemoveMostRecentLamp()
        {
            RemoveCalls++;
            return true;
        }

        public bool TryAdjustMostRecentLampHeight(float delta)
        {
            HeightDeltas.Add(delta);
            return true;
        }

        public bool TryAdjustAllLampIntensity(float delta)
        {
            IntensityDeltas.Add(delta);
            return true;
        }

        public bool TryToggleMostRecentLampYaw()
        {
            YawCalls++;
            return true;
        }

        public bool TryLogLampLayout()
        {
            LayoutCalls++;
            return true;
        }
    }
}
