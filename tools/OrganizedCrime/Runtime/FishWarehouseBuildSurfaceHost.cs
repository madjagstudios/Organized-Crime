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
            log?.Invoke($"Fish Warehouse employee locker placed (itemId={definition.ItemId}, guid={placedGuid}, grid=({definition.GridX},{definition.GridY}), ParentProperty={parentProperty.PropertyCode}).");
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
            return false;

        GameObject? staging = null;
        GameObject? clone = null;
        Grid? preparedGrid = null;
        try
        {
            if (!Guid.TryParse(gridGuid, out _))
                throw new InvalidOperationException($"Fish Warehouse build Grid GUID was invalid: {gridGuid}");

            var template = FindLargestOwnedGrid(out var sourceTileCount);
            if (template is null || template.gameObject is null)
                throw new InvalidOperationException("No owned native Grid template was available.");

            if (GridAlreadyUsesGuid(gridGuid))
                throw new InvalidOperationException($"Fish Warehouse build Grid GUID was already present: {gridGuid}");

            staging = new GameObject(gridRootName + "_Staging");
            staging.transform.SetParent(parentProperty.transform, false);
            staging.SetActive(false);

            clone = UnityEngine.Object.Instantiate(template.gameObject, staging.transform, false);
            clone.name = gridRootName;
            clone.SetActive(false);

            var clonedGrid = clone.GetComponent<Grid>();
            if (clonedGrid is null)
                throw new InvalidOperationException("Owned Grid template clone did not retain its Grid component.");
            preparedGrid = clonedGrid;

            if (!TrySetMember(clonedGrid, "_guid", gridGuid) ||
                !TrySetMember(clonedGrid, "_parentProperty", parentProperty))
            {
                throw new InvalidOperationException("Could not prepare the Grid GUID and ParentProperty before Awake.");
            }

            var tiles = clone.GetComponentsInChildren<Tile>(includeInactive: true);
            if (tiles.Length == 0)
                throw new InvalidOperationException("Owned Grid template clone contained no Tile components.");

            PrepareTiles(clonedGrid, tiles);
            PositionGrid(clone.transform, parentProperty.transform, tiles);
            var retainedTiles = BuildMeasuredTileSet(
                clone.transform,
                parentProperty.transform,
                clonedGrid,
                tiles,
                Grid.TileSize,
                out var rejectedTileCount);
            if (retainedTiles.Length == 0)
                throw new InvalidOperationException("The measured Fish Warehouse footprint retained no native Grid cells.");

            var employeeHomeDefinition = FishWarehouseEmployeeHomeDefinition.Default;
            if (!retainedTiles.Any(tile =>
                    tile.x == employeeHomeDefinition.GridX &&
                    tile.y == employeeHomeDefinition.GridY))
            {
                throw new InvalidOperationException(
                    $"The measured Fish Warehouse footprint excluded the restored employee-home cell " +
                    $"({employeeHomeDefinition.GridX},{employeeHomeDefinition.GridY}).");
            }

            DisableNonTileColliders(clone);
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

            log?.Invoke(
                $"Fish Warehouse native build Grid enabled (guid={GridGuid}, tiles={TileCount}, " +
                $"sourceTiles={sourceTileCount}, rejectedTiles={rejectedTileCount}, ParentProperty={parentProperty.PropertyCode}).");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Fish Warehouse native build Grid attachment failed safely: {ex.Message}");
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

    private static Grid? FindLargestOwnedGrid(out int tileCount)
    {
        tileCount = 0;
        Grid? best = null;
        var grids = UnityEngine.Object.FindObjectsOfType<Grid>(includeInactive: true);
        for (var i = 0; i < grids.Length; i++)
        {
            var candidate = grids[i];
            var parent = candidate?.ParentProperty;
            if (candidate is null || candidate.gameObject is null || parent is null || !parent.IsOwned)
                continue;

            var count = candidate.GetComponentsInChildren<Tile>(includeInactive: true).Length;
            if (count <= tileCount)
                continue;

            best = candidate;
            tileCount = count;
        }

        return best;
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

    private static void PrepareTiles(Grid ownerGrid, Tile[] tiles)
    {
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i];
            if (tile is null || tile.gameObject is null)
                continue;

            tile.OwnerGrid = ownerGrid;
            ClearMember(tile, "BuildableOccupants");
            ClearMember(tile, "OccupantTiles");

            var renderer = tile.GetComponent<Renderer>();
            if (renderer is not null)
                renderer.enabled = false;
        }
    }

    private static void PositionGrid(Transform grid, Transform propertyRoot, Tile[] tiles)
    {
        var bounds = new Bounds(Vector3.zero, Vector3.zero);
        var initialized = false;
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i];
            if (tile is null || tile.transform is null)
                continue;

            var local = grid.InverseTransformPoint(tile.transform.position);
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

        grid.SetParent(propertyRoot, false);
        grid.localRotation = Quaternion.identity;
        grid.localPosition = new Vector3(-bounds.center.x, GridPlaneOffset - bounds.center.y, -bounds.center.z);
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

    private static Tile[] BuildMeasuredTileSet(
        Transform gridRoot,
        Transform propertyRoot,
        Grid ownerGrid,
        Tile[] tiles,
        float gridCellSize,
        out int rejectedTileCount)
    {
        if (gridRoot is null)
            throw new ArgumentNullException(nameof(gridRoot));
        if (propertyRoot is null)
            throw new ArgumentNullException(nameof(propertyRoot));
        if (ownerGrid is null)
            throw new ArgumentNullException(nameof(ownerGrid));
        if (tiles is null || tiles.Length == 0)
            throw new ArgumentException("The source Grid did not contain any Tile components.", nameof(tiles));

        var footprint = FishWarehouseBuildableFootprintDefinition.CreateMeasured(gridCellSize);
        var reference = tiles.FirstOrDefault(tile =>
            tile is not null && tile.transform is not null && tile.x == 0 && tile.y == 0);
        if (reference is null || reference.transform is null)
        {
            throw new InvalidOperationException("The source Grid did not contain the existing (0,0) reference cell.");
        }

        var referenceLocal = propertyRoot.InverseTransformPoint(reference.transform.position);
        var allowedCoordinates = footprint.GetAllowedCellCoordinates(
            referenceCoordinateX: reference.x,
            referenceCoordinateY: reference.y,
            referenceCenterX: referenceLocal.x,
            referenceCenterZ: referenceLocal.z);
        if (allowedCoordinates.Count == 0)
            throw new InvalidOperationException("The measured Fish Warehouse footprint produced no native cell coordinates.");

        var sourceByCoordinate = new Dictionary<(int X, int Y), Tile>();
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i];
            if (tile is null || tile.transform is null || tile.gameObject is null)
                continue;

            if (!sourceByCoordinate.TryAdd((tile.x, tile.y), tile))
            {
                throw new InvalidOperationException($"The source Grid contained duplicate native cell ({tile.x},{tile.y}).");
            }

            var expectedLocal = new Vector3(
                referenceLocal.x + ((tile.x - reference.x) * gridCellSize),
                referenceLocal.y,
                referenceLocal.z + ((tile.y - reference.y) * gridCellSize));
            if (Vector3.Distance(propertyRoot.InverseTransformPoint(tile.transform.position), expectedLocal) > 0.02f)
            {
                throw new InvalidOperationException(
                    $"The source Grid coordinate spacing was not representable from the (0,0) cell ({tile.x},{tile.y}).");
            }
        }

        var retained = new List<Tile>(allowedCoordinates.Count);
        var allowedSet = new HashSet<(int X, int Y)>();
        for (var i = 0; i < allowedCoordinates.Count; i++)
        {
            var coordinate = allowedCoordinates[i];
            if (!allowedSet.Add((coordinate.X, coordinate.Y)))
                throw new InvalidOperationException($"The measured footprint produced duplicate native cell ({coordinate.X},{coordinate.Y}).");

            if (sourceByCoordinate.TryGetValue((coordinate.X, coordinate.Y), out var existing))
            {
                retained.Add(existing);
                continue;
            }

            var generated = CreateGeneratedTile(
                reference,
                gridRoot,
                ownerGrid,
                coordinate,
                referenceLocal,
                gridCellSize);
            retained.Add(generated);
        }

        rejectedTileCount = 0;
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i];
            if (tile is null || tile.transform is null || tile.gameObject is null)
                continue;

            if (!allowedSet.Contains((tile.x, tile.y)))
            {
                tile.gameObject.SetActive(false);
                rejectedTileCount++;
            }
        }

        return retained.ToArray();
    }

    private static Tile CreateGeneratedTile(
        Tile reference,
        Transform gridRoot,
        Grid ownerGrid,
        FishWarehouseGridCellCoordinate coordinate,
        Vector3 referenceLocal,
        float gridCellSize)
    {
        var generatedObject = UnityEngine.Object.Instantiate(reference.gameObject, gridRoot, false);
        generatedObject.name = $"Grid [{coordinate.X},{coordinate.Y}]";
        generatedObject.SetActive(false);

        var generated = generatedObject.GetComponent<Tile>();
        if (generated is null || generated.transform is null)
            throw new InvalidOperationException($"Generated native Grid cell ({coordinate.X},{coordinate.Y}) did not retain its Tile component.");

        var generatedLocal = gridRoot.InverseTransformPoint(
            gridRoot.parent!.TransformPoint(referenceLocal));
        generatedLocal.x += (coordinate.X - reference.x) * gridCellSize;
        generatedLocal.z += (coordinate.Y - reference.y) * gridCellSize;
        generated.transform.localPosition = generatedLocal;
        generated.InitializePropertyTile(coordinate.X, coordinate.Y, reference.AvailableOffset, ownerGrid);
        generated.OwnerGrid = ownerGrid;
        ClearMember(generated, "BuildableOccupants");
        ClearMember(generated, "OccupantTiles");

        var renderer = generated.GetComponent<Renderer>();
        if (renderer is not null)
            renderer.enabled = false;

        // The clone is inactive while it is being initialized so native Tile state can be
        // assigned before registration. It must be reactivated before the Grid becomes visible;
        // otherwise only the source/template cells participate in physics and the generated
        // perimeter is visually present in the topology but unavailable to build intersections.
        generatedObject.SetActive(true);

        return generated;
    }



}
