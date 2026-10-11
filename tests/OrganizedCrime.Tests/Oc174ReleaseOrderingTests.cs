using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc174ReleaseOrderingTests
{
    [Theory]
    [InlineData("profile change")]
    [InlineData("auto off/on")]
    [InlineData("save")]
    [InlineData("lockdown off")]
    [InlineData("knockout")]
    public void Lifecycle_unbinds_before_return_and_removal_before_replacement(string boundary)
    {
        using var f = new Chapter3PatrolServiceTests.Fixture();
        if (boundary == "lockdown off") f.Input = f.Input with { Lockdown = true, Minutes = 0d };
        f.Converge();
        var old = f.World.Bindings.Snapshot().Select(b => b.Officer).ToArray();
        Assert.Contains(f.World.Bindings.Snapshot(), b => b.Duty == OcPatrolDuty.Vehicle);
        f.World.Native.Events.Clear();
        switch (boundary)
        {
            case "profile change": f.Input = f.Input with { Tier = LocalPressureTier.Quiet }; break;
            case "auto off/on": Assert.True(f.Service.SetAuto(false).Ok); break;
            case "save": f.Service.OnSaveStart(); f.Roster.OnSaveStart(); break;
            case "lockdown off": f.Input = f.Input with { Lockdown = false }; break;
            case "knockout": f.World.Unavailable = "knockout"; break;
        }
        if (boundary == "knockout")
        {
            // Stop once the original live duties retire, before requesting unbound replacements.
            for (var i = 0; i < 80 && f.World.Bindings.Snapshot().Count > 0; i++) f.Step();
        }
        else f.Converge();
        Assert.Empty(f.World.Bindings.Snapshot());
        AssertReleaseOrder(f.World.Native.Events);
        Assert.Equal(old.Length, f.World.Native.Events.Count(e => e == "remove"));
        Assert.Single(f.World.Native.Events, e => e == "return");
        if (boundary == "auto off/on") Assert.True(f.Service.SetAuto(true).Ok);
        if (boundary == "save") { f.Roster.OnSaveComplete(); f.Service.OnSaveComplete(); }
        if (boundary == "knockout") f.World.Unavailable = null;
        f.Converge();
        Assert.DoesNotContain(f.World.Bindings.Snapshot(), b => old.Contains(b.Officer));
        AssertReleaseOrder(f.World.Native.Events);
        Assert.InRange(f.World.Native.FreeCars, 1, 3);
    }

    [Fact]
    public void Off_on_during_failed_vehicle_return_retries_stop_once_before_replacement()
    {
        using var f = new Chapter3PatrolServiceTests.Fixture();
        f.Converge();
        var old = f.World.Bindings.Snapshot().Select(b => b.Officer).ToArray();
        f.World.Native.Events.Clear();
        f.World.Native.FailReturn = true;
        f.Service.SetAuto(false); f.Converge();
        Assert.Equal(1, f.Roster.PendingRemovalCount);
        var starts = f.World.Native.Starts;
        f.Service.SetAuto(true); f.Step();
        Assert.Equal(starts, f.World.Native.Starts);
        Assert.DoesNotContain("return", f.World.Native.Events);
        f.World.Native.FailReturn = false;
        f.Converge();
        Assert.Equal(0, f.Roster.PendingRemovalCount);
        Assert.DoesNotContain(f.World.Bindings.Snapshot(), b => old.Contains(b.Officer));
        // Ordinary orphan cleanup must not repeat Stop after a failed return.
        Assert.Equal(old.Length, f.World.Native.Events.Count(e => e == "stop"));
        Assert.Single(f.World.Native.Events, e => e == "return");
    }

    [Fact]
    public void Auto_toggle_preserves_QA_vehicle_binding_and_its_lease()
    {
        using var f = new Chapter3PatrolServiceTests.Fixture();
        f.Service.SetAuto(false); f.Converge();
        Assert.True(f.Roster.QaPatrolHere(OcPatrolDuty.Vehicle).Ok);
        f.Roster.Update(); f.Roster.Update();
        var qa = Assert.Single(f.World.Bindings.Snapshot()).Officer;
        f.Service.SetAuto(true); f.Converge();
        f.Service.SetAuto(false); f.Converge();
        var remaining = Assert.Single(f.World.Bindings.Snapshot());
        Assert.Equal(qa, remaining.Officer);
        Assert.True(remaining.CarLeased);
    }

    [Fact]
    public void Clients_do_not_unbind_host_patrols_on_profile_or_auto_changes()
    {
        using var f = new Chapter3PatrolServiceTests.Fixture();
        f.Converge();
        var before = f.World.Bindings.Snapshot();
        f.World.Native.Events.Clear();
        f.World.Native.IsHost = false;
        f.Input = f.Input with { IsHost = false, Protected = true, Lockdown = false };
        Assert.False(f.Service.SetAuto(false).Ok);
        f.Converge();
        Assert.Equal(before, f.World.Bindings.Snapshot());
        Assert.Empty(f.World.Native.Events);
    }

    private static void AssertReleaseOrder(IEnumerable<string> events)
    {
        var stopping = false;
        foreach (var action in events)
        {
            switch (action)
            {
                case "stop": Assert.False(stopping); stopping = true; break;
                case "return": Assert.True(stopping); break;
                case "remove": Assert.True(stopping); stopping = false; break;
                case "start": Assert.False(stopping); break;
            }
        }
        Assert.False(stopping);
    }
}
