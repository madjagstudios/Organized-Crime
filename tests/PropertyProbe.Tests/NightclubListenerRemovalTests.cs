using OrganizedCrime.PropertyProbe.Model;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class NightclubListenerRemovalTests
{
    [Fact]
    public void IntObj_reassignment_removes_from_original_subscribed_target()
    {
        var candidate = NightclubDoorFingerprint.Test();
        var originalTarget = new FakeRegistration(candidate.StableIdentity, "original IntObj", NightclubListenerRemovalDisposition.Removed);
        var reassignedCurrentTarget = new FakeRegistration(candidate.StableIdentity, "reassigned IntObj", NightclubListenerRemovalDisposition.Removed);
        var registry = new NightclubListenerRegistry(new[] { candidate });
        Assert.True(registry.MarkRegistered(originalTarget));

        var cleanup = registry.RemovePending(NightclubListenerTermination.NormalCompletion);

        Assert.True(cleanup.TeardownSucceeded);
        Assert.Equal(new[] { candidate.StableIdentity }, cleanup.RemovedDoorStableIdentities);
        Assert.Equal(1, originalTarget.SuccessfulRemovals);
        Assert.Equal(0, reassignedCurrentTarget.RemovalAttempts);
    }

    [Fact]
    public void Destroyed_subscribed_target_fails_closed_without_successful_teardown()
    {
        var candidate = NightclubDoorFingerprint.Test();
        var destroyedTarget = new FakeRegistration(candidate.StableIdentity, "destroyed IntObj", NightclubListenerRemovalDisposition.TargetUnavailable);
        var registry = new NightclubListenerRegistry(new[] { candidate });
        Assert.True(registry.MarkRegistered(destroyedTarget));

        var cleanup = registry.RemovePending(NightclubListenerTermination.SceneChanged);

        Assert.False(cleanup.TeardownSucceeded);
        Assert.Empty(cleanup.RemovedDoorStableIdentities);
        Assert.Equal(new[] { candidate.StableIdentity }, cleanup.PendingDoorStableIdentities);
        Assert.Equal(NightclubListenerRemovalDisposition.TargetUnavailable, cleanup.FailedRemovals.Single().Disposition);
    }

    [Fact]
    public void Failed_removal_stays_pending_for_disposal_retry_without_repeating_successful_removal()
    {
        var candidateA = NightclubDoorFingerprint.Test("Map/Hyland Point/Region_Northtown/Nightclub/desert town hall/Door A");
        var candidateB = NightclubDoorFingerprint.Test("Map/Hyland Point/Region_Northtown/Nightclub/desert town hall/Door B");
        var failingThenSuccessful = new FakeRegistration(
            candidateA.StableIdentity,
            "A IntObj",
            NightclubListenerRemovalDisposition.Failed,
            NightclubListenerRemovalDisposition.Removed);
        var immediatelySuccessful = new FakeRegistration(candidateB.StableIdentity, "B IntObj", NightclubListenerRemovalDisposition.Removed);
        var registry = new NightclubListenerRegistry(new[] { candidateA, candidateB });
        Assert.True(registry.MarkRegistered(failingThenSuccessful));
        Assert.True(registry.MarkRegistered(immediatelySuccessful));

        var firstCleanup = registry.RemovePending(NightclubListenerTermination.Exception);
        var disposalRetry = registry.RemovePending(NightclubListenerTermination.Disposal);
        var finalRetry = registry.RemovePending(NightclubListenerTermination.Disposal);

        Assert.False(firstCleanup.TeardownSucceeded);
        Assert.Equal(new[] { candidateB.StableIdentity }, firstCleanup.RemovedDoorStableIdentities);
        Assert.Equal(new[] { candidateA.StableIdentity }, firstCleanup.PendingDoorStableIdentities);
        Assert.True(disposalRetry.TeardownSucceeded);
        Assert.Equal(new[] { candidateA.StableIdentity }, disposalRetry.RemovedDoorStableIdentities);
        Assert.Empty(finalRetry.RemovedDoorStableIdentities);
        Assert.Equal(1, immediatelySuccessful.SuccessfulRemovals);
        Assert.Equal(1, failingThenSuccessful.SuccessfulRemovals);
        Assert.Equal(2, failingThenSuccessful.RemovalAttempts);
    }

    private sealed class FakeRegistration : INightclubListenerRegistration
    {
        private readonly Queue<NightclubListenerRemovalDisposition> _outcomes;

        public FakeRegistration(string doorStableIdentity, string targetName, params NightclubListenerRemovalDisposition[] outcomes)
        {
            DoorStableIdentity = doorStableIdentity;
            TargetName = targetName;
            _outcomes = new Queue<NightclubListenerRemovalDisposition>(outcomes);
        }

        public string DoorStableIdentity { get; }

        public string TargetName { get; }

        public int RemovalAttempts { get; private set; }

        public int SuccessfulRemovals { get; private set; }

        public NightclubListenerRemovalAttempt TryRemove()
        {
            RemovalAttempts++;
            var outcome = _outcomes.Dequeue();
            if (outcome == NightclubListenerRemovalDisposition.Removed)
                SuccessfulRemovals++;

            return new(DoorStableIdentity, outcome, TargetName);
        }
    }
}
