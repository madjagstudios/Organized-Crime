using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class SafehouseCensusFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(SafehouseCensusSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("SAFEHOUSE PROPERTY/STORAGE CENSUS");
        builder.AppendLine("Read-only capture: no ownership, storage, save, geometry, registration, or Fish Warehouse mutation was attempted.");
        builder.AppendLine($"CAPTURE_PHASE: {snapshot.CapturePhase}");
        builder.AppendLine();

        foreach (var property in snapshot.Properties
                     .OrderBy(property => property.PropertyCode, StringComparer.Ordinal)
                     .ThenBy(property => property.PropertyPath, StringComparer.Ordinal))
        {
            builder.AppendLine($"PROPERTY_CODE: {property.PropertyCode}");
            builder.AppendLine($"PROPERTY_NAME: {property.PropertyName}");
            builder.AppendLine($"PROPERTY_PATH: {property.PropertyPath}");
            builder.AppendLine($"PROPERTY_TYPE: {property.RuntimeType}");
            builder.AppendLine($"PROPERTY_REGISTERED: {property.IsRegistered}");
            builder.AppendLine($"PROPERTY_NATIVE_TYPE: {property.NativePropertyType}");
            builder.AppendLine($"PROPERTY_OWNED: {property.IsOwned}");
            builder.AppendLine($"PROPERTY_INSTANCE_ID: {property.InstanceId}");

            foreach (var storage in property.Storages.OrderBy(storage => storage.StoragePath, StringComparer.Ordinal))
            {
                builder.AppendLine($"  STORAGE_NAME: {storage.StorageName}");
                builder.AppendLine($"  STORAGE_PATH: {storage.StoragePath}");
                builder.AppendLine($"  STORAGE_TYPE: {storage.RuntimeType}");
                builder.AppendLine($"  STORAGE_NATIVE_TYPE: {storage.NativeStorageType}");
                builder.AppendLine($"  STORAGE_INSTANCE_ID: {storage.InstanceId}");
                builder.AppendLine($"  STORAGE_SLOTS: {storage.ItemSlotCount} / {storage.SlotCount}");
                builder.AppendLine($"  STORAGE_ITEM_COUNT: {storage.ItemCount}");
                builder.AppendLine($"  STORAGE_ACCESS: {storage.AccessSettings}");
                builder.AppendLine($"  STORAGE_CAN_BE_OPENED: {storage.CanBeOpened}");
                builder.AppendLine($"  STORAGE_NETWORK_OBJECT: {storage.NetworkObjectPresent}");
                builder.AppendLine($"  STORAGE_NETWORK_STATE: {storage.NetworkState}");
                builder.AppendLine($"  STORAGE_OBJECT_ID: {storage.ObjectId}");
                builder.AppendLine($"  STORAGE_SCENE_ID: {storage.SceneId}");
            }

            if (!string.IsNullOrWhiteSpace(property.CaptureError))
                builder.AppendLine($"CAPTURE_ERROR: {property.CaptureError}");

            builder.AppendLine();
        }

        builder.AppendLine($"PROPERTY_COUNT: {snapshot.Properties.Count}");
        builder.AppendLine($"MUTATION_ATTEMPTED: {snapshot.MutationAttempted}");
        builder.AppendLine($"DOCKS_WAREHOUSE_UNCHANGED: {snapshot.DocksWarehouseUnchanged}");
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(SafehouseCensusSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
