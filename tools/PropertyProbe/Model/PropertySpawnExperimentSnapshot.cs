namespace OrganizedCrime.PropertyProbe.Model;

public sealed record PropertySpawnExperimentSnapshot(
    string TargetName,
    string TargetPath,
    bool RegistrationPassed,
    bool ServerManagerPresent,
    bool ServerAvailable,
    bool SpawnAttempted,
    bool SpawnPassed,
    bool TargetNetworkObjectPresentBefore,
    bool TargetNetworkObjectPresentAfter,
    bool TargetPropertyNetworkObjectResolvedAfter,
    bool TargetPropertyNetworkInitializedAfter,
    bool TargetPropertyClientInitializedAfter,
    bool TargetPropertyServerInitializedAfter,
    bool TargetNetworkObjectSpawnedAfter,
    bool CleanupAttempted,
    bool CleanupPassed,
    bool DocksNetworkObjectPresent,
    bool DocksPropertyNetworkInitialized,
    bool DocksPropertyClientInitialized,
    bool DocksPropertyServerInitialized,
    bool DocksNetworkObjectSpawned,
    bool OwnershipAttempted,
    bool PersistenceAttempted,
    string Gate,
    string? FailureReason)
{
    public static PropertySpawnExperimentSnapshot Test(
        bool serverAvailable = false,
        bool spawnAttempted = false,
        bool spawnPassed = false,
        bool targetNetworkObjectSpawnedAfter = false,
        bool cleanupPassed = false)
    {
        return new PropertySpawnExperimentSnapshot(
            TargetName: "Fish Warehouse",
            TargetPath: "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse",
            RegistrationPassed: true,
            ServerManagerPresent: serverAvailable,
            ServerAvailable: serverAvailable,
            SpawnAttempted: spawnAttempted,
            SpawnPassed: spawnPassed,
            TargetNetworkObjectPresentBefore: false,
            TargetNetworkObjectPresentAfter: true,
            TargetPropertyNetworkObjectResolvedAfter: true,
            TargetPropertyNetworkInitializedAfter: true,
            TargetPropertyClientInitializedAfter: targetNetworkObjectSpawnedAfter,
            TargetPropertyServerInitializedAfter: targetNetworkObjectSpawnedAfter,
            TargetNetworkObjectSpawnedAfter: targetNetworkObjectSpawnedAfter,
            CleanupAttempted: cleanupPassed,
            CleanupPassed: cleanupPassed,
            DocksNetworkObjectPresent: true,
            DocksPropertyNetworkInitialized: true,
            DocksPropertyClientInitialized: true,
            DocksPropertyServerInitialized: true,
            DocksNetworkObjectSpawned: true,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            Gate: spawnPassed ? "spawn" : "spawn-failed",
            FailureReason: null);
    }
}
