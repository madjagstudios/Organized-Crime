using OrganizedCrime.PropertyProbe.Model;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class NightclubListenerLifecycleTests
{
    [Fact]
    public void Authority_drift_during_active_window_yields_stop()
    {
        var result = NightclubProbeContract.EvaluateActiveAuthority(
            hostCount: 2,
            serverStarted: true,
            clientStarted: true);

        Assert.Equal(NightclubActiveWindowDecision.Stop, result.Decision);
        Assert.Contains("authority drift", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(NightclubListenerTermination.NormalCompletion)]
    [InlineData(NightclubListenerTermination.Stop)]
    [InlineData(NightclubListenerTermination.Inconclusive)]
    [InlineData(NightclubListenerTermination.SceneChanged)]
    [InlineData(NightclubListenerTermination.AuthorityDrift)]
    [InlineData(NightclubListenerTermination.Exception)]
    [InlineData(NightclubListenerTermination.Disposal)]
    [InlineData(NightclubListenerTermination.ApplicationQuit)]
    public void Terminal_path_creates_one_cleanup_plan_per_registered_listener(NightclubListenerTermination termination)
    {
        var candidateA = NightclubDoorFingerprint.Test("Map/Hyland Point/Region_Northtown/Nightclub/desert town hall/Door A");
        var candidateB = NightclubDoorFingerprint.Test("Map/Hyland Point/Region_Northtown/Nightclub/desert town hall/Door B");
        var registry = new NightclubListenerRegistry(new[] { candidateA, candidateB });
        var registrationA = new SuccessfulRegistration(candidateA.StableIdentity);
        var registrationB = new SuccessfulRegistration(candidateB.StableIdentity);
        Assert.True(registry.MarkRegistered(registrationA));
        Assert.True(registry.MarkRegistered(registrationB));

        var firstCleanup = registry.RemovePending(termination);
        var secondCleanup = registry.RemovePending(termination);

        Assert.Equal(termination, firstCleanup.Termination);
        Assert.True(firstCleanup.TeardownSucceeded);
        Assert.Equal(new[] { candidateA.StableIdentity, candidateB.StableIdentity }, firstCleanup.RemovedDoorStableIdentities);
        Assert.Empty(secondCleanup.RemovedDoorStableIdentities);
    }

    [Fact]
    public void Partial_subscription_failure_rolls_back_previously_registered_listener()
    {
        var candidateA = NightclubDoorFingerprint.Test("Map/Hyland Point/Region_Northtown/Nightclub/desert town hall/Door A");
        var candidateB = NightclubDoorFingerprint.Test("Map/Hyland Point/Region_Northtown/Nightclub/desert town hall/Door B");
        var registry = new NightclubListenerRegistry(new[] { candidateA, candidateB });
        Assert.True(registry.MarkRegistered(new SuccessfulRegistration(candidateA.StableIdentity)));

        var rollback = registry.RemovePending(NightclubListenerTermination.PartialSubscriptionFailure);

        Assert.True(rollback.TeardownSucceeded);
        Assert.Equal(new[] { candidateA.StableIdentity }, rollback.RemovedDoorStableIdentities);
        Assert.DoesNotContain(candidateB.StableIdentity, rollback.RemovedDoorStableIdentities);
        Assert.Empty(registry.RemovePending(NightclubListenerTermination.PartialSubscriptionFailure).RemovedDoorStableIdentities);
    }

    private sealed class SuccessfulRegistration : INightclubListenerRegistration
    {
        public SuccessfulRegistration(string doorStableIdentity) => DoorStableIdentity = doorStableIdentity;

        public string DoorStableIdentity { get; }

        public NightclubListenerRemovalAttempt TryRemove() =>
            new(DoorStableIdentity, NightclubListenerRemovalDisposition.Removed, null);
    }
}
