using HarmonyLib;
using NativeCallPoliceBehaviour = Il2CppScheduleOne.NPCs.Behaviour.CallPoliceBehaviour;
using NativeCheckpointInstance = Il2CppScheduleOne.Law.CheckpointInstance;
using NativeCheckpointManager = Il2CppScheduleOne.Law.CheckpointManager;
using NativeFootRoute = Il2CppScheduleOne.NPCs.Behaviour.FootPatrolRoute;
using NativeLawManager = Il2CppScheduleOne.Law.LawManager;
using NativeOfficer = Il2CppScheduleOne.Police.PoliceOfficer;
using NativePatrolGroup = Il2CppScheduleOne.NPCs.Behaviour.PatrolGroup;
using NativePatrolInstance = Il2CppScheduleOne.Law.PatrolInstance;
using NativePlayer = Il2CppScheduleOne.PlayerScripts.Player;
using NativeSentryInstance = Il2CppScheduleOne.Law.SentryInstance;
using NativeVehiclePatrolInstance = Il2CppScheduleOne.Law.VehiclePatrolInstance;
using NativeVehicleRoute = Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-118. The native law gates behind Campbell's protection retainer: nine Harmony prefixes,
/// each a provider read, a gate-state decision, and a boolean, every one failing open to vanilla
/// behaviour. The decision core is <see cref="Release1ProtectionGateState"/> (covered on the host,
/// per gate); this file only adapts native calls into it:
/// <list type="bullet">
/// <item><c>LawManager.PoliceCalled</c>: dropped when the reported player is the local, protected
/// player. The dispatch and the search-position record inside never run for him; other players
/// are unaffected, and the officer-vision escalation paths are untouched.</item>
/// <item><c>CallPoliceBehaviour.IsTargetValid</c> (owner run fix): a civilian's own phone call
/// sets Investigating and adds the crime before PoliceCalled, so for the protected local player
/// the call's target reads invalid and the behaviour backs out through its own path.</item>
/// <item><c>PatrolInstance.StartPatrol</c> and <c>LawManager.StartFootpatrol</c>: about half the
/// foot starts skipped, both levels sharing one coin over the route name and the game day, so a
/// patrol never flickers within a day and instance and funnel always agree. Skipping at instance
/// entry leaves no half-started state: the subscription, the officer pull, and the group build
/// all live inside the skipped body.</item>
/// <item><c>VehiclePatrolInstance.StartPatrol</c> and <c>LawManager.StartVehiclePatrol</c>: the
/// same two-level coin for vehicle starts. Both levels are required, not just agreed: the
/// schedule's Evaluate calls the funnel directly, bypassing the instance start.</item>
/// <item><c>SentryInstance.StartEntry</c>: every sentry start skipped.</item>
/// <item><c>CheckpointInstance.EnableCheckpoint</c>: every schedule enable skipped at entry, so
/// the instance never touches its own state and the next Evaluate after a lapse re-enables on
/// its own.</item>
/// <item><c>CheckpointManager.SetCheckpointEnabled</c>: the backstop over every enabler. Enables
/// (true) are blocked while covered; disables always pass, so the cover-start disable-all and
/// the schedule's own window ends are never gated.</item>
/// </list>
/// Patrols and sentries already running when protection starts are never ended: EndPatrol and
/// EndSentry stay unpatched, and the schedule's own MinPass/CheckEnd finish them normally.
/// Member facts this file relies on (f9 index db4df318): both instance types expose a public
/// Route field, both route types are MonoBehaviours with a public RouteName string, both
/// funnels take the route as their first argument, every target is overload-free on its
/// declaring type, and RoadCheckpoint.Enable is server-side (IsServerInitialized), matching the
/// host-only gate. The structural test resolves every target through the same AccessTools calls
/// Apply uses, against the referenced f9 interop.
/// </summary>
internal static class Release1ProtectionGatePatch
{
    private static bool _patched;

    public static bool Apply(HarmonyLib.Harmony harmony, Action<string>? log = null)
    {
        log ??= (_ => { });
        if (_patched) return true;
        var ok = true;
        ok &= TryPatch(
            harmony, log, typeof(NativeLawManager), nameof(NativeLawManager.PoliceCalled),
            new[] { typeof(NativePlayer), typeof(Il2CppScheduleOne.Law.Crime) }, nameof(PoliceCalledPrefix));
        ok &= TryPatch(
            harmony, log, typeof(NativeCallPoliceBehaviour), nameof(NativeCallPoliceBehaviour.IsTargetValid),
            Type.EmptyTypes, nameof(CallPoliceTargetPrefix));
        ok &= TryPatch(
            harmony, log, typeof(NativePatrolInstance), nameof(NativePatrolInstance.StartPatrol),
            Type.EmptyTypes, nameof(FootPatrolStartPrefix));
        ok &= TryPatch(
            harmony, log, typeof(NativeLawManager), nameof(NativeLawManager.StartFootpatrol),
            new[] { typeof(NativeFootRoute), typeof(int) }, nameof(StartFootpatrolPrefix));
        ok &= TryPatch(
            harmony, log, typeof(NativeVehiclePatrolInstance), nameof(NativeVehiclePatrolInstance.StartPatrol),
            Type.EmptyTypes, nameof(VehiclePatrolStartPrefix));
        ok &= TryPatch(
            harmony, log, typeof(NativeLawManager), nameof(NativeLawManager.StartVehiclePatrol),
            new[] { typeof(NativeVehicleRoute) }, nameof(StartVehiclePatrolPrefix));
        ok &= TryPatch(
            harmony, log, typeof(NativeSentryInstance), nameof(NativeSentryInstance.StartEntry),
            Type.EmptyTypes, nameof(SentryStartPrefix));
        ok &= TryPatch(
            harmony, log, typeof(NativeCheckpointInstance), nameof(NativeCheckpointInstance.EnableCheckpoint),
            Type.EmptyTypes, nameof(EnableCheckpointPrefix));
        ok &= TryPatch(
            harmony, log, typeof(NativeCheckpointManager), nameof(NativeCheckpointManager.SetCheckpointEnabled),
            new[] { typeof(NativeCheckpointManager.ECheckpointLocation), typeof(bool), typeof(int) },
            nameof(SetCheckpointEnabledPrefix));
        if (ok) _patched = true;
        return ok;
    }

    private static bool TryPatch(
        HarmonyLib.Harmony harmony,
        Action<string> log,
        Type targetType,
        string methodName,
        Type[] argumentTypes,
        string prefixName)
    {
        try
        {
            var target = AccessTools.Method(targetType, methodName, argumentTypes);
            if (target is null)
            {
                log($"Chief protection gate missed {targetType.Name}.{methodName}; that native behaviour stays ungated while covered.");
                return false;
            }
            var prefix = AccessTools.Method(typeof(Release1ProtectionGatePatch), prefixName);
            if (prefix is null)
            {
                log($"Chief protection gate missed its own {prefixName}.");
                return false;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            return true;
        }
        catch (Exception exception)
        {
            log($"Chief protection gate failed on {targetType.Name}.{methodName}: {exception.GetType().Name}.");
            return false;
        }
    }

    private static bool PoliceCalledPrefix(NativePlayer __0)
    {
        try
        {
            return !Release1ProtectionGateState.ShouldDropPoliceCall(ReportedPlayerIsLocal(__0));
        }
        catch
        {
            return true;
        }
    }

    // OC-118 owner run: a civilian's phone call on the protected local player reads as an invalid
    // target, so FinalizeCall never sets Investigating or adds the crime, and the caller gives up.
    private static bool CallPoliceTargetPrefix(NativeCallPoliceBehaviour __instance, ref bool __result)
    {
        try
        {
            if (!Release1ProtectionGateState.ShouldVoidCivilianCallTarget(ReportedPlayerIsLocal(__instance?.Target)))
                return true;
            __result = false;
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool FootPatrolStartPrefix(NativePatrolInstance __instance)
    {
        try
        {
            return !Release1ProtectionGateState.ShouldSkipFootPatrolInstance(RouteKey(__instance?.Route), Today());
        }
        catch
        {
            return true;
        }
    }

    private static bool StartFootpatrolPrefix(NativeFootRoute __0, ref NativePatrolGroup? __result)
    {
        try
        {
            if (!Release1ProtectionGateState.ShouldSkipFootPatrol(RouteKey(__0), Today()))
                return true;
            __result = null;
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool VehiclePatrolStartPrefix(NativeVehiclePatrolInstance __instance)
    {
        try
        {
            return !Release1ProtectionGateState.ShouldSkipVehiclePatrolInstance(RouteKey(__instance?.Route), Today());
        }
        catch
        {
            return true;
        }
    }

    private static bool StartVehiclePatrolPrefix(NativeVehicleRoute __0, ref NativeOfficer? __result)
    {
        try
        {
            if (!Release1ProtectionGateState.ShouldSkipVehiclePatrol(RouteKey(__0), Today()))
                return true;
            __result = null;
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool SentryStartPrefix()
    {
        try
        {
            return !Release1ProtectionGateState.ShouldSkipSentry();
        }
        catch
        {
            return true;
        }
    }

    private static bool EnableCheckpointPrefix()
    {
        try
        {
            return !Release1ProtectionGateState.ShouldBlockCheckpointEnable(enabling: true);
        }
        catch
        {
            return true;
        }
    }

    private static bool SetCheckpointEnabledPrefix(bool __1)
    {
        try
        {
            return !Release1ProtectionGateState.ShouldBlockCheckpointEnable(__1);
        }
        catch
        {
            return true;
        }
    }

    // The reported player is local when both wrappers point at the same native object. Pointer
    // comparison, not reference equality: interop may hand out distinct wrappers per call.
    private static bool ReportedPlayerIsLocal(NativePlayer? reported)
    {
        try
        {
            var local = NativePlayer.Local;
            return reported is not null && local is not null && reported.Pointer == local.Pointer;
        }
        catch
        {
            return false;
        }
    }

    // The coin key both patrol levels share: the route's own name, null when anything on the
    // path is missing, which fails open to running the patrol.
    private static string? RouteKey(object? route)
    {
        try
        {
            return route switch
            {
                NativeFootRoute foot => string.IsNullOrEmpty(foot.RouteName) ? null : foot.RouteName,
                NativeVehicleRoute vehicle => string.IsNullOrEmpty(vehicle.RouteName) ? null : vehicle.RouteName,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static int? Today()
    {
        try
        {
            return Release1ProtectionGateState.Today?.Invoke();
        }
        catch
        {
            return null;
        }
    }
}
