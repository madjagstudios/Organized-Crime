namespace OrganizedCrime.Runtime;

public sealed class LocalPressureRuntimeComposition : IDisposable
{
    private bool _disposed;

    public LocalPressureRuntimeComposition(
        ILocalPressureRuntimeHostAdapter adapter,
        Func<string?, ILocalPressureStateRepository?> repositoryFactory,
        Action<string>? log = null,
        ILocalPressureTierTransitionSink? tierTransitionSink = null,
        Action<string>? receiptLog = null)
    {
        Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        Service = new LocalPressureRuntimeService(
            adapter,
            repositoryFactory,
            log: log,
            tierTransitionSink: tierTransitionSink,
            receiptLog: receiptLog);
    }

    public ILocalPressureRuntimeHostAdapter Adapter { get; }
    public LocalPressureRuntimeService Service { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Service.Dispose();
    }
}
