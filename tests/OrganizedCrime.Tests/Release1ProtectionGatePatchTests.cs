using HarmonyLib;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-118. The OC-135 lesson as a test: the protection patch's native lookups live in an
/// adapter the behavior tests fake, so this file resolves every one of them against the
/// referenced f9 interop assemblies through the same AccessTools calls Apply uses. A wrong
/// member name, a reordered signature, or a drifted enum fails here, not in game.
/// </summary>
public sealed class Release1ProtectionGatePatchTests
{
    [Fact]
    public void All_eight_targets_resolve_with_their_exact_signatures()
    {
        var policeCalled = AccessTools.Method(
            typeof(Il2CppScheduleOne.Law.LawManager), "PoliceCalled",
            new[] { typeof(Il2CppScheduleOne.PlayerScripts.Player), typeof(Il2CppScheduleOne.Law.Crime) });
        Assert.NotNull(policeCalled);
        Assert.Equal(typeof(void), policeCalled!.ReturnType);

        var footStart = AccessTools.Method(typeof(Il2CppScheduleOne.Law.PatrolInstance), "StartPatrol", Type.EmptyTypes);
        Assert.NotNull(footStart);
        Assert.Equal(typeof(void), footStart!.ReturnType);

        var footFunnel = AccessTools.Method(
            typeof(Il2CppScheduleOne.Law.LawManager), "StartFootpatrol",
            new[] { typeof(Il2CppScheduleOne.NPCs.Behaviour.FootPatrolRoute), typeof(int) });
        Assert.NotNull(footFunnel);
        Assert.Equal(typeof(Il2CppScheduleOne.NPCs.Behaviour.PatrolGroup), footFunnel!.ReturnType);

        var vehicleStart = AccessTools.Method(typeof(Il2CppScheduleOne.Law.VehiclePatrolInstance), "StartPatrol", Type.EmptyTypes);
        Assert.NotNull(vehicleStart);
        Assert.Equal(typeof(void), vehicleStart!.ReturnType);

        var vehicleFunnel = AccessTools.Method(
            typeof(Il2CppScheduleOne.Law.LawManager), "StartVehiclePatrol",
            new[] { typeof(Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute) });
        Assert.NotNull(vehicleFunnel);
        Assert.Equal(typeof(Il2CppScheduleOne.Police.PoliceOfficer), vehicleFunnel!.ReturnType);

        var sentryStart = AccessTools.Method(typeof(Il2CppScheduleOne.Law.SentryInstance), "StartEntry", Type.EmptyTypes);
        Assert.NotNull(sentryStart);
        Assert.Equal(typeof(void), sentryStart!.ReturnType);

        var enableCheckpoint = AccessTools.Method(typeof(Il2CppScheduleOne.Law.CheckpointInstance), "EnableCheckpoint", Type.EmptyTypes);
        Assert.NotNull(enableCheckpoint);
        Assert.Equal(typeof(void), enableCheckpoint!.ReturnType);

        var setEnabled = AccessTools.Method(
            typeof(Il2CppScheduleOne.Law.CheckpointManager), "SetCheckpointEnabled",
            new[] { typeof(Il2CppScheduleOne.Law.CheckpointManager.ECheckpointLocation), typeof(bool), typeof(int) });
        Assert.NotNull(setEnabled);
        Assert.Equal(typeof(void), setEnabled!.ReturnType);

        // OC-118 owner run fix: the civilian phone call's target check.
        var callTarget = AccessTools.Method(
            typeof(Il2CppScheduleOne.NPCs.Behaviour.CallPoliceBehaviour), "IsTargetValid", Type.EmptyTypes);
        Assert.NotNull(callTarget);
        Assert.Equal(typeof(bool), callTarget!.ReturnType);
        Assert.NotNull(AccessTools.Property(typeof(Il2CppScheduleOne.NPCs.Behaviour.CallPoliceBehaviour), "Target")
            ?? (object?)AccessTools.Field(typeof(Il2CppScheduleOne.NPCs.Behaviour.CallPoliceBehaviour), "Target"));
    }

    [Fact]
    public void No_target_has_an_overload_that_could_steal_the_patch()
    {
        Assert.Single(typeof(Il2CppScheduleOne.Law.LawManager).GetMethods(), m => m.Name == "PoliceCalled");
        Assert.Single(typeof(Il2CppScheduleOne.Law.PatrolInstance).GetMethods(), m => m.Name == "StartPatrol");
        Assert.Single(typeof(Il2CppScheduleOne.Law.LawManager).GetMethods(), m => m.Name == "StartFootpatrol");
        Assert.Single(typeof(Il2CppScheduleOne.Law.VehiclePatrolInstance).GetMethods(), m => m.Name == "StartPatrol");
        Assert.Single(typeof(Il2CppScheduleOne.Law.LawManager).GetMethods(), m => m.Name == "StartVehiclePatrol");
        Assert.Single(typeof(Il2CppScheduleOne.Law.SentryInstance).GetMethods(), m => m.Name == "StartEntry");
        Assert.Single(typeof(Il2CppScheduleOne.Law.CheckpointInstance).GetMethods(), m => m.Name == "EnableCheckpoint");
        Assert.Single(typeof(Il2CppScheduleOne.Law.CheckpointManager).GetMethods(), m => m.Name == "SetCheckpointEnabled");
        Assert.Single(typeof(Il2CppScheduleOne.NPCs.Behaviour.CallPoliceBehaviour).GetMethods(), m => m.Name == "IsTargetValid");
    }

    [Fact]
    public void Route_key_members_exist_on_both_instances_and_both_routes()
    {
        // The interop projection exposes the native fields as properties; the patch reads them
        // the same way either way, so this pins the property shape the projection ships.
        var footRoute = typeof(Il2CppScheduleOne.Law.PatrolInstance).GetProperty("Route");
        Assert.NotNull(footRoute);
        Assert.Equal(typeof(Il2CppScheduleOne.NPCs.Behaviour.FootPatrolRoute), footRoute!.PropertyType);

        var vehicleRoute = typeof(Il2CppScheduleOne.Law.VehiclePatrolInstance).GetProperty("Route");
        Assert.NotNull(vehicleRoute);
        Assert.Equal(typeof(Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute), vehicleRoute!.PropertyType);

        var footName = typeof(Il2CppScheduleOne.NPCs.Behaviour.FootPatrolRoute).GetProperty("RouteName");
        Assert.NotNull(footName);
        Assert.Equal(typeof(string), footName!.PropertyType);

        var vehicleName = typeof(Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute).GetProperty("RouteName");
        Assert.NotNull(vehicleName);
        Assert.Equal(typeof(string), vehicleName!.PropertyType);
    }

    [Fact]
    public void Player_local_and_pointer_identity_exist()
    {
        var local = typeof(Il2CppScheduleOne.PlayerScripts.Player).GetProperty(
            "Local", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(local);
        Assert.Equal(typeof(Il2CppScheduleOne.PlayerScripts.Player), local!.PropertyType);

        var pointer = typeof(Il2CppScheduleOne.PlayerScripts.Player).GetProperty("Pointer");
        Assert.NotNull(pointer);
        Assert.Equal(typeof(IntPtr), pointer!.PropertyType);
    }

    [Fact]
    public void Checkpoint_location_order_matches_the_protection_bitmask()
    {
        var location = typeof(Il2CppScheduleOne.Law.CheckpointManager.ECheckpointLocation);
        Assert.True(location.IsEnum);
        Assert.Equal(
            new[] { "Western", "Docks", "NorthResidential", "WestResidential" },
            Enum.GetNames(location));
        Assert.Equal(new[] { 0, 1, 2, 3 }, Enum.GetValues(location).Cast<int>().ToArray());
    }
}
