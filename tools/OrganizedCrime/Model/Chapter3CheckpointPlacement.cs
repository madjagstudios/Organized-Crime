namespace OrganizedCrime.Model;

/// <summary>
/// Where the checkpoint root must sit so the barrier midpoint lands on the
/// spot, plus how far the root moves. The adapter measures the halves after
/// rotation; this math never touches the engine.
/// </summary>
public sealed record OcBarrierCorrection(float CorrectedX, float CorrectedZ, float LateralMetres);

/// <summary>
/// Barrier alignment for OC checkpoint clones. The owner stands mid-road but
/// the RoadCheckpoint transform sits off-centre, so the midpoint of the two
/// barrier halves is lined up with the spot instead of the transform.
/// </summary>
public static class Chapter3CheckpointPlacement
{
    public static OcBarrierCorrection CorrectBarrierMidpoint(
        float spotX, float spotZ,
        float rootX, float rootZ,
        float half1X, float half1Z,
        float half2X, float half2Z)
    {
        var inputs = new[] { spotX, spotZ, rootX, rootZ, half1X, half1Z, half2X, half2Z };
        foreach (var input in inputs)
        {
            if (float.IsNaN(input) || float.IsInfinity(input))
                return new OcBarrierCorrection(rootX, rootZ, 0f);
        }
        var midX = (half1X + half2X) / 2f;
        var midZ = (half1Z + half2Z) / 2f;
        var correctedX = spotX + (rootX - midX);
        var correctedZ = spotZ + (rootZ - midZ);
        var shiftX = correctedX - rootX;
        var shiftZ = correctedZ - rootZ;
        return new OcBarrierCorrection(correctedX, correctedZ, MathF.Sqrt(shiftX * shiftX + shiftZ * shiftZ));
    }
}
