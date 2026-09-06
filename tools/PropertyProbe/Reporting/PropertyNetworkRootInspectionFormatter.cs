using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class PropertyNetworkRootInspectionFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(PropertyNetworkRootInspectionSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FISH WAREHOUSE NETWORK ROOT INSPECTION");
        builder.AppendLine("Read-only chain inspection: no spawn, ownership, persistence, or existing-property mutation was attempted.");
        builder.AppendLine();
        builder.AppendLine($"TARGET_NAME: {snapshot.TargetName}");
        builder.AppendLine($"TARGET_PATH: {snapshot.TargetPath}");
        builder.AppendLine($"REGISTRATION_PASSED: {snapshot.RegistrationPassed}");
        builder.AppendLine($"METADATA_CAPTURE_PASSED: {snapshot.MetadataCapturePassed}");
        builder.AppendLine($"SPAWN_ATTEMPTED: {snapshot.SpawnAttempted}");
        builder.AppendLine($"OWNERSHIP_ATTEMPTED: {snapshot.OwnershipAttempted}");
        builder.AppendLine($"PERSISTENCE_ATTEMPTED: {snapshot.PersistenceAttempted}");
        builder.AppendLine($"CLEANUP_ATTEMPTED: {snapshot.CleanupAttempted}");
        builder.AppendLine($"CLEANUP_PASSED: {snapshot.CleanupPassed}");

        for (var index = 0; index < snapshot.Chain.Count; index++)
        {
            var entry = snapshot.Chain[index];
            var prefix = $"CHAIN_{index}";
            builder.AppendLine();
            builder.AppendLine($"{prefix}_NAME: {entry.Name}");
            builder.AppendLine($"{prefix}_PATH: {entry.Path}");
            builder.AppendLine($"{prefix}_LOCAL_NETWORK_OBJECT_PRESENT: {entry.LocalNetworkObjectPresent}");
            builder.AppendLine($"{prefix}_PARENT_NETWORK_OBJECT_RESOLVED: {entry.ParentNetworkObjectResolved}");
            builder.AppendLine($"{prefix}_IS_SCENE_OBJECT: {entry.Metadata.SceneObject}");
            builder.AppendLine($"{prefix}_IS_NETWORKED: {entry.Metadata.Networked}");
            builder.AppendLine($"{prefix}_STATE: {entry.Metadata.State}");
            builder.AppendLine($"{prefix}_OBJECT_ID: {entry.Metadata.ObjectId}");
            builder.AppendLine($"{prefix}_PREFAB_ID: {entry.Metadata.PrefabId}");
            builder.AppendLine($"{prefix}_SCENE_ID: {entry.Metadata.SceneId}");
            builder.AppendLine($"{prefix}_NETWORK_MANAGER_PRESENT: {entry.Metadata.NetworkManagerPresent}");
            builder.AppendLine($"{prefix}_SERVER_MANAGER_PRESENT: {entry.Metadata.ServerManagerPresent}");
            builder.AppendLine($"{prefix}_NETWORK_BEHAVIOUR_COUNT: {entry.Metadata.NetworkBehaviourCount}");
        }

        builder.AppendLine();
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(PropertyNetworkRootInspectionSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
