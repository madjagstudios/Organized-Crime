using System.Reflection;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Tiles;
using OrganizedCrime.Model;
using S1API.Building;
using S1API.Items;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Adds native floor-placement support to the Fish Warehouse without touching the authored map
/// object. The proxy lives under the runtime Property and receives its own Grid identity.
/// </summary>
public sealed class FishWarehouseBuildSurfaceHost
{
    private const float GridPlaneOffset = 0.02f;
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private GameObject? _gridProxy;
    private Grid? _grid;
    private BuildableItem? _employeeHome;
    private EmployeeHome? _employeeHomeComponent;

    public bool IsReady => _gridProxy is not null && _grid is not null;
    public string GridGuid { get; private set; } = string.Empty;
    public int TileCount { get; private set; }
    public EmployeeHome? CurrentEmployeeHome => _employeeHomeComponent;

    public bool TryAdoptRestoredEmployeeHome(
        Property parentProperty,
        BuildableItem buildable,
        EmployeeHome employeeHome,
        Action<string>? log = null)
    {
        if (_employeeHome is not null)
            return _employeeHome == buildable && _employeeHomeComponent == employeeHome;

        if (!IsReady || parentProperty is null || buildable is null || employeeHome is null)
        {
            log?.Invoke("Fish Warehouse restored employee-home adoption rejected because the native Grid, Property, BuildableItem, or EmployeeHome was unavailable.");
            return false;
        }

        try
        {
            if (buildable.ParentProperty != parentProperty || employeeHome.GetComponent<BuildableItem>() != buildable)
            {
                log?.Invoke("Fish Warehouse restored employee-home adoption rejected because the locker did not belong to the runtime Property.");
                return false;
            }

            _employeeHome = buildable;
            _employeeHomeComponent = employeeHome;
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Fish Warehouse restored employee-home adoption failed safely: {ex.Message}");
            return false;
        }
    }

    public bool TryPlaceEmployeeHome(
        Property parentProperty,
        FishWarehouseEmployeeHomeDefinition definition,
        Action<string>? log = null)
    {
        if (_employeeHome is not null)
            return true;

        if (!IsReady || parentProperty is null || definition is null)
        {
            log?.Invoke("Fish Warehouse employee-home placement rejected because the native Grid or Property was unavailable.");
            return false;
        }

        BuildableItem? createdBuildable = null;
        try
        {
            if (!Guid.TryParse(definition.PlacementGuid, out var placementGuid) || placementGuid == Guid.Empty)
                throw new InvalidOperationException($"Employee-home placement GUID was invalid: {definition.PlacementGuid}");

            var itemDefinition = ItemManager.GetDefinition(definition.ItemId);
            if (itemDefinition is null)
                throw new InvalidOperationException($"Vanilla buildable item was not found: {definition.ItemId}");

            var gameObject = BuildManager.CreateGridItem(
                itemDefinition.CreateInstance(),
                _grid!,
                new Vector2(definition.GridX, definition.GridY),
                definition.Rotation,
                placementGuid.ToString());
            var buildable = gameObject?.GetComponent<BuildableItem>();
            createdBuildable = buildable;
            var employeeHome = gameObject?.GetComponent<EmployeeHome>();
            if (buildable is null || employeeHome is null)
                throw new InvalidOperationException("Vanilla locker did not produce both BuildableItem and EmployeeHome components.");

            if (buildable.ParentProperty != parentProperty)
                throw new InvalidOperationException($"Vanilla locker ParentProperty was not Fish Warehouse (actual={buildable.ParentProperty?.PropertyCode ?? "null"}).");

            if (!Guid.TryParse(buildable.GUID.ToString(), out var placedGuid) || placedGuid == Guid.Empty)
                throw new InvalidOperationException($"Vanilla locker returned an invalid placement GUID: {buildable.GUID}");

            _employeeHome = buildable;
            _employeeHomeComponent = employeeHome;
            OrganizedCrimeLog.Diagnostic($"Fish Warehouse employee locker placed (itemId={definition.ItemId}, guid={placedGuid}, grid=({definition.GridX},{definition.GridY}), ParentProperty={parentProperty.PropertyCode}).", log);
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                createdBuildable?.Destroy_Server();
            }
            catch
            {
                // The owning property/root remains the cleanup fallback.
            }

            log?.Invoke($"Fish Warehouse employee locker placement failed safely: {ex.Message}");
            return false;
        }
    }

    public bool TryAttach(Property parentProperty, Transform anchor, string gridRootName, string gridGuid, Action<string>? log = null)
    {
        if (IsReady)
            return true;

        if (parentProperty is null || anchor is null || string.IsNullOrWhiteSpace(gridRootName) || string.IsNullOrWhiteSpace(gridGuid))
        {
            var fixedCellZero = new Vector3(
                FishWarehouseBuildableFootprintDefinition.GridOriginX,
                GridPlaneOffset,
                FishWarehouseBuildableFootprintDefinition.GridOriginZ);
            log?.Invoke(
                $"Fish Warehouse native build Grid attachment failed safely (sourceProperty=none, tiles=0, " +
                $"cell00PropertyLocal={FormatPosition(fixedCellZero)}): required Property, anchor, root name, or GUID was unavailable.");
            return false;
        }

        GameObject? staging = null;
        GameObject? clone = null;
        Grid? preparedGrid = null;
        var sourcePropertyCode = "none";
        var laidTileCount = 0;
        var cellZeroPropertyLocal = new Vector3(
            FishWarehouseBuildableFootprintDefinition.GridOriginX,
            GridPlaneOffset,
            FishWarehouseBuildableFootprintDefinition.GridOriginZ);
        try
        {
            if (!Guid.TryParse(gridGuid, out _))
                throw new InvalidOperationException($"Fish Warehouse build Grid GUID was invalid: {gridGuid}");

            var sourceGrid = FindDeterministicSourceGrid(out var sourceTile, out sourcePropertyCode);
            if (sourceGrid is null || sourceGrid.gameObject is null || sourceTile is null || sourceTile.gameObject is null)
                throw new InvalidOperationException("No native Grid with a (0,0) Tile was available.");

            if (GridAlreadyUsesGuid(gridGuid))
                throw new InvalidOperationException($"Fish Warehouse build Grid GUID was already present: {gridGuid}");

            staging = new GameObject(gridRootName + "_Staging");
            staging.transform.SetParent(parentProperty.transform, false);
            staging.SetActive(false);

            clone = UnityEngine.Object.Instantiate(sourceGrid.gameObject, staging.transform, false);
            clone.name = gridRootName;
            clone.SetActive(false);

            var clonedGrid = clone.GetComponent<Grid>();
            if (clonedGrid is null)
                throw new InvalidOperationException("Native Grid source clone did not retain its Grid component.");
            preparedGrid = clonedGrid;

            if (!TrySetMember(clonedGrid, "_guid", gridGuid) ||
                !TrySetMember(clonedGrid, "_parentProperty", parentProperty))
            {
                throw new InvalidOperationException("Could not prepare the Grid GUID and ParentProperty before Awake.");
            }

            var clonedTiles = clone.GetComponentsInChildren<Tile>(includeInactive: true);
            if (clonedTiles.Length == 0)
                throw new InvalidOperationException("Native Grid source clone contained no Tile components to remove.");

            for (var i = 0; i < clonedTiles.Length; i++)
            {
                var clonedTile = clonedTiles[i];
                if (clonedTile is null || clonedTile.gameObject is null)
                    continue;
                if (clonedTile.gameObject == clone)
                    throw new InvalidOperationException("Native Grid source clone placed a Tile component on its Grid root.");

                UnityEngine.Object.DestroyImmediate(clonedTile.gameObject);
            }

            var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(Grid.TileSize);
            var allowedCoordinates = footprint.GetAllowedCellCoordinates(
                referenceCoordinateX: 0,
                referenceCoordinateY: 0,
                referenceCenterX: FishWarehouseBuildableFootprintDefinition.GridOriginX,
                referenceCenterZ: FishWarehouseBuildableFootprintDefinition.GridOriginZ);
            if (allowedCoordinates.Count == 0)
                throw new InvalidOperationException("The measured Fish Warehouse footprint produced no native cell coordinates.");

            var employeeHomeDefinition = FishWarehouseEmployeeHomeDefinition.Default;
            if (!allowedCoordinates.Any(coordinate =>
                    coordinate.X == employeeHomeDefinition.GridX &&
                    coordinate.Y == employeeHomeDefinition.GridY))
            {
                throw new InvalidOperationException(
                    $"The measured Fish Warehouse footprint excluded the employee-home cell " +
                    $"({employeeHomeDefinition.GridX},{employeeHomeDefinition.GridY}).");
            }
            if (!allowedCoordinates.Any(coordinate => coordinate.X == 0 && coordinate.Y == 0))
                throw new InvalidOperationException("The measured Fish Warehouse footprint excluded its fixed (0,0) cell.");

            var gridOrigin = footprint.GetPropertyLocalCellCenter(
                new FishWarehouseGridCellCoordinate(0, 0),
                GridPlaneOffset);
            clone.transform.SetParent(parentProperty.transform, false);
            clone.transform.localPosition = new Vector3(gridOrigin.X, gridOrigin.Y, gridOrigin.Z);
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;

            var laidTiles = new List<Tile>(allowedCoordinates.Count);
            for (var i = 0; i < allowedCoordinates.Count; i++)
            {
                var coordinate = allowedCoordinates[i];
                var localCenter = footprint.GetPropertyLocalCellCenter(coordinate, GridPlaneOffset);
                var tile = CreateGeneratedTile(
                    sourceTile,
                    clone.transform,
                    parentProperty.transform,
                    clonedGrid,
                    coordinate,
                    localCenter);
                laidTiles.Add(tile);
                laidTileCount = laidTiles.Count;

                if (coordinate.X == 0 && coordinate.Y == 0)
                {
                    cellZeroPropertyLocal = parentProperty.transform.InverseTransformPoint(tile.transform.position);
                    var expectedCellZero = new Vector3(gridOrigin.X, gridOrigin.Y, gridOrigin.Z);
                    if (Vector3.Distance(cellZeroPropertyLocal, expectedCellZero) > 0.001f)
                    {
                        throw new InvalidOperationException(
                            $"The generated (0,0) cell was not at its fixed Property-local point " +
                            $"(actual={FormatPosition(cellZeroPropertyLocal)}, expected={FormatPosition(expectedCellZero)}).");
                    }
                }
            }

            if (laidTiles.Count != allowedCoordinates.Count)
                throw new InvalidOperationException($"Generated native Grid tile count mismatch (actual={laidTiles.Count}, expected={allowedCoordinates.Count}).");

            DisableNonTileColliders(clone);
            var retainedTiles = laidTiles.ToArray();
            AddBuildHoverCollider(clone.transform, retainedTiles, Grid.TileSize);

            clone.SetActive(true);
            RebuildNativeTopology(clonedGrid, retainedTiles);
            var referenceTile = retainedTiles.FirstOrDefault(tile =>
                tile is not null && tile.x == 0 && tile.y == 0);
            if (referenceTile is null || referenceTile.transform is null)
                throw new InvalidOperationException("The rebuilt Fish Warehouse Grid lost its existing (0,0) reference cell.");

            FishWarehouseNativeGridPlacementPatches.Register(
                clonedGrid,
                clonedGrid.transform.InverseTransformPoint(referenceTile.transform.position),
                Grid.TileSize);
            RegisterPropertyGrid(parentProperty, clonedGrid);

            var runtimeGuid = clonedGrid.GUID.ToString();
            if (!runtimeGuid.Equals(gridGuid, StringComparison.OrdinalIgnoreCase) ||
                clonedGrid.ParentProperty != parentProperty)
            {
                throw new InvalidOperationException($"Grid identity validation failed (actualGuid={runtimeGuid}, expectedGuid={gridGuid}, parent={(clonedGrid.ParentProperty is null ? "null" : clonedGrid.ParentProperty.PropertyCode)}).");
            }

            _gridProxy = clone;
            _grid = clonedGrid;
            GridGuid = runtimeGuid;
            TileCount = retainedTiles.Length;
            clone = null;
            UnityEngine.Object.DestroyImmediate(staging);
            staging = null;
            Physics.SyncTransforms();

            OrganizedCrimeLog.Diagnostic(
                $"Fish Warehouse native build Grid enabled (guid={GridGuid}, sourceProperty={sourcePropertyCode}, " +
                $"tiles={TileCount}, cell00PropertyLocal={FormatPosition(cellZeroPropertyLocal)}, " +
                $"ParentProperty={parentProperty.PropertyCode}).", log);
            return true;
        }
        catch (Exception ex)
        {
            var reason = ex.Message.Replace('\r', ' ').Replace('\n', ' ');
            log?.Invoke(
                $"Fish Warehouse native build Grid attachment failed safely (sourceProperty={sourcePropertyCode}, " +
                $"tiles={laidTileCount}, cell00PropertyLocal={FormatPosition(cellZeroPropertyLocal)}): {reason}");
            if (preparedGrid is not null)
            {
                try
                {
                    FishWarehouseNativeGridPlacementPatches.Unregister(preparedGrid);
                    parentProperty.Grids?.Remove(preparedGrid);
                }
                catch
                {
                    // Failed attachment cleanup is best-effort; the staging root remains disposable.
                }
            }
            if (clone is not null)
                UnityEngine.Object.DestroyImmediate(clone);
            if (staging is not null)
                UnityEngine.Object.DestroyImmediate(staging);
            return false;
        }
    }

    public void Dispose()
    {
        if (_employeeHome is not null)
        {
            try
            {
                _employeeHome.Destroy_Server();
            }
            catch
            {
                // Buildable cleanup is best-effort; the owning Property/root remains the fallback.
            }
        }

        _employeeHome = null;
        _employeeHomeComponent = null;

        if (_gridProxy is not null)
        {
            try
            {
                FishWarehouseNativeGridPlacementPatches.Unregister(_grid);
                if (_grid is not null && _grid.ParentProperty is not null && _grid.ParentProperty.Grids is not null)
                    _grid.ParentProperty.Grids.Remove(_grid);
            }
            catch
            {
                // Property teardown remains responsible for clearing a stale runtime Grid reference.
            }

            try
            {
                UnityEngine.Object.DestroyImmediate(_gridProxy);
            }
            catch
            {
                // Runtime Property cleanup remains responsible for the owning root.
            }
        }

        _gridProxy = null;
        _grid = null;
        GridGuid = string.Empty;
        TileCount = 0;
    }

    private static Grid? FindDeterministicSourceGrid(out Tile? referenceTile, out string propertyCode)
    {
        referenceTile = null;
        propertyCode = "none";
        var grids = UnityEngine.Object.FindObjectsOfType<Grid>(includeInactive: true);
        var candidates = new List<(Grid Grid, Tile Reference, string PropertyCode, string Guid, string Name)>();
        for (var i = 0; i < grids.Length; i++)
        {
            var candidate = grids[i];
            var parent = candidate?.ParentProperty;
            if (candidate is null || candidate.gameObject is null || parent is null)
                continue;

            var zeroTile = candidate.GetComponentsInChildren<Tile>(includeInactive: true)
                .FirstOrDefault(tile =>
                    tile is not null && tile.gameObject is not null && tile.x == 0 && tile.y == 0);
            if (zeroTile is null)
                continue;

            candidates.Add((
                candidate,
                zeroTile,
                parent.PropertyCode ?? string.Empty,
                candidate.GUID.ToString(),
                candidate.gameObject.name));
        }

        if (candidates.Count == 0)
            return null;

        candidates.Sort((left, right) =>
        {
            var comparison = StringComparer.Ordinal.Compare(left.PropertyCode, right.PropertyCode);
            if (comparison != 0)
                return comparison;
            comparison = StringComparer.Ordinal.Compare(left.Guid, right.Guid);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left.Name, right.Name);
        });

        var source = candidates[0];
        referenceTile = source.Reference;
        propertyCode = source.PropertyCode;
        return source.Grid;
    }

    private static bool GridAlreadyUsesGuid(string gridGuid)
    {
        var grids = UnityEngine.Object.FindObjectsOfType<Grid>(includeInactive: true);
        for (var i = 0; i < grids.Length; i++)
        {
            if (grids[i] is not null && grids[i].GUID.ToString().Equals(gridGuid, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void DisableNonTileColliders(GameObject root)
    {
        var colliders = root.GetComponentsInChildren<Collider>(includeInactive: true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider is not null && collider.GetComponent<Tile>() is null)
                collider.enabled = false;
        }
    }

    private static void RebuildNativeTopology(Grid grid, Tile[] tiles)
    {
        grid.Tiles.Clear();
        grid.CoordinateTilePairs.Clear();
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i];
            if (tile is null)
                continue;

            tile.OwnerGrid = grid;
            grid.RegisterTile(tile);
        }

        var lookup = GetMember(grid, "_coordinateToTile");
        if (!Invoke(lookup, "Clear") ||
            !Invoke(grid, "ProcessCoordinateDataPairs") ||
            !Invoke(grid, "SetGridSize"))
        {
            throw new InvalidOperationException("Native Grid coordinate topology could not be rebuilt.");
        }

        if (grid.Tiles.Count != tiles.Length)
            throw new InvalidOperationException($"Native Grid tile registration mismatch (actual={grid.Tiles.Count}, expected={tiles.Length}).");
    }

    private static object? GetMember(object target, string name)
    {
        var type = target.GetType();
        var property = type.GetProperty(name, MemberFlags);
        if (property is not null)
            return property.GetValue(target);
        return type.GetField(name, MemberFlags)?.GetValue(target);
    }

    private static bool TrySetMember(object target, string name, object value)
    {
        try
        {
            var type = target.GetType();
            var property = type.GetProperty(name, MemberFlags);
            if (property is not null && property.CanWrite)
            {
                property.SetValue(target, value);
                return true;
            }

            var field = type.GetField(name, MemberFlags);
            if (field is null)
                return false;

            field.SetValue(target, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ClearMember(object target, string name)
    {
        var member = GetMember(target, name);
        return member is not null && Invoke(member, "Clear");
    }

    private static bool Invoke(object? target, string name)
    {
        if (target is null)
            return false;

        try
        {
            var method = target.GetType().GetMethod(name, MemberFlags, null, Type.EmptyTypes, null);
            if (method is null)
                return false;

            method.Invoke(target, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AddBuildHoverCollider(Transform gridRoot, Tile[] tiles, float gridCellSize)
    {
        if (gridRoot is null)
            throw new ArgumentNullException(nameof(gridRoot));
        if (tiles is null || tiles.Length == 0)
            throw new ArgumentException("The measured Grid had no retained cells for its hover collider.", nameof(tiles));
        if (!float.IsFinite(gridCellSize) || gridCellSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(gridCellSize));

        var initialized = false;
        var bounds = default(Bounds);
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i];
            if (tile is null || tile.transform is null)
                continue;

            var local = gridRoot.InverseTransformPoint(tile.transform.position);
            if (!initialized)
            {
                bounds = new Bounds(local, Vector3.zero);
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(local);
            }
        }

        if (!initialized)
            throw new InvalidOperationException("The measured Grid had no valid retained tile transforms.");

        var colliderObject = new GameObject("OC_FishWarehouse_BuildGridCollider");
        colliderObject.transform.SetParent(gridRoot, false);
        colliderObject.layer = 7;
        colliderObject.transform.localPosition = bounds.center;
        colliderObject.transform.localRotation = Quaternion.identity;
        colliderObject.transform.localScale = Vector3.one;
        var collider = colliderObject.AddComponent<BoxCollider>();
        collider.isTrigger = false;
        collider.center = Vector3.zero;
        collider.size = new Vector3(
            bounds.size.x + gridCellSize,
            0.2f,
            bounds.size.z + gridCellSize);
    }

    private static void RegisterPropertyGrid(Property parentProperty, Grid grid)
    {
        var grids = parentProperty.Grids;
        if (grids is null)
            throw new InvalidOperationException("Fish Warehouse Property did not expose its native Grid list.");

        if (!grids.Contains(grid))
            grids.Add(grid);
    }

    private static Tile CreateGeneratedTile(
        Tile prototype,
        Transform gridRoot,
        Transform propertyRoot,
        Grid ownerGrid,
        FishWarehouseGridCellCoordinate coordinate,
        FishWarehousePoint propertyLocalCenter)
    {
        var generatedObject = UnityEngine.Object.Instantiate(prototype.gameObject, gridRoot, false);
        generatedObject.name = $"Grid [{coordinate.X},{coordinate.Y}]";
        generatedObject.SetActive(false);

        var generated = generatedObject.GetComponent<Tile>();
        if (generated is null || generated.transform is null)
            throw new InvalidOperationException($"Generated native Grid cell ({coordinate.X},{coordinate.Y}) did not retain its Tile component.");

        generated.InitializePropertyTile(coordinate.X, coordinate.Y, prototype.AvailableOffset, ownerGrid);
        generated.OwnerGrid = ownerGrid;
        generated.transform.position = propertyRoot.TransformPoint(
            new Vector3(propertyLocalCenter.X, propertyLocalCenter.Y, propertyLocalCenter.Z));
        ClearMember(generated, "BuildableOccupants");
        ClearMember(generated, "OccupantTiles");

        var renderer = generated.GetComponent<Renderer>();
        if (renderer is not null)
            renderer.enabled = false;

        // Initialize the clone while inactive, then activate it so its collider participates in physics.
        generatedObject.SetActive(true);

        return generated;
    }

    private static string FormatPosition(Vector3 position) =>
        FormattableString.Invariant($"({position.x:0.000},{position.y:0.000},{position.z:0.000})");



}
