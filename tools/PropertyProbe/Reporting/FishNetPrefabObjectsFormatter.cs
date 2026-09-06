using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class FishNetPrefabObjectsFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(FishNetPrefabObjectsSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FISHNET PREFAB OBJECTS INSPECTION");
        builder.AppendLine("Read-only reflection: no PrefabObjects member was invoked and no registration, component creation, spawn, ownership, persistence, or scene mutation was attempted.");
        builder.AppendLine();
        builder.AppendLine($"PREFAB_OBJECTS_PRESENT: {snapshot.PrefabObjectsPresent}");
        builder.AppendLine($"PREFAB_OBJECTS_TYPE: {snapshot.PrefabObjectsType}");
        builder.AppendLine($"COUNT_MEMBER_FOUND: {snapshot.CountMemberFound}");
        builder.AppendLine($"INVOCATION_ATTEMPTED: {snapshot.InvocationAttempted}");
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

    public static string FormatJson(FishNetPrefabObjectsSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
