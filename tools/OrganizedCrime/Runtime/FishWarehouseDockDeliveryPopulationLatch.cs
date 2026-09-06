namespace OrganizedCrime.Runtime;

/// <summary>
/// Remembers which active delivery has already populated the dock. Native dock
/// occupancy can clear while the same arrived delivery remains active; retaining
/// this identity prevents the bridge from re-seating that delivery's van.
/// </summary>
public sealed class FishWarehouseDockDeliveryPopulationLatch
{
    private string? _populatedDeliveryId;

    public bool HasPopulated(string deliveryId) =>
        !string.IsNullOrWhiteSpace(deliveryId) &&
        string.Equals(_populatedDeliveryId, deliveryId, StringComparison.Ordinal);

    public void MarkPopulated(string deliveryId)
    {
        if (!string.IsNullOrWhiteSpace(deliveryId))
            _populatedDeliveryId = deliveryId;
    }

    public void ObserveActiveDelivery(string? deliveryId)
    {
        if (!string.Equals(_populatedDeliveryId, deliveryId, StringComparison.Ordinal))
            _populatedDeliveryId = null;
    }

    public void Reset() => _populatedDeliveryId = null;
}
