namespace OrganizedCrime.PropertyProbe.Model;

public sealed record FishNetRuntimePrefabRegistrationSnapshot(
    ushort CollectionId,
    string TemporaryObjectPath,
    int BeforeBucketObjectCount,
    int AfterBucketObjectCount,
    bool AddObjectAttempted,
    bool RegistrationPassed,
    string AddObjectResult,
    bool NetworkObjectPresent,
    bool Networked,
    bool SceneObject,
    bool Spawned,
    string NetworkState,
    int PrefabId,
    ushort SpawnableCollectionId,
    bool CleanupAttempted,
    bool CleanupPassed,
    bool TemporaryObjectDestroyed,
    bool AuthoredCollectionUnchanged,
    bool DocksNetworkIdentityUnchanged,
    bool SpawnAttempted,
    bool OwnershipAttempted,
    bool PersistenceAttempted,
    bool PropertyMutationAttempted,
    string Gate,
    string? FailureReason)
{
    public static FishNetRuntimePrefabRegistrationSnapshot Test(
        bool addObjectAttempted = false,
        bool registrationPassed = false,
        bool cleanupPassed = false)
    {
        return new FishNetRuntimePrefabRegistrationSnapshot(
            CollectionId: 65000,
            TemporaryObjectPath: "OC_FishWarehouse_RuntimePrefabProbe",
            BeforeBucketObjectCount: 0,
            AfterBucketObjectCount: registrationPassed ? 1 : 0,
            AddObjectAttempted: addObjectAttempted,
            RegistrationPassed: registrationPassed,
            AddObjectResult: registrationPassed ? "success" : "not-attempted",
            NetworkObjectPresent: registrationPassed,
            Networked: registrationPassed,
            SceneObject: false,
            Spawned: false,
            NetworkState: "Unset",
            PrefabId: registrationPassed ? 1 : 0,
            SpawnableCollectionId: registrationPassed ? (ushort)65000 : (ushort)0,
            CleanupAttempted: cleanupPassed,
            CleanupPassed: cleanupPassed,
            TemporaryObjectDestroyed: cleanupPassed,
            AuthoredCollectionUnchanged: true,
            DocksNetworkIdentityUnchanged: true,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            PropertyMutationAttempted: false,
            Gate: registrationPassed && cleanupPassed ? "registration-passed" : "registration-partial",
            FailureReason: null);
    }
}
