using Il2CppFishNet;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Police;
using Il2CppScheduleOne.PlayerScripts;
using UnityEngine;

namespace OrganizedCrime.PoliceDispatchProof;

public sealed class PoliceDispatchProofObserver
{
    private const string DeployedVehiclesUnavailable =
        "deployedVehicles is private and has no usable current interop wrapper; preserved unavailable";

    private long _sequence;

    public DispatchProofObservation Capture()
    {
        var stations = new List<StationSnapshot>();
        var officers = new List<OfficerSnapshot>();
        var players = new List<PlayerSnapshot>();
        var clock = ProofObservation<long>.Unavailable("host clock unavailable");

        try
        {
            var timeManager = Il2CppScheduleOne.GameTime.TimeManager.Instance;
            if (timeManager is not null)
                clock = ProofObservation<long>.Available(timeManager.GetDateTime().GetMinSum());
        }
        catch (Exception exception)
        {
            clock = ProofObservation<long>.Unavailable($"host clock read threw: {exception.GetType().Name}");
        }

        try
        {
            foreach (var station in PoliceStation.PoliceStations ?? new Il2CppSystem.Collections.Generic.List<PoliceStation>())
            {
                if (station is null)
                    continue;

                stations.Add(CaptureStation(station, officers));
            }
        }
        catch (Exception exception)
        {
            stations.Add(new StationSnapshot(
                "station-registry",
                ProofObservation<IReadOnlyList<string>>.Unavailable($"station registry read threw: {exception.GetType().Name}"),
                ProofObservation<int>.Unavailable("station registry read threw"),
                ProofObservation<IReadOnlyList<string>>.Unavailable("station registry read threw"),
                ProofObservation<IReadOnlyList<string>>.Unavailable(DeployedVehiclesUnavailable),
                ProofObservation<float>.Unavailable("station registry read threw")));
        }

        try
        {
            foreach (var player in Player.PlayerList ?? new Il2CppSystem.Collections.Generic.List<Player>())
            {
                if (player is null)
                    continue;

                players.Add(new PlayerSnapshot(
                    player.PlayerCode?.Trim(),
                    SafeObservation(() => player.IsServerInitialized, "supported server-initialized player read")));
            }
        }
        catch (Exception exception)
        {
            players.Add(new PlayerSnapshot(
                null,
                ProofObservation<bool>.Unavailable($"player registry read threw: {exception.GetType().Name}")));
        }

        return new DispatchProofObservation(
            Sequence: ++_sequence,
            HostGameMinutes: clock,
            Stations: stations,
            Officers: officers,
            Players: players,
            IsAuthoritativeHost: ReadAuthoritativeHost(),
            IsSinglePlayer: players.Count(player => player.IsSupportedServerInitialized.IsAvailable && player.IsSupportedServerInitialized.Value) == 1,
            CanonicalTargetIdentity: ReadCanonicalIdentity());
    }

    private static StationSnapshot CaptureStation(PoliceStation station, ICollection<OfficerSnapshot> officers)
    {
        var stationIdentity = SafeIdentity(station);
        var officerIdentities = new List<string>();
        ProofObservation<int> availableVehicleCount;
        ProofObservation<IReadOnlyList<string>> policeVehicleIdentities;
        ProofObservation<float> cooldown;

        try
        {
            var pool = station.OfficerPool;
            foreach (var officer in pool ?? new Il2CppSystem.Collections.Generic.List<PoliceOfficer>())
            {
                if (officer is null)
                    continue;

                var identity = SafeIdentity(officer);
                officerIdentities.Add(identity);
                officers.Add(CaptureOfficer(officer, identity));
            }
        }
        catch (Exception exception)
        {
            officerIdentities.Clear();
            return new StationSnapshot(
                stationIdentity,
                ProofObservation<IReadOnlyList<string>>.Unavailable($"OfficerPool read threw: {exception.GetType().Name}"),
                SafeObservation(() => station.AvailableVehicleCount, "AvailableVehicleCount"),
                CapturePoliceVehicles(station),
                ProofObservation<IReadOnlyList<string>>.Unavailable(DeployedVehiclesUnavailable),
                SafeObservation(() => station.TimeSinceLastDispatch, "TimeSinceLastDispatch"));
        }

        availableVehicleCount = SafeObservation(() => station.AvailableVehicleCount, "AvailableVehicleCount");
        policeVehicleIdentities = CapturePoliceVehicles(station);
        cooldown = SafeObservation(() => station.TimeSinceLastDispatch, "TimeSinceLastDispatch");

        return new StationSnapshot(
            stationIdentity,
            ProofObservation<IReadOnlyList<string>>.Available(officerIdentities),
            availableVehicleCount,
            policeVehicleIdentities,
            ProofObservation<IReadOnlyList<string>>.Unavailable(DeployedVehiclesUnavailable),
            cooldown);
    }

    private static OfficerSnapshot CaptureOfficer(PoliceOfficer officer, string identity)
    {
        var active = SafeObservation(() => officer.gameObject is not null && officer.gameObject.activeInHierarchy, "officer active state");
        var alive = SafeObservation(() => officer.Health is not null && !officer.Health.IsDead, "officer death state");
        var target = SafeObservation(() => officer.PursuitTarget is null ? null : SafeIdentity(officer.PursuitTarget), "officer target");
        return new OfficerSnapshot(identity, active, alive, target);
    }

    private static ProofObservation<IReadOnlyList<string>> CapturePoliceVehicles(PoliceStation station)
    {
        try
        {
            var identities = new List<string>();
            foreach (var vehicle in station.PoliceVehicles ?? Array.Empty<Il2CppScheduleOne.Vehicles.LandVehicle>())
            {
                if (vehicle is not null)
                    identities.Add(SafeIdentity(vehicle));
            }

            return ProofObservation<IReadOnlyList<string>>.Available(identities);
        }
        catch (Exception exception)
        {
            return ProofObservation<IReadOnlyList<string>>.Unavailable($"PoliceVehicles read threw: {exception.GetType().Name}");
        }
    }

    private static bool ReadAuthoritativeHost()
    {
        try
        {
            var server = InstanceFinder.ServerManager;
            var client = InstanceFinder.ClientManager;
            return server is not null && client is not null && server.OneServerStarted() && client.Started;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadCanonicalIdentity()
    {
        try
        {
            return ProofCanonicalIdentity.TryGet(LoadManager.Instance?.LoadedGameFolderPath, out var identity)
                ? identity
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string SafeIdentity(UnityEngine.Object value) =>
        value.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static ProofObservation<T> SafeObservation<T>(Func<T> read, string label)
    {
        try
        {
            return ProofObservation<T>.Available(read());
        }
        catch (Exception exception)
        {
            return ProofObservation<T>.Unavailable($"{label} read threw: {exception.GetType().Name}");
        }
    }
}

public sealed record DispatchProofObservation(
    long Sequence,
    ProofObservation<long> HostGameMinutes,
    IReadOnlyList<StationSnapshot> Stations,
    IReadOnlyList<OfficerSnapshot> Officers,
    IReadOnlyList<PlayerSnapshot> Players,
    bool IsAuthoritativeHost,
    bool IsSinglePlayer,
    string? CanonicalTargetIdentity);
