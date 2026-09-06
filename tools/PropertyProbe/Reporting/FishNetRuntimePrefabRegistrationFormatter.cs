using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class FishNetRuntimePrefabRegistrationFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(FishNetRuntimePrefabRegistrationSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FISHNET RUNTIME PREFAB REGISTRATION EXPERIMENT");
        builder.AppendLine("Guarded runtime registration: one temporary NetworkObject was eligible for AddObject; no spawn, ownership, persistence, Property mutation, or save operation was attempted.");
        builder.AppendLine();
        builder.AppendLine($"COLLECTION_ID: {snapshot.CollectionId}");
        builder.AppendLine($"TEMPORARY_OBJECT_PATH: {snapshot.TemporaryObjectPath}");
        builder.AppendLine($"BEFORE_BUCKET_OBJECT_COUNT: {snapshot.BeforeBucketObjectCount}");
        builder.AppendLine($"AFTER_BUCKET_OBJECT_COUNT: {snapshot.AfterBucketObjectCount}");
        builder.AppendLine($"ADD_OBJECT_ATTEMPTED: {snapshot.AddObjectAttempted}");
        builder.AppendLine($"REGISTRATION_PASSED: {snapshot.RegistrationPassed}");
        builder.AppendLine($"ADD_OBJECT_RESULT: {snapshot.AddObjectResult}");
        builder.AppendLine($"NETWORK_OBJECT_PRESENT: {snapshot.NetworkObjectPresent}");
        builder.AppendLine($"NETWORKED: {snapshot.Networked}");
        builder.AppendLine($"SCENE_OBJECT: {snapshot.SceneObject}");
        builder.AppendLine($"SPAWNED: {snapshot.Spawned}");
        builder.AppendLine($"NETWORK_STATE: {snapshot.NetworkState}");
        builder.AppendLine($"PREFAB_ID: {snapshot.PrefabId}");
        builder.AppendLine($"SPAWNABLE_COLLECTION_ID: {snapshot.SpawnableCollectionId}");
        builder.AppendLine($"CLEANUP_ATTEMPTED: {snapshot.CleanupAttempted}");
        builder.AppendLine($"CLEANUP_PASSED: {snapshot.CleanupPassed}");
        builder.AppendLine($"TEMPORARY_OBJECT_DESTROYED: {snapshot.TemporaryObjectDestroyed}");
        builder.AppendLine($"AUTHORED_COLLECTION_UNCHANGED: {snapshot.AuthoredCollectionUnchanged}");
        builder.AppendLine($"DOCKS_NETWORK_IDENTITY_UNCHANGED: {snapshot.DocksNetworkIdentityUnchanged}");
        builder.AppendLine($"SPAWN_ATTEMPTED: {snapshot.SpawnAttempted}");
        builder.AppendLine($"OWNERSHIP_ATTEMPTED: {snapshot.OwnershipAttempted}");
        builder.AppendLine($"PERSISTENCE_ATTEMPTED: {snapshot.PersistenceAttempted}");
        builder.AppendLine($"PROPERTY_MUTATION_ATTEMPTED: {snapshot.PropertyMutationAttempted}");
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(FishNetRuntimePrefabRegistrationSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
