namespace OrganizedCrime.Runtime.Police;

// Internal capability: preserve the pinned public officer-world contract.
internal interface IOcOfficerPoolWorld
{
    void PollOfficerPools();
    void ResetOfficerPoolWarnings();
}

internal interface IOcOfficerPoolRecoveryWorld : IOcOfficerPoolWorld
{
    bool IsOwnedOfficer(long pointer);
    void SetPoolRelease(Action<long> release);
}

// One warning budget per load, shared by all stations and independent of patrols.
internal sealed class OcOfficerPoolSweep
{
    private readonly Action<string> _warn;
    private bool _warned;
    internal OcOfficerPoolSweep(Action<string> warn) => _warn = warn;
    internal void ResetWarnings() => _warned = false;

    internal static int CountOwned<T>(int count, Func<int, T> read, Func<T, bool> isOwned)
    {
        var owned = 0;
        for (var i = 0; i < count; i++)
        {
            try { if (isOwned(read(i))) owned++; }
            catch { } // An unreadable entry must not hide later entries or stations.
        }
        return owned;
    }

    internal int RemoveOwned<T>(bool isHost, string station, IReadOnlyList<T> snapshot,
        Func<T, bool> isOwned, Func<T, string> cloneId, Action<int> removeAt,
        Action<T>? onRemoved = null)
    {
        if (!isHost) return 0;
        var removed = 0;
        // The adapter snapshots the entire pool before mutation. Descending indices
        // preserve native officer order and remove duplicate owned entries safely.
        for (var i = snapshot.Count - 1; i >= 0; i--)
        {
            try
            {
                var entry = snapshot[i];
                if (!isOwned(entry)) continue;
                if (!_warned)
                {
                    var id = "unavailable";
                    try { id = cloneId(entry); }
                    catch { } // Diagnostic failure must not prevent owned removal.
                    _warned = true;
                    try { _warn($"OC station pool contamination: clone={id}, station={station}; removing OC-owned entry and releasing roster slot for normal replacement. Warning once per load."); }
                    catch { }
                }
                removeAt(i);
                removed++;
                onRemoved?.Invoke(entry);
            }
            catch { } // A failed read/removal stays visible and can retry next poll.
        }
        return removed;
    }
}
