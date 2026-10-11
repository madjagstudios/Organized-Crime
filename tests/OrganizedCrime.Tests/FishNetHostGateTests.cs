using OrganizedCrime.Runtime.Platform;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-147 fix round: the Il2Cpp worlds read FishNet's ServerManager twice
// (null-check on the first read, started-read on the second), so a null or
// throwing second read maps to false. DoubleRead pins that evaluation shape
// without the engine; production passes FishNet's static through it unchanged.
public sealed class FishNetHostGateTests
{
    private sealed class FakeManager
    {
        public bool Started { get; set; }
    }

    [Fact]
    public void DoubleRead_returns_started_and_reads_twice_when_present()
    {
        var manager = new FakeManager { Started = true };
        var reads = 0;
        var startedCalls = 0;

        var result = FishNetHostGate.DoubleRead(
            () => { reads++; return manager; },
            candidate => { startedCalls++; return candidate.Started; });

        Assert.True(result);
        Assert.Equal(2, reads);
        Assert.Equal(1, startedCalls);
    }

    [Fact]
    public void DoubleRead_returns_false_when_not_started()
    {
        var manager = new FakeManager { Started = false };
        var reads = 0;

        var result = FishNetHostGate.DoubleRead(
            () => { reads++; return manager; },
            candidate => candidate.Started);

        Assert.False(result);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void DoubleRead_short_circuits_on_first_null()
    {
        var reads = 0;
        var startedCalls = 0;

        var result = FishNetHostGate.DoubleRead(
            () => { reads++; return (FakeManager?)null; },
            candidate => { startedCalls++; return candidate.Started; });

        Assert.False(result);
        Assert.Equal(1, reads);
        Assert.Equal(0, startedCalls);
    }

    [Fact]
    public void DoubleRead_returns_false_when_second_read_is_null()
    {
        var manager = new FakeManager { Started = true };
        var queued = new Queue<FakeManager?>(new FakeManager?[] { manager, null });
        var reads = 0;

        var result = FishNetHostGate.DoubleRead(
            () => { reads++; return queued.Dequeue(); },
            candidate => candidate.Started);

        Assert.False(result);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void DoubleRead_returns_false_when_first_read_throws()
    {
        var reads = 0;

        var result = FishNetHostGate.DoubleRead<FakeManager>(
            () => { reads++; throw new InvalidOperationException("first read fault"); },
            candidate => candidate.Started);

        Assert.False(result);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void DoubleRead_returns_false_when_second_read_throws()
    {
        var manager = new FakeManager { Started = true };
        var reads = 0;

        var result = FishNetHostGate.DoubleRead(
            () =>
            {
                reads++;
                return reads == 1 ? manager : throw new InvalidOperationException("second read fault");
            },
            candidate => candidate.Started);

        Assert.False(result);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void DoubleRead_returns_false_when_started_read_throws()
    {
        var manager = new FakeManager { Started = true };
        var reads = 0;

        var result = FishNetHostGate.DoubleRead(
            () => { reads++; return manager; },
            candidate => throw new InvalidOperationException("started read fault"));

        Assert.False(result);
        Assert.Equal(2, reads);
    }
}
