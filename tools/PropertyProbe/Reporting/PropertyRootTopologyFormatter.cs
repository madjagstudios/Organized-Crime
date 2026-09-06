using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class PropertyRootTopologyFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(PropertyRootTopologySnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("PROPERTY ROOT TOPOLOGY CENSUS");
        builder.AppendLine("Read-only topology capture: no registration, component creation, spawn, ownership, persistence, or scene mutation was attempted.");
        builder.AppendLine();

        foreach (var property in snapshot.Properties.OrderBy(property => property.PropertyCode, StringComparer.Ordinal))
        {
            builder.AppendLine($"PROPERTY_CODE: {property.PropertyCode}");
            builder.AppendLine($"PROPERTY_NAME: {property.PropertyName}");
            builder.AppendLine($"PROPERTY_PATH: {property.PropertyPath}");
            builder.AppendLine($"LOCAL_NETWORK_OBJECT_PRESENT: {property.LocalNetworkObjectPresent}");
            builder.AppendLine($"RESOLVED_NETWORK_OBJECT_PRESENT: {property.ResolvedNetworkObjectPresent}");
            builder.AppendLine($"PARENT_NETWORK_OBJECT_PRESENT: {property.ParentNetworkObjectPresent}");
            builder.AppendLine($"LOCAL_OBJECT_ID: {property.LocalNetworkObject.ObjectId}");
            builder.AppendLine($"LOCAL_SCENE_ID: {property.LocalNetworkObject.SceneId}");
            builder.AppendLine($"LOCAL_STATE: {property.LocalNetworkObject.State}");
            builder.AppendLine($"LOCAL_NETWORK_BEHAVIOUR_COUNT: {property.LocalNetworkObject.NetworkBehaviourCount}");
            builder.AppendLine($"COMPONENT_TYPES: {string.Join(", ", property.ComponentTypes)}");
            builder.AppendLine();
        }

        builder.AppendLine($"PROPERTY_COUNT: {snapshot.Properties.Count}");
        builder.AppendLine($"FISH_WAREHOUSE_PATH: {snapshot.FishWarehousePath}");
        builder.AppendLine($"FISH_WAREHOUSE_LOCAL_NETWORK_OBJECT_PRESENT: {snapshot.FishWarehouseLocalNetworkObjectPresent}");
        builder.AppendLine($"FISH_WAREHOUSE_PARENT_NETWORK_OBJECT_PRESENT: {snapshot.FishWarehouseParentNetworkObjectPresent}");
        builder.AppendLine($"FISH_WAREHOUSE_PARENT_OBJECT_ID: {snapshot.FishWarehouseParentNetworkObject.ObjectId}");
        builder.AppendLine($"FISH_WAREHOUSE_PARENT_SCENE_ID: {snapshot.FishWarehouseParentNetworkObject.SceneId}");
        builder.AppendLine($"FISH_WAREHOUSE_PARENT_STATE: {snapshot.FishWarehouseParentNetworkObject.State}");
        builder.AppendLine($"DOCKS_NETWORK_IDENTITY_UNCHANGED: {snapshot.DocksNetworkIdentityUnchanged}");
        builder.AppendLine($"MUTATION_ATTEMPTED: {snapshot.MutationAttempted}");
        builder.AppendLine($"SPAWN_ATTEMPTED: {snapshot.SpawnAttempted}");
        builder.AppendLine($"OWNERSHIP_ATTEMPTED: {snapshot.OwnershipAttempted}");
        builder.AppendLine($"PERSISTENCE_ATTEMPTED: {snapshot.PersistenceAttempted}");
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(PropertyRootTopologySnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
