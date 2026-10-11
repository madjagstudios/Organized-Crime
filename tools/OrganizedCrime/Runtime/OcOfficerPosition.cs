namespace OrganizedCrime.Runtime;

/// <summary>
/// A world position plus the yaw an OC officer spawns facing, in degrees.
/// Pure so the roster model and its tests never touch UnityEngine.
/// </summary>
public readonly record struct OcOfficerPosition(float X, float Y, float Z, float YawDegrees)
{
    public OcOfficerPosition(float x, float y, float z)
        : this(x, y, z, 0f)
    {
    }

    public bool IsFinite =>
        float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) && float.IsFinite(YawDegrees);

    public double DistanceTo(OcOfficerPosition other)
    {
        var dx = (double)X - other.X;
        var dy = (double)Y - other.Y;
        var dz = (double)Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
