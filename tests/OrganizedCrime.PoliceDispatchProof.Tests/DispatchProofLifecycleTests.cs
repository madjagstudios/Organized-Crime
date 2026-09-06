using OrganizedCrime.PoliceDispatchProof;
using Xunit;

namespace OrganizedCrime.PoliceDispatchProof.Tests;

public sealed class DispatchProofLifecycleTests
{
    [Fact]
    public void Detaches_tracked_handlers_once_and_is_idempotent()
    {
        var detachCount = 0;
        using (var ledger = new DispatchProofSubscriptionLedger())
        {
            ledger.Track(() => detachCount++);
            ledger.Dispose();
            ledger.Dispose();
            Assert.True(ledger.IsDisposed);
        }

        Assert.Equal(1, detachCount);
    }

    [Fact]
    public void Rejects_new_subscriptions_after_disposal()
    {
        var ledger = new DispatchProofSubscriptionLedger();
        ledger.Dispose();

        Assert.Throws<ObjectDisposedException>(() => ledger.Track(() => { }));
    }

    [Fact]
    public void Initial_preload_before_first_load_complete_is_not_a_terminal_stop()
    {
        Assert.True(DispatchProofLifecycle.IsInitialLoadBoundary(
            DispatchProofPhase.AwaitingLoadAuthorityIdentity,
            loadCompleteSeen: false));
        Assert.False(DispatchProofLifecycle.IsInitialLoadBoundary(
            DispatchProofPhase.AwaitingLoadAuthorityIdentity,
            loadCompleteSeen: true));
        Assert.False(DispatchProofLifecycle.IsInitialLoadBoundary(
            DispatchProofPhase.Response1Armed,
            loadCompleteSeen: false));
    }
}
