using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class PropertyPromotionExperimentFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(PropertyPromotionExperimentSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("GATED FISH WAREHOUSE PROPERTY PROMOTION EXPERIMENT");
        builder.AppendLine($"PROPOSED_PROPERTY_CODE: {snapshot.ProposedPropertyCode}");
        builder.AppendLine($"TARGET_NAME: {snapshot.TargetName}");
        builder.AppendLine($"TARGET_PATH: {snapshot.TargetPath}");
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"PRECONDITIONS_PASSED: {snapshot.PreconditionsPassed}");
        builder.AppendLine($"REGISTRATION_PASSED: {snapshot.RegistrationPassed}");
        builder.AppendLine($"OWNERSHIP_PASSED: {snapshot.OwnershipPassed}");
        builder.AppendLine($"PERSISTENCE_PASSED: {snapshot.PersistencePassed}");
        builder.AppendLine($"PROPERTY_COUNT_BEFORE: {snapshot.PropertyCountBefore}");
        builder.AppendLine($"PROPERTY_COUNT_AFTER: {snapshot.PropertyCountAfter}");
        builder.AppendLine($"OWNED_PROPERTY_COUNT_BEFORE: {snapshot.OwnedPropertyCountBefore}");
        builder.AppendLine($"OWNED_PROPERTY_COUNT_AFTER: {snapshot.OwnedPropertyCountAfter}");
        builder.AppendLine($"UNOWNED_PROPERTY_COUNT_BEFORE: {snapshot.UnownedPropertyCountBefore}");
        builder.AppendLine($"UNOWNED_PROPERTY_COUNT_AFTER: {snapshot.UnownedPropertyCountAfter}");
        builder.AppendLine($"TARGET_REGISTERED_AFTER: {snapshot.TargetRegisteredAfter}");
        builder.AppendLine($"TARGET_OWNED_AFTER: {snapshot.TargetOwnedAfter}");
        builder.AppendLine($"TARGET_IN_OWNED_COLLECTION_AFTER: {snapshot.TargetInOwnedCollectionAfter}");
        builder.AppendLine($"TARGET_IN_UNOWNED_COLLECTION_AFTER: {snapshot.TargetInUnownedCollectionAfter}");
        builder.AppendLine($"DOCKS_WAREHOUSE_CODE_BEFORE: {snapshot.DocksWarehouseCodeBefore}");
        builder.AppendLine($"DOCKS_WAREHOUSE_CODE_AFTER: {snapshot.DocksWarehouseCodeAfter}");
        builder.AppendLine($"DOCKS_WAREHOUSE_OWNED_BEFORE: {snapshot.DocksWarehouseOwnedBefore}");
        builder.AppendLine($"DOCKS_WAREHOUSE_OWNED_AFTER: {snapshot.DocksWarehouseOwnedAfter}");
        builder.AppendLine($"DOCKS_WAREHOUSE_CODE_UNCHANGED: {string.Equals(snapshot.DocksWarehouseCodeBefore, snapshot.DocksWarehouseCodeAfter, StringComparison.Ordinal)}");
        builder.AppendLine($"DOCKS_WAREHOUSE_OWNERSHIP_UNCHANGED: {snapshot.DocksWarehouseOwnedBefore == snapshot.DocksWarehouseOwnedAfter}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(PropertyPromotionExperimentSnapshot snapshot) =>
        JsonSerializer.Serialize(new
        {
            snapshot.ProposedPropertyCode,
            snapshot.TargetName,
            snapshot.TargetPath,
            snapshot.Gate,
            snapshot.PreconditionsPassed,
            snapshot.RegistrationPassed,
            snapshot.OwnershipPassed,
            snapshot.PersistencePassed,
            snapshot.PropertyCountBefore,
            snapshot.PropertyCountAfter,
            snapshot.OwnedPropertyCountBefore,
            snapshot.OwnedPropertyCountAfter,
            snapshot.UnownedPropertyCountBefore,
            snapshot.UnownedPropertyCountAfter,
            snapshot.TargetRegisteredAfter,
            snapshot.TargetOwnedAfter,
            snapshot.TargetInOwnedCollectionAfter,
            snapshot.TargetInUnownedCollectionAfter,
            snapshot.DocksWarehouseCodeBefore,
            snapshot.DocksWarehouseCodeAfter,
            snapshot.DocksWarehouseOwnedBefore,
            snapshot.DocksWarehouseOwnedAfter,
            DocksWarehouseCodeUnchanged = string.Equals(
                snapshot.DocksWarehouseCodeBefore,
                snapshot.DocksWarehouseCodeAfter,
                StringComparison.Ordinal),
            DocksWarehouseOwnershipUnchanged = snapshot.DocksWarehouseOwnedBefore == snapshot.DocksWarehouseOwnedAfter,
            snapshot.FailureReason
        }, JsonOptions);
}
