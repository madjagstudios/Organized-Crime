using System.Text;
using System.Text.Json;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class BuildingCensusFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string FormatText(
        IEnumerable<BuildingCensusSnapshot> snapshots,
        string distanceLabel = "DISTANCE_FROM_DOCKS_WAREHOUSE")
    {
        var ordered = snapshots
            .OrderBy(snapshot => snapshot.Candidate, StringComparer.Ordinal)
            .ThenBy(snapshot => snapshot.DistanceFromReference)
            .ThenBy(snapshot => snapshot.TransformPath, StringComparer.Ordinal)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("READ-ONLY BUILDING CENSUS");
        builder.AppendLine("Docks Warehouse is a reference anchor and is excluded from candidate mutation.");
        builder.AppendLine();

        foreach (var snapshot in ordered)
        {
            builder.AppendLine($"CANDIDATE: {snapshot.Candidate}");
            builder.AppendLine($"GAME_OBJECT: {snapshot.GameObjectName}");
            builder.AppendLine($"TRANSFORM_PATH: {snapshot.TransformPath}");
            builder.AppendLine($"ACTIVE_SELF: {snapshot.ActiveSelf}");
            builder.AppendLine($"POSITION: {snapshot.Position.X}, {snapshot.Position.Y}, {snapshot.Position.Z}");
            builder.AppendLine($"{distanceLabel}: {FormatDistance(snapshot.DistanceFromReference)}");
            builder.AppendLine($"CHILD_COUNT: {snapshot.ChildCount}");
            builder.AppendLine($"COMPONENT_COUNT: {snapshot.ComponentCount}");
            builder.AppendLine($"HAS_PROPERTY_COMPONENT: {snapshot.HasPropertyComponent}");
            builder.AppendLine($"HAS_BUSINESS_COMPONENT: {snapshot.HasBusinessComponent}");
            builder.AppendLine($"HAS_TRANSIT_COMPONENT: {snapshot.HasTransitComponent}");
            builder.AppendLine($"HAS_CONFIGURABLE_COMPONENT: {snapshot.HasConfigurableComponent}");
            builder.AppendLine($"HAS_GRID_COMPONENT: {snapshot.HasGridComponent}");
            builder.AppendLine($"HAS_CONTAINER_COMPONENT: {snapshot.HasContainerComponent}");
            builder.AppendLine($"HAS_COLLIDER: {snapshot.HasCollider}");
            builder.AppendLine($"HAS_RENDERER: {snapshot.HasRenderer}");
            if (snapshot.DeliveryLocationName is not null)
                builder.AppendLine($"DELIVERY_LOCATION_NAME: {snapshot.DeliveryLocationName}");
            if (snapshot.DeliveryLocationDescription is not null)
                builder.AppendLine($"DELIVERY_LOCATION_DESCRIPTION: {snapshot.DeliveryLocationDescription}");
            if (snapshot.DeliveryLocationGuid is not null)
                builder.AppendLine($"DELIVERY_LOCATION_GUID: {snapshot.DeliveryLocationGuid}");
            builder.AppendLine($"COMPONENT_TYPES: {string.Join(", ", snapshot.ComponentTypes)}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string FormatJson(IEnumerable<BuildingCensusSnapshot> snapshots) =>
        JsonSerializer.Serialize(
            snapshots
                .OrderBy(snapshot => snapshot.Candidate, StringComparer.Ordinal)
                .ThenBy(snapshot => snapshot.DistanceFromReference)
                .ThenBy(snapshot => snapshot.TransformPath, StringComparer.Ordinal)
                .ToArray(),
            JsonOptions);

    public static string FormatPlayerLocationText(
        Vector3Dto playerPosition,
        IEnumerable<BuildingCensusSnapshot> snapshots)
    {
        var builder = new StringBuilder();
        builder.AppendLine("READ-ONLY PLAYER LOCATION PROBE");
        builder.AppendLine($"PLAYER_POSITION: {playerPosition.X}, {playerPosition.Y}, {playerPosition.Z}");
        builder.AppendLine("Press F9 while standing outside a building; entries are sorted by distance from the player.");
        builder.AppendLine();
        builder.Append(FormatText(snapshots, "DISTANCE_FROM_PLAYER"));
        return builder.ToString();
    }

    public static string FormatPlayerLocationJson(
        Vector3Dto playerPosition,
        IEnumerable<BuildingCensusSnapshot> snapshots) =>
        JsonSerializer.Serialize(new
        {
            playerPosition = new { x = playerPosition.X, y = playerPosition.Y, z = playerPosition.Z },
            snapshots = snapshots
                .OrderBy(snapshot => snapshot.DistanceFromReference)
                .ThenBy(snapshot => snapshot.TransformPath, StringComparer.Ordinal)
                .ToArray()
        }, JsonOptions);

    private static string FormatDistance(float distance) =>
        float.IsPositiveInfinity(distance) ? "n/a" : distance.ToString("0.###");
}
