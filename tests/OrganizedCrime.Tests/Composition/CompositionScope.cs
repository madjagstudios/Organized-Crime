// OC-151: one built composition for integration tests. Builds the root through the
// recording fake and restores the statics Build assigns (log delegates, QA access)
// on dispose, following the suite's save/restore precedent for parallel safety.
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;

namespace OrganizedCrime.Tests;

internal sealed class CompositionScope : IDisposable
{
    private readonly Action<string> _savedWarning;
    private readonly Action<string> _savedReceipt;
    private readonly IChapter3QaTarget? _savedService;

    public CompositionScope(Action<FakeOcEngineAdapters>? configure = null, bool build = true, OcCompositionLifetime? lifetime = null)
    {
        _savedWarning = OrganizedCrimeLog.Warning;
        _savedReceipt = OrganizedCrimeLog.Receipt;
        _savedService = Chapter3QaAccess.Service;
        Engine = new FakeOcEngineAdapters();
        configure?.Invoke(Engine);
        Root = new OcCompositionRoot(Engine, Logs.Add, Warnings.Add, lifetime);
        if (build)
            Root.Build();
    }

    public FakeOcEngineAdapters Engine { get; }
    public OcCompositionRoot Root { get; }
    public List<string> Logs { get; } = new();
    public List<string> Warnings { get; } = new();

    public void Dispose()
    {
        OrganizedCrimeLog.Warning = _savedWarning;
        OrganizedCrimeLog.Receipt = _savedReceipt;
        Chapter3QaAccess.Service = _savedService;
    }
}
