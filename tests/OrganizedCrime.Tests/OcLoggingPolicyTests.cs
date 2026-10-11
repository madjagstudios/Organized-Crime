using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

[Collection("OC-151 composition scope")]
public sealed class OcLoggingPolicyTests
{
    [Theory]
    [InlineData(false, 50, 675, 0)]
    [InlineData(false, 1, 675, 0)]
    [InlineData(true, 50, 49, 0)]
    [InlineData(true, 50, 50, 1)]
    [InlineData(true, 50, 675, 1)]
    [InlineData(true, 0, 675, 0)]
    public void Owner_keys_gate_timing_independently_of_saved_threshold(bool ownerKeys, int threshold, double elapsed, int expected)
    {
        var lines = new List<string>();
        var policy = new OcLoggingPolicy(lines.Add, _ => { }) { DiagnosticsEnabled = ownerKeys };
        new OrganizedCrimeTimingReceipts(policy.Diagnostic, threshold).Report("save-complete", elapsed);
        Assert.Equal(expected, lines.Count);
    }

    [Theory]
    [InlineData("Local Pressure SaveStart snapshot: players=1; loadEpoch=4.")]
    [InlineData("Local Pressure sidecar write succeeded; playerId=p; revision=2; loadEpoch=4.")]
    [InlineData("Local Pressure phase changed: Saving; loadEpoch=4.")]
    [InlineData("Fish Warehouse runtime Property is ready for developer lifecycle validation.")]
    [InlineData("Fish Warehouse save state written to the Organized Crime sidecar.")]
    [InlineData("OC officers: despawned 2 for save.")]
    [InlineData("OC officers: respawned 2 after save.")]
    [InlineData("OC-52 staging proof at load: dead drops read: 25")]
    [InlineData("Chapter 3 quest: applied Family Work.")]
    [InlineData("[OC-142 probe] phase=prefix probeError=none")]
    [InlineData("Syndicate HQ storage receipt: load cycle began.")]
    [InlineData("Fish Warehouse employee locker placed (itemId=locker, guid=a, grid=(1,2), ParentProperty=oc_fishwarehouse).")]
    [InlineData("Fish Warehouse native build Grid enabled (guid=a, sourceProperty=barn, tiles=10).")]
    [InlineData("Fish Warehouse employee infrastructure enabled (capacity=6, idlePoints=6, npcSpawn=spawn).")]
    [InlineData("OC-109 warehouse lighting enabled: lamps=4 materials=barn.")]
    [InlineData("OC-110 garage jambs: count=2.")]
    [InlineData("Fish Warehouse native loading docks enabled (count=2, indices=0,1, source=barn, offStreet=True).")]
    [InlineData("Fish Warehouse frontage clearance restored 2 scene object(s).")]
    [InlineData("[oc7-bridge] dock populated via native RefreshOccupant.")]
    [InlineData("[oc7-dock] dock0: occupant=van;out=[].")]
    [InlineData("Fish Warehouse delivery action replayed (delivery=d, action=Dispatch).")]
    [InlineData("Fish Warehouse native replay summary: 2/2 resolved, loaded=2.")]
    [InlineData("Fish Warehouse employee replay summary: 2/2 supported employees replayed.")]
    [InlineData("Syndicate HQ storage receipt: completion result=Ready; ready=True; reason=none")]
    [InlineData("Syndicate HQ storage receipt: admission result=NotPrepared; reason=none")]
    [InlineData("Syndicate HQ storage receipt: grid preparation result=NotPrepared; prepared=True; reason=none")]
    [InlineData("Syndicate HQ storage receipt: pocket placement result=Ready; ready=True; reason=none")]
    [InlineData("Local Pressure readiness/reject reason changed: None; phase=Active; loadEpoch=4.")]
    public void Routine_details_are_hidden_only_via_explicit_diagnostic_calls(string message)
    {
        var lines = new List<string>();
        var warnings = new List<string>();
        var policy = new OcLoggingPolicy(lines.Add, warnings.Add);
        policy.Diagnostic(message);
        Assert.Empty(lines);
        Assert.Empty(warnings);
        policy.DiagnosticsEnabled = true;
        policy.Diagnostic(message);
        Assert.Equal(new[] { message }, lines);
        Assert.Empty(warnings);

        // Receipt-looking text never opts an ordinary call into suppression.
        // The probe contains the legacy failure keyword "error"; Information
        // still promotes it, while an explicit owner diagnostic stays Info.
        foreach (var diagnostics in new[] { false, true })
        {
            lines.Clear();
            warnings.Clear();
            policy.DiagnosticsEnabled = diagnostics;
            policy.Information(message);
            policy.Warning(message);
            if (message.Contains("probeError", StringComparison.Ordinal))
            {
                Assert.Empty(lines);
                Assert.Equal(new[] { message, message }, warnings);
            }
            else
            {
                Assert.Equal(new[] { message }, lines);
                Assert.Equal(new[] { message }, warnings);
            }
        }
    }

    [Theory]
    [InlineData("Local Pressure sidecar save failed: disk full.")]
    [InlineData("Local Pressure sidecar save was rejected: bad revision.")]
    [InlineData("Fish Warehouse runtime Property cleanup failed: partial cleanup.")]
    [InlineData("Fish Warehouse native navigation acceptance warning: graph=employee target=locker is unavailable.")]
    [InlineData("OC-52 staging proof at load failed: storage read failed.")]
    [InlineData("OC officers: respawn failed: missing template.")]
    [InlineData("Chapter 3 quest: applied failed: missing journal.")]
    [InlineData("Fish Warehouse frontage clearance could not restore destroyed scene object.")]
    [InlineData("WARNING: Fish Warehouse employee replay preserved unsupported record (guid=a, type=Unknown): unsupported employee.")]
    [InlineData("OC officer c1: checkpoint bind threw (InvalidOperationException).")]
    [InlineData("Fish Warehouse save state was not written: disk full.")]
    [InlineData("Syndicate HQ storage receipt: completion result=Failed; ready=False; reason=bad grid")]
    public void Mixed_information_failures_use_warning_in_both_modes(string message)
    {
        var lines = new List<string>();
        var warnings = new List<string>();
        var policy = new OcLoggingPolicy(lines.Add, warnings.Add);
        foreach (var diagnostics in new[] { false, true })
        {
            lines.Clear();
            warnings.Clear();
            policy.DiagnosticsEnabled = diagnostics;
            policy.Information(message);
            policy.Warning(message);
            Assert.Empty(lines);
            Assert.Equal(new[] { message, message }, warnings);
        }
    }

    [Theory]
    [InlineData("notice")]
    [InlineData("[Organized Crime] notice")]
    [InlineData("[Organized Crime] [Organized Crime] notice")]
    public void Melon_owns_exactly_one_prefix(string input)
    {
        var lines = new List<string>();
        var policy = new OcLoggingPolicy(message => lines.Add("[Organized Crime] " + message), _ => { });
        policy.Information(input);
        Assert.Equal(new[] { "[Organized Crime] notice" }, lines);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Native_replay_producer_hides_success_but_keeps_failure(bool diagnostics, bool fail)
    {
        var lines = new List<string>();
        var warnings = new List<string>();
        var policy = new OcLoggingPolicy(lines.Add, warnings.Add) { DiagnosticsEnabled = diagnostics };
        var savedDiagnostic = OrganizedCrimeLog.DiagnosticSink;
        try
        {
            OrganizedCrimeLog.DiagnosticSink = policy.Diagnostic;
            var descriptors = fail
                ? new[] { new FishWarehouseSavedObjectDescriptor(0, "CounterData", "invalid-guid", 0, false, false) }
                : Array.Empty<FishWarehouseSavedObjectDescriptor>();
            var host = new FishWarehousePropertyReplayHost(new UnusedReplayAdapter(), descriptors, log: policy.Information);
            var result = host.Replay();
            Assert.Equal(fail ? FishWarehousePropertyReplayState.Failed : FishWarehousePropertyReplayState.Succeeded, result.State);
            Assert.Equal(fail ? 1 : 0, warnings.Count);
            Assert.Equal(!fail && diagnostics ? 1 : 0, lines.Count);
            if (fail) Assert.Contains("malformed-guid", warnings[0]);
            else if (diagnostics) Assert.Contains("native replay summary: 0/0 resolved", lines[0]);
        }
        finally
        {
            OrganizedCrimeLog.DiagnosticSink = savedDiagnostic;
        }
    }

    private sealed class UnusedReplayAdapter : IFishWarehouseNativePropertyReplayAdapter
    {
        public bool IsGuidRegistered(string guid) => throw new InvalidOperationException("unexpected adapter call");
        public bool TryGetObjectLoaderLoadOrder(string dataType, out int loadOrder) => throw new InvalidOperationException("unexpected adapter call");
        public void LoadObject(int payloadIndex) => throw new InvalidOperationException("unexpected adapter call");
        public FishWarehouseRestoredObjectObservation ObserveObject(string guid) => throw new InvalidOperationException("unexpected adapter call");
    }
    [Fact]
    public void Unknown_warning_and_player_notice_remain_visible()
    {
        var lines = new List<string>();
        var warnings = new List<string>();
        var policy = new OcLoggingPolicy(lines.Add, warnings.Add);
        var notices = new[]
        {
            "Chief Campbell late fee charged: round 2, 30000 in cash.",
            "Chapter 3 Nell: sent built",
            "Chief Campbell sent the forced-Wanted warning.",
            "Chapter 3 Arthur cooperation call delivered.",
            "Chapter 3 raid completed; seized 20 bricks.",
            "unrecognized informational notice"
        };
        var failures = new[]
        {
            "distinct new failure",
            "OC pool-sweep: clone cleanup warning",
            "OC officer clone c1: diagnostics warning",
            "Local Pressure phase changed: Quarantined; loadEpoch=4.",
            "Nell portrait: no icon found; keeping the default.",
            "OC checkpoint c1 placed: network spawned False.",
            "Fish Warehouse employee replay summary: 1/2 supported employees replayed."
        };
        foreach (var diagnostics in new[] { false, true })
        {
            lines.Clear();
            warnings.Clear();
            policy.DiagnosticsEnabled = diagnostics;
            foreach (var message in failures) policy.Warning(message);
            foreach (var message in notices) policy.Information(message);
            Assert.Equal(failures, warnings);
            Assert.Equal(notices, lines);
        }
    }

    [Fact]
    public void Command_response_bypasses_diagnostics_and_severity_classification()
    {
        var lines = new List<string>();
        var warnings = new List<string>();
        var policy = new OcLoggingPolicy(lines.Add, warnings.Add);
        policy.CommandResponse("[Organized Crime] OC officers: despawned 2 for save.");
        policy.CommandResponse("Chapter 3 QA commands are disabled: OwnerQaKeys is off.");
        Assert.Equal(new[]
        {
            "OC officers: despawned 2 for save.",
            "Chapter 3 QA commands are disabled: OwnerQaKeys is off."
        }, lines);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Identical_existing_once_warnings_aggregate_but_distinct_sites_do_not()
    {
        var warnings = new List<string>();
        var policy = new OcLoggingPolicy(_ => { }, warnings.Add);
        const string a = "Chapter 3 raid entrance unavailable for rv; occupancy blocked, retrying every 30 seconds.";
        const string b = "Chapter 3 raid entrance unavailable for motelroom; occupancy blocked, retrying every 30 seconds.";
        for (var i = 0; i < 12; i++) policy.Warning(a);
        policy.Warning(b);
        policy.FlushWarningSummaries();
        Assert.Equal(a, warnings[0]);
        Assert.Contains("repeated 9 times", warnings[1]);
        Assert.Equal(b, warnings[2]);
        Assert.Contains("repeated 2 times", warnings[3]);
        Assert.Equal(4, warnings.Count);
        policy.FlushWarningSummaries();
        Assert.Equal(4, warnings.Count);
    }

    [Fact]
    public void Diagnostics_keep_full_warning_detail()
    {
        var warnings = new List<string>();
        var policy = new OcLoggingPolicy(_ => { }, warnings.Add) { DiagnosticsEnabled = true };
        const string message = "Chapter 3 raid entrance unavailable for rv; occupancy blocked, retrying every 30 seconds.";
        policy.Warning(message);
        policy.Warning(message);
        Assert.Equal(new[] { message, message }, warnings);
    }

    [Fact]
    public void Policy_does_not_swallow_sink_failures()
    {
        var failure = new InvalidOperationException("sink failed");
        var policy = new OcLoggingPolicy(_ => throw failure, _ => throw failure);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => policy.Warning("failure")));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => policy.CommandResponse("response")));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => policy.Information("notice")));
        policy.Diagnostic("routine");
        policy.DiagnosticsEnabled = true;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => policy.Diagnostic("routine")));
    }

    [Fact]
    public void Comparable_two_save_run_keeps_failures_responses_and_notices()
    {
        // Same 27 producer calls in both modes: 4 setup, 9 per save, 1 staging,
        // 2 identical existing-once warnings, 1 command response and 1 player notice.
        // Routine producers explicitly use Diagnostic; startup adds one visible line.
        var setup = new[]
        {
            "production runtime shell loaded.",
            "Timing receipts report any phase at or above 50 ms.",
            "Owner QA keys are enabled by preference; F8 recognition and fish warehouse developer keys are live.",
            "Fish Warehouse runtime Property is ready for developer lifecycle validation."
        };
        var save = new[]
        {
            "Local Pressure phase changed: Saving; loadEpoch=1.",
            "Local Pressure SaveStart snapshot: players=1; loadEpoch=1.",
            "Local Pressure sidecar write succeeded; playerId=p; revision=1; loadEpoch=1.",
            "Local Pressure phase changed: Active; loadEpoch=1.",
            "Fish Warehouse save state written to the Organized Crime sidecar.",
            "OC officers: despawned 2 for save.",
            "OC officers: respawned 2 after save.",
            "timing: save-complete 675 ms",
            "timing: save-complete/hq 650 ms"
        };
        Assert.Equal(27, setup.Length + 2 * save.Length + 5);
        foreach (var diagnostics in new[] { false, true })
        {
            var lines = new List<string>();
            var warnings = new List<string>();
            var policy = new OcLoggingPolicy(lines.Add, warnings.Add) { DiagnosticsEnabled = diagnostics };
            foreach (var message in setup) policy.Diagnostic(message);
            policy.Startup("2.0.0");
            policy.Diagnostic("OC-52 staging proof at load: dead drops read: 25");
            for (var i = 0; i < 2; i++)
            {
                foreach (var message in save) policy.Diagnostic(message);
                policy.Warning("Chapter 3 raid entrance unavailable for rv; occupancy blocked, retrying every 30 seconds.");
            }
            policy.CommandResponse("Chapter 3 QA: demand d1 issued: 250 OG Kush, due day 4.");
            policy.Information("Chief Campbell made his protection offer.");
            policy.FlushWarningSummaries();
            Assert.Equal(diagnostics ? 28 : 5, lines.Count + warnings.Count);
            Assert.Equal(2, warnings.Count);
            Assert.Contains("Chapter 3 QA: demand d1 issued: 250 OG Kush, due day 4.", lines);
            Assert.Contains("Chief Campbell made his protection offer.", lines);
            var startup = $"v2.0.0 ready; diagnostics={(diagnostics ? "on" : "off")} (OwnerQaKeys).";
            Assert.Single(lines, line => line == startup);
            var visible = new[]
            {
                startup,
                "Chapter 3 QA: demand d1 issued: 250 OG Kush, due day 4.",
                "Chief Campbell made his protection offer."
            };
            var expected = diagnostics
                ? setup.Concat(new[] { startup, "OC-52 staging proof at load: dead drops read: 25" })
                    .Concat(save).Concat(save).Concat(visible.Skip(1))
                : visible;
            Assert.Equal(expected, lines);
            const string raid = "Chapter 3 raid entrance unavailable for rv; occupancy blocked, retrying every 30 seconds.";
            Assert.Equal(new[] { raid, diagnostics ? raid : $"Warning repeated 1 times: {raid}" }, warnings);
        }
    }
}
