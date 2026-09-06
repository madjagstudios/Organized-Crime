namespace OrganizedCrime.Runtime;

public enum FishWarehouseDeliveryActionKind
{
    Dispatch,
    Status,
    StatusDisplay
}

public sealed record FishWarehousePendingDelivery<TDelivery>(
    string DeliveryId,
    TDelivery Delivery,
    FishWarehouseDeliveryActionKind ActionKind = FishWarehouseDeliveryActionKind.Dispatch)
    where TDelivery : class;

public sealed class FishWarehouseDeliveryRestoreGate<TDelivery>
    where TDelivery : class
{
    private readonly record struct DeliveryActionKey(
        string DeliveryId,
        FishWarehouseDeliveryActionKind ActionKind);

    private readonly string _targetPropertyCode;
    private readonly Dictionary<DeliveryActionKey, TDelivery> _pending = new();
    private readonly Dictionary<DeliveryActionKey, long> _sequence = new();
    private readonly HashSet<DeliveryActionKey> _attempted = new();
    private readonly HashSet<DeliveryActionKey> _replayed = new();
    private long _nextSequence;

    public FishWarehouseDeliveryRestoreGate(string targetPropertyCode)
    {
        _targetPropertyCode = targetPropertyCode;
    }

    public int PendingCount => _pending.Count;

    public bool ShouldSuppress(
        TDelivery delivery,
        string destinationCode,
        string deliveryId,
        FishWarehouseDeliveryActionKind actionKind,
        bool targetReady,
        bool isLoading)
    {
        if (!string.Equals(destinationCode, _targetPropertyCode, StringComparison.Ordinal))
            return false;
        if (string.IsNullOrWhiteSpace(deliveryId))
            throw new ArgumentException("Fish Warehouse delivery identity is required.", nameof(deliveryId));

        var key = new DeliveryActionKey(deliveryId, actionKind);
        // Dispatch and status-display replay entries represent one idempotent
        // restore action. A status replay only represents the saved status,
        // though; later native transitions (for example Arrived -> Completed)
        // must continue through the game.
        if (actionKind != FishWarehouseDeliveryActionKind.Status && _replayed.Contains(key))
            return true;
        if (!ShouldDefer(actionKind, targetReady, isLoading))
            return false;

        if (_pending.TryAdd(key, delivery))
            _sequence[key] = _nextSequence++;
        return true;
    }

    // Kept for callers compiled against the dispatch-only gate. The action-key
    // overload above is the policy used by the delivery patches.
    public bool ShouldSuppress(
        TDelivery delivery,
        string destinationCode,
        string deliveryId,
        bool targetReady,
        bool isRestoreContext)
    {
        if (!isRestoreContext)
            return false;

        return ShouldSuppress(
            delivery,
            destinationCode,
            deliveryId,
            FishWarehouseDeliveryActionKind.Dispatch,
            targetReady,
            isLoading: false);
    }

    public IReadOnlyList<FishWarehousePendingDelivery<TDelivery>> BeginReadyReplay(bool targetReady)
    {
        if (!targetReady)
            return Array.Empty<FishWarehousePendingDelivery<TDelivery>>();

        return _pending
            .Where(entry => _attempted.Add(entry.Key))
            .OrderBy(entry => ActionOrder(entry.Key.ActionKind))
            .ThenBy(entry => _sequence[entry.Key])
            .Select(entry => new FishWarehousePendingDelivery<TDelivery>(
                entry.Key.DeliveryId,
                entry.Value,
                entry.Key.ActionKind))
            .ToArray();
    }

    public void MarkReplayed(string deliveryId, FishWarehouseDeliveryActionKind actionKind)
    {
        var key = new DeliveryActionKey(deliveryId, actionKind);
        _pending.Remove(key);
        _sequence.Remove(key);
        _attempted.Remove(key);
        _replayed.Add(key);
    }

    public void MarkReplayed(string deliveryId) =>
        MarkReplayed(deliveryId, FishWarehouseDeliveryActionKind.Dispatch);

    public bool HasPending(string deliveryId) =>
        _pending.Keys.Any(key => string.Equals(key.DeliveryId, deliveryId, StringComparison.Ordinal));

    public bool HasPending(string deliveryId, FishWarehouseDeliveryActionKind actionKind) =>
        _pending.ContainsKey(new DeliveryActionKey(deliveryId, actionKind));

    public bool IsReplayed(string deliveryId, FishWarehouseDeliveryActionKind actionKind) =>
        _replayed.Contains(new DeliveryActionKey(deliveryId, actionKind));

    public void Reset()
    {
        _pending.Clear();
        _sequence.Clear();
        _attempted.Clear();
        _replayed.Clear();
        _nextSequence = 0;
    }

    private static bool ShouldDefer(
        FishWarehouseDeliveryActionKind actionKind,
        bool targetReady,
        bool isLoading) =>
        actionKind switch
        {
            FishWarehouseDeliveryActionKind.Dispatch => !targetReady,
            FishWarehouseDeliveryActionKind.Status => isLoading && !targetReady,
            FishWarehouseDeliveryActionKind.StatusDisplay => isLoading && !targetReady,
            _ => throw new ArgumentOutOfRangeException(nameof(actionKind), actionKind, null)
        };

    private static int ActionOrder(FishWarehouseDeliveryActionKind actionKind) =>
        actionKind switch
        {
            FishWarehouseDeliveryActionKind.Dispatch => 0,
            FishWarehouseDeliveryActionKind.Status => 1,
            FishWarehouseDeliveryActionKind.StatusDisplay => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(actionKind), actionKind, null)
        };
}
