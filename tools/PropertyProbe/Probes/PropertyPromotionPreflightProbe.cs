using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class PropertyPromotionPreflightProbe
{
    private const string ProposedPropertyCode = "oc_fishwarehouse";
    private const string TargetName = "Fish Warehouse";
    private const string TargetPath = "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse";

    public PropertyPromotionPreflightSnapshot? Run()
    {
        ProbeLog.Info("Running read-only Fish Warehouse property-promotion preflight...");

        var target = FindTarget();
        if (target is null)
        {
            ProbeLog.Warn($"Could not find target GameObject at {TargetPath}.");
            return null;
        }

        var root = target.transform.parent?.gameObject ?? target;
        var componentTypes = GetComponentTypes(target, root);
        var building = root.GetComponent<NPCEnterableBuilding>() ?? target.GetComponent<NPCEnterableBuilding>();
        var allProperties = Property.Properties is null
            ? Array.Empty<Property>()
            : Property.Properties.ToArray();
        var ownedProperties = Property.OwnedProperties is null
            ? Array.Empty<Property>()
            : Property.OwnedProperties.ToArray();
        var unownedProperties = Property.UnownedProperties is null
            ? Array.Empty<Property>()
            : Property.UnownedProperties.ToArray();

        var snapshot = new PropertyPromotionPreflightSnapshot(
            ProposedPropertyCode: ProposedPropertyCode,
            TargetName: root.name,
            TargetPath: GetTransformPath(target.transform),
            HasNpcEnterableBuilding: building is not null,
            BuildingName: building?.BuildingName,
            BuildingGuid: building?.GUID.ToString(),
            DoorCount: building?.Doors?.Length ?? 0,
            HasPropertyComponent: HasComponent(componentTypes, "Property"),
            HasBusinessComponent: HasComponent(componentTypes, "Business"),
            HasTransitComponent: HasComponent(componentTypes, "Transit") || HasComponent(componentTypes, "LoadingDock"),
            IsInPropertyCollection: ContainsTarget(allProperties, target, root),
            IsInOwnedPropertyCollection: ContainsTarget(ownedProperties, target, root),
            IsInUnownedPropertyCollection: ContainsTarget(unownedProperties, target, root),
            ExistingPropertyCount: allProperties.Length,
            ExistingOwnedPropertyCount: ownedProperties.Length,
            ExistingUnownedPropertyCount: unownedProperties.Length);

        ProbeLog.WriteFile(
            "property-promotion-preflight.txt",
            PropertyPromotionPreflightFormatter.FormatText(snapshot));
        ProbeLog.WriteFile(
            "property-promotion-preflight.json",
            PropertyPromotionPreflightFormatter.FormatJson(snapshot));
        ProbeLog.Info("Fish Warehouse preflight completed read-only; no registration, ownership, save, network, employee, delivery, or scene mutation was performed.");

        return snapshot;
    }

    private static GameObject? FindTarget()
    {
        foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (gameObject is null || !gameObject.scene.IsValid())
                continue;

            if (string.Equals(GetTransformPath(gameObject.transform), TargetPath, StringComparison.Ordinal))
                return gameObject;
        }

        foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (gameObject is null || !gameObject.scene.IsValid() ||
                !string.Equals(gameObject.name, "fishwarehouse", StringComparison.OrdinalIgnoreCase))
                continue;

            if (gameObject.transform.parent is not null &&
                string.Equals(gameObject.transform.parent.name, TargetName, StringComparison.OrdinalIgnoreCase))
            {
                return gameObject;
            }
        }

        return null;
    }

    private static string[] GetComponentTypes(GameObject target, GameObject root)
    {
        var components = new List<string>();
        foreach (var gameObject in new[] { target, root }.Distinct())
        {
            foreach (var component in gameObject.GetComponents<Component>() ?? Array.Empty<Component>())
            {
                if (component is not null)
                    components.Add(component.GetType().FullName ?? component.GetType().Name);
            }
        }

        return components.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool HasComponent(IEnumerable<string> componentTypes, string term) =>
        componentTypes.Any(type => type.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsTarget(
        IEnumerable<Property> properties,
        GameObject target,
        GameObject root)
    {
        var targetPath = GetTransformPath(target.transform);
        var rootPath = GetTransformPath(root.transform);

        return properties.Any(property =>
            property is not null &&
            (property.gameObject == target ||
             property.gameObject == root ||
             string.Equals(GetTransformPath(property.transform), targetPath, StringComparison.Ordinal) ||
             string.Equals(GetTransformPath(property.transform), rootPath, StringComparison.Ordinal) ||
             string.Equals(property.PropertyCode, ProposedPropertyCode, StringComparison.OrdinalIgnoreCase)));
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
