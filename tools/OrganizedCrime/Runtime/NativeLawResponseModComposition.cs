namespace OrganizedCrime.Runtime;

internal sealed class NativeLawResponseModComposition : IDisposable
{
    private readonly IDisposable _runtimeLifetime;
    private readonly IDisposable _controllerLifetime;
    private bool _disposed;

    public NativeLawResponseModComposition(
        Func<string?, ILocalPressureStateRepository?> repositoryFactory,
        Action<string> warningLog,
        Action<string> infoLog,
        Action<string>? receiptLog = null)
        : this(
            new LocalPressureHostLifecycleAdapter(warningLog),
            repositoryFactory,
            warningLog,
            infoLog,
            receiptLog: receiptLog)
    {
    }

    internal NativeLawResponseModComposition(
        ILocalPressureRuntimeHostAdapter hostAdapter,
        Func<string?, ILocalPressureStateRepository?> repositoryFactory,
        Action<string> warningLog,
        Action<string> infoLog,
        Func<NativeLawResponseAdmissionGate, INativeLawResponseAdapter, NativeLawResponseProfile, Action<string>, NativeLawResponseController>? controllerFactory = null,
        Func<ILocalPressureRuntimeHostAdapter, Func<string?, ILocalPressureStateRepository?>, Action<string>, ILocalPressureTierTransitionSink, LocalPressureRuntimeComposition>? runtimeFactory = null,
        Func<LocalPressureRuntimeComposition, IDisposable>? runtimeLifetimeFactory = null,
        Func<NativeLawResponseController, IDisposable>? controllerLifetimeFactory = null,
        Action<string>? receiptLog = null)
    {
        HostAdapter = hostAdapter ?? throw new ArgumentNullException(nameof(hostAdapter));
        ArgumentNullException.ThrowIfNull(repositoryFactory);
        ArgumentNullException.ThrowIfNull(warningLog);
        ArgumentNullException.ThrowIfNull(infoLog);

        controllerFactory ??= CreateController;
        runtimeFactory ??= (adapter, factory, log, sink) => CreateRuntime(adapter, factory, log, sink, receiptLog);

        var nativeAdapter = new NativePoliceDispatchAdapter(HostAdapter, warningLog);
        Controller = controllerFactory(
            new NativeLawResponseAdmissionGate(),
            nativeAdapter,
            NativeLawResponseProfile.VehicleTwoOfficerV1,
            infoLog);
        ChiefObserver = new Release1ChiefTierObserver();
        TierTransitionSink = new LocalPressureTierTransitionFanOut(Controller, ChiefObserver, warningLog);
        LocalPressureRuntime = runtimeFactory(
            HostAdapter,
            repositoryFactory,
            warningLog,
            TierTransitionSink);
        _runtimeLifetime = runtimeLifetimeFactory is null
            ? LocalPressureRuntime
            : runtimeLifetimeFactory(LocalPressureRuntime);
        _controllerLifetime = controllerLifetimeFactory is null
            ? Controller
            : controllerLifetimeFactory(Controller);
    }

    public ILocalPressureRuntimeHostAdapter HostAdapter { get; }
    public NativeLawResponseController Controller { get; }
    public Release1ChiefTierObserver ChiefObserver { get; }
    public ILocalPressureTierTransitionSink TierTransitionSink { get; }
    public LocalPressureRuntimeComposition LocalPressureRuntime { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        (TierTransitionSink as IDisposable)?.Dispose();
        _runtimeLifetime.Dispose();
        _controllerLifetime.Dispose();
    }

    private static NativeLawResponseController CreateController(
        NativeLawResponseAdmissionGate gate,
        INativeLawResponseAdapter adapter,
        NativeLawResponseProfile profile,
        Action<string> log) =>
        new(gate, adapter, profile, log);

    private static LocalPressureRuntimeComposition CreateRuntime(
        ILocalPressureRuntimeHostAdapter adapter,
        Func<string?, ILocalPressureStateRepository?> repositoryFactory,
        Action<string> log,
        ILocalPressureTierTransitionSink sink,
        Action<string>? receiptLog = null) =>
        new(adapter, repositoryFactory, log, tierTransitionSink: sink, receiptLog: receiptLog);
}
