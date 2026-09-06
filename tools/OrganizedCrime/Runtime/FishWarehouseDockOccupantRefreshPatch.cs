using HarmonyLib;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.Vehicles;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

internal static class FishWarehouseDockOccupantRefreshPatch
{
    public static void Apply(HarmonyLib.Harmony harmony)
    {
        var original = AccessTools.Method(
            typeof(LoadingDock),
            nameof(LoadingDock.SetOccupant),
            new[] { typeof(LandVehicle) });
        if (original is null)
            throw new MissingMethodException(typeof(LoadingDock).FullName, nameof(LoadingDock.SetOccupant));

        var prefix = AccessTools.Method(
            typeof(FishWarehouseDockOccupantRefreshPatch),
            nameof(Prefix));
        if (prefix is null)
            throw new MissingMethodException(typeof(FishWarehouseDockOccupantRefreshPatch).FullName, nameof(Prefix));

        harmony.Patch(original, prefix: new HarmonyMethod(prefix));
    }

    private static bool Prefix(LoadingDock __instance, LandVehicle? __0)
    {
        try
        {
            if (__0 is not null && __0 != null)
                return true;

            return !FishWarehouseDockOccupantRefreshPolicy.ShouldSuppressOccupantClear(
                IsFishWarehouseDock(__instance),
                HasDynamicOccupant(__instance),
                HasOutputItems(__instance));
        }
        catch
        {
            return true;
        }
    }

    private static bool IsFishWarehouseDock(LoadingDock dock)
    {
        var guid = dock.GUID.ToString();
        return FishWarehouseLoadingDockDefinition.Docks.Any(definition =>
            string.Equals(definition.DockGuid, guid, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasDynamicOccupant(LoadingDock dock)
    {
        var occupant = dock.DynamicOccupant;
        return occupant is not null && occupant != null;
    }

    private static bool HasOutputItems(LoadingDock dock)
    {
        foreach (var slot in dock.OutputSlots)
        {
            if (slot.ItemInstance is not null && slot.ItemInstance != null && slot.Quantity > 0)
                return true;
        }

        return false;
    }
}
