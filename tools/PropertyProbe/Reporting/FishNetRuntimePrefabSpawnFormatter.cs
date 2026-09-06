using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class FishNetRuntimePrefabSpawnFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(FishNetRuntimePrefabSpawnSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FISHNET RUNTIME PREFAB SPAWN EXPERIMENT");
        builder.AppendLine("Guarded temporary FishNet spawn/despawn: no Schedule I Property, ownership, persistence, employee, delivery, or save operation was attempted.");
        builder.AppendLine();
        builder.AppendLine($"COLLECTION_ID: {snapshot.CollectionId}");
        builder.AppendLine($"SERVER_MANAGER_PRESENT: {snapshot.ServerManagerPresent}");
        builder.AppendLine($"SERVER_AVAILABLE: {snapshot.ServerAvailable}");
        builder.AppendLine($"REGISTRATION_PASSED: {snapshot.RegistrationPassed}");
        builder.AppendLine($"SPAWN_ATTEMPTED: {snapshot.SpawnAttempted}");
        builder.AppendLine($"SPAWN_PASSED: {snapshot.SpawnPassed}");
        builder.AppendLine($"DESPAWN_ATTEMPTED: {snapshot.DespawnAttempted}");
        builder.AppendLine($"DESPAWN_PASSED: {snapshot.DespawnPassed}");
        builder.AppendLine($"NETWORK_OBJECT_PRESENT: {snapshot.NetworkObjectPresent}");
        builder.AppendLine($"NETWORKED: {snapshot.Networked}");
        builder.AppendLine($"SPAWNED: {snapshot.Spawned}");
        builder.AppendLine($"SERVER_INITIALIZED: {snapshot.ServerInitialized}");
        builder.AppendLine($"NETWORK_STATE: {snapshot.NetworkState}");
        builder.AppendLine($"OBJECT_ID: {snapshot.ObjectId}");
        builder.AppendLine($"PREFAB_ID: {snapshot.PrefabId}");
        builder.AppendLine($"SPAWNABLE_COLLECTION_ID: {snapshot.SpawnableCollectionId}");
        builder.AppendLine($"BUCKET_CLEANUP_ATTEMPTED: {snapshot.BucketCleanupAttempted}");
        builder.AppendLine($"BUCKET_CLEANUP_PASSED: {snapshot.BucketCleanupPassed}");
        builder.AppendLine($"TEMPORARY_OBJECT_DESTROYED: {snapshot.TemporaryObjectDestroyed}");
        builder.AppendLine($"AUTHORED_COLLECTION_UNCHANGED: {snapshot.AuthoredCollectionUnchanged}");
        builder.AppendLine($"DOCKS_NETWORK_IDENTITY_UNCHANGED: {snapshot.DocksNetworkIdentityUnchanged}");
        builder.AppendLine($"OWNERSHIP_ATTEMPTED: {snapshot.OwnershipAttempted}");
        builder.AppendLine($"PERSISTENCE_ATTEMPTED: {snapshot.PersistenceAttempted}");
        builder.AppendLine($"PROPERTY_MUTATION_ATTEMPTED: {snapshot.PropertyMutationAttempted}");
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(FishNetRuntimePrefabSpawnSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
