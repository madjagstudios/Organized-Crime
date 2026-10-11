using System.Reflection;
using System.Text.Json.Nodes;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using S1API.Items;
using S1API.Storages;
using Xunit;

namespace OrganizedCrime.Tests;

[Collection("OC-151 composition scope")]
public sealed class Chapter3RaidLifecycleTests
{
    [Fact]
    public void Installed_321_API_exposes_the_quantity_write_and_storage_reads_used_by_raids()
    {
        Assert.Equal(typeof(void), typeof(ItemSlotInstance).GetMethod("AddQuantity", new[] { typeof(int) })!.ReturnType);
        Assert.Equal(typeof(ItemSlotInstance[]), typeof(StorageInstance).GetProperty("Slots")!.PropertyType);
        Assert.NotNull(typeof(StorageInstance).GetMethod("FromGameObject", new[] { typeof(UnityEngine.GameObject) }));
        Assert.StartsWith("3.2.1+a4cbdd9e", typeof(ItemSlotInstance).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        Assert.NotNull(typeof(Il2CppScheduleOne.ItemFramework.ItemSlot).GetMethod("SetIsRemovalLocked"));
        Assert.Equal(typeof(int), typeof(ItemSlotInstance).GetProperty("Quantity")!.PropertyType);
        foreach (var name in new[] { "Local", "IsInVehicle", "CurrentProperty", "CrimeData", "IsArrested" })
            Assert.NotNull(typeof(S1API.Entities.Player).GetProperty(name));
        Assert.Equal(typeof(S1API.Law.PursuitLevel), typeof(S1API.Law.PlayerCrimeData).GetProperty("CurrentPursuitLevel")!.PropertyType);
        Assert.True(Enum.IsDefined(S1API.Law.PursuitLevel.Arresting));
    }

    [Fact]
    public void Coordinator_freezes_before_save_restores_receipts_and_sends_Nell_notice_once()
    {
        var repo = new ChapterRepository(); var world = new Chapter3RaidSeizureTests.World();
        var input = new Chapter3RaidInput(true, 1000, LocalPressureTier.Watched, false, false);
        var cooling = new List<string>();
        using var service = Create(repo);
        service.WireRaidWorld(world); service.WireRaids(() => input, id => { cooling.Add(id); return true; });
        var native = new Chapter3SweepTests.WarningNative();
        using var presenter = new Chapter3PresentationProjector(service, () => service.GetPresentationPlan(false), native, new Chapter3PresentationCommandRouter(service));
        Assert.Contains("queued", Assert.Single(Chapter3QaCommand.Execute(service, new[] { "raid", "now", "a", "force-away" }, true)));
        service.PollProductionDemand(); service.PollProductionDemand(); Assert.Single(world.Writes);
        var id = service.State!.Raid!.Id;
        presenter.OnSaveStart(); service.OnSaveStart();
        for (var i = 0; i < 4; i++) service.PollProductionDemand();
        Assert.Single(world.Writes); Assert.False(((IChapter3RaidSeizureQaTarget)service).RaidNow("a", true).Ok);
        service.OnSaveComplete(); presenter.OnSaveComplete();
        Assert.Equal(5, repo.Stored!.Raid!.ActualUnits);
        service.OnPreLoad(); presenter.OnPreLoad(); service.OnLoadComplete(); presenter.OnLoadComplete();
        world.Restored = false; service.PollProductionDemand(); Assert.Single(world.Writes);
        world.Restored = true;
        for (var i = 0; i <= 11; i++) { service.PollProductionDemand(); input = input with { Minutes = input.Minutes + 1 }; }
        presenter.Reconcile(); presenter.Reconcile();
        Assert.Equal(id, service.State!.Raid!.Id); Assert.Equal(10, service.State.Raid.ActualUnits);
        Assert.Equal(2, world.Writes.Count); Assert.Single(cooling);
        Assert.Equal("Police hit Site A. They took 10 units.", Assert.Single(native.Messages, m => m.StartsWith("Police hit")));
        Assert.Equal(service.State.Raid.NoticeCorrelation(service.State.PlayerId), service.State.Raid.NoticeReceipt);
        service.OnSaveStart(); service.OnSaveComplete(); service.OnPreLoad(); presenter.OnPreLoad(); service.OnLoadComplete(); presenter.OnLoadComplete();
        service.PollProductionDemand(); presenter.Reconcile();
        Assert.Equal(2, world.Writes.Count); Assert.Single(cooling);
        Assert.Single(native.Messages, m => m.StartsWith("Police hit"));
        Assert.Contains(Chapter3QaCommand.Execute(service, new[] { "raid", "status" }, true), s => s.Contains("notice sent=True"));
        Assert.Contains("Police have hit", service.GetOvernightLineForSleep());
    }

    [Fact]
    public void Now_honors_cover_and_cap_force_away_is_only_an_occupancy_override()
    {
        var h = new Chapter3RaidSeizureTests.Harness();
        h.Input = h.Input with { Minutes = 100, Tier = LocalPressureTier.Quiet, Protected = true };
        h.World.Presence = Chapter3RaidPresence.Inside;
        Assert.True(h.Feature.Queue("a", true).Ok); h.Poll(); Assert.Null(h.State.Raid);
        h.Input = h.Input with { Protected = false };
        h.Context.State = h.State with { LastRaidSuccessMinutes = 0 };
        h.Feature.Queue("a", true); h.Poll(); Assert.Null(h.State.Raid);
        h.Feature.SkipCap(); h.Feature.Queue("a", true); h.Poll(); h.Poll(); h.Poll();
        Assert.Equal(10, h.State.Raid!.ActualUnits);
        h.Context.State = h.State with { Raid = h.State.Raid with { NoticeReceipt = h.State.Raid.NoticeCorrelation(h.State.PlayerId) } };
        h.Feature.Queue("a", true); h.Poll(); Assert.Equal(2, h.World.Writes.Count);
    }

    [Fact]
    public void Cooling_runtime_persists_marker_every_save_and_preserves_native_record_and_pursuit()
    {
        var old = new LocalPressureState(90, true, 1, 2, 3, "player-1", "north", "home", 7);
        var repo = new PressureRepository(old);
        using var adapter = new Host(); using var pressure = new LocalPressureRuntimeService(adapter, _ => repo);
        pressure.OnPreLoad(); pressure.OnLoadComplete(); pressure.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        Assert.True(pressure.TryApplyRaidCooling("raid-id"));
        Assert.True(pressure.TryGetState("player-1", out var cooled));
        Assert.Equal(LocalPressureProfile.Moderate.WatchedUpperBound, cooled!.LocalHeat);
        Assert.True(cooled.KnownOffender); Assert.Equal(old.LastEvidenceGameTime, cooled.LastEvidenceGameTime);
        Assert.Equal(old.QuietGraceUntil, cooled.QuietGraceUntil); Assert.Equal(old.LastDecayEvaluation, cooled.LastDecayEvaluation);
        Assert.Equal(old.Region, cooled.Region); Assert.Equal(old.PropertyCode, cooled.PropertyCode);
        var revision = cooled.Revision;
        Assert.True(pressure.TryApplyRaidCooling("raid-id")); pressure.TryGetState("player-1", out var again);
        Assert.Equal(revision, again!.Revision);
        pressure.OnSaveStart(); Assert.False(pressure.TryApplyRaidCooling("other")); pressure.OnSaveComplete();
        pressure.OnPreLoad(); pressure.OnLoadComplete(); pressure.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        Assert.True(pressure.TryApplyRaidCooling("raid-id")); pressure.TryGetState("player-1", out var reloaded);
        Assert.Equal(revision, reloaded!.Revision);
        pressure.OnSaveStart(); pressure.OnSaveComplete(); Assert.Equal(2, repo.Writes);
        Assert.Equal("raid-id", repo.Record.LastAppliedRaidCoolingId);
        Assert.Equal(2, adapter.NativeCrimes.Count); Assert.Equal(3, adapter.NativePursuit);
        adapter.Authoritative = false; Assert.False(pressure.TryApplyRaidCooling("client-raid"));
    }

    [Fact]
    public void Cooling_keeps_unpaid_Chief_lockdown_and_debt_when_Chief_reconciles()
    {
        var context = new Release1SmallCourtesyDepositTests.FakeContext();
        using var story = new Release1StoryRuntimeService(context, new Release1SmallCourtesyDepositTests.FakeRepository());
        story.OnPreLoad(); story.OnLoadComplete();
        const string receipt = "oc173-intro";
        var correlation = Release1LogicalCorrelation.Create(context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
        Assert.True(story.TryExecuteDurably(new(context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt, correlation)).Accepted);
        var record = Release1ChiefRecord.Adopt(LocalPressureTier.Critical, false) with
        { State = Release1ChiefState.DemandOpen, DemandRound = 2, LockdownEngaged = true, LockdownAnnouncements = 1, LockdownEngagedAtMinutes = 3000 };
        Assert.True(story.TrySetChiefRecord(record).Accepted);
        var cooled = LocalPressureTransitions.ApplyRaidCooling(new(90, true, 1, 2, 3, context.Snapshot.PlayerId, null, null, 7), LocalPressureProfile.Moderate, "raid-id");
        var world = new ChiefWorld();
        using var chief = new Release1ChiefService(story, world, new Release1ChiefTierObserver(), _ => cooled,
            _ => throw new InvalidOperationException("Raid must never wipe the record"),
            (out Guid session, out long load) => { session = context.Snapshot.SessionEpoch; load = context.Snapshot.LoadEpoch; return true; },
            LocalPressureProfile.Moderate, isAuthoritativeHost: () => true);
        chief.Update();
        Assert.True(chief.ReadLockdownEngaged()); Assert.Equal(0, world.Releases);
        Assert.Equal(record.DemandRound, story.State!.ChiefRecord!.DemandRound);
        Assert.Equal(record.State, story.State.ChiefRecord.State);
        Assert.Equal(record.LockdownEngagedAtMinutes, story.State.ChiefRecord.LockdownEngagedAtMinutes);
    }

    [Theory]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, false)]
    public void Overnight_raid_line_is_recent(int day, bool expected)
    {
        var h = new Chapter3RaidSeizureTests.Harness(); h.Away(); h.Poll(); h.Poll();
        var state = h.State with { LastProcessedElapsedDay = day };
        Assert.Equal(expected, Chapter3OvernightLine.Select(state, day)?.Contains("Police have hit") == true);
    }

    private sealed class ChiefWorld : Release1ChiefWorldStub
    {
        internal int Releases;
        public override Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double minutes) { minutes = 3000; return Release1SmallCourtesyWorldReadStatus.Ready; }
        public override Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason) { reason = "retained lockdown"; return Release1SmallCourtesyWorldMutationStatus.Succeeded; }
        public override Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason) { Releases++; reason = "unexpected release"; return Release1SmallCourtesyWorldMutationStatus.Succeeded; }
    }
    private static Chapter3RuntimeService Create(ChapterRepository repo)
    { var service = new Chapter3RuntimeService(new Context(), repo, new Gate(), new Clock()); service.OnPreLoad(); service.OnLoadComplete(); return service; }
    private sealed class Context : IRelease1StoryHostContext
    { public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot) { snapshot = new(Guid.Empty, 1, "player", Path.GetTempPath()); return Release1StoryHostContextReadStatus.Ready; } }
    private sealed class Gate : IChapter3StoryGate { public bool IsEnvelopeSatisfied() => true; }
    private sealed class Clock : IChapter3DayClock
    { public event Action? DayPassed { add { } remove { } } public event Action? WeekPassed { add { } remove { } } public bool TryReadElapsedDays(out int days) { days = 0; return true; } }
    private sealed class ChapterRepository : IChapter3Repository, IChapter3SaveFolderBoundRepository
    {
        public string BoundSaveFolder => Path.GetFullPath(Path.GetTempPath());
        internal Chapter3State? Stored = new("player", Chapter3Phase.Active, 0, 0, 0);
        public Chapter3StoreLoadResult Load() => new(true, Chapter3StoreLoadStatus.Loaded, new(Chapter3SaveCodec.CurrentSchemaVersion, Stored), Chapter3StoreFailureReason.None, "loaded");
        public Chapter3StoreUpdateResult Update(Chapter3State? state)
        { Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var write), write.Message); Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var decoded, out var read), read.Message); Stored = decoded!.State; return new(true, Chapter3StoreUpdateStatus.Updated, null, Chapter3StoreFailureReason.None, "stored"); }
    }
    private sealed class Host : ILocalPressureRuntimeHostAdapter
    {
        internal bool Authoritative = true;
        internal Dictionary<string, int> NativeCrimes = new() { ["assault"] = 2, ["dealing"] = 1 };
        internal int NativePursuit = 3;
        public string ActiveSaveFolder => @"C:\saves\oc173";
        public string CanonicalHostIdentity => "player-1";
        public event Action? PreLoad { add { } remove { } } public event Action? LoadComplete { add { } remove { } }
        public event Action? SaveStart { add { } remove { } } public event Action? SaveComplete { add { } remove { } }
        public event Action<LocalPressureClockBoundary>? ClockBoundary { add { } remove { } }
        public LocalPressureHostAuthorityReadStatus ReadHostAuthority() => Authoritative ? LocalPressureHostAuthorityReadStatus.Ready : LocalPressureHostAuthorityReadStatus.NotAuthoritative;
        public LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions() => LocalPressureClockBoundaryBindingStatus.Ready;
        public LocalPressureClockReadStatus TryReadHostClock(out LocalPressureClockSample clock) { clock = new(180, 0, 300, null, LocalPressureClockBoundary.HostReady, DateTime.UtcNow); return LocalPressureClockReadStatus.Ready; }
        public LocalPressurePlayerReadStatus TryReadSupportedPlayers(out IReadOnlyList<LocalPressurePlayerSample> players) { players = new[] { new LocalPressurePlayerSample("player-1", true, true, "player-1", "north", "home", this) }; return LocalPressurePlayerReadStatus.Ready; }
        public void Dispose() { }
    }
    private sealed class PressureRepository : ILocalPressureStateRepository
    {
        internal LocalPressurePlayerRecord Record; internal int Writes;
        internal PressureRepository(LocalPressureState state) => Record = LocalPressurePlayerRecord.FromState(state);
        public LocalPressureStoreLoadResult Load() => new(true, LocalPressureStoreLoadStatus.Loaded, new(LocalPressureSaveCodec.CurrentSchemaVersion, new[] { Record }), LocalPressureStoreFailureReason.None, "loaded");
        public LocalPressureStoreUpdateResult Update(LocalPressurePlayerRecord record)
        { Assert.True(LocalPressureSaveCodec.TrySerialize(new LocalPressureSaveEnvelope(LocalPressureSaveCodec.CurrentSchemaVersion, new[] { record }), out var json, out var write), write.Message); Assert.True(LocalPressureSaveCodec.TryDeserialize(json, out var decoded, out var read), read.Message); Record = decoded!.Players.Single(); Writes++; return new(true, LocalPressureStoreUpdateStatus.Updated, null, LocalPressureStoreFailureReason.None, "stored"); }
    }
}
