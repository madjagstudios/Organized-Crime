using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum NativeLawResponseAdmissionRejectReason
{
    None, StaleEpoch, MissingIdentity, MissingSourcePlayer, UnsupportedTierEdge,
    DuplicateCorrelation, InFlight, Disposed
}

public sealed record NativeLawResponseAdmissionResult(
    bool Accepted,
    NativeLawResponseAdmissionRejectReason RejectReason,
    NativeLawResponseRequest? Request);

public sealed class NativeLawResponseAdmissionGate : IDisposable
{
    private Guid _sessionEpoch;
    private long _loadEpoch;
    private bool _hasEpoch;
    private bool _disposed;
    private string? _inFlight;
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);

    public void ResetForEpoch(Guid sessionEpoch, long loadEpoch)
    {
        if (_disposed || (_hasEpoch && _sessionEpoch == sessionEpoch && _loadEpoch == loadEpoch))
            return;

        _sessionEpoch = sessionEpoch;
        _loadEpoch = loadEpoch;
        _hasEpoch = true;
        _inFlight = null;
        _consumed.Clear();
    }

    public NativeLawResponseAdmissionResult TryBegin(
        LocalPressureTierTransitionNotification transition,
        NativeLawResponseProfile profile)
    {
        if (_disposed) return Reject(NativeLawResponseAdmissionRejectReason.Disposed);
        if (!_hasEpoch || transition.SessionEpoch != _sessionEpoch || transition.LoadEpoch != _loadEpoch)
            return Reject(NativeLawResponseAdmissionRejectReason.StaleEpoch);
        if (string.IsNullOrWhiteSpace(transition.PlayerId))
            return Reject(NativeLawResponseAdmissionRejectReason.MissingIdentity);
        if (transition.SourcePlayer is null)
            return Reject(NativeLawResponseAdmissionRejectReason.MissingSourcePlayer);
        if (string.IsNullOrWhiteSpace(transition.CorrelationId))
            return Reject(NativeLawResponseAdmissionRejectReason.MissingIdentity);
        if (!IsSupportedRisingEdge(transition.PreviousTier, transition.CurrentTier))
            return Reject(NativeLawResponseAdmissionRejectReason.UnsupportedTierEdge);
        if (_inFlight is not null)
            return Reject(NativeLawResponseAdmissionRejectReason.InFlight);
        if (_consumed.Contains(transition.CorrelationId))
            return Reject(NativeLawResponseAdmissionRejectReason.DuplicateCorrelation);

        _inFlight = transition.CorrelationId;
        return new(true, NativeLawResponseAdmissionRejectReason.None, new NativeLawResponseRequest(
            transition.SessionEpoch, transition.LoadEpoch, transition.PlayerId, transition.SourcePlayer,
            transition.CorrelationId, transition.CurrentTier, transition.Region, transition.PropertyCode, profile));
    }

    public void Complete(string correlationId)
    {
        if (_disposed || _inFlight != correlationId) return;
        _consumed.Add(correlationId);
        _inFlight = null;
    }

    public void Dispose()
    {
        _disposed = true;
        _inFlight = null;
        _consumed.Clear();
    }

    private static bool IsSupportedRisingEdge(LocalPressureTier previous, LocalPressureTier current) =>
        current > previous && current is LocalPressureTier.Watched or LocalPressureTier.Critical;

    private static NativeLawResponseAdmissionResult Reject(NativeLawResponseAdmissionRejectReason reason) =>
        new(false, reason, null);
}
