namespace OrganizedCrime.Model;

/// <summary>
/// Pure decision for which of the Property's live employee-home lockers to adopt as
/// the single representative on replay.
///
/// OC restores each employee's own home independently via that employee's captured
/// bed GUID, so the property-level adoption only needs to pick ONE representative
/// locker — for the optional navigation probe and to suppress the authored
/// no-locker fallback. It must therefore never treat "more than one locker" as a
/// failure: a player who hires several handlers legitimately owns several homes, and
/// failing there would strand the whole replay. The planner prefers a locker whose
/// GUID matches a saved employee-home GUID (the primary employee's home) and
/// otherwise adopts the first candidate; it reports "none" only when there are no
/// candidates at all.
/// </summary>
public static class FishWarehouseEmployeeHomeAdoptionPlanner
{
    /// <summary>
    /// Returns the index into <paramref name="candidateHomeGuids"/> of the locker to
    /// adopt, or <c>null</c> when there are no candidates. A candidate whose GUID
    /// could not be read is represented by a null/blank entry; it may still be
    /// adopted as a last-resort representative but is never preferred over a
    /// candidate whose GUID matches a saved home GUID or over any readable GUID.
    /// The choice is deterministic (first match wins) and never signals ambiguity.
    /// </summary>
    public static int? SelectHomeIndex(
        IReadOnlyList<string?> candidateHomeGuids,
        IReadOnlyCollection<string>? savedHomeGuids)
    {
        if (candidateHomeGuids is null || candidateHomeGuids.Count == 0)
            return null;

        var savedSet = BuildSavedSet(savedHomeGuids);
        if (savedSet.Count > 0)
        {
            for (int i = 0; i < candidateHomeGuids.Count; i++)
            {
                var guid = candidateHomeGuids[i];
                if (!string.IsNullOrWhiteSpace(guid) && savedSet.Contains(guid))
                    return i;
            }
        }

        // No saved-GUID match (or no saved GUIDs known): still adopt exactly one
        // representative so the authored no-locker fallback stays suppressed. Prefer
        // the first candidate with a readable GUID; failing that, the first candidate.
        for (int i = 0; i < candidateHomeGuids.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(candidateHomeGuids[i]))
                return i;
        }

        return 0;
    }

    private static HashSet<string> BuildSavedSet(IReadOnlyCollection<string>? savedHomeGuids)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (savedHomeGuids is null)
            return set;

        foreach (var guid in savedHomeGuids)
        {
            if (!string.IsNullOrWhiteSpace(guid))
                set.Add(guid);
        }

        return set;
    }
}
