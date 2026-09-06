namespace OrganizedCrime.PropertyProbe.Model;

public sealed record FishNetCapabilitySnapshot(
    bool NetworkManagerPresent,
    bool ServerManagerPresent,
    bool ServerAvailable,
    string NetworkManagerType,
    string ServerManagerType,
    string SpawnablePrefabsType,
    bool SpawnablePrefabMemberFound,
    IReadOnlyList<string> RelevantMembers,
    bool S1ApiLoaded,
    bool S1MApiLoaded,
    bool SceneIdentityReused,
    NetworkObjectMetadata DocksMetadata,
    bool MutationAttempted,
    bool SpawnAttempted,
    bool OwnershipAttempted,
    bool PersistenceAttempted,
    string Gate,
    string? FailureReason)
{
    public static FishNetCapabilitySnapshot Test(
        bool serverManagerPresent = false,
        bool spawnablePrefabMemberFound = false,
        bool s1MApiLoaded = false)
    {
        return new FishNetCapabilitySnapshot(
            NetworkManagerPresent: true,
            ServerManagerPresent: serverManagerPresent,
            ServerAvailable: serverManagerPresent,
            NetworkManagerType: "FishNet.NetworkManager",
            ServerManagerType: serverManagerPresent ? "FishNet.ServerManager" : "<unavailable>",
            SpawnablePrefabsType: spawnablePrefabMemberFound ? "FishNet.SpawnablePrefabs" : "<unavailable>",
            SpawnablePrefabMemberFound: spawnablePrefabMemberFound,
            RelevantMembers: spawnablePrefabMemberFound ? new[] { "SpawnablePrefabs", "AddPrefab" } : Array.Empty<string>(),
            S1ApiLoaded: true,
            S1MApiLoaded: s1MApiLoaded,
            SceneIdentityReused: false,
            DocksMetadata: NetworkObjectMetadata.Empty with { Present = true, SceneObject = true, State = "Spawned" },
            MutationAttempted: false,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            Gate: "read-only",
            FailureReason: null);
    }
}
