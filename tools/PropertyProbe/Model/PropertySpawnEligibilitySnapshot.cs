namespace OrganizedCrime.PropertyProbe.Model;

public sealed record NetworkObjectMetadata(
    bool Present,
    bool Networked,
    bool SceneObject,
    bool Nested,
    bool Deinitializing,
    string State,
    int ObjectId,
    ushort PrefabId,
    ulong SceneId,
    ushort SpawnableCollectionId,
    bool NetworkManagerPresent,
    bool ServerManagerPresent,
    int NetworkBehaviourCount,
    string SerializedJson)
{
    public static NetworkObjectMetadata Empty => new(
        Present: false,
        Networked: false,
        SceneObject: false,
        Nested: false,
        Deinitializing: false,
        State: "Unavailable",
        ObjectId: 0,
        PrefabId: 0,
        SceneId: 0,
        SpawnableCollectionId: 0,
        NetworkManagerPresent: false,
        ServerManagerPresent: false,
        NetworkBehaviourCount: 0,
        SerializedJson: "<unavailable>");
}

public sealed record PropertySpawnEligibilitySnapshot(
    string TargetName,
    string TargetPath,
    bool RegistrationPassed,
    bool MetadataCapturePassed,
    bool SpawnAttempted,
    bool CleanupAttempted,
    bool CleanupPassed,
    NetworkObjectMetadata TargetMetadata,
    NetworkObjectMetadata DocksMetadata,
    string Gate,
    string? FailureReason)
{
    public static PropertySpawnEligibilitySnapshot Test(
        bool capturePassed = false,
        bool targetNetworked = false,
        bool spawnAttempted = false,
        bool cleanupPassed = false)
    {
        var target = NetworkObjectMetadata.Empty with
        {
            Present = true,
            Networked = targetNetworked,
            NetworkBehaviourCount = 1,
            SerializedJson = "{\"test\":true}"
        };

        return new PropertySpawnEligibilitySnapshot(
            TargetName: "Fish Warehouse",
            TargetPath: "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse",
            RegistrationPassed: true,
            MetadataCapturePassed: capturePassed,
            SpawnAttempted: spawnAttempted,
            CleanupAttempted: cleanupPassed,
            CleanupPassed: cleanupPassed,
            TargetMetadata: target,
            DocksMetadata: target with { Networked = true },
            Gate: capturePassed ? "metadata" : "metadata-failed",
            FailureReason: null);
    }
}
