using OrganizedCrime.Model;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseGarageOpeningSafetyDefinitionTests
{
    [Fact]
    public void Projects_the_rotated_live_panel_envelope_against_its_separate_north_plane()
    {
        var anchor = new FishWarehouseAnchorFrame(
            new FishWarehouseGarageVector(-67.31696f, -2.5f, -79.96761f),
            yawDegrees: 60f);
        var liveBounds = new FishWarehouseWorldBounds(
            new FishWarehouseGarageVector(-65.7018f, -0.45f, -67.7652f),
            new FishWarehouseGarageVector(1.0173f, 2.2f, 1.7421f));

        FishWarehouseAnchorLocalBounds envelope =
            FishWarehouseGarageOpeningGeometryDefinition.ToAnchorLocalBounds(liveBounds, anchor);
        FishWarehousePhysicalPanelPlane plane = new(
            FishWarehouseGarageOpeningGeometryDefinition.ToAnchorLocalPoint(liveBounds.Center, anchor),
            FishWarehouseGarageOpeningGeometryDefinition.ToAnchorLocalDirection(
                new FishWarehouseGarageVector(0.8660254f, 0f, 0.5f),
                anchor));

        Assert.Equal(-11.77737f, envelope.MinimumX, 4);
        Assert.Equal(-7.74266f, envelope.MaximumX, 4);
        Assert.Equal(5.74792f, envelope.MinimumZ, 4);
        Assert.Equal(9.25203f, envelope.MaximumZ, 4);

        Assert.True(FishWarehouseGarageOpeningDefinition.TryProject(
            ApprovedCandidates(envelope, plane),
            out FishWarehouseGarageOpeningProjection? projection));

        FishWarehouseGarageOpeningProjection result = projection!;
        Assert.Equal(-11.6f, result.Opening.HorizontalMinimum, 4);
        Assert.Equal(-7.64266f, result.Opening.HorizontalMaximum, 4);
        Assert.Equal(4.35f, result.Opening.Top, 4);
        Assert.NotEqual(5.35f, result.Opening.Top);
        Assert.Equal(0.6f, result.TransitionPrism.Depth, 4);
    }

    [Fact]
    public void Derives_the_trusted_physical_normal_from_the_mesh_thin_axis_and_actual_rotations()
    {
        var evidence = new FishWarehouseGaragePanelAxisEvidence(
            new FishWarehouseGarageQuaternion(-0.7071f, 0f, 0f, 0.7071f),
            new FishWarehouseGarageQuaternion(0f, 0f, -0.7071f, 0.7071f),
            new FishWarehouseGarageVector(0.05f, 2.2f, 1.7421f));

        Assert.True(FishWarehouseGaragePhysicalPanelDefinition.TryDeriveAnchorLocalAxes(
            evidence,
            out FishWarehouseGaragePanelAxes axes));
        Assert.Equal(0f, axes.Forward.X, 3);
        Assert.Equal(1f, axes.Forward.Y, 3);
        Assert.Equal(0f, axes.Forward.Z, 3);
        Assert.Equal(0f, axes.Right.X, 3);
        Assert.Equal(0f, axes.Right.Y, 3);
        Assert.Equal(1f, axes.Right.Z, 3);

        Assert.True(FishWarehouseGaragePhysicalPanelDefinition.TryDerivePhysicalNormal(
            evidence,
            out FishWarehouseGarageVector normal));
        Assert.Equal(0f, normal.X, 3);
        Assert.Equal(0f, normal.Y, 3);
        Assert.Equal(1f, normal.Z, 3);
    }

    [Fact]
    public void Measures_the_thin_axis_in_physical_scaled_space()
    {
        Assert.True(FishWarehouseGaragePhysicalPanelDefinition.TryGetPhysicalAxisExtents(
            new FishWarehouseGarageVector(0.50f, 0.25f, 1.7421f),
            new FishWarehouseGarageVector(0.10f, 1f, 1f),
            out FishWarehouseGarageVector physicalExtents));

        Assert.Equal(0.05f, physicalExtents.X, 4);
        Assert.Equal(0.25f, physicalExtents.Y, 4);
        Assert.Equal(1.7421f, physicalExtents.Z, 4);
    }

    [Fact]
    public void Maps_independent_runtime_primary_and_alternate_evidence_to_the_safety_envelopes_and_physical_planes()
    {
        var anchor = new FishWarehouseGarageRuntimeAnchor(
            new FishWarehouseGarageVector(0f, 0f, 0f),
            new FishWarehouseGarageQuaternion(0f, 0f, 0f, 1f));
        var primary = new FishWarehouseGaragePanelRuntimeEvidence(
            new FishWarehouseWorldBounds(
                new FishWarehouseGarageVector(-9.76f, 2.625f, 7.45f),
                new FishWarehouseGarageVector(1.74205f, 2.625f, 0.05f)),
            new FishWarehouseGarageVector(0.10f, 0.20f, 0.30f),
            new FishWarehouseGarageVector(0.50f, 2.20f, 1.7421f),
            new FishWarehouseGarageVector(0.10f, 1f, 1f),
            new FishWarehouseGarageVector(-9.96f, 2.325f, 7.49f),
            new FishWarehouseGarageQuaternion(-0.7071f, 0f, 0f, 0.7071f),
            new FishWarehouseGarageQuaternion(-0.5f, -0.5f, -0.5f, 0.5f));
        var alternate = new FishWarehouseGaragePanelRuntimeEvidence(
            new FishWarehouseWorldBounds(
                new FishWarehouseGarageVector(-9.76f, 2.625f, 7.45f),
                new FishWarehouseGarageVector(1.74205f, 2.625f, 0.05f)),
            new FishWarehouseGarageVector(0.20f, 0.30f, 0.40f),
            new FishWarehouseGarageVector(0.50f, 2.20f, 1.7421f),
            new FishWarehouseGarageVector(0.10f, 1f, 1f),
            new FishWarehouseGarageVector(-10.16f, 2.325f, 7.52f),
            new FishWarehouseGarageQuaternion(0f, 0.7071f, 0f, 0.7071f),
            new FishWarehouseGarageQuaternion(0f, 0.7071f, 0f, 0.7071f));

        Assert.True(FishWarehouseGarageRuntimeEvidenceMapper.TryMap(anchor, primary, out FishWarehouseGaragePanelRuntimeMapping primaryMapping));
        Assert.True(FishWarehouseGarageRuntimeEvidenceMapper.TryMap(anchor, alternate, out FishWarehouseGaragePanelRuntimeMapping alternateMapping));

        Assert.Equal(-11.50205f, primaryMapping.SafetyEnvelope.MinimumX, 3);
        Assert.Equal(-8.01795f, primaryMapping.SafetyEnvelope.MaximumX, 3);
        Assert.Equal(7.4f, primaryMapping.SafetyEnvelope.MinimumZ, 3);
        Assert.Equal(7.5f, primaryMapping.SafetyEnvelope.MaximumZ, 3);
        Assert.Equal(-9.76f, primaryMapping.PhysicalPanelPlane.AnchorLocalCenter.X, 3);
        Assert.Equal(2.625f, primaryMapping.PhysicalPanelPlane.AnchorLocalCenter.Y, 3);
        Assert.Equal(7.5f, primaryMapping.PhysicalPanelPlane.AnchorLocalCenter.Z, 3);
        Assert.Equal(0f, primaryMapping.PhysicalPanelPlane.AnchorLocalNormal.X, 3);
        Assert.Equal(0f, primaryMapping.PhysicalPanelPlane.AnchorLocalNormal.Y, 3);
        Assert.Equal(1f, primaryMapping.PhysicalPanelPlane.AnchorLocalNormal.Z, 3);
        Assert.Equal(-1f, alternateMapping.PhysicalPanelPlane.AnchorLocalNormal.Z, 3);
        Assert.True(FishWarehouseGarageOpeningGeometryDefinition.AreCoincidentPhysicalFaces(
            primaryMapping.SafetyEnvelope,
            primaryMapping.PhysicalPanelPlane,
            alternateMapping.SafetyEnvelope,
            alternateMapping.PhysicalPanelPlane));
        bool projected = FishWarehouseGarageOpeningDefinition.TryProject(
            new[]
            {
                new FishWarehouseGarageOpeningCandidate(
                    FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath,
                    "GarageDoor (1)",
                    primaryMapping.SafetyEnvelope,
                    primaryMapping.PhysicalPanelPlane,
                    Array.Empty<FishWarehouseAnchorLocalBounds>(),
                    HasFloorSupport: true),
                new FishWarehouseGarageOpeningCandidate(
                    FishWarehouseGarageOpeningDefinition.ApprovedAlternateRootPath,
                    "GarageDoor_Other",
                    alternateMapping.SafetyEnvelope,
                    alternateMapping.PhysicalPanelPlane,
                    Array.Empty<FishWarehouseAnchorLocalBounds>(),
                    HasFloorSupport: true)
            },
            out _);
        Assert.True(projected);
    }

    [Fact]
    public void Maps_the_live_static_batched_primary_panel_to_its_observed_north_wall_plane()
    {
        var anchor = new FishWarehouseGarageRuntimeAnchor(
            new FishWarehouseGarageVector(-67.31696f, -2.5f, -79.96761f),
            new FishWarehouseGarageQuaternion(0f, 0.5f, 0f, 0.8660254f));
        var evidence = new FishWarehouseGaragePanelRuntimeEvidence(
            new FishWarehouseWorldBounds(
                new FishWarehouseGarageVector(-65.7018f, -0.45f, -67.7652f),
                new FishWarehouseGarageVector(1.0173f, 2.2f, 1.7421f)),
            new FishWarehouseGarageVector(11.5282f, 2.9715f, 81.5166f),
            new FishWarehouseGarageVector(189.9143f, 17.9715f, 298.5334f),
            new FishWarehouseGarageVector(1f, 1f, 1.1f),
            new FishWarehouseGarageVector(-65.7017695f, -0.45f, -67.7652021f),
            new FishWarehouseGarageQuaternion(-0.6123724f, 0.3535534f, 0.3535534f, 0.6123724f),
            new FishWarehouseGarageQuaternion(-0.6830127f, -0.1830127f, -0.1830127f, 0.6830127f),
            IsPartOfStaticBatch: true);

        Assert.True(FishWarehouseGarageRuntimeEvidenceMapper.TryMap(
            anchor,
            evidence,
            out FishWarehouseGaragePanelRuntimeMapping mapping));

        Assert.Equal(-11.777f, mapping.SafetyEnvelope.MinimumX, 3);
        Assert.Equal(-7.743f, mapping.SafetyEnvelope.MaximumX, 3);
        Assert.Equal(-0.150f, mapping.SafetyEnvelope.MinimumY, 3);
        Assert.Equal(4.250f, mapping.SafetyEnvelope.MaximumY, 3);
        Assert.Equal(5.748f, mapping.SafetyEnvelope.MinimumZ, 3);
        Assert.Equal(9.252f, mapping.SafetyEnvelope.MaximumZ, 3);
        Assert.Equal(-9.760f, mapping.PhysicalPanelPlane.AnchorLocalCenter.X, 3);
        Assert.Equal(2.050f, mapping.PhysicalPanelPlane.AnchorLocalCenter.Y, 3);
        Assert.Equal(7.500f, mapping.PhysicalPanelPlane.AnchorLocalCenter.Z, 3);
        Assert.Equal(0f, mapping.PhysicalPanelPlane.AnchorLocalNormal.X, 3);
        Assert.Equal(0f, mapping.PhysicalPanelPlane.AnchorLocalNormal.Y, 3);
        Assert.Equal(1f, MathF.Abs(mapping.PhysicalPanelPlane.AnchorLocalNormal.Z), 3);
        Assert.True(FishWarehouseGarageOpeningGeometryDefinition.IsNorthWallPhysicalPlane(
            mapping.SafetyEnvelope,
            mapping.PhysicalPanelPlane));
    }

    [Fact]
    public void Accepts_live_primary_panel_with_larger_alternate_backing_face_envelope()
    {
        FishWarehouseAnchorLocalBounds primaryEnvelope = new(
            -11.777f, -7.743f, -0.150f, 4.250f, 5.748f, 9.252f);
        FishWarehouseAnchorLocalBounds alternateEnvelope = new(
            -11.943f, -7.577f, 0.000f, 6.000f, 5.647f, 9.453f);
        FishWarehousePhysicalPanelPlane primaryPlane = new(
            new FishWarehouseGarageVector(-9.760f, 2.050f, 7.500f),
            new FishWarehouseGarageVector(0f, 0f, 1f));
        FishWarehousePhysicalPanelPlane alternatePlane = new(
            new FishWarehouseGarageVector(-9.760f, 3.000f, 7.550f),
            new FishWarehouseGarageVector(0f, 0f, -1f));

        Assert.True(FishWarehouseGarageOpeningDefinition.TryProject(
            new[]
            {
                new FishWarehouseGarageOpeningCandidate(
                    FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath,
                    "GarageDoor (1)",
                    primaryEnvelope,
                    primaryPlane,
                    Array.Empty<FishWarehouseAnchorLocalBounds>(),
                    HasFloorSupport: true),
                new FishWarehouseGarageOpeningCandidate(
                    FishWarehouseGarageOpeningDefinition.ApprovedAlternateRootPath,
                    "GarageDoor_Other",
                    alternateEnvelope,
                    alternatePlane,
                    Array.Empty<FishWarehouseAnchorLocalBounds>(),
                    HasFloorSupport: true)
            },
            out FishWarehouseGarageOpeningProjection? projection));

        Assert.NotNull(projection);
        Assert.Equal(-11.877f, projection.RawMinimumX, 3);
        Assert.Equal(-7.643f, projection.RawMaximumX, 3);
        Assert.Equal(-11.600f, projection.Opening.HorizontalMinimum, 3);
        Assert.Equal(-7.643f, projection.Opening.HorizontalMaximum, 3);
        Assert.Equal(0f, projection.Opening.Bottom, 3);
        Assert.Equal(4.350f, projection.Opening.Top, 3);
    }

    [Fact]
    public void Rejects_missing_and_finite_ambiguous_mesh_axis_evidence_under_the_trusted_rotations()
    {
        var trustedRotations = new FishWarehouseGaragePanelAxisEvidence(
            new FishWarehouseGarageQuaternion(-0.7071f, 0f, 0f, 0.7071f),
            new FishWarehouseGarageQuaternion(0f, 0f, -0.7071f, 0.7071f),
            new FishWarehouseGarageVector(0f, 0f, 0f));

        Assert.False(FishWarehouseGaragePhysicalPanelDefinition.TryDerivePhysicalNormal(
            trustedRotations,
            out _));

        Assert.False(FishWarehouseGaragePhysicalPanelDefinition.TryDerivePhysicalNormal(
            trustedRotations with { PhysicalAxisExtents = new FishWarehouseGarageVector(0.050f, 0.055f, 1.7421f) },
            out _));
    }

    [Fact]
    public void Rejects_a_uniquely_thin_local_forward_axis_when_the_supplied_rotations_make_it_vertical()
    {
        var evidence = new FishWarehouseGaragePanelAxisEvidence(
            new FishWarehouseGarageQuaternion(-0.7071f, 0f, 0f, 0.7071f),
            new FishWarehouseGarageQuaternion(0f, 0f, -0.7071f, 0.7071f),
            new FishWarehouseGarageVector(1.7421f, 2.20f, 0.050f));
        var envelope = new FishWarehouseAnchorLocalBounds(-11.5f, -8f, 0f, 5.25f, 7.4f, 7.6f);

        Assert.True(FishWarehouseGaragePhysicalPanelDefinition.TryDerivePhysicalNormal(evidence, out FishWarehouseGarageVector normal));
        Assert.Equal(0f, normal.X, 3);
        Assert.Equal(1f, normal.Y, 3);
        Assert.Equal(0f, normal.Z, 3);
        var plane = new FishWarehousePhysicalPanelPlane(new FishWarehouseGarageVector(-9.75f, 2.625f, 7.5f), normal);
        Assert.False(FishWarehouseGarageOpeningGeometryDefinition.IsNorthWallPhysicalPlane(envelope, plane));
        Assert.False(FishWarehouseGarageOpeningDefinition.TryProject(ApprovedCandidates(envelope, plane), out _));
    }

    [Fact]
    public void Rejects_a_normal_that_is_north_aligned_but_not_horizontal()
    {
        var envelope = new FishWarehouseAnchorLocalBounds(-11.5f, -8f, 0f, 5.25f, 7.4f, 7.6f);
        var plane = new FishWarehousePhysicalPanelPlane(
            new FishWarehouseGarageVector(-9.75f, 2.625f, 7.5f),
            new FishWarehouseGarageVector(0f, 0.10f, 4.90f));

        Assert.False(FishWarehouseGarageOpeningGeometryDefinition.IsNorthWallPhysicalPlane(envelope, plane));
    }

    [Fact]
    public void Allows_floor_support_below_the_strict_pedestrian_volume_but_rejects_a_blocking_sill()
    {
        FishWarehouseGarageOpeningProjection projection = ApprovedProjection();
        var floor = new FishWarehouseAnchorLocalBounds(
            projection.TransitionPrism.MinimumX,
            projection.TransitionPrism.MaximumX,
            -0.20f,
            FishWarehouseInteriorRoomDefinition.FloorY,
            projection.TransitionPrism.MinimumZ,
            projection.TransitionPrism.MaximumZ);
        var sill = new FishWarehouseAnchorLocalBounds(
            projection.TransitionPrism.MinimumX,
            projection.TransitionPrism.MaximumX,
            0.02f,
            0.40f,
            projection.TransitionPrism.MinimumZ,
            projection.TransitionPrism.MaximumZ);

        Assert.True(FishWarehouseGarageOpeningClearanceDefinition.HasContinuousFloorSupport(
            Enumerable.Range(0, 9).Select(_ => (IReadOnlyList<float>)new[] { FishWarehouseInteriorRoomDefinition.FloorY }),
            floorYTolerance: 0.20f));
        Assert.True(FishWarehouseGarageOpeningClearanceDefinition.IsTraversableVolumeClear(
            projection.TransitionPrism,
            new[] { floor }));
        Assert.True(FishWarehouseGarageOpeningDefinition.TryProject(
            ApprovedCandidates(survivingNonTargetColliders: new[] { floor }),
            out _));
        Assert.False(FishWarehouseGarageOpeningClearanceDefinition.IsTraversableVolumeClear(
            projection.TransitionPrism,
            new[] { floor, sill }));
        Assert.False(FishWarehouseGarageOpeningDefinition.TryProject(
            ApprovedCandidates(survivingNonTargetColliders: new[] { floor, sill }),
            out _));
    }

    [Fact]
    public void Uses_the_same_elevated_overlap_request_before_and_after_mutation_and_restores_the_target_snapshot_when_a_sill_appears()
    {
        FishWarehouseGarageOpeningProjection projection = ApprovedProjection();
        var anchor = new FishWarehouseGarageRuntimeAnchor(
            new FishWarehouseGarageVector(10f, 1f, -3f),
            new FishWarehouseGarageQuaternion(0f, 0.7071068f, 0f, 0.7071068f));
        var floor = new FishWarehouseAnchorLocalBounds(
            projection.TransitionPrism.MinimumX,
            projection.TransitionPrism.MaximumX,
            -0.20f,
            FishWarehouseInteriorRoomDefinition.FloorY,
            projection.TransitionPrism.MinimumZ,
            projection.TransitionPrism.MaximumZ);
        var sill = new FishWarehouseAnchorLocalBounds(
            projection.TransitionPrism.MinimumX,
            projection.TransitionPrism.MaximumX,
            0.02f,
            0.40f,
            projection.TransitionPrism.MinimumZ,
            projection.TransitionPrism.MaximumZ);
        var query = new FakeOverlapBoxQuery(
            new[] { new FishWarehouseGarageOverlapBoxHit("floor", floor) },
            new[]
            {
                new FishWarehouseGarageOverlapBoxHit("floor", floor),
                new FishWarehouseGarageOverlapBoxHit("sill", sill)
            });

        Assert.True(FishWarehouseGarageOverlapBoxClearanceDefinition.TryQueryNonTargetBounds(
            projection.TransitionPrism,
            anchor,
            query,
            Array.Empty<string>(),
            out IReadOnlyList<FishWarehouseAnchorLocalBounds> preflightBounds));
        Assert.True(FishWarehouseGarageOpeningClearanceDefinition.IsTraversableVolumeClear(projection.TransitionPrism, preflightBounds));
        Assert.True(FishWarehouseGarageOpeningClearanceDefinition.IsFloorBandClear(projection.TransitionPrism, preflightBounds));

        FishWarehouseGarageTargetSet targets = ApprovedTargets();
        var state = new FakeTargetState(targets.All);
        var mutation = new FishWarehouseGarageTargetMutationState(targets);
        Assert.False(mutation.TryOpen(preflightPassed: true, state, () =>
        {
            Assert.True(FishWarehouseGarageOverlapBoxClearanceDefinition.TryQueryNonTargetBounds(
                projection.TransitionPrism,
                anchor,
                query,
                Array.Empty<string>(),
                out IReadOnlyList<FishWarehouseAnchorLocalBounds> postOpenBounds));
            return FishWarehouseGarageOpeningClearanceDefinition.IsTraversableVolumeClear(projection.TransitionPrism, postOpenBounds);
        }));

        Assert.Equal(2, query.Requests.Count);
        foreach (FishWarehouseGarageOverlapBoxRequest request in query.Requests)
        {
            Assert.Equal(17.20f, request.WorldCenter.X, 3);
            Assert.Equal(3.68f, request.WorldCenter.Y, 3);
            Assert.Equal(6.75898f, request.WorldCenter.Z, 3);
            Assert.Equal(1.84103f, request.HalfExtents.X, 3);
            Assert.Equal(2.67f, request.HalfExtents.Y, 3);
            Assert.Equal(0.30f, request.HalfExtents.Z, 3);
            Assert.Equal(anchor.WorldRotation, request.Rotation);
        }
        Assert.True(state.IsEnabled("primary-renderer"));
        Assert.True(state.IsEnabled("primary-collider"));
        Assert.True(state.IsEnabled("child-renderer"));
        Assert.True(state.IsEnabled("child-collider"));
        Assert.False(state.IsEnabled("alternate-renderer"));
    }

    [Fact]
    public void Excludes_recorded_target_colliders_while_retaining_supporting_floor_bounds()
    {
        FishWarehouseGarageOpeningProjection projection = ApprovedProjection();
        var anchor = new FishWarehouseGarageRuntimeAnchor(
            new FishWarehouseGarageVector(0f, 0f, 0f),
            new FishWarehouseGarageQuaternion(0f, 0f, 0f, 1f));
        var floor = new FishWarehouseAnchorLocalBounds(
            projection.TransitionPrism.MinimumX,
            projection.TransitionPrism.MaximumX,
            -0.20f,
            FishWarehouseInteriorRoomDefinition.FloorY,
            projection.TransitionPrism.MinimumZ,
            projection.TransitionPrism.MaximumZ);
        var targetCollider = new FishWarehouseAnchorLocalBounds(
            projection.TransitionPrism.MinimumX,
            projection.TransitionPrism.MaximumX,
            0.10f,
            0.50f,
            projection.TransitionPrism.MinimumZ,
            projection.TransitionPrism.MaximumZ);
        var query = new FakeOverlapBoxQuery(
            new[]
            {
                new FishWarehouseGarageOverlapBoxHit("primary-collider", targetCollider),
                new FishWarehouseGarageOverlapBoxHit("floor", floor)
            });

        Assert.True(FishWarehouseGarageOverlapBoxClearanceDefinition.TryQueryNonTargetBounds(
            projection.TransitionPrism,
            anchor,
            query,
            new[] { "primary-collider" },
            out IReadOnlyList<FishWarehouseAnchorLocalBounds> nonTargetBounds));

        Assert.Single(nonTargetBounds);
        Assert.Equal(floor, nonTargetBounds[0]);
        Assert.True(FishWarehouseGarageOpeningClearanceDefinition.IsTraversableVolumeClear(
            projection.TransitionPrism,
            nonTargetBounds));
    }

    [Fact]
    public void Rejects_an_overlap_response_without_a_collider_identity_instead_of_ignoring_it()
    {
        FishWarehouseGarageOpeningProjection projection = ApprovedProjection();
        var anchor = new FishWarehouseGarageRuntimeAnchor(
            new FishWarehouseGarageVector(0f, 0f, 0f),
            new FishWarehouseGarageQuaternion(0f, 0f, 0f, 1f));
        var query = new FakeOverlapBoxQuery(
            new[]
            {
                new FishWarehouseGarageOverlapBoxHit(
                    string.Empty,
                    new FishWarehouseAnchorLocalBounds(-11f, -10f, 0.1f, 0.5f, 7f, 7.2f))
            });

        Assert.False(FishWarehouseGarageOverlapBoxClearanceDefinition.TryQueryNonTargetBounds(
            projection.TransitionPrism,
            anchor,
            query,
            Array.Empty<string>(),
            out _));
    }

    [Fact]
    public void Restores_every_target_when_a_post_open_volume_obstruction_is_detected_above_supported_floor()
    {
        FishWarehouseGarageTargetSet targets = ApprovedTargets();
        var state = new FakeTargetState(targets.All);
        var mutation = new FishWarehouseGarageTargetMutationState(targets);
        FishWarehouseGarageOpeningProjection projection = ApprovedProjection();
        var floor = new FishWarehouseAnchorLocalBounds(
            projection.TransitionPrism.MinimumX,
            projection.TransitionPrism.MaximumX,
            -0.20f,
            FishWarehouseInteriorRoomDefinition.FloorY,
            projection.TransitionPrism.MinimumZ,
            projection.TransitionPrism.MaximumZ);
        var obstruction = new FishWarehouseAnchorLocalBounds(
            projection.TransitionPrism.MinimumX,
            projection.TransitionPrism.MaximumX,
            0.10f,
            0.50f,
            projection.TransitionPrism.MinimumZ,
            projection.TransitionPrism.MaximumZ);

        Assert.False(mutation.TryOpen(
            preflightPassed: true,
            state,
            () => FishWarehouseGarageOpeningClearanceDefinition.IsTraversableVolumeClear(
                projection.TransitionPrism,
                new[] { floor, obstruction })));

        Assert.True(state.IsEnabled("primary-renderer"));
        Assert.True(state.IsEnabled("primary-collider"));
        Assert.True(state.IsEnabled("child-renderer"));
        Assert.True(state.IsEnabled("child-collider"));
        Assert.False(state.IsEnabled("alternate-renderer"));
    }

    [Fact]
    public void Rejects_an_alternate_face_without_mesh_axis_evidence()
    {
        var missingAlternateEvidence = new FishWarehouseGaragePanelAxisEvidence(
            new FishWarehouseGarageQuaternion(0f, 0f, 0f, 1f),
            new FishWarehouseGarageQuaternion(0f, 0f, 0f, 1f),
            new FishWarehouseGarageVector(0f, 0f, 0f));

        Assert.False(FishWarehouseGaragePhysicalPanelDefinition.TryDerivePhysicalNormal(
            missingAlternateEvidence,
            out _));
    }

    [Fact]
    public void Rejects_an_alternate_face_with_an_off_wall_center()
    {
        FishWarehouseAnchorLocalBounds alternateBounds = new(-11.50205f, -8.01795f, 0f, 5.25f, 7.90f, 8.00f);
        FishWarehousePhysicalPanelPlane alternatePlane = new(
            new FishWarehouseGarageVector(-9.76f, 2.625f, 7.95f),
            new FishWarehouseGarageVector(0f, 0f, 1f));

        Assert.False(FishWarehouseGarageOpeningDefinition.TryProject(
            ApprovedCandidates(alternateBounds: alternateBounds, alternatePlane: alternatePlane),
            out _));
    }

    [Fact]
    public void Rejects_an_alternate_face_with_a_non_parallel_normal()
    {
        FishWarehousePhysicalPanelPlane alternatePlane = new(
            new FishWarehouseGarageVector(-9.76f, 2.625f, 7.5f),
            new FishWarehouseGarageVector(0.15f, 0f, 0.99f));

        Assert.False(FishWarehouseGarageOpeningDefinition.TryProject(
            ApprovedCandidates(alternatePlane: alternatePlane),
            out _));
    }

    [Fact]
    public void Rejects_a_narrower_alternate_backing_face_that_does_not_cover_the_primary_aperture()
    {
        FishWarehouseAnchorLocalBounds alternateBounds = new(-11.20f, -8.30f, 0f, 5.25f, 7.40f, 7.50f);
        FishWarehousePhysicalPanelPlane alternatePlane = new(
            new FishWarehouseGarageVector(-9.75f, 2.625f, 7.5f),
            new FishWarehouseGarageVector(0f, 0f, -1f));

        Assert.False(FishWarehouseGarageOpeningDefinition.TryProject(
            ApprovedCandidates(alternateBounds: alternateBounds, alternatePlane: alternatePlane),
            out _));
    }

    [Fact]
    public void Selects_only_direct_components_on_the_validated_primary_child_and_alternate_nodes()
    {
        Assert.True(FishWarehouseGarageSceneSelectionDefinition.TrySelect(
            new[]
            {
                Node(FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath, "GarageDoor (1)", null),
                Node(FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath + "/GarageDoor (1)", "GarageDoor (1)", FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath),
                Node(FishWarehouseGarageOpeningDefinition.ApprovedAlternateRootPath, "GarageDoor_Other", null),
                Node(FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath + "/GarageDoor (1)/Facade", "Facade", FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath + "/GarageDoor (1)")
            },
            out FishWarehouseGarageSceneSelection? selection));

        Assert.True(FishWarehouseGarageTargetSetDefinition.TryCreate(
            selection!,
            new[]
            {
                Component(FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath, "primary-renderer", FishWarehouseGarageTargetComponentKind.Renderer, true),
                Component(FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath, "primary-collider", FishWarehouseGarageTargetComponentKind.Collider, true),
                Component(FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath + "/GarageDoor (1)", "child-renderer", FishWarehouseGarageTargetComponentKind.Renderer, true),
                Component(FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath + "/GarageDoor (1)", "child-collider", FishWarehouseGarageTargetComponentKind.Collider, true),
                Component(FishWarehouseGarageOpeningDefinition.ApprovedAlternateRootPath, "alternate-renderer", FishWarehouseGarageTargetComponentKind.Renderer, false),
                Component(FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath + "/GarageDoor (1)/Facade", "facade-renderer", FishWarehouseGarageTargetComponentKind.Renderer, true),
                Component(FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath + "/GarageDoor (1)/Facade", "facade-collider", FishWarehouseGarageTargetComponentKind.Collider, true)
            },
            out FishWarehouseGarageTargetSet? targets));

        Assert.Equal(
            new[] { "primary-renderer", "child-renderer", "alternate-renderer" },
            targets!.Renderers.Select(component => component.Identity));
        Assert.Equal(
            new[] { "primary-collider", "child-collider" },
            targets.Colliders.Select(component => component.Identity));
        Assert.DoesNotContain(targets.All, component => component.Identity.StartsWith("facade", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_the_trusted_target_census_when_the_primary_root_collider_is_missing()
    {
        FishWarehouseGarageSceneSelection selection = ApprovedSelection();

        Assert.False(FishWarehouseGarageTargetSetDefinition.TryCreate(
            selection,
            new[]
            {
                Component(selection.PrimaryRootPath, "primary-renderer", FishWarehouseGarageTargetComponentKind.Renderer, true),
                Component(selection.PrimaryPanelPath, "child-renderer", FishWarehouseGarageTargetComponentKind.Renderer, true),
                Component(selection.PrimaryPanelPath, "child-collider", FishWarehouseGarageTargetComponentKind.Collider, true),
                Component(selection.AlternateRootPath, "alternate-renderer", FishWarehouseGarageTargetComponentKind.Renderer, false)
            },
            out _));
    }

    [Fact]
    public void Leaves_every_target_untouched_when_preflight_failed_before_opening()
    {
        FishWarehouseGarageTargetSet targets = ApprovedTargets();
        var state = new FakeTargetState(targets.All.Append(
            Component("unrelated", "facade-collider", FishWarehouseGarageTargetComponentKind.Collider, true)));
        var mutation = new FishWarehouseGarageTargetMutationState(targets);

        Assert.False(mutation.TryOpen(preflightPassed: false, state, postOpenPrismIsClear: static () => true));

        Assert.True(state.IsEnabled("primary-renderer"));
        Assert.True(state.IsEnabled("primary-collider"));
        Assert.True(state.IsEnabled("child-renderer"));
        Assert.True(state.IsEnabled("child-collider"));
        Assert.False(state.IsEnabled("alternate-renderer"));
        Assert.True(state.IsEnabled("facade-collider"));
    }

    [Fact]
    public void Disables_only_the_recorded_direct_targets_when_the_post_open_prism_is_clear()
    {
        FishWarehouseGarageTargetSet targets = ApprovedTargets();
        var state = new FakeTargetState(targets.All.Append(
            Component("unrelated", "facade-collider", FishWarehouseGarageTargetComponentKind.Collider, true)));
        var mutation = new FishWarehouseGarageTargetMutationState(targets);

        Assert.True(mutation.TryOpen(preflightPassed: true, state, postOpenPrismIsClear: static () => true));

        Assert.False(state.IsEnabled("primary-renderer"));
        Assert.False(state.IsEnabled("primary-collider"));
        Assert.False(state.IsEnabled("child-renderer"));
        Assert.False(state.IsEnabled("child-collider"));
        Assert.False(state.IsEnabled("alternate-renderer"));
        Assert.True(state.IsEnabled("facade-collider"));
    }

    [Fact]
    public void Mutates_only_the_target_set_and_restores_it_idempotently_after_a_post_open_failure()
    {
        FishWarehouseGarageTargetSet targets = ApprovedTargets();
        var state = new FakeTargetState(targets.All.Append(
            Component("unrelated", "facade-collider", FishWarehouseGarageTargetComponentKind.Collider, true)));
        var mutation = new FishWarehouseGarageTargetMutationState(targets);

        Assert.False(mutation.TryOpen(preflightPassed: true, state, postOpenPrismIsClear: static () => false));
        Assert.True(mutation.Restore(state));
        Assert.True(mutation.Restore(state));

        Assert.True(state.IsEnabled("primary-renderer"));
        Assert.True(state.IsEnabled("primary-collider"));
        Assert.True(state.IsEnabled("child-renderer"));
        Assert.True(state.IsEnabled("child-collider"));
        Assert.False(state.IsEnabled("alternate-renderer"));
        Assert.True(state.IsEnabled("facade-collider"));
    }

    [Fact]
    public void Rejects_non_unit_lossy_scale_before_geometry_can_be_created_or_mutated()
    {
        var anchor = new FishWarehouseTransformSignature(
            new FishWarehouseTransformVector(0f, 0f, 0f),
            new FishWarehouseTransformVector(0f, 0f, 0f),
            new FishWarehouseTransformVector(1.1f, 1f, 1f));
        var propertyRoot = new FishWarehouseTransformSignature(
            new FishWarehouseTransformVector(0f, 0f, 0f),
            new FishWarehouseTransformVector(0f, 0f, 0f),
            new FishWarehouseTransformVector(1.1f, 1f, 1f));

        Assert.False(FishWarehouseTransformSignatureDefinition.IsCoincident(anchor, propertyRoot));
        Assert.Equal(new FishWarehouseTransformVector(1.1f, 1f, 1f), anchor.LossyScale);
    }

    private static FishWarehouseGarageOpeningCandidate[] ApprovedCandidates(
        FishWarehouseAnchorLocalBounds bounds,
        FishWarehousePhysicalPanelPlane plane) =>
        new[]
        {
            new FishWarehouseGarageOpeningCandidate(
                FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath,
                "GarageDoor (1)",
                bounds,
                plane,
                Array.Empty<FishWarehouseAnchorLocalBounds>(),
                HasFloorSupport: true),
            new FishWarehouseGarageOpeningCandidate(
                FishWarehouseGarageOpeningDefinition.ApprovedAlternateRootPath,
                "GarageDoor_Other",
                bounds,
                plane,
                Array.Empty<FishWarehouseAnchorLocalBounds>(),
                HasFloorSupport: true)
        };

    private static FishWarehouseGarageOpeningCandidate[] ApprovedCandidates(
        FishWarehouseAnchorLocalBounds? alternateBounds = null,
        FishWarehousePhysicalPanelPlane? alternatePlane = null,
        IReadOnlyList<FishWarehouseAnchorLocalBounds>? survivingNonTargetColliders = null)
    {
        var primaryBounds = new FishWarehouseAnchorLocalBounds(-11.50205f, -8.01795f, 0f, 5.25f, 7.40f, 7.50f);
        FishWarehousePhysicalPanelPlane primaryPlane = new(
            new FishWarehouseGarageVector(-9.76f, 2.625f, 7.5f),
            new FishWarehouseGarageVector(0f, 0f, 1f));
        FishWarehouseAnchorLocalBounds alternateEnvelope = alternateBounds ??
            new FishWarehouseAnchorLocalBounds(-11.50205f, -8.01795f, 0f, 5.25f, 7.40f, 7.50f);
        FishWarehousePhysicalPanelPlane alternateFace = alternatePlane ?? new(
            new FishWarehouseGarageVector(-9.76f, 2.625f, 7.5f),
            new FishWarehouseGarageVector(0f, 0f, -1f));
        IReadOnlyList<FishWarehouseAnchorLocalBounds> colliders = survivingNonTargetColliders ??
            Array.Empty<FishWarehouseAnchorLocalBounds>();
        return new[]
        {
            new FishWarehouseGarageOpeningCandidate(
                FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath,
                "GarageDoor (1)",
                primaryBounds,
                primaryPlane,
                colliders,
                HasFloorSupport: true),
            new FishWarehouseGarageOpeningCandidate(
                FishWarehouseGarageOpeningDefinition.ApprovedAlternateRootPath,
                "GarageDoor_Other",
                alternateEnvelope,
                alternateFace,
                colliders,
                HasFloorSupport: true)
        };
    }

    private static FishWarehouseGarageOpeningProjection ApprovedProjection()
    {
        Assert.True(FishWarehouseGarageOpeningDefinition.TryProject(ApprovedCandidates(), out FishWarehouseGarageOpeningProjection? projection));
        return projection!;
    }

    private static FishWarehouseGarageSceneSelection ApprovedSelection()
    {
        Assert.True(FishWarehouseGarageSceneSelectionDefinition.TrySelect(
            new[]
            {
                Node(FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath, "GarageDoor (1)", null),
                Node(FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath + "/GarageDoor (1)", "GarageDoor (1)", FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath),
                Node(FishWarehouseGarageOpeningDefinition.ApprovedAlternateRootPath, "GarageDoor_Other", null)
            },
            out FishWarehouseGarageSceneSelection? selection));
        return selection!;
    }

    private static FishWarehouseGarageTargetSet ApprovedTargets()
    {
        FishWarehouseGarageSceneSelection selection = ApprovedSelection();
        Assert.True(FishWarehouseGarageTargetSetDefinition.TryCreate(
            selection,
            new[]
            {
                Component(selection.PrimaryRootPath, "primary-renderer", FishWarehouseGarageTargetComponentKind.Renderer, true),
                Component(selection.PrimaryRootPath, "primary-collider", FishWarehouseGarageTargetComponentKind.Collider, true),
                Component(selection.PrimaryPanelPath, "child-renderer", FishWarehouseGarageTargetComponentKind.Renderer, true),
                Component(selection.PrimaryPanelPath, "child-collider", FishWarehouseGarageTargetComponentKind.Collider, true),
                Component(selection.AlternateRootPath, "alternate-renderer", FishWarehouseGarageTargetComponentKind.Renderer, false)
            },
            out FishWarehouseGarageTargetSet? targets));
        return targets!;
    }

    private static FishWarehouseGarageSceneNode Node(string path, string name, string? parentPath) =>
        new(path, name, parentPath);

    private static FishWarehouseGarageTargetComponent Component(
        string nodePath,
        string identity,
        FishWarehouseGarageTargetComponentKind kind,
        bool enabled) =>
        new(nodePath, identity, kind, enabled);

    private sealed class FakeTargetState : IFishWarehouseGarageTargetState
    {
        private readonly Dictionary<string, bool> _enabled;

        public FakeTargetState(IEnumerable<FishWarehouseGarageTargetComponent> components)
        {
            _enabled = components.ToDictionary(component => component.Identity, component => component.Enabled, StringComparer.Ordinal);
        }

        public bool TryGetEnabled(FishWarehouseGarageTargetComponent component, out bool enabled) =>
            _enabled.TryGetValue(component.Identity, out enabled);

        public bool TrySetEnabled(FishWarehouseGarageTargetComponent component, bool enabled)
        {
            if (!_enabled.ContainsKey(component.Identity))
                return false;

            _enabled[component.Identity] = enabled;
            return true;
        }

        public bool IsEnabled(string identity) => _enabled[identity];
    }

    private sealed class FakeOverlapBoxQuery : IFishWarehouseGarageOverlapBoxQuery
    {
        private readonly Queue<IReadOnlyList<FishWarehouseGarageOverlapBoxHit>> _responses;

        public FakeOverlapBoxQuery(params IReadOnlyList<FishWarehouseGarageOverlapBoxHit>[] responses)
        {
            _responses = new Queue<IReadOnlyList<FishWarehouseGarageOverlapBoxHit>>(responses);
        }

        public List<FishWarehouseGarageOverlapBoxRequest> Requests { get; } = new();

        public IReadOnlyList<FishWarehouseGarageOverlapBoxHit> OverlapBox(FishWarehouseGarageOverlapBoxRequest request)
        {
            Requests.Add(request);
            return _responses.Dequeue();
        }
    }
}
