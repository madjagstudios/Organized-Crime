using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using NativeCheckpointBehaviour = Il2CppScheduleOne.NPCs.Behaviour.CheckpointBehaviour;
using NativeCheckpointManager = Il2CppScheduleOne.Law.CheckpointManager;
using NativeRoadCheckpoint = Il2CppScheduleOne.Police.RoadCheckpoint;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-141. The native CheckpointBehaviour.Activate/Resume bodies resolve the
/// officer's checkpoint through CheckpointManager.GetCheckpoint, which only
/// knows the four native checkpoints, so any Checkpoint OC sets on a clone is
/// overwritten on Activate. This patch borrows one manager field for the
/// duration of the native body: the prefix swaps the Western field to the OC
/// checkpoint when the behaviour's NPC is a mapped OC clone, and the postfix
/// plus the finalizer always restore the original. The swap window is fully
/// synchronous, every method fails open to vanilla, and native officers are
/// untouched (they are never in the map). Member facts this file relies on
/// (f9 index db4df318): Activate/Resume are overload-free overrides on
/// CheckpointBehaviour, Npc/AssignedCheckpoint read from the behaviour, and
/// CheckpointManager.WesternCheckpoint is a settable RoadCheckpoint reached
/// through the NetworkSingleton instance. The structural test resolves every
/// target through the same AccessTools calls Apply uses, against the
/// referenced f9 interop.
/// </summary>
internal static class OcCheckpointSwapPatch
{
    private static bool _patched;
    private static Action<string>? _log;
    private static bool _restoreWarned;

    public static bool Apply(HarmonyLib.Harmony harmony, Action<string>? log = null)
    {
        _log = log ?? (_ => { });
        if (_patched) return true;
        var ok = true;
        ok &= TryPatch(harmony, nameof(NativeCheckpointBehaviour.Activate));
        ok &= TryPatch(harmony, nameof(NativeCheckpointBehaviour.Resume));
        if (ok) _patched = true;
        return ok;
    }

    private static bool TryPatch(HarmonyLib.Harmony harmony, string methodName)
    {
        try
        {
            var target = AccessTools.DeclaredMethod(typeof(NativeCheckpointBehaviour), methodName, Type.EmptyTypes);
            if (target is null)
            {
                _log?.Invoke($"OC checkpoint swap missed CheckpointBehaviour.{methodName}; cloned officers cannot take checkpoint duty.");
                return false;
            }
            var prefix = AccessTools.Method(typeof(OcCheckpointSwapPatch), nameof(SwapPrefix));
            var postfix = AccessTools.Method(typeof(OcCheckpointSwapPatch), nameof(SwapPostfix));
            var finalizer = AccessTools.Method(typeof(OcCheckpointSwapPatch), nameof(SwapFinalizer));
            if (prefix is null || postfix is null || finalizer is null)
            {
                _log?.Invoke($"OC checkpoint swap missed its own {methodName} hook.");
                return false;
            }
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix),
                postfix: new HarmonyMethod(postfix),
                finalizer: new HarmonyMethod(finalizer));
            return true;
        }
        catch (Exception exception)
        {
            _log?.Invoke($"OC checkpoint swap failed on CheckpointBehaviour.{methodName}: {exception.GetType().Name}.");
            return false;
        }
    }

    private static bool SwapPrefix(NativeCheckpointBehaviour __instance, out OcCheckpointSwap __state)
    {
        __state = OcCheckpointSwap.None;
        try
        {
            if (__instance is null || __instance == null)
                return true;
            var npc = __instance.Npc;
            if (npc is null || npc == null)
                return true;
            if (!NetworkSingleton<NativeCheckpointManager>.InstanceExists)
                return true;
            var manager = NetworkSingleton<NativeCheckpointManager>.Instance;
            if (manager is null || manager == null)
                return true;
            __state = OcCheckpointSwapState.BeginSwap(
                npc.Pointer.ToInt64(),
                __instance.AssignedCheckpoint,
                readField: () => manager.WesternCheckpoint,
                writeField: value => manager.WesternCheckpoint = (NativeRoadCheckpoint?)value);
            return true;
        }
        catch
        {
            __state = OcCheckpointSwap.None;
            return true;
        }
    }

    private static void SwapPostfix(OcCheckpointSwap __state) => Restore(__state);

    private static void SwapFinalizer(OcCheckpointSwap __state) => Restore(__state);

    // The finalizer covers the native body throwing; running both it and the
    // postfix restores the same original twice, which is a no-op the second
    // time. A void finalizer lets the native exception propagate untouched.
    private static void Restore(OcCheckpointSwap __state)
    {
        try
        {
            if (__state is null || !__state.Swapped)
                return;
            if (!NetworkSingleton<NativeCheckpointManager>.InstanceExists)
                return;
            var manager = NetworkSingleton<NativeCheckpointManager>.Instance;
            if (manager is null || manager == null)
                return;
            var restored = OcCheckpointSwapState.EndSwap(
                __state,
                value => manager.WesternCheckpoint = (NativeRoadCheckpoint?)value);
            if (!restored && !_restoreWarned)
            {
                _restoreWarned = true;
                _log?.Invoke("OC checkpoint swap: the manager field could not be restored; native checkpoint staffing may misbehave until reload.");
            }
        }
        catch
        {
        }
    }
}
