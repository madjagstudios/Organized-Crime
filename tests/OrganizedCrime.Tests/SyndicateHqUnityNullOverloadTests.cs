using Xunit;

namespace OrganizedCrime.Tests;

// SyndicateHqInteriorRuntime, SyndicateHqDoorObserver, and SyndicateHqNativeStorageBoundary each
// used to declare a private IsUnityNull(object?) helper that several call sites used with GameObject,
// Transform, Component, Material, and Collider arguments. Because the parameter type is object,
// "value == null" there was reference equality, not UnityEngine.Object's overloaded null check, so
// a destroyed-but-still-referenced native object never read as null. That let SyndicateHqInteriorRuntime
// get stuck reporting "waiting for deferred root destruction" forever after the first save of a
// session, killing the HQ door and closets until a process restart (found live 2026-09-03).
// OC-147 centralized both overloads in Runtime/Platform/UnityLife.cs, where every former call
// site now resolves.
//
// SyndicateHqInteriorRuntime cannot be exercised behaviorally in this test project: it directly
// constructs real UnityEngine.GameObject instances and calls Resources.FindObjectsOfTypeAll, neither
// of which is available outside a running Unity/IL2Cpp process. This source-scan test is the
// regression guard: it asserts UnityLife still declares the UnityEngine.Object overload of
// IsUnityNull alongside the object overload, and that each of the three files routes through
// UnityLife with no remaining private copy, so overload resolution keeps routing Unity-object
// call sites to the real null check instead of silently falling back to reference equality.
public sealed class SyndicateHqUnityNullOverloadTests
{
    private const string UnityObjectOverload = "public static bool IsUnityNull(UnityEngine.Object? value) => value == null;";
    private const string PlainObjectOverload = "public static bool IsUnityNull(object? value) => value is null || value == null;";

    [Fact]
    public void UnityLife_declares_both_is_unity_null_overloads()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Platform", "UnityLife.cs"));

        Assert.Contains(UnityObjectOverload, text, StringComparison.Ordinal);
        // The object overload must remain too: several call sites (for example
        // IsUnityNull(parentProperty?.Grids) in SyndicateHqNativeStorageBoundary.cs) pass non-Unity
        // arguments and must keep resolving to it.
        Assert.Contains(PlainObjectOverload, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SyndicateHqInteriorRuntime.cs")]
    [InlineData("SyndicateHqDoorObserver.cs")]
    [InlineData("SyndicateHqNativeStorageBoundary.cs")]
    public void File_routes_its_unity_null_checks_through_UnityLife(string fileName)
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", fileName));

        Assert.Contains("UnityLife.IsUnityNull(", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool IsUnityNull(", text, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
