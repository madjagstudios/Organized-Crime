namespace OrganizedCrime.Runtime;

public sealed class NativeLawResponseController : ILocalPressureTierTransitionSink, IDisposable
{
    private readonly NativeLawResponseAdmissionGate _gate;
    private readonly INativeLawResponseAdapter _adapter;
    private readonly NativeLawResponseProfile _profile;
    private readonly Action<string> _log;
    private bool _disposed;

    public NativeLawResponseController(
        NativeLawResponseAdmissionGate gate,
        INativeLawResponseAdapter adapter,
        NativeLawResponseProfile profile,
        Action<string> log)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public NativeLawResponseResult? LastResult { get; private set; }

    public void Publish(LocalPressureTierTransitionNotification notification)
    {
        if (_disposed)
            return;

        var admission = _gate.TryBegin(notification, _profile);
        if (!admission.Accepted || admission.Request is null)
        {
            LastResult = SuppressedOrRejected(notification.CorrelationId, admission);
            return;
        }

        try
        {
            LastResult = _adapter.TryRequest(admission.Request);
        }
        catch (Exception exception)
        {
            LastResult = new NativeLawResponseResult(
                NativeLawResponseResultState.AdapterUnavailable,
                admission.Request.CorrelationId,
                $"Native law-response adapter threw: {exception.GetType().Name}");
        }
        finally
        {
            _gate.Complete(admission.Request.CorrelationId);
        }

        _log(FormatBoundedResult(LastResult));
    }

    public void ResetForEpoch(Guid sessionEpoch, long loadEpoch)
    {
        if (_disposed)
            return;

        _gate.ResetForEpoch(sessionEpoch, loadEpoch);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _gate.Dispose();
    }

    private static NativeLawResponseResult SuppressedOrRejected(
        string correlationId,
        NativeLawResponseAdmissionResult admission)
    {
        var state = admission.RejectReason is
            NativeLawResponseAdmissionRejectReason.UnsupportedTierEdge or
            NativeLawResponseAdmissionRejectReason.DuplicateCorrelation or
            NativeLawResponseAdmissionRejectReason.InFlight
                ? NativeLawResponseResultState.Suppressed
                : NativeLawResponseResultState.Rejected;
        return new NativeLawResponseResult(state, correlationId, admission.RejectReason.ToString());
    }

    private static string FormatBoundedResult(NativeLawResponseResult result) =>
        $"Native law response {result.State} for {result.CorrelationId}: {result.Reason}";
}
