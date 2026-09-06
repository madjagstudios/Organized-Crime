using OrganizedCrime.PoliceDispatchProof;
using Xunit;

namespace OrganizedCrime.PoliceDispatchProof.Tests;

public sealed class ProofAdmissionGateTests
{
    [Fact]
    public void Allows_exactly_two_owner_calls_and_rejects_duplicate_or_wrong_stage_triggers()
    {
        var machine = new DispatchProofStateMachine();
        Move(machine, DispatchProofPhase.AwaitingLoadAuthorityIdentity, "load");
        Move(machine, DispatchProofPhase.BaselineReady, "baseline");
        Move(machine, DispatchProofPhase.Response1Armed, "arm-1");
        var admission = new DispatchProofAdmission(machine);
        var context = ValidContext();

        var first = admission.TryAdmit(DispatchProofTrigger.Response1, "owner-f9", context);
        var duplicate = admission.TryAdmit(DispatchProofTrigger.Response1, "owner-f9-repeat", context);
        Move(machine, DispatchProofPhase.ObservingRecovery1, "observe-1");
        Move(machine, DispatchProofPhase.Response2Eligible, "eligible-2");
        var second = admission.TryAdmit(DispatchProofTrigger.Response2, "owner-f10", context);
        var third = admission.TryAdmit(DispatchProofTrigger.Response2, "owner-f10-repeat", context);

        Assert.True(first.Accepted);
        Assert.False(duplicate.Accepted);
        Assert.True(second.Accepted);
        Assert.False(third.Accepted);
        Assert.Equal(2, admission.AcceptedCallCount);
    }

    [Theory]
    [InlineData(false, true, "canonical", "canonical", false, "authority")]
    [InlineData(true, false, "canonical", "canonical", false, "single-player")]
    [InlineData(true, true, null, "canonical", false, "target")]
    [InlineData(true, true, "canonical", "other", false, "target")]
    [InlineData(true, true, "canonical", "canonical", true, "MorePatrols")]
    public void Rejects_context_that_is_not_proof_safe(
        bool host,
        bool singlePlayer,
        string? canonical,
        string? actualTarget,
        bool morePatrols,
        string expectedReason)
    {
        var machine = new DispatchProofStateMachine();
        Move(machine, DispatchProofPhase.AwaitingLoadAuthorityIdentity, "load");
        Move(machine, DispatchProofPhase.BaselineReady, "baseline");
        Move(machine, DispatchProofPhase.Response1Armed, "arm-1");
        var result = new DispatchProofAdmission(machine).TryAdmit(
            DispatchProofTrigger.Response1,
            "owner-f9",
            ValidContext(host, singlePlayer, canonical, actualTarget, morePatrols));

        Assert.False(result.Accepted);
        Assert.Contains(expectedReason, result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", "compile-time direct interop boundary verified", "build pin provenance")]
    [InlineData("manual S1Atlas build-pin provenance verified", "", "Dispatch signature provenance")]
    public void Requires_independent_provenance_labels_instead_of_tautological_runtime_pin_comparisons(
        string buildProvenance,
        string signatureProvenance,
        string expectedReason)
    {
        var machine = new DispatchProofStateMachine();
        Move(machine, DispatchProofPhase.AwaitingLoadAuthorityIdentity, "load");
        Move(machine, DispatchProofPhase.BaselineReady, "baseline");
        Move(machine, DispatchProofPhase.Response1Armed, "arm-1");

        var context = ValidContext() with
        {
            BuildPinProvenance = buildProvenance,
            DispatchSignatureProvenance = signatureProvenance
        };
        var result = new DispatchProofAdmission(machine).TryAdmit(DispatchProofTrigger.Response1, "owner-f9", context);

        Assert.False(result.Accepted);
        Assert.Contains(expectedReason, result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static DispatchProofContext ValidContext(
        bool host = true,
        bool singlePlayer = true,
        string? canonical = "canonical",
        string? actualTarget = "canonical",
        bool morePatrols = false) => new(
            IsAuthoritativeHost: host,
            IsSinglePlayer: singlePlayer,
            CanonicalTargetIdentity: canonical,
            ActualTargetIdentity: actualTarget,
            BuildPinProvenance: "manually verified S1Atlas build pin",
            DispatchSignatureProvenance: "compile-time direct interop boundary verified",
            MorePatrolsPresent: morePatrols);

    private static void Move(DispatchProofStateMachine machine, DispatchProofPhase next, string key) =>
        Assert.True(machine.TryTransition(machine.Phase, next, key));
}
