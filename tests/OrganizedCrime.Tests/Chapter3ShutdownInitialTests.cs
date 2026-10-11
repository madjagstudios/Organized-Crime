using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ShutdownInitialTests
{
    [Fact]
    public void Current_schema_is_twenty() => Assert.Equal(20, Chapter3SaveCodec.CurrentSchemaVersion);

    [Fact]
    public void Console_routes_shutdown_offer_to_host_queue()
    {
        var h = new Chapter3CashRuntimeTests.Harness();
        using var service = new Chapter3RuntimeService(h.Host, new Repository(), new Gate(), new Clock());
        Assert.Contains("shutdown", Assert.Single(Chapter3QaCommand.Execute(service, new[] { "offer", "shutdown" }, true)));
    }
    private sealed class Gate : IChapter3StoryGate { public bool IsEnvelopeSatisfied() => true; }
    private sealed class Clock : IChapter3DayClock
    {
        public event Action? DayPassed { add { } remove { } }
        public event Action? WeekPassed { add { } remove { } }
        public bool TryReadElapsedDays(out int days) { days = 0; return true; }
    }
    private sealed class Repository : IChapter3Repository
    {
        public Chapter3StoreLoadResult Load() => new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "");
        public Chapter3StoreUpdateResult Update(Chapter3State? state) => new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "");
    }
}
