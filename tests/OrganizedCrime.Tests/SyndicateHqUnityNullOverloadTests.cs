using Xunit;

namespace OrganizedCrime.Tests;

// SyndicateHqInteriorRuntime, SyndicateHqDoorObserver, and SyndicateHqNativeStorageBoundary each
// declare a private IsUnityNull(object?) helper that several call sites use with GameObject,
// Transform, Component, Material, and Collider arguments. Because the parameter type is object,
// "value == null" there was reference equality, not UnityEngine.Object's overloaded null check, so
// a destroyed-but-still-referenced native object never read as null. That let SyndicateHqInteriorRuntime
// get stuck reporting "waiting for deferred root destruction" forever after the first save of a
// session, killing the HQ door and closets until a process restart (found live 2026-09-03).
//
// SyndicateHqInteriorRuntime cannot be exercised behaviorally in this test project: it directly
// constructs real UnityEngine.GameObject instances and calls Resources.FindObjectsOfTypeAll, neither
// of which is available outside a running Unity/IL2Cpp process. This source-scan test is the
// regression guard: it asserts each of the three files still declares the UnityEngine.Object overload
// of IsUnityNull, so overload resolution keeps routing Unity-object call sites to the real null check
// instead of silently falling back to reference equality.
public sealed class SyndicateHqUnityNullOverloadTests
{
    private const string UnityObjectOverload = "private static bool IsUnityNull(UnityEngine.Object? value) => value == null;";

    [Theory]
    [InlineData("SyndicateHqInteriorRuntime.cs")]
    [InlineData("SyndicateHqDoorObserver.cs")]
    [InlineData("SyndicateHqNativeStorageBoundary.cs")]
    public void File_declares_the_unity_object_overload_of_is_unity_null(string fileName)
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", fileName));

        Assert.Contains(UnityObjectOverload, text, StringComparison.Ordinal);
        // The object overload must remain too: several call sites (for example
        // IsUnityNull(parentProperty?.Grids) in SyndicateHqNativeStorageBoundary.cs) pass non-Unity
        // arguments and must keep resolving to it.
        Assert.Contains("private static bool IsUnityNull(object? value) => value is null || value == null;", text, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
