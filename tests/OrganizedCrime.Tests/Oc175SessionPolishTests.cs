using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

[Collection("OC-151 composition scope")]
public sealed class Oc175SessionPolishTests
{
    [Fact]
    public void Entrance_warnings_survive_saves_and_failed_read_resets_and_rearm_on_load()
    {
        var lines = new List<string>();
        var world = new Il2CppChapter3RaidWorld(null!, () => true, () => null, null!, lines.Add);
        using var chapter = CreateChapter();
        chapter.WireRaidWorld(world);
        chapter.OnPreLoad(); chapter.OnLoadComplete();
        Assert.Equal(Chapter3RuntimePhase.Active, chapter.Phase);
        world.ReportEntranceUnavailable("rv");
        world.ReportEntranceUnavailable("dockswarehouse");
        world.ReportEntranceUnavailable("rv");
        Assert.Equal(2, lines.Count);
        chapter.OnSaveStart(); chapter.OnSaveComplete();
        world.ReportEntranceUnavailable("rv");
        world.Reset(); // Failed read/preview reset, independent of save lifecycle.
        world.ReportEntranceUnavailable("dockswarehouse");
        Assert.Equal(2, lines.Count);
        chapter.OnPreLoad(); chapter.OnLoadComplete();
        world.ReportEntranceUnavailable("rv");
        world.ReportEntranceUnavailable("dockswarehouse");
        Assert.Equal(4, lines.Count);
        Assert.All(lines, line => Assert.Contains("occupancy blocked, retrying every 30 seconds", line));
    }

    [Fact]
    public void Chapter_status_explains_rewind_and_only_reload_releases_quarantine()
    {
        using var host = new Oc175HeatQaTests.Host { Minutes = 180 };
        var repo = new Oc175HeatQaTests.Repository(new(65, false, 1d, 2d, 3d, "player-1", "north", "home", 7));
        using var pressure = new LocalPressureRuntimeService(host, _ => repo);
        Oc175HeatQaTests.Load(pressure);
        using var chapter = CreateChapter();
        chapter.WirePressureStatus(pressure.QaStatus);
        chapter.OnPreLoad(); chapter.OnLoadComplete();
        host.Minutes = 120;
        pressure.OnClockBoundary(LocalPressureClockBoundary.Hour);
        Assert.Equal(LocalPressureRuntimePhase.Quarantined, pressure.Phase);
        Assert.Contains(Chapter3QaCommand.Execute(chapter, new[] { "status" }, true),
            line => line.Contains("heat paused: clock rewound; save and reload") && line.Contains("D4 forced Wanted"));
        Assert.False(pressure.TryGetActiveEvidenceSnapshot(out _));
        Assert.False(pressure.QaSetTier(LocalPressureTier.Critical).Ok);
        pressure.PumpReadiness();
        pressure.OnSaveStart(); pressure.OnSaveComplete();
        Assert.Equal(LocalPressureRuntimePhase.Quarantined, pressure.Phase);
        Assert.Equal(0, repo.Writes);
        Assert.Contains(pressure.QaStatus(), line => line.Contains("clock rewound"));
        Oc175HeatQaTests.Load(pressure);
        Assert.True(pressure.TryGetActiveEvidenceSnapshot(out _));
        Assert.DoesNotContain(Chapter3QaCommand.Execute(chapter, new[] { "status" }, true),
            line => line.Contains("clock rewound"));
    }

    [Theory]
    [InlineData(LocalPressureClockReadStatus.Pending)]
    [InlineData(LocalPressureClockReadStatus.Faulted)]
    public void Heat_QA_refuses_an_unavailable_current_clock_without_changing_the_record(LocalPressureClockReadStatus status)
    {
        using var host = new Oc175HeatQaTests.Host { Minutes = 180 };
        var old = new LocalPressureState(65, false, 1d, 2d, 3d, "player-1", "north", "home", 7);
        var repo = new Oc175HeatQaTests.Repository(old);
        using var pressure = new LocalPressureRuntimeService(host, _ => repo);
        Oc175HeatQaTests.Load(pressure);
        host.ClockStatus = status;
        Assert.False(pressure.QaSetTier(LocalPressureTier.Critical).Ok);
        Assert.True(pressure.TryGetState("player-1", out var after));
        Assert.Equal(old, after);
        Assert.Equal(0, repo.Writes);
    }

    internal static Chapter3RuntimeService CreateChapter() => new(new Context(), new Repository(), new Gate(), new Clock());
    private sealed class Context : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), 1, "player-1", Path.GetTempPath());
            return Release1StoryHostContextReadStatus.Ready;
        }
    }
    private sealed class Repository : IChapter3Repository, IChapter3SaveFolderBoundRepository
    {
        public string BoundSaveFolder => Path.GetFullPath(Path.GetTempPath());
        private Chapter3State? _state;
        public Chapter3StoreLoadResult Load() => _state is null
            ? new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "empty")
            : new(true, Chapter3StoreLoadStatus.Loaded, new(Chapter3SaveCodec.CurrentSchemaVersion, _state), Chapter3StoreFailureReason.None, "loaded");
        public Chapter3StoreUpdateResult Update(Chapter3State? state)
        {
            _state = state;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "stored");
        }
    }
    private sealed class Gate : IChapter3StoryGate { public bool IsEnvelopeSatisfied() => true; }
    private sealed class Clock : IChapter3DayClock
    {
        public event Action? DayPassed { add { } remove { } }
        public event Action? WeekPassed { add { } remove { } }
        public bool TryReadElapsedDays(out int elapsedDays) { elapsedDays = 0; return true; }
    }
}