using System.Text.RegularExpressions;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc178ReviewFollowupTests
{
    [Fact]
    public void Punch_binding_exemption_is_scoped_to_inactive_root()
    {
        var source = Read("OcOfficerNativeLifetime.cs");
        Assert.Matches(@"if\s*\(!root\.activeInHierarchy\)\s*\{\s*if \(item == punch\) continue;\s*\}", source);
        Assert.Contains("equipment.Add(new(item.Pointer.ToString(), item.avatar == avatar && UnityLife.IsAlive(item.avatar)", source);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void Active_punch_binding_flows_to_existing_warn_once_diagnostic(bool bound, int expected)
    {
        var issues = OcOfficerDependencyIssues.Read(new(true, true, true, true, true),
            new[] { new OcEquippableDependency("punch", bound, true) });
        var warnings = new OcOfficerDependencyWarnings();
        var lines = new List<string>();
        var clone = new OcOfficerCloneRef(null, 123, 1);
        warnings.Observe(clone, issues, lines.Add);
        warnings.Observe(clone, issues, lines.Add);
        Assert.Equal(expected, lines.Count);
        if (!bound) Assert.Equal(new[] { "equippable.avatar@punch" }, lines);
    }

    [Fact]
    public void Container_rejection_remains_fatal_when_warning_sink_throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            OcOfficerCloneLifetime.RejectCleanup("combat container", _ => throw new Exception("logger")));
        Assert.Equal("combat container", error.Message);
    }

    [Fact]
    public void Cleanup_warning_is_visible_in_normal_mode_once_per_clone_and_reason_and_resets()
    {
        var lines = new List<string>();
        var policy = new OcLoggingPolicy(_ => throw new Exception("information"), lines.Add);
        var warnings = new OcOfficerCleanupWarnings();
        void Reject(string clone, string reason) => Assert.Throws<InvalidOperationException>(() =>
            OcOfficerCloneLifetime.RejectCleanup(reason, message => warnings.Observe(clone, message, policy.Warning)));
        Reject("clone-1", "combat container");
        Reject("clone-1", "combat container");
        Reject("clone-1", "shared object");
        Reject("clone-2", "combat container");
        Assert.Equal(3, lines.Count);
        Assert.All(lines, line => Assert.DoesNotContain("\n", line));
        Assert.Contains("clone=clone-1 reason=combat container", lines[0]);
        Assert.Contains("clone=clone-1 reason=shared object", lines[1]);
        Assert.Contains("clone=clone-2 reason=combat container", lines[2]);
        warnings.Reset();
        Reject("clone-1", "combat container");
        Assert.Equal(4, lines.Count);
    }

    [Fact]
    public void Both_copy_adapters_wire_rejection_warning_and_load_reset()
    {
        Assert.Contains("OcOfficerNativeLifetime.StripCopies(templateRoot, officers[0]);", Read("OcOfficerTemplateBuilder.cs"));
        Assert.Contains("OcOfficerNativeLifetime.StripCopies(instance, officer)", Read("OcOfficerSpawner.cs"));
        Assert.Contains("OcOfficerNativeLifetime.ResetCleanupWarnings()", Read("OcOfficerDiagnostics.cs"));
        var strip = Read("OcOfficerNativeLifetime.cs").Split("internal static void BindDependencies")[0];
        Assert.Contains("CleanupWarnings.Observe(root.name, reason, OrganizedCrimeLog.Warning)", strip);
        Assert.Equal(2, Regex.Matches(strip, "OcOfficerCloneLifetime.RejectCleanup").Count);
        Assert.True(strip.IndexOf("RejectCleanup", StringComparison.Ordinal) < strip.IndexOf("Object.DestroyImmediate", StringComparison.Ordinal));
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
