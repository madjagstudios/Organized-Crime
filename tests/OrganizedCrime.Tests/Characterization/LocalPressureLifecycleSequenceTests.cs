using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

// OC-148 characterization: the Local Pressure save/load sequence and the
// per-record merge (other players' records kept). Pinned before SidecarStore
// replaces the three state stores; passes unchanged after. The service
// lifecycle itself is not extracted (void/property/retry shape), but these
// pins guard the store beneath it.
public sealed class LocalPressureLifecycleSequenceTests
{
    private const string CurrentPlayerId = "player-1";
    private const string OtherPlayerId = "player-2";

    [Fact]
    public void Save_round_trip_keeps_other_players_records()
    {
        using var folder = TempDir.Create();
        var store = CreateStore(folder.Path);
        var seededCurrent = new LocalPressureState(10, false, 0, null, 0, CurrentPlayerId, "north", "safehouse", 1);
        var seededOther = new LocalPressureState(40, true, 5, null, 5, OtherPlayerId, "docks", "warehouse", 3);
        Assert.True(store.TryUpdate(LocalPressurePlayerRecord.FromState(seededCurrent), out _));
        Assert.True(store.TryUpdate(LocalPressurePlayerRecord.FromState(seededOther), out _));

        var adapter = ActiveAdapter(folder.Path);
        using var service = new LocalPressureRuntimeService(adapter, FolderRepository(folder.Path));
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
        Assert.Equal(seededOther, ReadState(service, OtherPlayerId));

        Assert.True(service.TryGetActiveEvidenceSnapshot(out var snapshot));
        var evidence = new CustodyEntryEvidence(
            CurrentPlayerId, $"custody/v1/{snapshot.SessionEpoch:D}/{snapshot.LoadEpoch}/{CurrentPlayerId}/1", null, null);
        var applied = service.TryApplyCustodyEvidence(evidence, snapshot.SessionEpoch, snapshot.LoadEpoch);
        Assert.True(applied.Accepted, applied.Message);
        Assert.NotEqual(seededCurrent.LocalHeat, applied.State!.LocalHeat);

        service.OnSaveStart();
        service.OnSaveComplete();
        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);

        Assert.True(store.TryLoad(out var load));
        Assert.Equal(LocalPressureStoreLoadStatus.Loaded, load.Status);
        var records = load.Envelope!.Players.ToDictionary(record => record.PlayerId, StringComparer.Ordinal);
        Assert.Equal(2, records.Count);
        Assert.Equal(LocalPressurePlayerRecord.FromState(applied.State), records[CurrentPlayerId]);
        Assert.Equal(LocalPressurePlayerRecord.FromState(seededOther), records[OtherPlayerId]);
    }

    [Fact]
    public void Store_merge_rejects_stale_per_player_revisions_and_replays_identical_quietly()
    {
        using var folder = TempDir.Create();
        var store = CreateStore(folder.Path);
        var current = new LocalPressureState(10, false, 0, null, 0, CurrentPlayerId, null, null, 1);
        var other = new LocalPressureState(40, true, 5, null, 5, OtherPlayerId, null, null, 3);
        Assert.True(store.TryUpdate(LocalPressurePlayerRecord.FromState(current), out _));
        Assert.True(store.TryUpdate(LocalPressurePlayerRecord.FromState(other), out _));

        var advanced = new LocalPressureState(35, false, 0, null, 0, CurrentPlayerId, null, null, 2);
        Assert.True(store.TryUpdate(LocalPressurePlayerRecord.FromState(advanced), out var updated));
        Assert.Equal(LocalPressureStoreUpdateStatus.Updated, updated.Status);

        Assert.False(store.TryUpdate(LocalPressurePlayerRecord.FromState(current), out var stale));
        Assert.Equal(LocalPressureStoreFailureReason.StaleRevision, stale.FailureReason);

        Assert.True(store.TryUpdate(LocalPressurePlayerRecord.FromState(advanced), out var idempotent));
        Assert.Equal(LocalPressureStoreUpdateStatus.Idempotent, idempotent.Status);

        Assert.True(store.TryLoad(out var load));
        var records = load.Envelope!.Players.ToDictionary(record => record.PlayerId, StringComparer.Ordinal);
        Assert.Equal(2, records.Count);
        Assert.Equal(LocalPressurePlayerRecord.FromState(advanced), records[CurrentPlayerId]);
        Assert.Equal(LocalPressurePlayerRecord.FromState(other), records[OtherPlayerId]);
    }

    [Fact]
    public void Repeat_calls_are_safe()
    {
        var adapter = ActiveAdapter("C:\\saves\\slot-1");
        var repository = new FakeRepository();
        using var service = new LocalPressureRuntimeService(adapter, _ => repository);

        service.OnPreLoad();
        service.OnPreLoad();
        Assert.Equal(1, service.LoadEpoch);
        Assert.Equal(LocalPressureRuntimePhase.AwaitingLoad, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);

        service.OnLoadComplete();
        service.OnLoadComplete();
        Assert.Equal(1, repository.LoadCalls);
        Assert.Equal(LocalPressureRuntimePhase.AwaitingHostBaseline, service.Phase);

        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);

        service.OnSaveStart();
        service.OnSaveStart();
        Assert.Equal(LocalPressureRuntimePhase.Saving, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);

        service.OnSaveComplete();
        service.OnSaveComplete();
        Assert.Equal(LocalPressureRuntimePhase.Active, service.Phase);
        Assert.Equal(1, repository.UpdateCalls);
    }

    [Fact]
    public void Save_complete_before_the_baseline_writes_nothing()
    {
        var adapter = ActiveAdapter("C:\\saves\\slot-1");
        var repository = new FakeRepository();
        using var service = new LocalPressureRuntimeService(adapter, _ => repository);
        service.OnPreLoad();
        service.OnLoadComplete();
        Assert.Equal(LocalPressureRuntimePhase.AwaitingHostBaseline, service.Phase);

        service.OnSaveStart();
        Assert.Equal(LocalPressureRuntimePhase.Saving, service.Phase);
        service.OnSaveComplete();

        Assert.Equal(LocalPressureRuntimePhase.AwaitingHostBaseline, service.Phase);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Equal(LocalPressureRuntimeRejectReason.None, service.LastRejectReason);
    }

    [Fact]
    public void Dispose_unsubscribes_and_blocks_lifecycle()
    {
        var adapter = ActiveAdapter("C:\\saves\\slot-1");
        var repository = new FakeRepository();
        var service = new LocalPressureRuntimeService(adapter, _ => repository);
        service.OnPreLoad();
        service.OnLoadComplete();

        service.Dispose();
        service.Dispose();

        Assert.Equal(LocalPressureRuntimePhase.Disposed, service.Phase);
        Assert.Equal(1, adapter.DisposeCalls);
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnSaveStart();
        service.OnSaveComplete();
        Assert.Equal(LocalPressureRuntimePhase.Disposed, service.Phase);
        Assert.Equal(LocalPressureRuntimeRejectReason.Disposed, service.LastRejectReason);
        adapter.RaisePreLoad();
        adapter.RaiseLoadComplete();
        adapter.RaiseSaveStart();
        adapter.RaiseSaveComplete();
        Assert.Equal(LocalPressureRuntimePhase.Disposed, service.Phase);
        Assert.Equal(1, repository.LoadCalls);
    }

    private static LocalPressureState? ReadState(LocalPressureRuntimeService service, string playerId) =>
        service.TryGetState(playerId, out var state) ? state : null;

    private static FakeHostAdapter ActiveAdapter(string saveFolder) =>
        new()
        {
            ActiveSaveFolder = saveFolder,
            CanonicalHostIdentity = CurrentPlayerId,
            Players = new[] { FakeHostAdapter.SinglePlayer(CurrentPlayerId) },
            Clock = FakeHostAdapter.CreateClock(600)
        };

    private static Func<string?, ILocalPressureStateRepository?> FolderRepository(string saveFolder) =>
        _ =>
        {
            Assert.True(LocalPressureSavePath.TryCreate(saveFolder, out var path, out var result), result.Message);
            return new LocalPressureStateStoreRepository(new LocalPressureStateStore(path!));
        };

    private static LocalPressureStateStore CreateStore(string saveFolder)
    {
        Assert.True(LocalPressureSavePath.TryCreate(saveFolder, out var path, out var result), result.Message);
        return new LocalPressureStateStore(path!);
    }

    private sealed class TempDir : IDisposable
    {
        private TempDir(string path) => Path = path;

        public string Path { get; }

        public static TempDir Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oc-148-lp-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(path);
            return new TempDir(path);
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }

    #pragma warning disable CS0067
    private sealed class FakeHostAdapter : ILocalPressureRuntimeHostAdapter
    {
        public bool HostAuthority { get; set; } = true;
        public string? CanonicalHostIdentity { get; set; } = CurrentPlayerId;
        public string? ActiveSaveFolder { get; set; }
        public LocalPressureClockSample Clock { get; set; }
        public IReadOnlyList<LocalPressurePlayerSample> Players { get; set; } = Array.Empty<LocalPressurePlayerSample>();
        public int DisposeCalls { get; private set; }

        public event Action? PreLoad;
        public event Action? LoadComplete;
        public event Action? SaveStart;
        public event Action? SaveComplete;
        public event Action<LocalPressureClockBoundary>? ClockBoundary;

        public LocalPressureHostAuthorityReadStatus ReadHostAuthority() =>
            HostAuthority ? LocalPressureHostAuthorityReadStatus.Ready : LocalPressureHostAuthorityReadStatus.NotAuthoritative;

        public LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions() =>
            LocalPressureClockBoundaryBindingStatus.Ready;

        public LocalPressureClockReadStatus TryReadHostClock(out LocalPressureClockSample sample)
        {
            sample = Clock;
            return LocalPressureClockReadStatus.Ready;
        }

        public LocalPressurePlayerReadStatus TryReadSupportedPlayers(out IReadOnlyList<LocalPressurePlayerSample> players)
        {
            players = Players;
            return Players.Count == 0 ? LocalPressurePlayerReadStatus.Pending : LocalPressurePlayerReadStatus.Ready;
        }

        public void RaisePreLoad() => PreLoad?.Invoke();
        public void RaiseLoadComplete() => LoadComplete?.Invoke();
        public void RaiseSaveStart() => SaveStart?.Invoke();
        public void RaiseSaveComplete() => SaveComplete?.Invoke();

        public void Dispose() => DisposeCalls++;

        public static LocalPressurePlayerSample SinglePlayer(string playerId) =>
            new(playerId, true, true, playerId, "north", "safehouse");

        public static LocalPressureClockSample CreateClock(long totalGameMinutes) =>
            new(totalGameMinutes, (int)(totalGameMinutes / 1440), 700, null, LocalPressureClockBoundary.HostReady, DateTime.UtcNow);
    }

    private sealed class FakeRepository : ILocalPressureStateRepository
    {
        public int LoadCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public LocalPressurePlayerRecord? UpdatedRecord { get; private set; }

        public LocalPressureStoreLoadResult Load()
        {
            LoadCalls++;
            return new(
                true,
                LocalPressureStoreLoadStatus.Empty,
                LocalPressureSaveEnvelope.CreateEmpty(),
                LocalPressureStoreFailureReason.None,
                "ok");
        }

        public LocalPressureStoreUpdateResult Update(LocalPressurePlayerRecord record)
        {
            UpdateCalls++;
            UpdatedRecord = record;
            return new(
                true,
                LocalPressureStoreUpdateStatus.Updated,
                null,
                LocalPressureStoreFailureReason.None,
                "ok");
        }
    }
}
