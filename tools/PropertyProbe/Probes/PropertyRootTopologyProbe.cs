using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class PropertyRootTopologyProbe
{
    private const string TargetPath = "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse";
    private const string DocksWarehouseCode = "dockswarehouse";
    private bool _hasRun;

    public PropertyRootTopologySnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "F18 property root topology census already ran in this game process.");

        _hasRun = true;
        ProbeLog.Info("Running read-only property root topology census. Registration, component creation, spawn, ownership, persistence, and scene mutation are disabled.");

        try
        {
            var properties = Property.Properties is null
                ? Array.Empty<Property>()
                : Property.Properties.ToArray();
            var docks = FindProperty(DocksWarehouseCode);
            var target = FindTarget();

            if (docks is null)
                return WriteFailure("preconditions", "Docks Warehouse was not found by property code.");

            if (target is null)
                return WriteFailure("preconditions", $"Could not find Fish Warehouse at {TargetPath}.");

            var docksBefore = CaptureMetadata(ResolveNetworkObject(docks));
            var entries = properties
                .Where(property => property is not null)
                .Select(CaptureProperty)
                .OrderBy(entry => entry.PropertyCode, StringComparer.Ordinal)
                .ToArray();
            var docksAfterProperty = FindProperty(DocksWarehouseCode);
            var docksAfter = CaptureMetadata(docksAfterProperty is null ? null : ResolveNetworkObject(docksAfterProperty));
            var targetLocalNetworkObject = target.GetComponent<NetworkObject>();
            var targetParentNetworkObject = FindParentNetworkObject(target.transform.parent);
            var docksUnchanged = docksBefore == docksAfter;

            var snapshot = new PropertyRootTopologySnapshot(
                Properties: entries,
                FishWarehousePath: GetTransformPath(target.transform),
                FishWarehouseLocalNetworkObjectPresent: targetLocalNetworkObject is not null,
                FishWarehouseParentNetworkObjectPresent: targetParentNetworkObject is not null,
                FishWarehouseParentNetworkObject: CaptureMetadata(targetParentNetworkObject),
                DocksNetworkIdentityUnchanged: docksUnchanged,
                MutationAttempted: false,
                SpawnAttempted: false,
                OwnershipAttempted: false,
                PersistenceAttempted: false,
                Gate: docksUnchanged ? "read-only" : "invariant-failed",
                FailureReason: docksUnchanged ? null : "Docks Warehouse NetworkObject metadata changed during read-only capture.");

            Write(snapshot);
            ProbeLog.Info(docksUnchanged
                ? "Property root topology census passed. No registration, component creation, spawn, ownership, persistence, or scene mutation was attempted."
                : "Property root topology census failed its Docks Warehouse invariant. No mutation was attempted.");
            return snapshot;
        }
        catch (Exception ex)
        {
            return WriteFailure("capture", ex.ToString());
        }
    }

    private static PropertyRootTopologyEntry CaptureProperty(Property property)
    {
        var localNetworkObject = property.gameObject.GetComponent<NetworkObject>();
        var resolvedNetworkObject = ResolveNetworkObject(property);
        var parentNetworkObject = FindParentNetworkObject(property.transform.parent);
        var componentTypes = (property.gameObject.GetComponents<Component>() ?? Array.Empty<Component>())
            .Where(component => component is not null)
            .Select(component => component.GetType().FullName ?? component.GetType().Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToArray();

        return new PropertyRootTopologyEntry(
            PropertyCode: Safe(() => property.PropertyCode, "<unavailable>"),
            PropertyName: Safe(() => property.PropertyName, "<unavailable>"),
            PropertyPath: GetTransformPath(property.transform),
            LocalNetworkObjectPresent: localNetworkObject is not null,
            ResolvedNetworkObjectPresent: resolvedNetworkObject is not null,
            ParentNetworkObjectPresent: parentNetworkObject is not null,
            LocalNetworkObject: CaptureMetadata(localNetworkObject),
            ResolvedNetworkObject: CaptureMetadata(resolvedNetworkObject),
            ParentNetworkObject: CaptureMetadata(parentNetworkObject),
            ComponentTypes: componentTypes);
    }

    private static PropertyRootTopologySnapshot WriteFailure(string gate, string reason)
    {
        var docks = FindProperty(DocksWarehouseCode);
        var target = FindTarget();
        var snapshot = new PropertyRootTopologySnapshot(
            Properties: Array.Empty<PropertyRootTopologyEntry>(),
            FishWarehousePath: target is null ? TargetPath : GetTransformPath(target.transform),
            FishWarehouseLocalNetworkObjectPresent: target?.GetComponent<NetworkObject>() is not null,
            FishWarehouseParentNetworkObjectPresent: target is not null && FindParentNetworkObject(target.transform.parent) is not null,
            FishWarehouseParentNetworkObject: target is null ? NetworkObjectMetadata.Empty : CaptureMetadata(FindParentNetworkObject(target.transform.parent)),
            DocksNetworkIdentityUnchanged: true,
            MutationAttempted: false,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            Gate: gate,
            FailureReason: reason);

        Write(snapshot);
        ProbeLog.Warn($"Property root topology census stopped at {gate}: {reason}");
        return snapshot;
    }

    private static Property? FindProperty(string code)
    {
        if (Property.Properties is null)
            return null;

        foreach (var property in Property.Properties)
        {
            if (property is not null && string.Equals(property.PropertyCode, code, StringComparison.OrdinalIgnoreCase))
                return property;
        }

        return null;
    }

    private static GameObject? FindTarget()
    {
        foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (gameObject is not null && gameObject.scene.IsValid() &&
                string.Equals(GetTransformPath(gameObject.transform), TargetPath, StringComparison.Ordinal))
                return gameObject;
        }

        return null;
    }

    private static NetworkObject? ResolveNetworkObject(Property property)
    {
        try
        {
            if (property.NetworkObject is not null)
                return property.NetworkObject;
        }
        catch
        {
            // Fall through to local component capture.
        }

        return property.gameObject.GetComponent<NetworkObject>();
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

    private static void Write(PropertyRootTopologySnapshot snapshot)
    {
        ProbeLog.WriteFile("property-root-topology.txt", PropertyRootTopologyFormatter.FormatText(snapshot));
        ProbeLog.WriteFile("property-root-topology.json", PropertyRootTopologyFormatter.FormatJson(snapshot));
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
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
