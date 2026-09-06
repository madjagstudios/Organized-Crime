namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-73 spec decision 10. The Local Pressure runtime takes exactly one sink; this is that sink, and
/// it forwards to the OC-41 law response controller first and to the Chief Campbell observer second,
/// each inside its own try/catch so neither can suppress the other. The order is fixed and asserted:
/// the law response is the shipped, live proven consumer and keeps first call, and the Chief's
/// observer only ever enqueues into memory, so it can never delay a dispatch.
/// </summary>
public sealed class LocalPressureTierTransitionFanOut : ILocalPressureTierTransitionSink, IDisposable
{
    private readonly ILocalPressureTierTransitionSink _first;
    private readonly ILocalPressureTierTransitionSink _second;
    private readonly Action<string> _log;
    private bool _disposed;

    public LocalPressureTierTransitionFanOut(
        ILocalPressureTierTransitionSink first, ILocalPressureTierTransitionSink second, Action<string>? log = null)
    {
        _first = first ?? throw new ArgumentNullException(nameof(first));
        _second = second ?? throw new ArgumentNullException(nameof(second));
        _log = log ?? (_ => { });
    }

    public void Publish(LocalPressureTierTransitionNotification notification)
    {
        if (_disposed) return;
        Forward(() => _first.Publish(notification), "law response");
        Forward(() => _second.Publish(notification), "Chief Campbell observer");
    }

    public void ResetForEpoch(Guid sessionEpoch, long loadEpoch)
    {
        if (_disposed) return;
        Forward(() => _first.ResetForEpoch(sessionEpoch, loadEpoch), "law response");
        Forward(() => _second.ResetForEpoch(sessionEpoch, loadEpoch), "Chief Campbell observer");
    }

    // OC-73 review fix (finding 7). Used to dispose both sinks here as well, but neither is owned by
    // this fan out: the composition that constructs it (NativeLawResponseModComposition) also owns and
    // disposes the law response controller through its own _controllerLifetime, so disposing it here
    // too double disposed it, once through this call and once through the composition's own. Harmless
    // today only because the controller's own Dispose happens to be idempotent; this fan out should
    // not own a lifetime the composition already owns. Disposing merely flips the flag so Publish and
    // ResetForEpoch stop forwarding.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    private void Forward(Action action, string label)
    {
        try { action(); }
        catch (Exception exception) { _log($"Local Pressure tier transition fan out to the {label} threw: {exception.GetType().Name}"); }
    }
}
