using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class FishNetRuntimeBucketLifecycleFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(FishNetRuntimeBucketLifecycleSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FISHNET RUNTIME PREFAB BUCKET LIFECYCLE");
        builder.AppendLine("Guarded runtime lifecycle: create, retrieve, remove, and post-removal verification only; no NetworkObject registration or spawn was attempted.");
        builder.AppendLine();
        builder.AppendLine($"COLLECTION_ID: {snapshot.CollectionId}");
        builder.AppendLine($"CREATE_RETURNED_BUCKET: {snapshot.CreateReturnedBucket}");
        builder.AppendLine($"RETRIEVE_RETURNED_SAME_BUCKET: {snapshot.RetrieveReturnedSameBucket}");
        builder.AppendLine($"REMOVAL_RETURNED_TRUE: {snapshot.RemovalReturnedTrue}");
        builder.AppendLine($"POST_REMOVAL_RETURNED_BUCKET: {snapshot.PostRemovalReturnedBucket}");
        builder.AppendLine($"BUCKET_TYPE: {snapshot.BucketType}");
        builder.AppendLine($"CREATED_BUCKET_INSTANCE_ID: {snapshot.CreatedBucketInstanceId}");
        builder.AppendLine($"RETRIEVED_BUCKET_INSTANCE_ID: {snapshot.RetrievedBucketInstanceId}");
        builder.AppendLine($"AUTHORED_COLLECTION_UNCHANGED: {snapshot.AuthoredCollectionUnchanged}");
        builder.AppendLine($"DOCKS_NETWORK_IDENTITY_UNCHANGED: {snapshot.DocksNetworkIdentityUnchanged}");
        builder.AppendLine($"BUCKET_CREATION_ATTEMPTED: {snapshot.BucketCreationAttempted}");
        builder.AppendLine($"REMOVAL_ATTEMPTED: {snapshot.RemovalAttempted}");
        builder.AppendLine($"ADD_OBJECT_ATTEMPTED: {snapshot.AddObjectAttempted}");
        builder.AppendLine($"SPAWN_ATTEMPTED: {snapshot.SpawnAttempted}");
        builder.AppendLine($"OWNERSHIP_ATTEMPTED: {snapshot.OwnershipAttempted}");
        builder.AppendLine($"PERSISTENCE_ATTEMPTED: {snapshot.PersistenceAttempted}");
        builder.AppendLine($"SCENE_MUTATION_ATTEMPTED: {snapshot.SceneMutationAttempted}");
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(FishNetRuntimeBucketLifecycleSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
