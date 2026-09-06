namespace OrganizedCrime.Model;

public static class SyndicateHqContract
{
    public const string RootName = "OC_SyndicateHQ_Interior";
    public const string EntryMarker = "HQ_ENTRY";
    public const string ExitMarker = "HQ_EXIT";
    public const string StorageMarker = "HQ_STORAGE";
    public const string EnterAction = "Enter Syndicate HQ";
    public const string ExitAction = "Exit Syndicate HQ";
    public const string NightclubShellPath = "Map/Hyland Point/Region_Northtown/Nightclub/desert town hall";
    public const string NightclubDoorPath = NightclubShellPath + "/Facade (1)/Metal Glass Door (Static)";
    public const string NightclubBuildingPath = "Map/Hyland Point/Region_Northtown/Nightclub";
    public const string NightclubBuildingGuid = "6121cc6b-ccc6-4c9e-b4cb-2d2db634f671";
    public const int NightclubDoorIndex = 0;
    public const float PositionTolerance = 0.1f;
    public const float ForwardDotTolerance = 0.999f;
}

public readonly record struct SyndicateHqVector3(float X, float Y, float Z)
{
    public bool IsFinite =>
        !float.IsNaN(X) && !float.IsInfinity(X) &&
        !float.IsNaN(Y) && !float.IsInfinity(Y) &&
        !float.IsNaN(Z) && !float.IsInfinity(Z);

    public float LengthSquared => X * X + Y * Y + Z * Z;

    public bool IsNonZero => IsFinite && LengthSquared > 0.000001f;

    public SyndicateHqVector3 Normalized()
    {
        if (!IsNonZero) return Zero;
        var length = MathF.Sqrt(LengthSquared);
        return new(X / length, Y / length, Z / length);
    }

    public float NormalizedDot(SyndicateHqVector3 other) =>
        Normalized().X * other.Normalized().X +
        Normalized().Y * other.Normalized().Y +
        Normalized().Z * other.Normalized().Z;

    public float DistanceTo(SyndicateHqVector3 other)
    {
        var x = X - other.X;
        var y = Y - other.Y;
        var z = Z - other.Z;
        return MathF.Sqrt(x * x + y * y + z * z);
    }

    public static SyndicateHqVector3 operator +(SyndicateHqVector3 left, SyndicateHqVector3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static SyndicateHqVector3 operator *(SyndicateHqVector3 value, float scalar) =>
        new(value.X * scalar, value.Y * scalar, value.Z * scalar);

    public static SyndicateHqVector3 Zero => new(0, 0, 0);
}

public readonly record struct SyndicateHqQuaternion(float X, float Y, float Z, float W)
{
    public static SyndicateHqQuaternion Identity => new(0, 0, 0, 1);
}

public sealed record SyndicateHqDoorFingerprint(
    string DoorPath,
    string DoorType,
    string InteractablePath,
    string InteractableType,
    string BuildingPath,
    string BuildingType,
    string BuildingGuid,
    string AccessPointPath,
    int DoorIndex,
    SyndicateHqVector3 Position,
    SyndicateHqVector3 Forward,
    SyndicateHqVector3? AccessPointPosition,
    bool HasIntObj,
    bool HasBuilding,
    bool HasAccessPoint,
    bool HasCanKnock,
    bool HasUsable);

public sealed record SyndicateHqSceneSnapshot(
    IReadOnlyList<string> ShellPaths,
    IReadOnlyList<SyndicateHqDoorFingerprint> Doors);

public sealed record SyndicateHqIdentityCheckResult(bool Accepted, string Reason, SyndicateHqDoorFingerprint? Door)
{
    public static SyndicateHqIdentityCheckResult Reject(string reason) => new(false, reason, null);
    public static SyndicateHqIdentityCheckResult Accept(SyndicateHqDoorFingerprint door) => new(true, "Exact Nightclub shell and door fingerprint matched.", door);
}

public sealed record SyndicateHqExteriorReturnState(
    SyndicateHqVector3? CapturedPosition,
    SyndicateHqVector3? AccessPointPosition,
    Guid SessionEpoch,
    long LoadEpoch,
    string CanonicalPlayerId);

public enum SyndicateHqReturnPath
{
    NoSafeReturn,
    CapturedExteriorPosition,
    DoorAccessPoint
}

public sealed record SyndicateHqReturnPlan(SyndicateHqReturnPath Path, SyndicateHqVector3 Position, string Reason);

public enum SyndicateHqPromptKind
{
    None,
    Locked,
    Enter,
    Exit,
    Fault
}

public sealed record SyndicateHqPrompt(SyndicateHqPromptKind Kind, string Text, string ActionText);
