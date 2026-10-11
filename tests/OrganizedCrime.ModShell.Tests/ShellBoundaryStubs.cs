// Compile the production Mod.cs constructor and lifecycle without loading Melon
// or game assemblies. Graph construction is covered by the main composition suite.
namespace MelonLoader
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonInfoAttribute : Attribute
    {
        public MelonInfoAttribute(Type type, string name, string version, string author) { }
    }

    public abstract class MelonMod
    {
        public TestLogger LoggerInstance { get; } = new();
        public object HarmonyInstance { get; } = new();
        public TestInfo Info { get; } = new();
        public virtual void OnInitializeMelon() { }
        public virtual void OnDeinitializeMelon() { }
        public virtual void OnUpdate() { }
        public virtual void OnGUI() { }
    }

    public sealed class TestInfo { public string Version => "1.0.1"; }
    public sealed class TestLogger
    {
        public List<string> Information { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
        public void Error(string message) => Errors.Add(message);
        public bool ThrowOnWarning { get; set; }
        public void Msg(string message) => Information.Add(message);
        public void Warning(string message)
        {
            if (ThrowOnWarning) throw new InvalidOperationException("sink failed");
            Warnings.Add(message);
        }
    }
}

namespace OrganizedCrime.Runtime.Composition
{
    internal sealed class OcCompositionLifetime { }
    internal sealed class MelonEngineAdapters
    {
        internal static bool OwnerQaKeys { get; set; }
        private readonly Action<bool>? _configureDiagnostics;
        public MelonEngineAdapters(object harmony, Action<string> warning, Action<bool>? configureDiagnostics)
            => _configureDiagnostics = configureDiagnostics;
        public void ConfigureDiagnostics() => _configureDiagnostics?.Invoke(OwnerQaKeys);
    }
    internal sealed class OcCompositionRoot
    {
        private readonly MelonEngineAdapters _engine;
        private readonly Action<string> _log;
        public OcCompositionRoot(MelonEngineAdapters engine, Action<string> log, Action<string> warning, OcCompositionLifetime lifetime)
        {
            _engine = engine;
            _log = log;
        }
        public bool TornDown { get; private set; }
        public TestLifecycle Lifecycle { get; } = new();
        public void Build()
        {
            _engine.ConfigureDiagnostics();
            OrganizedCrime.Runtime.OrganizedCrimeLog.Diagnostic("production runtime shell loaded.", _log);
        }
        public Exception? TeardownFailure { get; set; }
        public void Teardown()
        {
            TornDown = true;
            if (TeardownFailure is not null) throw TeardownFailure;
        }
    }
    internal sealed class TestLifecycle
    {
        public void Update() { }
        public void OnGUI() { }
    }
}
