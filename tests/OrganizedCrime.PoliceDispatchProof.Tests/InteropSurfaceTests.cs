using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.PlayerScripts;
using Xunit;

namespace OrganizedCrime.PoliceDispatchProof.Tests;

public sealed class InteropSurfaceTests
{
    [Fact]
    public void Pinned_native_surface_compiles_without_reflection()
    {
        // This method is deliberately not invoked. Compilation is the boundary assertion;
        // invoking Dispatch would be a game mutation, outside static verification.
        var compiledBoundary = (Action<PoliceStation, Player>)CompilePinnedSurface;
        Assert.NotNull(compiledBoundary);
        Assert.Equal(2, OrganizedCrime.PoliceDispatchProof.DispatchProofPins.RequestedOfficerCount);
        Assert.False(OrganizedCrime.PoliceDispatchProof.DispatchProofPins.BeginAsSighted);
        Assert.Equal("UseVehicle", OrganizedCrime.PoliceDispatchProof.DispatchProofPins.DispatchType);
    }

    private static void CompilePinnedSurface(PoliceStation station, Player target)
    {
        var stations = PoliceStation.PoliceStations;
        var officers = station.OfficerPool;
        var availableVehicles = station.AvailableVehicleCount;
        var policeVehicles = station.PoliceVehicles;
        var cooldown = station.TimeSinceLastDispatch;

        foreach (var officer in officers)
        {
            var active = officer.gameObject.activeInHierarchy;
            var alive = officer.Health is not null && !officer.Health.IsDead;
            var pursuitTarget = officer.PursuitTarget;
            _ = (active, alive, pursuitTarget);
        }

        station.Dispatch(2, target, PoliceStation.EDispatchType.UseVehicle, false);
        _ = (stations, availableVehicles, policeVehicles, cooldown);
    }
}
