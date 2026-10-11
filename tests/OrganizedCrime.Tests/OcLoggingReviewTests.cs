using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;
using Xunit;

namespace OrganizedCrime.Tests;

[Collection("OC-151 composition scope")]
public sealed class OcLoggingReviewTests
{
    [Theory]
    [InlineData("Chapter 3 day tick skipped: the host clock was unreadable.")]
    [InlineData("Chapter 3 week tick skipped: the host clock was unreadable.")]
    [InlineData("Chapter 3 day skipped")]
    [InlineData("Chapter 3 day unreadable")]
    [InlineData("Chapter 3 day failed")]
    [InlineData("Chapter 3 day could not proceed")]
    [InlineData("Chapter 3 day refused")]
    [InlineData("Chapter 3 day quarantined")]
    [InlineData("Chapter 3 day blocked")]
    [InlineData("Chapter 3 day partial")]
    [InlineData("Chapter 3 day exception")]
    [InlineData("Chapter 3 day error")]
    [InlineData("Chapter 3 day invalid")]
    [InlineData("Chapter 3 day missing")]
    [InlineData("Chapter 3 day unavailable")]
    public void Failure_lines_remain_visible_in_both_modes_and_channels(string message)
    {
        foreach (var diagnostics in new[] { false, true })
        {
            var lines = new List<string>();
            var warnings = new List<string>();
            var policy = new OcLoggingPolicy(lines.Add, warnings.Add) { DiagnosticsEnabled = diagnostics };
            policy.Information(message);
            policy.Warning(message);
            Assert.Empty(lines);
            Assert.Equal(new[] { message, message }, warnings);
        }
    }

    [Theory]
    [InlineData("Chapter 3 day 4")]
    [InlineData("Fish Warehouse save state written to the Organized Crime sidecar.")]
    [InlineData("Local Pressure phase changed: Active; loadEpoch=4.")]
    [InlineData("OC officers: despawned 2 for save.")]
    [InlineData("timing: save-complete 675 ms")]
    [InlineData("[OC-142 probe] phase=prefix probeError=none")]
    public void Only_explicit_diagnostics_are_hidden(string message)
    {
        var lines = new List<string>();
        var warnings = new List<string>();
        var policy = new OcLoggingPolicy(lines.Add, warnings.Add);
        policy.Diagnostic(message);
        Assert.Empty(lines);
        Assert.Empty(warnings);
        policy.Information(message);
        policy.Warning(message);
        Assert.Equal(2, lines.Count + warnings.Count);
        lines.Clear();
        warnings.Clear();
        policy.DiagnosticsEnabled = true;
        policy.Diagnostic(message);
        Assert.Equal(new[] { message }, lines);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Startup_is_one_visible_line_with_one_mod_name(bool diagnostics)
    {
        var lines = new List<string>();
        var policy = new OcLoggingPolicy(line => lines.Add("[Organized Crime] " + line), _ => { })
            { DiagnosticsEnabled = diagnostics };
        policy.Startup("2.0.0");
        Assert.Equal(new[] { $"[Organized Crime] v2.0.0 ready; diagnostics={(diagnostics ? "on" : "off")} (OwnerQaKeys)." }, lines);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(20)]
    public void Save_start_flushes_pending_repeats_without_duplicate_teardown_summaries(int occurrences)
    {
        const string message = "Chapter 3 raid entrance unavailable for rv; occupancy blocked, retrying every 30 seconds.";
        var warnings = new List<string>();
        var policy = new OcLoggingPolicy(_ => { }, warnings.Add);
        var savedFlush = OrganizedCrimeLog.FlushWarningSummaries;
        try
        {
            OrganizedCrimeLog.FlushWarningSummaries = policy.FlushWarningSummaries;
            for (var i = 0; i < occurrences; i++) policy.Warning(message);
            var expectedBeforeSave = 1 + occurrences / 10;
            Assert.Equal(expectedBeforeSave, warnings.Count);
            var fanout = new LifecycleFanout(new LifecycleTables(), new FakeOcEngineAdapters(), () => OrganizedCrimeTimingReceipts.Disabled);
            fanout.HandleSaveStart();
            Assert.Equal(expectedBeforeSave + (occurrences % 10 != 0 ? 1 : 0), warnings.Count);
            if (occurrences < 10) Assert.Contains($"repeated {occurrences - 1} times", warnings[1]);
            var afterSave = warnings.ToArray();
            policy.FlushWarningSummaries();
            Assert.Equal(afterSave, warnings);
        }
        finally
        {
            OrganizedCrimeLog.FlushWarningSummaries = savedFlush;
        }
    }
}
