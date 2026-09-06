using System.Text;
using System.Text.Json;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class PropertySnapshotFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string FormatText(IEnumerable<PropertySnapshot> properties)
    {
        var ordered = properties
            .OrderBy(p => p.PropertyCode, StringComparer.Ordinal)
            .ThenBy(p => p.PropertyName, StringComparer.Ordinal)
            .ToArray();

        var builder = new StringBuilder();

        foreach (var property in ordered)
        {
            var score = PropertyCandidateRanker.Score(property);

            builder.AppendLine($"PROPERTY: {property.PropertyName}");
            builder.AppendLine($"CODE: {property.PropertyCode}");
            builder.AppendLine($"TYPE: {property.RuntimeType}");
            builder.AppendLine($"GAME_OBJECT: {property.GameObjectName}");
            builder.AppendLine($"TRANSFORM_PATH: {property.TransformPath}");
            builder.AppendLine($"OWNED: {property.IsOwned.ToString().ToLowerInvariant()}");
            builder.AppendLine($"OWNED_BY_DEFAULT: {property.OwnedByDefault.ToString().ToLowerInvariant()}");
            builder.AppendLine($"PRICE: {property.Price}");
            builder.AppendLine($"POSITION: {property.Position.X}, {property.Position.Y}, {property.Position.Z}");
            builder.AppendLine($"EMPLOYEES: {property.EmployeeCount} / {property.EmployeeCapacity}");
            builder.AppendLine($"EMPLOYEE_IDLE_POINTS: {property.EmployeeIdlePointCount}");
            builder.AppendLine($"NPC_SPAWN: {property.HasNpcSpawnPoint.ToString().ToLowerInvariant()}");
            builder.AppendLine($"EMPLOYEE_CONTAINER: {property.HasEmployeeContainer.ToString().ToLowerInvariant()}");
            builder.AppendLine($"CONTENTS_CONTAINER: {property.HasContentsContainer.ToString().ToLowerInvariant()}");
            builder.AppendLine($"BOUNDING_BOX: {property.HasBoundingBox.ToString().ToLowerInvariant()}");
            builder.AppendLine($"GRIDS: {property.GridCount}");
            builder.AppendLine($"BUILDABLE_ITEMS: {property.BuildableItemCount}");
            builder.AppendLine($"CONFIGURABLES: {property.ConfigurableCount}");
            builder.AppendLine($"LOADING_DOCKS: {property.LoadingDockCount}");
            builder.AppendLine($"SAFEHOUSE_SCORE: {score.Safehouse}");
            builder.AppendLine($"WAREHOUSE_SCORE: {score.Warehouse}");
            builder.AppendLine($"DOCK_SCORE: {score.Dock}");
            builder.AppendLine($"CASINO_SCORE: {score.Casino}");

            if (!string.IsNullOrWhiteSpace(property.CaptureError))
                builder.AppendLine($"CAPTURE_ERROR: {property.CaptureError}");

            foreach (var dock in property.LoadingDocks)
            {
                builder.AppendLine($"  DOCK_GUID: {dock.Guid}");
                builder.AppendLine($"  DOCK_PATH: {dock.TransformPath}");
                builder.AppendLine($"  DOCK_ACCESS_POINTS: {dock.AccessPointCount}");
                builder.AppendLine($"  DOCK_INPUT_SLOTS: {dock.InputSlotCount}");
                builder.AppendLine($"  DOCK_OUTPUT_SLOTS: {dock.OutputSlotCount}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string FormatJson(IEnumerable<PropertySnapshot> properties)
    {
        var report = properties
            .OrderBy(p => p.PropertyCode, StringComparer.Ordinal)
            .ThenBy(p => p.PropertyName, StringComparer.Ordinal)
            .Select(p => new
            {
                property = p,
                scores = PropertyCandidateRanker.Score(p)
            })
            .ToArray();

        return JsonSerializer.Serialize(report, JsonOptions);
    }
}
