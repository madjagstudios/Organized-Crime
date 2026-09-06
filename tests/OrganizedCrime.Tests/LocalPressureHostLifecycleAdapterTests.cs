using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressureHostLifecycleAdapterTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Hour_boundary_is_forwarded_only_when_awake(bool sleepInProgress, bool expected)
    {
        Assert.Equal(
            expected,
            LocalPressureHostLifecyclePolicies.ShouldForwardAwakeHour(sleepInProgress));
    }

    [Fact]
    public void Day_boundary_is_diagnostic_only()
    {
        Assert.False(LocalPressureHostLifecyclePolicies.ShouldForwardDayPass());
    }

    [Theory]
    [InlineData(@"C:\Saves\76561190000000001\SaveGame_4", "76561190000000001")]
    [InlineData(@"C:/Saves/76561190000000001/SaveGame_4/", "76561190000000001")]
    public void Canonical_host_identity_is_parsed_from_the_save_account_folder(
        string activeSaveFolder,
        string expectedIdentity)
    {
        Assert.True(
            LocalPressureHostLifecyclePolicies.TryGetCanonicalHostIdentity(
                activeSaveFolder,
                out var identity));

        Assert.Equal(expectedIdentity, identity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Saves\0\SaveGame_4")]
    [InlineData(@"C:\Saves\offline\SaveGame_4")]
    [InlineData(@"C:\Saves\76561190000000001\NotASave")]
    public void Canonical_host_identity_rejects_missing_or_noncanonical_save_paths(
        string? activeSaveFolder)
    {
        Assert.False(
            LocalPressureHostLifecyclePolicies.TryGetCanonicalHostIdentity(
                activeSaveFolder,
                out _));
    }

    [Fact]
    public void Sleep_end_is_forwarded_once_for_one_absolute_boundary()
    {
        long? lastForwarded = null;

        Assert.True(LocalPressureHostLifecyclePolicies.TryAcceptSleepEnd(1860, ref lastForwarded));
        Assert.False(LocalPressureHostLifecyclePolicies.TryAcceptSleepEnd(1860, ref lastForwarded));
        Assert.True(LocalPressureHostLifecyclePolicies.TryAcceptSleepEnd(3300, ref lastForwarded));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Host_projection_requires_server_and_local_client(
        bool isServer,
        bool isLocalClient,
        bool expectedHost)
    {
        Assert.Equal(expectedHost, LocalPressureHostLifecyclePolicies.IsAuthoritativeHost(isServer, isLocalClient));
    }

    [Fact]
    public void Empty_registry_is_pending()
    {
        var status = LocalPressureHostLifecyclePolicies.ClassifyPlayerRegistrySnapshot(
            Array.Empty<LocalPressurePlayerSample>(), out var players);

        Assert.Equal(LocalPressurePlayerReadStatus.Pending, status);
        Assert.Empty(players);
    }

    [Fact]
    public void Registry_player_without_connection_is_pending()
    {
        var sample = new LocalPressurePlayerSample("player-1", true, true, "player-1", null, null, HasConnection: false);

        var status = LocalPressureHostLifecyclePolicies.ClassifyPlayerRegistrySnapshot(
            new[] { sample }, out _);

        Assert.Equal(LocalPressurePlayerReadStatus.Pending, status);
    }

    [Fact]
    public void Placeholder_registry_player_code_is_pending()
    {
        var sample = new LocalPressurePlayerSample("0", true, true, "0", null, null);

        var status = LocalPressureHostLifecyclePolicies.ClassifyPlayerRegistrySnapshot(
            new[] { sample }, out _);

        Assert.Equal(LocalPressurePlayerReadStatus.Pending, status);
    }

    [Fact]
    public void Multiple_registry_players_are_unsupported()
    {
        var samples = new[]
        {
            new LocalPressurePlayerSample("player-1", true, true, "player-1", null, null),
            new LocalPressurePlayerSample("player-2", true, false, "player-2", null, null)
        };

        var status = LocalPressureHostLifecyclePolicies.ClassifyPlayerRegistrySnapshot(samples, out var players);

        Assert.Equal(LocalPressurePlayerReadStatus.UnsupportedMultiplayer, status);
        Assert.Equal(2, players.Count);
    }

    [Fact]
    public void One_connected_registry_player_is_ready()
    {
        var sample = new LocalPressurePlayerSample("player-1", true, true, "player-1", null, null);

        var status = LocalPressureHostLifecyclePolicies.ClassifyPlayerRegistrySnapshot(
            new[] { sample }, out var players);

        Assert.Equal(LocalPressurePlayerReadStatus.Ready, status);
        Assert.Single(players);
    }

    [Fact]
    public void Time_binding_state_requires_rebind_for_a_new_manager_instance()
    {
        var binding = new LocalPressureHostLifecycleBindingState();
        var firstManager = new object();
        var replacementManager = new object();

        binding.MarkBound(firstManager);

        Assert.True(binding.IsBoundTo(firstManager));
        Assert.False(binding.IsBoundTo(replacementManager));

        binding.Clear();

        Assert.False(binding.IsBoundTo(firstManager));
    }

    [Fact]
    public void Composition_owns_one_service_and_disposes_it_without_update_polling()
    {
        var adapter = new FakeAdapter();
        using var composition = new LocalPressureRuntimeComposition(
            adapter,
            _ => new EmptyRepository(),
            _ => { });

        Assert.NotNull(composition.Service);
        composition.Dispose();
        composition.Dispose();

        Assert.Equal(1, adapter.DisposeCalls);
        Assert.Equal(LocalPressureRuntimePhase.Disposed, composition.Service.Phase);
    }

    private sealed class FakeAdapter : ILocalPressureRuntimeHostAdapter
    {
        public int DisposeCalls { get; private set; }
        public LocalPressureHostAuthorityReadStatus ReadHostAuthority() => LocalPressureHostAuthorityReadStatus.Ready;
        public LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions() =>
            LocalPressureClockBoundaryBindingStatus.Ready;
        public string? CanonicalHostIdentity => "player-1";
        public string? ActiveSaveFolder => "C:\\saves\\slot-1";
        public event Action? PreLoad { add { } remove { } }
        public event Action? LoadComplete { add { } remove { } }
        public event Action? SaveStart { add { } remove { } }
        public event Action? SaveComplete { add { } remove { } }
        public event Action<LocalPressureClockBoundary>? ClockBoundary { add { } remove { } }

        public LocalPressureClockReadStatus TryReadHostClock(out LocalPressureClockSample sample)
        {
            sample = default;
            return LocalPressureClockReadStatus.Pending;
        }

        public LocalPressurePlayerReadStatus TryReadSupportedPlayers(out IReadOnlyList<LocalPressurePlayerSample> players)
        {
            players = Array.Empty<LocalPressurePlayerSample>();
            return LocalPressurePlayerReadStatus.Pending;
        }

        public void Dispose() => DisposeCalls++;
    }

    private sealed class EmptyRepository : ILocalPressureStateRepository
    {
        public LocalPressureStoreLoadResult Load() => new(
            true,
            LocalPressureStoreLoadStatus.Empty,
            LocalPressureSaveEnvelope.CreateEmpty(),
            LocalPressureStoreFailureReason.None,
            "empty");

        public LocalPressureStoreUpdateResult Update(LocalPressurePlayerRecord record) => new(
            true,
            LocalPressureStoreUpdateStatus.Updated,
            null,
            LocalPressureStoreFailureReason.None,
            "updated");
    }
}
