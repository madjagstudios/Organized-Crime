using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseOwnershipReconcilerTests
{
    [Fact]
    public void Sidecar_owned_stays_owned_the_happy_path_is_unchanged()
    {
        Assert.True(FishWarehouseOwnershipReconciler.IsOwnedByEvidence(
            sidecarOwned: true, capturedObjectCount: 0, capturedEmployeeCount: 0, nativeIsOwned: null));
    }

    [Fact]
    public void Never_owned_with_no_evidence_stays_unowned()
    {
        Assert.False(FishWarehouseOwnershipReconciler.IsOwnedByEvidence(
            sidecarOwned: false, capturedObjectCount: 0, capturedEmployeeCount: 0, nativeIsOwned: null));
    }

    [Fact]
    public void Corrupt_owned_false_recovers_when_captured_objects_prove_prior_ownership()
    {
        // The doom-loop: sidecar owned=false, but captured content shows it was owned.
        Assert.True(FishWarehouseOwnershipReconciler.IsOwnedByEvidence(
            sidecarOwned: false, capturedObjectCount: 5, capturedEmployeeCount: 0, nativeIsOwned: null));
    }

    [Fact]
    public void Corrupt_owned_false_recovers_when_captured_employees_prove_prior_ownership()
    {
        Assert.True(FishWarehouseOwnershipReconciler.IsOwnedByEvidence(
            sidecarOwned: false, capturedObjectCount: 0, capturedEmployeeCount: 1, nativeIsOwned: null));
    }

    [Fact]
    public void Native_is_owned_true_recovers_even_an_empty_warehouse()
    {
        Assert.True(FishWarehouseOwnershipReconciler.IsOwnedByEvidence(
            sidecarOwned: false, capturedObjectCount: 0, capturedEmployeeCount: 0, nativeIsOwned: true));
    }

    [Fact]
    public void Native_is_owned_false_does_not_override_captured_content()
    {
        // Ownership is monotonic (no sell path): a native false must not downgrade
        // when captured content proves prior ownership.
        Assert.True(FishWarehouseOwnershipReconciler.IsOwnedByEvidence(
            sidecarOwned: false, capturedObjectCount: 3, capturedEmployeeCount: 0, nativeIsOwned: false));
    }

    [Fact]
    public void Native_is_owned_false_does_not_override_a_sidecar_owned_true()
    {
        Assert.True(FishWarehouseOwnershipReconciler.IsOwnedByEvidence(
            sidecarOwned: true, capturedObjectCount: 0, capturedEmployeeCount: 0, nativeIsOwned: false));
    }

    [Fact]
    public void No_evidence_and_native_false_stays_unowned()
    {
        Assert.False(FishWarehouseOwnershipReconciler.IsOwnedByEvidence(
            sidecarOwned: false, capturedObjectCount: 0, capturedEmployeeCount: 0, nativeIsOwned: false));
    }
}
