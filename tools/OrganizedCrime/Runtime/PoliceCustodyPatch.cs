using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.PlayerScripts;

namespace OrganizedCrime.Runtime;

public static class PoliceCustodyPatch
{
    public const string TargetMethodName = "RpcLogic___Arrest_Server_2166136261";
    public static IReadOnlyList<string> DeclaredTargetNames { get; } = new[] { TargetMethodName };

    private static PoliceCustodyEvidenceBridge? _bridge;
    private static Action<string> _log = _ => { };

    public static MethodInfo ResolveTarget()
    {
        var target = AccessTools.Method(typeof(Player), TargetMethodName, Type.EmptyTypes);
        if (target is null || target.ReturnType != typeof(void) || target.DeclaringType != typeof(Player))
            throw new MissingMethodException(typeof(Player).FullName, TargetMethodName);

        return target;
    }

    public static bool Apply(
        HarmonyLib.Harmony harmony,
        PoliceCustodyEvidenceBridge bridge,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(bridge);
        _log = log ?? (_ => { });

        MethodInfo target;
        try
        {
            target = ResolveTarget();
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(PoliceCustodyPatch), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(PoliceCustodyPatch), nameof(Postfix)));
            _bridge = bridge;
            return true;
        }
        catch (Exception exception)
        {
            _bridge = null;
            _log($"Police custody patch was not applied: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    public static void Reset()
    {
        _bridge = null;
        _log = _ => { };
    }

    private static void Prefix(Player __instance, out PoliceCustodyPrefixState __state)
    {
        __state = PoliceCustodyPrefixState.Ineligible;
        var bridge = _bridge;
        if (bridge is null)
            return;

        try
        {
            __state = bridge.CapturePrefix(Observe(__instance));
        }
        catch (Exception exception)
        {
            _log($"Police custody prefix observation failed closed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void Postfix(Player __instance, PoliceCustodyPrefixState __state)
    {
        var bridge = _bridge;
        if (bridge is null)
            return;

        try
        {
            bridge.ConfirmPostfix(Observe(__instance), __state);
        }
        catch (Exception exception)
        {
            _log($"Police custody postfix observation failed closed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static CustodyPlayerObservation Observe(Player instance)
    {
        if (instance is null)
            return default;

        var playerCode = ReadPlayerCode(instance);
        var isHostOwned = ReadHostOwnership(instance);
        var hasConnection = ReadConnection(instance);
        var arrestState = ReadArrested(instance);
        var region = ReadRegion(instance);
        var propertyCode = ReadPropertyCode(instance);
        return new(
            instance,
            playerCode,
            isHostOwned,
            hasConnection,
            arrestState.Value,
            region,
            propertyCode,
            arrestState.Succeeded);
    }

    private static string? ReadPlayerCode(Player instance)
    {
        try { return instance.PlayerCode?.Trim(); }
        catch { return null; }
    }

    private static bool ReadHostOwnership(Player instance)
    {
        try { return instance.IsServerInitialized; }
        catch { return false; }
    }

    private static bool ReadConnection(Player instance)
    {
        try { return instance.Connection is not null; }
        catch { return false; }
    }

    private static (bool Succeeded, bool Value) ReadArrested(Player instance)
    {
        try { return (true, instance.IsArrested); }
        catch { return (false, false); }
    }

    private static string? ReadRegion(Player instance)
    {
        try { return instance.CurrentRegion.ToString(); }
        catch { return null; }
    }

    private static string? ReadPropertyCode(Player instance)
    {
        try { return instance.CurrentProperty?.PropertyCode; }
        catch { return null; }
    }
}
