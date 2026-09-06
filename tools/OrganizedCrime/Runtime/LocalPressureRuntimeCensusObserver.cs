namespace OrganizedCrime.Runtime;

public interface ILocalPressureRuntimeCensusSource
{
    bool TryReadSnapshot(out LocalPressureRuntimeCensusSnapshot snapshot);
}

public readonly record struct LocalPressureRuntimeCensusSnapshot(
    bool IsServer,
    bool IsHost,
    long? TotalGameMinutes,
    int? ElapsedDays,
    int? Time24h,
    double? CurrentTime,
    bool IsSleepInProgress,
    bool SaveFolderPresent,
    int PlayerCount,
    bool HostOwnedPlayerCodePresent,
    bool HostOwnedPlayerCodeUnique,
    bool ConnectionPresent);

public readonly record struct LocalPressureRuntimeCensusRow(
    int Sequence,
    string CallbackName,
    DateTime ReceivedAtUtc,
    bool SnapshotAvailable,
    LocalPressureRuntimeCensusSnapshot Snapshot)
{
    public long? TotalGameMinutes => SnapshotAvailable ? Snapshot.TotalGameMinutes : null;
    public int? ElapsedDays => SnapshotAvailable ? Snapshot.ElapsedDays : null;
    public int? Time24h => SnapshotAvailable ? Snapshot.Time24h : null;
    public double? CurrentTime => SnapshotAvailable ? Snapshot.CurrentTime : null;
    public bool SaveFolderPresent => SnapshotAvailable && Snapshot.SaveFolderPresent;
    public int? PlayerCount => SnapshotAvailable ? Snapshot.PlayerCount : null;
    public bool HostOwnedPlayerCodePresent => SnapshotAvailable && Snapshot.HostOwnedPlayerCodePresent;
    public bool HostOwnedPlayerCodeUnique => SnapshotAvailable && Snapshot.HostOwnedPlayerCodeUnique;
    public bool ConnectionPresent => SnapshotAvailable && Snapshot.ConnectionPresent;
}

/// <summary>
/// Bounded, read-only census storage. Wiring this observer to native callbacks
/// is intentionally external and remains owner-approved proof work.
/// </summary>
public sealed class LocalPressureRuntimeCensusObserver : IDisposable
{
    private readonly int _maxRows;
    private readonly List<LocalPressureRuntimeCensusRow> _rows = new();
    private bool _disposed;

    public LocalPressureRuntimeCensusObserver(int maxRows = 256)
    {
        if (maxRows <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRows));

        _maxRows = maxRows;
    }

    public IReadOnlyList<LocalPressureRuntimeCensusRow> Rows => _rows;

    public bool CapacityReached => _rows.Count >= _maxRows;

    public void Observe(string callbackName, ILocalPressureRuntimeCensusSource source)
    {
        if (_disposed || _rows.Count >= _maxRows)
            return;

        var normalizedCallbackName = string.IsNullOrWhiteSpace(callbackName)
            ? "(unknown)"
            : callbackName.Trim();
        var available = false;
        var snapshot = default(LocalPressureRuntimeCensusSnapshot);

        try
        {
            available = source is not null && source.TryReadSnapshot(out snapshot);
        }
        catch
        {
            available = false;
            snapshot = default;
        }

        _rows.Add(new LocalPressureRuntimeCensusRow(
            _rows.Count + 1,
            normalizedCallbackName,
            DateTime.UtcNow,
            available,
            snapshot));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _rows.Clear();
    }
}
