namespace OrganizedCrime.PropertyProbe.Model;

public sealed record NetworkRootChainEntry(
    string Name,
    string Path,
    bool LocalNetworkObjectPresent,
    bool ParentNetworkObjectResolved,
    NetworkObjectMetadata Metadata);

public sealed record PropertyNetworkRootInspectionSnapshot(
    string TargetName,
    string TargetPath,
    bool RegistrationPassed,
    bool MetadataCapturePassed,
    bool SpawnAttempted,
    bool OwnershipAttempted,
    bool PersistenceAttempted,
    bool CleanupAttempted,
    bool CleanupPassed,
    IReadOnlyList<NetworkRootChainEntry> Chain,
    string Gate,
    string? FailureReason)
{
    public static PropertyNetworkRootInspectionSnapshot Test(
        bool capturePassed = false,
        bool localNetworkObjectPresent = false,
        bool sceneObject = false)
    {
        var metadata = NetworkObjectMetadata.Empty with
        {
            Present = localNetworkObjectPresent,
            Networked = localNetworkObjectPresent,
            SceneObject = sceneObject,
            State = sceneObject ? "Spawned" : "Unset",
            NetworkBehaviourCount = localNetworkObjectPresent ? 1 : 0
        };

        return new PropertyNetworkRootInspectionSnapshot(
            TargetName: "Fish Warehouse",
            TargetPath: "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse",
            RegistrationPassed: true,
            MetadataCapturePassed: capturePassed,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            CleanupAttempted: true,
            CleanupPassed: true,
            Chain: new[]
            {
                new NetworkRootChainEntry(
                    Name: "Fish Warehouse",
                    Path: "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse",
                    LocalNetworkObjectPresent: localNetworkObjectPresent,
                    ParentNetworkObjectResolved: localNetworkObjectPresent,
                    Metadata: metadata)
            },
            Gate: capturePassed ? "metadata" : "metadata-failed",
            FailureReason: null);
    }
}
