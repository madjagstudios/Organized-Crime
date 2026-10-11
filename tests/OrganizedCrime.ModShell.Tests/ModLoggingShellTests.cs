using System.Reflection;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;
using Xunit;

namespace OrganizedCrime.ModShell.Tests;

public sealed class ModLoggingShellTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Constructor_initialize_and_teardown_reset_state_when_teardown_or_summary_sink_throws(bool diagnostics, bool teardownThrows, bool summaryThrows)
    {
        var savedDiagnostic = OrganizedCrimeLog.DiagnosticSink;
        var savedFlush = OrganizedCrimeLog.FlushWarningSummaries;
        var savedResponse = OrganizedCrimeLog.CommandResponse;
        var savedKeys = MelonEngineAdapters.OwnerQaKeys;
        var mod = new OrganizedCrimeMod();
        try
        {
            // Populate the actual shell via its constructor and initialization path,
            // rather than manually setting private initialized/root fields (OC-112).
            MelonEngineAdapters.OwnerQaKeys = diagnostics;
            mod.OnInitializeMelon();
            var root = Assert.IsType<OcCompositionRoot>(Field(mod, "_root"));
            Assert.Equal(true, Field(mod, "_initialized"));
            const string warning = "Chapter 3 raid entrance unavailable for rv; occupancy blocked, retrying every 30 seconds.";
            var logging = Assert.IsType<OcLoggingPolicy>(Field(mod, "_logging"));
            logging.DiagnosticsEnabled = false;
            logging.Warning(warning);
            logging.Warning(warning);
            var teardownFailure = new InvalidOperationException("root teardown failed");
            if (teardownThrows) root.TeardownFailure = teardownFailure;
            mod.LoggerInstance.ThrowOnWarning = summaryThrows;
            var thrown = Assert.Throws<InvalidOperationException>(mod.OnDeinitializeMelon);
            if (!summaryThrows) Assert.Same(teardownFailure, thrown);
            Assert.Equal((teardownThrows ? 1 : 0) + (summaryThrows ? 1 : 0), mod.LoggerInstance.Errors.Count);
            if (teardownThrows)
            {
                Assert.Contains(mod.LoggerInstance.Errors, line =>
                    line.Contains("System.InvalidOperationException: root teardown failed", StringComparison.Ordinal) &&
                    line.Contains("OcCompositionRoot.Teardown", StringComparison.Ordinal));
            }
            if (summaryThrows)
            {
                Assert.Contains(mod.LoggerInstance.Errors, line =>
                    line.Contains("System.InvalidOperationException: sink failed", StringComparison.Ordinal) &&
                    line.Contains("TestLogger.Warning", StringComparison.Ordinal));
            }
            else
            {
                Assert.Contains(mod.LoggerInstance.Warnings, line => line.Contains("repeated 1 times", StringComparison.Ordinal));
            }
            Assert.True(root.TornDown);
            Assert.Null(Field(mod, "_root"));
            Assert.Null(Field(mod, "_logging"));
            Assert.Equal(false, Field(mod, "_initialized"));
            mod.LoggerInstance.ThrowOnWarning = false;
            mod.OnInitializeMelon();
            Assert.NotSame(root, Field(mod, "_root"));
            Assert.Equal(true, Field(mod, "_initialized"));
            mod.OnDeinitializeMelon();
        }
        finally
        {
            OrganizedCrimeLog.DiagnosticSink = savedDiagnostic;
            OrganizedCrimeLog.FlushWarningSummaries = savedFlush;
            OrganizedCrimeLog.CommandResponse = savedResponse;
            MelonEngineAdapters.OwnerQaKeys = savedKeys;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Initialization_always_emits_one_version_readiness_summary_after_preferences(bool diagnostics)
    {
        var savedDiagnostic = OrganizedCrimeLog.DiagnosticSink;
        var savedFlush = OrganizedCrimeLog.FlushWarningSummaries;
        var savedResponse = OrganizedCrimeLog.CommandResponse;
        var savedKeys = MelonEngineAdapters.OwnerQaKeys;
        MelonEngineAdapters.OwnerQaKeys = diagnostics;
        var mod = new OrganizedCrimeMod();
        mod.OnInitializeMelon();
        try
        {
            Assert.Single(mod.LoggerInstance.Information, line => line.StartsWith("v1.0.1 ready;", StringComparison.Ordinal));
            Assert.Equal(diagnostics ? 2 : 1, mod.LoggerInstance.Information.Count);
            Assert.DoesNotContain(mod.LoggerInstance.Information, line => line.Contains("Organized Crime", StringComparison.Ordinal));
        }
        finally
        {
            mod.OnDeinitializeMelon();
            OrganizedCrimeLog.DiagnosticSink = savedDiagnostic;
            OrganizedCrimeLog.FlushWarningSummaries = savedFlush;
            OrganizedCrimeLog.CommandResponse = savedResponse;
            MelonEngineAdapters.OwnerQaKeys = savedKeys;
        }
    }

    private static object? Field(OrganizedCrimeMod mod, string name) =>
        typeof(OrganizedCrimeMod).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mod);
}
