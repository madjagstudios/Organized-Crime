using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.PlayerScripts;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class NativePoliceDispatchBoundaryTests
{
    private const string CanonicalPlayerId = "76561190000000001";
    private static readonly Guid Session = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Pinned_native_members_remain_directly_available()
    {
        var dispatch = typeof(PoliceStation)
            .GetMethod(nameof(PoliceStation.Dispatch),
                new[]
                {
                    typeof(int),
                    typeof(Player),
                    typeof(PoliceStation.EDispatchType),
                    typeof(bool)
                });

        Assert.NotNull(dispatch);
        Assert.NotNull(typeof(Player).GetProperty("PlayerCode"));
        Assert.NotNull(typeof(Player).GetProperty("IsServerInitialized"));
        Assert.NotNull(typeof(Player).GetProperty("Connection"));
    }

    [Fact]
    public void Production_adapter_has_exactly_one_direct_dispatch_callsite_with_required_arguments()
    {
        var source = ReadProductionSource("NativePoliceDispatchAdapter.cs");
        var compactSource = string.Concat(source.Where(character => !char.IsWhiteSpace(character)));

        Assert.Equal(1, Count(source, "station.Dispatch("));
        Assert.Contains(
            "station.Dispatch(2,canonicalTarget,PoliceStation.EDispatchType.UseVehicle,false);",
            compactSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_authority_rejects_without_native_dispatch()
    {
        var fixture = new AdapterFixture();
        fixture.Host.Authority = LocalPressureHostAuthorityReadStatus.NotAuthoritative;

        var result = fixture.Adapter.TryRequest(fixture.Request());

        Assert.Equal(NativeLawResponseResultState.AuthorityRejected, result.State);
        Assert.Equal(0, fixture.Native.DispatchCalls);
    }

    [Fact]
    public void Non_canonical_target_rejects_without_native_dispatch()
    {
        var fixture = new AdapterFixture();
        fixture.Host.Players = new[] { AdapterFixture.Player(new object()) };

        var result = fixture.Adapter.TryRequest(fixture.Request());

        Assert.Equal(NativeLawResponseResultState.TargetRejected, result.State);
        Assert.Equal(0, fixture.Native.DispatchCalls);
    }

    [Fact]
    public void Loaded_more_patrols_rejects_without_native_dispatch()
    {
        var fixture = new AdapterFixture();
        fixture.Native.MorePatrolsPresent = true;

        var result = fixture.Adapter.TryRequest(fixture.Request());

        Assert.Equal(NativeLawResponseResultState.Rejected, result.State);
        Assert.Equal(0, fixture.Native.DispatchCalls);
    }

    [Fact]
    public void Unavailable_station_rejects_without_native_dispatch()
    {
        var fixture = new AdapterFixture();
        fixture.Native.ClosestStation = null;

        var result = fixture.Adapter.TryRequest(fixture.Request());

        Assert.Equal(NativeLawResponseResultState.AdapterUnavailable, result.State);
        Assert.Equal(0, fixture.Native.DispatchCalls);
    }

    [Fact]
    public void Unavailable_officer_capacity_rejects_without_native_dispatch()
    {
        var fixture = new AdapterFixture();
        fixture.Native.Snapshots.Enqueue(Snapshot(new[] { "officer-1" }, vehicles: 1));

        var result = fixture.Adapter.TryRequest(fixture.Request());

        Assert.Equal(NativeLawResponseResultState.UnavailableCapacity, result.State);
        Assert.Equal(0, fixture.Native.DispatchCalls);
    }

    [Fact]
    public void Unavailable_vehicle_capacity_rejects_without_native_dispatch()
    {
        var fixture = new AdapterFixture();
        fixture.Native.Snapshots.Enqueue(Snapshot(new[] { "officer-1", "officer-2" }, vehicles: 0));

        var result = fixture.Adapter.TryRequest(fixture.Request());

        Assert.Equal(NativeLawResponseResultState.UnavailableCapacity, result.State);
        Assert.Equal(0, fixture.Native.DispatchCalls);
    }

    [Fact]
    public void Production_native_dispatch_sources_do_not_escape_the_read_only_boundary()
    {
        var source = ReadProductionSource("NativeDispatchOutcomeEvaluator.cs") +
            "\n" +
            ReadProductionSource("NativePoliceDispatchAdapter.cs");
        var forbiddenTokens = new[]
        {
            "Player.Local",
            "PullOfficer",
            "ReturnVehicle",
            "Revive",
            "OfficerPool.Add",
            "OfficerPool.Remove",
            "HarmonyPatch",
            "GetMethod(",
            "MethodInfo",
            ".Invoke(",
            "StartFootPatrol",
            "StartVehiclePatrol",
            "LawActivitySettings",
            "Delayed",
            "Retry",
            "retry"
        };

        foreach (var token in forbiddenTokens)
            Assert.DoesNotContain(token, source, StringComparison.Ordinal);
    }

    private static string ReadProductionSource(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime", fileName));

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static NativeDispatchStationSnapshot Snapshot(
        IReadOnlyList<string> officerIdentities,
        int vehicles) =>
        new("station-1", officerIdentities, vehicles);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private sealed class AdapterFixture
    {
        public AdapterFixture()
        {
            Host.Players = new[] { Player(SourcePlayer) };
            Native.CanonicalTarget = SourcePlayer;
            Adapter = new NativePoliceDispatchAdapter(Host, Native);
        }

        public object SourcePlayer { get; } = new();
        public FakeHostAdapter Host { get; } = new();
        public FakeNativePoliceDispatchBoundary Native { get; } = new();
        public NativePoliceDispatchAdapter Adapter { get; }

        public NativeLawResponseRequest Request() =>
            new(
                Session,
                3,
                CanonicalPlayerId,
                SourcePlayer,
                "edge-1",
                OrganizedCrime.Model.LocalPressureTier.Watched,
                "north",
                "safehouse",
                NativeLawResponseProfile.VehicleTwoOfficerV1);

        public static LocalPressurePlayerSample Player(object sourcePlayer) =>
            new(
                "player-1",
                IsHostOwned: true,
                IsLocalPlayer: true,
                CanonicalPlayerId,
                "north",
                "safehouse",
                sourcePlayer,
                HasConnection: true);
    }

    #pragma warning disable CS0067
    private sealed class FakeHostAdapter : ILocalPressureRuntimeHostAdapter
    {
        public LocalPressureHostAuthorityReadStatus Authority { get; set; } = LocalPressureHostAuthorityReadStatus.Ready;
        public LocalPressurePlayerReadStatus PlayerReadStatus { get; set; } = LocalPressurePlayerReadStatus.Ready;
        public IReadOnlyList<LocalPressurePlayerSample> Players { get; set; } = Array.Empty<LocalPressurePlayerSample>();
        public string? ActiveSaveFolder => null;
        public string? CanonicalHostIdentity => CanonicalPlayerId;

        public event Action? PreLoad;
        public event Action? LoadComplete;
        public event Action? SaveStart;
        public event Action? SaveComplete;
        public event Action<LocalPressureClockBoundary>? ClockBoundary;

        public LocalPressureHostAuthorityReadStatus ReadHostAuthority() => Authority;

        public LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions() =>
            LocalPressureClockBoundaryBindingStatus.Pending;

        public LocalPressureClockReadStatus TryReadHostClock(out LocalPressureClockSample sample)
        {
            sample = default;
            return LocalPressureClockReadStatus.Pending;
        }

        public LocalPressurePlayerReadStatus TryReadSupportedPlayers(
            out IReadOnlyList<LocalPressurePlayerSample> players)
        {
            players = Players;
            return PlayerReadStatus;
        }

        public void Dispose() { }
    }
    #pragma warning restore CS0067

    private sealed class FakeNativePoliceDispatchBoundary : INativePoliceDispatchBoundary
    {
        public bool MorePatrolsPresent { get; set; }
        public bool IsNativePlayer { get; set; } = true;
        public object CanonicalTarget { get; set; } = new();
        public bool IdentityReadable { get; set; } = true;
        public bool IsServerInitialized { get; set; } = true;
        public object? Connection { get; set; } = new();
        public string? PlayerCode { get; set; } = CanonicalPlayerId;
        public object? ClosestStation { get; set; } = new();
        public Queue<NativeDispatchStationSnapshot?> Snapshots { get; } = new();
        public int DispatchCalls { get; private set; }

        public bool IsMorePatrolsPresent() => MorePatrolsPresent;

        public bool TryGetCanonicalTarget(object sourcePlayer, out object canonicalTarget)
        {
            canonicalTarget = CanonicalTarget;
            return IsNativePlayer;
        }

        public bool TryReadTargetIdentity(
            object canonicalTarget,
            out bool isServerInitialized,
            out object? connection,
            out string? playerCode,
            out string failure)
        {
            isServerInitialized = IsServerInitialized;
            connection = Connection;
            playerCode = PlayerCode;
            failure = IdentityReadable ? string.Empty : "native player identity read failed: TestException";
            return IdentityReadable;
        }

        public object? GetClosestPoliceStation(object canonicalTarget) => ClosestStation;

        public bool TrySnapshot(object station, out NativeDispatchStationSnapshot snapshot)
        {
            var next = Snapshots.Count == 0 ? null : Snapshots.Dequeue();
            snapshot = next!;
            return next is not null;
        }

        public void RequestResponse(object station, object canonicalTarget) => DispatchCalls++;
    }
}
