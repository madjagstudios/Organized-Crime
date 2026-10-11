using System;
using HarmonyLib;
using NativeImpact = Il2CppScheduleOne.Combat.Impact;
using NativeNpc = Il2CppScheduleOne.NPCs.NPC;
using NativeNpcHealth = Il2CppScheduleOne.NPCs.NPCHealth;
using NativeResponses = Il2CppScheduleOne.NPCs.Responses.NPCResponses;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-135: keeps Arthur from reacting like a native NPC when he is hit.
/// f7 offers no supported surface for this: S1API 3.2.1-beta.7 exposes no
/// response override, and the native <c>NPCHealth._invincible</c> flag is
/// never read by game code (no <c>get__invincible</c> call in the f7
/// scripts), so setting it would be a placebo. The five prefixes below
/// are the narrowest seam instead, each scoped to Arthur's NPC id and
/// each failing open to vanilla behaviour for every other NPC:
/// <list type="bullet">
/// <item><c>ReceiveImpact</c>: skipped for Arthur, so a punch never
/// reaches damage, force, or the response dispatch; the hit is recorded
/// for the visit pump.</item>
/// <item><c>ImpactReceived</c>: skipped for Arthur, so no native voice,
/// flee, cower, fight, police call, or crime report runs. f7 builds
/// every <c>Assault</c> only inside <c>NPCResponses.RespondTo*</c>, so
/// nothing reaches the police or the crime record.</item>
/// <item><c>TakeDamage</c>: the damage is zeroed for Arthur, so direct
/// damage paths (explosions, cars) cannot hurt, stun, or kill him
/// either.</item>
/// <item><c>KnockOut</c> and <c>Die</c>: skipped for Arthur as a safety
/// net over direct calls.</item>
/// </list>
/// </summary>
internal static class Chapter3ArthurResponsePatch
{
    public static bool Apply(HarmonyLib.Harmony harmony, Action<string>? log = null)
    {
        log ??= (_ => { });
        var ok = true;
        ok &= TryPatch(
            harmony, log, typeof(NativeNpc), nameof(NativeNpc.ReceiveImpact),
            new[] { typeof(NativeImpact) }, nameof(ReceiveImpactPrefix));
        ok &= TryPatch(
            harmony, log, typeof(NativeResponses), nameof(NativeResponses.ImpactReceived),
            new[] { typeof(NativeImpact) }, nameof(ImpactReceivedPrefix));
        ok &= TryPatch(
            harmony, log, typeof(NativeNpcHealth), nameof(NativeNpcHealth.TakeDamage),
            new[] { typeof(float), typeof(bool) }, nameof(TakeDamagePrefix));
        ok &= TryPatch(
            harmony, log, typeof(NativeNpcHealth), nameof(NativeNpcHealth.KnockOut),
            Type.EmptyTypes, nameof(KnockOutPrefix));
        ok &= TryPatch(
            harmony, log, typeof(NativeNpcHealth), nameof(NativeNpcHealth.Die),
            Type.EmptyTypes, nameof(DiePrefix));
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
                log($"Chapter 3 Arthur response patch missed {targetType.Name}.{methodName}; Arthur may react like a native NPC.");
                return false;
            }
            var prefix = AccessTools.Method(typeof(Chapter3ArthurResponsePatch), prefixName);
            if (prefix is null)
            {
                log($"Chapter 3 Arthur response patch missed its own {prefixName}.");
                return false;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            return true;
        }
        catch (Exception exception)
        {
            log($"Chapter 3 Arthur response patch failed on {targetType.Name}.{methodName}: {exception.GetType().Name}.");
            return false;
        }
    }

    private static bool ReceiveImpactPrefix(NativeNpc __instance, NativeImpact __0)
    {
        if (!IsArthurNpc(__instance))
            return true;
        Chapter3ArthurResponseFilter.NoteImpact();
        return false;
    }

    private static bool ImpactReceivedPrefix(NativeResponses __instance, NativeImpact __0)
    {
        if (!IsArthurResponses(__instance))
            return true;
        Chapter3ArthurResponseFilter.NoteImpact();
        return false;
    }

    private static void TakeDamagePrefix(NativeNpcHealth __instance, ref float __0)
    {
        if (!IsArthurHealth(__instance))
            return;
        __0 = 0f;
    }

    private static bool KnockOutPrefix(NativeNpcHealth __instance) => !IsArthurHealth(__instance);

    private static bool DiePrefix(NativeNpcHealth __instance) => !IsArthurHealth(__instance);

    private static bool IsArthurNpc(NativeNpc? npc)
    {
        try
        {
            return npc is not null && Chapter3ArthurResponseFilter.ShouldSuppress(npc.ID);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsArthurHealth(NativeNpcHealth? health)
    {
        try
        {
            return health is not null && IsArthurNpc(health._npc);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsArthurResponses(NativeResponses? responses)
    {
        try
        {
            return responses is not null && IsArthurNpc(responses.npc);
        }
        catch
        {
            return false;
        }
    }
}
