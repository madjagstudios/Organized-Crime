using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class PropertySpawnEligibilityFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(PropertySpawnEligibilitySnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FISH WAREHOUSE FISHNET SPAWN ELIGIBILITY PREFLIGHT");
        builder.AppendLine("Metadata capture only: no spawn, ownership, persistence, or existing-property mutation was attempted.");
        builder.AppendLine();
        builder.AppendLine($"TARGET_NAME: {snapshot.TargetName}");
        builder.AppendLine($"TARGET_PATH: {snapshot.TargetPath}");
        builder.AppendLine($"REGISTRATION_PASSED: {snapshot.RegistrationPassed}");
        builder.AppendLine($"METADATA_CAPTURE_PASSED: {snapshot.MetadataCapturePassed}");
        builder.AppendLine($"SPAWN_ATTEMPTED: {snapshot.SpawnAttempted}");
        builder.AppendLine($"CLEANUP_ATTEMPTED: {snapshot.CleanupAttempted}");
        builder.AppendLine($"CLEANUP_PASSED: {snapshot.CleanupPassed}");
        AppendMetadata(builder, "TARGET", snapshot.TargetMetadata);
        AppendMetadata(builder, "DOCKS", snapshot.DocksMetadata);
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(PropertySpawnEligibilitySnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);

    private static void AppendMetadata(StringBuilder builder, string prefix, NetworkObjectMetadata metadata)
    {
        builder.AppendLine();
        builder.AppendLine($"{prefix}_NETWORK_OBJECT_PRESENT: {metadata.Present}");
        builder.AppendLine($"{prefix}_IS_NETWORKED: {metadata.Networked}");
        builder.AppendLine($"{prefix}_IS_SCENE_OBJECT: {metadata.SceneObject}");
        builder.AppendLine($"{prefix}_IS_NESTED: {metadata.Nested}");
        builder.AppendLine($"{prefix}_IS_DEINITIALIZING: {metadata.Deinitializing}");
        builder.AppendLine($"{prefix}_STATE: {metadata.State}");
        builder.AppendLine($"{prefix}_OBJECT_ID: {metadata.ObjectId}");
        builder.AppendLine($"{prefix}_PREFAB_ID: {metadata.PrefabId}");
        builder.AppendLine($"{prefix}_SCENE_ID: {metadata.SceneId}");
        builder.AppendLine($"{prefix}_SPAWNABLE_COLLECTION_ID: {metadata.SpawnableCollectionId}");
        builder.AppendLine($"{prefix}_NETWORK_MANAGER_PRESENT: {metadata.NetworkManagerPresent}");
        builder.AppendLine($"{prefix}_SERVER_MANAGER_PRESENT: {metadata.ServerManagerPresent}");
        builder.AppendLine($"{prefix}_NETWORK_BEHAVIOUR_COUNT: {metadata.NetworkBehaviourCount}");
        builder.AppendLine($"{prefix}_SERIALIZED_JSON: {metadata.SerializedJson}");
    }
}
