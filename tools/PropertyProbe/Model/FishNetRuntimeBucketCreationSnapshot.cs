namespace OrganizedCrime.PropertyProbe.Model;

public sealed record FishNetRuntimeBucketCreationSnapshot(
    ushort CollectionId,
    int BeforeBucketCount,
    int AfterBucketCount,
    IReadOnlyList<string> BeforeBucketKeys,
    IReadOnlyList<string> AfterBucketKeys,
    bool BucketCreated,
    string BucketType,
    ushort ReturnedCollectionId,
    int BucketObjectCount,
    bool AuthoredCollectionUnchanged,
    int DocksObjectIdBefore,
    int DocksObjectIdAfter,
    ulong DocksSceneIdBefore,
    ulong DocksSceneIdAfter,
    string DocksStateBefore,
    string DocksStateAfter,
    bool DocksNetworkIdentityUnchanged,
    bool BucketCreationAttempted,
    bool AddObjectAttempted,
    bool SpawnAttempted,
    bool OwnershipAttempted,
    bool PersistenceAttempted,
    bool SceneMutationAttempted,
    string Gate,
    string? FailureReason)
{
    public static FishNetRuntimeBucketCreationSnapshot Test(
        ushort collectionId = 65000,
        bool bucketCreated = false,
        int beforeBucketCount = 0,
        int afterBucketCount = 0)
    {
        return new FishNetRuntimeBucketCreationSnapshot(
            CollectionId: collectionId,
            BeforeBucketCount: beforeBucketCount,
            AfterBucketCount: afterBucketCount,
            BeforeBucketKeys: Array.Empty<string>(),
            AfterBucketKeys: bucketCreated ? new[] { collectionId.ToString() } : Array.Empty<string>(),
            BucketCreated: bucketCreated,
            BucketType: bucketCreated ? "Il2CppFishNet.Managing.Object.SinglePrefabObjects" : "<unavailable>",
            ReturnedCollectionId: bucketCreated ? collectionId : (ushort)0,
            BucketObjectCount: 0,
            AuthoredCollectionUnchanged: true,
            DocksObjectIdBefore: 0,
            DocksObjectIdAfter: 0,
            DocksSceneIdBefore: 0,
            DocksSceneIdAfter: 0,
            DocksStateBefore: "<unavailable>",
            DocksStateAfter: "<unavailable>",
            DocksNetworkIdentityUnchanged: true,
            BucketCreationAttempted: bucketCreated,
            AddObjectAttempted: false,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            SceneMutationAttempted: false,
            Gate: bucketCreated ? "bucket-created" : "capability-partial",
            FailureReason: null);
    }
}
