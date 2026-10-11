namespace OrganizedCrime.Model;

/// <summary>
/// One OC-built warehouse light bar. Coordinates are local to the runtime
/// Property root; the bar's long axis runs along Property-local X at yaw 0
/// and along Property-local Z at yaw 90.
/// </summary>
public sealed record FishWarehouseLightingLamp(
    float X,
    float Z,
    float HeightY,
    int Yaw,
    float Intensity,
    float Range);

/// <summary>
/// Data-only warehouse lighting layout: the baked lamp list plus the shared
/// fixture dimensions and owner-tuning bounds. The runtime session mutates
/// its own copy; edits are never saved.
/// </summary>
public sealed class FishWarehouseLightingDefinition
{
    public const float BarLength = 2.0f;
    public const float BarWidth = 0.5f;
    public const float DefaultHeight = 4.5f;
    public const float DefaultIntensity = 2f;
    public const float DefaultRange = 7f;
    public const float MinTuningHeight = 3.0f;
    public const float HeightStep = 0.25f;
    public const float SnapStep = 0.25f;
    public const float MinIntensity = 0.25f;
    public const float MaxIntensity = 6f;
    public const float IntensityStep = 0.25f;
    public static float MaxTuningHeight => FishWarehouseInteriorRoomDefinition.CeilingY - 0.3f;

    private const float BoundaryEpsilon = 1e-4f;

    public static FishWarehouseLightingDefinition Default { get; } = new(new[]
    {
        new FishWarehouseLightingLamp(-5.75f, -3.45f, DefaultHeight, 0, DefaultIntensity, DefaultRange),
        new FishWarehouseLightingLamp(-5.75f, 3.45f, DefaultHeight, 0, DefaultIntensity, DefaultRange),
        new FishWarehouseLightingLamp(5.75f, -3.45f, DefaultHeight, 0, DefaultIntensity, DefaultRange),
        new FishWarehouseLightingLamp(5.75f, 3.45f, DefaultHeight, 0, DefaultIntensity, DefaultRange),
    });

    private FishWarehouseLightingDefinition(IReadOnlyList<FishWarehouseLightingLamp> lamps)
    {
        Lamps = lamps;
    }

    public IReadOnlyList<FishWarehouseLightingLamp> Lamps { get; }

    public static bool TryCreate(
        IEnumerable<FishWarehouseLightingLamp> lamps,
        out FishWarehouseLightingDefinition? definition)
    {
        definition = null;
        if (lamps is null)
            return false;

        List<FishWarehouseLightingLamp> list = lamps.ToList();
        if (list.Count == 0)
            return false;

        foreach (FishWarehouseLightingLamp lamp in list)
        {
            if (!IsValidLamp(lamp))
                return false;
        }

        for (var i = 0; i < list.Count; i++)
        {
            for (var j = i + 1; j < list.Count; j++)
            {
                if (Overlaps(list[i], list[j]))
                    return false;
            }
        }

        definition = new FishWarehouseLightingDefinition(list.AsReadOnly());
        return true;
    }

    private static bool IsValidLamp(FishWarehouseLightingLamp lamp)
    {
        if (!float.IsFinite(lamp.X) ||
            !float.IsFinite(lamp.Z) ||
            !float.IsFinite(lamp.HeightY) ||
            !float.IsFinite(lamp.Intensity) ||
            !float.IsFinite(lamp.Range))
        {
            return false;
        }

        if (lamp.Yaw != 0 && lamp.Yaw != 90)
            return false;

        if (lamp.Intensity <= 0f || lamp.Range <= 0f)
            return false;

        if (lamp.HeightY >= MaxTuningHeight)
            return false;

        GetHalfExtents(lamp.Yaw, out float halfX, out float halfZ);
        return lamp.X - halfX >= -FishWarehouseInteriorRoomDefinition.HalfWidth - BoundaryEpsilon &&
               lamp.X + halfX <= FishWarehouseInteriorRoomDefinition.HalfWidth + BoundaryEpsilon &&
               lamp.Z - halfZ >= -FishWarehouseInteriorRoomDefinition.HalfDepth - BoundaryEpsilon &&
               lamp.Z + halfZ <= FishWarehouseInteriorRoomDefinition.HalfDepth + BoundaryEpsilon;
    }

    private static bool Overlaps(FishWarehouseLightingLamp left, FishWarehouseLightingLamp right)
    {
        GetHalfExtents(left.Yaw, out float leftHalfX, out float leftHalfZ);
        GetHalfExtents(right.Yaw, out float rightHalfX, out float rightHalfZ);
        return MathF.Abs(left.X - right.X) < leftHalfX + rightHalfX &&
               MathF.Abs(left.Z - right.Z) < leftHalfZ + rightHalfZ;
    }

    private static void GetHalfExtents(int yaw, out float halfX, out float halfZ)
    {
        if (yaw == 90)
        {
            halfX = BarWidth / 2f;
            halfZ = BarLength / 2f;
            return;
        }

        halfX = BarLength / 2f;
        halfZ = BarWidth / 2f;
    }
}
