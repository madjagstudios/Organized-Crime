namespace OrganizedCrime.Model;

public readonly record struct FishWarehouseTransformVector(float X, float Y, float Z);

public readonly record struct FishWarehouseTransformSignature(
    FishWarehouseTransformVector Position,
    FishWarehouseTransformVector EulerRotation,
    FishWarehouseTransformVector LossyScale);

public static class FishWarehouseTransformSignatureDefinition
{
    public const float PositionTolerance = 0.01f;
    public const float RotationToleranceDegrees = 0.10f;
    public const float UnitScaleTolerance = 0.001f;

    public static bool IsCoincident(
        FishWarehouseTransformSignature anchor,
        FishWarehouseTransformSignature propertyRoot)
    {
        return ComponentsWithin(anchor.Position, propertyRoot.Position, PositionTolerance) &&
               RotationWithin(anchor.EulerRotation, propertyRoot.EulerRotation, RotationToleranceDegrees) &&
               IsUnitScale(anchor.LossyScale) &&
               IsUnitScale(propertyRoot.LossyScale);
    }

    private static bool ComponentsWithin(
        FishWarehouseTransformVector left,
        FishWarehouseTransformVector right,
        float tolerance) =>
        float.IsFinite(left.X) && float.IsFinite(left.Y) && float.IsFinite(left.Z) &&
        float.IsFinite(right.X) && float.IsFinite(right.Y) && float.IsFinite(right.Z) &&
        MathF.Abs(left.X - right.X) <= tolerance &&
        MathF.Abs(left.Y - right.Y) <= tolerance &&
        MathF.Abs(left.Z - right.Z) <= tolerance;

    private static bool RotationWithin(
        FishWarehouseTransformVector left,
        FishWarehouseTransformVector right,
        float tolerance) =>
        ComponentsWithin(
            new FishWarehouseTransformVector(
                AngleDifference(left.X, right.X),
                AngleDifference(left.Y, right.Y),
                AngleDifference(left.Z, right.Z)),
            new FishWarehouseTransformVector(0f, 0f, 0f),
            tolerance);

    private static bool IsUnitScale(FishWarehouseTransformVector scale) =>
        float.IsFinite(scale.X) && float.IsFinite(scale.Y) && float.IsFinite(scale.Z) &&
        MathF.Abs(scale.X - 1f) <= UnitScaleTolerance &&
        MathF.Abs(scale.Y - 1f) <= UnitScaleTolerance &&
        MathF.Abs(scale.Z - 1f) <= UnitScaleTolerance;

    private static float AngleDifference(float left, float right)
    {
        float difference = (left - right) % 360f;
        if (difference > 180f)
            difference -= 360f;
        else if (difference < -180f)
            difference += 360f;

        return difference;
    }
}
