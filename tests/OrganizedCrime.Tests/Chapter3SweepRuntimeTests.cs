using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3SweepRuntimeTests
{
    [Fact]
    public void Coordinator_saves_active_interval_and_reload_restores_only_remaining_time()
    {
        var repo = new Repository(); var input = new Chapter3SweepInput(true, 359d, LocalPressureTier.Critical, false);
        using var service = Create(repo);
        service.WireSweeps(() => input, new Chapter3SweepTests.WarningNative());
        service.PollProductionDemand();
        input = input with { Minutes = 360d }; service.PollProductionDemand();
        input = input with { Minutes = 1200d }; service.OnSaveStart(); service.OnSaveComplete();
        Assert.Equal(1800d, repo.Stored!.Sweep!.ActiveEndMinutes);
        service.OnPreLoad(); service.OnLoadComplete(); service.PollProductionDemand();
        Assert.Equal(360d, service.State!.Sweep!.ActiveStartMinutes);
        Assert.Equal(1800d, service.State.Sweep.ActiveEndMinutes);
        input = input with { Minutes = 1800d }; service.PollProductionDemand();
        Assert.Null(service.State.Sweep); Assert.Equal(360d, service.State.LastSweepStartMinutes);
    }

    [Fact]
    public void Warning_receipt_survives_reload_and_unreadable_cover_defers_start()
    {
        var repo = new Repository(); var native = new Chapter3SweepTests.WarningNative();
        var input = new Chapter3SweepInput(true, 359d, LocalPressureTier.Critical, true);
        using var service = Create(repo); service.WireSweeps(() => input, native); service.PollProductionDemand();
        service.OnSaveStart(); service.OnSaveComplete(); service.OnPreLoad(); service.OnLoadComplete();
        input = input with { Minutes = 1800d, Protected = null }; service.PollProductionDemand();
        Assert.Null(service.State!.Sweep!.ActiveStartMinutes); Assert.Single(native.Messages);
        input = input with { Protected = true }; service.PollProductionDemand();
        Assert.True(service.State.Sweep.Small); Assert.Equal(1800d, service.State.Sweep.ActiveStartMinutes);
        Assert.Single(native.Messages);
    }

    [Fact]
    public void Console_dispatch_and_owner_gate_drive_actual_feature_without_adding_public_surface()
    {
        var input = new Chapter3SweepInput(true, 500d, LocalPressureTier.Critical, false);
        using var service = Create(new Repository()); service.WireSweeps(() => input, null);
        Assert.Contains("disabled", Assert.Single(Chapter3QaCommand.Execute(service, new[] { "sweep", "start" }, false)));
        service.PollProductionDemand(); Assert.Null(service.State!.Sweep!.ActiveStartMinutes);
        Assert.Contains("queued", Assert.Single(Chapter3QaCommand.Execute(service, new[] { "sweep", "start" }, true)));
        // QA can start an already-scheduled ordinary pending sweep immediately.
        service.PollProductionDemand(); Assert.Equal(500d, service.State.Sweep.ActiveStartMinutes);
        Assert.Contains(Chapter3QaCommand.Execute(service, new[] { "sweep", "status" }, true), s => s.Contains("active", StringComparison.Ordinal));
        Chapter3QaCommand.Execute(service, new[] { "sweep", "end" }, true); service.PollProductionDemand();
        Assert.Null(service.State.Sweep); Assert.Equal(500d, service.State.LastSweepStartMinutes);
        input = input with { IsHost = false };
        Assert.Contains("host", Assert.Single(Chapter3QaCommand.Execute(service, new[] { "sweep", "start" }, true)));
    }

    [Fact]
    public void Save_suspension_and_preload_discard_queued_QA_mutation()
    {
        using var service = Create(new Repository());
        service.WireSweeps(() => new(true, 500d, LocalPressureTier.Critical, false), null);
        Chapter3QaCommand.Execute(service, new[] { "sweep", "start" }, true);
        service.OnSaveStart(); service.PollProductionDemand(); Assert.Null(service.State!.Sweep);
        service.OnSaveComplete(); service.OnPreLoad(); service.OnLoadComplete(); service.PollProductionDemand();
        Assert.Null(service.State!.Sweep!.ActiveStartMinutes);
        Assert.Equal(1800d, service.State.Sweep.PlannedStartMinutes);
    }

    [Fact]
    public void Scene_change_freezes_warning_and_scheduling_until_reload()
    {
        var native = new Chapter3SweepTests.WarningNative();
        using var service = Create(new Repository()); service.WireSweeps(() => new(true, 500d, LocalPressureTier.Critical, true), native);
        service.SuspendSweeps(); service.PollProductionDemand();
        Assert.Null(service.State!.Sweep); Assert.Empty(native.Messages);
        Assert.Contains("refused", Assert.Single(Chapter3QaCommand.Execute(service, new[] { "sweep", "start" }, true)));
        service.OnPreLoad(); service.OnLoadComplete(); service.PollProductionDemand();
        Assert.Single(native.Messages); Assert.NotNull(service.State!.Sweep!.WarningReceipt);
    }

    private static Chapter3RuntimeService Create(Repository repo)
    {
        var service = new Chapter3RuntimeService(new Context(), repo, new Gate(), new Clock());
        service.OnPreLoad(); service.OnLoadComplete();
        Assert.Equal(Chapter3RuntimePhase.Active, service.Phase);
        return service;
    }

    private sealed class Context : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = new(Guid.Parse("44444444-4444-4444-4444-444444444444"), 1, "76561190000000001", Path.GetTempPath());
            return Release1StoryHostContextReadStatus.Ready;
        }
    }
    private sealed class Gate : IChapter3StoryGate { public bool IsEnvelopeSatisfied() => true; }
    private sealed class Clock : IChapter3DayClock
    {
        public event Action? DayPassed { add { } remove { } }
        public event Action? WeekPassed { add { } remove { } }
        public bool TryReadElapsedDays(out int elapsedDays) { elapsedDays = 0; return true; }
    }
    private sealed class Repository : IChapter3Repository, IChapter3SaveFolderBoundRepository
    {
        public string BoundSaveFolder => Path.GetFullPath(Path.GetTempPath());
        internal Chapter3State? Stored = new("76561190000000001", Chapter3Phase.Active, 0, 0, 0,
            CheckpointTargetCode: "barn", CheckpointTargetDay: 0);
        public Chapter3StoreLoadResult Load() => new(true, Chapter3StoreLoadStatus.Loaded,
            new Chapter3SaveEnvelope(Chapter3SaveCodec.CurrentSchemaVersion, Stored), Chapter3StoreFailureReason.None, "loaded");
        public Chapter3StoreUpdateResult Update(Chapter3State? state)
        {
            Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var write), write.Message);
            Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var read), read.Message);
            Stored = envelope!.State;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "stored");
        }
    }
}
