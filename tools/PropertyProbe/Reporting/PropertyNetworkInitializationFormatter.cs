using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class PropertyNetworkInitializationFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(PropertyNetworkInitializationSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FISHNET PROPERTY NETWORK INITIALIZATION REPORT");
        builder.AppendLine("Initialization only: no ownership, spawn, persistence, or existing-property mutation was attempted.");
        builder.AppendLine();
        builder.AppendLine($"TARGET_NAME: {snapshot.TargetName}");
        builder.AppendLine($"TARGET_PATH: {snapshot.TargetPath}");
        builder.AppendLine($"REGISTRATION_PASSED: {snapshot.RegistrationPassed}");
        builder.AppendLine($"TARGET_NETWORK_OBJECT_PRESENT_BEFORE: {snapshot.TargetNetworkObjectPresentBefore}");
        builder.AppendLine($"TARGET_NETWORK_OBJECT_PRESENT_AFTER: {snapshot.TargetNetworkObjectPresentAfter}");
        builder.AppendLine($"TARGET_PROPERTY_NETWORK_OBJECT_RESOLVED_AFTER: {snapshot.TargetPropertyNetworkObjectResolvedAfter}");
        builder.AppendLine($"TARGET_PROPERTY_NETWORK_INITIALIZED_AFTER: {snapshot.TargetPropertyNetworkInitializedAfter}");
        builder.AppendLine($"TARGET_PROPERTY_CLIENT_INITIALIZED_AFTER: {snapshot.TargetPropertyClientInitializedAfter}");
        builder.AppendLine($"TARGET_PROPERTY_SERVER_INITIALIZED_AFTER: {snapshot.TargetPropertyServerInitializedAfter}");
        builder.AppendLine($"TARGET_NETWORK_OBJECT_SPAWNED_AFTER: {snapshot.TargetNetworkObjectSpawnedAfter}");
        builder.AppendLine();
        builder.AppendLine($"DOCKS_NETWORK_OBJECT_PRESENT: {snapshot.DocksNetworkObjectPresent}");
        builder.AppendLine($"DOCKS_PROPERTY_NETWORK_INITIALIZED: {snapshot.DocksPropertyNetworkInitialized}");
        builder.AppendLine($"DOCKS_PROPERTY_CLIENT_INITIALIZED: {snapshot.DocksPropertyClientInitialized}");
        builder.AppendLine($"DOCKS_PROPERTY_SERVER_INITIALIZED: {snapshot.DocksPropertyServerInitialized}");
        builder.AppendLine($"DOCKS_NETWORK_OBJECT_SPAWNED: {snapshot.DocksNetworkObjectSpawned}");
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(PropertyNetworkInitializationSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
