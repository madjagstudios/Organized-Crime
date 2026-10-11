using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;
using FakeWorld = OrganizedCrime.Tests.Chapter3DeadDropRuntimeTests.FakeWorld;

namespace OrganizedCrime.Tests;

public sealed class Chapter3StorageServiceTests
{
    [Fact]
    public void Console_to_service_success_reload_reprompt_and_every_save_write()
    {
        var world = new FakeWorld { CanonicalMinutes = 16_000 };
        world.SeedProducts(new Release1SmallCourtesyProductCandidate("rich", "Rich", 100, true));
        world.SeedDrops(new("a", "A", "Drop A", 1, 2, 3, true), new("b", "B", "Drop B", 4, 5, 6, true));
        world.SeedSlots("a", Chapter3StorageRuntimeTests.Slot(0));
        world.SeedSlots("b", Chapter3StorageRuntimeTests.Slot(0));
        var repository = new Repository();
        var clock = new Clock();
        var room = EmptyRoom();
        var context = new Context();
        Chapter3RuntimeService Create()
        {
            var service = new Chapter3RuntimeService(context, repository, new Gate(), clock)
            {
                DeadDropWorld = world, Wallet = new Chapter3Wallet(world)
            };
            service.WireStorageRoom(() => room, () => new Release1WorldPoint(10, 20, 30));
            service.OnPreLoad(); service.OnLoadComplete();
            return service;
        }
        var current = Create();
        current.OnDayPassed();
        Assert.Contains("queued", Assert.Single(Chapter3QaCommand.Execute(current, new[] { "offer", "storage" }, true)));
        current.PollEmergencyExpiry();
        Assert.Contains("storage run", current.GetPresentationPlan(false).Decision!.Prompt);
        Assert.Contains("accepted", Assert.Single(Chapter3QaCommand.Execute(current, new[] { "accept" }, true)));
        var run = current.State!.JobBoard.ActiveJob!.StorageRun!;
        Assert.Equal(new[] { run.Route.Pickup.Guid }, world.DropClosedCallbacks.Keys);
        world.SeedSlots(run.Route.Pickup.Guid, Chapter3StorageRuntimeTests.Slot(0));
        world.FireDropClosed(run.Route.Pickup.Guid);
        Assert.True(current.State.JobBoard.ActiveJob.StorageRun!.Route.Custody);
        var closets = room.Closets.ToArray();
        closets[0] = closets[0] with { Slots = new[] { Chapter3StorageRuntimeTests.Slot(run.Route.BrickCount) } };
        room = room with { Closets = closets };
        current.PollEmergencyExpiry();
        Assert.Equal(16_000d, current.State.JobBoard.ActiveJob.StorageRun!.StowedAt);
        Assert.NotNull(current.GetPresentationPlan(false).Quest!.Entries[1].Marker);
        current.OnSaveStart(); current.OnSaveComplete();
        current.OnSaveStart(); current.OnSaveComplete();
        Assert.Equal(2, repository.Writes);
        Assert.Contains("\"schemaVersion\": 20", repository.Json!);
        current.Dispose();
        world.CanonicalMinutes = 16_200;
        current = Create();
        Assert.Equal(16_000d, current.State!.JobBoard.ActiveJob!.StorageRun!.StowedAt);
        Assert.Equal(new[] { run.Route.Handoff.Guid }, world.DropClosedCallbacks.Keys);
        var status = Chapter3QaCommand.Execute(current, new[] { "status" }, true);
        Assert.Contains(status, line => line.Contains("stage hold"));
        Assert.Contains(status, line => line.Contains("stow time 16000"));
        Chapter3QaCommand.Execute(current, new[] { "storage", "hold", "done" }, true);
        current.PollEmergencyExpiry();
        Assert.True(current.State.JobBoard.ActiveJob.StorageRun!.Released);
        world.SeedSlots(run.Route.Handoff.Guid, Chapter3StorageRuntimeTests.Slot(run.Route.BrickCount));
        world.FireDropClosed(run.Route.Handoff.Guid);
        Assert.Null(current.State.JobBoard.ActiveJob);
        Assert.Single(world.CashChanges);
        Assert.Single(world.QuantityChanges);
        current.Dispose();
    }

    [Fact]
    public void Host_only_console_force_refuses_a_client()
    {
        var service = new Chapter3RuntimeService(new Context { Host = false }, new Repository(), new Gate(), new Clock());
        service.OnPreLoad(); service.OnLoadComplete();
        var lines = Chapter3QaCommand.Execute(service, new[] { "offer", "storage" }, true);
        Assert.Contains("refused", Assert.Single(lines));
        Assert.Null(service.State);
        service.Dispose();
    }

    private static Release1HoldRoomSnapshot EmptyRoom() =>
        new(Release1HoldRoomReadiness.Ready, SyndicateHqStorageContract.Definitions.Select(d =>
            new Release1HoldRoomClosetSnapshot(d.Guid.ToString(), 1, new[] { Chapter3StorageRuntimeTests.Slot(0) })).ToArray());

    private sealed class Context : IRelease1StoryHostContext
    {
        internal bool Host = true;
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = new(Guid.Parse("66666666-6666-6666-6666-666666666666"), 1, "player", System.IO.Path.GetTempPath());
            return Host ? Release1StoryHostContextReadStatus.Ready : Release1StoryHostContextReadStatus.NotAuthoritative;
        }
    }
    private sealed class Gate : IChapter3StoryGate { public bool IsEnvelopeSatisfied() => true; }
    private sealed class Clock : IChapter3DayClock
    {
        public event Action? DayPassed { add { } remove { } }
        public event Action? WeekPassed { add { } remove { } }
        public bool TryReadElapsedDays(out int days) { days = 11; return true; }
    }
    private sealed class Repository : IChapter3Repository, IChapter3SaveFolderBoundRepository
    {
        public string BoundSaveFolder => System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        internal string? Json;
        internal int Writes;
        public Chapter3StoreLoadResult Load()
        {
            if (Json is null) return new(true, Chapter3StoreLoadStatus.Empty, Chapter3SaveEnvelope.CreateEmpty(), Chapter3StoreFailureReason.None, "");
            Assert.True(Chapter3SaveCodec.TryDeserialize(Json, out var envelope, out var result), result.Message);
            return new(true, Chapter3StoreLoadStatus.Loaded, envelope, Chapter3StoreFailureReason.None, "");
        }
        public Chapter3StoreUpdateResult Update(Chapter3State? state)
        {
            Writes++;
            Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var result), result.Message);
            Json = json;
            return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "");
        }
    }
}
