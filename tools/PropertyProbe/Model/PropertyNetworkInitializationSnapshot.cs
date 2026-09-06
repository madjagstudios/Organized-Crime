namespace OrganizedCrime.PropertyProbe.Model;

public sealed record PropertyNetworkInitializationSnapshot(
    string TargetName,
    string TargetPath,
    bool RegistrationPassed,
    bool TargetNetworkObjectPresentBefore,
    bool TargetNetworkObjectPresentAfter,
    bool TargetPropertyNetworkObjectResolvedAfter,
    bool TargetPropertyNetworkInitializedAfter,
    bool TargetPropertyClientInitializedAfter,
    bool TargetPropertyServerInitializedAfter,
    bool TargetNetworkObjectSpawnedAfter,
    bool DocksNetworkObjectPresent,
    bool DocksPropertyNetworkInitialized,
    bool DocksPropertyClientInitialized,
    bool DocksPropertyServerInitialized,
    bool DocksNetworkObjectSpawned,
    string Gate,
    string? FailureReason)
{
    public static PropertyNetworkInitializationSnapshot Test(
        bool targetNetworkObjectPresentAfter = false,
        bool targetPropertyNetworkObjectResolvedAfter = false,
        bool targetPropertyNetworkInitializedAfter = false,
        bool targetPropertyClientInitializedAfter = false,
        bool targetPropertyServerInitializedAfter = false,
        bool targetNetworkObjectSpawnedAfter = false)
    {
        return new PropertyNetworkInitializationSnapshot(
            TargetName: "Fish Warehouse",
            TargetPath: "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse",
            RegistrationPassed: true,
            TargetNetworkObjectPresentBefore: false,
            TargetNetworkObjectPresentAfter: targetNetworkObjectPresentAfter,
            TargetPropertyNetworkObjectResolvedAfter: targetPropertyNetworkObjectResolvedAfter,
            TargetPropertyNetworkInitializedAfter: targetPropertyNetworkInitializedAfter,
            TargetPropertyClientInitializedAfter: targetPropertyClientInitializedAfter,
            TargetPropertyServerInitializedAfter: targetPropertyServerInitializedAfter,
            TargetNetworkObjectSpawnedAfter: targetNetworkObjectSpawnedAfter,
            DocksNetworkObjectPresent: true,
            DocksPropertyNetworkInitialized: true,
            DocksPropertyClientInitialized: false,
            DocksPropertyServerInitialized: false,
            DocksNetworkObjectSpawned: true,
            Gate: "network-initialization-complete",
            FailureReason: null);
    }
}
