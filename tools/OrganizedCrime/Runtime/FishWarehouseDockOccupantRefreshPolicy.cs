namespace OrganizedCrime.Runtime;

internal static class FishWarehouseDockOccupantRefreshPolicy
{
    public static bool ShouldSuppressOccupantClear(
        bool isFishWarehouseDock,
        bool hasDynamicOccupant,
        bool hasOutputItems) =>
        isFishWarehouseDock && hasDynamicOccupant && hasOutputItems;
}
