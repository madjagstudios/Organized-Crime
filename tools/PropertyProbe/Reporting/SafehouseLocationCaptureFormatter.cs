using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class SafehouseLocationCaptureFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string FormatText(SafehouseLocationCaptureArchive archive)
    {
        var builder = new StringBuilder();
        builder.AppendLine("READ-ONLY SAFEHOUSE LOCATION CANDIDATES");
        builder.AppendLine($"MANUAL_REVIEW_REQUIRED: {archive.ManualReviewRequired}");
        builder.AppendLine($"MUTATION_ATTEMPTED: {archive.MutationAttempted}");
        builder.AppendLine("No capture is an ownership, availability, or implementation approval.");
        builder.AppendLine();

        foreach (var capture in archive.Captures)
        {
            builder.AppendLine($"CANDIDATE_NUMBER: {capture.CandidateNumber}");
            builder.AppendLine($"MARKER: {capture.Marker}");
            builder.AppendLine($"SCENE: {capture.SceneName}");
            builder.AppendLine($"POSITION: {Format(capture.Position)}");
            builder.AppendLine($"VIEW_FORWARD: {Format(capture.ViewForward)}");
            builder.AppendLine($"PLAYER_YAW_DEGREES: {capture.PlayerYawDegrees:0.###}");

            foreach (var location in capture.RegisteredLocations.OrderBy(item => item.DistanceFromAnchor))
            {
                builder.AppendLine(
                    $"REGISTERED_LOCATION: {location.Kind} | {location.Code} | {location.Name} | " +
                    $"{location.DistanceFromAnchor:0.###} | owned:{location.IsOwned} | {location.TransformPath}");
                builder.AppendLine($"ANCHOR_INSIDE_REGISTERED_BOUNDS: {location.AnchorInsideBounds}");
                if (location.BoundsCenter is { } registeredBoundsCenter)
                    builder.AppendLine($"REGISTERED_BOUNDS_CENTER: {Format(registeredBoundsCenter)}");
                if (location.BoundsSize is { } registeredBoundsSize)
                    builder.AppendLine($"REGISTERED_BOUNDS_SIZE: {Format(registeredBoundsSize)}");
            }

            foreach (var structuralObject in capture.StructuralObjects.OrderBy(item => item.DistanceFromAnchor))
            {
                builder.AppendLine($"STRUCTURAL_OBJECT: {structuralObject.Name}");
                builder.AppendLine($"TRANSFORM_PATH: {structuralObject.TransformPath}");
                builder.AppendLine($"OBJECT_POSITION: {Format(structuralObject.Position)}");
                builder.AppendLine($"DISTANCE_FROM_ANCHOR: {structuralObject.DistanceFromAnchor:0.###}");
                if (structuralObject.BoundsCenter is { } boundsCenter)
                    builder.AppendLine($"BOUNDS_CENTER: {Format(boundsCenter)}");
                if (structuralObject.BoundsSize is { } boundsSize)
                    builder.AppendLine($"BOUNDS_SIZE: {Format(boundsSize)}");
                builder.AppendLine($"COMPONENT_TYPES: {string.Join(", ", structuralObject.ComponentTypes)}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string FormatJson(SafehouseLocationCaptureArchive archive) =>
        JsonSerializer.Serialize(archive, JsonOptions);

    private static string Format(Vector3Dto vector) =>
        $"{vector.X:0.###}, {vector.Y:0.###}, {vector.Z:0.###}";
}
