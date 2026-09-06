using HarmonyLib;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.UI.Phone.Delivery;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed record FishWarehouseDeliveryRestoreFlushResult(int PendingCount, string? Failure)
{
    public string? FailureReason => Failure;
}

internal static class FishWarehouseDeliveryRestorePatch
{
    private abstract record PendingDeliveryPayload;

    private sealed record PendingDispatch(
        DeliveryManager Manager,
        DeliveryInstance Delivery) : PendingDeliveryPayload;

    private sealed record PendingStatus(
        DeliveryInstance Delivery,
        EDeliveryStatus Status) : PendingDeliveryPayload;

    private sealed record PendingStatusDisplay(
        DeliveryApp App,
        DeliveryInstance Delivery) : PendingDeliveryPayload;

    private static readonly FishWarehouseDeliveryRestoreGate<PendingDeliveryPayload> Gate =
        new(FishWarehouseLoadingDockDefinition.TargetPropertyCode);
    private static Func<bool> _isReady = () => false;
    private static Action<string> _log = _ => { };
    private static bool _replaying;
    private static string? _statusReplayDeliveryId;
    private static bool _nestedStatusDisplayObserved;
    private static bool _replayFailureReported;

    public static FishWarehouseDeliveryRestoreFlushResult LastFlushResult { get; private set; } =
        new(0, null);

    public static void Apply(HarmonyLib.Harmony harmony, Func<bool> isReady, Action<string>? log = null)
    {
        _isReady = isReady;
        _log = log ?? (_ => { });

        var sendDelivery = AccessTools.Method(
            typeof(DeliveryManager),
            nameof(DeliveryManager.SendDelivery),
            new[] { typeof(DeliveryInstance) })
            ?? throw new MissingMethodException(typeof(DeliveryManager).FullName, nameof(DeliveryManager.SendDelivery));
        var setStatus = AccessTools.Method(
            typeof(DeliveryInstance),
            nameof(DeliveryInstance.SetStatus),
            new[] { typeof(EDeliveryStatus) })
            ?? throw new MissingMethodException(typeof(DeliveryInstance).FullName, nameof(DeliveryInstance.SetStatus));
        var createStatusDisplay = AccessTools.Method(
            typeof(DeliveryApp),
            nameof(DeliveryApp.CreateDeliveryStatusDisplay),
            new[] { typeof(DeliveryInstance) })
            ?? throw new MissingMethodException(typeof(DeliveryApp).FullName, nameof(DeliveryApp.CreateDeliveryStatusDisplay));

        harmony.Patch(sendDelivery, prefix: HarmonyMethod(nameof(DispatchPrefix)));
        harmony.Patch(setStatus, prefix: HarmonyMethod(nameof(StatusPrefix)));
        harmony.Patch(createStatusDisplay, prefix: HarmonyMethod(nameof(StatusDisplayPrefix)));
    }

    private static HarmonyMethod HarmonyMethod(string methodName) =>
        new(AccessTools.Method(typeof(FishWarehouseDeliveryRestorePatch), methodName)
            ?? throw new MissingMethodException(typeof(FishWarehouseDeliveryRestorePatch).FullName, methodName));

    private static bool DispatchPrefix(DeliveryManager __instance, DeliveryInstance __0)
    {
        if (_replaying || __0 is null)
            return true;

        return ApplyGate(
            new PendingDispatch(__instance, __0),
            __0,
            FishWarehouseDeliveryActionKind.Dispatch,
            isLoading: IsLoading());
    }

    private static bool StatusPrefix(DeliveryInstance __instance, EDeliveryStatus __0)
    {
        if (_replaying || __instance is null)
            return true;

        return ApplyGate(
            new PendingStatus(__instance, __0),
            __instance,
            FishWarehouseDeliveryActionKind.Status,
            isLoading: IsLoading());
    }

    private static bool StatusDisplayPrefix(DeliveryApp __instance, DeliveryInstance __0)
    {
        if (__0 is null)
            return true;

        if (_replaying)
        {
            if (string.Equals(_statusReplayDeliveryId, __0.DeliveryID, StringComparison.Ordinal) &&
                Gate.HasPending(__0.DeliveryID ?? string.Empty, FishWarehouseDeliveryActionKind.StatusDisplay))
            {
                _nestedStatusDisplayObserved = true;
            }

            return true;
        }

        return ApplyGate(
            new PendingStatusDisplay(__instance, __0),
            __0,
            FishWarehouseDeliveryActionKind.StatusDisplay,
            isLoading: IsLoading());
    }

    private static bool ApplyGate(
        PendingDeliveryPayload payload,
        DeliveryInstance delivery,
        FishWarehouseDeliveryActionKind actionKind,
        bool isLoading)
    {
        try
        {
            var deliveryId = delivery.DeliveryID ?? string.Empty;
            var suppress = Gate.ShouldSuppress(
                payload,
                delivery.DestinationCode ?? string.Empty,
                deliveryId,
                actionKind,
                _isReady(),
                isLoading);
            if (suppress)
            {
                _log($"Fish Warehouse delivery action deferred (delivery={deliveryId}, action={actionKind}).");
            }

            return !suppress;
        }
        catch (Exception ex)
        {
            _log($"Fish Warehouse saved-delivery gate failed safely; vanilla action will continue: {ex.Message}");
            return true;
        }
    }

    internal static void ReplayUntilFailure<TDelivery>(
        IEnumerable<FishWarehousePendingDelivery<TDelivery>> pendingActions,
        Action<FishWarehousePendingDelivery<TDelivery>> replay,
        Action<FishWarehousePendingDelivery<TDelivery>, Exception> onFailure)
        where TDelivery : class
    {
        foreach (var pending in pendingActions)
        {
            try
            {
                replay(pending);
            }
            catch (Exception ex)
            {
                onFailure(pending, ex);
                break;
            }
        }
    }

    public static FishWarehouseDeliveryRestoreFlushResult FlushIfReady()
    {
        var ready = Gate.BeginReadyReplay(_isReady());
        string? failure = null;
        ReplayUntilFailure(
            ready.Where(pending => !Gate.IsReplayed(pending.DeliveryId, pending.ActionKind)),
            pending =>
            {
                try
                {
                    _replaying = true;
                    Replay(pending, ready);
                }
                finally
                {
                    _statusReplayDeliveryId = null;
                    _nestedStatusDisplayObserved = false;
                    _replaying = false;
                }
            },
            (pending, ex) =>
            {
                failure ??= $"delivery={pending.DeliveryId}, action={pending.ActionKind}: {ex.Message}";
                if (!_replayFailureReported)
                {
                    _replayFailureReported = true;
                    _log($"Fish Warehouse saved delivery action remains pending after replay failure ({failure}).");
                }
            });
        return StoreFlushResult(new FishWarehouseDeliveryRestoreFlushResult(Gate.PendingCount, failure));
    }

    private static void Replay(
        FishWarehousePendingDelivery<PendingDeliveryPayload> pending,
        IReadOnlyList<FishWarehousePendingDelivery<PendingDeliveryPayload>> ready)
    {
        switch (pending.ActionKind)
        {
            case FishWarehouseDeliveryActionKind.Dispatch:
            {
                var dispatch = (PendingDispatch)pending.Delivery;
                dispatch.Manager.SendDelivery(dispatch.Delivery);
                Gate.MarkReplayed(pending.DeliveryId, pending.ActionKind);
                _log($"Fish Warehouse delivery action replayed (delivery={pending.DeliveryId}, action=Dispatch).");
                break;
            }
            case FishWarehouseDeliveryActionKind.Status:
            {
                var status = (PendingStatus)pending.Delivery;
                _statusReplayDeliveryId = pending.DeliveryId;
                _nestedStatusDisplayObserved = false;
                status.Delivery.SetStatus(status.Status);
                Gate.MarkReplayed(pending.DeliveryId, pending.ActionKind);

                var display = ready.FirstOrDefault(candidate =>
                    candidate.DeliveryId == pending.DeliveryId &&
                    candidate.ActionKind == FishWarehouseDeliveryActionKind.StatusDisplay);
                if (display is not null && Gate.HasPending(pending.DeliveryId, FishWarehouseDeliveryActionKind.StatusDisplay))
                {
                    if (_nestedStatusDisplayObserved)
                    {
                        Gate.MarkReplayed(pending.DeliveryId, FishWarehouseDeliveryActionKind.StatusDisplay);
                        _log($"Fish Warehouse delivery action replayed (delivery={pending.DeliveryId}, action=StatusDisplay, mode=nested).");
                    }
                    else
                    {
                        ReplayStatusDisplay(display);
                    }
                }

                _log($"Fish Warehouse delivery action replayed (delivery={pending.DeliveryId}, action=Status).");
                break;
            }
            case FishWarehouseDeliveryActionKind.StatusDisplay:
                ReplayStatusDisplay(pending);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pending.ActionKind), pending.ActionKind, null);
        }
    }

    private static void ReplayStatusDisplay(FishWarehousePendingDelivery<PendingDeliveryPayload> pending)
    {
        var display = (PendingStatusDisplay)pending.Delivery;
        display.App.CreateDeliveryStatusDisplay(display.Delivery);
        Gate.MarkReplayed(pending.DeliveryId, pending.ActionKind);
        _log($"Fish Warehouse delivery action replayed (delivery={pending.DeliveryId}, action=StatusDisplay, mode=explicit).");
    }

    internal static FishWarehouseDeliveryRestoreFlushResult StoreFlushResult(
        FishWarehouseDeliveryRestoreFlushResult result)
    {
        if (result.Failure is null && LastFlushResult.Failure is not null)
            return LastFlushResult;

        LastFlushResult = result;
        return result;
    }

    public static void Reset()
    {
        Gate.Reset();
        _isReady = () => false;
        _log = _ => { };
        _replaying = false;
        _statusReplayDeliveryId = null;
        _nestedStatusDisplayObserved = false;
        _replayFailureReported = false;
        LastFlushResult = new FishWarehouseDeliveryRestoreFlushResult(0, null);
    }

    private static bool IsLoading() =>
        Il2CppScheduleOne.Persistence.LoadManager.Instance?.IsLoading == true;
}
