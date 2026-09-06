namespace OrganizedCrime.PropertyProbe.Model;

public sealed record FishNetPrefabObjectsSnapshot(
    bool PrefabObjectsPresent,
    string PrefabObjectsType,
    IReadOnlyList<string> RelevantMembers,
    bool CountMemberFound,
    bool InvocationAttempted,
    bool MutationAttempted,
    bool SpawnAttempted,
    bool OwnershipAttempted,
    bool PersistenceAttempted,
    string Gate,
    string? FailureReason)
{
    public static FishNetPrefabObjectsSnapshot Test(
        bool prefabObjectsPresent = false,
        IReadOnlyList<string>? relevantMembers = null,
        bool countMemberFound = false,
        string gate = "capability-partial")
    {
        return new FishNetPrefabObjectsSnapshot(
            PrefabObjectsPresent: prefabObjectsPresent,
            PrefabObjectsType: prefabObjectsPresent
                ? "Il2CppFishNet.Managing.Object.PrefabObjects"
                : "<unavailable>",
            RelevantMembers: relevantMembers ?? Array.Empty<string>(),
            CountMemberFound: countMemberFound,
            InvocationAttempted: false,
            MutationAttempted: false,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            Gate: gate,
            FailureReason: null);
    }
}
