namespace OrganizedCrime.Model;

/// <summary>
/// Pure decision for whether the Fish Warehouse should be treated as owned, used to
/// break the ownership persistence doom-loop.
///
/// OC keeps its own sidecar <c>owned</c> flag beside the game-serialized property
/// <c>IsOwned</c>. A transient restore-failure can leave the sidecar recording
/// <c>owned=false</c> even though the property is still owned; once that lands on
/// disk it is self-reinforcing — every later load skips restore and re-saves false,
/// so the property reads permanently un-owned.
///
/// OC's Fish Warehouse ownership is <b>monotonic</b>: there is no in-game sell /
/// disown path. So any authoritative evidence that the property was ever owned means
/// it is still owned, and a bare sidecar <c>owned=false</c> must not be trusted over
/// that evidence. Evidence, strongest first: the game-serialized <c>IsOwned</c> (when
/// readable), the sidecar <c>owned</c> flag, and the presence of captured
/// objects/employees ("it was owned when we captured it"). A negative
/// <c>nativeIsOwned</c> never overrides positive evidence — monotonicity means we
/// only ever conclude owned, never downgrade — so this is a strict superset of the
/// old <c>if (owned)</c> gate and leaves a genuinely-owned save unaffected.
/// </summary>
public static class FishWarehouseOwnershipReconciler
{
    /// <param name="sidecarOwned">The sidecar's persisted owned flag (may be corrupt).</param>
    /// <param name="capturedObjectCount">Objects captured in the replay checkpoint.</param>
    /// <param name="capturedEmployeeCount">Employees captured in the replay checkpoint.</param>
    /// <param name="nativeIsOwned">
    /// The game-serialized property <c>IsOwned</c>, or <c>null</c> when it could not be
    /// read this load. Only a <c>true</c> is used (as the strongest positive signal); a
    /// <c>false</c> never overrides other evidence because ownership is monotonic.
    /// </param>
    public static bool IsOwnedByEvidence(
        bool sidecarOwned,
        int capturedObjectCount,
        int capturedEmployeeCount,
        bool? nativeIsOwned)
    {
        if (nativeIsOwned == true)
            return true;

        if (sidecarOwned)
            return true;

        return capturedObjectCount > 0 || capturedEmployeeCount > 0;
    }
}
