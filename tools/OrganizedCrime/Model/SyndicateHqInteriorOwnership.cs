namespace OrganizedCrime.Model;

public sealed record SyndicateHqInteriorOwnershipSnapshot(
    string? OwnedRootToken,
    IReadOnlyList<string> SceneRootTokens,
    IReadOnlySet<string> MarkerNames,
    IReadOnlySet<string> ColliderGeometryNames,
    bool DestroyPending);

public static class SyndicateHqInteriorOwnership
{
    public static bool IsRetainedNativeRoot(int retainedInstanceId, IReadOnlyList<int> sceneRootInstanceIds, out string reason)
    {
        if (sceneRootInstanceIds.Count != 1)
        {
            reason = $"the scene contained {sceneRootInstanceIds.Count} HQ roots";
            return false;
        }
        if (sceneRootInstanceIds[0] != retainedInstanceId)
        {
            reason = "the scene root native instance identity did not match the retained owned root";
            return false;
        }
        reason = "the scene root native instance identity matched the retained owned root";
        return true;
    }

    public static bool CanReuse(SyndicateHqInteriorOwnershipSnapshot snapshot, out string reason)
    {
        if (snapshot.DestroyPending) { reason = "Owned HQ root is pending deferred destruction."; return false; }
        if (string.IsNullOrWhiteSpace(snapshot.OwnedRootToken)) { reason = "No exact runtime-created HQ root reference was retained."; return false; }
        if (snapshot.SceneRootTokens.Count != 1 || !string.Equals(snapshot.SceneRootTokens[0], snapshot.OwnedRootToken, StringComparison.Ordinal)) { reason = "A foreign or duplicate same-name HQ root was present."; return false; }
        if (!SyndicateHqInteriorDefinition.Markers.All(marker => snapshot.MarkerNames.Contains(marker.Name))) { reason = "Owned HQ root markers were incomplete."; return false; }
        if (!SyndicateHqInteriorDefinition.RequiredColliderNames.All(snapshot.ColliderGeometryNames.Contains)) { reason = "Owned HQ geometry/collider shell was incomplete."; return false; }
        reason = "Exact runtime-created HQ root is reusable.";
        return true;
    }
}
