using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

[Collection("OC-151 composition scope")]
public sealed class Chapter3PatrolMinorFollowupTests
{
    [Theory]
    [InlineData(false, 8)]
    [InlineData(true, 10)]
    public void Status_reports_the_active_admission_cap(bool lockdown, int cap)
    {
        using var f = new Chapter3PatrolServiceTests.Fixture();
        f.Input = f.Input with { Lockdown = lockdown };
        f.Checkpoints(6);
        f.Converge();
        var status = string.Join("\n", f.Service.Status());
        Assert.Contains($"slots {cap}/{cap}", status);
        Assert.DoesNotContain("(draining:", status);
        Assert.Contains(f.Roster.QaStatus(), s => s.Contains($"{cap}/{cap} slots held", StringComparison.Ordinal));
    }

    [Fact]
    public void Status_names_over_cap_drain_and_retained_response_slots()
    {
        using var f = new Chapter3PatrolServiceTests.Fixture();
        f.Input = f.Input with { Lockdown = true };
        f.Checkpoints(6);
        f.Converge();
        foreach (var binding in f.World.Bindings.Snapshot()) f.World.Busy.Add(binding.Officer.Pointer);
        f.Input = f.Input with { Lockdown = false };
        f.Converge();
        Assert.Contains(f.Service.Status(), s => s.Contains(
            "slots 10/8 (draining: 2 over cap; 4 responding officers keep their slots)", StringComparison.Ordinal));
        f.World.Busy.Clear();
        f.Converge();
        var status = string.Join("\n", f.Service.Status());
        Assert.Contains("slots 8/8", status);
        Assert.DoesNotContain("(draining:", status);
    }

    [Fact]
    public void Failed_input_reads_warn_once_per_exception_type_per_load_without_releasing_duty()
    {
        using var f = new Chapter3PatrolServiceTests.Fixture();
        f.Converge();
        var before = f.Roster.Snapshot().Select(s => s.CloneId).ToArray();
        var warnings = new List<string>();
        Exception failure = new InvalidOperationException("input unavailable");
        using var service = new Chapter3PatrolService(f.Roster, () => throw failure, warn: warnings.Add);
        for (var i = 0; i < 20; i++) service.Poll();
        Assert.Single(warnings);
        Assert.Contains("patrol input read failed (System.InvalidOperationException)", warnings[0]);
        failure = new ArgumentException("different read failure");
        service.Poll(); service.Poll();
        Assert.Equal(2, warnings.Count);
        failure = new InvalidOperationException("another message");
        service.Poll();
        Assert.Equal(2, warnings.Count);
        Assert.Equal(before, f.Roster.Snapshot().Select(s => s.CloneId).ToArray());
        service.OnSaveStart(); service.OnSaveComplete(); service.Poll();
        Assert.Equal(2, warnings.Count);
        // Save deliberately releases duty; establish a fresh load warning budget.
        service.OnPreLoad(); service.OnLoadComplete(); service.Poll(); service.Poll();
        Assert.Equal(3, warnings.Count);
        Assert.Contains("System.InvalidOperationException", warnings[2]);
        var logAttempts = 0;
        using var throwingLogger = new Chapter3PatrolService(f.Roster, () => throw failure,
            warn: _ => { logAttempts++; throw new InvalidOperationException("logger failed"); });
        throwingLogger.Poll(); throwingLogger.Poll();
        Assert.Equal(1, logAttempts);
    }

    [Fact]
    public void Checkpoint_reservations_equal_D3_active_set_staffing_totals()
    {
        var spots = new[]
        {
            new Chapter3CheckpointSpot("a", 0, 0, 0, 0),
            new Chapter3CheckpointSpot("b", 10, 0, 0, 0),
            new Chapter3CheckpointSpot("c", 20, 0, 0, 0)
        };
        foreach (var tier in new[] { LocalPressureTier.Watched, LocalPressureTier.Critical })
        {
            var active = Chapter3CheckpointPlanner.ActiveSet(0, 0, tier, false, spots,
                Array.Empty<Chapter3CheckpointNativeSite>());
            var reserved = tier == LocalPressureTier.Watched
                ? Chapter3PatrolRules.WatchedCheckpoints : Chapter3PatrolRules.CriticalCheckpoints;
            Assert.Equal(active.OcLabels.Count * Chapter3CheckpointService.OfficersPerCheckpoint, reserved);
        }
    }

    [Fact]
    public void Teardown_logs_patrol_disposal_once_and_keeps_the_pinned_trace()
    {
        using var scope = new CompositionScope();
        var setup = scope.Engine.Trace.ToArray();
        scope.Root.Teardown();
        CompositionAssertions.AssertTeardownTrace(scope.Engine, setup);
        Assert.Single(scope.Logs, s => s == "[Organized Crime] teardown:Patrols.Dispose");
        scope.Root.Teardown();
        Assert.Single(scope.Logs, s => s == "[Organized Crime] teardown:Patrols.Dispose");
    }
}
