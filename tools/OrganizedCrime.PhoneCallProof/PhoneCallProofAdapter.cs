namespace OrganizedCrime.PhoneCallProof;

public sealed class PhoneCallProofAdapter : IDisposable
{
    private readonly Func<PhoneCallProofContext> _contextProvider;
    private readonly IPhoneCallQueue _queue;
    private readonly PhoneCallProofState _state;
    private bool _disposed;

    public PhoneCallProofAdapter(
        PhoneCallProofContext context,
        IPhoneCallQueue queue,
        PhoneCallProofState? state = null)
        : this(() => context, queue, state)
    {
    }

    public PhoneCallProofAdapter(
        Func<PhoneCallProofContext> contextProvider,
        IPhoneCallQueue queue,
        PhoneCallProofState? state = null)
    {
        _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _state = state ?? new PhoneCallProofState();
    }

    public PhoneCallProofResult TryRequest(string correlation, PhoneCallProofRequest request)
    {
        if (string.IsNullOrEmpty(correlation))
            throw new ArgumentException("Correlation is required.", nameof(correlation));
        ArgumentNullException.ThrowIfNull(request);

        if (!_contextProvider().IsCanonicalSinglePlayerHost)
            return PhoneCallProofResult.NotCanonicalHost;
        if (_state.IsQueued(correlation))
            return PhoneCallProofResult.Duplicate;

        try
        {
            if (!_queue.TryQueue(request))
                return PhoneCallProofResult.Deferred;
        }
        catch
        {
            return PhoneCallProofResult.Deferred;
        }

        _state.MarkQueued(correlation);
        return PhoneCallProofResult.Queued;
    }

    public bool ObserveCompleted(string correlation)
    {
        if (string.IsNullOrEmpty(correlation))
            throw new ArgumentException("Correlation is required.", nameof(correlation));
        if (_disposed)
            return false;

        _state.MarkCompleted(correlation);
        return true;
    }

    public PhoneCallProofSnapshot Snapshot() => _state.Snapshot();

    public static PhoneCallProofAdapter Restore(
        PhoneCallProofContext context,
        IPhoneCallQueue queue,
        PhoneCallProofSnapshot snapshot) =>
        new(context, queue, PhoneCallProofState.Restore(snapshot));

    public void Dispose() => _disposed = true;
}
