using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc178bPursuitWeaponTests
{
    [Fact]
    public void Adapter_observes_pursuit_callback_fields_after_initial_Start_allowance()
    {
        var source = ReadNative();
        Assert.Contains("if (!awaitingCombatStart)", source);
        Assert.Contains("combat.TryCast<PursuitBehaviour>()", source);
        Assert.Contains("pursuit.officer", source);
        Assert.Contains("cachedOfficer!.PoliceBelt", source);
        Assert.Contains("pursuit.Weapon_Baton", source);
        Assert.Contains("pursuit.Weapon_Taser", source);
        Assert.Contains("pursuit.Weapon_Gun", source);
        Assert.Contains("OcOfficerPursuitIssues.Read", source);
    }

    [Theory]
    [InlineData(false, false, false, true, true, true, "combat.pursuit.officer")]
    [InlineData(true, false, false, true, true, true, "combat.pursuit.officer.owner")]
    [InlineData(true, true, false, true, true, true, "combat.pursuit.officer.PoliceBelt")]
    [InlineData(true, true, true, false, true, true, "combat.pursuit.Weapon_Baton")]
    [InlineData(true, true, true, true, false, true, "combat.pursuit.Weapon_Taser")]
    [InlineData(true, true, true, true, true, false, "combat.pursuit.Weapon_Gun")]
    public void Diagnostic_names_each_callback_dependency_without_cascading_officer_failures(
        bool officer, bool owned, bool belt, bool baton, bool taser, bool gun, string issue)
    {
        Assert.Equal(new[] { issue }, OcOfficerPursuitIssues.Read(new(officer, owned, belt, baton, taser, gun)));
    }

    [Fact]
    public void Healthy_pursuit_has_no_warning_even_without_a_drawn_current_weapon()
    {
        Assert.Empty(OcOfficerPursuitIssues.Read(new(true, true, true, true, true, true)));
    }

    [Fact]
    public void Multiple_missing_references_remain_distinct_combat_warning_keys()
    {
        var issues = OcOfficerPursuitIssues.Read(new(true, true, false, false, false, false));
        Assert.Equal(new[] { "combat.pursuit.officer.PoliceBelt", "combat.pursuit.Weapon_Baton",
            "combat.pursuit.Weapon_Taser", "combat.pursuit.Weapon_Gun" }, issues);
        var parsed = OcOfficerDependencyWarnings.Problems($"dependencies: missing={string.Join(",", issues)}; active=True");
        Assert.Equal(issues, parsed);
    }

    [Fact]
    public void Pursuit_observations_warn_once_for_each_clone_and_problem_count_and_rearm_on_load()
    {
        var gate = new OcOfficerDependencyCheck();
        var warnings = new OcOfficerDependencyWarnings();
        var clone = new OcOfficerCloneRef(null, 178, 1);
        var reused = new OcOfficerCloneRef(null, 178, 2);
        var logged = new List<string>();
        var issues = OcOfficerPursuitIssues.Read(new(true, true, false, true, true, true));
        IReadOnlyList<string> Scan()
        {
            warnings.Observe(clone, issues, logged.Add);
            return Array.Empty<string>();
        }
        gate.Poll(false, 0, Scan, logged.Add);
        Assert.Empty(logged);
        gate.Poll(true, 0, Scan, logged.Add);
        gate.Poll(true, 1000, Scan, logged.Add);
        gate.Poll(true, 5000, Scan, logged.Add);
        Assert.Equal(new[] { "combat.pursuit.officer.PoliceBelt" }, logged);
        Assert.Equal("combat warnings=1; observations=2", warnings.CombatStatus(clone));
        warnings.Observe(reused, issues, logged.Add);
        Assert.Equal(2, logged.Count);
        gate.Reset(); warnings.Reset();
        gate.Poll(true, 5001, Scan, logged.Add);
        Assert.Equal(3, logged.Count);
        Assert.Equal("combat warnings=1; observations=1", warnings.CombatStatus(clone));
    }

    [Fact]
    public void Throwing_logger_consumes_pursuit_warning_and_cannot_spam()
    {
        var warnings = new OcOfficerDependencyWarnings();
        var clone = new OcOfficerCloneRef(null, 178, 1);
        var calls = 0;
        var issues = OcOfficerPursuitIssues.Read(new(false, false, false, true, true, true));
        void Log(string _) { calls++; throw new InvalidOperationException("logger"); }
        warnings.Observe(clone, issues, Log);
        warnings.Observe(clone, issues, Log);
        Assert.Equal(1, calls);
        Assert.Equal("combat warnings=1; observations=2", warnings.CombatStatus(clone));
    }

    private static string ReadNative()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "tools/OrganizedCrime/Runtime/Police/OcOfficerNativeLifetime.cs");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new InvalidOperationException("Worktree source not found.");
    }
}
