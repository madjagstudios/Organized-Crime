using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Vehicles;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-7 van→dock population bridge. Delivered goods live in the delivery van's
/// storage; a <see cref="LoadingDock"/> only exposes them as a route source once
/// its private <c>SetOccupant(van)</c> runs, which the native
/// <c>VehicleDetector → RefreshOccupant → SetOccupant</c> chain normally triggers.
/// On Organized Crime's Barn-clone docks that chain fires only intermittently, so
/// a van can sit at the bay with the dock still empty and an employee's
/// dock→interior route never receives a source (the employee stays idle).
///
/// This host re-runs the native population deterministically. It is host authority
/// only, scoped to the property's own active delivery, and acts only while the
/// target dock is unoccupied: it first forces the native <c>RefreshOccupant()</c>
/// (the detector path), and only if the delivery has arrived but the dock is still
/// empty does it call the native <c>SetOccupant(van)</c> directly from the
/// delivery's own vehicle. Il2CppInterop exposes both methods as public managed
/// wrappers, so no reflection is needed.
///
/// It never re-runs population on an already-occupied dock — toggling
/// <c>DynamicOccupant</c> mid-drain re-feeds the van's slots and over-stacks (the
/// native conservation hazard). It adds no storage: the dock's OutputSlots proxy
/// the van's slots by reference, exactly as vanilla. All effects are native and
/// self-clearing (the van departs and the delivery completes on the native path).
/// </summary>
public sealed class FishWarehouseDockDeliveryBridgeHost
{
    private readonly FishWarehouseDockDeliveryPopulationLatch _populationLatch = new();

    /// <summary>Per-tick bridge. Safe to call every frame while owned; host authority required.</summary>
    public void Tick(Property property, bool hostAuthority, Action<string>? log = null)
    {
        if (!hostAuthority || property is null || property == null)
            return;

        try
        {
            DeliveryInstance? delivery = ResolveActiveDelivery(property);
#if DEBUG
            ProbeDocks(property, log);
#endif
            if (delivery is null)
            {
                _populationLatch.ObserveActiveDelivery(null);
                return;
            }

            string deliveryId = SafeDeliveryId(delivery);
            _populationLatch.ObserveActiveDelivery(deliveryId);
            if (string.IsNullOrWhiteSpace(deliveryId) || _populationLatch.HasPopulated(deliveryId))
                return;

            LoadingDock? dock = SafeDock(delivery);
            if (dock is null)
                return;

            // Act only while the dock is unoccupied. Once it has an occupant we must
            // not re-run population: re-seating DynamicOccupant mid-drain re-feeds the
            // van's OutputSlots and over-stacks (native conservation hazard).
            if (HasOccupant(dock))
                return;

            // First, the native detector path: RefreshOccupant reads the dock's own
            // VehicleDetector.closestVehicle and calls SetOccupant. This is the exact
            // native mechanism, just re-run deterministically instead of waiting on
            // the (intermittent) trigger event.
            TryRefresh(dock);
            if (HasOccupant(dock))
            {
                _populationLatch.MarkPopulated(deliveryId);
                log?.Invoke("[oc7-bridge] dock populated via native RefreshOccupant.");
                return;
            }

            // The detector did not settle the van. If the delivery reports the van
            // has arrived, populate directly from the delivery's own vehicle.
            if (SafeStatus(delivery) != EDeliveryStatus.Arrived)
                return;

            LandVehicle? van = SafeVan(delivery);
            if (van is null || van == null)
                return;

            dock.SetOccupant(van);
            if (HasOccupant(dock))
            {
                _populationLatch.MarkPopulated(deliveryId);
                log?.Invoke("[oc7-bridge] dock populated via direct SetOccupant (arrived delivery van).");
            }
        }
        catch
        {
            // The bridge must never throw into the owned-features loop.
        }
    }

    private static DeliveryInstance? ResolveActiveDelivery(Property property)
    {
        try
        {
            if (!NetworkSingleton<DeliveryManager>.InstanceExists)
                return null;

            DeliveryInstance? delivery = NetworkSingleton<DeliveryManager>.Instance.GetDelivery(property);
            return (delivery is not null && delivery != null) ? delivery : null;
        }
        catch
        {
            return null;
        }
    }

    private static LoadingDock? SafeDock(DeliveryInstance delivery)
    {
        try
        {
            LoadingDock? dock = delivery.LoadingDock;
            return (dock is not null && dock != null) ? dock : null;
        }
        catch
        {
            return null;
        }
    }

    private static string SafeDeliveryId(DeliveryInstance delivery)
    {
        try
        {
            return delivery.DeliveryID ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static LandVehicle? SafeVan(DeliveryInstance delivery)
    {
        try
        {
            DeliveryVehicle? active = delivery.ActiveVehicle;
            if (active is null || active == null)
                return null;

            LandVehicle? van = active.Vehicle;
            return (van is not null && van != null) ? van : null;
        }
        catch
        {
            return null;
        }
    }

    private static EDeliveryStatus SafeStatus(DeliveryInstance delivery)
    {
        try
        {
            return delivery.Status;
        }
        catch
        {
            // Treat an unreadable status as not-yet-arrived: fail closed rather than
            // populating a dock whose van may still be in transit.
            return EDeliveryStatus.InTransit;
        }
    }

    private static bool HasOccupant(LoadingDock dock)
    {
        try
        {
            LandVehicle? occupant = dock.DynamicOccupant;
            return occupant is not null && occupant != null;
        }
        catch
        {
            // Fail closed: if occupancy can't be read, assume occupied so the bridge
            // does not risk a mid-drain re-seat.
            return true;
        }
    }

    private static void TryRefresh(LoadingDock dock)
    {
        try
        {
            dock.RefreshOccupant();
        }
        catch
        {
            // RefreshOccupant is best-effort; the direct SetOccupant fallback follows.
        }
    }

#if DEBUG
    // Concise, change-only dock telemetry: logs a dock line only when its occupant
    // or its non-empty output-slot summary changes (van arrives, items appear,
    // items drain) — enough to evidence item conservation without per-frame spam.
    private readonly Dictionary<int, string> _lastDockSummary = new();

    private void ProbeDocks(Property property, Action<string>? log)
    {
        try
        {
            if (property.LoadingDocks is null)
                return;

            int index = 0;
            foreach (var dock in property.LoadingDocks)
            {
                int i = index++;
                if (dock is null || dock == null)
                    continue;

                string occupant = "none";
                try
                {
                    var van = dock.DynamicOccupant;
                    occupant = (van is not null && van != null) ? van.name : "none";
                }
                catch { occupant = "<err>"; }

                var outs = new List<string>();
                try
                {
                    foreach (var slot in dock.OutputSlots)
                    {
                        string item = SafeStr(() => slot.ItemInstance?.GetItemData()?.ID ?? "<empty>");
                        if (item == "<empty>")
                            continue;
                        outs.Add($"{item}:{SafeStr(() => slot.Quantity.ToString())}");
                    }
                }
                catch { outs.Add("<err>"); }

                string summary = $"occupant={occupant};out=[{string.Join(",", outs)}]";
                if (_lastDockSummary.TryGetValue(i, out string? previous) && previous == summary)
                    continue;

                _lastDockSummary[i] = summary;
                log?.Invoke($"[oc7-dock] dock{i}: {summary}.");
            }
        }
        catch
        {
            // Telemetry must never disturb the bridge.
        }
    }

    private static string SafeStr(Func<string> read)
    {
        try { return read(); } catch { return "<err>"; }
    }
#endif
}
