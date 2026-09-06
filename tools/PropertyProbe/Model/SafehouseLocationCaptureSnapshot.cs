namespace OrganizedCrime.PropertyProbe.Model;

public enum SafehouseLocationMarker
{
    Entrance,
    Interior
}

public sealed record SafehouseRegisteredLocationSnapshot(
    string Kind,
    string Code,
    string Name,
    string TransformPath,
    Vector3Dto Position,
    float DistanceFromAnchor,
    bool IsOwned,
    bool AnchorInsideBounds = false,
    Vector3Dto? BoundsCenter = null,
    Vector3Dto? BoundsSize = null);

public sealed record SafehouseStructuralObjectSnapshot(
    string Name,
    string TransformPath,
    string SceneName,
    int InstanceId,
    Vector3Dto Position,
    Vector3Dto? BoundsCenter,
    Vector3Dto? BoundsSize,
    float DistanceFromAnchor,
    IReadOnlyList<string> ComponentTypes);

public sealed record SafehouseLocationCaptureSnapshot(
    int CandidateNumber,
    SafehouseLocationMarker Marker,
    Vector3Dto Position,
    Vector3Dto ViewForward,
    float PlayerYawDegrees,
    string SceneName,
    IReadOnlyList<SafehouseRegisteredLocationSnapshot> RegisteredLocations,
    IReadOnlyList<SafehouseStructuralObjectSnapshot> StructuralObjects,
    bool MutationAttempted);

public sealed record SafehouseLocationCaptureArchive(
    IReadOnlyList<SafehouseLocationCaptureSnapshot> Captures,
    bool MutationAttempted,
    bool ManualReviewRequired = true);

public sealed record SafehouseLocationCaptureAdmission(
    bool Accepted,
    int CandidateNumber,
    string? Reason = null);

public sealed class SafehouseLocationCaptureLedger
{
    private readonly List<SafehouseLocationCaptureSnapshot> _captures = new();
    private readonly int _maximumCandidates;

    public SafehouseLocationCaptureLedger(int maximumCandidates)
    {
        if (maximumCandidates <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCandidates));

        _maximumCandidates = maximumCandidates;
    }

    public IReadOnlyList<SafehouseLocationCaptureSnapshot> Captures => _captures;

    public SafehouseLocationCaptureArchive Archive =>
        new(_captures.ToArray(), _captures.Any(capture => capture.MutationAttempted));

    public SafehouseLocationCaptureAdmission TryRecord(
        SafehouseLocationMarker marker,
        SafehouseLocationCaptureSnapshot capture)
    {
        if (capture.MutationAttempted)
            return new(false, 0, "capture reported a mutation attempt");

        if (capture.RegisteredLocations.Count == 0)
            return new(false, 0, "registered Property/Business registry was unavailable");

        if (marker == SafehouseLocationMarker.Entrance)
        {
            var candidateNumber = _captures.Count(existing =>
                existing.Marker == SafehouseLocationMarker.Entrance) + 1;
            if (candidateNumber > _maximumCandidates)
                return new(false, 0, $"maximum of {_maximumCandidates} candidates already captured");

            _captures.Add(capture with
            {
                CandidateNumber = candidateNumber,
                Marker = SafehouseLocationMarker.Entrance
            });
            return new(true, candidateNumber);
        }

        var latestEntrance = _captures.LastOrDefault(existing =>
            existing.Marker == SafehouseLocationMarker.Entrance);
        if (latestEntrance is null)
            return new(false, 0, "capture an entrance before its interior point");

        if (_captures.Any(existing =>
                existing.CandidateNumber == latestEntrance.CandidateNumber &&
                existing.Marker == SafehouseLocationMarker.Interior))
        {
            return new(false, latestEntrance.CandidateNumber, "interior point already captured");
        }

        _captures.Add(capture with
        {
            CandidateNumber = latestEntrance.CandidateNumber,
            Marker = SafehouseLocationMarker.Interior
        });
        return new(true, latestEntrance.CandidateNumber);
    }
}

internal static class SafehouseLocationCaptureTrigger
{
    public static SafehouseLocationMarker GetMarker(bool f9Pressed, bool shiftPressed)
    {
        if (!f9Pressed)
            throw new InvalidOperationException("F9 was not pressed.");

        return shiftPressed
            ? SafehouseLocationMarker.Interior
            : SafehouseLocationMarker.Entrance;
    }
}
