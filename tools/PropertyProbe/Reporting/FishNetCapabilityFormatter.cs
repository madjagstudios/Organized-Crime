using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class FishNetCapabilityFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(FishNetCapabilitySnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FISHNET PREFAB REGISTRATION CAPABILITY PREFLIGHT");
        builder.AppendLine("Read-only reflection and manager inspection: no prefab registration, component creation, spawn, ownership, persistence, or scene mutation was attempted.");
        builder.AppendLine();
        builder.AppendLine($"NETWORK_MANAGER_PRESENT: {snapshot.NetworkManagerPresent}");
        builder.AppendLine($"SERVER_MANAGER_PRESENT: {snapshot.ServerManagerPresent}");
        builder.AppendLine($"SERVER_AVAILABLE: {snapshot.ServerAvailable}");
        builder.AppendLine($"NETWORK_MANAGER_TYPE: {snapshot.NetworkManagerType}");
        builder.AppendLine($"SERVER_MANAGER_TYPE: {snapshot.ServerManagerType}");
        builder.AppendLine($"SPAWNABLE_PREFABS_TYPE: {snapshot.SpawnablePrefabsType}");
        builder.AppendLine($"SPAWNABLE_PREFAB_MEMBER_FOUND: {snapshot.SpawnablePrefabMemberFound}");
        builder.AppendLine($"S1API_LOADED: {snapshot.S1ApiLoaded}");
        builder.AppendLine($"S1MAPI_LOADED: {snapshot.S1MApiLoaded}");
        builder.AppendLine($"SCENE_IDENTITY_REUSED: {snapshot.SceneIdentityReused}");
        builder.AppendLine($"DOCKS_IS_SCENE_OBJECT: {snapshot.DocksMetadata.SceneObject}");
        builder.AppendLine($"DOCKS_STATE: {snapshot.DocksMetadata.State}");
        builder.AppendLine($"DOCKS_OBJECT_ID: {snapshot.DocksMetadata.ObjectId}");
        builder.AppendLine($"DOCKS_SCENE_ID: {snapshot.DocksMetadata.SceneId}");
        builder.AppendLine($"MUTATION_ATTEMPTED: {snapshot.MutationAttempted}");
        builder.AppendLine($"SPAWN_ATTEMPTED: {snapshot.SpawnAttempted}");
        builder.AppendLine($"OWNERSHIP_ATTEMPTED: {snapshot.OwnershipAttempted}");
        builder.AppendLine($"PERSISTENCE_ATTEMPTED: {snapshot.PersistenceAttempted}");
        builder.AppendLine();
        builder.AppendLine("RELEVANT_MEMBERS:");
        foreach (var member in snapshot.RelevantMembers.OrderBy(member => member, StringComparer.Ordinal))
            builder.AppendLine($"- {member}");
        builder.AppendLine();
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(FishNetCapabilitySnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
