using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2Cpp;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Property;
using OrganizedCrime.Model;
using UnityEngine;
using Object = UnityEngine.Object;
using Il2CppGuid = Il2CppSystem.Guid;

namespace OrganizedCrime.Runtime;

public sealed class FishWarehouseLoadingDockHost
{
    private Property? _property;
    private GameObject? _root;
    private Il2CppReferenceArray<LoadingDock>? _previousDocks;
    private Il2CppReferenceArray<LoadingDock>? _publishedDocks;
    private readonly List<LoadingDock> _docks = new();
    private readonly List<ParkingLot> _parkingLots = new();
    private readonly List<VehicleDetector> _detectors = new();

    public bool IsReady =>
        _property is not null &&
        _root is not null &&
        _publishedDocks is not null &&
        _docks.Count == FishWarehouseLoadingDockDefinition.Docks.Count;

    public bool TryAttach(Property property, Action<string>? log = null)
    {
        if (IsReady)
            return true;
        if (property is null || !FishWarehouseLoadingDockDefinition.HasValidLayout())
            return false;

        GameObject? staging = null;
        var staged = new List<ClonedDock>();
        var previousDocksCaptured = false;
        try
        {
            var barn = FindProperty(FishWarehouseLoadingDockDefinition.SourcePropertyCode);
            var sources = barn?.LoadingDocks?
                .Where(dock => dock is not null && dock.InputSlots.Count == 0 && dock.OutputSlots.Count == 0)
                .Take(2)
                .ToArray() ?? Array.Empty<LoadingDock>();
            if (sources.Length != 2)
            {
                log?.Invoke("Fish Warehouse loading dock setup failed safely: Barn did not expose two complete zero-slot loading docks.");
                return false;
            }

            staging = new GameObject("OC_FishWarehouse_LoadingDocks");
            staging.transform.SetParent(property.transform, false);
            staging.SetActive(false);

            for (var index = 0; index < FishWarehouseLoadingDockDefinition.Docks.Count; index++)
                staged.Add(CloneAndConfigure(sources[index], FishWarehouseLoadingDockDefinition.Docks[index], property, staging.transform));

            var valid = staged.Count(entry => ValidateClone(entry, property));
            if (!FishWarehouseLoadingDockDefinition.CanPublish(staged.Count, valid))
                throw new InvalidOperationException("Both Fish Warehouse loading docks did not pass staged validation.");

            var published = new Il2CppReferenceArray<LoadingDock>(staged.Count);
            for (var index = 0; index < staged.Count; index++)
                published[index] = staged[index].Dock;

            _previousDocks = property.LoadingDocks;
            previousDocksCaptured = true;
            property.LoadingDocks = published;
            if (property.LoadingDocks is null || property.LoadingDocks.Length != 2 ||
                property.LoadingDocks[0] != staged[0].Dock || property.LoadingDocks[1] != staged[1].Dock)
            {
                property.LoadingDocks = _previousDocks;
                throw new InvalidOperationException("Fish Warehouse did not retain the atomic loading-dock publication.");
            }

            staging.SetActive(true);
            _property = property;
            _root = staging;
            _publishedDocks = published;
            _docks.AddRange(staged.Select(entry => entry.Dock));
            _parkingLots.AddRange(staged.Select(entry => entry.Parking));
            _detectors.AddRange(staged.Select(entry => entry.Detector));
            log?.Invoke("Fish Warehouse native loading docks enabled (count=2, indices=0,1, source=barn, offStreet=True).");
            return true;
        }
        catch (Exception ex)
        {
            if (previousDocksCaptured)
                property.LoadingDocks = _previousDocks;

            foreach (var entry in staged)
            {
                DeregisterIfOwned(entry.Dock, ParseGuid(entry.Definition.DockGuid));
                DeregisterIfOwned(entry.Parking, ParseGuid(entry.Definition.ParkingLotGuid));
            }

            if (staging is not null)
                Object.Destroy(staging);
            Clear();
            log?.Invoke($"Fish Warehouse loading dock setup failed safely: {ex.Message}");
            return false;
        }
    }

    public bool CanUnload()
    {
        if (!IsReady || _property is null)
            return true;

        var activeDelivery = NetworkSingleton<DeliveryManager>.InstanceExists
            ? NetworkSingleton<DeliveryManager>.Instance.GetDelivery(_property)
            : null;
        var occupancy = _docks.Select(dock => new FishWarehouseDockOccupancySnapshot(
            HasDynamicVehicle: dock.DynamicOccupant is not null,
            HasStaticVehicle: dock.StaticOccupant is not null,
            HasActiveDelivery: activeDelivery is not null && activeDelivery.LoadingDock == dock));
        return FishWarehouseLoadingDockDefinition.CanUnload(occupancy);
    }

    public void Dispose()
    {
        if (!CanUnload())
            return;

        if (_property is not null && _publishedDocks is not null && _property.LoadingDocks == _publishedDocks)
            _property.LoadingDocks = _previousDocks;

        foreach (var detector in _detectors)
            detector.Clear();

        for (var index = 0; index < _docks.Count; index++)
        {
            DeregisterIfOwned(_docks[index], ParseGuid(FishWarehouseLoadingDockDefinition.Docks[index].DockGuid));
            DeregisterIfOwned(_parkingLots[index], ParseGuid(FishWarehouseLoadingDockDefinition.Docks[index].ParkingLotGuid));
        }

        if (_root is not null)
            Object.Destroy(_root);
        Clear();
    }

    private static ClonedDock CloneAndConfigure(
        LoadingDock source,
        FishWarehouseDockDefinition definition,
        Property property,
        Transform stagingRoot)
    {
        GameObject? cloneRoot = null;
        try
        {
            var sourceParking = source.Parking ?? throw new InvalidOperationException("Barn loading dock has no parking lot.");
            var sourceDetector = source.VehicleDetector ?? throw new InvalidOperationException("Barn loading dock has no vehicle detector.");
            var sourceSpots = sourceParking.ParkingSpots?.ToArray() ?? Array.Empty<ParkingSpot>();
            if (sourceSpots.Length == 0)
                throw new InvalidOperationException("Barn loading dock has no parking spots.");

            var sourceRoot = FindSmallestCompleteAncestor(source, sourceParking, sourceDetector, sourceSpots);
            var dockPath = GetRelativePath(sourceRoot, source.transform);
            var parkingPath = GetRelativePath(sourceRoot, sourceParking.transform);
            var detectorPath = GetRelativePath(sourceRoot, sourceDetector.transform);
            var spotPaths = sourceSpots.Select(spot => GetRelativePath(sourceRoot, spot.transform)).ToArray();

            cloneRoot = Object.Instantiate(sourceRoot.gameObject, stagingRoot);
            var dock = FindAtPath<LoadingDock>(cloneRoot.transform, dockPath);
            var parking = FindAtPath<ParkingLot>(cloneRoot.transform, parkingPath);
            var detector = FindAtPath<VehicleDetector>(cloneRoot.transform, detectorPath);
            var parkingSpots = spotPaths.Select(path => FindAtPath<ParkingSpot>(cloneRoot.transform, path)).ToArray();
            if (dock is null || parking is null || detector is null || parkingSpots.Any(spot => spot is null) ||
                dock == source || parking == sourceParking || detector == sourceDetector)
            {
                throw new InvalidOperationException("Cloned Barn loading dock retained a source reference.");
            }
            var clonedParkingSpots = parkingSpots.Select(spot => spot!).ToArray();

            var registerables = cloneRoot.GetComponentsInChildren<MonoBehaviour>(true)
                .Select(component => component.TryCast<IGUIDRegisterable>())
                .Where(component => component is not null)
                .ToArray();
            if (registerables.Length != 2 ||
                !registerables.Any(registerable => registerable!.Pointer == dock.Pointer) ||
                !registerables.Any(registerable => registerable!.Pointer == parking.Pointer))
                throw new InvalidOperationException("Cloned Barn loading dock exposed unexpected GUID-bearing components.");

            var dockGuid = ParseGuid(definition.DockGuid);
            var parkingGuid = ParseGuid(definition.ParkingLotGuid);
            if (GUIDManager.IsGUIDAlreadyRegistered(dockGuid) || GUIDManager.IsGUIDAlreadyRegistered(parkingGuid))
                throw new InvalidOperationException("Fish Warehouse loading dock GUID is already registered.");

            dock.BakedGUID = definition.DockGuid;
            parking.BakedGUID = definition.ParkingLotGuid;
            dock.SetGUID(dockGuid);
            parking.SetGUID(parkingGuid);

            dock.ParentProperty = property;
            if (dock.InputSlots.Count != 0 || dock.OutputSlots.Count != 0)
                throw new InvalidOperationException("Cloned Barn loading dock did not preserve zero slots.");
            foreach (var spot in clonedParkingSpots)
                spot.ParentLot = parking;

            var alignmentPoint = clonedParkingSpots[0].AlignmentPoint ?? throw new InvalidOperationException("Cloned parking spot has no alignment point.");
            var targetPosition = property.transform.TransformPoint(new Vector3(definition.CenterX, definition.CenterY, definition.CenterZ));
            var targetRotation = property.transform.rotation * Quaternion.Euler(0f, definition.YawDegrees, 0f);
            cloneRoot.transform.rotation = targetRotation * Quaternion.Inverse(alignmentPoint.rotation) * cloneRoot.transform.rotation;
            cloneRoot.transform.position += targetPosition - alignmentPoint.position;

            var runtimeDetectionColliders = detector.detectionColliders?.ToArray() ?? Array.Empty<Collider>();
            var hierarchyDetectionColliders = detector.GetComponentsInChildren<Collider>(true).ToArray();
            if (!FishWarehouseLoadingDockDefinition.TryBindDetectorCollider(
                    runtimeDetectionColliders,
                    hierarchyDetectionColliders,
                    collider => collider.TryCast<BoxCollider>(),
                    collider => IsChildOf(cloneRoot.transform, collider.transform) &&
                                IsChildOf(detector.transform, collider.transform),
                    out var selectedCollider,
                    out var diagnostics) ||
                selectedCollider is null)
            {
                throw new InvalidOperationException(
                    $"Cloned vehicle detector did not expose one cloned BoxCollider " +
                    $"(runtime references={diagnostics.RuntimeReferenceCount}, " +
                    $"hierarchy colliders={diagnostics.HierarchyColliderCount}, " +
                    $"owned BoxColliders={diagnostics.OwnedBoxColliderCount}).");
            }

            var detectionCollider = selectedCollider;
            detector.detectionColliders = new Il2CppReferenceArray<Collider>(1);
            detector.detectionColliders[0] = detectionCollider;
            detector.transform.SetPositionAndRotation(targetPosition, targetRotation);
            detectionCollider.center = new Vector3(0f, FishWarehouseLoadingDockDefinition.DetectorHeight / 2f, 0f);
            detectionCollider.size = new Vector3(definition.HalfWidth * 2f, FishWarehouseLoadingDockDefinition.DetectorHeight, definition.HalfLength * 2f);
            detectionCollider.isTrigger = true;

            cloneRoot.name = definition.Name;
            dock.name = definition.Name;
            cloneRoot.SetActive(true);
            return new ClonedDock(cloneRoot, dock, parking, detector, detectionCollider, definition);
        }
        catch
        {
            if (cloneRoot is not null)
            {
                var clonedDock = cloneRoot.GetComponentInChildren<LoadingDock>(true);
                var clonedParking = cloneRoot.GetComponentInChildren<ParkingLot>(true);
                if (clonedDock is not null)
                    DeregisterIfOwned(clonedDock, ParseGuid(definition.DockGuid));
                if (clonedParking is not null)
                    DeregisterIfOwned(clonedParking, ParseGuid(definition.ParkingLotGuid));
                Object.Destroy(cloneRoot);
            }

            throw;
        }
    }

    private static bool ValidateClone(ClonedDock entry, Property property)
    {
        var definition = entry.Definition;
        return entry.Dock.GUID.Equals(ParseGuid(definition.DockGuid)) &&
             entry.Parking.GUID.Equals(ParseGuid(definition.ParkingLotGuid)) &&
             entry.Dock.ParentProperty == property &&
             entry.Dock.InputSlots.Count == 0 &&
             entry.Dock.OutputSlots.Count == 0 &&
             entry.Parking.ParkingSpots is not null && entry.Parking.ParkingSpots.Count > 0 &&
             entry.Parking.ParkingSpots.ToArray().All(spot => spot.ParentLot == entry.Parking && IsChildOf(entry.Root.transform, spot.transform)) &&
             IsChildOf(entry.Root.transform, entry.Dock.transform) &&
             IsChildOf(entry.Root.transform, entry.Parking.transform) &&
             IsChildOf(entry.Root.transform, entry.Detector.transform) &&
             entry.Dock.Parking == entry.Parking &&
             entry.Dock.VehicleDetector == entry.Detector &&
             entry.Detector.detectionColliders.Count == 1 && entry.Detector.detectionColliders[0] == entry.DetectionCollider &&
             entry.DetectionCollider.center == new Vector3(0f, FishWarehouseLoadingDockDefinition.DetectorHeight / 2f, 0f) &&
             entry.DetectionCollider.size == new Vector3(definition.HalfWidth * 2f, FishWarehouseLoadingDockDefinition.DetectorHeight, definition.HalfLength * 2f);
    }

    private static Transform FindSmallestCompleteAncestor(
        LoadingDock dock,
        ParkingLot parking,
        VehicleDetector detector,
        IReadOnlyList<ParkingSpot> spots)
    {
        for (Transform? candidate = dock.transform; candidate is not null; candidate = candidate.parent)
        {
            if (!IsChildOf(candidate, parking.transform) || !IsChildOf(candidate, detector.transform) ||
                spots.Any(spot => !IsChildOf(candidate, spot.transform)))
            {
                continue;
            }

            if (candidate.GetComponentInChildren<Property>(true) is not null ||
                candidate.GetComponentInChildren<Il2CppFishNet.Object.NetworkObject>(true) is not null)
            {
                throw new InvalidOperationException("Barn loading dock clone boundary includes a Property or NetworkObject.");
            }

            return candidate;
        }

        throw new InvalidOperationException("Barn loading dock has no complete clone boundary.");
    }

    private static T? FindAtPath<T>(Transform root, string path) where T : Component
    {
        var transform = string.IsNullOrEmpty(path) ? root : root.Find(path);
        return transform is null ? null : transform.GetComponent<T>();
    }

    private static string GetRelativePath(Transform root, Transform child)
    {
        var names = new Stack<string>();
        for (Transform? current = child; current is not null && current != root; current = current.parent)
            names.Push(current.name);
        if (child != root && names.Count == 0)
            throw new InvalidOperationException("Barn loading dock reference lies outside the clone boundary.");
        return string.Join("/", names);
    }

    private static bool IsChildOf(Transform root, Transform candidate) => candidate == root || candidate.IsChildOf(root);

    private static Property? FindProperty(string code)
    {
        foreach (var property in Property.Properties.ToArray())
        {
            if (string.Equals(property.PropertyCode, code, StringComparison.OrdinalIgnoreCase))
                return property;
        }

        return null;
    }

    private static Il2CppGuid ParseGuid(string value) => new(value);

    private static void DeregisterIfOwned(LoadingDock dock, Il2CppGuid guid)
    {
        if (GUIDManager.IsGUIDAlreadyRegistered(guid) && GUIDManager.GetObject<LoadingDock>(guid) == dock)
            GUIDManager.DeregisterObject(dock.TryCast<IGUIDRegisterable>());
    }

    private static void DeregisterIfOwned(ParkingLot parking, Il2CppGuid guid)
    {
        if (GUIDManager.IsGUIDAlreadyRegistered(guid) && GUIDManager.GetObject<ParkingLot>(guid) == parking)
            GUIDManager.DeregisterObject(parking.TryCast<IGUIDRegisterable>());
    }

    private void Clear()
    {
        _property = null;
        _root = null;
        _previousDocks = null;
        _publishedDocks = null;
        _docks.Clear();
        _parkingLots.Clear();
        _detectors.Clear();
    }

    private sealed record ClonedDock(
        GameObject Root,
        LoadingDock Dock,
        ParkingLot Parking,
        VehicleDetector Detector,
        BoxCollider DetectionCollider,
        FishWarehouseDockDefinition Definition);
}
