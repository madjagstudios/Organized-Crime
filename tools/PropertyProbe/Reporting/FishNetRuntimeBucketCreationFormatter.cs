using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class FishNetRuntimeBucketCreationFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(FishNetRuntimeBucketCreationSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FISHNET RUNTIME PREFAB BUCKET CREATION EXPERIMENT");
        builder.AppendLine("Guarded runtime mutation: only GetPrefabObjects<T>(collectionId, true) may create the empty bucket; no NetworkObject is added and no spawn, ownership, persistence, or scene mutation is attempted.");
        builder.AppendLine();
        builder.AppendLine($"COLLECTION_ID: {snapshot.CollectionId}");
        builder.AppendLine($"BEFORE_BUCKET_COUNT: {snapshot.BeforeBucketCount}");
        builder.AppendLine($"AFTER_BUCKET_COUNT: {snapshot.AfterBucketCount}");
        builder.AppendLine($"BUCKET_CREATED: {snapshot.BucketCreated}");
        builder.AppendLine($"BUCKET_TYPE: {snapshot.BucketType}");
        builder.AppendLine($"RETURNED_COLLECTION_ID: {snapshot.ReturnedCollectionId}");
        builder.AppendLine($"BUCKET_OBJECT_COUNT: {snapshot.BucketObjectCount}");
        builder.AppendLine($"AUTHORED_COLLECTION_UNCHANGED: {snapshot.AuthoredCollectionUnchanged}");
        builder.AppendLine($"DOCKS_OBJECT_ID_BEFORE: {snapshot.DocksObjectIdBefore}");
        builder.AppendLine($"DOCKS_OBJECT_ID_AFTER: {snapshot.DocksObjectIdAfter}");
        builder.AppendLine($"DOCKS_SCENE_ID_BEFORE: {snapshot.DocksSceneIdBefore}");
        builder.AppendLine($"DOCKS_SCENE_ID_AFTER: {snapshot.DocksSceneIdAfter}");
        builder.AppendLine($"DOCKS_STATE_BEFORE: {snapshot.DocksStateBefore}");
        builder.AppendLine($"DOCKS_STATE_AFTER: {snapshot.DocksStateAfter}");
        builder.AppendLine($"DOCKS_NETWORK_IDENTITY_UNCHANGED: {snapshot.DocksNetworkIdentityUnchanged}");
        builder.AppendLine($"BUCKET_CREATION_ATTEMPTED: {snapshot.BucketCreationAttempted}");
        builder.AppendLine($"ADD_OBJECT_ATTEMPTED: {snapshot.AddObjectAttempted}");
        builder.AppendLine($"SPAWN_ATTEMPTED: {snapshot.SpawnAttempted}");
        builder.AppendLine($"OWNERSHIP_ATTEMPTED: {snapshot.OwnershipAttempted}");
        builder.AppendLine($"PERSISTENCE_ATTEMPTED: {snapshot.PersistenceAttempted}");
        builder.AppendLine($"SCENE_MUTATION_ATTEMPTED: {snapshot.SceneMutationAttempted}");
        builder.AppendLine();
        builder.AppendLine("BEFORE_BUCKET_KEYS:");
        foreach (var key in snapshot.BeforeBucketKeys.OrderBy(key => key, StringComparer.Ordinal))
            builder.AppendLine($"- {key}");
        builder.AppendLine();
        builder.AppendLine("AFTER_BUCKET_KEYS:");
        foreach (var key in snapshot.AfterBucketKeys.OrderBy(key => key, StringComparer.Ordinal))
            builder.AppendLine($"- {key}");
        builder.AppendLine();
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(FishNetRuntimeBucketCreationSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
