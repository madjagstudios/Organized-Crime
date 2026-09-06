using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class SafehouseLocationCaptureTests
{
    [Fact]
    public void Three_candidate_entrances_are_preserved_and_a_fourth_is_rejected()
    {
        var ledger = new SafehouseLocationCaptureLedger(maximumCandidates: 3);

        var first = ledger.TryRecord(SafehouseLocationMarker.Entrance, CaptureAt(10));
        var second = ledger.TryRecord(SafehouseLocationMarker.Entrance, CaptureAt(20));
        var third = ledger.TryRecord(SafehouseLocationMarker.Entrance, CaptureAt(30));
        var fourth = ledger.TryRecord(SafehouseLocationMarker.Entrance, CaptureAt(40));

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.True(third.Accepted);
        Assert.False(fourth.Accepted);
        Assert.Equal(new[] { 1, 2, 3 }, ledger.Captures.Select(capture => capture.CandidateNumber));
        Assert.Equal(new[] { 10f, 20f, 30f }, ledger.Captures.Select(capture => capture.Position.X));
    }

    [Fact]
    public void Interior_marker_attaches_to_the_latest_candidate_and_cannot_be_duplicated()
    {
        var ledger = new SafehouseLocationCaptureLedger(maximumCandidates: 3);
        ledger.TryRecord(SafehouseLocationMarker.Entrance, CaptureAt(10));
        ledger.TryRecord(SafehouseLocationMarker.Entrance, CaptureAt(20));

        var interior = ledger.TryRecord(SafehouseLocationMarker.Interior, CaptureAt(21));
        var duplicate = ledger.TryRecord(SafehouseLocationMarker.Interior, CaptureAt(22));

        Assert.True(interior.Accepted);
        Assert.Equal(2, interior.CandidateNumber);
        Assert.False(duplicate.Accepted);
        Assert.Equal(
            new[]
            {
                (1, SafehouseLocationMarker.Entrance),
                (2, SafehouseLocationMarker.Entrance),
                (2, SafehouseLocationMarker.Interior)
            },
            ledger.Captures.Select(capture => (capture.CandidateNumber, capture.Marker)));
    }

    [Fact]
    public void Interior_marker_without_an_entrance_is_rejected()
    {
        var ledger = new SafehouseLocationCaptureLedger(maximumCandidates: 3);

        var result = ledger.TryRecord(SafehouseLocationMarker.Interior, CaptureAt(5));

        Assert.False(result.Accepted);
        Assert.Empty(ledger.Captures);
    }

    [Fact]
    public void Capture_is_rejected_when_the_registered_location_registry_is_unavailable()
    {
        var ledger = new SafehouseLocationCaptureLedger(maximumCandidates: 3);

        var result = ledger.TryRecord(
            SafehouseLocationMarker.Entrance,
            CaptureAt(5) with { RegisteredLocations = Array.Empty<SafehouseRegisteredLocationSnapshot>() });

        Assert.False(result.Accepted);
        Assert.Contains("registry", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ledger.Captures);
    }

    [Theory]
    [InlineData(true, false, SafehouseLocationMarker.Entrance)]
    [InlineData(true, true, SafehouseLocationMarker.Interior)]
    public void F9_and_shift_f9_select_the_accessible_capture_marker(
        bool f9Pressed,
        bool shiftPressed,
        SafehouseLocationMarker expected)
    {
        Assert.Equal(expected, SafehouseLocationCaptureTrigger.GetMarker(f9Pressed, shiftPressed));
    }

    [Fact]
    public void Report_preserves_geometry_and_registered_conflicts_without_approving_a_candidate()
    {
        var ledger = new SafehouseLocationCaptureLedger(maximumCandidates: 3);
        ledger.TryRecord(
            SafehouseLocationMarker.Entrance,
            CaptureAt(10) with
            {
                RegisteredLocations = new[]
                {
                    new SafehouseRegisteredLocationSnapshot(
                        Kind: "Property",
                        Code: "seweroffice",
                        Name: "Sewer Office",
                        TransformPath: "Map/SewerOffice",
                        Position: new Vector3Dto(15, 0, 0),
                        DistanceFromAnchor: 5,
                        IsOwned: true,
                        AnchorInsideBounds: true,
                        BoundsCenter: new Vector3Dto(10, 2, 0),
                        BoundsSize: new Vector3Dto(20, 4, 20))
                },
                StructuralObjects = new[]
                {
                    new SafehouseStructuralObjectSnapshot(
                        Name: "EmptyBuildingShell",
                        TransformPath: "Map/Northtown/EmptyBuildingShell",
                        SceneName: "Main",
                        InstanceId: 42,
                        Position: new Vector3Dto(11, 0, 0),
                        BoundsCenter: new Vector3Dto(11, 2, 0),
                        BoundsSize: new Vector3Dto(8, 4, 12),
                        DistanceFromAnchor: 1,
                        ComponentTypes: new[] { "UnityEngine.MeshRenderer", "UnityEngine.BoxCollider" })
                }
            });

        var text = SafehouseLocationCaptureFormatter.FormatText(ledger.Archive);

        Assert.Contains("MANUAL_REVIEW_REQUIRED: True", text);
        Assert.Contains("CANDIDATE_NUMBER: 1", text);
        Assert.Contains("VIEW_FORWARD: 0, 0, 1", text);
        Assert.Contains("REGISTERED_LOCATION: Property | seweroffice | Sewer Office | 5", text);
        Assert.Contains("ANCHOR_INSIDE_REGISTERED_BOUNDS: True", text);
        Assert.Contains("BOUNDS_SIZE: 8, 4, 12", text);
        Assert.DoesNotContain("APPROVED", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Json_report_names_the_marker_instead_of_emitting_an_opaque_number()
    {
        var ledger = new SafehouseLocationCaptureLedger(maximumCandidates: 3);
        ledger.TryRecord(SafehouseLocationMarker.Entrance, CaptureAt(10));

        var json = SafehouseLocationCaptureFormatter.FormatJson(ledger.Archive);

        Assert.Contains("\"marker\": \"Entrance\"", json);
    }

    private static SafehouseLocationCaptureSnapshot CaptureAt(float x) =>
        new(
            CandidateNumber: 0,
            Marker: SafehouseLocationMarker.Entrance,
            Position: new Vector3Dto(x, 0, 0),
            ViewForward: new Vector3Dto(0, 0, 1),
            PlayerYawDegrees: 90,
            SceneName: "Main",
            RegisteredLocations: new[]
            {
                new SafehouseRegisteredLocationSnapshot(
                    Kind: "Property",
                    Code: "reference",
                    Name: "Reference Property",
                    TransformPath: "Map/Reference",
                    Position: new Vector3Dto(100, 0, 0),
                    DistanceFromAnchor: 100,
                    IsOwned: false)
            },
            StructuralObjects: Array.Empty<SafehouseStructuralObjectSnapshot>(),
            MutationAttempted: false);
}
