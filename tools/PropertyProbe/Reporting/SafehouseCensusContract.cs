using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class SafehouseCensusContract
{
    public static SafehouseCensusAssessment Assess(
        SafehouseCensusSnapshot before,
        SafehouseCensusSnapshot after,
        string propertyCode,
        string storagePath)
    {
        if (before.MutationAttempted || after.MutationAttempted)
            return Stop("census or proof attempted mutation");

        if (!before.DocksWarehouseUnchanged || !after.DocksWarehouseUnchanged)
            return Stop("Docks Warehouse changed during the bounded capture");

        if (HasDuplicatePropertyIdentity(before) || HasDuplicatePropertyIdentity(after))
            return Stop("duplicate registered Property identity was observed");

        if (HasDuplicateStorageIdentity(before) || HasDuplicateStorageIdentity(after))
            return Stop("duplicate StorageEntity identity was observed");

        var beforeProperty = FindProperty(before, propertyCode);
        var afterProperty = FindProperty(after, propertyCode);
        if (beforeProperty is null || afterProperty is null)
            return Inconclusive("named Property identity was not observed in both captures");

        var beforeStorage = FindStorage(beforeProperty, storagePath);
        var afterStorage = FindStorage(afterProperty, storagePath);
        if (beforeStorage is null || afterStorage is null)
            return Inconclusive("named native StorageEntity identity was not observed in both captures");

        var reasons = new List<string>();
        if (beforeProperty.StableIdentity != afterProperty.StableIdentity ||
            beforeProperty.PropertyPath != afterProperty.PropertyPath ||
            beforeProperty.PropertyName != afterProperty.PropertyName)
            reasons.Add("Property identity is not stable across reload");

        if (!beforeProperty.NativePropertyType || !afterProperty.NativePropertyType ||
            !beforeProperty.IsRegistered || !afterProperty.IsRegistered)
            reasons.Add("native registered Property ownership is not established");

        if (!beforeProperty.IsOwned || !afterProperty.IsOwned)
            reasons.Add("Property ownership is not observed in both captures");

        if (!beforeStorage.NativeStorageType || !afterStorage.NativeStorageType ||
            beforeStorage.StorageName != afterStorage.StorageName)
            reasons.Add("native StorageEntity identity is not stable across reload");

        var storageIdentityReason = CompareStorageIdentity(beforeStorage, afterStorage);
        if (storageIdentityReason is not null)
            reasons.Add(storageIdentityReason);

        if (beforeStorage.ItemSlotCount <= 0 || afterStorage.ItemSlotCount <= 0 ||
            !beforeStorage.CanBeOpened || !afterStorage.CanBeOpened)
            reasons.Add("usable storage access is not observed");

        if (beforeStorage.ItemCount != afterStorage.ItemCount)
            reasons.Add("storage contents did not round-trip");

        if (!string.Equals(after.CapturePhase, "full-reload", StringComparison.OrdinalIgnoreCase))
            reasons.Add("full-reload capture phase was not observed");

        return reasons.Count == 0
            ? new SafehouseCensusAssessment("PASS", Array.Empty<string>())
            : new SafehouseCensusAssessment("INCONCLUSIVE", reasons);
    }

    private static SafehousePropertySnapshot? FindProperty(SafehouseCensusSnapshot snapshot, string propertyCode) =>
        snapshot.Properties.FirstOrDefault(property =>
            string.Equals(property.PropertyCode, propertyCode, StringComparison.OrdinalIgnoreCase));

    private static SafehouseStorageSnapshot? FindStorage(SafehousePropertySnapshot property, string storagePath) =>
        property.Storages.FirstOrDefault(storage =>
            string.Equals(storage.StoragePath, storagePath, StringComparison.Ordinal));

    private static bool HasDuplicatePropertyIdentity(SafehouseCensusSnapshot snapshot) =>
        snapshot.Properties
            .GroupBy(property => property.StableIdentity, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);

    private static bool HasDuplicateStorageIdentity(SafehouseCensusSnapshot snapshot) =>
        snapshot.Properties
            .SelectMany(property => property.Storages)
            .GroupBy(storage => storage.StableIdentity, StringComparer.Ordinal)
            .Any(group => group.Count() > 1);

    private static string? CompareStorageIdentity(
        SafehouseStorageSnapshot before,
        SafehouseStorageSnapshot after)
    {
        if (before.SceneId == 0 || after.SceneId == 0 ||
            before.ObjectId <= 0 || after.ObjectId <= 0)
            return "native StorageEntity identity was unavailable in one or both captures";

        return before.StableIdentity == after.StableIdentity
            ? null
            : "native StorageEntity identity is not stable across reload";
    }

    private static SafehouseCensusAssessment Inconclusive(string reason) =>
        new("INCONCLUSIVE", new[] { reason });

    private static SafehouseCensusAssessment Stop(string reason) =>
        new("STOP", new[] { reason });
}
