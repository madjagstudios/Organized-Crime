using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc176CloneLifetimeTests
{
    [Theory]
    [InlineData(false, true, true, true, true, "avatar")]
    [InlineData(true, false, false, false, false, "movement")]
    [InlineData(true, true, false, true, true, "movement.agent")]
    [InlineData(true, true, true, false, true, "movement.npc")]
    [InlineData(true, true, true, true, false, "movement.speedController")]
    public void Diagnostic_identifies_exact_missing_dependency_without_reading_absent_movement(
        bool avatar, bool movement, bool agent, bool owner, bool speed, string expected)
    {
        Assert.Equal(new[] { expected }, OcOfficerDependencyIssues.Read(new(avatar, movement, agent, owner, speed), Array.Empty<OcEquippableDependency>()));
    }

    [Fact]
    public void Movement_diagnostic_can_separate_null_agent_from_a_null_off_mesh_link()
    {
        Assert.Equal(new[] { "movement.offMeshLink" }, OcOfficerDependencyIssues.Read(
            new(true, true, true, true, true, false), Array.Empty<OcEquippableDependency>()));
    }

    [Fact]
    public void Equippable_diagnostic_identifies_both_bad_parent_and_missing_flashlight_light()
    {
        var missing = OcOfficerDependencyIssues.Read(new(true, true, true, true, true),
            new[] { new OcEquippableDependency("12", false, true), new OcEquippableDependency("13", true, false) });
        Assert.Equal(new[] { "equippable.avatar@12", "equippable.Light@13" }, missing);
        Assert.Empty(OcOfficerDependencyIssues.Read(new(true, true, true, true, true), Array.Empty<OcEquippableDependency>()));
    }

    [Fact]
    public void Copies_are_stripped_and_dependencies_bound_before_a_fresh_guid_and_activation()
    {
        var events = new List<string>();
        OcOfficerCloneLifetime.Prepare(() => events.Add("strip"), () => events.Add("bind"), () => events.Add("guid"));
        events.Add("activate");
        Assert.Equal(new[] { "strip", "bind", "guid", "activate" }, events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Preparation_failure_never_activates_or_runs_later_steps(int fail)
    {
        var events = new List<int>();
        void Step(int step) { events.Add(step); if (step == fail) throw new InvalidOperationException(); }
        Assert.Throws<InvalidOperationException>(() => OcOfficerCloneLifetime.Prepare(() => Step(0), () => Step(1), () => Step(2)));
        Assert.Equal(Enumerable.Range(0, fail + 1), events);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("release")]
    [InlineData("failed spawn")]
    public void Stop_and_car_return_precede_equipment_cleanup_and_quiescence_before_despawn(string boundary)
    {
        var events = new List<string> { boundary, "stop duty", "return car" };
        OcOfficerCloneLifetime.Quiesce(() => events.Add("unequip"), () => events.Add("stop updates"));
        events.Add("despawn");
        Assert.Equal(new[] { boundary, "stop duty", "return car", "unequip", "stop updates", "despawn" }, events);
    }

    [Fact]
    public void Failed_equipment_cleanup_stops_updates_and_refuses_despawn_until_retry()
    {
        var stopped = false;
        Assert.Throws<InvalidOperationException>(() => OcOfficerCloneLifetime.Quiesce(
            () => throw new InvalidOperationException(), () => stopped = true));
        Assert.True(stopped);
        var events = new List<string>();
        OcOfficerCloneLifetime.Quiesce(() => events.Add("retry unequip"), () => events.Add("stopped"));
        Assert.Equal(new[] { "retry unequip", "stopped" }, events);
    }

    [Fact]
    public void Copy_selection_never_mutates_external_equippables_or_dead_components()
    {
        var items = new[] { (1, true, true), (2, false, true), (3, true, false), (4, true, true) };
        Assert.Equal(new[] { 1, 4 }, OcOfficerCloneLifetime.Copies(items, x => x.Item2, x => x.Item3).Select(x => x.Item1));
    }

    [Fact]
    public void Diagnostic_is_host_only_five_seconds_and_one_warning_shared_until_load_reset()
    {
        var gate = new OcOfficerDependencyCheck(); var reads = 0; var warnings = new List<string>();
        IReadOnlyList<string> Read() { reads++; return new[] { "clone=oc_a pointer=1 missing=movement.agent", "clone=oc_b pointer=2 missing=equippable.Light" }; }
        gate.Poll(false, 0, Read, warnings.Add); Assert.Equal(0, reads);
        gate.Poll(true, 0, Read, warnings.Add);
        for (var time = 1; time < 5000; time++) gate.Poll(true, time, Read, warnings.Add);
        Assert.Equal(1, reads); Assert.Single(warnings);
        Assert.Contains("clone=oc_a", warnings[0]); Assert.Contains("clone=oc_b", warnings[0]);
        gate.Poll(true, 5000, Read, warnings.Add); Assert.Equal(2, reads); Assert.Single(warnings);
        gate.Reset(); gate.Poll(true, 5001, Read, warnings.Add); Assert.Equal(3, reads); Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void Healthy_load_does_not_spend_warning_and_throwing_logger_cannot_spam()
    {
        var gate = new OcOfficerDependencyCheck(); var warnings = 0;
        gate.Poll(true, 0, () => Array.Empty<string>(), _ => warnings++);
        gate.Poll(true, 5000, () => new[] { "missing avatar" }, _ => { warnings++; throw new InvalidOperationException(); });
        gate.Poll(true, 10000, () => new[] { "missing movement" }, _ => warnings++);
        Assert.Equal(1, warnings);
    }
}
