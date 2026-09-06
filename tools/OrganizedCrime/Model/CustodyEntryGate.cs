namespace OrganizedCrime.Model;

public sealed record CustodyEntryOccurrence(
    string PlayerId,
    Guid SessionEpoch,
    long LoadEpoch,
    string? Region,
    string? PropertyCode);

public enum CustodyEntryRejectReason
{
    None,
    MissingIdentity,
    ExistingCustody,
    StaleEpoch,
    ConfirmationMissing,
    PlayerMismatch,
    AlreadyCommitted
}

public sealed record CustodyEntryResult(
    bool Accepted,
    CustodyEntryRejectReason RejectReason,
    string? CorrelationId,
    CustodyEntryOccurrence? Occurrence);

public sealed class CustodyEntryGate
{
    private readonly object _sync = new();
    private readonly Dictionary<string, PlayerLatch> _latches = new(StringComparer.Ordinal);
    private Guid? _sessionEpoch;
    private long _loadEpoch;

    public void ResetForEpoch(Guid sessionEpoch, long loadEpoch)
    {
        lock (_sync)
        {
            _sessionEpoch = sessionEpoch;
            _loadEpoch = loadEpoch;
            _latches.Clear();
        }
    }

    public void ObserveNotArrested(string playerId, Guid sessionEpoch, long loadEpoch)
    {
        if (!IsUsablePlayerId(playerId))
            return;

        lock (_sync)
        {
            if (!IsCurrentEpoch(sessionEpoch, loadEpoch))
                return;

            if (!_latches.TryGetValue(playerId, out var latch))
            {
                latch = new PlayerLatch();
                _latches.Add(playerId, latch);
            }

            latch.Armed = true;
        }
    }

    public CustodyEntryResult TryCommit(
        CustodyEntryOccurrence occurrence,
        string confirmedPlayerId,
        Guid sessionEpoch,
        long loadEpoch,
        bool isArrestedAfterCall)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        lock (_sync)
        {
            if (!IsUsablePlayerId(occurrence.PlayerId) || !IsUsablePlayerId(confirmedPlayerId))
                return Reject(CustodyEntryRejectReason.MissingIdentity);

            if (!IsCurrentEpoch(sessionEpoch, loadEpoch) ||
                occurrence.SessionEpoch != sessionEpoch ||
                occurrence.LoadEpoch != loadEpoch)
            {
                return Reject(CustodyEntryRejectReason.StaleEpoch);
            }

            if (!string.Equals(occurrence.PlayerId, confirmedPlayerId, StringComparison.Ordinal))
                return Reject(CustodyEntryRejectReason.PlayerMismatch);

            if (!isArrestedAfterCall)
                return Reject(CustodyEntryRejectReason.ConfirmationMissing);

            if (!_latches.TryGetValue(occurrence.PlayerId, out var latch))
                return Reject(CustodyEntryRejectReason.ExistingCustody);

            if (!latch.Armed)
                return Reject(CustodyEntryRejectReason.AlreadyCommitted);

            latch.Armed = false;
            latch.EpisodeCounter = checked(latch.EpisodeCounter + 1);
            var correlationId = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"custody/v1/{sessionEpoch:D}/{loadEpoch}/{occurrence.PlayerId}/{latch.EpisodeCounter}");
            return new CustodyEntryResult(true, CustodyEntryRejectReason.None, correlationId, occurrence);
        }
    }

    private bool IsCurrentEpoch(Guid sessionEpoch, long loadEpoch) =>
        _sessionEpoch == sessionEpoch && _loadEpoch == loadEpoch;

    private static bool IsUsablePlayerId(string? playerId) =>
        !string.IsNullOrWhiteSpace(playerId) &&
        !playerId.Contains('\0') &&
        !playerId.Contains('/') &&
        !playerId.Contains('\\');

    private static CustodyEntryResult Reject(CustodyEntryRejectReason reason) =>
        new(false, reason, null, null);

    private sealed class PlayerLatch
    {
        public bool Armed { get; set; }
        public long EpisodeCounter { get; set; }
    }
}
