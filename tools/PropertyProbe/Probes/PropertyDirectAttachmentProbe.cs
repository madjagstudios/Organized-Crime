using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class PropertyDirectAttachmentProbe
{
    private const string TargetName = "Fish Warehouse";
    private const string TargetPath = "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse";
    private const string ProposedPropertyCode = "oc_fishwarehouse";
    private const string DocksWarehouseCode = "dockswarehouse";

    private readonly PropertyPromotionExperimentProbe _promotionExperimentProbe;
    private bool _hasRun;

    public PropertyDirectAttachmentProbe(PropertyPromotionExperimentProbe promotionExperimentProbe)
    {
        _promotionExperimentProbe = promotionExperimentProbe;
    }

    public PropertyDirectAttachmentSnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F17 direct Property attachment already ran in this game process.");

        _hasRun = true;
        ProbeLog.Info("Running gated Fish Warehouse direct Property attachment experiment. Spawn, ownership, and persistence are disabled.");

        if (!_promotionExperimentProbe.TryGetRegisteredExperiment(out _, out _))
            return WriteFailure("preconditions", "F11 registration did not pass in this game process.");

        GameObject? target = null;
        Property? property = null;
        NetworkObject? parentNetworkObject = null;
        var attachmentAttempted = false;
        var cleanupAttempted = false;
        var cleanupPassed = true;

        try
        {
            _promotionExperimentProbe.CleanupRegisteredExperiment();
            target = FindTarget();
            if (target is null)
                return WriteFailure("preconditions", $"Could not find target GameObject at {TargetPath}.");

            if (target.GetComponent<Property>() is not null)
                return WriteFailure("preconditions", "Fish Warehouse already has a Property component.", target);

            var docksBeforeProperty = FindProperty(DocksWarehouseCode);
            if (docksBeforeProperty is null)
                return WriteFailure("preconditions", "Docks Warehouse was not found by property code.", target);

            if (FindProperty(ProposedPropertyCode) is not null)
                return WriteFailure("preconditions", $"Property code {ProposedPropertyCode} is already registered.", target, docksBeforeProperty);

            parentNetworkObject = FindParentNetworkObject(target.transform.parent);
            if (parentNetworkObject is null)
                return WriteFailure("preconditions", "Fish Warehouse did not resolve a parent NetworkObject.", target, docksBeforeProperty);

            var targetLocalBefore = target.GetComponent<NetworkObject>() is not null;
            var parentBefore = CaptureMetadata(parentNetworkObject);
            var docksMetadataBefore = CaptureMetadata(ResolveNetworkObject(docksBeforeProperty));

            attachmentAttempted = true;
            property = target.AddComponent<Property>();
            JsonUtility.FromJsonOverwrite(PropertyPromotionIdentityPayload.Json, property);
            RegisterPropertyCollections(property);
            property.InitializeSaveable();
            property.NetworkInitializeIfDisabled();

            var resolvedNetworkObject = ResolveNetworkObject(property);
            var parentAfter = CaptureMetadata(parentNetworkObject);
            var docksMetadataAfter = CaptureMetadata(ResolveNetworkObject(FindProperty(DocksWarehouseCode)));
            var targetLocalAfter = target.GetComponent<NetworkObject>() is not null;
            var resolvedParent = resolvedNetworkObject is not null && resolvedNetworkObject == parentNetworkObject;
            var registered = FindProperty(ProposedPropertyCode) == property;
            var unowned = SnapshotProperties(Property.UnownedProperties).Contains(property);
            var attachmentPassed = resolvedParent &&
                !targetLocalAfter &&
                registered &&
                unowned &&
                parentAfter.Present &&
                parentAfter.SceneObject &&
                parentAfter.State == "Spawned" &&
                docksMetadataBefore == docksMetadataAfter;

            var snapshot = new PropertyDirectAttachmentSnapshot(
                TargetName: target.name,
                TargetPath: GetTransformPath(target.transform),
                RegistrationPassed: true,
                AttachmentAttempted: attachmentAttempted,
                AttachmentPassed: attachmentPassed,
                TargetLocalNetworkObjectPresentBefore: targetLocalBefore,
                TargetLocalNetworkObjectPresentAfter: targetLocalAfter,
                PropertyNetworkObjectResolved: resolvedNetworkObject is not null,
                PropertyNetworked: Read(() => property.IsNetworked),
                PropertyNetworkInitialized: resolvedNetworkObject is not null,
                PropertyClientInitialized: Read(() => property.IsClientInitialized),
                PropertyServerInitialized: Read(() => property.IsServerInitialized),
                PropertySpawned: Read(() => property.IsSpawned),
                TargetRegistered: registered,
                TargetInUnownedCollection: unowned,
                ParentNetworkBehaviourCountBefore: parentBefore.NetworkBehaviourCount,
                ParentNetworkBehaviourCountAfter: parentAfter.NetworkBehaviourCount,
                ParentNetworkBehaviourCountAfterCleanup: 0,
                ParentMetadata: parentAfter,
                DocksMetadataBefore: docksMetadataBefore,
                DocksMetadataAfter: docksMetadataAfter,
                SpawnAttempted: false,
                OwnershipAttempted: false,
                PersistenceAttempted: false,
                CleanupAttempted: false,
                CleanupPassed: false,
                Gate: attachmentPassed ? "direct-attachment" : "attachment-failed",
                FailureReason: attachmentPassed ? null : "Direct Property attachment or Docks Warehouse invariant check failed.");

            return CompleteCleanup(snapshot, target, property, parentNetworkObject, ref cleanupAttempted, ref cleanupPassed);
        }
        catch (Exception ex)
        {
            var snapshot = new PropertyDirectAttachmentSnapshot(
                TargetName: target?.name ?? TargetName,
                TargetPath: target is null ? TargetPath : GetTransformPath(target.transform),
                RegistrationPassed: true,
                AttachmentAttempted: attachmentAttempted,
                AttachmentPassed: false,
                TargetLocalNetworkObjectPresentBefore: false,
                TargetLocalNetworkObjectPresentAfter: target?.GetComponent<NetworkObject>() is not null,
                PropertyNetworkObjectResolved: property is not null && ResolveNetworkObject(property) is not null,
                PropertyNetworked: property is not null && Read(() => property.IsNetworked),
                PropertyNetworkInitialized: false,
                PropertyClientInitialized: false,
                PropertyServerInitialized: false,
                PropertySpawned: false,
                TargetRegistered: property is not null && FindProperty(ProposedPropertyCode) == property,
                TargetInUnownedCollection: property is not null && SnapshotProperties(Property.UnownedProperties).Contains(property),
                ParentNetworkBehaviourCountBefore: 0,
                ParentNetworkBehaviourCountAfter: parentNetworkObject is null ? 0 : CaptureMetadata(parentNetworkObject).NetworkBehaviourCount,
                ParentNetworkBehaviourCountAfterCleanup: 0,
                ParentMetadata: CaptureMetadata(parentNetworkObject),
                DocksMetadataBefore: CaptureMetadata(ResolveNetworkObject(FindProperty(DocksWarehouseCode))),
                DocksMetadataAfter: CaptureMetadata(ResolveNetworkObject(FindProperty(DocksWarehouseCode))),
                SpawnAttempted: false,
                OwnershipAttempted: false,
                PersistenceAttempted: false,
                CleanupAttempted: false,
                CleanupPassed: false,
                Gate: "attachment-failed",
                FailureReason: ex.ToString());

            return CompleteCleanup(snapshot, target, property, parentNetworkObject, ref cleanupAttempted, ref cleanupPassed);
        }
    }

    private static PropertyDirectAttachmentSnapshot CompleteCleanup(
        PropertyDirectAttachmentSnapshot snapshot,
        GameObject? target,
        Property? property,
        NetworkObject? parentNetworkObject,
        ref bool cleanupAttempted,
        ref bool cleanupPassed)
    {
        cleanupAttempted = true;
        try
        {
            if (property is not null)
                RemovePropertyCollections(property);

            if (property is not null)
                UnityEngine.Object.Destroy(property);

            var collectionRemoved = property is null ||
                !SnapshotProperties(Property.Properties).Contains(property) &&
                !SnapshotProperties(Property.OwnedProperties).Contains(property) &&
                !SnapshotProperties(Property.UnownedProperties).Contains(property);
            var componentRemoved = target is null || target.GetComponent<Property>() is null;
            cleanupPassed = collectionRemoved && componentRemoved;
        }
        catch (Exception ex)
        {
            cleanupPassed = false;
            ProbeLog.Warn($"Temporary Fish Warehouse direct Property cleanup failed: {ex}");
        }

        var completed = snapshot with
        {
            CleanupAttempted = cleanupAttempted,
            CleanupPassed = cleanupPassed,
            ParentNetworkBehaviourCountAfterCleanup = CaptureMetadata(parentNetworkObject).NetworkBehaviourCount,
            FailureReason = snapshot.FailureReason ?? (cleanupPassed ? null : "Temporary Property cleanup failed.")
        };

        Write(completed);
        ProbeLog.Info(completed.AttachmentPassed && completed.CleanupPassed
            ? "Direct Property attachment gate passed. Spawn, ownership, and persistence were not attempted."
            : "Direct Property attachment gate did not pass. Spawn, ownership, and persistence were not attempted.");
        return completed;
    }

    private static PropertyDirectAttachmentSnapshot WriteFailure(
        string gate,
        string reason,
        GameObject? target = null,
        Property? docks = null)
    {
        var snapshot = new PropertyDirectAttachmentSnapshot(
            TargetName: target?.name ?? TargetName,
            TargetPath: target is null ? TargetPath : GetTransformPath(target.transform),
            RegistrationPassed: false,
            AttachmentAttempted: false,
            AttachmentPassed: false,
            TargetLocalNetworkObjectPresentBefore: false,
            TargetLocalNetworkObjectPresentAfter: target?.GetComponent<NetworkObject>() is not null,
            PropertyNetworkObjectResolved: false,
            PropertyNetworked: false,
            PropertyNetworkInitialized: false,
            PropertyClientInitialized: false,
            PropertyServerInitialized: false,
            PropertySpawned: false,
            TargetRegistered: false,
            TargetInUnownedCollection: false,
            ParentNetworkBehaviourCountBefore: 0,
            ParentNetworkBehaviourCountAfter: 0,
            ParentNetworkBehaviourCountAfterCleanup: 0,
            ParentMetadata: NetworkObjectMetadata.Empty,
            DocksMetadataBefore: CaptureMetadata(ResolveNetworkObject(docks)),
            DocksMetadataAfter: CaptureMetadata(ResolveNetworkObject(FindProperty(DocksWarehouseCode))),
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            CleanupAttempted: true,
            CleanupPassed: true,
            Gate: gate,
            FailureReason: reason);

        Write(snapshot);
        ProbeLog.Warn($"Direct Property attachment stopped at {gate}: {reason}");
        return snapshot;
    }

    private static NetworkObject? ResolveNetworkObject(Property? property)
    {
        if (property is null)
            return null;

        try
        {
            if (property.NetworkObject is not null)
                return property.NetworkObject;
        }
        catch
        {
            // Fall through to local component lookup for diagnostics.
        }

        return property.GetComponent<NetworkObject>();
    }

    private static NetworkObject? FindParentNetworkObject(Transform? parent)
    {
        var current = parent;
        while (current is not null)
        {
            var networkObject = current.GetComponent<NetworkObject>();
            if (networkObject is not null)
                return networkObject;

            current = current.parent;
        }

        return null;
    }

    private static NetworkObjectMetadata CaptureMetadata(NetworkObject? networkObject)
    {
        if (networkObject is null)
            return NetworkObjectMetadata.Empty;

        return new NetworkObjectMetadata(
            Present: true,
            Networked: Read(() => networkObject.IsNetworked),
            SceneObject: Read(() => networkObject.IsSceneObject),
            Nested: Read(() => networkObject.IsNested),
            Deinitializing: Read(() => networkObject.IsDeinitializing),
            State: ReadString(() => networkObject.State.ToString()),
            ObjectId: ReadInt(() => networkObject.ObjectId),
            PrefabId: ReadUShort(() => networkObject.PrefabId),
            SceneId: ReadULong(() => networkObject.SceneId),
            SpawnableCollectionId: ReadUShort(() => networkObject.SpawnableCollectionId),
            NetworkManagerPresent: Read(() => networkObject.NetworkManager is not null),
            ServerManagerPresent: Read(() => networkObject.ServerManager is not null),
            NetworkBehaviourCount: ReadInt(() => networkObject.NetworkBehaviours?.Length ?? 0),
            SerializedJson: ReadString(() => JsonUtility.ToJson(networkObject)));
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

        return null;
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

    private static Property[] SnapshotProperties(Il2CppSystem.Collections.Generic.List<Property>? properties) =>
        properties is null ? Array.Empty<Property>() : properties.ToArray();

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

    private static void Write(PropertyDirectAttachmentSnapshot snapshot)
    {
        ProbeLog.WriteFile(
            "property-direct-attachment.txt",
            PropertyDirectAttachmentFormatter.FormatText(snapshot));
        ProbeLog.WriteFile(
            "property-direct-attachment.json",
            PropertyDirectAttachmentFormatter.FormatJson(snapshot));
    }

    private static bool Read(Func<bool> read)
    {
        try { return read(); }
        catch { return false; }
    }

    private static int ReadInt(Func<int> read)
    {
        try { return read(); }
        catch { return 0; }
    }

    private static ushort ReadUShort(Func<ushort> read)
    {
        try { return read(); }
        catch { return 0; }
    }

    private static ulong ReadULong(Func<ulong> read)
    {
        try { return read(); }
        catch { return 0; }
    }

    private static string ReadString(Func<string> read)
    {
        try { return read(); }
        catch (Exception ex) { return $"<unavailable: {ex.GetType().Name}>"; }
    }
}
