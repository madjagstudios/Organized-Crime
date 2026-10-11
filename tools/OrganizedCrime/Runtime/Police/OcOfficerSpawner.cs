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


namespace OrganizedCrime.Runtime.Police;

internal sealed class OcOfficerSpawner
{
    private readonly OcOfficerWorldContext _context;
    private readonly OcOfficerTemplateBuilder _templates;

    internal OcOfficerSpawner(OcOfficerWorldContext context, OcOfficerTemplateBuilder templates)
    {
        _context = context;
        _templates = templates;
    }

    public OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string cloneId, out OcOfficerCloneRef clone, out string detail)
    {
        clone = default;
        detail = string.Empty;
        GameObject? instance = null;
        PoliceOfficer? preparedOfficer = null;
        try
        {
            if (!_context.IsHost)
            {
                detail = "not host";
                return OcOfficerWorldStatus.NotHost;
            }
            var template = _context._template;
            if (template is null || !_templates.TemplateValid(out _))
            {
                detail = "template invalid";
                return OcOfficerWorldStatus.NotReady;
            }
            instance = Object.Instantiate(template.Root);
            instance.SetActive(false);
            instance.name = cloneId;
            var officers = instance.GetComponentsInChildren<PoliceOfficer>(true);
            if (officers.Length != 1 || officers[0] == null)
            {
                return _templates.FaultTemplate(template, $"template instance holds {officers.Length} officers, expected 1", out detail);
            }
            var officer = officers[0];
            preparedOfficer = officer;
            var removedAttachments = 0;
            try
            {
                OcOfficerCloneLifetime.Prepare(
                    () => OcOfficerNativeLifetime.StripCopies(instance, officer),
                    () =>
                    {
                        OcOfficerNativeLifetime.BindDependencies(instance, officer);
                        BindMovement(instance, officer);
                        BindSpeedController(instance, officer, template.Officer);
                        WakeTargets(instance, officer);
                    },
                    () => OcOfficerNativeLifetime.FreshGuid(officer, _context._ownGuids));
                removedAttachments = OcOfficerAttachmentCleanup.RemoveBeforeActivation(instance, officer);
                ValidateOwned(instance, officer);
            }
            catch (Exception exception)
            {
                return _templates.FaultTemplate(template, $"pre-activation binding failed ({exception.Message}); {OcOfficerDiagnostics.SnapshotApplyInputs(officer)}", out detail);
            }
            instance.transform.position = new Vector3(position.X, position.Y, position.Z);
            instance.transform.rotation = Quaternion.Euler(0f, position.YawDegrees, 0f);
            if (!TrySelectTopNetwork(instance, officer, out var network))
            {
                return _templates.FaultTemplate(template, $"no single top NetworkObject; {OcOfficerDiagnostics.SnapshotApplyInputs(officer)}", out detail);
            }
            var probe = OcOfficerDiagnostics.SnapshotApplyInputs(officer);
            try
            {
                // OC-174: OnTick can recycle an idle clone before delayed patrol binding.
                // This flag belongs to the clone lifetime, including unbound intervals.
                officer.AutoDeactivate = false;
                instance.SetActive(true);
            }
            catch (Exception exception)
            {
                return _templates.FaultTemplate(template, $"activation threw ({exception.GetType().Name}); {probe}", out detail);
            }
            var problems = ValidateAwake(officer);
            var dependencies = OcOfficerNativeLifetime.Dependencies(instance, officer, awaitingCombatStart: true);
            if (!dependencies.StartsWith("dependencies: ok;", StringComparison.Ordinal)) problems.Add(dependencies);
            if (problems.Count > 0)
            {
                return _templates.FaultTemplate(template, $"awake validation: {string.Join("; ", problems)}; {probe}", out detail);
            }
            if (!TryAssignCloneId(officer, template.Officer, cloneId, out var idDetail))
            {
                RemoveSpawnedClone(officer, instance, network);
                instance = null;
                detail = idDetail;
                return OcOfficerWorldStatus.Failed;
            }
            RemoveFromStationPools(officer.Pointer.ToInt64());
            try
            {
                InstanceFinder.ServerManager.Spawn(network);
            }
            catch (Exception exception)
            {
                RemoveSpawnedClone(officer, instance, network);
                instance = null;
                detail = $"server spawn threw ({exception.GetType().Name})";
                return OcOfficerWorldStatus.Failed;
            }
            if (!network.IsSpawned)
            {
                RemoveSpawnedClone(officer, instance, network);
                instance = null;
                detail = "server spawn did not flag IsSpawned";
                return OcOfficerWorldStatus.Failed;
            }
            _context._serial++;
            var pointer = officer.Pointer.ToInt64();
            _context._ownPointers.Add(pointer);
            _context._ownCloneIds[pointer] = cloneId;
            clone = new OcOfficerCloneRef(officer, pointer, _context._serial);
            _context._ownClones[pointer] = clone;
            _context._ownCloneRoots[pointer] = instance;
            // One receipt per successfully spawned clone, never per frame or rebind.
            try { OrganizedCrimeLog.Receipt($"OC officer clone {cloneId}: removed {removedAttachments} unparented template attachments."); }
            catch { }
            instance = null;
            detail = $"spawned {cloneId} from template PrefabId {network.PrefabId}; {idDetail}";
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer spawn failed ({exception.GetType().Name}).");
            detail = $"spawn failed ({exception.GetType().Name})";
            return OcOfficerWorldStatus.Failed;
        }
        finally
        {
            // Managed guard; native cleanup is best effort and separately contained.
            if (instance is not null)
            {
                try
                {
                    if (preparedOfficer is not null && UnityLife.IsAlive(instance) && UnityLife.IsAlive(preparedOfficer))
                        OcOfficerNativeLifetime.Quiesce(instance, preparedOfficer);
                }
                catch (Exception exception) { _context._warn($"OC officer abandoned instance quiescence failed ({exception.GetType().Name})."); }
                if (preparedOfficer is not null)
                    _context._ownGuids.Release(preparedOfficer.Pointer.ToInt64(), OcOfficerNativeLifetime.DeregisterGuid);
                try { Object.DestroyImmediate(instance); }
                catch (Exception exception) { _context._warn($"OC officer abandoned instance destroy failed ({exception.GetType().Name})."); }
            }
        }
    }

    public OcOfficerWorldStatus TryRemoveClone(OcOfficerCloneRef clone, out string detail)
    {
        detail = string.Empty;
        try
        {
            if (!_context.IsHost)
            {
                detail = "not host";
                return OcOfficerWorldStatus.NotHost;
            }
            var pointer = clone.Pointer;
            if (!_context._ownPointers.Contains(pointer))
            {
                detail = "clone is not owned by this world";
                return OcOfficerWorldStatus.InvalidHandle;
            }
            var alive = TryGetOfficer(clone, out var officer) && officer is not null;
            var root = _context._ownCloneRoots.GetValueOrDefault(pointer);
            _context._ownGuids.Release(pointer, OcOfficerNativeLifetime.DeregisterGuid);
            // Unbind first: disable the CheckpointBehaviour so the native
            // Deactivate removes the clone from our checkpoint's AssignedNPCs
            // and its walk-through listener while both are alive, then unmap.
            var unbindNote = OcOfficerDutyBinder.UnbindCloneForRemove(alive ? officer : null, pointer);
            RemoveFromOfficers(pointer);
            RemoveFromRegistry(pointer);
            RemoveFromStationPools(pointer);
            if (alive)
                RemoveFromOwnCheckpoint(officer!);
            if (alive)
            {
                if (!UnityLife.IsAlive(root))
                {
                    RetireMissingRoot(clone, officer!);
                    detail = "owned clone retained root unavailable; retired remaining officer and released ownership";
                    return OcOfficerWorldStatus.InvalidHandle;
                }
                OcOfficerNativeLifetime.Quiesce(root!, officer!);
            }
            var despawnNote = "not spawned";
            NetworkObject? network = null;
            if (alive)
            {
                try { OcOfficerSpawner.TrySelectTopNetwork(root!, officer!, out network); }
                catch { network = null; }
            }
            if (network != null)
            {
                var spawned = false;
                try { spawned = network.IsSpawned; }
                catch { spawned = false; }
                if (spawned)
                {
                    try
                    {
                        InstanceFinder.ServerManager.Despawn(network, new Il2CppSystem.Nullable<DespawnType>(DespawnType.Destroy));
                        despawnNote = "despawned";
                    }
                    catch (Exception exception)
                    {
                        despawnNote = $"despawn threw ({exception.GetType().Name})";
                    }
                }
            }
            if (alive)
            {
                try
                {
                    if (root != null)
                        Object.Destroy(root);
                }
                catch (Exception exception)
                {
                    despawnNote += $"; destroy threw ({exception.GetType().Name})";
                }
            }
            _context._ownPointers.Remove(pointer);
            _context._ownCloneIds.Remove(pointer);
            _context._ownClones.Remove(pointer);
            _context._ownCloneRoots.Remove(pointer);
            RemoveFromOfficers(pointer);
            RemoveFromRegistry(pointer);
            var officersLeft = OcOfficerDiagnostics.CountPointer(PoliceOfficer.Officers, pointer);
            var registryLeft = OcOfficerDiagnostics.CountPointer(NPCManager.NPCRegistry, pointer);
            detail = alive
                ? $"removed ({despawnNote}; {unbindNote}); Officers left {officersLeft}, NPCRegistry left {registryLeft}"
                : $"already destroyed ({unbindNote}); Officers left {officersLeft}, NPCRegistry left {registryLeft}";
            if (officersLeft > 0 || registryLeft > 0)
                _context._warn($"OC officer removal left Officers {officersLeft}, NPCRegistry {registryLeft}.");
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer removal failed ({exception.GetType().Name}).");
            detail = $"removal failed ({exception.GetType().Name})";
            return OcOfficerWorldStatus.Failed;
        }
    }

    internal void ClearOwnedGuids() => _context._ownGuids.Clear(OcOfficerNativeLifetime.DeregisterGuid);

    private void RetireMissingRoot(OcOfficerCloneRef clone, PoliceOfficer officer) =>
        OcOfficerCloneLifetime.RetireMissingRoot(
            () =>
            {
                var remaining = officer.gameObject;
                try { OcOfficerNativeLifetime.Quiesce(remaining, officer); }
                finally { remaining.SetActive(false); }
            },
            () =>
            {
                var remaining = officer.gameObject;
                if (TrySelectTopNetwork(remaining, officer, out var network) && network.IsSpawned)
                    InstanceFinder.ServerManager.Despawn(network, new Il2CppSystem.Nullable<DespawnType>(DespawnType.Destroy));
            },
            () => Object.Destroy(officer.gameObject),
            () =>
            {
                _context._ownPointers.Remove(clone.Pointer);
                _context._ownCloneIds.Remove(clone.Pointer);
                _context._ownClones.Remove(clone.Pointer);
                _context._ownCloneRoots.Remove(clone.Pointer);
            });

    private static void BindMovement(GameObject root, PoliceOfficer officer)
    {
        if (officer.Movement != null)
            return;
        if (root.activeInHierarchy || officer.gameObject.activeInHierarchy)
            throw new InvalidOperationException("Movement binding requires an inactive clone.");
        var candidates = root.GetComponentsInChildren<NPCMovement>(true);
        if (candidates.Length != 1 || candidates[0] == null || !IsInside(root, candidates[0]))
            throw new InvalidOperationException($"Inactive clone needs exactly one owned NPCMovement (found {candidates.Length}).");
        officer._Movement_k__BackingField = candidates[0];
        if (officer.Movement != candidates[0])
            throw new InvalidOperationException("Cloned NPC.Movement did not read back.");
    }

    private static void BindSpeedController(GameObject root, PoliceOfficer officer, PoliceOfficer templateOfficer)
    {
        var movement = officer.Movement;
        if (movement == null)
            throw new InvalidOperationException("Speed controller binding requires bound NPCMovement.");
        if (movement._speedController is not null)
            return;
        if (root.activeInHierarchy || officer.gameObject.activeInHierarchy)
            throw new InvalidOperationException("Speed controller binding requires an inactive clone.");
        // f9 removed the parameterless constructor: seed from the template's
        // live controller, then copy every float across so the constructor
        // argument's meaning never matters.
        var moveSpeed = 2f;
        var maxSpeed = 2f;
        var multiplier = 1f;
        try
        {
            var templateController = templateOfficer.Movement?._speedController;
            if (templateController is not null)
            {
                if (float.IsFinite(templateController.MoveSpeed) && templateController.MoveSpeed > 0f)
                    moveSpeed = templateController.MoveSpeed;
                if (float.IsFinite(templateController._maxSpeed) && templateController._maxSpeed > 0f)
                    maxSpeed = templateController._maxSpeed;
                if (float.IsFinite(templateController._speedMultiplier) && templateController._speedMultiplier > 0f)
                    multiplier = templateController._speedMultiplier;
            }
        }
        catch
        {
        }
        NPCSpeedController controller;
        try
        {
            controller = new NPCSpeedController(moveSpeed);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"NPCSpeedController construction failed ({exception.GetType().Name}).");
        }
        movement._speedController = controller;
        controller._maxSpeed = maxSpeed;
        controller._speedMultiplier = multiplier;
        controller.MoveSpeed = moveSpeed;
        if (movement._speedController is null || movement._speedController.Pointer != controller.Pointer)
            throw new InvalidOperationException("Cloned NPCMovement._speedController did not read back.");
    }

    // Every reference NPC.GetAndValidateReferences resolves with an
    // active-only GetComponentInChildren (f9 ISIL). One left on an inactive
    // child stays null and ApplyNPCData throws in Awake; VOEmitter was the
    // missing one behind OC-141's intermittent template faults.
    private static void WakeTargets(GameObject root, PoliceOfficer officer)
    {
        OcOfficerDutyBinder.PrepareCopiedBehaviours(root);
        WakeTarget<DialogueHandler>(root, officer);
        WakeTarget<Avatar>(root, officer);
        WakeTarget<NPCAwareness>(root, officer);
        WakeTarget<NPCResponses>(root, officer);
        WakeTarget<NPCActions>(root, officer);
        WakeTarget<NPCBehaviour>(root, officer);
        WakeTarget<VOEmitter>(root, officer);
        if (root.activeInHierarchy)
            throw new InvalidOperationException("Clone root is active after waking children.");
    }

    private static void WakeTarget<T>(GameObject root, PoliceOfficer officer) where T : Component
    {
        var found = officer.gameObject.GetComponentsInChildren<T>(true);
        foreach (var component in found)
        {
            if (component == null)
                continue;
            for (var current = component.transform;
                 current != null && current != root.transform && current.IsChildOf(root.transform);
                 current = current.parent)
            {
                if (!current.gameObject.activeSelf)
                    current.gameObject.SetActive(true);
            }
        }
    }

    private static void ValidateOwned(GameObject root, PoliceOfficer officer)
    {
        foreach (var component in OcOfficerTemplateBuilder.OfficerReferences(officer))
        {
            if (!IsInside(root, component))
                throw new InvalidOperationException("Officer movement or behaviour points outside the cloned shell.");
        }
    }

    private static bool IsInside(GameObject root, Component? component) =>
        component == null || component.transform == root.transform || component.transform.IsChildOf(root.transform);

    internal static bool TrySelectTopNetwork(GameObject root, PoliceOfficer officer, out NetworkObject top)
    {
        top = null!;
        try
        {
            var networks = root.GetComponentsInChildren<NetworkObject>(true);
            NetworkObject? selected = null;
            foreach (var network in networks)
            {
                if (network == null)
                    continue;
                var parent = NearestNetworkAncestor(network.transform.parent);
                if (parent == null || !IsInside(root, parent))
                {
                    if (selected != null)
                        return false;
                    selected = network;
                }
            }
            if (selected == null || !IsInside(selected.gameObject, officer))
                return false;
            top = selected;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static NetworkObject? NearestNetworkAncestor(Transform? transform)
    {
        for (var current = transform; current != null; current = current.parent)
        {
            NetworkObject? network;
            try { network = current.GetComponent<NetworkObject>(); }
            catch { continue; }
            if (network != null)
                return network;
        }
        return null;
    }

    private List<string> ValidateAwake(PoliceOfficer officer)
    {
        var problems = new List<string>();
        try
        {
            string id;
            try { id = officer.ID; }
            catch (Exception exception)
            {
                problems.Add($"ID unreadable ({exception.GetType().Name})");
                return problems;
            }
            if (string.IsNullOrWhiteSpace(id))
                problems.Add("ID empty after activation");
            var probe = OcOfficerDiagnostics.SnapshotApplyInputs(officer);
            if (!probe.StartsWith("apply-inputs: all present", StringComparison.Ordinal))
                problems.Add(probe);
            long pointer;
            try { pointer = officer.Pointer.ToInt64(); }
            catch
            {
                problems.Add("pointer unreadable after activation");
                return problems;
            }
            if (OcOfficerDiagnostics.CountPointer(PoliceOfficer.Officers, pointer) == 0)
                problems.Add("clone did not self-register in Officers");
            if (OcOfficerDiagnostics.CountPointer(NPCManager.NPCRegistry, pointer) == 0)
                problems.Add("clone did not self-register in NPCRegistry");
        }
        catch (Exception exception)
        {
            problems.Add($"validation threw ({exception.GetType().Name})");
        }
        return problems;
    }

    private bool TryAssignCloneId(PoliceOfficer officer, PoliceOfficer templateOfficer, string cloneId, out string detail)
    {
        detail = string.Empty;
        try
        {
            if (templateOfficer == null)
            {
                detail = "template officer is gone; unique ID failed";
                return false;
            }
            string templateIdBefore;
            try { templateIdBefore = templateOfficer.ID; }
            catch { templateIdBefore = string.Empty; }
            var cloneData = officer._NPCData_k__BackingField;
            var templateData = templateOfficer._NPCData_k__BackingField;
            if (cloneData is null)
            {
                detail = "runtime NPCData unavailable; unique ID failed";
                return false;
            }
            // The template is never activated, so its runtime NPCData is null.
            // A null template can't share data with the clone, so the clone's own
            // data is distinct and safe to write; only a real pointer match forks.
            var forked = false;
            if (templateData is not null && cloneData.Pointer == templateData.Pointer)
            {
                // The run-13 case: a template spawn shares the template's
                // runtime data because the template was never activated. Fork
                // a private deep copy instead of writing into shared data.
                NPCData fork;
                try
                {
                    fork = cloneData.GetDeepCopy();
                }
                catch (Exception exception)
                {
                    detail = $"NPCData fork failed ({exception.GetType().Name}); unique ID failed";
                    return false;
                }
                if (fork is null || fork.Pointer == templateData.Pointer)
                {
                    detail = "NPCData fork did not detach; unique ID failed";
                    return false;
                }
                officer._NPCData_k__BackingField = fork;
                if (officer._NPCData_k__BackingField is null || officer._NPCData_k__BackingField.Pointer != fork.Pointer)
                {
                    detail = "NPCData fork did not read back; unique ID failed";
                    return false;
                }
                cloneData = fork;
                forked = true;
            }
            var cloneBasic = cloneData.BasicInfo;
            var templateBasic = templateData?.BasicInfo;
            if (cloneBasic is null)
            {
                detail = "runtime BasicInfo unavailable; unique ID failed";
                return false;
            }
            // With the template check skipped for a null template, make
            // sure no other live officer shares this data, so the ID write can never
            // rename a native officer.
            foreach (var other in PoliceOfficer.Officers)
            {
                if (other == null || other.Pointer == officer.Pointer) continue;
                var otherData = other._NPCData_k__BackingField;
                if (otherData is not null && (otherData.Pointer == cloneData.Pointer ||
                    (otherData.BasicInfo is { } otherBasic && otherBasic.Pointer == cloneBasic.Pointer)))
                {
                    detail = "clone shares runtime data with a live officer; unique ID failed";
                    return false;
                }
            }
            if (templateBasic is not null && cloneBasic.Pointer == templateBasic.Pointer)
            {
                detail = "clone shares the template BasicInfo; unique ID failed";
                return false;
            }
            cloneBasic.ID = cloneId;
            string cloneReadBack;
            try { cloneReadBack = officer.ID; }
            catch
            {
                detail = "clone ID unreadable after write; unique ID failed";
                return false;
            }
            if (cloneReadBack != cloneId)
            {
                detail = $"clone ID reads {cloneReadBack}; unique ID failed";
                return false;
            }
            var registered = false;
            try
            {
                var byNew = NPCManager.GetNPC(cloneId);
                registered = byNew != null && byNew.Pointer == officer.Pointer;
            }
            catch
            {
                registered = false;
            }
            if (!registered)
            {
                detail = $"GetNPC({cloneId}) is not the clone; unique ID failed";
                return false;
            }
            if (!string.IsNullOrEmpty(templateIdBefore))
            {
                string templateIdAfter;
                try { templateIdAfter = templateOfficer.ID; }
                catch { templateIdAfter = string.Empty; }
                if (templateIdAfter != templateIdBefore)
                {
                    detail = "template ID changed by the clone ID write; unique ID failed";
                    return false;
                }
            }
            detail = forked ? "forked private NPCData; unique ID set" : "distinct runtime NPCData; unique ID set";
            return true;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer unique ID failed ({exception.GetType().Name}).");
            detail = $"unique ID failed ({exception.GetType().Name})";
            return false;
        }
    }

    private void RemoveSpawnedClone(PoliceOfficer officer, GameObject root, NetworkObject network)
    {
        _context._ownGuids.Release(officer.Pointer.ToInt64(), OcOfficerNativeLifetime.DeregisterGuid);
        OcOfficerNativeLifetime.Quiesce(root, officer);
        long pointer;
        try { pointer = officer.Pointer.ToInt64(); }
        catch { pointer = 0; }
        try
        {
            if (network != null && network.IsSpawned)
                InstanceFinder.ServerManager.Despawn(network, new Il2CppSystem.Nullable<DespawnType>(DespawnType.Destroy));
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer failed-spawn despawn threw ({exception.GetType().Name}).");
        }
        try
        {
            if (root != null)
                Object.DestroyImmediate(root);
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer failed-spawn destroy threw ({exception.GetType().Name}).");
        }
        if (pointer != 0)
        {
            RemoveFromOfficers(pointer);
            RemoveFromRegistry(pointer);
            RemoveFromStationPools(pointer);
        }
    }

    private static void RemoveFromStationPools(long pointer)
    {
        try
        {
            var stations = PoliceStation.PoliceStations;
            if (stations is null)
                return;
            for (var i = 0; i < stations.Count; i++)
            {
                PoliceStation station;
                try { station = stations[i]; }
                catch { continue; }
                if (station == null)
                    continue;
                try
                {
                    RemovePointer(station.OfficerPool, pointer);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static void RemoveFromOwnCheckpoint(PoliceOfficer officer)
    {
        try
        {
            var checkpoint = officer.CheckpointBehaviour?.Checkpoint;
            if (checkpoint == null)
                return;
            RemovePointer(checkpoint.AssignedNPCs, officer.Pointer.ToInt64());
        }
        catch
        {
        }
    }

    private static void RemoveFromOfficers(long pointer)
    {
        try { RemovePointer(PoliceOfficer.Officers, pointer); }
        catch { }
    }

    private static void RemoveFromRegistry(long pointer)
    {
        try { RemovePointer(NPCManager.NPCRegistry, pointer); }
        catch { }
    }

    private static void RemovePointer<T>(Il2CppSystem.Collections.Generic.List<T>? list, long pointer) where T : NPC
    {
        if (list is null)
            return;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            try
            {
                var entry = list[i];
                if (entry != null && entry.Pointer.ToInt64() == pointer)
                    list.RemoveAt(i);
            }
            catch
            {
            }
        }
    }

    internal static bool TryGetOfficer(OcOfficerCloneRef clone, out PoliceOfficer? officer)
    {
        officer = null;
        try
        {
            if (clone.Native is not PoliceOfficer candidate || candidate == null)
                return false;
            if (candidate.WasCollected)
                return false;
            if (candidate.Pointer.ToInt64() != clone.Pointer)
                return false;
            officer = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
