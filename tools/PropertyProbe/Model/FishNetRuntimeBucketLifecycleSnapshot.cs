namespace OrganizedCrime.PropertyProbe.Model;

public sealed record FishNetRuntimeBucketLifecycleSnapshot(
    ushort CollectionId,
    bool CreateReturnedBucket,
    bool RetrieveReturnedSameBucket,
    bool RemovalReturnedTrue,
    bool PostRemovalReturnedBucket,
    string BucketType,
    int CreatedBucketInstanceId,
    int RetrievedBucketInstanceId,
    bool AuthoredCollectionUnchanged,
    bool DocksNetworkIdentityUnchanged,
    bool BucketCreationAttempted,
    bool RemovalAttempted,
    bool AddObjectAttempted,
    bool SpawnAttempted,
    bool OwnershipAttempted,
    bool PersistenceAttempted,
    bool SceneMutationAttempted,
    string Gate,
    string? FailureReason)
{
    public static FishNetRuntimeBucketLifecycleSnapshot Test(
        bool createReturnedBucket = false,
        bool retrieveReturnedSameBucket = false,
        bool removalReturnedTrue = false,
        bool postRemovalReturnedBucket = false)
    {
        return new FishNetRuntimeBucketLifecycleSnapshot(
            CollectionId: 65000,
            CreateReturnedBucket: createReturnedBucket,
            RetrieveReturnedSameBucket: retrieveReturnedSameBucket,
            RemovalReturnedTrue: removalReturnedTrue,
            PostRemovalReturnedBucket: postRemovalReturnedBucket,
            BucketType: createReturnedBucket ? "Il2CppFishNet.Managing.Object.SinglePrefabObjects" : "<unavailable>",
            CreatedBucketInstanceId: createReturnedBucket ? 1 : 0,
            RetrievedBucketInstanceId: retrieveReturnedSameBucket ? 1 : 0,
            AuthoredCollectionUnchanged: true,
            DocksNetworkIdentityUnchanged: true,
            BucketCreationAttempted: createReturnedBucket,
            RemovalAttempted: removalReturnedTrue,
            AddObjectAttempted: false,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            SceneMutationAttempted: false,
            Gate: removalReturnedTrue && !postRemovalReturnedBucket ? "lifecycle-passed" : "lifecycle-partial",
            FailureReason: null);
    }
}
