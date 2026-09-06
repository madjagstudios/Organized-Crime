using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class PropertyPromotionPreflightFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(PropertyPromotionPreflightSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("READ-ONLY PROPERTY PROMOTION PREFLIGHT");
        builder.AppendLine("No property, ownership, save, network, employee, delivery, or scene mutation was performed.");
        builder.AppendLine();
        builder.AppendLine($"PROPOSED_PROPERTY_CODE: {snapshot.ProposedPropertyCode}");
        builder.AppendLine($"TARGET_NAME: {snapshot.TargetName}");
        builder.AppendLine($"TARGET_PATH: {snapshot.TargetPath}");
        builder.AppendLine($"HAS_NPC_ENTERABLE_BUILDING: {snapshot.HasNpcEnterableBuilding}");
        builder.AppendLine($"BUILDING_NAME: {snapshot.BuildingName ?? "n/a"}");
        builder.AppendLine($"BUILDING_GUID: {snapshot.BuildingGuid ?? "n/a"}");
        builder.AppendLine($"DOOR_COUNT: {snapshot.DoorCount}");
        builder.AppendLine($"HAS_PROPERTY_COMPONENT: {snapshot.HasPropertyComponent}");
        builder.AppendLine($"HAS_BUSINESS_COMPONENT: {snapshot.HasBusinessComponent}");
        builder.AppendLine($"HAS_TRANSIT_COMPONENT: {snapshot.HasTransitComponent}");
        builder.AppendLine($"IS_IN_PROPERTY_COLLECTION: {snapshot.IsInPropertyCollection}");
        builder.AppendLine($"IS_IN_OWNED_PROPERTY_COLLECTION: {snapshot.IsInOwnedPropertyCollection}");
        builder.AppendLine($"IS_IN_UNOWNED_PROPERTY_COLLECTION: {snapshot.IsInUnownedPropertyCollection}");
        builder.AppendLine($"EXISTING_PROPERTY_COUNT: {snapshot.ExistingPropertyCount}");
        builder.AppendLine($"EXISTING_OWNED_PROPERTY_COUNT: {snapshot.ExistingOwnedPropertyCount}");
        builder.AppendLine($"EXISTING_UNOWNED_PROPERTY_COUNT: {snapshot.ExistingUnownedPropertyCount}");
        return builder.ToString();
    }

    public static string FormatJson(PropertyPromotionPreflightSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
