using System.Text.RegularExpressions;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// Refactor rule: Model, Persistence and Core stay engine-free and never depend on Runtime.
/// Engine-bound helpers live in Runtime/Platform. Comment lines are ignored.
/// </summary>
public sealed class ArchitectureBoundaryTests
{
    private static readonly string[] PureFolders = { "Model", "Persistence", "Core" };

    internal static readonly Regex Forbidden = new(
        @"^\s*using\s+(static\s+)?(OrganizedCrime\.Runtime|UnityEngine|S1API|Il2Cpp\w*|MelonLoader|HarmonyLib)\b" +
        @"|\b(OrganizedCrime\.Runtime|UnityEngine|Il2CppScheduleOne|Il2CppInterop|Il2CppFishNet|S1API|MelonLoader|HarmonyLib)\.",
        RegexOptions.CultureInvariant);

    [Theory]
    [InlineData("using OrganizedCrime.Runtime;")]
    [InlineData("using UnityEngine;")]
    [InlineData("using Il2CppScheduleOne.Police;")]
    [InlineData("    var x = OrganizedCrime.Runtime.Foo.Bar;")]
    [InlineData("    UnityEngine.Object.Destroy(go);")]
    public void Forbidden_pattern_catches_engine_and_runtime_references(string line) =>
        Assert.Matches(Forbidden, line);

    // String literals are data, not references: Model compares native type NAMES
    // (FishWarehouseInteriorShellDefinition, SyndicateHqIdentity). Those name-string
    // dependencies are covered by Task 11's native inventory, not by this boundary.
    private static readonly Regex StringLiteral = new(@"@""(?:[^""]|"""")*""|""(?:[^""\\]|\\.)*""", RegexOptions.CultureInvariant);

    internal static bool Violates(string line) => Forbidden.IsMatch(StringLiteral.Replace(line, "\"\""));

    [Theory]
    [InlineData("using OrganizedCrime.Model;")]
    [InlineData("using System.Text.Json;")]
    [InlineData("namespace OrganizedCrime.Model;")]
    [InlineData("        hasMesh && string.Equals(componentTypeName, \"UnityEngine.Renderer\", StringComparison.Ordinal);")]
    [InlineData("        if (!string.Equals(door.DoorType, \"Il2CppScheduleOne.Doors.StaticDoor\", StringComparison.Ordinal))")]
    public void Boundary_allows_pure_references_and_type_names_in_strings(string line) =>
        Assert.False(Violates(line));

    [Fact]
    public void Boundary_still_catches_a_real_reference_next_to_a_string() =>
        Assert.True(Violates("    var name = \"x\"; UnityEngine.Object.Destroy(go);"));

    [Fact]
    public void Pure_folders_do_not_reference_runtime_or_engine_code()
    {
        var root = FindProductionRoot();
        var violations = new List<string>();
        foreach (var folder in PureFolders)
        {
            var path = Path.Combine(root, folder);
            if (!Directory.Exists(path))
                continue;
            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal))
                        continue;
                    if (Violates(lines[i]))
                        violations.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {trimmed}");
                }
            }
        }
        Assert.True(violations.Count == 0,
            "Model, Persistence and Core must not reference Runtime or engine code:\n" + string.Join("\n", violations));
    }

    private static string FindProductionRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "OrganizedCrime");
            if (File.Exists(Path.Combine(candidate, "OrganizedCrime.csproj")))
                return candidate;
        }
        throw new InvalidOperationException("Couldn't find tools/OrganizedCrime from the test output.");
    }
}
