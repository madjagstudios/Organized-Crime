using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class PropertyPromotionExperimentTests
{
    [Fact]
    public void Experiment_report_preserves_gate_and_docks_safety_evidence()
    {
        var snapshot = PropertyPromotionExperimentSnapshot.Test(
            gate: "registration",
            registrationPassed: true,
            ownershipPassed: false,
            docksWarehouseCodeBefore: "dockswarehouse",
            docksWarehouseCodeAfter: "dockswarehouse",
            docksWarehouseOwnedBefore: true,
            docksWarehouseOwnedAfter: true);

        var text = PropertyPromotionExperimentFormatter.FormatText(snapshot);

        Assert.Contains("GATE: registration", text);
        Assert.Contains("REGISTRATION_PASSED: True", text);
        Assert.Contains("OWNERSHIP_PASSED: False", text);
        Assert.Contains("DOCKS_WAREHOUSE_CODE_UNCHANGED: True", text);
        Assert.Contains("DOCKS_WAREHOUSE_OWNERSHIP_UNCHANGED: True", text);
    }

    [Fact]
    public void Experiment_code_is_stable_and_cannot_target_docks_warehouse()
    {
        var snapshot = PropertyPromotionExperimentSnapshot.Test();

        Assert.Equal("oc_fishwarehouse", snapshot.ProposedPropertyCode);
        Assert.NotEqual("dockswarehouse", snapshot.ProposedPropertyCode);
    }

    [Fact]
    public void Ownership_and_persistence_are_false_before_registration_gate()
    {
        var snapshot = PropertyPromotionExperimentSnapshot.Test(
            gate: "preconditions",
            preconditionsPassed: true,
            registrationPassed: false,
            ownershipPassed: false,
            persistencePassed: false);

        Assert.False(snapshot.RegistrationPassed);
        Assert.False(snapshot.OwnershipPassed);
        Assert.False(snapshot.PersistencePassed);
    }

    [Fact]
    public void Identity_payload_targets_the_vanilla_serialized_property_fields()
    {
        var payload = PropertyPromotionIdentityPayload.Json;

        Assert.Contains("\"propertyCode\":\"oc_fishwarehouse\"", payload);
        Assert.Contains("\"propertyName\":\"Fish Warehouse\"", payload);
    }

    [Fact]
    public void Ownership_report_records_transition_without_claiming_persistence()
    {
        var snapshot = new PropertyPromotionExperimentSnapshot(
            ProposedPropertyCode: "oc_fishwarehouse",
            TargetName: "Fish Warehouse",
            TargetPath: "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse",
            Gate: "ownership",
            PreconditionsPassed: true,
            RegistrationPassed: true,
            OwnershipPassed: true,
            PersistencePassed: false,
            PropertyCountBefore: 14,
            PropertyCountAfter: 14,
            OwnedPropertyCountBefore: 12,
            OwnedPropertyCountAfter: 13,
            UnownedPropertyCountBefore: 2,
            UnownedPropertyCountAfter: 1,
            TargetRegisteredAfter: true,
            TargetOwnedAfter: true,
            TargetInOwnedCollectionAfter: true,
            TargetInUnownedCollectionAfter: false,
            DocksWarehouseCodeBefore: "dockswarehouse",
            DocksWarehouseCodeAfter: "dockswarehouse",
            DocksWarehouseOwnedBefore: true,
            DocksWarehouseOwnedAfter: true,
            FailureReason: null);

        var text = PropertyPromotionExperimentFormatter.FormatText(snapshot);

        Assert.Contains("GATE: ownership", text);
        Assert.Contains("OWNERSHIP_PASSED: True", text);
        Assert.Contains("PERSISTENCE_PASSED: False", text);
        Assert.Contains("TARGET_OWNED_AFTER: True", text);
        Assert.Contains("OWNED_PROPERTY_COUNT_BEFORE: 12", text);
        Assert.Contains("OWNED_PROPERTY_COUNT_AFTER: 13", text);
        Assert.Contains("TARGET_IN_OWNED_COLLECTION_AFTER: True", text);
        Assert.Contains("TARGET_IN_UNOWNED_COLLECTION_AFTER: False", text);
    }
}
