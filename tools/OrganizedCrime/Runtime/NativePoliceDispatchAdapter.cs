using System.Globalization;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.PlayerScripts;

namespace OrganizedCrime.Runtime;

internal interface INativePoliceDispatchBoundary
{
    bool IsMorePatrolsPresent();
    bool TryGetCanonicalTarget(object sourcePlayer, out object canonicalTarget);
    bool TryReadTargetIdentity(
        object canonicalTarget,
        out bool isServerInitialized,
        out object? connection,
        out string? playerCode,
        out string failure);
    object? GetClosestPoliceStation(object canonicalTarget);
    bool TrySnapshot(object station, out NativeDispatchStationSnapshot snapshot);
    void RequestResponse(object station, object canonicalTarget);
}

internal sealed class NativePoliceDispatchBoundary : INativePoliceDispatchBoundary
{
    public bool IsMorePatrolsPresent() =>
        AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            string.Equals(assembly.GetName().Name, "MorePatrols", StringComparison.OrdinalIgnoreCase));

    public bool TryGetCanonicalTarget(object sourcePlayer, out object canonicalTarget)
    {
        if (sourcePlayer is Player player)
        {
            canonicalTarget = player;
            return true;
        }

        canonicalTarget = null!;
        return false;
    }

    public bool TryReadTargetIdentity(
        object canonicalTarget,
        out bool isServerInitialized,
        out object? connection,
        out string? playerCode,
        out string failure)
    {
        try
        {
            var player = (Player)canonicalTarget;
            isServerInitialized = player.IsServerInitialized;
            connection = player.Connection;
            playerCode = player.PlayerCode?.Trim();
            failure = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            isServerInitialized = false;
            connection = null;
            playerCode = null;
            failure = $"native player identity read failed: {exception.GetType().Name}";
            return false;
        }
    }

    public object? GetClosestPoliceStation(object canonicalTarget)
    {
        var player = (Player)canonicalTarget;
        return PoliceStation.GetClosestPoliceStation(player.transform.position);
    }

    public bool TrySnapshot(object stationHandle, out NativeDispatchStationSnapshot snapshot)
    {
        snapshot = null!;

        try
        {
            var station = (PoliceStation)stationHandle;
            var officers = new List<string>();
            var pool = station.OfficerPool;
            if (pool is null)
                return false;

            foreach (var officer in pool)
            {
                if (officer is not null)
                    officers.Add(officer.GetInstanceID().ToString(CultureInfo.InvariantCulture));
            }

            snapshot = new NativeDispatchStationSnapshot(
                station.GetInstanceID().ToString(CultureInfo.InvariantCulture),
                officers,
                station.AvailableVehicleCount);
            return true;
        }
        catch
        {
            snapshot = null!;
            return false;
        }
    }

    public void RequestResponse(object stationHandle, object canonicalTargetHandle)
    {
        var station = (PoliceStation)stationHandle;
        var canonicalTarget = (Player)canonicalTargetHandle;
        station.Dispatch(2, canonicalTarget, PoliceStation.EDispatchType.UseVehicle, false);
    }
}

public sealed class NativePoliceDispatchAdapter : INativeLawResponseAdapter
{
    private readonly ILocalPressureRuntimeHostAdapter _runtimeHost;
    private readonly INativePoliceDispatchBoundary _nativeBoundary;
    private readonly Action<string> _log;
    private bool _morePatrolsSuppressionLogged;

    public NativePoliceDispatchAdapter(
        ILocalPressureRuntimeHostAdapter runtimeHost,
        Action<string>? log = null)
        : this(runtimeHost, new NativePoliceDispatchBoundary(), log)
    {
    }

    internal NativePoliceDispatchAdapter(
        ILocalPressureRuntimeHostAdapter runtimeHost,
        INativePoliceDispatchBoundary nativeBoundary,
        Action<string>? log = null)
    {
        _runtimeHost = runtimeHost ?? throw new ArgumentNullException(nameof(runtimeHost));
        _nativeBoundary = nativeBoundary ?? throw new ArgumentNullException(nameof(nativeBoundary));
        _log = log ?? (_ => { });
    }

    public NativeLawResponseResult TryRequest(NativeLawResponseRequest request)
    {
        if (!TryReadAuthoritativeSinglePlayerHost(request, out var sample, out var hostFailure))
            return hostFailure!;
        if (IsMorePatrolsPresent())
            return Result(NativeLawResponseResultState.Rejected, request, "MorePatrols is loaded; coexistence is not approved");
        if (!TryValidateCanonicalTarget(request, sample, out var target, out var targetFailure))
            return Result(NativeLawResponseResultState.TargetRejected, request, targetFailure);

        object? station;
        try
        {
            station = _nativeBoundary.GetClosestPoliceStation(target);
        }
        catch (Exception exception)
        {
            return AdapterFailure(request, "closest station", exception);
        }

        if (station is null)
            return Result(NativeLawResponseResultState.AdapterUnavailable, request, "closest police station was unavailable");

        if (!_nativeBoundary.TrySnapshot(station, out var before))
            return Result(NativeLawResponseResultState.AdapterUnavailable, request, "station preflight could not be observed");
        if (before.OfficerIdentities.Count < 2 || before.AvailableVehicleCount < 1)
            return UnavailableCapacity(request, before);

        try
        {
            _nativeBoundary.RequestResponse(station, target);
        }
        catch (Exception exception)
        {
            return AdapterFailure(request, "Dispatch", exception);
        }

        return _nativeBoundary.TrySnapshot(station, out var after)
            ? NativeDispatchOutcomeEvaluator.Evaluate(before, after, request.Profile, request.CorrelationId)
            : Result(NativeLawResponseResultState.InconclusiveOutcome, request, "post-dispatch outcome was unavailable");
    }

    private bool TryReadAuthoritativeSinglePlayerHost(
        NativeLawResponseRequest request,
        out LocalPressurePlayerSample sample,
        out NativeLawResponseResult? failure)
    {
        sample = default;
        failure = null;

        switch (_runtimeHost.ReadHostAuthority())
        {
            case LocalPressureHostAuthorityReadStatus.Ready:
                break;
            case LocalPressureHostAuthorityReadStatus.Faulted:
                failure = Result(NativeLawResponseResultState.AdapterUnavailable, request, "host authority read faulted");
                return false;
            case LocalPressureHostAuthorityReadStatus.Pending:
            case LocalPressureHostAuthorityReadStatus.NotAuthoritative:
            default:
                failure = Result(NativeLawResponseResultState.AuthorityRejected, request, "authoritative single-player host was unavailable");
                return false;
        }

        switch (_runtimeHost.TryReadSupportedPlayers(out var players))
        {
            case LocalPressurePlayerReadStatus.Ready when players.Count == 1:
                sample = players[0];
                return true;
            case LocalPressurePlayerReadStatus.Faulted:
                failure = Result(NativeLawResponseResultState.AdapterUnavailable, request, "supported player read faulted");
                return false;
            case LocalPressurePlayerReadStatus.Ready:
            case LocalPressurePlayerReadStatus.Pending:
            case LocalPressurePlayerReadStatus.UnsupportedMultiplayer:
            default:
                failure = Result(NativeLawResponseResultState.TargetRejected, request, "supported single-player host target was unavailable");
                return false;
        }
    }

    private bool TryValidateCanonicalTarget(
        NativeLawResponseRequest request,
        LocalPressurePlayerSample sample,
        out object target,
        out string failure)
    {
        target = null!;
        failure = string.Empty;

        if (!ReferenceEquals(sample.SourcePlayer, request.SourcePlayer))
        {
            failure = "supported player sample did not match the requested source player";
            return false;
        }

        if (!_nativeBoundary.TryGetCanonicalTarget(sample.SourcePlayer, out target))
        {
            failure = "requested source player was not the native Player interop type";
            return false;
        }

        if (!sample.IsHostOwned || !sample.HasConnection)
        {
            failure = "requested source player was not host-owned and connected";
            return false;
        }

        if (!_nativeBoundary.TryReadTargetIdentity(
                target,
                out var isServerInitialized,
                out var connection,
                out var playerCode,
                out failure))
            return false;

        if (!isServerInitialized || connection is null)
        {
            failure = "native player was not server-initialized and connected";
            return false;
        }

        if (!LocalPressureHostLifecyclePolicies.IsCanonicalPlayerIdentity(playerCode) ||
            !string.Equals(playerCode, request.PlayerId, StringComparison.Ordinal))
        {
            failure = "native player identity did not match the canonical request identity";
            return false;
        }

        return true;
    }

    private bool IsMorePatrolsPresent()
    {
        var present = _nativeBoundary.IsMorePatrolsPresent();
        if (present && !_morePatrolsSuppressionLogged)
        {
            _morePatrolsSuppressionLogged = true;
            _log("MorePatrols is loaded; native police dispatch response suppressed.");
        }

        return present;
    }

    private static NativeLawResponseResult UnavailableCapacity(
        NativeLawResponseRequest request,
        NativeDispatchStationSnapshot before) =>
        new(
            NativeLawResponseResultState.UnavailableCapacity,
            request.CorrelationId,
            "station preflight capacity was unavailable",
            new NativeLawResponseDiagnostics(
                before.StationIdentity,
                before.OfficerIdentities.Count,
                OfficersConsumed: null,
                before.AvailableVehicleCount,
                VehiclesConsumed: null));

    private static NativeLawResponseResult AdapterFailure(
        NativeLawResponseRequest request,
        string operation,
        Exception exception) =>
        Result(
            NativeLawResponseResultState.AdapterUnavailable,
            request,
            $"native police dispatch {operation} failed: {exception.GetType().Name}");

    private static NativeLawResponseResult Result(
        NativeLawResponseResultState state,
        NativeLawResponseRequest request,
        string reason) =>
        new(state, request.CorrelationId, reason);
}
