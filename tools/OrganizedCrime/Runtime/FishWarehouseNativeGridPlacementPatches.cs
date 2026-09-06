using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Tiles;
using OrganizedCrime.Model;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Keeps the runtime Fish Warehouse Grid on the same explicit coordinate contract as its
/// generated cells. Vanilla Grid.GetMatchedCoordinate derives from Grid.Origin, which is not
/// guaranteed to describe a runtime-created Grid with a preserved existing reference cell.
/// </summary>
internal static class FishWarehouseNativeGridPlacementPatches
{
    private static IntPtr _gridPointer;
    private static Vector3 _referenceCenterLocal;
    private static float _gridCellSize;
    public static void Apply(HarmonyLib.Harmony harmony)
    {
        var matchedCoordinate = AccessTools.Method(
            typeof(Grid),
            nameof(Grid.GetMatchedCoordinate),
            new[] { typeof(FootprintTile) });
        var parentTile = AccessTools.Method(
            typeof(GridItem),
            nameof(GridItem.GetParentTileAtFootprintCoordinate),
            new[] { typeof(Coordinate) });
        if (matchedCoordinate is null || parentTile is null)
        {
            throw new MissingMethodException("Could not resolve the native Grid placement coordinate surfaces.");
        }

        harmony.Patch(
            matchedCoordinate,
            postfix: new HarmonyMethod(AccessTools.Method(
                typeof(FishWarehouseNativeGridPlacementPatches),
                nameof(MatchedCoordinatePostfix))));
        harmony.Patch(
            parentTile,
            postfix: new HarmonyMethod(AccessTools.Method(
                typeof(FishWarehouseNativeGridPlacementPatches),
                nameof(ParentTilePostfix))));
    }

    public static void Register(Grid grid, Vector3 referenceCenterLocal, float gridCellSize)
    {
        if (grid is null)
            throw new ArgumentNullException(nameof(grid));
        if (!float.IsFinite(gridCellSize) || gridCellSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(gridCellSize));

        _gridPointer = GetPointer(grid);
        if (_gridPointer == IntPtr.Zero)
            throw new InvalidOperationException("The Fish Warehouse Grid did not expose a native object pointer.");

        _referenceCenterLocal = referenceCenterLocal;
        _gridCellSize = gridCellSize;
    }

    public static void Unregister(Grid? grid)
    {
        if (grid is null || GetPointer(grid) == _gridPointer)
        {
            _gridPointer = IntPtr.Zero;
            _referenceCenterLocal = default;
            _gridCellSize = 0f;
        }
    }

    private static void MatchedCoordinatePostfix(
        Grid __instance,
        FootprintTile __0,
        ref Coordinate __result)
    {
        try
        {
            if (!IsActiveGrid(__instance) || __0 is null || __0.transform is null)
                return;

            var local = __instance.transform.InverseTransformPoint(__0.transform.position);
            if (!FishWarehouseBuildableFootprintDefinition.TryMapLocalPointToCoordinate(
                    new FishWarehousePoint(local.x, local.y, local.z),
                    new FishWarehousePoint(_referenceCenterLocal.x, _referenceCenterLocal.y, _referenceCenterLocal.z),
                    _gridCellSize,
                    out var mapped))
            {
                __result = default!;
                return;
            }

            var tile = __instance.GetTile(new Coordinate(mapped.X, mapped.Y));
            __result = tile is null ? default! : new Coordinate(mapped.X, mapped.Y);
        }
        catch
        {
            // Vanilla remains the fallback if the diagnostic contract cannot be evaluated.
        }
    }

    private static void ParentTilePostfix(
        GridItem __instance,
        Coordinate __0,
        ref Tile __result)
    {
        try
        {
            if (__result is not null || __instance is null || __0 is null)
                return;

            var grid = __instance.OwnerGrid;
            if (!IsActiveGrid(grid))
                return;

            // Resolve only the exact registered cell. Nearest-cell fallback could admit shell or
            // exterior points when the native coordinate calculation is out of range.
            __result = grid.GetTile(new Coordinate(__0.x, __0.y)) ?? default!;
        }
        catch
        {
            // Vanilla remains the fallback if the native object is already tearing down.
        }
    }

    private static bool IsActiveGrid(Grid? grid) =>
        grid is not null && GetPointer(grid) != IntPtr.Zero && GetPointer(grid) == _gridPointer;

    private static IntPtr GetPointer(Il2CppObjectBase value) => value.Pointer;
}
