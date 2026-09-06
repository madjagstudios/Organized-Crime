using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>Which observation the Chief's convergence pass is being told about.</summary>
public enum Release1ChiefObservedEvent { ArrestObserved, RoseToWatched, RoseToCritical }

// OC-73 review fix (finding 5). CorrelationId used to be carried here but was never read outside one
// test assertion: DrainOne only ever reads Kind, and the notification's own correlation id belongs to
// OC-41's dispatch path, not this queue. Removed rather than kept unused.
public sealed record Release1ChiefQueuedEvent(
    Release1ChiefObservedEvent Kind, string PlayerId, Guid SessionEpoch, long LoadEpoch);

/// <summary>
/// OC-73 spec decision 11. The Chief never writes story state inside <see cref="Publish"/>: this sink
/// records the event in a bounded in memory queue and the next convergence pass drains it, so a
/// durable write never rides the arrest patch's own stack.
///
/// <see cref="LocalPressureRuntimeService"/> publishes a notification only from
/// <c>TryApplyCustodyEvidence</c>, so every notification that reaches here is an accepted arrest;
/// decay publishes nothing. That is why <see cref="Publish"/> always queues an
/// <see cref="Release1ChiefObservedEvent.ArrestObserved"/>, and additionally queues the crossing when
/// the edge is one of the two OC-41 already acts on. Arrest first, crossing second, so a pass that
/// opens the demand on the arrest can still send the watch line for the same event on a later pass.
/// The queue is bounded so a runaway publisher cannot grow it without limit; overflow drops the oldest
/// and counts the drop, which the convergence pass logs once per load.
/// </summary>
public sealed class Release1ChiefTierObserver : ILocalPressureTierTransitionSink
{
    public const int MaximumQueueDepth = 32;

    private readonly object _gate = new();
    private readonly Queue<Release1ChiefQueuedEvent> _queue = new();

    public int QueueDepth { get { lock (_gate) return _queue.Count; } }
    public int DroppedCount { get; private set; }

    public void Publish(LocalPressureTierTransitionNotification notification)
    {
        if (notification is null) return;
        lock (_gate)
        {
            Enqueue(notification, Release1ChiefObservedEvent.ArrestObserved);
            if (notification.CurrentTier <= notification.PreviousTier) return;
            if (notification.CurrentTier == LocalPressureTier.Watched)
                Enqueue(notification, Release1ChiefObservedEvent.RoseToWatched);
            else if (notification.CurrentTier == LocalPressureTier.Critical)
                Enqueue(notification, Release1ChiefObservedEvent.RoseToCritical);
        }
    }

    public void ResetForEpoch(Guid sessionEpoch, long loadEpoch) => Clear();

    /// <summary>
    /// Takes the oldest queued event for exactly this player, session epoch and load epoch. Anything
    /// else is left where it is: only <see cref="Clear"/> and <see cref="ResetForEpoch"/> ever
    /// discard, so a mismatched drain can never silently eat an event the correct pass wanted.
    /// </summary>
    public bool TryDequeue(string playerId, Guid sessionEpoch, long loadEpoch, out Release1ChiefQueuedEvent queued)
    {
        queued = null!;
        lock (_gate)
        {
            if (_queue.Count == 0) return false;
            var head = _queue.Peek();
            if (!string.Equals(head.PlayerId, playerId, StringComparison.Ordinal) ||
                head.SessionEpoch != sessionEpoch || head.LoadEpoch != loadEpoch) return false;
            queued = _queue.Dequeue();
            return true;
        }
    }

    public void Clear() { lock (_gate) _queue.Clear(); }

    private void Enqueue(LocalPressureTierTransitionNotification notification, Release1ChiefObservedEvent kind)
    {
        while (_queue.Count >= MaximumQueueDepth) { _queue.Dequeue(); DroppedCount++; }
        _queue.Enqueue(new Release1ChiefQueuedEvent(
            kind, notification.PlayerId, notification.SessionEpoch, notification.LoadEpoch));
    }
}
