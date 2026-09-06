using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class PropertyPromotionExperimentProbe
{
    private const string ProposedPropertyCode = "oc_fishwarehouse";
    private const string TargetName = "Fish Warehouse";
    private const string TargetPath = "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse";
    private const string DocksWarehouseCode = "dockswarehouse";

    private bool _hasRun;
    private bool _ownershipHasRun;
    private bool _registrationPassed;
    private Property? _experimentProperty;
    private GameObject? _experimentHost;

    public PropertyPromotionExperimentSnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F11 registration experiment already ran in this game process.");

        _hasRun = true;
        ProbeLog.Info("Running gated Fish Warehouse registration experiment. Ownership and persistence are disabled.");

        var target = FindTarget();
        if (target is null)
            return WriteFailure("preconditions", $"Could not find target GameObject at {TargetPath}.");

        var root = target.transform.parent?.gameObject ?? target;
        var building = root.GetComponent<NPCEnterableBuilding>() ?? target.GetComponent<NPCEnterableBuilding>();
        if (building is null)
            return WriteFailure("preconditions", "Fish Warehouse does not have an NPCEnterableBuilding component.", root);

            var propertiesBefore = SnapshotProperties(Property.Properties);
            var ownedPropertiesBefore = SnapshotProperties(Property.OwnedProperties);
            var unownedPropertiesBefore = SnapshotProperties(Property.UnownedProperties);
            var docksWarehouse = FindProperty(DocksWarehouseCode);
        if (docksWarehouse is null)
            return WriteFailure("preconditions", "Docks Warehouse was not found by property code.", root, propertiesBefore);

        if (ContainsPropertyCode(propertiesBefore, ProposedPropertyCode))
            return WriteFailure("preconditions", $"Property code {ProposedPropertyCode} is already registered.", root, propertiesBefore, docksWarehouse);

        Property? experimentProperty = null;
        GameObject? experimentHost = null;
        try
        {
            experimentHost = new GameObject("OrganizedCrime Fish Warehouse Property");
            experimentHost.transform.SetParent(root.transform, false);
            experimentHost.SetActive(false);

            experimentProperty = experimentHost.AddComponent<Property>();
            if (!TrySetIdentity(experimentProperty, out var identityFailure))
            {
                UnityEngine.Object.Destroy(experimentHost);
                return WriteFailure("registration", identityFailure, root, propertiesBefore, docksWarehouse);
            }

            RegisterPropertyCollections(experimentProperty);
            experimentHost.SetActive(true);
            experimentProperty.InitializeSaveable();

            var propertiesAfter = SnapshotProperties(Property.Properties);
            var ownedPropertiesAfter = SnapshotProperties(Property.OwnedProperties);
            var unownedPropertiesAfter = SnapshotProperties(Property.UnownedProperties);
            var registeredAfter = propertiesAfter.Any(property =>
                property == experimentProperty &&
                string.Equals(property.PropertyCode, ProposedPropertyCode, StringComparison.OrdinalIgnoreCase));
            var managerProperty = PropertyManager.Instance.GetProperty(ProposedPropertyCode);
            registeredAfter &= managerProperty == experimentProperty ||
                managerProperty is not null &&
                string.Equals(managerProperty.PropertyCode, ProposedPropertyCode, StringComparison.OrdinalIgnoreCase);

            var docksAfter = FindProperty(DocksWarehouseCode);
            var docksUnchanged = false;
            if (docksAfter is not null)
            {
                docksUnchanged = docksAfter == docksWarehouse &&
                    string.Equals(docksAfter.PropertyCode, docksWarehouse.PropertyCode, StringComparison.OrdinalIgnoreCase) &&
                    docksAfter.IsOwned == docksWarehouse.IsOwned;
            }

            var snapshot = new PropertyPromotionExperimentSnapshot(
                ProposedPropertyCode: ProposedPropertyCode,
                TargetName: root.name,
                TargetPath: GetTransformPath(target.transform),
                Gate: registeredAfter && docksUnchanged ? "registration" : "registration-failed",
                PreconditionsPassed: true,
                RegistrationPassed: registeredAfter && docksUnchanged,
                OwnershipPassed: false,
                PersistencePassed: false,
                PropertyCountBefore: propertiesBefore.Length,
                PropertyCountAfter: propertiesAfter.Length,
                OwnedPropertyCountBefore: ownedPropertiesBefore.Length,
                OwnedPropertyCountAfter: ownedPropertiesAfter.Length,
                UnownedPropertyCountBefore: unownedPropertiesBefore.Length,
                UnownedPropertyCountAfter: unownedPropertiesAfter.Length,
                TargetRegisteredAfter: registeredAfter,
                TargetOwnedAfter: experimentProperty.IsOwned,
                TargetInOwnedCollectionAfter: ownedPropertiesAfter.Contains(experimentProperty),
                TargetInUnownedCollectionAfter: unownedPropertiesAfter.Contains(experimentProperty),
                DocksWarehouseCodeBefore: docksWarehouse.PropertyCode,
                DocksWarehouseCodeAfter: docksAfter?.PropertyCode ?? "<missing>",
                DocksWarehouseOwnedBefore: docksWarehouse.IsOwned,
                DocksWarehouseOwnedAfter: docksAfter?.IsOwned ?? false,
                FailureReason: registeredAfter && docksUnchanged
                    ? null
                    : "Registration or Docks Warehouse invariant check failed.");

            if (!registeredAfter || !docksUnchanged)
            {
                RemovePropertyCollections(experimentProperty);
                UnityEngine.Object.Destroy(experimentHost);
            }

            Write(snapshot);
            ProbeLog.Info(registeredAfter && docksUnchanged
                ? "Registration gate passed. Ownership and persistence were not attempted."
                : "Registration gate failed. Ownership and persistence were not attempted.");
            if (snapshot.RegistrationPassed)
            {
                _registrationPassed = true;
                _experimentProperty = experimentProperty;
                _experimentHost = experimentHost;
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            if (experimentProperty is not null)
                RemovePropertyCollections(experimentProperty);

            if (experimentHost is not null)
                UnityEngine.Object.Destroy(experimentHost);

            return WriteFailure("registration", ex.ToString(), root, propertiesBefore, docksWarehouse);
        }
    }

    private static PropertyPromotionExperimentSnapshot WriteFailure(
        string gate,
        string reason,
        GameObject? root = null,
        Property[]? propertiesBefore = null,
        Property? docksWarehouse = null)
    {
        var properties = propertiesBefore ?? SnapshotProperties(Property.Properties);
        var ownedProperties = SnapshotProperties(Property.OwnedProperties);
        var unownedProperties = SnapshotProperties(Property.UnownedProperties);
        var docks = docksWarehouse ?? FindProperty(DocksWarehouseCode);
        var snapshot = new PropertyPromotionExperimentSnapshot(
            ProposedPropertyCode: ProposedPropertyCode,
            TargetName: root?.name ?? TargetName,
            TargetPath: root is null ? TargetPath : GetTransformPath(root.transform),
            Gate: gate,
            PreconditionsPassed: false,
            RegistrationPassed: false,
            OwnershipPassed: false,
            PersistencePassed: false,
            PropertyCountBefore: properties.Length,
            PropertyCountAfter: SnapshotProperties(Property.Properties).Length,
            OwnedPropertyCountBefore: ownedProperties.Length,
            OwnedPropertyCountAfter: ownedProperties.Length,
            UnownedPropertyCountBefore: unownedProperties.Length,
            UnownedPropertyCountAfter: unownedProperties.Length,
            TargetRegisteredAfter: false,
            TargetOwnedAfter: false,
            TargetInOwnedCollectionAfter: false,
            TargetInUnownedCollectionAfter: false,
            DocksWarehouseCodeBefore: docks?.PropertyCode ?? "<missing>",
            DocksWarehouseCodeAfter: FindProperty(DocksWarehouseCode)?.PropertyCode ?? "<missing>",
            DocksWarehouseOwnedBefore: docks?.IsOwned ?? false,
            DocksWarehouseOwnedAfter: FindProperty(DocksWarehouseCode)?.IsOwned ?? false,
            FailureReason: reason);

        Write(snapshot);
        ProbeLog.Warn($"Registration experiment stopped at {gate}: {reason}");
        return snapshot;
    }

    public PropertyPromotionExperimentSnapshot RunOwnership()
    {
        if (_ownershipHasRun)
            return WriteOwnershipFailure("F12 ownership experiment already ran in this game process.");

        _ownershipHasRun = true;
        if (!_registrationPassed || _experimentProperty is null || _experimentHost is null)
            return WriteOwnershipFailure("F11 registration did not pass in this game process.");

        var property = _experimentProperty;
        var managerProperty = PropertyManager.Instance.GetProperty(ProposedPropertyCode);
        var ownedBefore = SnapshotProperties(Property.OwnedProperties);
        var unownedBefore = SnapshotProperties(Property.UnownedProperties);
        var propertiesBefore = SnapshotProperties(Property.Properties);
        var docksBefore = FindProperty(DocksWarehouseCode);

        if (managerProperty != property ||
            property.IsOwned ||
            !unownedBefore.Contains(property) ||
            ownedBefore.Contains(property) ||
            docksBefore is null)
        {
            return WriteOwnershipFailure("Ownership preconditions were not satisfied.");
        }

        ProbeLog.Info("Calling Property.SetOwned() for oc_fishwarehouse. No explicit save write will be performed.");
        try
        {
            property.SetOwned();
        }
        catch (Exception ex)
        {
            return WriteOwnershipFailure($"Property.SetOwned() failed: {ex}", property, docksBefore, propertiesBefore, ownedBefore, unownedBefore);
        }

        var propertiesAfter = SnapshotProperties(Property.Properties);
        var ownedAfter = SnapshotProperties(Property.OwnedProperties);
        var unownedAfter = SnapshotProperties(Property.UnownedProperties);
        var managerAfter = PropertyManager.Instance.GetProperty(ProposedPropertyCode);
        var docksAfter = FindProperty(DocksWarehouseCode);
        var docksUnchanged = docksAfter is not null &&
            docksAfter == docksBefore &&
            string.Equals(docksAfter.PropertyCode, docksBefore.PropertyCode, StringComparison.OrdinalIgnoreCase) &&
            docksAfter.IsOwned == docksBefore.IsOwned;
        var ownershipPassed = property.IsOwned &&
            propertiesAfter.Contains(property) &&
            ownedAfter.Contains(property) &&
            !unownedAfter.Contains(property) &&
            managerAfter == property &&
            ownedAfter.Length == ownedBefore.Length + 1 &&
            unownedAfter.Length == unownedBefore.Length - 1 &&
            docksUnchanged;

        var snapshot = new PropertyPromotionExperimentSnapshot(
            ProposedPropertyCode: ProposedPropertyCode,
            TargetName: TargetName,
            TargetPath: TargetPath,
            Gate: ownershipPassed ? "ownership" : "ownership-failed",
            PreconditionsPassed: true,
            RegistrationPassed: true,
            OwnershipPassed: ownershipPassed,
            PersistencePassed: false,
            PropertyCountBefore: propertiesBefore.Length,
            PropertyCountAfter: propertiesAfter.Length,
            OwnedPropertyCountBefore: ownedBefore.Length,
            OwnedPropertyCountAfter: ownedAfter.Length,
            UnownedPropertyCountBefore: unownedBefore.Length,
            UnownedPropertyCountAfter: unownedAfter.Length,
            TargetRegisteredAfter: propertiesAfter.Contains(property),
            TargetOwnedAfter: property.IsOwned,
            TargetInOwnedCollectionAfter: ownedAfter.Contains(property),
            TargetInUnownedCollectionAfter: unownedAfter.Contains(property),
            DocksWarehouseCodeBefore: docksBefore.PropertyCode,
            DocksWarehouseCodeAfter: docksAfter?.PropertyCode ?? "<missing>",
            DocksWarehouseOwnedBefore: docksBefore.IsOwned,
            DocksWarehouseOwnedAfter: docksAfter?.IsOwned ?? false,
            FailureReason: ownershipPassed ? null : "Ownership or Docks Warehouse invariant check failed.");

        WriteOwnership(snapshot);
        ProbeLog.Info(ownershipPassed
            ? "Ownership gate passed. Persistence was not attempted."
            : "Ownership gate failed. Persistence was not attempted.");
        return snapshot;
    }

    public bool TryGetRegisteredExperiment(out Property property, out GameObject host)
    {
        if (_registrationPassed && _experimentProperty is not null && _experimentHost is not null)
        {
            property = _experimentProperty;
            host = _experimentHost;
            return true;
        }

        property = null!;
        host = null!;
        return false;
    }

    public void CleanupRegisteredExperiment()
    {
        if (_experimentProperty is not null)
            RemovePropertyCollections(_experimentProperty);

        if (_experimentHost is not null)
            UnityEngine.Object.Destroy(_experimentHost);

        _experimentProperty = null;
        _experimentHost = null;
        _registrationPassed = false;
    }

    private PropertyPromotionExperimentSnapshot WriteOwnershipFailure(
        string reason,
        Property? property = null,
        Property? docksBefore = null,
        Property[]? propertiesBefore = null,
        Property[]? ownedBefore = null,
        Property[]? unownedBefore = null)
    {
        var currentProperty = property ?? _experimentProperty;
        var properties = propertiesBefore ?? SnapshotProperties(Property.Properties);
        var owned = ownedBefore ?? SnapshotProperties(Property.OwnedProperties);
        var unowned = unownedBefore ?? SnapshotProperties(Property.UnownedProperties);
        var docks = docksBefore ?? FindProperty(DocksWarehouseCode);
        var snapshot = new PropertyPromotionExperimentSnapshot(
            ProposedPropertyCode: ProposedPropertyCode,
            TargetName: TargetName,
            TargetPath: TargetPath,
            Gate: "ownership-failed",
            PreconditionsPassed: false,
            RegistrationPassed: _registrationPassed,
            OwnershipPassed: false,
            PersistencePassed: false,
            PropertyCountBefore: properties.Length,
            PropertyCountAfter: SnapshotProperties(Property.Properties).Length,
            OwnedPropertyCountBefore: owned.Length,
            OwnedPropertyCountAfter: SnapshotProperties(Property.OwnedProperties).Length,
            UnownedPropertyCountBefore: unowned.Length,
            UnownedPropertyCountAfter: SnapshotProperties(Property.UnownedProperties).Length,
            TargetRegisteredAfter: currentProperty is not null && SnapshotProperties(Property.Properties).Contains(currentProperty),
            TargetOwnedAfter: currentProperty?.IsOwned ?? false,
            TargetInOwnedCollectionAfter: currentProperty is not null && SnapshotProperties(Property.OwnedProperties).Contains(currentProperty),
            TargetInUnownedCollectionAfter: currentProperty is not null && SnapshotProperties(Property.UnownedProperties).Contains(currentProperty),
            DocksWarehouseCodeBefore: docks?.PropertyCode ?? "<missing>",
            DocksWarehouseCodeAfter: FindProperty(DocksWarehouseCode)?.PropertyCode ?? "<missing>",
            DocksWarehouseOwnedBefore: docks?.IsOwned ?? false,
            DocksWarehouseOwnedAfter: FindProperty(DocksWarehouseCode)?.IsOwned ?? false,
            FailureReason: reason);

        WriteOwnership(snapshot);
        ProbeLog.Warn($"Ownership experiment stopped: {reason}");
        return snapshot;
    }

    private static void Write(PropertyPromotionExperimentSnapshot snapshot)
    {
        ProbeLog.WriteFile(
            "property-promotion-experiment.txt",
            PropertyPromotionExperimentFormatter.FormatText(snapshot));
        ProbeLog.WriteFile(
            "property-promotion-experiment.json",
            PropertyPromotionExperimentFormatter.FormatJson(snapshot));
    }

    private static void WriteOwnership(PropertyPromotionExperimentSnapshot snapshot)
    {
        ProbeLog.WriteFile(
            "property-promotion-ownership.txt",
            PropertyPromotionExperimentFormatter.FormatText(snapshot));
        ProbeLog.WriteFile(
            "property-promotion-ownership.json",
            PropertyPromotionExperimentFormatter.FormatJson(snapshot));
    }

    private static bool TrySetIdentity(Property property, out string failure)
    {
        try
        {
            JsonUtility.FromJsonOverwrite(PropertyPromotionIdentityPayload.Json, property);
            var codeSet = string.Equals(property.PropertyCode, ProposedPropertyCode, StringComparison.Ordinal);
            var nameSet = string.Equals(property.PropertyName, TargetName, StringComparison.Ordinal);
            if (codeSet && nameSet)
            {
                failure = string.Empty;
                return true;
            }

            failure = $"Serialized Property identity did not apply (code applied: {codeSet}; name applied: {nameSet}).";
            return false;
        }
        catch (Exception ex)
        {
            failure = $"Serialized Property identity failed: {ex}";
            return false;
        }
    }

    private static void RegisterPropertyCollections(Property property)
    {
        if (!Property.Properties.Contains(property))
            Property.Properties.Add(property);

        if (!Property.UnownedProperties.Contains(property))
            Property.UnownedProperties.Add(property);
    }

    private static void RemovePropertyCollections(Property property)
    {
        Property.Properties.Remove(property);
        Property.OwnedProperties.Remove(property);
        Property.UnownedProperties.Remove(property);
    }

    private static Property? FindProperty(string code)
    {
        foreach (var property in SnapshotProperties(Property.Properties))
        {
            if (string.Equals(property.PropertyCode, code, StringComparison.OrdinalIgnoreCase))
                return property;
        }

        return null;
    }

    private static Property[] SnapshotProperties(Il2CppSystem.Collections.Generic.List<Property>? properties) =>
        properties is null ? Array.Empty<Property>() : properties.ToArray();

    private static bool ContainsPropertyCode(IEnumerable<Property> properties, string code) =>
        properties.Any(property => string.Equals(property.PropertyCode, code, StringComparison.OrdinalIgnoreCase));

    private static GameObject? FindTarget()
    {
        foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (gameObject is null || !gameObject.scene.IsValid())
                continue;

            if (string.Equals(GetTransformPath(gameObject.transform), TargetPath, StringComparison.Ordinal))
                return gameObject;
        }

        return null;
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
