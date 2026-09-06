using HarmonyLib;
using Il2CppFishNet.Connection;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Law;
using Il2CppScheduleOne.PlayerScripts;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-73 lockdown gate. Owner QA harness, gate key End, OwnerQaKeys only. Forces the native curfew
/// manager to hold curfew active at any hour, so police treat the player as a curfew violator around
/// the clock, the same genuine arrest on sight path a real curfew violation uses (see
/// docs/research/2026-09-05-curfew-seam-notes.md). Built against
/// <see cref="Il2CppScheduleOne.Law.CurfewManager"/>, whose IsCurrentlyActive and IsHardCurfewActive
/// setters are in fact public C# wrappers (the Protected text in the IL2CPP metadata pointer field
/// names describes the original native accessor, not the generated wrapper), reached here through
/// AccessTools.PropertySetter, which finds and invokes a public setter without incident, and whose
/// per-minute recompute, OnUncappedMinPass, which reflection reports public on the referenced
/// Assembly-CSharp.dll and which AccessTools.Method resolves either way, is the smallest verified
/// seam, so the only mod side hook is a Harmony postfix on that method plus one AccessTools reach
/// into the two setters. Every member this file touches on CurfewManager, TimeManager, and Player was
/// verified by reflection against the referenced Il2Cpp assemblies, not assumed from the research
/// note.
///
/// The Harmony patch is applied lazily: <see cref="TryEngage"/> is the only path that ever calls
/// <see cref="EnsurePatched"/>, and Mod.cs reaches <see cref="TryEngage"/> from its End key handler
/// and from <see cref="S1ApiRelease1SmallCourtesyWorld.TryEngageLockdown"/>, itself gated on the
/// OwnerQaKeys preference for the End key handler and reached only from the Chief's convergence pass
/// once an announcement receipt exists for the second caller. Nothing in
/// <see cref="OnUncappedMinPassPostfix"/> runs, and the patch is not even installed into Harmony's
/// patch table, until the owner has pressed End at least once with owner keys on. Once installed, the
/// postfix is a second, independent gate on top of that: it returns immediately whenever
/// <see cref="Release1LockdownGateState.LockdownActive"/> is false, so a released lockdown is fully
/// inert on every later native minute tick, not merely quiet.
/// </summary>
public static class Release1LockdownGatePatch
{
    private static readonly Release1LockdownGateState State = new();
    private static readonly Release1LockdownGateThrottle Throttle = new();
    private static Action<string> _log = _ => { };
    private static bool _patched;

    public static bool LockdownActive => State.LockdownActive;

    /// <summary>
    /// First press. Applies the Harmony postfix if this is the first press this session, reads the
    /// curfew manager's current flags, enables curfew on the save through the game's own Enable path
    /// if it was not already enabled and this session is the authoritative host, then engages the
    /// lockdown flag so the postfix starts re-asserting curfew active on every subsequent recompute.
    /// If curfew was not enabled and this session is not the authoritative host (or no local player
    /// connection is available to enable it with), nothing changes: the lockdown does not engage, and
    /// the result reports NeedsOptionB.
    /// </summary>
    public static Release1StagingHarnessResult TryEngage(
        HarmonyLib.Harmony harmony, Action<string> log, Func<bool> isAuthoritativeHost)
    {
        _log = log ?? (_ => { });
        var lines = new List<string>();
        try
        {
            if (!EnsurePatched(harmony))
            {
                lines.Add("the Harmony postfix on the curfew manager's per-minute recompute could not be applied.");
                return new(Release1StagingHarnessStatus.Faulted, lines);
            }

            var manager = CurfewManager.Instance;
            if (manager is null)
            {
                lines.Add("the curfew manager instance was not available.");
                return new(Release1StagingHarnessStatus.Unavailable, lines);
            }

            if (State.LockdownActive)
            {
                lines.Add(DescribeManager(manager, "the lockdown was already engaged."));
                return new(Release1StagingHarnessStatus.Succeeded, lines);
            }

            var curfewEnabledByThisCall = false;
            if (!manager.IsEnabled)
            {
                if (!isAuthoritativeHost())
                {
                    lines.Add(DescribeManager(manager,
                        "NEEDS OPTION B: curfew is not enabled on this save and this session is not the authoritative host, so the game's Enable path was not called."));
                    return new(Release1StagingHarnessStatus.NeedsOptionB, lines);
                }

                var connection = Player.Local?.Connection;
                if (connection is null)
                {
                    lines.Add(DescribeManager(manager,
                        "NEEDS OPTION B: curfew is not enabled on this save and no local player connection was available to call Enable with."));
                    return new(Release1StagingHarnessStatus.NeedsOptionB, lines);
                }

                manager.Enable(connection);
                curfewEnabledByThisCall = true;
            }

            State.Engage(curfewEnabledByThisCall);
            Throttle.Reset();
            lines.Add(DescribeManager(manager,
                curfewEnabledByThisCall
                    ? "engaged; curfew was enabled on this save by this press."
                    : "engaged; curfew was already enabled on this save."));
            return new(Release1StagingHarnessStatus.Succeeded, lines);
        }
        catch (Exception ex)
        {
            lines.Add("an exception was thrown while engaging the lockdown: " + ex.Message);
            return new(Release1StagingHarnessStatus.Faulted, lines);
        }
    }

    /// <summary>
    /// Second press. Clears the lockdown flag so the very next native per-minute recompute restores
    /// the vanilla schedule with no extra call needed for the hour based flags, then disables curfew
    /// on the save through the game's own Disable path if this gate itself had enabled it on engage.
    /// </summary>
    public static Release1StagingHarnessResult TryRelease(Func<bool> isAuthoritativeHost)
    {
        var lines = new List<string>();
        try
        {
            var manager = CurfewManager.Instance;
            if (!State.LockdownActive)
            {
                lines.Add(manager is null
                    ? "the lockdown was already released."
                    : DescribeManager(manager, "the lockdown was already released."));
                return new(Release1StagingHarnessStatus.Succeeded, lines);
            }

            var shouldDisableCurfew = State.Release();
            Throttle.Reset();

            if (shouldDisableCurfew)
            {
                if (manager is not null && isAuthoritativeHost())
                {
                    manager.Disable();
                }
                else
                {
                    lines.Add("NEEDS OPTION B: curfew was enabled by this gate but could not be disabled again because this session is not the authoritative host or the manager was unavailable.");
                    return new(Release1StagingHarnessStatus.NeedsOptionB, lines);
                }
            }

            lines.Add(manager is null
                ? "released."
                : DescribeManager(manager,
                    shouldDisableCurfew
                        ? "released; curfew was disabled again because this gate had enabled it."
                        : "released; curfew was left enabled because this gate had not enabled it."));
            return new(Release1StagingHarnessStatus.Succeeded, lines);
        }
        catch (Exception ex)
        {
            lines.Add("an exception was thrown while releasing the lockdown: " + ex.Message);
            return new(Release1StagingHarnessStatus.Faulted, lines);
        }
    }

    /// <summary>
    /// Mod teardown safety net. Best effort: if the lockdown was left engaged (a mod reload without a
    /// game restart), releases it the same way a second End press would, swallowing any exception so
    /// deinitialization never throws. Clears the throttle either way.
    /// </summary>
    public static void Reset()
    {
        try
        {
            if (State.LockdownActive) TryRelease(() => true);
        }
        catch
        {
            // Best effort only; deinitialization must never throw.
        }
        finally
        {
            Throttle.Reset();
            _log = _ => { };
        }
    }

    private static bool EnsurePatched(HarmonyLib.Harmony harmony)
    {
        if (_patched) return true;
        try
        {
            var original = AccessTools.Method(typeof(CurfewManager), "OnUncappedMinPass")
                ?? throw new MissingMethodException(typeof(CurfewManager).FullName, "OnUncappedMinPass");
            var postfix = AccessTools.Method(typeof(Release1LockdownGatePatch), nameof(OnUncappedMinPassPostfix))
                ?? throw new MissingMethodException(typeof(Release1LockdownGatePatch).FullName, nameof(OnUncappedMinPassPostfix));
            harmony.Patch(original, postfix: new HarmonyMethod(postfix));
            _patched = true;
            return true;
        }
        catch (Exception ex)
        {
            _log($"the postfix on CurfewManager.OnUncappedMinPass was not applied: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void OnUncappedMinPassPostfix(CurfewManager __instance)
    {
        if (!State.LockdownActive || __instance is null) return;
        try
        {
            var activeSetter = AccessTools.PropertySetter(typeof(CurfewManager), nameof(CurfewManager.IsCurrentlyActive));
            var hardActiveSetter = AccessTools.PropertySetter(typeof(CurfewManager), nameof(CurfewManager.IsHardCurfewActive));
            activeSetter?.Invoke(__instance, new object[] { true });
            hardActiveSetter?.Invoke(__instance, new object[] { true });

            var minuteKey = TimeManager.Instance?.CurrentTime ?? -1;
            if (Throttle.TryMark(minuteKey))
            {
                _log(DescribeManager(__instance, "re-asserted curfew active on the native per-minute recompute."));
            }
        }
        catch (Exception ex)
        {
            _log($"re-assert failed on the native per-minute recompute: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string DescribeManager(CurfewManager manager, string suffix)
    {
        var gameHour = TimeManager.Instance?.CurrentTime;
        return $"enabled={manager.IsEnabled} active={manager.IsCurrentlyActive} hardActive={manager.IsHardCurfewActive} " +
               $"gameHour={(gameHour.HasValue ? gameHour.Value.ToString() : "unknown")} patchApplied={_patched} {suffix}";
    }
}
