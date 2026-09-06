using System.Reflection;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-73 lockdown gate. <see cref="Release1LockdownGatePatch"/> reaches into Il2Cpp game types
/// (CurfewManager, TimeManager, Player) that only work inside the running game process, so every test
/// in <see cref="Release1LockdownGatePatchReachabilityTests"/> either reflects on the compiled type's
/// shape or reads its own source text; none of them calls <c>TryEngage</c>, <c>TryRelease</c>, or the
/// Harmony postfix, the same restriction <c>SyndicateHqStorageLoadPatchLoggingTests</c> already
/// documents for its own sibling patch. The actual flag and throttle decisions the patch relies on
/// live in two plain classes with no such restriction, <see cref="Release1LockdownGateState"/> and
/// <see cref="Release1LockdownGateThrottle"/>, exercised directly below.
/// </summary>
public sealed class Release1LockdownGatePatchReachabilityTests
{
    [Fact]
    public void The_patch_class_declares_the_expected_engage_release_reset_and_postfix_members()
    {
        var type = typeof(Release1LockdownGatePatch);

        Assert.NotNull(type.GetMethod("TryEngage", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetMethod("TryRelease", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetMethod("Reset", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetProperty("LockdownActive", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetMethod("OnUncappedMinPassPostfix", BindingFlags.NonPublic | BindingFlags.Static));
        Assert.NotNull(type.GetMethod("EnsurePatched", BindingFlags.NonPublic | BindingFlags.Static));
    }

    [Fact]
    public void LockdownActive_reads_the_private_state_field_without_touching_any_game_type()
    {
        var stateField = typeof(Release1LockdownGatePatch).GetField("State", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(stateField);
        var state = (Release1LockdownGateState)stateField!.GetValue(null)!;

        try
        {
            Assert.False(Release1LockdownGatePatch.LockdownActive);
            state.Engage(false);
            Assert.True(Release1LockdownGatePatch.LockdownActive);
        }
        finally
        {
            state.Release();
        }

        Assert.False(Release1LockdownGatePatch.LockdownActive);
    }

    [Fact]
    public void Exactly_one_production_site_calls_into_the_lockdown_gate_patch_from_the_world_boundary()
    {
        // OC-73 Task 4. The End key handler no longer reaches Release1LockdownGatePatch directly: it
        // is a second caller of the two world boundary members, exactly as
        // S1ApiRelease1SmallCourtesyWorld.TryEngageLockdown/TryReleaseLockdown are. This replaces the
        // pre-Task-4 pin that required Mod.cs to be the one and only call site.
        var root = FindRepositoryRoot();
        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));

        Assert.Equal(1, Count(mod, "KeyCode.End"));
        Assert.Equal(1, Count(mod, "_release1SmallCourtesyWorld.TryEngageLockdown("));
        Assert.Equal(1, Count(mod, "_release1SmallCourtesyWorld.TryReleaseLockdown("));
        Assert.Equal(1, Count(mod, "Release1LockdownGatePatch.LockdownActive"));
        Assert.Equal(1, Count(mod, "Release1LockdownGatePatch.Reset();"));
        Assert.DoesNotContain("Release1LockdownGatePatch.TryEngage(", mod, StringComparison.Ordinal);
        Assert.DoesNotContain("Release1LockdownGatePatch.TryRelease(", mod, StringComparison.Ordinal);

        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var sites = new List<string>();
        foreach (var file in Directory.EnumerateFiles(runtimeDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (name is "Release1LockdownGatePatch.cs" or "Release1LockdownGateState.cs" or "Release1LockdownGateThrottle.cs")
                continue;
            if (Count(File.ReadAllText(file), "Release1LockdownGatePatch.") > 0) sites.Add(name);
        }
        Assert.Equal(new[] { "S1ApiRelease1SmallCourtesyWorld.cs" }, sites);
    }

    [Fact]
    public void The_end_key_handler_sits_inside_the_owner_qa_keys_gate()
    {
        var root = FindRepositoryRoot();
        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));

        var onUpdateStart = mod.IndexOf("public override void OnUpdate()", StringComparison.Ordinal);
        var onUpdateEnd = mod.IndexOf("public override void OnGUI()", onUpdateStart, StringComparison.Ordinal);
        Assert.True(onUpdateStart >= 0 && onUpdateEnd > onUpdateStart, "OnUpdate method body was not found.");
        var body = mod.Substring(onUpdateStart, onUpdateEnd - onUpdateStart);

        var gateIndex = body.IndexOf("if (_ownerQaKeysEnabled)", StringComparison.Ordinal);
        var endHandlerIndex = body.IndexOf("KeyCode.End", StringComparison.Ordinal);
        Assert.True(gateIndex >= 0, "owner QA keys gate was not found in OnUpdate.");
        Assert.True(endHandlerIndex > gateIndex, "the End key handler must sit inside the owner QA keys gate.");
    }

    [Fact]
    public void The_patch_is_applied_lazily_from_try_engage_and_never_from_on_initialize_melon()
    {
        var root = FindRepositoryRoot();
        var patchText = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1LockdownGatePatch.cs"));

        Assert.Equal(1, Count(patchText, "EnsurePatched(harmony)"));
        var tryEngageStart = patchText.IndexOf("public static Release1StagingHarnessResult TryEngage(", StringComparison.Ordinal);
        var ensurePatchedCallIndex = patchText.IndexOf("EnsurePatched(harmony)", StringComparison.Ordinal);
        Assert.True(tryEngageStart >= 0 && ensurePatchedCallIndex > tryEngageStart,
            "EnsurePatched must be called from inside TryEngage, not at load time.");
        Assert.Equal(1, Count(patchText, "harmony.Patch("));

        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));
        var initStart = mod.IndexOf("public override void OnInitializeMelon()", StringComparison.Ordinal);
        var initEnd = mod.IndexOf("public override void OnDeinitializeMelon()", initStart, StringComparison.Ordinal);
        Assert.True(initStart >= 0 && initEnd > initStart, "OnInitializeMelon method body was not found.");
        var initBody = mod.Substring(initStart, initEnd - initStart);
        Assert.DoesNotContain("Release1LockdownGatePatch", initBody, StringComparison.Ordinal);
    }

    [Fact]
    public void The_postfix_returns_immediately_when_the_lockdown_is_not_active()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1LockdownGatePatch.cs"));

        var postfixStart = text.IndexOf("private static void OnUncappedMinPassPostfix(", StringComparison.Ordinal);
        Assert.True(postfixStart >= 0, "OnUncappedMinPassPostfix was not found.");
        var guardIndex = text.IndexOf("if (!State.LockdownActive", postfixStart, StringComparison.Ordinal);
        Assert.True(guardIndex >= 0 && guardIndex - postfixStart < 120,
            "the inertness guard must be at or near the top of the postfix.");
    }

    [Fact]
    public void Mod_has_exactly_one_insert_key_handler_calling_the_npc_registry_dump()
    {
        var root = FindRepositoryRoot();
        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));

        Assert.Equal(1, Count(mod, "KeyCode.Insert"));
        Assert.Equal(1, Count(mod, "Release1NpcRegistryDumpHarness.TryDump()"));
    }

    [Fact]
    public void The_npc_registry_dump_harness_is_read_only_and_uses_the_native_registry_not_s1api()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1NpcRegistryDumpHarness.cs"));

        Assert.Contains("NPCManager.NPCRegistry", text, StringComparison.Ordinal);
        Assert.DoesNotContain("using S1API", text, StringComparison.Ordinal);
        foreach (var banned in new[] { "SetGUID", ".Enable(", ".Disable(", "HarmonyPatch", "GUI.", "MelonPreferences" })
            Assert.DoesNotContain(banned, text, StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}

/// <summary>
/// OC-73 lockdown gate pure state logic, exercised directly with no Unity, S1API, Il2Cpp, or
/// MelonLoader involvement at all.
/// </summary>
public sealed class Release1LockdownGateStateTests
{
    [Fact]
    public void Engage_sets_active_and_records_whether_curfew_was_enabled_by_this_call()
    {
        var state = new Release1LockdownGateState();

        Assert.False(state.LockdownActive);
        Assert.False(state.CurfewEnabledByGate);

        state.Engage(true);

        Assert.True(state.LockdownActive);
        Assert.True(state.CurfewEnabledByGate);
    }

    [Fact]
    public void Engage_when_curfew_was_already_enabled_records_that_this_call_did_not_enable_it()
    {
        var state = new Release1LockdownGateState();

        state.Engage(false);

        Assert.True(state.LockdownActive);
        Assert.False(state.CurfewEnabledByGate);
    }

    [Fact]
    public void A_second_engage_call_while_already_active_is_a_no_op()
    {
        var state = new Release1LockdownGateState();

        state.Engage(true);
        state.Engage(false);

        Assert.True(state.LockdownActive);
        Assert.True(state.CurfewEnabledByGate);
    }

    [Fact]
    public void Release_clears_active_and_returns_whether_curfew_should_be_disabled()
    {
        var state = new Release1LockdownGateState();
        state.Engage(true);

        var shouldDisableCurfew = state.Release();

        Assert.True(shouldDisableCurfew);
        Assert.False(state.LockdownActive);
        Assert.False(state.CurfewEnabledByGate);
    }

    [Fact]
    public void Release_when_curfew_was_already_enabled_reports_no_disable_needed()
    {
        var state = new Release1LockdownGateState();
        state.Engage(false);

        Assert.False(state.Release());
        Assert.False(state.LockdownActive);
    }

    [Fact]
    public void Release_while_not_active_is_a_no_op_and_returns_false()
    {
        var state = new Release1LockdownGateState();

        Assert.False(state.Release());
        Assert.False(state.LockdownActive);
        Assert.False(state.CurfewEnabledByGate);
    }
}

/// <summary>
/// OC-73 lockdown gate re-assert log throttle, exercised directly with no Unity, S1API, Il2Cpp, or
/// MelonLoader involvement at all.
/// </summary>
public sealed class Release1LockdownGateThrottleTests
{
    [Fact]
    public void TryMark_returns_true_the_first_time_a_minute_key_is_seen()
    {
        var throttle = new Release1LockdownGateThrottle();

        Assert.True(throttle.TryMark(2100));
    }

    [Fact]
    public void TryMark_returns_false_on_repeats_of_the_same_minute_key()
    {
        var throttle = new Release1LockdownGateThrottle();

        Assert.True(throttle.TryMark(2100));
        Assert.False(throttle.TryMark(2100));
        Assert.False(throttle.TryMark(2100));
    }

    [Fact]
    public void TryMark_returns_true_again_once_the_minute_key_changes()
    {
        var throttle = new Release1LockdownGateThrottle();

        Assert.True(throttle.TryMark(2100));
        Assert.True(throttle.TryMark(2101));
        Assert.False(throttle.TryMark(2101));
    }

    [Fact]
    public void Reset_allows_the_same_minute_key_to_log_again()
    {
        var throttle = new Release1LockdownGateThrottle();
        Assert.True(throttle.TryMark(2100));

        throttle.Reset();

        Assert.True(throttle.TryMark(2100));
    }
}
