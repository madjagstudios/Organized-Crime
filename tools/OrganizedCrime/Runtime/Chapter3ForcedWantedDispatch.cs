using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-74: builds one forced-Wanted dispatch request and sends it through the OC-41 adapter.
/// Reads the single supported host player from the shared host adapter (the adapter itself
/// re-validates authority, the canonical target, and pool capacity), always with the
/// VehicleTwoOfficerV1 profile. No new native dispatch code: the one station.Dispatch call
/// stays inside NativePoliceDispatchBoundary. Total: false when the sample cannot be read
/// or the send faults, never throws.
/// </summary>
public sealed class Chapter3ForcedWantedDispatch : IChapter3ForcedWantedDispatch
{
    private readonly ILocalPressureRuntimeHostAdapter _host;
    private readonly INativeLawResponseAdapter _adapter;
    private readonly Action<string> _warn;
    private bool _disposed;

    public Chapter3ForcedWantedDispatch(
        ILocalPressureRuntimeHostAdapter host,
        INativeLawResponseAdapter adapter,
        Action<string> warn)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
    }

    public bool TryDispatch(string playerId, Guid sessionEpoch, long loadEpoch, string correlationId, out NativeLawResponseResult? result)
    {
        result = null;
        if (_disposed)
            return false;
        try
        {
            if (_host.TryReadSupportedPlayers(out var players) != LocalPressurePlayerReadStatus.Ready || players.Count != 1)
                return false;
            if (players[0].SourcePlayer is not { } sourcePlayer)
                return false;
            var request = new NativeLawResponseRequest(
                sessionEpoch, loadEpoch, playerId, sourcePlayer, correlationId,
                LocalPressureTier.Critical, Region: null, PropertyCode: null, NativeLawResponseProfile.VehicleTwoOfficerV1);
            result = _adapter.TryRequest(request);
            return true;
        }
        catch (Exception exception)
        {
            _warn($"Chapter 3 forced-Wanted dispatch failed closed: {exception.GetType().Name}.");
            result = null;
            return false;
        }
    }

    public void Dispose() => _disposed = true;
}
