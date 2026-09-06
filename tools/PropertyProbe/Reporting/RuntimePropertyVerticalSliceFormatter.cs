using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class RuntimePropertyVerticalSliceFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(RuntimePropertyVerticalSliceSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("RUNTIME PROPERTY VERTICAL SLICE");
        builder.AppendLine("Guarded temporary Schedule I Property runtime slice: registration, network initialization, spawn, and cleanup only; ownership, persistence, and save writes were not attempted.");
        builder.AppendLine();
        builder.AppendLine($"PROPOSED_PROPERTY_CODE: {snapshot.ProposedPropertyCode}");
        builder.AppendLine($"TARGET_NAME: {snapshot.TargetName}");
        builder.AppendLine($"COLLECTION_ID: {snapshot.CollectionId}");
        builder.AppendLine($"SERVER_MANAGER_PRESENT: {snapshot.ServerManagerPresent}");
        builder.AppendLine($"SERVER_AVAILABLE: {snapshot.ServerAvailable}");
        builder.AppendLine($"TEMPORARY_ROOT_CREATED: {snapshot.TemporaryRootCreated}");
        builder.AppendLine($"NETWORK_OBJECT_PRESENT: {snapshot.NetworkObjectPresent}");
        builder.AppendLine($"PROPERTY_PRESENT: {snapshot.PropertyPresent}");
        builder.AppendLine($"IDENTITY_CONFIGURED: {snapshot.IdentityConfigured}");
        builder.AppendLine($"PROPERTY_REGISTERED: {snapshot.PropertyRegistered}");
        builder.AppendLine($"RUNTIME_NETWORK_REGISTRATION_PASSED: {snapshot.RuntimeNetworkRegistrationPassed}");
        builder.AppendLine($"PROPERTY_COUNT_BEFORE: {snapshot.PropertyCountBefore}");
        builder.AppendLine($"PROPERTY_COUNT_AFTER: {snapshot.PropertyCountAfter}");
        builder.AppendLine($"TARGET_IN_PROPERTIES: {snapshot.TargetInProperties}");
        builder.AppendLine($"TARGET_IN_UNOWNED_PROPERTIES: {snapshot.TargetInUnownedProperties}");
        builder.AppendLine($"TARGET_IN_OWNED_PROPERTIES: {snapshot.TargetInOwnedProperties}");
        builder.AppendLine($"PROPERTY_NETWORK_OBJECT_RESOLVED: {snapshot.PropertyNetworkObjectResolved}");
        builder.AppendLine($"NETWORK_INITIALIZE_ATTEMPTED: {snapshot.NetworkInitializeAttempted}");
        builder.AppendLine($"NETWORK_INITIALIZE_PASSED: {snapshot.NetworkInitializePassed}");
        builder.AppendLine($"PROPERTY_NETWORKED: {snapshot.PropertyNetworked}");
        builder.AppendLine($"PROPERTY_CLIENT_INITIALIZED: {snapshot.PropertyClientInitialized}");
        builder.AppendLine($"PROPERTY_SERVER_INITIALIZED: {snapshot.PropertyServerInitialized}");
        builder.AppendLine($"PROPERTY_SPAWNED: {snapshot.PropertySpawned}");
        builder.AppendLine($"SPAWN_ATTEMPTED: {snapshot.SpawnAttempted}");
        builder.AppendLine($"SPAWN_PASSED: {snapshot.SpawnPassed}");
        builder.AppendLine($"DESPAWN_ATTEMPTED: {snapshot.DespawnAttempted}");
        builder.AppendLine($"DESPAWN_PASSED: {snapshot.DespawnPassed}");
        builder.AppendLine($"CLEANUP_PASSED: {snapshot.CleanupPassed}");
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
        builder.AppendLine($"SAVE_ATTEMPTED: {snapshot.SaveAttempted}");
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(RuntimePropertyVerticalSliceSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
