namespace OrganizedCrime.PropertyProbe.Model;

public sealed record SafehouseStorageSnapshot(
    string PropertyCode,
    string StorageName,
    string StoragePath,
    string RuntimeType,
    int InstanceId,
    int SlotCount,
    int ItemSlotCount,
    int ItemCount,
    string AccessSettings,
    float MaxAccessDistance,
    bool IsOpened,
    bool CanBeOpened,
    bool NativeStorageType,
    bool NetworkObjectPresent,
    string NetworkState,
    ulong SceneId,
    int ObjectId)
{
    public string StableIdentity => ObjectId > 0
        ? $"{StoragePath}|scene:{SceneId}|object:{ObjectId}"
        : $"{StoragePath}|scene:{SceneId}";

    public static SafehouseStorageSnapshot Test(string propertyCode, string storagePath, int itemCount = 0) =>
        new(
            PropertyCode: propertyCode,
            StorageName: storagePath[(storagePath.LastIndexOf('/') + 1)..],
            StoragePath: storagePath,
            RuntimeType: "Il2CppScheduleOne.Storage.StorageEntity",
            InstanceId: 1,
            SlotCount: 8,
            ItemSlotCount: 8,
            ItemCount: itemCount,
            AccessSettings: "SinglePlayerOnly",
            MaxAccessDistance: 3f,
            IsOpened: false,
            CanBeOpened: true,
            NativeStorageType: true,
            NetworkObjectPresent: true,
            NetworkState: "Spawned",
            SceneId: 1,
            ObjectId: 1);
}

public sealed record SafehousePropertySnapshot(
    string PropertyCode,
    string PropertyName,
    string PropertyPath,
    bool IsOwned,
    IReadOnlyList<SafehouseStorageSnapshot> Storages,
    string RuntimeType = "Il2CppScheduleOne.Property.Property",
    int InstanceId = 1,
    bool IsRegistered = true,
    bool NativePropertyType = true,
    bool HasContentsContainer = true,
    string? CaptureError = null)
{
    public string StableIdentity => PropertyCode;

    public static SafehousePropertySnapshot Test(
        string propertyCode,
        bool owned = false,
        string? storagePath = null,
        int itemCount = 0)
    {
        var storages = storagePath is null
            ? Array.Empty<SafehouseStorageSnapshot>()
            : new[] { SafehouseStorageSnapshot.Test(propertyCode, storagePath, itemCount) };

        return new SafehousePropertySnapshot(
            PropertyCode: propertyCode,
            PropertyName: propertyCode,
            PropertyPath: $"World/{propertyCode}",
            IsOwned: owned,
            Storages: storages);
    }
}

public sealed record SafehouseCensusSnapshot(
    string CapturePhase,
    IReadOnlyList<SafehousePropertySnapshot> Properties,
    bool MutationAttempted,
    bool DocksWarehouseUnchanged,
    string Gate,
    string? FailureReason = null)
{
    public static SafehouseCensusSnapshot Test(params SafehousePropertySnapshot[] properties) =>
        new("initial", properties, false, true, "read-only");
}

public sealed record SafehouseCensusAssessment(string Outcome, IReadOnlyList<string> Reasons);

internal static class SafehouseCensusTrigger
{
    public static bool IsPressed(bool f6Pressed, bool f17Pressed) =>
        f6Pressed || f17Pressed;
}
