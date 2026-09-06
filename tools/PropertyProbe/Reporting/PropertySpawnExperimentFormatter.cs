using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class PropertySpawnExperimentFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(PropertySpawnExperimentSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FISH WAREHOUSE FISHNET SPAWN EXPERIMENT");
        builder.AppendLine("Spawn only: no ownership, persistence, or existing-property mutation was attempted.");
        builder.AppendLine();
        builder.AppendLine($"TARGET_NAME: {snapshot.TargetName}");
        builder.AppendLine($"TARGET_PATH: {snapshot.TargetPath}");
        builder.AppendLine($"REGISTRATION_PASSED: {snapshot.RegistrationPassed}");
        builder.AppendLine($"SERVER_MANAGER_PRESENT: {snapshot.ServerManagerPresent}");
        builder.AppendLine($"SERVER_AVAILABLE: {snapshot.ServerAvailable}");
        builder.AppendLine($"SPAWN_ATTEMPTED: {snapshot.SpawnAttempted}");
        builder.AppendLine($"SPAWN_PASSED: {snapshot.SpawnPassed}");
        builder.AppendLine($"TARGET_NETWORK_OBJECT_PRESENT_BEFORE: {snapshot.TargetNetworkObjectPresentBefore}");
        builder.AppendLine($"TARGET_NETWORK_OBJECT_PRESENT_AFTER: {snapshot.TargetNetworkObjectPresentAfter}");
        builder.AppendLine($"TARGET_PROPERTY_NETWORK_OBJECT_RESOLVED_AFTER: {snapshot.TargetPropertyNetworkObjectResolvedAfter}");
        builder.AppendLine($"TARGET_PROPERTY_NETWORK_INITIALIZED_AFTER: {snapshot.TargetPropertyNetworkInitializedAfter}");
        builder.AppendLine($"TARGET_PROPERTY_CLIENT_INITIALIZED_AFTER: {snapshot.TargetPropertyClientInitializedAfter}");
        builder.AppendLine($"TARGET_PROPERTY_SERVER_INITIALIZED_AFTER: {snapshot.TargetPropertyServerInitializedAfter}");
        builder.AppendLine($"TARGET_NETWORK_OBJECT_SPAWNED_AFTER: {snapshot.TargetNetworkObjectSpawnedAfter}");
        builder.AppendLine($"CLEANUP_ATTEMPTED: {snapshot.CleanupAttempted}");
        builder.AppendLine($"CLEANUP_PASSED: {snapshot.CleanupPassed}");
        builder.AppendLine();
        builder.AppendLine($"DOCKS_NETWORK_OBJECT_PRESENT: {snapshot.DocksNetworkObjectPresent}");
        builder.AppendLine($"DOCKS_PROPERTY_NETWORK_INITIALIZED: {snapshot.DocksPropertyNetworkInitialized}");
        builder.AppendLine($"DOCKS_PROPERTY_CLIENT_INITIALIZED: {snapshot.DocksPropertyClientInitialized}");
        builder.AppendLine($"DOCKS_PROPERTY_SERVER_INITIALIZED: {snapshot.DocksPropertyServerInitialized}");
        builder.AppendLine($"DOCKS_NETWORK_OBJECT_SPAWNED: {snapshot.DocksNetworkObjectSpawned}");
        builder.AppendLine();
        builder.AppendLine($"OWNERSHIP_ATTEMPTED: {snapshot.OwnershipAttempted}");
        builder.AppendLine($"PERSISTENCE_ATTEMPTED: {snapshot.PersistenceAttempted}");
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(PropertySpawnExperimentSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
