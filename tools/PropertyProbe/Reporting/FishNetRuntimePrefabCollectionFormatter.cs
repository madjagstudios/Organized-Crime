using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class FishNetRuntimePrefabCollectionFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(FishNetRuntimePrefabCollectionSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FISHNET RUNTIME PREFAB COLLECTION INSPECTION");
        builder.AppendLine("Read-only inspection: no runtime prefab member was invoked and no registration, object creation, spawn, ownership, persistence, or scene mutation was attempted.");
        builder.AppendLine();
        builder.AppendLine($"RUNTIME_COLLECTION_PRESENT: {snapshot.RuntimeCollectionPresent}");
        builder.AppendLine($"RUNTIME_COLLECTION_TYPE: {snapshot.RuntimeCollectionType}");
        builder.AppendLine($"BUCKET_COUNT: {snapshot.BucketCount}");
        builder.AppendLine($"INVOCATION_ATTEMPTED: {snapshot.InvocationAttempted}");
        builder.AppendLine($"MUTATION_ATTEMPTED: {snapshot.MutationAttempted}");
        builder.AppendLine($"SPAWN_ATTEMPTED: {snapshot.SpawnAttempted}");
        builder.AppendLine($"OWNERSHIP_ATTEMPTED: {snapshot.OwnershipAttempted}");
        builder.AppendLine($"PERSISTENCE_ATTEMPTED: {snapshot.PersistenceAttempted}");
        builder.AppendLine();
        builder.AppendLine("BUCKET_KEYS:");
        foreach (var key in snapshot.BucketKeys.OrderBy(key => key, StringComparer.Ordinal))
            builder.AppendLine($"- {key}");
        builder.AppendLine();
        builder.AppendLine("RELEVANT_MEMBERS:");
        foreach (var member in snapshot.RelevantMembers.OrderBy(member => member, StringComparer.Ordinal))
            builder.AppendLine($"- {member}");
        builder.AppendLine();
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(FishNetRuntimePrefabCollectionSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
