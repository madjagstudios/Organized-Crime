using Il2CppFishNet.Object;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Storage;
using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using UnityEngine;

namespace OrganizedCrime.PropertyProbe.Probes;

internal sealed class SafehouseCensusProbe
{
    private bool _hasRun;

    public SafehouseCensusSnapshot Run()
    {
        if (_hasRun)
            return WriteFailure("guard", "Safehouse census already ran in this game process.");

        _hasRun = true;
        ProbeLog.Info("Running read-only Safehouse Property/StorageEntity census. Ownership, item transfer, save, registration, geometry, and Fish Warehouse mutation are disabled.");

        try
        {
            var properties = Property.Properties is null
                ? Array.Empty<Property>()
                : Property.Properties.ToArray().Where(property => property is not null).ToArray();
            var docksBefore = CapturePropertyFingerprint(properties, "dockswarehouse");
            var entries = properties
                .Select(CaptureProperty)
                .OrderBy(entry => entry.PropertyCode, StringComparer.Ordinal)
                .ThenBy(entry => entry.PropertyPath, StringComparer.Ordinal)
                .ToArray();
            var docksAfter = CapturePropertyFingerprint(properties, "dockswarehouse");
            var docksUnchanged = docksBefore == docksAfter;

            var snapshot = new SafehouseCensusSnapshot(
                CapturePhase: "census",
                Properties: entries,
                MutationAttempted: false,
                DocksWarehouseUnchanged: docksUnchanged,
                Gate: docksUnchanged ? "read-only" : "invariant-failed",
                FailureReason: docksUnchanged ? null : "Docks Warehouse fingerprint changed during read-only capture.");

            Write(snapshot);
            ProbeLog.Info($"Safehouse census captured {entries.Length} registered Properties and {entries.Sum(entry => entry.Storages.Count)} descendant native StorageEntity objects.");
            return snapshot;
        }
        catch (Exception ex)
        {
            return WriteFailure("capture", ex.ToString());
        }
    }

    private static SafehousePropertySnapshot CaptureProperty(Property property)
    {
        var storages = (property.GetComponentsInChildren<StorageEntity>(true) ?? Array.Empty<StorageEntity>())
            .Where(storage => storage is not null)
            .GroupBy(storage => GetTransformPath(storage.transform), StringComparer.Ordinal)
            .Select(group => CaptureStorage(property, group.First()))
            .OrderBy(storage => storage.StoragePath, StringComparer.Ordinal)
            .ToArray();

        return new SafehousePropertySnapshot(
            PropertyCode: Safe(() => property.PropertyCode, "<unavailable>"),
            PropertyName: Safe(() => property.PropertyName, "<unavailable>"),
            PropertyPath: GetTransformPath(property.transform),
            IsOwned: Safe(() => property.IsOwned, false),
            Storages: storages,
            RuntimeType: property.GetType().FullName ?? property.GetType().Name,
            InstanceId: property.GetInstanceID(),
            IsRegistered: true,
            NativePropertyType: property.GetType().FullName?.Contains("ScheduleOne.Property.Property", StringComparison.Ordinal) == true,
            HasContentsContainer: Safe(() => property.Container is not null, false));
    }

    private static SafehouseStorageSnapshot CaptureStorage(Property property, StorageEntity storage)
    {
        var networkObject = storage.GetComponent<NetworkObject>();
        return new SafehouseStorageSnapshot(
            PropertyCode: Safe(() => property.PropertyCode, "<unavailable>"),
            StorageName: Safe(() => storage.StorageEntityName, storage.gameObject.name),
            StoragePath: GetTransformPath(storage.transform),
            RuntimeType: storage.GetType().FullName ?? storage.GetType().Name,
            InstanceId: storage.GetInstanceID(),
            SlotCount: Safe(() => storage.SlotCount, 0),
            ItemSlotCount: Safe(() => storage.ItemSlots?.Count ?? 0, 0),
            ItemCount: Safe(() => storage.ItemCount, 0),
            AccessSettings: Safe(() => storage.AccessSettings.ToString(), "<unavailable>"),
            MaxAccessDistance: Safe(() => storage.MaxAccessDistance, 0f),
            IsOpened: Safe(() => storage.IsOpened, false),
            CanBeOpened: Safe(() => storage.CanBeOpened(), false),
            NativeStorageType: storage.GetType().FullName?.Contains("ScheduleOne.Storage.StorageEntity", StringComparison.Ordinal) == true,
            NetworkObjectPresent: networkObject is not null,
            NetworkState: Safe(() => networkObject?.State.ToString() ?? "<unavailable>", "<unavailable>"),
            SceneId: Safe(() => networkObject?.SceneId ?? 0UL, 0UL),
            ObjectId: Safe(() => networkObject?.ObjectId ?? 0, 0));
    }

    private static string CapturePropertyFingerprint(IEnumerable<Property> properties, string propertyCode)
    {
        var property = properties.FirstOrDefault(candidate =>
            string.Equals(Safe(() => candidate.PropertyCode, string.Empty), propertyCode, StringComparison.OrdinalIgnoreCase));
        return property is null
            ? "<missing>"
            : $"{Safe(() => property.PropertyCode, "<unavailable>")}|{GetTransformPath(property.transform)}|{property.GetInstanceID()}";
    }

    private static SafehouseCensusSnapshot WriteFailure(string gate, string reason)
    {
        var snapshot = new SafehouseCensusSnapshot(
            CapturePhase: "census",
            Properties: Array.Empty<SafehousePropertySnapshot>(),
            MutationAttempted: false,
            DocksWarehouseUnchanged: true,
            Gate: gate,
            FailureReason: reason);
        Write(snapshot);
        ProbeLog.Warn($"Safehouse census stopped at {gate}: {reason}");
        return snapshot;
    }

    private static void Write(SafehouseCensusSnapshot snapshot)
    {
        ProbeLog.WriteFile("safehouse-census.txt", SafehouseCensusFormatter.FormatText(snapshot));
        ProbeLog.WriteFile("safehouse-census.json", SafehouseCensusFormatter.FormatJson(snapshot));
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
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
