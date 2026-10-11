using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

[Collection("OC-151 composition scope")]
public sealed class Chapter3PatrolPressureQaTests
{
    [Theory]
    [InlineData(LocalPressureTier.Quiet)]
    [InlineData(LocalPressureTier.Noticed)]
    [InlineData(LocalPressureTier.Watched)]
    [InlineData(LocalPressureTier.Critical)]
    public void Heat_QA_changes_only_heat_and_revision_and_persists_on_existing_save_path(LocalPressureTier tier)
    {
        var old = new LocalPressureState(65, true, 1d, 2d, 3d, "player-1", "north", "home", 7);
        var repo = new FakeRepository(new LocalPressureSaveEnvelope(1, new[] { LocalPressurePlayerRecord.FromState(old) }));
        using var adapter = new FakeHostAdapter { Players = new[] { FakeHostAdapter.SinglePlayer() }, Clock = FakeHostAdapter.CreateClock(180) };
        using var pressure = new LocalPressureRuntimeService(adapter, _ => repo);
        pressure.OnPreLoad(); pressure.OnLoadComplete(); pressure.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        Assert.True(pressure.QaSetTier(tier).Ok);
        Assert.True(pressure.TryGetState("player-1", out var read));
        Assert.Equal(tier, LocalPressureTransitions.GetTier(read!.LocalHeat, LocalPressureProfile.Moderate));
        Assert.True(read.KnownOffender);
        Assert.Equal(old.LastEvidenceGameTime, read.LastEvidenceGameTime);
        Assert.Equal(old.QuietGraceUntil, read.QuietGraceUntil);
        Assert.Equal(old.LastDecayEvaluation, read.LastDecayEvaluation);
        Assert.Equal(old.PlayerId, read.PlayerId);
        Assert.Equal(old.Region, read.Region);
        Assert.Equal(old.PropertyCode, read.PropertyCode);
        Assert.Equal(old.Revision + 1, read.Revision);
        Assert.Equal(0, repo.UpdateCalls);
        pressure.OnSaveStart(); pressure.OnSaveComplete();
        Assert.NotNull(repo.UpdatedRecord);
        Assert.Equal(read.LocalHeat, repo.UpdatedRecord!.ToState().LocalHeat);
    }

    [Fact]
    public void Heat_QA_refuses_client_and_saving_without_mutating_state()
    {
        var repo = new FakeRepository();
        using var adapter = new FakeHostAdapter { Players = new[] { FakeHostAdapter.SinglePlayer() }, Clock = FakeHostAdapter.CreateClock(180) };
        using var pressure = new LocalPressureRuntimeService(adapter, _ => repo);
        pressure.OnPreLoad(); pressure.OnLoadComplete(); pressure.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        pressure.TryGetState("player-1", out var before);
        adapter.HostAuthority = false;
        Assert.False(pressure.QaSetTier(LocalPressureTier.Critical).Ok);
        adapter.HostAuthority = true;
        pressure.OnSaveStart();
        Assert.False(pressure.QaSetTier(LocalPressureTier.Critical).Ok);
        pressure.TryGetState("player-1", out var after);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Chief_QA_sets_real_record_and_native_gate_and_preserves_debt_for_D4()
    {
        using var f = new ChiefFixture();
        var started = f.Chief.QaSetLockdown(true);
        Assert.True(started.Ok, started.Message);
        var record = f.Story.State!.ChiefRecord!;
        Assert.True(record.LockdownEngaged);
        Assert.Equal(3000d, record.LockdownEngagedAtMinutes);
        Assert.Equal(2, record.DemandRound);
        Assert.Equal(1, f.World.Engages);
        Assert.True(f.Chief.QaLockdownAge(24d).Ok);
        Assert.Equal(1560d, f.Story.State.ChiefRecord!.LockdownEngagedAtMinutes);
        // QA holds at low heat, without disabling D4's real record/stamp reads.
        f.World.Now++;
        f.Chief.Update();
        Assert.True(f.Chief.ReadLockdownEngaged());
        Assert.True(f.Chief.QaForcedWantedSnapshot().LockdownEngaged);
        Assert.True(f.Chief.QaSetLockdown(false).Ok);
        Assert.False(f.Chief.ReadLockdownEngaged());
        Assert.Null(f.Story.State.ChiefRecord!.LockdownEngagedAtMinutes);
        Assert.Equal(2, f.Story.State.ChiefRecord.DemandRound);
        Assert.Equal(1, f.World.Releases);
    }

    [Fact]
    public void Chief_QA_refuses_client_protection_and_native_failure_without_record_write()
    {
        using var f = new ChiefFixture();
        var before = f.Story.State!.ChiefRecord!;
        f.Host = false;
        Assert.False(f.Chief.QaSetLockdown(true).Ok);
        Assert.Equal(0, f.World.Engages);
        f.Host = true;
        f.Story.TrySetChiefRecord(before with { ProtectedUntilDay = 10, Revision = before.Revision + 1 });
        Assert.False(f.Chief.QaSetLockdown(true).Ok);
        Assert.Equal(0, f.World.Engages);
        var protectedRecord = f.Story.State.ChiefRecord!;
        f.Story.TrySetChiefRecord(protectedRecord with { ProtectedUntilDay = null, Revision = protectedRecord.Revision + 1 });
        f.World.Succeeds = false;
        before = f.Story.State.ChiefRecord!;
        Assert.False(f.Chief.QaSetLockdown(true).Ok);
        Assert.Equal(before, f.Story.State.ChiefRecord);
    }

    [Fact]
    public void Chief_QA_hold_ends_on_load_and_normal_cooling_resumes()
    {
        using var f = new ChiefFixture();
        var started = f.Chief.QaSetLockdown(true);
        Assert.True(started.Ok, started.Message);
        f.Chief.OnPreLoad(); f.Chief.OnLoadComplete();
        f.Chief.Update();
        Assert.False(f.Chief.ReadLockdownEngaged());
    }

    private sealed class ChiefFixture : IDisposable
    {
        internal readonly Release1SmallCourtesyDepositTests.FakeContext Context = new();
        internal readonly Release1StoryRuntimeService Story;
        internal readonly ChiefWorld World = new();
        internal readonly Release1ChiefService Chief;
        internal bool Host = true;
        internal ChiefFixture()
        {
            Story = new(Context, new Release1SmallCourtesyDepositTests.FakeRepository());
            Story.OnPreLoad(); Story.OnLoadComplete();
            const string receipt = "oc170-qa-intro";
            var correlation = Release1LogicalCorrelation.Create(Context.Snapshot.PlayerId,
                Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
            Assert.True(Story.TryExecuteDurably(new(Context.Snapshot.SessionEpoch, Context.Snapshot.LoadEpoch,
                Context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0,
                Release1TransitionKind.IntroAccepted, receipt, correlation)).Accepted);
            var record = Release1ChiefRecord.Adopt(LocalPressureTier.Watched, false) with
            { State = Release1ChiefState.DemandOpen, DemandRound = 2 };
            Assert.True(Story.TrySetChiefRecord(record).Accepted);
            Chief = new(Story, World, new Release1ChiefTierObserver(),
                _ => LocalPressureState.Quiet(Context.Snapshot.PlayerId),
                _ => new(false, LocalPressureEvidenceWriteRejectReason.RuntimeNotActive, null, "unused"),
                (out Guid session, out long load) => { session = Context.Snapshot.SessionEpoch; load = Context.Snapshot.LoadEpoch; return true; },
                LocalPressureProfile.Moderate, isAuthoritativeHost: () => Host);
        }
        public void Dispose() { Chief.Dispose(); Story.Dispose(); }
    }
    private sealed class ChiefWorld : Release1ChiefWorldStub
    {
        internal double Now = 3000d;
        internal bool Succeeds = true;
        internal int Engages;
        internal int Releases;
        public override Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double minutes)
        { minutes = Now; return Release1SmallCourtesyWorldReadStatus.Ready; }
        public override Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)
        { Engages++; reason = "QA"; return Succeeds ? Release1SmallCourtesyWorldMutationStatus.Succeeded : Release1SmallCourtesyWorldMutationStatus.Rejected; }
        public override Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)
        { Releases++; reason = "QA"; return Succeeds ? Release1SmallCourtesyWorldMutationStatus.Succeeded : Release1SmallCourtesyWorldMutationStatus.Rejected; }
    }
    private sealed class FakeHostAdapter : ILocalPressureRuntimeHostAdapter
    {
        internal bool HostAuthority = true;
        internal IReadOnlyList<LocalPressurePlayerSample> Players = Array.Empty<LocalPressurePlayerSample>();
        internal LocalPressureClockSample Clock;
        public string ActiveSaveFolder => @"C:\saves\oc170-qa";
        public string CanonicalHostIdentity => "player-1";
        public event Action? PreLoad { add { } remove { } }
        public event Action? LoadComplete { add { } remove { } }
        public event Action? SaveStart { add { } remove { } }
        public event Action? SaveComplete { add { } remove { } }
        public event Action<LocalPressureClockBoundary>? ClockBoundary { add { } remove { } }
        public LocalPressureHostAuthorityReadStatus ReadHostAuthority() => HostAuthority
            ? LocalPressureHostAuthorityReadStatus.Ready : LocalPressureHostAuthorityReadStatus.NotAuthoritative;
        public LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions() => LocalPressureClockBoundaryBindingStatus.Ready;
        public LocalPressureClockReadStatus TryReadHostClock(out LocalPressureClockSample sample)
        { sample = Clock; return LocalPressureClockReadStatus.Ready; }
        public LocalPressurePlayerReadStatus TryReadSupportedPlayers(out IReadOnlyList<LocalPressurePlayerSample> players)
        { players = Players; return LocalPressurePlayerReadStatus.Ready; }
        internal static LocalPressurePlayerSample SinglePlayer() => new("player-1", true, true, "player-1", "north", "safehouse");
        internal static LocalPressureClockSample CreateClock(long minutes) => new(minutes, (int)(minutes / 1440L),
            700, null, LocalPressureClockBoundary.HostReady, DateTime.UtcNow);
        public void Dispose() { }
    }
    private sealed class FakeRepository : ILocalPressureStateRepository
    {
        private readonly LocalPressureSaveEnvelope _envelope;
        internal int UpdateCalls;
        internal LocalPressurePlayerRecord? UpdatedRecord;
        internal FakeRepository(LocalPressureSaveEnvelope? envelope = null) => _envelope = envelope ?? LocalPressureSaveEnvelope.CreateEmpty();
        public LocalPressureStoreLoadResult Load() => new(true, LocalPressureStoreLoadStatus.Loaded,
            _envelope, LocalPressureStoreFailureReason.None, "QA loaded");
        public LocalPressureStoreUpdateResult Update(LocalPressurePlayerRecord record)
        {
            UpdateCalls++; UpdatedRecord = record;
            return new(true, LocalPressureStoreUpdateStatus.Updated, null, LocalPressureStoreFailureReason.None, "QA stored");
        }
    }
}
