namespace OrganizedCrime.Core.Diagnostics;

/// <summary>
/// Keyed warn-once set: the first <see cref="Report"/> for a key invokes the
/// sink, repeats stay silent until the caller clears the key. The caller owns
/// every reset point (by key, by predicate, or in full); nothing resets on
/// its own. Not thread-safe: callers already hold their own locks.
/// </summary>
public sealed class OnceLog
{
    private readonly Action<string> _sink;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    public OnceLog(Action<string> sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <summary>
    /// Reports <paramref name="line"/> under <paramref name="key"/>: logs on
    /// the first report of the key and returns true, else stays silent and
    /// returns false.
    /// </summary>
    public bool Report(string key, string line)
    {
        if (!_reported.Add(key)) return false;
        _sink(line);
        return true;
    }

    /// <summary>Re-arms <paramref name="key"/>; returns whether it was reported.</summary>
    public bool Clear(string key) => _reported.Remove(key);

    /// <summary>
    /// Re-arms every key matching <paramref name="predicate"/> (for example a
    /// per-job prune); returns how many were removed.
    /// </summary>
    public int ClearWhere(Func<string, bool> predicate) => _reported.RemoveWhere(key => predicate(key));

    /// <summary>Re-arms every key.</summary>
    public void ClearAll() => _reported.Clear();

    /// <summary>Whether <paramref name="key"/> is currently reported (silent).</summary>
    public bool Contains(string key) => _reported.Contains(key);
}
