namespace OrganizedCrime.PoliceDispatchProof;

public sealed class DispatchProofSubscriptionLedger : IDisposable
{
    private readonly List<Action> _detachActions = new();
    private bool _disposed;

    public bool IsDisposed => _disposed;

    public void Track(Action detach)
    {
        ArgumentNullException.ThrowIfNull(detach);
        if (_disposed)
            throw new ObjectDisposedException(nameof(DispatchProofSubscriptionLedger));

        _detachActions.Add(detach);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        for (var index = _detachActions.Count - 1; index >= 0; index--)
        {
            try
            {
                _detachActions[index]();
            }
            catch
            {
                // Cleanup is best effort; the terminal disposed state is still recorded.
            }
        }

        _detachActions.Clear();
    }
}

public static class DispatchProofLifecycle
{
    public static bool IsInitialLoadBoundary(DispatchProofPhase phase, bool loadCompleteSeen) =>
        phase == DispatchProofPhase.AwaitingLoadAuthorityIdentity && !loadCompleteSeen;
}
