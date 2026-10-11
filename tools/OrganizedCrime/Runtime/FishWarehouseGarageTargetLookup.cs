namespace OrganizedCrime.Runtime;

// Engine-free seam for the garage adapter's identity lookup and live writes.
internal static class FishWarehouseGarageTargetLookup
{
    internal static IReadOnlyDictionary<string, T> Create<T>(
        IEnumerable<T> targets,
        Func<T, bool> isAlive,
        Func<T, string> getIdentity) =>
        targets.Where(isAlive).ToDictionary(getIdentity, StringComparer.Ordinal);

    internal static bool TrySetEnabled<T>(
        IReadOnlyDictionary<string, T> targets,
        string identity,
        bool enabled,
        Func<T, bool> isAlive,
        Action<T, bool> setEnabled,
        bool restoring = false)
    {
        if (!targets.TryGetValue(identity, out T? target) || !isAlive(target))
            return restoring;

        setEnabled(target, enabled);
        return true;
    }
}
