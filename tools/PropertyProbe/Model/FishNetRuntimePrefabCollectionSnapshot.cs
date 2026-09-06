namespace OrganizedCrime.PropertyProbe.Model;

public sealed record FishNetRuntimePrefabCollectionSnapshot(
    bool RuntimeCollectionPresent,
    string RuntimeCollectionType,
    int BucketCount,
    IReadOnlyList<string> BucketKeys,
    IReadOnlyList<string> RelevantMembers,
    bool InvocationAttempted,
    bool MutationAttempted,
    bool SpawnAttempted,
    bool OwnershipAttempted,
    bool PersistenceAttempted,
    string Gate,
    string? FailureReason)
{
    public static FishNetRuntimePrefabCollectionSnapshot Test(
        bool runtimeCollectionPresent = false,
        int bucketCount = 0,
        IReadOnlyList<string>? bucketKeys = null,
        IReadOnlyList<string>? relevantMembers = null,
        string gate = "capability-partial")
    {
        return new FishNetRuntimePrefabCollectionSnapshot(
            RuntimeCollectionPresent: runtimeCollectionPresent,
            RuntimeCollectionType: runtimeCollectionPresent
                ? "System.Collections.Generic.IReadOnlyDictionary<System.UInt16, PrefabObjects>"
                : "<unavailable>",
            BucketCount: bucketCount,
            BucketKeys: bucketKeys ?? Array.Empty<string>(),
            RelevantMembers: relevantMembers ?? Array.Empty<string>(),
            InvocationAttempted: false,
            MutationAttempted: false,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            Gate: gate,
            FailureReason: null);
    }
}
