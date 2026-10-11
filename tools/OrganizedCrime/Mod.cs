using MelonLoader;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;

[assembly: MelonInfo(
    typeof(OrganizedCrime.OrganizedCrimeMod),
    "Organized Crime",
    "2.0.0",
    "MadJag Studios")]

namespace OrganizedCrime;

// OC-151: the mod shell is a thin composition publisher. OnInitializeMelon keeps the
// duplicate-init guard, publishes the root before Build (so a mid-build throw leaves
// partial state visible exactly like the old partially assigned fields), and builds.
// Update, GUI and teardown delegate through the root, null-safe like the old fields.
// The lifetime object survives teardown, preserving the timer accumulators and QA toggles
// across a later reinit exactly like the old MelonMod fields.
public sealed class OrganizedCrimeMod : MelonMod
{
    private bool _initialized;
    private OcCompositionRoot? _root;
    private OcLoggingPolicy? _logging;
    private readonly OcCompositionLifetime _lifetime = new();

    public override void OnInitializeMelon()
    {
        if (_initialized)
        {
            LoggerInstance.Warning("duplicate initialization was ignored.");
            return;
        }
        _initialized = true;
        var logging = new OcLoggingPolicy(
            LoggerInstance.Msg,
            LoggerInstance.Warning);
        _logging = logging;
        OrganizedCrimeLog.DiagnosticSink = logging.Diagnostic;
        OrganizedCrimeLog.FlushWarningSummaries = logging.FlushWarningSummaries;
        OrganizedCrimeLog.CommandResponse = logging.CommandResponse;
        var root = new OcCompositionRoot(
            new MelonEngineAdapters(HarmonyInstance, logging.Warning, enabled => logging.DiagnosticsEnabled = enabled),
            logging.Information,
            logging.Warning,
            _lifetime);
        _root = root;
        root.Build();
        logging.Startup(Info.Version);
    }

    public override void OnDeinitializeMelon()
    {
        TeardownAndFlush(() =>
        {
            _root?.Teardown();
        }, _logging, message => LoggerInstance.Error(message), () =>
        {
            _root = null;
            _logging = null;
            _initialized = false;
        });
    }

    public override void OnUpdate() => _root?.Lifecycle.Update();

    public override void OnGUI() => _root?.Lifecycle.OnGUI();

    // Flush even after a root failure; always release shell state. Exception.ToString
    // retains the type and stack, and rethrow preserves exception visibility.
    private static void TeardownAndFlush(Action teardown, OcLoggingPolicy? logging, Action<string> error, Action reset)
    {
        try
        {
            teardown();
        }
        catch (Exception exception)
        {
            error($"teardown failed: {exception}");
            throw;
        }
        finally
        {
            try
            {
                logging?.FlushWarningSummaries();
            }
            catch (Exception exception)
            {
                error($"teardown warning-summary flush failed: {exception}");
                throw;
            }
            finally
            {
                reset();
            }
        }
    }
}
