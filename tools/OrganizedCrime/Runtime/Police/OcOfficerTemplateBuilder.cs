using Il2CppFishNet;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Law;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Actions;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.NPCs.Framework;
using Il2CppScheduleOne.NPCs.Responses;
using Il2CppScheduleOne.Police;
using Il2CppScheduleOne.VoiceOver;
using OrganizedCrime.Core.Time;
using OrganizedCrime.Runtime.Platform;
using UnityEngine;
using Avatar = Il2CppScheduleOne.AvatarFramework.Avatar;
using Object = UnityEngine.Object;

using OfficerTemplate = OrganizedCrime.Runtime.Police.OcOfficerWorldContext.OfficerTemplate;

namespace OrganizedCrime.Runtime.Police;

internal sealed class OcOfficerTemplateBuilder
{
    private const long StableSourceMs = 5000;
    private readonly OcOfficerWorldContext _context;

    internal OcOfficerTemplateBuilder(OcOfficerWorldContext context)
    {
        _context = context;
    }

    public OcOfficerWorldStatus TryEnsureTemplate(string templateId, out OcOfficerTemplateStatus status, out string detail)
    {
        status = new OcOfficerTemplateStatus(Present: false, Registered: false, PrefabId: 0, TemplateId: null);
        detail = string.Empty;
        try
        {
            if (!_context.IsHost)
            {
                detail = "not host";
                return OcOfficerWorldStatus.NotHost;
            }
            if (TemplateValid(out status))
            {
                detail = $"template PrefabId {status.PrefabId} still registered";
                return OcOfficerWorldStatus.Ok;
            }
            DestroyTemplateObject();
            if (!TryFindSource(out var source, out var sourceId, out var sourceRoot, out var findDetail))
            {
                detail = findDetail;
                return OcOfficerWorldStatus.NotReady;
            }
            if (!TryConfirmStableSource(sourceId, sourceRoot, out var stableDetail))
            {
                detail = stableDetail;
                return OcOfficerWorldStatus.NotReady;
            }
            if (!TryBuildTemplate(source, sourceId, sourceRoot, templateId, out status, out detail))
                return OcOfficerWorldStatus.Failed;
            detail = $"template PrefabId {status.PrefabId} from {sourceId}; {DetailSuffix(detail)}";
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer template failed ({exception.GetType().Name}).");
            DestroyTemplateObject();
            detail = $"template failed ({exception.GetType().Name})";
            return OcOfficerWorldStatus.Failed;
        }
    }

    public OcOfficerWorldStatus TryDestroyTemplate(out string detail)
    {
        detail = "template absent";
        try
        {
            // Review fix (owner run 1): no host gate. The template is a local
            // DontDestroyOnLoad object, and pre-load runs before the server is up
            // (OC-108), so a host gate here leaked the template across loads.
            DestroyTemplateObject();
            // FishNet offers no single-entry removal: the numeric registration
            // stays behind, but nothing ever spawns from it again.
            detail = "template destroyed; its numeric spawnable registration stays unreferenced";
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer template destroy failed ({exception.GetType().Name}).");
            detail = $"template destroy failed ({exception.GetType().Name})";
            return OcOfficerWorldStatus.Failed;
        }
    }

    public OcOfficerWorldStatus TryReadTemplateStatus(out OcOfficerTemplateStatus status)
    {
        status = new OcOfficerTemplateStatus(Present: false, Registered: false, PrefabId: 0, TemplateId: null);
        try
        {
            if (!_context.IsHost)
                return OcOfficerWorldStatus.NotHost;
            TemplateValid(out status);
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer template read failed ({exception.GetType().Name}).");
            return OcOfficerWorldStatus.Failed;
        }
    }

    internal bool TemplateValid(out OcOfficerTemplateStatus status)
    {
        status = new OcOfficerTemplateStatus(Present: false, Registered: false, PrefabId: 0, TemplateId: null);
        var template = _context._template;
        if (template is null)
            return false;
        try
        {
            if (template.Root == null || template.Network == null || template.Officer == null)
                return false;
            var spawnables = InstanceFinder.NetworkManager?.SpawnablePrefabs;
            if (spawnables is null)
                return false;
            var serverOk = spawnables.GetObject(true, template.PrefabId) == template.Network;
            var clientOk = spawnables.GetObject(false, template.PrefabId) == template.Network;
            status = new OcOfficerTemplateStatus(Present: true, Registered: serverOk && clientOk, template.PrefabId, template.TemplateId);
            return serverOk && clientOk;
        }
        catch
        {
            return false;
        }
    }

    private void DestroyTemplateObject()
    {
        var template = _context._template;
        _context._template = null;
        if (template is null)
            return;
        try
        {
            var root = template.Root;
            if (root != null)
                Object.Destroy(root);
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer template object destroy failed ({exception.GetType().Name}).");
        }
    }

    private bool TryConfirmStableSource(string sourceId, GameObject sourceRoot, out string detail)
    {
        detail = string.Empty;
        long rootPointer;
        try { rootPointer = sourceRoot.Pointer.ToInt64(); }
        catch
        {
            detail = "source root unreadable";
            return false;
        }
        var now = Environment.TickCount64;
        if (_context._stableRoot != rootPointer || _context._stableId != sourceId)
        {
            _context._stableRoot = rootPointer;
            _context._stableId = sourceId;
            _context._stableSinceMs = now;
            detail = $"source {sourceId} stabilizing (0s of 5s)";
            return false;
        }
        var waitedMs = now - _context._stableSinceMs;
        if (waitedMs < StableSourceMs)
        {
            detail = $"source {sourceId} stabilizing ({waitedMs / 1000}s of 5s)";
            return false;
        }
        return true;
    }

    internal OcOfficerWorldStatus FaultTemplate(OfficerTemplate template, string detail, out string detailOut)
    {
        _context._faultedSourceId = template.SourceId;
        detailOut = detail;
        return OcOfficerWorldStatus.TemplateFault;
    }

    private bool TryFindSource(out PoliceOfficer source, out string sourceId, out GameObject root, out string detail)
    {
        source = null!;
        sourceId = string.Empty;
        root = null!;
        detail = string.Empty;
        var scanned = 0;
        var pooled = 0;
        var hidden = 0;
        var unreadable = 0;
        var rootless = 0;
        PoliceOfficer? fallbackSource = null;
        string? fallbackId = null;
        GameObject? fallbackRoot = null;
        var officers = PoliceOfficer.Officers;
        if (officers is null)
        {
            detail = "native Officers list is unavailable";
            return false;
        }
        var templatePointer = TemplateOfficerPointer();
        for (var i = 0; i < officers.Count; i++)
        {
            PoliceOfficer candidate;
            try { candidate = officers[i]; }
            catch { unreadable++; continue; }
            if (candidate == null)
            {
                unreadable++;
                continue;
            }
            long pointer;
            try { pointer = candidate.Pointer.ToInt64(); }
            catch { unreadable++; continue; }
            scanned++;
            if (_context._ownPointers.Contains(pointer) || (templatePointer.HasValue && templatePointer.Value == pointer))
                continue;
            if (IsPooled(pointer))
            {
                pooled++;
                continue;
            }
            if (!IsAvatarActive(candidate))
            {
                hidden++;
                continue;
            }
            string id;
            try { id = candidate.ID; }
            catch { unreadable++; continue; }
            if (string.IsNullOrWhiteSpace(id))
            {
                unreadable++;
                continue;
            }
            if (!HasAppearanceData(candidate))
            {
                unreadable++;
                continue;
            }
            if (!TryChooseCloneRoot(candidate, out var candidateRoot))
            {
                rootless++;
                continue;
            }
            if (!string.IsNullOrEmpty(_context._faultedSourceId) && id == _context._faultedSourceId)
            {
                fallbackSource = candidate;
                fallbackId = id;
                fallbackRoot = candidateRoot;
                continue;
            }
            source = candidate;
            sourceId = id;
            root = candidateRoot;
            detail = $"source {id} ({scanned} scanned, {pooled} pooled, {hidden} avatar-inactive, {unreadable} unreadable, {rootless} rootless)";
            return true;
        }
        if (fallbackSource is not null && fallbackRoot is not null && !string.IsNullOrEmpty(fallbackId))
        {
            source = fallbackSource;
            sourceId = fallbackId;
            root = fallbackRoot;
            detail = $"source {fallbackId} (only faulted source available; {scanned} scanned, {pooled} pooled, {hidden} avatar-inactive, {unreadable} unreadable, {rootless} rootless)";
            return true;
        }
        detail = $"no usable source ({scanned} scanned, {pooled} pooled, {hidden} avatar-inactive, {unreadable} unreadable, {rootless} rootless)";
        return false;
    }

    private long? TemplateOfficerPointer()
    {
        try
        {
            var officer = _context._template?.Officer;
            return officer is null || officer == null ? null : officer.Pointer.ToInt64();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPooled(long pointer)
    {
        try
        {
            var stations = PoliceStation.PoliceStations;
            if (stations is null)
                return true;
            for (var i = 0; i < stations.Count; i++)
            {
                PoliceStation station;
                try { station = stations[i]; }
                catch { return true; }
                if (station == null)
                    continue;
                Il2CppSystem.Collections.Generic.List<PoliceOfficer>? pool;
                try { pool = station.OfficerPool; }
                catch { return true; }
                if (pool is null)
                    continue;
                for (var j = 0; j < pool.Count; j++)
                {
                    try
                    {
                        var member = pool[j];
                        if (member != null && member.Pointer.ToInt64() == pointer)
                            return true;
                    }
                    catch
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsAvatarActive(PoliceOfficer officer)
    {
        try
        {
            var avatars = officer.gameObject.GetComponentsInChildren<Avatar>(true);
            return avatars.Length != 0 && avatars[0] != null && avatars[0].gameObject.activeInHierarchy;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasAppearanceData(PoliceOfficer officer)
    {
        try
        {
            return officer._NPCData_k__BackingField?.Appearance is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryChooseCloneRoot(PoliceOfficer source, out GameObject root)
    {
        root = null!;
        try
        {
            if (source.Movement == null)
                return false;
            var candidate = source.transform;
            foreach (var component in OfficerReferences(source))
            {
                if (component == null)
                    continue;
                while (candidate != null && component.transform != candidate && !component.transform.IsChildOf(candidate))
                    candidate = candidate.parent;
                if (candidate == null)
                    return false;
            }
            var npcs = candidate.GetComponentsInChildren<NPC>(true);
            var stations = candidate.GetComponentsInChildren<PoliceStation>(true);
            if (npcs.Length != 1 || npcs[0] == null || npcs[0].Pointer != source.Pointer || stations.Length != 0)
                return false;
            root = candidate.gameObject;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static Component?[] OfficerReferences(PoliceOfficer officer) => new Component?[]
    {
        officer.Movement,
        officer.FootPatrolBehaviour,
        officer.CheckpointBehaviour,
        officer.SentryBehaviour,
        officer.VehiclePatrolBehaviour,
        officer.PursuitBehaviour,
        officer.BodySearchBehaviour
    };

    private bool TryBuildTemplate(PoliceOfficer source, string sourceId, GameObject sourceRoot, string templateId, out OcOfficerTemplateStatus status, out string detail)
    {
        status = new OcOfficerTemplateStatus(Present: false, Registered: false, PrefabId: 0, TemplateId: null);
        detail = string.Empty;
        GameObject? templateRoot = null;
        try
        {
            var staging = new GameObject("OC_OfficerTemplateStaging");
            staging.SetActive(false);
            try
            {
                templateRoot = Object.Instantiate(sourceRoot, staging.transform, false);
                templateRoot.SetActive(false);
                templateRoot.transform.SetParent(null, true);
            }
            finally
            {
                try { Object.Destroy(staging); }
                catch { }
            }
            templateRoot.name = "OC_OfficerTemplate";
            templateRoot.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(templateRoot);
            var officers = templateRoot.GetComponentsInChildren<PoliceOfficer>(true);
            if (officers.Length != 1 || officers[0] == null)
            {
                detail = $"template copy holds {officers.Length} officers, expected 1";
                return false;
            }
            // Keep the registered client template free of copied runtime equipment too.
            OcOfficerNativeLifetime.StripCopies(templateRoot, officers[0]);
            if (!OcOfficerSpawner.TrySelectTopNetwork(templateRoot, officers[0], out var network))
            {
                detail = "template has no single top NetworkObject";
                return false;
            }
            foreach (var member in templateRoot.GetComponentsInChildren<NetworkObject>(true))
                member.SceneId = 0;
            var spawnables = InstanceFinder.NetworkManager?.SpawnablePrefabs;
            if (spawnables is null)
            {
                detail = "NetworkManager.SpawnablePrefabs unavailable";
                return false;
            }
            var collectionId = spawnables.CollectionId;
            network.SpawnableCollectionId = collectionId;
            spawnables.AddObject(network, true);
            var prefabId = network.PrefabId;
            if (!PrefabResolves(spawnables, false, prefabId, network))
            {
                try { spawnables.AddObject(network, false); }
                catch (Exception exception)
                {
                    detail = $"template client registration threw ({exception.GetType().Name})";
                    return false;
                }
                prefabId = network.PrefabId;
            }
            if (!PrefabResolves(spawnables, true, prefabId, network) || !PrefabResolves(spawnables, false, prefabId, network))
            {
                detail = $"template PrefabId {prefabId} does not resolve on both peers";
                return false;
            }
            _context._template = new OfficerTemplate(templateRoot, officers[0], network, prefabId, collectionId, templateId, sourceId);
            _context._stableRoot = null;
            _context._stableId = null;
            _context._faultedSourceId = null;
            templateRoot = null;
            status = new OcOfficerTemplateStatus(Present: true, Registered: true, prefabId, templateId);
            TryWriteTemplateId(_context._template.Officer, source, templateId, out detail);
            return true;
        }
        catch (Exception exception)
        {
            detail = $"template build failed ({exception.GetType().Name}: {exception.Message})";
            return false;
        }
        finally
        {
            // Managed check: Unity's == needs the IL2CPP runtime, which a
            // finally block cannot assume, so it never runs here.
            if (templateRoot is not null)
            {
                DestroyTemplateObject();
                try { Object.DestroyImmediate(templateRoot); }
                catch { }
            }
        }
    }

    private static bool PrefabResolves(Il2CppFishNet.Managing.Object.PrefabObjects spawnables, bool asServer, ushort prefabId, NetworkObject network)
    {
        try
        {
            return spawnables.GetObject(asServer, prefabId) == network;
        }
        catch
        {
            return false;
        }
    }

    private bool TryWriteTemplateId(PoliceOfficer templateOfficer, PoliceOfficer source, string templateId, out string detail)
    {
        detail = string.Empty;
        try
        {
            var templateData = templateOfficer._NPCData_k__BackingField;
            var sourceData = source._NPCData_k__BackingField;
            if (templateData is null || sourceData is null)
            {
                detail = "runtime NPCData unreadable; template ID left as-is";
                return false;
            }
            if (templateData.Pointer == sourceData.Pointer)
            {
                detail = "template shares the source NPCData; template ID left as-is";
                return false;
            }
            var templateBasic = templateData.BasicInfo;
            var sourceBasic = sourceData.BasicInfo;
            if (templateBasic is null || sourceBasic is null)
            {
                detail = "runtime BasicInfo unreadable; template ID left as-is";
                return false;
            }
            if (templateBasic.Pointer == sourceBasic.Pointer)
            {
                detail = "template shares the source BasicInfo; template ID left as-is";
                return false;
            }
            templateBasic.ID = templateId;
            var readBack = templateBasic.ID;
            string sourceId;
            try { sourceId = sourceBasic.ID; }
            catch { sourceId = "unreadable"; }
            detail = $"template ID {readBack}; source still {sourceId}";
            return readBack == templateId;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer template ID write failed ({exception.GetType().Name}).");
            detail = $"template ID write failed ({exception.GetType().Name})";
            return false;
        }
    }

    private static string DetailSuffix(string detail) => detail.Length <= 120 ? detail : detail[..120];
}
