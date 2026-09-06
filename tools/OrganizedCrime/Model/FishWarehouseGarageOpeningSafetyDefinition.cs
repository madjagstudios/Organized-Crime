namespace OrganizedCrime.Model;

public readonly record struct FishWarehouseGarageVector(float X, float Y, float Z)
{
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);
}

/// <summary>
/// Rotation-only evidence captured from the approved primary root and its
/// exact direct renderer child. Keeping this outside Unity lets the live host
/// and pure tests share the same axis derivation.
/// </summary>
public readonly record struct FishWarehouseGarageQuaternion(float X, float Y, float Z, float W)
{
    public bool IsFinite =>
        float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) && float.IsFinite(W);

    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z + W * W);
}

public readonly record struct FishWarehouseGaragePanelAxisEvidence(
    FishWarehouseGarageQuaternion PrimaryRotationRelativeToAnchor,
    FishWarehouseGarageQuaternion SelectedChildRotationRelativeToPrimary,
    FishWarehouseGarageVector PhysicalAxisExtents);

public readonly record struct FishWarehouseGaragePanelAxes(
    FishWarehouseGarageVector Right,
    FishWarehouseGarageVector Up,
    FishWarehouseGarageVector Forward);

/// <summary>
/// Derives the panel normal from the uniquely thin mesh axis rather than from
/// an assumed renderer basis. Ambiguous or non-panel-like mesh evidence is a
/// fail-closed condition.
/// </summary>
public static class FishWarehouseGaragePhysicalPanelDefinition
{
    private const float MinimumDistinctAxisExtent = 0.01f;
    private const float MaximumThinAxisRatio = 0.25f;

    public static bool TryGetPhysicalAxisExtents(
        FishWarehouseGarageVector meshLocalExtents,
        FishWarehouseGarageVector lossyScale,
        out FishWarehouseGarageVector physicalAxisExtents)
    {
        physicalAxisExtents = default;
        if (!meshLocalExtents.IsFinite || !lossyScale.IsFinite ||
            meshLocalExtents.X < 0f || meshLocalExtents.Y < 0f || meshLocalExtents.Z < 0f)
        {
            return false;
        }

        physicalAxisExtents = new FishWarehouseGarageVector(
            MathF.Abs(meshLocalExtents.X * lossyScale.X),
            MathF.Abs(meshLocalExtents.Y * lossyScale.Y),
            MathF.Abs(meshLocalExtents.Z * lossyScale.Z));
        return physicalAxisExtents.IsFinite;
    }

    public static bool TryDeriveAnchorLocalAxes(
        FishWarehouseGaragePanelAxisEvidence evidence,
        out FishWarehouseGaragePanelAxes axes)
    {
        axes = default;
        if (!TryNormalize(evidence.PrimaryRotationRelativeToAnchor, out FishWarehouseGarageQuaternion primary) ||
            !TryNormalize(evidence.SelectedChildRotationRelativeToPrimary, out FishWarehouseGarageQuaternion child))
        {
            return false;
        }

        FishWarehouseGarageQuaternion combined = Multiply(primary, child);
        axes = new FishWarehouseGaragePanelAxes(
            Rotate(combined, new FishWarehouseGarageVector(1f, 0f, 0f)),
            Rotate(combined, new FishWarehouseGarageVector(0f, 1f, 0f)),
            Rotate(combined, new FishWarehouseGarageVector(0f, 0f, 1f)));
        return axes.Right.IsFinite && axes.Up.IsFinite && axes.Forward.IsFinite;
    }

    public static bool TryDerivePhysicalNormal(
        FishWarehouseGaragePanelAxisEvidence evidence,
        out FishWarehouseGarageVector normal)
    {
        normal = default;
        if (!TrySelectThinAxis(evidence.PhysicalAxisExtents, out int axis) ||
            !TryDeriveAnchorLocalAxes(evidence, out FishWarehouseGaragePanelAxes axes))
        {
            return false;
        }

        normal = axis switch
        {
            0 => axes.Right,
            1 => axes.Up,
            _ => axes.Forward
        };
        return normal.IsFinite && normal.Length > 0f;
    }

    private static bool TrySelectThinAxis(FishWarehouseGarageVector extents, out int axis)
    {
        axis = -1;
        if (!extents.IsFinite || extents.X < 0f || extents.Y < 0f || extents.Z < 0f)
            return false;

        float[] values = { extents.X, extents.Y, extents.Z };
        int smallest = 0;
        for (int index = 1; index < values.Length; index++)
        {
            if (values[index] < values[smallest])
                smallest = index;
        }

        float nextSmallest = values
            .Where((_, index) => index != smallest)
            .Min();
        if (nextSmallest < MinimumDistinctAxisExtent ||
            values[smallest] + MinimumDistinctAxisExtent >= nextSmallest ||
            values[smallest] > nextSmallest * MaximumThinAxisRatio)
        {
            return false;
        }

        axis = smallest;
        return true;
    }

    private static bool TryNormalize(
        FishWarehouseGarageQuaternion value,
        out FishWarehouseGarageQuaternion normalized)
    {
        normalized = default;
        float length = value.Length;
        if (!value.IsFinite || !float.IsFinite(length) || length <= 0f)
            return false;

        normalized = new FishWarehouseGarageQuaternion(
            value.X / length,
            value.Y / length,
            value.Z / length,
            value.W / length);
        return true;
    }

    private static FishWarehouseGarageQuaternion Multiply(
        FishWarehouseGarageQuaternion left,
        FishWarehouseGarageQuaternion right) =>
        new(
            left.W * right.X + left.X * right.W + left.Y * right.Z - left.Z * right.Y,
            left.W * right.Y - left.X * right.Z + left.Y * right.W + left.Z * right.X,
            left.W * right.Z + left.X * right.Y - left.Y * right.X + left.Z * right.W,
            left.W * right.W - left.X * right.X - left.Y * right.Y - left.Z * right.Z);

    private static FishWarehouseGarageVector Rotate(
        FishWarehouseGarageQuaternion rotation,
        FishWarehouseGarageVector vector)
    {
        FishWarehouseGarageQuaternion rotated = Multiply(
            Multiply(rotation, new FishWarehouseGarageQuaternion(vector.X, vector.Y, vector.Z, 0f)),
            new FishWarehouseGarageQuaternion(-rotation.X, -rotation.Y, -rotation.Z, rotation.W));
        return new FishWarehouseGarageVector(rotated.X, rotated.Y, rotated.Z);
    }
}

public readonly record struct FishWarehouseWorldBounds(
    FishWarehouseGarageVector Center,
    FishWarehouseGarageVector Extents)
{
    public bool IsValid =>
        Center.IsFinite && Extents.IsFinite &&
        Extents.X >= 0f && Extents.Y >= 0f && Extents.Z >= 0f;
}

public readonly record struct FishWarehouseAnchorFrame(
    FishWarehouseGarageVector Position,
    float yawDegrees);

/// <summary>
/// The complete primitive transform evidence the Unity host collects for a
/// renderer. Mapping it here keeps rotation, scale, mesh-axis, and AABB
/// decisions testable and prevents the host from maintaining a second model.
/// </summary>
public readonly record struct FishWarehouseGarageRuntimeAnchor(
    FishWarehouseGarageVector WorldPosition,
    FishWarehouseGarageQuaternion WorldRotation);

public readonly record struct FishWarehouseGaragePanelRuntimeEvidence(
    FishWarehouseWorldBounds WorldRendererBounds,
    FishWarehouseGarageVector MeshLocalCenter,
    FishWarehouseGarageVector MeshLocalExtents,
    FishWarehouseGarageVector RendererLossyScale,
    FishWarehouseGarageVector RendererWorldPosition,
    FishWarehouseGarageQuaternion PanelRootWorldRotation,
    FishWarehouseGarageQuaternion RendererWorldRotation,
    bool IsPartOfStaticBatch = false);

public enum FishWarehouseGaragePanelPlaneSource
{
    MeshCenterAndThinAxis,
    StaticBatchRendererBoundsCenterAndPanelRootUp
}

public readonly record struct FishWarehouseGaragePanelRuntimeMapping(
    FishWarehouseAnchorLocalBounds SafetyEnvelope,
    FishWarehousePhysicalPanelPlane PhysicalPanelPlane,
    FishWarehouseGaragePanelPlaneSource PlaneSource);

public static class FishWarehouseGarageRuntimeEvidenceMapper
{
    public static bool TryMap(
        FishWarehouseGarageRuntimeAnchor anchor,
        FishWarehouseGaragePanelRuntimeEvidence evidence,
        out FishWarehouseGaragePanelRuntimeMapping mapping)
    {
        mapping = default;
        if (!anchor.WorldPosition.IsFinite || !evidence.WorldRendererBounds.IsValid ||
            !TryNormalize(anchor.WorldRotation, out FishWarehouseGarageQuaternion anchorRotation) ||
            !TryNormalize(evidence.PanelRootWorldRotation, out FishWarehouseGarageQuaternion panelRootRotation) ||
            !TryNormalize(evidence.RendererWorldRotation, out FishWarehouseGarageQuaternion rendererRotation))
        {
            return false;
        }

        FishWarehouseAnchorLocalBounds envelope = ToAnchorLocalBounds(evidence.WorldRendererBounds, anchor, anchorRotation);
        if (!envelope.IsValid)
            return false;

        FishWarehouseGarageQuaternion panelRootRelativeToAnchor = Multiply(Inverse(anchorRotation), panelRootRotation);
        FishWarehouseGarageVector center;
        FishWarehouseGarageVector normal;
        FishWarehouseGaragePanelPlaneSource planeSource;
        if (evidence.IsPartOfStaticBatch)
        {
            center = ToAnchorLocalPoint(evidence.WorldRendererBounds.Center, anchor, anchorRotation);
            normal = Rotate(panelRootRelativeToAnchor, new FishWarehouseGarageVector(0f, 1f, 0f));
            planeSource = FishWarehouseGaragePanelPlaneSource.StaticBatchRendererBoundsCenterAndPanelRootUp;
        }
        else
        {
            if (!evidence.MeshLocalCenter.IsFinite || !evidence.RendererLossyScale.IsFinite ||
                !evidence.RendererWorldPosition.IsFinite ||
                !FishWarehouseGaragePhysicalPanelDefinition.TryGetPhysicalAxisExtents(
                    evidence.MeshLocalExtents,
                    evidence.RendererLossyScale,
                    out FishWarehouseGarageVector physicalAxisExtents))
            {
                return false;
            }

            FishWarehouseGarageVector scaledMeshCenter = new(
                evidence.MeshLocalCenter.X * evidence.RendererLossyScale.X,
                evidence.MeshLocalCenter.Y * evidence.RendererLossyScale.Y,
                evidence.MeshLocalCenter.Z * evidence.RendererLossyScale.Z);
            FishWarehouseGarageVector worldMeshCenter = Add(
                evidence.RendererWorldPosition,
                Rotate(rendererRotation, scaledMeshCenter));
            center = ToAnchorLocalPoint(worldMeshCenter, anchor, anchorRotation);
            FishWarehouseGarageQuaternion rendererRelativeToPanelRoot = Multiply(Inverse(panelRootRotation), rendererRotation);
            var axisEvidence = new FishWarehouseGaragePanelAxisEvidence(
                panelRootRelativeToAnchor,
                rendererRelativeToPanelRoot,
                physicalAxisExtents);
            if (!FishWarehouseGaragePhysicalPanelDefinition.TryDerivePhysicalNormal(
                    axisEvidence,
                    out normal))
            {
                return false;
            }

            planeSource = FishWarehouseGaragePanelPlaneSource.MeshCenterAndThinAxis;
        }

        if (!center.IsFinite || !normal.IsFinite || normal.Length <= 0f)
            return false;

        mapping = new FishWarehouseGaragePanelRuntimeMapping(
            envelope,
            new FishWarehousePhysicalPanelPlane(center, normal),
            planeSource);
        return true;
    }

    public static FishWarehouseAnchorLocalBounds ToAnchorLocalBounds(
        FishWarehouseWorldBounds worldBounds,
        FishWarehouseGarageRuntimeAnchor anchor)
    {
        if (!TryNormalize(anchor.WorldRotation, out FishWarehouseGarageQuaternion anchorRotation))
            return InvalidBounds();

        return ToAnchorLocalBounds(worldBounds, anchor, anchorRotation);
    }

    private static FishWarehouseAnchorLocalBounds ToAnchorLocalBounds(
        FishWarehouseWorldBounds worldBounds,
        FishWarehouseGarageRuntimeAnchor anchor,
        FishWarehouseGarageQuaternion anchorRotation)
    {
        FishWarehouseGarageVector minimum = new(
            worldBounds.Center.X - worldBounds.Extents.X,
            worldBounds.Center.Y - worldBounds.Extents.Y,
            worldBounds.Center.Z - worldBounds.Extents.Z);
        FishWarehouseGarageVector maximum = new(
            worldBounds.Center.X + worldBounds.Extents.X,
            worldBounds.Center.Y + worldBounds.Extents.Y,
            worldBounds.Center.Z + worldBounds.Extents.Z);
        return FishWarehouseGarageOpeningGeometryDefinition.ToAnchorLocalBounds(
            new[]
            {
                new FishWarehouseGarageVector(minimum.X, minimum.Y, minimum.Z),
                new FishWarehouseGarageVector(minimum.X, minimum.Y, maximum.Z),
                new FishWarehouseGarageVector(minimum.X, maximum.Y, minimum.Z),
                new FishWarehouseGarageVector(minimum.X, maximum.Y, maximum.Z),
                new FishWarehouseGarageVector(maximum.X, minimum.Y, minimum.Z),
                new FishWarehouseGarageVector(maximum.X, minimum.Y, maximum.Z),
                new FishWarehouseGarageVector(maximum.X, maximum.Y, minimum.Z),
                new FishWarehouseGarageVector(maximum.X, maximum.Y, maximum.Z)
            },
            point => ToAnchorLocalPoint(point, anchor, anchorRotation));
    }

    private static FishWarehouseGarageVector ToAnchorLocalPoint(
        FishWarehouseGarageVector worldPoint,
        FishWarehouseGarageRuntimeAnchor anchor,
        FishWarehouseGarageQuaternion anchorRotation) =>
        Rotate(Inverse(anchorRotation), new FishWarehouseGarageVector(
            worldPoint.X - anchor.WorldPosition.X,
            worldPoint.Y - anchor.WorldPosition.Y,
            worldPoint.Z - anchor.WorldPosition.Z));

    internal static bool TryNormalize(
        FishWarehouseGarageQuaternion value,
        out FishWarehouseGarageQuaternion normalized)
    {
        normalized = default;
        float length = value.Length;
        if (!value.IsFinite || !float.IsFinite(length) || length <= 0f)
            return false;

        normalized = new FishWarehouseGarageQuaternion(
            value.X / length,
            value.Y / length,
            value.Z / length,
            value.W / length);
        return true;
    }

    internal static FishWarehouseGarageQuaternion Multiply(
        FishWarehouseGarageQuaternion left,
        FishWarehouseGarageQuaternion right) =>
        new(
            left.W * right.X + left.X * right.W + left.Y * right.Z - left.Z * right.Y,
            left.W * right.Y - left.X * right.Z + left.Y * right.W + left.Z * right.X,
            left.W * right.Z + left.X * right.Y - left.Y * right.X + left.Z * right.W,
            left.W * right.W - left.X * right.X - left.Y * right.Y - left.Z * right.Z);

    internal static FishWarehouseGarageQuaternion Inverse(FishWarehouseGarageQuaternion value) =>
        new(-value.X, -value.Y, -value.Z, value.W);

    internal static FishWarehouseGarageVector Rotate(
        FishWarehouseGarageQuaternion rotation,
        FishWarehouseGarageVector vector)
    {
        FishWarehouseGarageQuaternion rotated = Multiply(
            Multiply(rotation, new FishWarehouseGarageQuaternion(vector.X, vector.Y, vector.Z, 0f)),
            Inverse(rotation));
        return new FishWarehouseGarageVector(rotated.X, rotated.Y, rotated.Z);
    }

    internal static FishWarehouseGarageVector Add(
        FishWarehouseGarageVector left,
        FishWarehouseGarageVector right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static FishWarehouseAnchorLocalBounds InvalidBounds() =>
        new(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN);
}

public readonly record struct FishWarehouseGarageOverlapBoxRequest(
    FishWarehouseGarageVector WorldCenter,
    FishWarehouseGarageVector HalfExtents,
    FishWarehouseGarageQuaternion Rotation);

public readonly record struct FishWarehouseGarageOverlapBoxHit(
    string Identity,
    FishWarehouseAnchorLocalBounds AnchorLocalBounds);

public interface IFishWarehouseGarageOverlapBoxQuery
{
    IReadOnlyList<FishWarehouseGarageOverlapBoxHit> OverlapBox(FishWarehouseGarageOverlapBoxRequest request);
}

public static class FishWarehouseGarageOverlapBoxRequestDefinition
{
    public static bool TryCreate(
        FishWarehouseTransitionPrism prism,
        FishWarehouseGarageRuntimeAnchor anchor,
        out FishWarehouseGarageOverlapBoxRequest request)
    {
        request = default;
        if (!FishWarehouseGarageOpeningClearanceDefinition.TryGetTraversableVolume(
                prism,
                out FishWarehouseTransitionPrism volume) ||
            !anchor.WorldPosition.IsFinite ||
            !FishWarehouseGarageRuntimeEvidenceMapper.TryNormalize(anchor.WorldRotation, out FishWarehouseGarageQuaternion rotation))
        {
            return false;
        }

        FishWarehouseGarageVector localCenter = new(
            (volume.MinimumX + volume.MaximumX) / 2f,
            (volume.MinimumY + volume.MaximumY) / 2f,
            (volume.MinimumZ + volume.MaximumZ) / 2f);
        FishWarehouseGarageVector halfExtents = new(
            (volume.MaximumX - volume.MinimumX) / 2f,
            (volume.MaximumY - volume.MinimumY) / 2f,
            (volume.MaximumZ - volume.MinimumZ) / 2f);
        request = new FishWarehouseGarageOverlapBoxRequest(
            FishWarehouseGarageRuntimeEvidenceMapper.Add(
                anchor.WorldPosition,
                FishWarehouseGarageRuntimeEvidenceMapper.Rotate(rotation, localCenter)),
            halfExtents,
            anchor.WorldRotation);
        return request.WorldCenter.IsFinite && request.HalfExtents.IsFinite &&
               request.HalfExtents.X > 0f && request.HalfExtents.Y > 0f && request.HalfExtents.Z > 0f;
    }
}

public static class FishWarehouseGarageOverlapBoxClearanceDefinition
{
    public static bool TryQueryNonTargetBounds(
        FishWarehouseTransitionPrism prism,
        FishWarehouseGarageRuntimeAnchor anchor,
        IFishWarehouseGarageOverlapBoxQuery? query,
        IEnumerable<string>? targetColliderIds,
        out IReadOnlyList<FishWarehouseAnchorLocalBounds> nonTargetBounds)
    {
        nonTargetBounds = Array.Empty<FishWarehouseAnchorLocalBounds>();
        if (query is null || targetColliderIds is null ||
            !FishWarehouseGarageOverlapBoxRequestDefinition.TryCreate(prism, anchor, out FishWarehouseGarageOverlapBoxRequest request))
        {
            return false;
        }

        IReadOnlyList<FishWarehouseGarageOverlapBoxHit> hits = query.OverlapBox(request);
        if (hits is null || hits.Any(hit => string.IsNullOrWhiteSpace(hit.Identity)))
            return false;

        var targets = targetColliderIds.ToHashSet(StringComparer.Ordinal);
        FishWarehouseAnchorLocalBounds[] bounds = hits
            .Where(hit => !targets.Contains(hit.Identity))
            .Select(hit => hit.AnchorLocalBounds)
            .ToArray();
        if (bounds.Any(bound => !bound.IsValid))
            return false;

        nonTargetBounds = bounds;
        return true;
    }
}

/// <summary>
/// The physical panel plane is intentionally distinct from the axis-aligned
/// anchor-local envelope produced by a world-space renderer AABB. The latter
/// is a conservative safety envelope; the former supplies the north-wall
/// normal and transition depth.
/// </summary>
public readonly record struct FishWarehousePhysicalPanelPlane(
    FishWarehouseGarageVector AnchorLocalCenter,
    FishWarehouseGarageVector AnchorLocalNormal);

public static class FishWarehouseGarageOpeningGeometryDefinition
{
    private const float MinimumNorthNormalAlignment = 0.98f;
    private const float MaximumVerticalNormalAlignment = 0.02f;
    private const float PlaneEnvelopeCenterTolerance = 0.25f;
    private const float AlternatePlaneCenterTolerance = 0.25f;
    private const float AlternateApertureEdgeTolerance = 0.25f;
    private const float MinimumAlternateNormalParallelism = 0.995f;

    public static FishWarehouseAnchorLocalBounds ToAnchorLocalBounds(
        FishWarehouseWorldBounds worldBounds,
        FishWarehouseAnchorFrame anchor)
    {
        if (!worldBounds.IsValid || !anchor.Position.IsFinite || !float.IsFinite(anchor.yawDegrees))
            return InvalidBounds();

        FishWarehouseGarageVector minimum = new(
            worldBounds.Center.X - worldBounds.Extents.X,
            worldBounds.Center.Y - worldBounds.Extents.Y,
            worldBounds.Center.Z - worldBounds.Extents.Z);
        FishWarehouseGarageVector maximum = new(
            worldBounds.Center.X + worldBounds.Extents.X,
            worldBounds.Center.Y + worldBounds.Extents.Y,
            worldBounds.Center.Z + worldBounds.Extents.Z);
        return ToAnchorLocalBounds(new[]
        {
            new FishWarehouseGarageVector(minimum.X, minimum.Y, minimum.Z),
            new FishWarehouseGarageVector(minimum.X, minimum.Y, maximum.Z),
            new FishWarehouseGarageVector(minimum.X, maximum.Y, minimum.Z),
            new FishWarehouseGarageVector(minimum.X, maximum.Y, maximum.Z),
            new FishWarehouseGarageVector(maximum.X, minimum.Y, minimum.Z),
            new FishWarehouseGarageVector(maximum.X, minimum.Y, maximum.Z),
            new FishWarehouseGarageVector(maximum.X, maximum.Y, minimum.Z),
            new FishWarehouseGarageVector(maximum.X, maximum.Y, maximum.Z)
        }, point => ToAnchorLocalPoint(point, anchor));
    }

    public static FishWarehouseAnchorLocalBounds ToAnchorLocalBounds(
        IEnumerable<FishWarehouseGarageVector>? worldCorners,
        Func<FishWarehouseGarageVector, FishWarehouseGarageVector>? toAnchorLocalPoint)
    {
        if (worldCorners is null || toAnchorLocalPoint is null)
            return InvalidBounds();

        bool initialized = false;
        float minimumX = 0f;
        float maximumX = 0f;
        float minimumY = 0f;
        float maximumY = 0f;
        float minimumZ = 0f;
        float maximumZ = 0f;
        foreach (FishWarehouseGarageVector worldCorner in worldCorners)
        {
            FishWarehouseGarageVector local = toAnchorLocalPoint(worldCorner);
            if (!local.IsFinite)
                return InvalidBounds();

            if (!initialized)
            {
                minimumX = maximumX = local.X;
                minimumY = maximumY = local.Y;
                minimumZ = maximumZ = local.Z;
                initialized = true;
                continue;
            }

            minimumX = MathF.Min(minimumX, local.X);
            maximumX = MathF.Max(maximumX, local.X);
            minimumY = MathF.Min(minimumY, local.Y);
            maximumY = MathF.Max(maximumY, local.Y);
            minimumZ = MathF.Min(minimumZ, local.Z);
            maximumZ = MathF.Max(maximumZ, local.Z);
        }

        return initialized
            ? new FishWarehouseAnchorLocalBounds(minimumX, maximumX, minimumY, maximumY, minimumZ, maximumZ)
            : InvalidBounds();
    }

    public static FishWarehouseGarageVector ToAnchorLocalPoint(
        FishWarehouseGarageVector worldPoint,
        FishWarehouseAnchorFrame anchor)
    {
        FishWarehouseGarageVector offset = new(
            worldPoint.X - anchor.Position.X,
            worldPoint.Y - anchor.Position.Y,
            worldPoint.Z - anchor.Position.Z);
        return ToAnchorLocalDirection(offset, anchor);
    }

    public static FishWarehouseGarageVector ToAnchorLocalDirection(
        FishWarehouseGarageVector worldDirection,
        FishWarehouseAnchorFrame anchor)
    {
        float radians = -anchor.yawDegrees * MathF.PI / 180f;
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        return new FishWarehouseGarageVector(
            cosine * worldDirection.X + sine * worldDirection.Z,
            worldDirection.Y,
            -sine * worldDirection.X + cosine * worldDirection.Z);
    }

    public static bool IsNorthWallPhysicalPlane(
        FishWarehouseAnchorLocalBounds safetyEnvelope,
        FishWarehousePhysicalPanelPlane plane)
    {
        if (!safetyEnvelope.IsValid ||
            !plane.AnchorLocalCenter.IsFinite ||
            !plane.AnchorLocalNormal.IsFinite)
        {
            return false;
        }

        float envelopeCenterX = (safetyEnvelope.MinimumX + safetyEnvelope.MaximumX) / 2f;
        float envelopeCenterZ = (safetyEnvelope.MinimumZ + safetyEnvelope.MaximumZ) / 2f;
        if (MathF.Abs(plane.AnchorLocalCenter.X - envelopeCenterX) > PlaneEnvelopeCenterTolerance ||
            MathF.Abs(plane.AnchorLocalCenter.Z - envelopeCenterZ) > PlaneEnvelopeCenterTolerance)
        {
            return false;
        }

        float normalLength = plane.AnchorLocalNormal.Length;
        return float.IsFinite(normalLength) && normalLength > 0f &&
               MathF.Abs(plane.AnchorLocalNormal.Y / normalLength) <= MaximumVerticalNormalAlignment &&
               MathF.Abs(plane.AnchorLocalNormal.Z / normalLength) >= MinimumNorthNormalAlignment;
    }

    public static bool AreCoincidentPhysicalFaces(
        FishWarehouseAnchorLocalBounds primaryEnvelope,
        FishWarehousePhysicalPanelPlane primaryPlane,
        FishWarehouseAnchorLocalBounds alternateEnvelope,
        FishWarehousePhysicalPanelPlane alternatePlane)
    {
        if (!IsNorthWallPhysicalPlane(primaryEnvelope, primaryPlane) ||
            !IsNorthWallPhysicalPlane(alternateEnvelope, alternatePlane))
        {
            return false;
        }

        float primaryLength = primaryPlane.AnchorLocalNormal.Length;
        float alternateLength = alternatePlane.AnchorLocalNormal.Length;
        float alignment = (primaryPlane.AnchorLocalNormal.X * alternatePlane.AnchorLocalNormal.X +
                           primaryPlane.AnchorLocalNormal.Y * alternatePlane.AnchorLocalNormal.Y +
                           primaryPlane.AnchorLocalNormal.Z * alternatePlane.AnchorLocalNormal.Z) /
                          (primaryLength * alternateLength);
        if (!float.IsFinite(alignment) || MathF.Abs(alignment) < MinimumAlternateNormalParallelism)
            return false;

        FishWarehouseGarageVector offset = new(
            alternatePlane.AnchorLocalCenter.X - primaryPlane.AnchorLocalCenter.X,
            alternatePlane.AnchorLocalCenter.Y - primaryPlane.AnchorLocalCenter.Y,
            alternatePlane.AnchorLocalCenter.Z - primaryPlane.AnchorLocalCenter.Z);
        float planeSeparation = MathF.Abs(
            (offset.X * primaryPlane.AnchorLocalNormal.X +
             offset.Y * primaryPlane.AnchorLocalNormal.Y +
             offset.Z * primaryPlane.AnchorLocalNormal.Z) / primaryLength);
        return float.IsFinite(planeSeparation) &&
               planeSeparation <= AlternatePlaneCenterTolerance &&
               alternateEnvelope.MinimumX <= primaryEnvelope.MinimumX + AlternateApertureEdgeTolerance &&
               alternateEnvelope.MaximumX >= primaryEnvelope.MaximumX - AlternateApertureEdgeTolerance &&
               alternateEnvelope.MinimumY <= primaryEnvelope.MinimumY + AlternateApertureEdgeTolerance &&
               alternateEnvelope.MaximumY >= primaryEnvelope.MaximumY - AlternateApertureEdgeTolerance;
    }

    private static FishWarehouseAnchorLocalBounds InvalidBounds() =>
        new(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN);
}

/// <summary>
/// Pedestrian clearance begins just above FloorY. This preserves the required
/// floor support while still treating every collider from FloorY + epsilon
/// through 0.85m as a blocking sill.
/// </summary>
public static class FishWarehouseGarageOpeningClearanceDefinition
{
    public const float PedestrianVolumeFloorEpsilon = 0.01f;
    private const float BlockingSillTop = 0.85f;

    public static bool HasContinuousFloorSupport(
        IEnumerable<IReadOnlyList<float>>? rayHitLocalYs,
        float floorYTolerance)
    {
        if (rayHitLocalYs is null || !float.IsFinite(floorYTolerance) || floorYTolerance < 0f)
            return false;

        IReadOnlyList<float>[] samples = rayHitLocalYs.ToArray();
        return samples.Length == 9 && samples.All(sample => IsFloorSupportedAt(sample, floorYTolerance));
    }

    public static bool IsFloorSupportedAt(
        IEnumerable<float>? hitLocalYs,
        float floorYTolerance) =>
        hitLocalYs is not null &&
        float.IsFinite(floorYTolerance) &&
        floorYTolerance >= 0f &&
        hitLocalYs.Any(hitY =>
            float.IsFinite(hitY) &&
            MathF.Abs(hitY - FishWarehouseInteriorRoomDefinition.FloorY) <= floorYTolerance);

    public static bool TryGetTraversableVolume(
        FishWarehouseTransitionPrism prism,
        out FishWarehouseTransitionPrism traversableVolume)
    {
        traversableVolume = default!;
        if (!AreFinite(prism.MinimumX, prism.MaximumX, prism.MinimumY, prism.MaximumY, prism.MinimumZ, prism.MaximumZ))
            return false;

        float minimumY = MathF.Max(prism.MinimumY, FishWarehouseInteriorRoomDefinition.FloorY + PedestrianVolumeFloorEpsilon);
        if (prism.MinimumX >= prism.MaximumX || minimumY >= prism.MaximumY || prism.MinimumZ >= prism.MaximumZ)
            return false;

        traversableVolume = new FishWarehouseTransitionPrism(
            prism.MinimumX,
            prism.MaximumX,
            minimumY,
            prism.MaximumY,
            prism.MinimumZ,
            prism.MaximumZ);
        return true;
    }

    public static bool IsTraversableVolumeClear(
        FishWarehouseTransitionPrism prism,
        IEnumerable<FishWarehouseAnchorLocalBounds>? colliders)
    {
        if (colliders is null || !TryGetTraversableVolume(prism, out FishWarehouseTransitionPrism volume))
            return false;

        FishWarehouseAnchorLocalBounds volumeBounds = ToBounds(volume);
        return colliders.All(collider => collider.IsValid && !collider.Intersects(volumeBounds));
    }

    public static bool IsFloorBandClear(
        FishWarehouseTransitionPrism prism,
        IEnumerable<FishWarehouseAnchorLocalBounds>? colliders)
    {
        if (colliders is null || !TryGetTraversableVolume(prism, out FishWarehouseTransitionPrism volume))
            return false;

        float maximumY = MathF.Min(volume.MaximumY, BlockingSillTop);
        if (volume.MinimumY >= maximumY)
            return false;

        var floorBand = new FishWarehouseAnchorLocalBounds(
            volume.MinimumX,
            volume.MaximumX,
            volume.MinimumY,
            maximumY,
            volume.MinimumZ,
            volume.MaximumZ);
        return colliders.All(collider => collider.IsValid && !collider.Intersects(floorBand));
    }

    private static FishWarehouseAnchorLocalBounds ToBounds(FishWarehouseTransitionPrism prism) =>
        new(prism.MinimumX, prism.MaximumX, prism.MinimumY, prism.MaximumY, prism.MinimumZ, prism.MaximumZ);

    private static bool AreFinite(params float[] values) => values.All(float.IsFinite);
}

public sealed record FishWarehouseGarageSceneNode(
    string HierarchyPath,
    string Name,
    string? ParentPath);

public sealed record FishWarehouseGarageSceneSelection(
    string PrimaryRootPath,
    string PrimaryPanelPath,
    string AlternateRootPath);

public static class FishWarehouseGarageSceneSelectionDefinition
{
    public static bool TrySelect(
        IEnumerable<FishWarehouseGarageSceneNode>? sceneNodes,
        out FishWarehouseGarageSceneSelection? selection)
    {
        selection = null;
        if (sceneNodes is null)
            return false;

        FishWarehouseGarageSceneNode[] nodes = sceneNodes.ToArray();
        FishWarehouseGarageSceneNode[] primaryRoots = nodes
            .Where(node => string.Equals(
                node.HierarchyPath,
                FishWarehouseGarageOpeningDefinition.ApprovedGarageDoorRootPath,
                StringComparison.Ordinal))
            .ToArray();
        FishWarehouseGarageSceneNode[] alternateRoots = nodes
            .Where(node => string.Equals(
                node.HierarchyPath,
                FishWarehouseGarageOpeningDefinition.ApprovedAlternateRootPath,
                StringComparison.Ordinal))
            .ToArray();
        if (primaryRoots.Length != 1 || alternateRoots.Length != 1 ||
            !string.Equals(primaryRoots[0].Name, "GarageDoor (1)", StringComparison.Ordinal) ||
            !string.Equals(alternateRoots[0].Name, "GarageDoor_Other", StringComparison.Ordinal))
        {
            return false;
        }

        FishWarehouseGarageSceneNode[] panels = nodes
            .Where(node => string.Equals(node.ParentPath, primaryRoots[0].HierarchyPath, StringComparison.Ordinal) &&
                           string.Equals(node.Name, primaryRoots[0].Name, StringComparison.Ordinal))
            .ToArray();
        if (panels.Length != 1)
            return false;

        selection = new FishWarehouseGarageSceneSelection(
            primaryRoots[0].HierarchyPath,
            panels[0].HierarchyPath,
            alternateRoots[0].HierarchyPath);
        return true;
    }
}

public enum FishWarehouseGarageTargetComponentKind
{
    Renderer,
    Collider
}

public sealed record FishWarehouseGarageTargetComponent(
    string NodePath,
    string Identity,
    FishWarehouseGarageTargetComponentKind Kind,
    bool Enabled);

public sealed record FishWarehouseGarageTargetSet(
    IReadOnlyList<FishWarehouseGarageTargetComponent> Renderers,
    IReadOnlyList<FishWarehouseGarageTargetComponent> Colliders)
{
    public IReadOnlyList<FishWarehouseGarageTargetComponent> All =>
        Renderers.Concat(Colliders).ToArray();
}

public static class FishWarehouseGarageTargetSetDefinition
{
    public static bool TryCreate(
        FishWarehouseGarageSceneSelection? selection,
        IEnumerable<FishWarehouseGarageTargetComponent>? components,
        out FishWarehouseGarageTargetSet? targetSet)
    {
        targetSet = null;
        if (selection is null || components is null)
            return false;

        FishWarehouseGarageTargetComponent[] directComponents = components
            .Where(component => IsApprovedNode(component.NodePath, selection))
            .ToArray();
        if (directComponents.Any(component => string.IsNullOrWhiteSpace(component.Identity)) ||
            directComponents.Select(component => component.Identity).Distinct(StringComparer.Ordinal).Count() != directComponents.Length)
        {
            return false;
        }

        FishWarehouseGarageTargetComponent[] primaryRootRenderers = Find(
            directComponents,
            selection.PrimaryRootPath,
            FishWarehouseGarageTargetComponentKind.Renderer);
        FishWarehouseGarageTargetComponent[] primaryRootColliders = Find(
            directComponents,
            selection.PrimaryRootPath,
            FishWarehouseGarageTargetComponentKind.Collider);
        FishWarehouseGarageTargetComponent[] primaryPanelRenderers = Find(
            directComponents,
            selection.PrimaryPanelPath,
            FishWarehouseGarageTargetComponentKind.Renderer);
        FishWarehouseGarageTargetComponent[] primaryPanelColliders = Find(
            directComponents,
            selection.PrimaryPanelPath,
            FishWarehouseGarageTargetComponentKind.Collider);
        FishWarehouseGarageTargetComponent[] alternateRenderers = Find(
            directComponents,
            selection.AlternateRootPath,
            FishWarehouseGarageTargetComponentKind.Renderer);
        FishWarehouseGarageTargetComponent[] alternateColliders = Find(
            directComponents,
            selection.AlternateRootPath,
            FishWarehouseGarageTargetComponentKind.Collider);

        if (primaryRootRenderers.Length != 1 ||
            primaryRootColliders.Length != 1 ||
            primaryPanelRenderers.Length != 1 ||
            primaryPanelColliders.Length != 1 ||
            alternateRenderers.Length != 1 ||
            alternateColliders.Length != 0)
        {
            return false;
        }

        targetSet = new FishWarehouseGarageTargetSet(
            Array.AsReadOnly(primaryRootRenderers.Concat(primaryPanelRenderers).Concat(alternateRenderers).ToArray()),
            Array.AsReadOnly(primaryRootColliders.Concat(primaryPanelColliders).ToArray()));
        return true;
    }

    private static bool IsApprovedNode(string nodePath, FishWarehouseGarageSceneSelection selection) =>
        string.Equals(nodePath, selection.PrimaryRootPath, StringComparison.Ordinal) ||
        string.Equals(nodePath, selection.PrimaryPanelPath, StringComparison.Ordinal) ||
        string.Equals(nodePath, selection.AlternateRootPath, StringComparison.Ordinal);

    private static FishWarehouseGarageTargetComponent[] Find(
        IEnumerable<FishWarehouseGarageTargetComponent> components,
        string nodePath,
        FishWarehouseGarageTargetComponentKind kind) =>
        components.Where(component =>
            string.Equals(component.NodePath, nodePath, StringComparison.Ordinal) &&
            component.Kind == kind).ToArray();
}

public interface IFishWarehouseGarageTargetState
{
    bool TryGetEnabled(FishWarehouseGarageTargetComponent component, out bool enabled);

    bool TrySetEnabled(FishWarehouseGarageTargetComponent component, bool enabled);
}

/// <summary>
/// Owns only the state of the approved target set. A false preflight decision
/// is deliberately a no-op; a failed post-open validation restores the exact
/// snapshot captured immediately before any component is changed.
/// </summary>
public sealed class FishWarehouseGarageTargetMutationState
{
    private readonly FishWarehouseGarageTargetSet _targets;
    private IReadOnlyList<FishWarehouseGarageTargetComponent>? _snapshot;
    private bool _opened;

    public FishWarehouseGarageTargetMutationState(FishWarehouseGarageTargetSet targets)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
    }

    public bool TryOpen(
        bool preflightPassed,
        IFishWarehouseGarageTargetState state,
        Func<bool> postOpenPrismIsClear)
    {
        if (!preflightPassed || state is null || postOpenPrismIsClear is null)
            return false;
        if (_opened)
            return true;
        if (!TryCapture(state))
            return false;

        foreach (FishWarehouseGarageTargetComponent component in _targets.All)
        {
            if (state.TrySetEnabled(component, false))
                continue;

            _ = Restore(state);
            return false;
        }

        if (!postOpenPrismIsClear())
        {
            _ = Restore(state);
            return false;
        }

        _opened = true;
        return true;
    }

    public bool Restore(IFishWarehouseGarageTargetState state)
    {
        if (state is null)
            return false;
        if (_snapshot is null)
        {
            _opened = false;
            return true;
        }

        bool restored = true;
        foreach (FishWarehouseGarageTargetComponent component in _snapshot)
            restored &= state.TrySetEnabled(component, component.Enabled);

        if (restored)
            _snapshot = null;
        _opened = false;
        return restored;
    }

    private bool TryCapture(IFishWarehouseGarageTargetState state)
    {
        var captured = new List<FishWarehouseGarageTargetComponent>();
        foreach (FishWarehouseGarageTargetComponent component in _targets.All)
        {
            if (!state.TryGetEnabled(component, out bool enabled))
                return false;

            captured.Add(component with { Enabled = enabled });
        }

        _snapshot = captured.AsReadOnly();
        return true;
    }
}
