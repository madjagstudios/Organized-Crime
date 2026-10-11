namespace OrganizedCrime.Runtime.Platform;

/// <summary>
/// The shared Unity destroyed-object check, byte-for-byte equivalent to the
/// copies it replaces. Both <c>IsUnityNull</c> overloads stay: Unity-typed
/// call sites must keep resolving to the overloaded null check, not to
/// reference equality (the object overload exists for non-Unity arguments).
/// </summary>
public static class UnityLife
{
    public static bool IsAlive(UnityEngine.Object? value)
    {
        try
        {
            return value is not null && value != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsUnityNull(object? value) => value is null || value == null;

    public static bool IsUnityNull(UnityEngine.Object? value) => value == null;
}
