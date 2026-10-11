// OC-151 round 2: replacement coverage for the deleted record tests' QaBeatInvincible
// paths (D24). The record is gone; the beat-probe arming through the setter and the
// unwired refusal are surviving behavior, pinned here through the factory ctor.
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3BeatProbeArmingTests
{
    [Fact]
    public void Beat_probe_setter_arms_invincible_on()
    {
        var probe = new CompositionFakeBeatProbeWorld
        {
            IsHost = true,
            ReadInvincible = () => (true, false, string.Empty),
            SetInvincible = _ => (true, string.Empty),
        };
        var service = Create();
        service.BeatProbeWorld = probe;
        Activate(service);

        var lines = service.QaBeatInvincible(true);

        Assert.Equal(new[] { "Chapter 3 QA beat probe invincible: on." }, lines);
        Assert.Equal(new[] { true }, probe.SetInvincibleCalls);
    }

    [Fact]
    public void Unwired_beat_probe_refuses_invincible()
    {
        var service = Create();
        Activate(service);

        Assert.Contains(service.QaBeatInvincible(true), line => line.Contains("not wired up", StringComparison.Ordinal));
    }

    private static Chapter3RuntimeService Create() =>
        new(
            new CompositionFakeStoryContext(),
            _ => new CompositionFakeChapter3Repository(),
            new CompositionFakeGate { Satisfied = true },
            new CompositionFakeClock { ElapsedDays = 11 },
            _ => { });

    private static void Activate(Chapter3RuntimeService service)
    {
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnDayPassed();
        Assert.Equal(Chapter3RuntimePhase.Active, service.Phase);
    }
}
