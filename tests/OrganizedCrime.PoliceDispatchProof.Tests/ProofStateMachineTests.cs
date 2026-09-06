using OrganizedCrime.PoliceDispatchProof;
using Xunit;

namespace OrganizedCrime.PoliceDispatchProof.Tests;

public sealed class ProofStateMachineTests
{
    [Fact]
    public void Starts_detached_and_accepts_only_the_required_ordered_phases()
    {
        var machine = new DispatchProofStateMachine();

        Assert.Equal(DispatchProofPhase.Detached, machine.Phase);
        Assert.True(machine.TryTransition(DispatchProofPhase.Detached, DispatchProofPhase.AwaitingLoadAuthorityIdentity, "load"));
        Assert.True(machine.TryTransition(DispatchProofPhase.AwaitingLoadAuthorityIdentity, DispatchProofPhase.BaselineReady, "baseline"));
        Assert.True(machine.TryTransition(DispatchProofPhase.BaselineReady, DispatchProofPhase.Response1Armed, "arm-1"));
        Assert.True(machine.TryTransition(DispatchProofPhase.Response1Armed, DispatchProofPhase.Response1Invoked, "invoke-1"));
        Assert.True(machine.TryTransition(DispatchProofPhase.Response1Invoked, DispatchProofPhase.ObservingRecovery1, "observe-1"));
        Assert.True(machine.TryTransition(DispatchProofPhase.ObservingRecovery1, DispatchProofPhase.Response2Eligible, "eligible-2"));
        Assert.True(machine.TryTransition(DispatchProofPhase.Response2Eligible, DispatchProofPhase.Response2Invoked, "invoke-2"));
        Assert.True(machine.TryTransition(DispatchProofPhase.Response2Invoked, DispatchProofPhase.ObservingRecovery2, "observe-2"));
        Assert.True(machine.TryTransition(DispatchProofPhase.ObservingRecovery2, DispatchProofPhase.TeardownPending, "teardown"));
        Assert.True(machine.TryTransition(DispatchProofPhase.TeardownPending, DispatchProofPhase.Complete, "complete"));
    }

    [Fact]
    public void Rejects_phase_skips_duplicate_keys_and_terminal_reentry()
    {
        var machine = new DispatchProofStateMachine();

        Assert.False(machine.TryTransition(DispatchProofPhase.Detached, DispatchProofPhase.BaselineReady, "skip"));
        Assert.True(machine.TryTransition(DispatchProofPhase.Detached, DispatchProofPhase.AwaitingLoadAuthorityIdentity, "load"));
        Assert.False(machine.TryTransition(DispatchProofPhase.AwaitingLoadAuthorityIdentity, DispatchProofPhase.BaselineReady, "load"));
        Assert.True(machine.TryTransition(DispatchProofPhase.AwaitingLoadAuthorityIdentity, DispatchProofPhase.BaselineReady, "baseline"));
        Assert.True(machine.TryTransition(DispatchProofPhase.BaselineReady, DispatchProofPhase.Stop, "stop"));
        Assert.False(machine.TryTransition(DispatchProofPhase.Stop, DispatchProofPhase.Detached, "restart"));
    }
}
