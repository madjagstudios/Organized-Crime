namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-130: pure placement selection for Arthur's <c>come</c> and
/// <c>leave</c>. The runtime evaluates ring candidates around the
/// player (navmesh snap plus a reachability check each); this picks
/// one from the evaluated rows. Preferring a point behind the player
/// keeps the arrival out of view; the distance bands are the ticket's
/// own 20-30 m (come) and about-30 m (leave).
/// </summary>
public static class Chapter3ArthurPlacement
{
    public const float ComeMinMetres = 20f;
    public const float ComeMaxMetres = 30f;
    public const float LeaveMetres = 30f;

    /// <summary>
    /// Picks the come point: reachable, in the 20-30 m band when one
    /// exists, behind the player first, then the closest to the band
    /// middle. Null when no candidate is reachable.
    /// </summary>
    public static Chapter3ArthurCandidate? SelectComeCandidate(IEnumerable<Chapter3ArthurCandidate> candidates)
    {
        if (candidates is null) return null;
        var reachable = candidates.Where(candidate => candidate is not null && candidate.Reachable).ToArray();
        if (reachable.Length == 0) return null;

        const float bandMiddle = (ComeMinMetres + ComeMaxMetres) / 2f;
        return reachable
            .OrderBy(candidate => InBand(candidate.DistanceToPlayer) ? 0 : 1)
            .ThenBy(candidate => candidate.BehindPlayer ? 0 : 1)
            .ThenBy(candidate => Math.Abs(candidate.DistanceToPlayer - bandMiddle))
            .First();
    }

    /// <summary>
    /// Picks the leave point: reachable, the closest to 30 m, behind
    /// the player first so he walks out of view. Null when no
    /// candidate is reachable.
    /// </summary>
    public static Chapter3ArthurCandidate? SelectLeaveCandidate(IEnumerable<Chapter3ArthurCandidate> candidates)
    {
        if (candidates is null) return null;
        var reachable = candidates.Where(candidate => candidate is not null && candidate.Reachable).ToArray();
        if (reachable.Length == 0) return null;

        return reachable
            .OrderBy(candidate => candidate.BehindPlayer ? 0 : 1)
            .ThenBy(candidate => Math.Abs(candidate.DistanceToPlayer - LeaveMetres))
            .First();
    }

    private static bool InBand(float distance) => distance >= ComeMinMetres && distance <= ComeMaxMetres;
}
