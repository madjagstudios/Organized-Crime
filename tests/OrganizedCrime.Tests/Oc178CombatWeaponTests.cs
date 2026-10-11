using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc178CombatWeaponTests
{
    [Theory]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, true, false, false, true)]
    [InlineData(true, true, true, false, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, false, false, false)]
    public void Strip_retains_required_combat_weapon_even_before_native_Start(
        bool owned, bool alive, bool bound, bool combat, bool stripped)
    {
        var result = OcOfficerCloneLifetime.StaleCopies(new[] { 1 }, _ => owned, _ => alive,
            _ => bound, _ => combat);
        Assert.Equal(stripped, result.Count == 1);
    }

    [Fact]
    public void Required_virtual_weapon_survives_both_template_and_clone_cleanup()
    {
        var items = new[] { "virtual punch", "stale flashlight", "bound baton", "native gun" };
        IReadOnlyList<string> Strip(IEnumerable<string> copies) => OcOfficerCloneLifetime.StaleCopies(copies,
            item => item != "native gun", _ => true, item =>
            {
                Assert.NotEqual("native gun", item);
                Assert.NotEqual("virtual punch", item);
                return item == "bound baton";
            }, item => item == "virtual punch");
        var templateRemoved = Strip(items);
        Assert.Equal(new[] { "stale flashlight" }, templateRemoved);
        var template = items.Except(templateRemoved).ToArray();
        Assert.Empty(Strip(template));
        Assert.Contains("virtual punch", template);
    }

    [Fact]
    public void Adapter_preserves_owned_virtual_weapon_and_container_before_both_activations()
    {
        var native = Read("OcOfficerNativeLifetime.cs");
        var strip = native[..native.IndexOf("internal static void BindDependencies", StringComparison.Ordinal)];
        Assert.Contains("OwnedCombat(root, officer)", strip);
        Assert.Contains("item => item == punch", strip);
        Assert.Contains("Inside(go, punch)", strip);
        Assert.DoesNotContain(".Equip(", strip);
        Assert.DoesNotContain(".Unequip(", strip);
        var template = Read("OcOfficerTemplateBuilder.cs");
        Assert.True(template.IndexOf("StripCopies(templateRoot", StringComparison.Ordinal) <
            template.IndexOf("spawnables.AddObject", StringComparison.Ordinal));
        var spawn = Read("OcOfficerSpawner.cs");
        Assert.True(spawn.IndexOf("StripCopies(instance", StringComparison.Ordinal) <
            spawn.IndexOf("WakeTargets(instance", StringComparison.Ordinal));
        Assert.True(spawn.IndexOf("WakeTargets(instance", StringComparison.Ordinal) <
            spawn.IndexOf("instance.SetActive(true)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Idle_and_drawn_owned_weapons_have_no_combat_failure(bool drawn)
    {
        Assert.Empty(OcOfficerCombatIssues.Read(new(true, true, true, drawn, drawn, drawn)));
    }

    [Theory]
    [InlineData(false, true, true, false, false, false, "combat.behaviour")]
    [InlineData(true, false, false, false, false, false, "combat.VirtualPunchWeapon")]
    [InlineData(true, true, false, false, false, false, "combat.VirtualPunchWeapon.owner")]
    [InlineData(true, true, true, true, false, true, "combat.currentWeapon.owner")]
    [InlineData(true, true, true, true, true, false, "combat.currentWeapon.avatar")]
    public void Combat_diagnostic_names_the_failed_prerequisite(bool behaviour, bool punch, bool punchOwned,
        bool current, bool currentOwned, bool currentAvatar, string issue)
    {
        Assert.Equal(new[] { issue }, OcOfficerCombatIssues.Read(new(behaviour, punch, punchOwned,
            current, currentOwned, currentAvatar)));
    }

    [Fact]
    public void Missing_virtual_fallback_is_reported_even_with_live_current_weapon()
    {
        Assert.Equal(new[] { "combat.VirtualPunchWeapon" },
            OcOfficerCombatIssues.Read(new(true, false, false, true, true, true)));
    }

    [Fact]
    public void Combat_observations_are_host_only_warn_once_counted_and_reset_per_load()
    {
        var gate = new OcOfficerDependencyCheck();
        var warnings = new OcOfficerDependencyWarnings();
        var clone = new OcOfficerCloneRef(null, 123, 1);
        var logged = new List<string>();
        var reads = 0;
        IReadOnlyList<string> ReadIssues()
        {
            reads++;
            warnings.Observe(clone, OcOfficerCombatIssues.Read(new(true, false, false, false, false, false)), logged.Add);
            return Array.Empty<string>();
        }
        gate.Poll(false, 0, ReadIssues, logged.Add);
        Assert.Equal(0, reads);
        Assert.Equal("combat warnings=0; observations=0", warnings.CombatStatus(clone));
        gate.Poll(true, 0, ReadIssues, logged.Add);
        gate.Poll(true, 1000, ReadIssues, logged.Add);
        gate.Poll(true, 5000, ReadIssues, logged.Add);
        gate.Poll(true, 10000, ReadIssues, logged.Add);
        Assert.Equal(3, reads);
        Assert.Equal(new[] { "combat.VirtualPunchWeapon" }, logged);
        Assert.Equal("combat warnings=1; observations=3", warnings.CombatStatus(clone));
        warnings.Observe(clone, new[] { "movement.agent" }, logged.Add);
        Assert.Equal("combat warnings=1; observations=3", warnings.CombatStatus(clone));
        Assert.Equal("combat warnings=0; observations=0", warnings.CombatStatus(new(null, 123, 2)));
        gate.Reset(); warnings.Reset();
        Assert.Equal("combat warnings=0; observations=0", warnings.CombatStatus(clone));
        gate.Poll(true, 10001, ReadIssues, logged.Add);
        Assert.Equal(3, logged.Count);
    }

    [Fact]
    public void Adapter_skips_pre_Start_virtual_avatar_check_and_publishes_combat_counts()
    {
        var native = Read("OcOfficerNativeLifetime.cs");
        Assert.Contains("if (item == punch) continue;", native);
        Assert.Contains("missing.AddRange(OcOfficerCombatIssues.Read(combatState))", native);
        Assert.Contains("combat!.currentWeapon", native);
        var diagnostics = Read("OcOfficerDiagnostics.cs");
        Assert.Contains("_dependencyWarnings.CombatStatus(clone)", diagnostics);
        Assert.Contains("_dependencies.Poll(_context.IsHost", diagnostics);
    }

    [Fact]
    public void Required_combat_members_are_public_interop_properties()
    {
        var combat = typeof(Il2CppScheduleOne.Combat.CombatBehaviour);
        Assert.NotNull(combat.GetProperty("VirtualPunchWeapon"));
        Assert.NotNull(combat.GetProperty("currentWeapon"));
        Assert.Equal(typeof(Il2CppScheduleOne.AvatarFramework.Equipping.AvatarMeleeWeapon),
            combat.GetProperty("VirtualPunchWeapon")!.PropertyType);
    }

    private static string Read(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "tools/OrganizedCrime/Runtime/Police", file);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new InvalidOperationException("Worktree source not found.");
    }
}
