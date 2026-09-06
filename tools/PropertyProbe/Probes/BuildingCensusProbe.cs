using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class BuildingCensusProbe
{
    private const float NearDocksRadius = 60f;
    private const int MaximumSnapshots = 500;
    private const float LocationCaptureRadius = 30f;
    private const int MaximumLocationObjects = 100;
    private readonly SafehouseLocationCaptureLedger _locationCaptures = new(maximumCandidates: 3);

    public IReadOnlyList<BuildingCensusSnapshot> Run()
    {
        ProbeLog.Info("Running read-only building census for the unnamed Docks-adjacent building and Randy's Bait and Tackle...");

        var docksWarehouse = FindDocksWarehouse();
        var referencePosition = docksWarehouse?.transform.position;
        var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        var snapshots = new List<BuildingCensusSnapshot>();

        foreach (var gameObject in allObjects)
        {
            if (gameObject is null || !gameObject.scene.IsValid())
                continue;

            if (IsExcludedHierarchy(gameObject.transform, docksWarehouse?.transform))
                continue;

            var randyNameMatch = BuildingCensusMatcher.MatchesName(
                gameObject.name,
                "Randy",
                "Bait",
                "Tackle");

            var buildingNameMatch = BuildingCensusMatcher.MatchesName(
                gameObject.name,
                "Brick",
                "Warehouse",
                "Fishing",
                "Hut");

            var deliveryLocation = gameObject.GetComponent<DeliveryLocation>();
            var deliveryLocationMatch = deliveryLocation is not null &&
                IsBrickWarehouseDeliveryLocation(deliveryLocation);

            var isUnderDocksWarehouse = docksWarehouse is not null &&
                IsDescendantOf(gameObject.transform, docksWarehouse.transform);

            var distance = referencePosition.HasValue
                ? Vector3.Distance(gameObject.transform.position, referencePosition.Value)
                : float.PositiveInfinity;

            var nearDocks = referencePosition.HasValue &&
                distance <= NearDocksRadius &&
                !isUnderDocksWarehouse;

            if (!randyNameMatch && !buildingNameMatch && !deliveryLocationMatch &&
                (!nearDocks || !IsStructuralCandidate(gameObject)))
                continue;

            var candidate = randyNameMatch
                ? "Randy's Bait and Tackle"
                : buildingNameMatch
                    ? "Named Building Keyword"
                    : deliveryLocationMatch
                        ? "S1API Delivery Location"
                    : "Near Docks Warehouse";

            snapshots.Add(Capture(gameObject, candidate, distance, deliveryLocation));
        }

        var ordered = snapshots
            .OrderBy(snapshot => snapshot.Candidate == "Named Building Keyword" ? 0 : 1)
            .ThenBy(snapshot => snapshot.Candidate == "S1API Delivery Location" ? 0 : 1)
            .ThenBy(snapshot => snapshot.Candidate == "Randy's Bait and Tackle" ? 0 : 1)
            .ThenBy(snapshot => snapshot.DistanceFromReference)
            .ThenBy(snapshot => snapshot.TransformPath, StringComparer.Ordinal)
            .Take(MaximumSnapshots)
            .ToArray();

        ProbeLog.WriteFile("building-census.txt", BuildingCensusFormatter.FormatText(ordered));
        ProbeLog.WriteFile("building-census.json", BuildingCensusFormatter.FormatJson(ordered));
        ProbeLog.Info($"Captured {ordered.Length} candidate building objects from the read-only census.");

        if (docksWarehouse is null)
            ProbeLog.Warn("Docks Warehouse was not found as a registered property; nearby-object matching used no reference position.");

        return ordered;
    }

    public SafehouseLocationCaptureAdmission RunAtPlayerLocation(SafehouseLocationMarker marker)
    {
        var player = FindAuthoritativeSinglePlayer();
        if (player is null)
        {
            const string reason = "No authoritative single-player host was available. Load a playable single-player save and press F9 again.";
            ProbeLog.Warn(reason);
            return new SafehouseLocationCaptureAdmission(false, 0, reason);
        }

        try
        {
            var capture = CaptureLocation(player, marker);
            var admission = _locationCaptures.TryRecord(marker, capture);
            if (!admission.Accepted)
            {
                ProbeLog.Warn($"Safehouse location capture rejected: {admission.Reason}");
                return admission;
            }

            ProbeLog.WriteFile(
                "safehouse-location-candidates.txt",
                SafehouseLocationCaptureFormatter.FormatText(_locationCaptures.Archive));
            ProbeLog.WriteFile(
                "safehouse-location-candidates.json",
                SafehouseLocationCaptureFormatter.FormatJson(_locationCaptures.Archive));
            ProbeLog.Info(
                $"Captured candidate {admission.CandidateNumber} {marker} marker at {capture.Position}. " +
                "This is evidence for manual review, not a location approval.");
            return admission;
        }
        catch (Exception ex)
        {
            ProbeLog.Error($"Safehouse location capture failed: {ex}");
            return new SafehouseLocationCaptureAdmission(false, 0, ex.Message);
        }
    }

    private static SafehouseLocationCaptureSnapshot CaptureLocation(
        Player player,
        SafehouseLocationMarker marker)
    {
        var playerPosition = player.transform.position;
        var viewTransform = Camera.main is { } camera ? camera.transform : player.transform;
        var viewForward = viewTransform.forward;

        return new SafehouseLocationCaptureSnapshot(
            CandidateNumber: 0,
            Marker: marker,
            Position: ToDto(playerPosition),
            ViewForward: ToDto(viewForward),
            PlayerYawDegrees: player.transform.eulerAngles.y,
            SceneName: player.gameObject.scene.name,
            RegisteredLocations: CaptureRegisteredLocations(playerPosition),
            StructuralObjects: CaptureStructuralObjects(player, playerPosition),
            MutationAttempted: false);
    }

    private static IReadOnlyList<SafehouseRegisteredLocationSnapshot> CaptureRegisteredLocations(
        Vector3 anchor)
    {
        var registered = new Dictionary<int, Property>();

        if (Property.Properties is not null)
        {
            foreach (var property in Property.Properties)
            {
                if (property is not null)
                    registered[property.GetInstanceID()] = property;
            }
        }

        if (Business.Businesses is not null)
        {
            foreach (var business in Business.Businesses)
            {
                if (business is not null)
                    registered[business.GetInstanceID()] = business;
            }
        }

        return registered.Values
            .Select(property => CaptureRegisteredLocation(property, anchor))
            .OrderBy(location => location.DistanceFromAnchor)
            .ThenBy(location => location.Code, StringComparer.Ordinal)
            .ToArray();
    }

    private static SafehouseRegisteredLocationSnapshot CaptureRegisteredLocation(
        Property property,
        Vector3 anchor)
    {
        var position = property.transform.position;
        var bounds = CaptureHierarchyBounds(Read<GameObject?>(() => property.BoundingBox, null));

        return new SafehouseRegisteredLocationSnapshot(
            Kind: property is Business ? "Business" : "Property",
            Code: Read(() => property.PropertyCode, "<unavailable>"),
            Name: Read(() => property.PropertyName, property.gameObject.name),
            TransformPath: GetTransformPath(property.transform),
            Position: ToDto(position),
            DistanceFromAnchor: Vector3.Distance(anchor, position),
            IsOwned: Read(() => property.IsOwned, false),
            AnchorInsideBounds: bounds?.Contains(anchor) ?? false,
            BoundsCenter: bounds.HasValue ? ToDto(bounds.Value.center) : null,
            BoundsSize: bounds.HasValue ? ToDto(bounds.Value.size) : null);
    }

    private static IReadOnlyList<SafehouseStructuralObjectSnapshot> CaptureStructuralObjects(
        Player player,
        Vector3 anchor)
    {
        var snapshots = new List<SafehouseStructuralObjectSnapshot>();

        foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (gameObject is null || !gameObject.scene.IsValid() ||
                IsExcludedHierarchy(gameObject.transform, docksWarehouse: null) ||
                IsDescendantOf(gameObject.transform, player.transform))
                continue;

            var bounds = CaptureObjectBounds(gameObject);
            var distance = bounds.HasValue
                ? Vector3.Distance(anchor, bounds.Value.ClosestPoint(anchor))
                : Vector3.Distance(gameObject.transform.position, anchor);
            if (distance > LocationCaptureRadius || !IsStructuralCandidate(gameObject))
                continue;

            snapshots.Add(new SafehouseStructuralObjectSnapshot(
                Name: gameObject.name,
                TransformPath: GetTransformPath(gameObject.transform),
                SceneName: gameObject.scene.name,
                InstanceId: gameObject.GetInstanceID(),
                Position: ToDto(gameObject.transform.position),
                BoundsCenter: bounds.HasValue ? ToDto(bounds.Value.center) : null,
                BoundsSize: bounds.HasValue ? ToDto(bounds.Value.size) : null,
                DistanceFromAnchor: distance,
                ComponentTypes: CaptureComponentTypes(gameObject)));
        }

        return snapshots
            .OrderBy(snapshot => snapshot.DistanceFromAnchor)
            .ThenBy(snapshot => snapshot.TransformPath, StringComparer.Ordinal)
            .Take(MaximumLocationObjects)
            .ToArray();
    }

    private static Player? FindAuthoritativeSinglePlayer()
    {
        try
        {
            var serverManager = InstanceFinder.ServerManager;
            var clientManager = InstanceFinder.ClientManager;
            if (serverManager is null || clientManager is null ||
                !serverManager.OneServerStarted() || !clientManager.Started)
                return null;

            if (Player.PlayerList is null)
                return null;

            var players = Player.PlayerList
                .ToArray()
                .Where(player =>
                    player is not null &&
                    player.IsServerInitialized &&
                    player.Connection is not null)
                .ToArray();
            return players.Length == 1 ? players[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> CaptureComponentTypes(GameObject gameObject) =>
        (gameObject.GetComponents<Component>() ?? Array.Empty<Component>())
            .Where(component => component is not null)
            .Select(component => component.GetType().FullName ?? component.GetType().Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToArray();

    private static Bounds? CaptureHierarchyBounds(GameObject? gameObject)
    {
        if (gameObject is null)
            return null;

        Bounds? aggregate = null;
        foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>(true) ?? Array.Empty<Renderer>())
            aggregate = Encapsulate(aggregate, renderer.bounds);
        foreach (var collider in gameObject.GetComponentsInChildren<Collider>(true) ?? Array.Empty<Collider>())
            aggregate = Encapsulate(aggregate, collider.bounds);
        return aggregate;
    }

    private static Bounds? CaptureObjectBounds(GameObject gameObject)
    {
        Bounds? aggregate = null;
        foreach (var renderer in gameObject.GetComponents<Renderer>() ?? Array.Empty<Renderer>())
            aggregate = Encapsulate(aggregate, renderer.bounds);
        foreach (var collider in gameObject.GetComponents<Collider>() ?? Array.Empty<Collider>())
            aggregate = Encapsulate(aggregate, collider.bounds);
        return aggregate;
    }

    private static Bounds Encapsulate(Bounds? aggregate, Bounds next)
    {
        if (!aggregate.HasValue)
            return next;

        var combined = aggregate.Value;
        combined.Encapsulate(next);
        return combined;
    }

    private static Vector3Dto ToDto(Vector3 value) => new(value.x, value.y, value.z);

    private static T Read<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    private static Property? FindDocksWarehouse()
    {
        if (Property.Properties is null)
            return null;

        foreach (var property in Property.Properties)
        {
            if (property is null)
                continue;

            if (string.Equals(property.PropertyCode, "dockswarehouse", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.gameObject.name, "DocksWarehouse", StringComparison.OrdinalIgnoreCase))
            {
                return property;
            }
        }

        return null;
    }

    private static BuildingCensusSnapshot Capture(
        GameObject gameObject,
        string candidate,
        float distance,
        DeliveryLocation? deliveryLocation)
    {
        var componentTypes = new List<string>();

        foreach (var component in gameObject.GetComponents<Component>() ?? Array.Empty<Component>())
        {
            if (component is null)
                continue;

            componentTypes.Add(component.GetType().FullName ?? component.GetType().Name);
        }

        var distinctTypes = componentTypes
            .Distinct(StringComparer.Ordinal)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToArray();

        bool Has(string term) => distinctTypes.Any(type => type.Contains(term, StringComparison.OrdinalIgnoreCase));
        var hasCollider = HasColliderComponent(gameObject);
        var hasRenderer = gameObject.GetComponent<Renderer>() is not null;
        var position = gameObject.transform.position;

        return new BuildingCensusSnapshot(
            Candidate: candidate,
            GameObjectName: gameObject.name,
            TransformPath: GetTransformPath(gameObject.transform),
            ActiveSelf: gameObject.activeSelf,
            Position: new Vector3Dto(position.x, position.y, position.z),
            DistanceFromReference: distance,
            ChildCount: gameObject.transform.childCount,
            ComponentCount: distinctTypes.Length,
            ComponentTypes: distinctTypes,
            HasPropertyComponent: Has("Property"),
            HasBusinessComponent: Has("Business"),
            HasTransitComponent: Has("Transit") || Has("LoadingDock"),
            HasConfigurableComponent: Has("Configurable"),
            HasGridComponent: Has("Grid"),
            HasContainerComponent: Has("Container"),
            HasCollider: hasCollider || Has("Collider"),
            HasRenderer: hasRenderer || Has("Renderer"),
            DeliveryLocationName: deliveryLocation?.LocationName,
            DeliveryLocationDescription: deliveryLocation?.LocationDescription,
            DeliveryLocationGuid: deliveryLocation is null ? null : deliveryLocation.GUID.ToString());
    }

    private static bool IsBrickWarehouseDeliveryLocation(DeliveryLocation deliveryLocation) =>
        Contains(deliveryLocation.LocationName, "BrickWarehouseDocks") ||
        Contains(deliveryLocation.LocationName, "Brick warehouse at the docks") ||
        Contains(deliveryLocation.LocationDescription, "Brick warehouse at the docks");

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static bool IsStructuralCandidate(GameObject gameObject)
    {
        var name = gameObject.name;
        var noiseTerms = new[]
        {
            "clone",
            "cornerobstacle",
            "footprinttiles",
            "gridunit",
            "model",
            "plant",
            "stem",
            "soil",
            "clump",
            "particle",
            "audio source",
            "seedrestingpoint",
            "vial",
            "glass",
            "label",
            "accesspoints",
            "gameobject",
            "transform",
            "schedule"
        };

        if (noiseTerms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            return false;

        return gameObject.transform.childCount > 0 ||
            gameObject.GetComponent<Renderer>() is not null ||
            HasColliderComponent(gameObject);
    }

    private static bool HasColliderComponent(GameObject gameObject) =>
        new[] { "Collider", "BoxCollider", "MeshCollider", "SphereCollider", "CapsuleCollider" }
            .Any(typeName => gameObject.GetComponent(typeName) is not null);

    private static bool IsExcludedHierarchy(Transform transform, Transform? docksWarehouse)
    {
        var current = transform;
        while (current is not null)
        {
            if (docksWarehouse is not null && current == docksWarehouse)
                return true;

            if (current.name.Contains("Docks Warehouse Contents Container", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.name, "_Temp", StringComparison.OrdinalIgnoreCase) ||
                current.name.Contains("Player_Local", StringComparison.OrdinalIgnoreCase) ||
                current.name.Contains("OverlayCamera", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool IsDescendantOf(Transform transform, Transform ancestor)
    {
        var current = transform;
        while (current is not null)
        {
            if (current == ancestor)
                return true;

            current = current.parent;
        }

        return false;
    }

    private static string GetTransformPath(Transform transform)
    {
        var names = new Stack<string>();
        Transform? current = transform;

        while (current is not null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }
}
