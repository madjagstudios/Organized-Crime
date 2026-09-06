using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public readonly record struct CustodyPlayerObservation(
    object? SourcePlayer,
    string? PlayerCode,
    bool IsHostOwned,
    bool HasConnection,
    bool IsArrested,
    string? Region,
    string? PropertyCode,
    bool ArrestStateReadSucceeded = true);

public readonly record struct PoliceCustodyPrefixState(
    bool Eligible,
    CustodyEntryOccurrence? Occurrence)
{
    public static PoliceCustodyPrefixState Ineligible => new(false, null);
}

public sealed class PoliceCustodyEvidenceBridge : IDisposable
{
    private readonly CustodyEntryGate _gate;
    private readonly ILocalPressureCustodyEvidenceWriter _writer;
    private readonly Action<string> _log;
    private Guid? _gateSessionEpoch;
    private long _gateLoadEpoch;
    private bool _disposed;

    public PoliceCustodyEvidenceBridge(
        CustodyEntryGate gate,
        ILocalPressureCustodyEvidenceWriter writer,
        Action<string>? log = null)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _log = log ?? (_ => { });
    }

    public PoliceCustodyPrefixState CapturePrefix(CustodyPlayerObservation observation)
    {
        if (_disposed || !observation.ArrestStateReadSucceeded || observation.IsArrested ||
            !observation.IsHostOwned || !observation.HasConnection)
            return PoliceCustodyPrefixState.Ineligible;

        LocalPressureActiveEvidenceSnapshot snapshot;
        try
        {
            if (!_writer.TryGetActiveEvidenceSnapshot(out snapshot))
                return PoliceCustodyPrefixState.Ineligible;
        }
        catch (Exception exception)
        {
            _log($"Police custody evidence snapshot read failed: {exception.Message}");
            return PoliceCustodyPrefixState.Ineligible;
        }

        if (!IsExactSupportedPlayer(observation, snapshot) || !IsConsistentPlayerCode(observation.PlayerCode, snapshot.PlayerId))
            return PoliceCustodyPrefixState.Ineligible;

        EnsureGateEpoch(snapshot);
        _gate.ObserveNotArrested(snapshot.PlayerId, snapshot.SessionEpoch, snapshot.LoadEpoch);
        var (region, propertyCode) = NormalizeContext(observation.Region, observation.PropertyCode);
        return new PoliceCustodyPrefixState(
            true,
            new CustodyEntryOccurrence(
                snapshot.PlayerId,
                snapshot.SessionEpoch,
                snapshot.LoadEpoch,
                region,
                propertyCode));
    }

    public void ConfirmPostfix(
        CustodyPlayerObservation observation,
        PoliceCustodyPrefixState prefixState)
    {
        if (_disposed || !prefixState.Eligible || prefixState.Occurrence is null ||
            !observation.ArrestStateReadSucceeded || !observation.IsArrested ||
            !observation.IsHostOwned || !observation.HasConnection)
        {
            return;
        }

        LocalPressureActiveEvidenceSnapshot snapshot;
        try
        {
            if (!_writer.TryGetActiveEvidenceSnapshot(out snapshot))
                return;
        }
        catch (Exception exception)
        {
            _log($"Police custody evidence snapshot confirmation failed: {exception.Message}");
            return;
        }

        if (!IsExactSupportedPlayer(observation, snapshot) || !IsConsistentPlayerCode(observation.PlayerCode, snapshot.PlayerId))
            return;

        var occurrence = prefixState.Occurrence;
        if (!string.Equals(occurrence.PlayerId, snapshot.PlayerId, StringComparison.Ordinal) ||
            occurrence.SessionEpoch != snapshot.SessionEpoch ||
            occurrence.LoadEpoch != snapshot.LoadEpoch)
        {
            return;
        }

        var commit = _gate.TryCommit(
            occurrence,
            snapshot.PlayerId,
            snapshot.SessionEpoch,
            snapshot.LoadEpoch,
            isArrestedAfterCall: true);
        if (!commit.Accepted || commit.CorrelationId is null || commit.Occurrence is null)
            return;

        try
        {
            _writer.TryApplyCustodyEvidence(
                new CustodyEntryEvidence(
                    commit.Occurrence.PlayerId,
                    commit.CorrelationId,
                    commit.Occurrence.Region,
                    commit.Occurrence.PropertyCode),
                commit.Occurrence.SessionEpoch,
                commit.Occurrence.LoadEpoch);
        }
        catch (Exception exception)
        {
            _log($"Police custody evidence submission failed: {exception.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _gate.ResetForEpoch(Guid.Empty, 0);
    }

    private void EnsureGateEpoch(LocalPressureActiveEvidenceSnapshot snapshot)
    {
        if (_gateSessionEpoch == snapshot.SessionEpoch && _gateLoadEpoch == snapshot.LoadEpoch)
            return;

        _gateSessionEpoch = snapshot.SessionEpoch;
        _gateLoadEpoch = snapshot.LoadEpoch;
        _gate.ResetForEpoch(snapshot.SessionEpoch, snapshot.LoadEpoch);
    }

    private static bool IsExactSupportedPlayer(
        CustodyPlayerObservation observation,
        LocalPressureActiveEvidenceSnapshot snapshot) =>
        observation.SourcePlayer is not null &&
        snapshot.SupportedPlayer is not null &&
        ReferenceEquals(observation.SourcePlayer, snapshot.SupportedPlayer);

    private static bool IsConsistentPlayerCode(string? playerCode, string canonicalPlayerId)
    {
        playerCode = playerCode?.Trim();
        return playerCode?.Contains('\0') != true &&
            (!LocalPressureHostLifecyclePolicies.IsCanonicalPlayerIdentity(playerCode) ||
             string.Equals(playerCode, canonicalPlayerId, StringComparison.Ordinal));
    }

    private static (string? Region, string? PropertyCode) NormalizeContext(string? region, string? propertyCode)
    {
        region = NormalizeContextValue(region);
        propertyCode = NormalizeContextValue(propertyCode);
        return region is null || propertyCode is null
            ? (null, null)
            : (region, propertyCode);
    }

    private static string? NormalizeContextValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.Length <= 128 && !normalized.Any(char.IsControl)
            ? normalized
            : null;
    }
}
