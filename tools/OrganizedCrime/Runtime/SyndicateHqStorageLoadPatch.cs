using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Persistence.Loaders;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ObjectScripts;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class SyndicateHqStorageLoadContextAdapter
{
    public bool TryRead(string? nativeMainPath, out SyndicateHqStorageContext context, out string reason)
    {
        context = default!;
        try
        {
            var server = InstanceFinder.ServerManager;
            var client = InstanceFinder.ClientManager;
            if (server is null || client is null || !server.OneServerStarted() || !client.Started)
            {
                reason = "The authoritative single-player host was not ready at the storage-load boundary.";
                return false;
            }

            if (!TryFindSaveRoot(nativeMainPath, out var saveRoot) &&
                !TryFindSaveRoot(LoadManager.Instance?.LoadedGameFolderPath, out saveRoot))
            {
                reason = "The native storage-load path did not resolve to a canonical save root.";
                return false;
            }

            if (!LocalPressureHostLifecyclePolicies.TryGetCanonicalHostIdentity(saveRoot, out var identity) ||
                string.IsNullOrWhiteSpace(identity))
            {
                reason = "The storage-load save root did not expose a canonical Steam identity.";
                return false;
            }

            var supportedPlayers = new List<Player>();
            if (Player.PlayerList is not null)
            {
                foreach (var player in Player.PlayerList)
                    if (player is not null && player.IsServerInitialized && player.Connection is not null)
                        supportedPlayers.Add(player);
            }

            if (supportedPlayers.Count > 1 ||
                supportedPlayers.Count == 1 &&
                !string.Equals(supportedPlayers[0].PlayerCode?.Trim(), identity, StringComparison.Ordinal))
            {
                reason = "The storage-load player registry was multiplayer or disagreed with the canonical save identity.";
                return false;
            }

            context = new(true, identity!, saveRoot, saveRoot);
            reason = "Canonical host and save binding were resolved at the vanilla storage-load boundary.";
            return true;
        }
        catch (Exception ex)
        {
            context = default!;
            reason = $"Storage-load context resolution threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public static bool TryFindSaveRoot(string? path, out string saveRoot)
    {
        saveRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            for (DirectoryInfo? current = new(Path.GetFullPath(path)); current is not null; current = current.Parent)
            {
                if (!current.Name.StartsWith("SaveGame_", StringComparison.OrdinalIgnoreCase) ||
                    current.Name.Length == "SaveGame_".Length ||
                    current.Parent is null || current.Parent.Parent is null ||
                    !string.Equals(current.Parent.Parent.Name, "Saves", StringComparison.OrdinalIgnoreCase) ||
                    !LocalPressureHostLifecyclePolicies.TryGetCanonicalHostIdentity(current.FullName, out _))
                    continue;

                saveRoot = Path.TrimEndingDirectorySeparator(current.FullName);
                return true;
            }
        }
        catch
        {
            // Fail closed below.
        }
        return false;
    }
}

internal static class SyndicateHqStorageLoadPatch
{
    private static ISyndicateHqStorageRuntime? _runtime;
    private static SyndicateHqStorageLoadContextAdapter? _context;
    private static Action<string> _log = _ => { };
    private static Action<string> _receiptLog = _ => { };
    private static bool _loadCycleAttempted;
    private static bool _loadCyclePrepared;

    public static bool Apply(
        HarmonyLib.Harmony harmony,
        ISyndicateHqStorageRuntime runtime,
        SyndicateHqStorageLoadContextAdapter context,
        Action<string>? log = null,
        Action<string>? receiptLog = null)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(context);
        _runtime = runtime;
        _context = context;
        _log = log ?? (_ => { });
        _receiptLog = receiptLog ?? _log;
        try
        {
            var original = AccessTools.Method(
                typeof(PlaceableStorageEntityLoader),
                nameof(PlaceableStorageEntityLoader.Load),
                new[] { typeof(DynamicSaveData) })
                ?? throw new MissingMethodException(typeof(StorageLoader).FullName, nameof(StorageLoader.Load));
            var prefix = AccessTools.Method(typeof(SyndicateHqStorageLoadPatch), nameof(Prefix))
                ?? throw new MissingMethodException(typeof(SyndicateHqStorageLoadPatch).FullName, nameof(Prefix));
            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
            var cullingOriginal = AccessTools.Method(
                typeof(BuildableItem),
                nameof(BuildableItem.SetCulled),
                new[] { typeof(bool) })
                ?? throw new MissingMethodException(typeof(BuildableItem).FullName, nameof(BuildableItem.SetCulled));
            var cullingPrefix = AccessTools.Method(typeof(SyndicateHqStorageLoadPatch), nameof(CullingPrefix))
                ?? throw new MissingMethodException(typeof(SyndicateHqStorageLoadPatch).FullName, nameof(CullingPrefix));
            harmony.Patch(cullingOriginal, prefix: new HarmonyMethod(cullingPrefix));
#if DEBUG
            _receiptLog("Syndicate HQ storage receipt: vanilla load prefix applied.");
#endif
            return true;
        }
        catch (Exception ex)
        {
            _runtime = null;
            _context = null;
            _log($"Syndicate HQ storage-load prefix was not applied: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static void Reset()
    {
        _runtime?.DisableInteractions();
        _runtime = null;
        _context = null;
        _loadCycleAttempted = false;
        _loadCyclePrepared = false;
        SyndicateHqStorageCullingGuard.Reset();
        _log = _ => { };
        _receiptLog = _ => { };
    }

    public static void BeginLoadCycle()
    {
        _loadCycleAttempted = false;
        _loadCyclePrepared = false;
        _runtime?.DisableInteractions();
#if DEBUG
        _receiptLog("Syndicate HQ storage receipt: load cycle began.");
#endif
    }

    public static bool CompleteAfterVanillaLoad()
    {
        PrepareOnce(null);
        var runtime = _runtime;
#if DEBUG
        _receiptLog($"Syndicate HQ storage receipt: completion boundary reached; runtime={runtime is not null}; attempted={_loadCycleAttempted}; prepared={_loadCyclePrepared}.");
#endif
        if (runtime is null || !_loadCyclePrepared) return false;
        try
        {
            var result = runtime.CompleteVanillaLoadBoundary();
#if DEBUG
            _receiptLog($"Syndicate HQ storage receipt: completion result={result.Status}; ready={result.IsReady}; reason={result.Reason}");
#endif
            if (!result.IsReady)
                _log($"Syndicate HQ standard closets remained inert after vanilla load: {result.Status} — {result.Reason}");
            return result.IsReady;
        }
        catch (Exception ex)
        {
            runtime.DisableInteractions();
            _log($"Syndicate HQ closet reconciliation failed closed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void Prefix(DynamicSaveData __0) => PrepareOnce(null);

    private static void CullingPrefix(BuildableItem __instance, ref bool __0)
    {
        if (!__0 || __instance is not PlaceableStorageEntity closet || closet == null ||
            !Guid.TryParse(closet.GUID.ToString(), out var guid)) return;
        __0 = SyndicateHqStorageCullingGuard.Filter(guid, __0);
    }

    private static void PrepareOnce(string? nativeMainPath)
    {
        var runtime = _runtime;
        var context = _context;
#if DEBUG
        _receiptLog($"Syndicate HQ storage receipt: prepare requested; nativePath={(!string.IsNullOrWhiteSpace(nativeMainPath) ? "present" : "absent")}; runtime={runtime is not null}; context={context is not null}; attempted={_loadCycleAttempted}.");
#endif
        if (runtime is null || context is null || _loadCycleAttempted) return;
        _loadCycleAttempted = true;
        try
        {
            if (!context.TryRead(nativeMainPath, out var snapshot, out var reason))
            {
                runtime.DisableInteractions();
                _log($"Syndicate HQ storage remained inert at native load: {reason}");
                return;
            }

            var admission = runtime.BeginLoad(snapshot);
#if DEBUG
            _receiptLog($"Syndicate HQ storage receipt: admission result={admission.Status}; reason={admission.Reason}");
#endif
            if (admission.Status != SyndicateHqStorageStatus.NotPrepared)
            {
                runtime.DisableInteractions();
                _log($"Syndicate HQ storage load admission was {admission.Status}: {admission.Reason}");
                return;
            }

            var result = runtime.PrepareAtVanillaLoadBoundary();
            _loadCyclePrepared = result.Status == SyndicateHqStorageStatus.NotPrepared;
#if DEBUG
            _receiptLog($"Syndicate HQ storage receipt: grid preparation result={result.Status}; prepared={_loadCyclePrepared}; reason={result.Reason}");
#endif
            if (!_loadCyclePrepared)
                _log($"Syndicate HQ storage remained inert at native load: {result.Status} — {result.Reason}");
        }
        catch (Exception ex)
        {
            runtime.DisableInteractions();
            _log($"Syndicate HQ storage-load prefix failed closed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
