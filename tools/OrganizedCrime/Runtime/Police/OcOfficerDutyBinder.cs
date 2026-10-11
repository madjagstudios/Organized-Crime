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


namespace OrganizedCrime.Runtime.Police;

internal sealed class OcOfficerDutyBinder
{
    private readonly OcOfficerWorldContext _context;

    internal static OcOfficerWorldStatus PrepareForPatrol(PoliceOfficer officer, out string detail)
    {
        try
        {
            var checkpoint = officer.CheckpointBehaviour;
            var npc = UnityLife.IsAlive(checkpoint) ? checkpoint!.Npc : null;
            var npcPointer = UnityLife.IsAlive(npc) ? npc!.Pointer.ToInt64() : officer.Pointer.ToInt64();
            var target = UnityLife.IsAlive(checkpoint) ? checkpoint!.Checkpoint : null;
            return OcPatrolCheckpointTransition.TryUnbind(
                unbind: () =>
                {
                    // Reuse the networked removal mirror: Deactivate cleans AssignedNPCs
                    // and the walk-through listener before the swap entry is cleared.
                    var note = UnbindCloneForRemove(officer, npcPointer);
                    SetCheckpointDialogue(officer, assign: false);
                    return note;
                },
                stillAssigned: () => UnityLife.IsAlive(checkpoint) &&
                    (checkpoint!.Enabled || checkpoint.Active ||
                     (UnityLife.IsAlive(target) && target!.AssignedNPCs is not null &&
                      target.AssignedNPCs.Contains(npc ?? officer))),
                out detail);
        }
        catch (Exception ex)
        {
            detail = $"checkpoint cleanup pending ({ex.GetType().Name}); patrol not ready";
            return OcOfficerWorldStatus.NotReady;
        }
    }

    internal static void SuppressForPatrol(PoliceOfficer officer)
    {
        DisableIfEnabled(FindScheduleBehaviour(officer));
        DisableIfEnabled(officer.SentryBehaviour);
        DisableIfEnabled(officer.BodySearchBehaviour);
        // PrepareForPatrol completed the full checkpoint unbind before resource allocation.
        DisableIfEnabled(officer.FootPatrolBehaviour);
        DisableIfEnabled(officer.VehiclePatrolBehaviour);
        // Pursuit, combat, arrest and reaction behaviours remain native-owned.
    }

    internal static void StopPatrolDuty(PoliceOfficer officer, OcPatrolDuty duty)
    {
        Il2CppScheduleOne.NPCs.Behaviour.Behaviour? behaviour = duty == OcPatrolDuty.Foot
            ? officer.FootPatrolBehaviour : officer.VehiclePatrolBehaviour;
        if (!UnityLife.IsAlive(behaviour)) return;
        DisableIfEnabled(behaviour);
        if (behaviour!.Active) behaviour.Deactivate();
        if (behaviour.Enabled || behaviour.Active)
            throw new InvalidOperationException("Patrol duty is still enabled or active after stop.");
    }

    internal OcOfficerDutyBinder(OcOfficerWorldContext context)
    {
        _context = context;
    }

    public OcOfficerWorldStatus TryBindToCheckpoint(OcOfficerCloneRef clone, object? nativeCheckpoint, OcOfficerPosition? standPoint, out string detail)
    {
        detail = string.Empty;
        try
        {
            if (nativeCheckpoint is not RoadCheckpoint target || target == null)
            {
                detail = "not a RoadCheckpoint";
                return OcOfficerWorldStatus.Rejected;
            }
            if (!_context.IsHost)
            {
                detail = "not host";
                return OcOfficerWorldStatus.NotHost;
            }
            if (!OcOfficerSpawner.TryGetOfficer(clone, out var officer) || officer is null)
            {
                detail = "clone is gone";
                return OcOfficerWorldStatus.InvalidHandle;
            }
            var behaviour = officer.CheckpointBehaviour;
            if (behaviour == null)
            {
                detail = "CheckpointBehaviour missing";
                return OcOfficerWorldStatus.Failed;
            }
            var npc = behaviour.Npc;
            if (npc is null || npc == null)
            {
                detail = "CheckpointBehaviour has no NPC";
                return OcOfficerWorldStatus.Failed;
            }
            // The map key is the behaviour's own NPC pointer: exactly what the
            // swap prefix reads, so no officer/NPC pointer assumption is needed.
            long npcPointer;
            try { npcPointer = npc.Pointer.ToInt64(); }
            catch
            {
                detail = "clone NPC pointer unreadable";
                return OcOfficerWorldStatus.Failed;
            }
            // The stand point only locates the spawn; the native behaviour walks
            // the clone to its own stand point, so the bind never warps.
            _ = standPoint;
            var status = OcCheckpointBindSequence.TryBind(
                checkpointReady: () => EnsureCheckpointReady(target),
                disableCopied: () => DisableCopiedBehaviours(officer, behaviour, target),
                mapClone: () => OcCheckpointSwapState.MapClone(npcPointer, target),
                setCheckpoint: () => behaviour.SetCheckpoint(OcCheckpointSwapState.CloneLocation),
                enable: () =>
                {
                    // Native AssignToCheckpoint enables from the server with a
                    // plain Enable; the bind is host-gated, so this matches.
                    if (!behaviour.Enabled)
                        behaviour.Enable();
                    return behaviour.Enabled;
                },
                out detail);
            var dialogueNote = "dialogue skipped";
            if (status == OcOfficerWorldStatus.Ok)
            {
                // Native AssignToCheckpoint's last step: point the officer's
                // DialogueController override at its CheckpointDialogue. Without
                // it the clone talks like an ordinary NPC. Duty doesn't depend
                // on it, so a failure warns once and the bind still counts.
                dialogueNote = SetCheckpointDialogue(officer, assign: true);
                if (!dialogueNote.StartsWith("dialogue set", StringComparison.Ordinal) && !_context._dialogueWarned)
                {
                    _context._dialogueWarned = true;
                    _context._warn($"OC officer checkpoint dialogue not applied ({dialogueNote}); clones on duty talk like ordinary NPCs.");
                }
            }
            detail += $"; {dialogueNote}; npc={npcPointer}, active={behaviour.Active}, assigned={behaviour.AssignedCheckpoint}";
            return status;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer checkpoint bind failed ({exception.GetType().Name}).");
            detail = $"bind failed ({exception.GetType().Name})";
            return OcOfficerWorldStatus.Failed;
        }
    }

    private static bool EnsureCheckpointReady(RoadCheckpoint target)
    {
        try
        {
            // Spawn enables the clone, but a bind against a disabled
            // checkpoint would only get OnActiveTick-disabled, so re-assert.
            if (target.ActivationState != RoadCheckpoint.ECheckpointState.Enabled)
            {
                target.ActivationState = RoadCheckpoint.ECheckpointState.Enabled;
                try { target.ApplyState(); }
                catch { }
            }
            if (target.ActivationState != RoadCheckpoint.ECheckpointState.Enabled)
                return false;
            var stands = target.StandPoints;
            return stands is not null && stands.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string DisableCopiedBehaviours(PoliceOfficer officer, CheckpointBehaviour checkpoint, RoadCheckpoint target)
    {
        var off = 0;
        // The clone copies an on-duty officer, so its schedule is Enabled and
        // outranks checkpoint duty; it stays off on every rebind.
        if (DisableIfEnabled(FindScheduleBehaviour(officer))) off++;
        if (DisableIfEnabled(officer.FootPatrolBehaviour)) off++;
        if (DisableIfEnabled(officer.VehiclePatrolBehaviour)) off++;
        if (DisableIfEnabled(officer.SentryBehaviour)) off++;
        if (DisableIfEnabled(officer.BodySearchBehaviour)) off++;
        // Left for the game: Pursuit and VehiclePursuit (officers listed on a
        // checkpoint join its foot pursuits), Combat, Dead/Unconscious, and the
        // reaction stack. The checkpoint behaviour itself resets only when
        // Active on another checkpoint, so a stale duty Deactivates (cleaning
        // its lists) first. The service re-binds every tick while Binding, so
        // duty already on our checkpoint must survive the re-bind.
        if (IsActive(checkpoint) && !PointsAt(checkpoint, target) && DisableIfEnabled(checkpoint)) off++;
        return $"{off} copied behaviour(s) off";
    }

    private static Il2CppScheduleOne.NPCs.Behaviour.Behaviour? FindScheduleBehaviour(PoliceOfficer officer)
    {
        try
        {
            // No direct accessor: the schedule behaviour lives only in the stack.
            // Il2Cpp list items arrive as the base Behaviour wrapper, so a C#
            // `is ScheduleBehaviour` never matches; TryCast checks the native type.
            var stack = officer.Behaviour?.behaviourStack;
            if (stack is not null)
            {
                for (var i = 0; i < stack.Count; i++)
                {
                    Il2CppScheduleOne.NPCs.Behaviour.Behaviour? item;
                    try { item = stack[i]; }
                    catch { continue; }
                    if (item is null || item == null)
                        continue;
                    var schedule = item.TryCast<ScheduleBehaviour>();
                    if (schedule is not null && schedule != null)
                        return schedule;
                }
            }
            var component = officer.GetComponentInChildren<ScheduleBehaviour>(true);
            return component is not null && component != null ? component : null;
        }
        catch
        {
            return null;
        }
    }

    // Awake copies EnabledOnAwake into Enabled; Start populates the enabled
    // stack and installs onDisable listeners. Suppress copied duty before both.
    internal static void PrepareCopiedBehaviours(GameObject root)
    {
        if (root.activeInHierarchy)
            throw new InvalidOperationException("Copied duty preparation requires an inactive clone.");
        foreach (var behaviour in root.GetComponentsInChildren<Il2CppScheduleOne.NPCs.Behaviour.Behaviour>(true))
        {
            if (!UnityLife.IsAlive(behaviour))
                continue;
            if (!UnityLife.IsAlive(behaviour.TryCast<ScheduleBehaviour>()) &&
                !UnityLife.IsAlive(behaviour.TryCast<FootPatrolBehaviour>()) &&
                !UnityLife.IsAlive(behaviour.TryCast<VehiclePatrolBehaviour>()) &&
                !UnityLife.IsAlive(behaviour.TryCast<SentryBehaviour>()) &&
                !UnityLife.IsAlive(behaviour.TryCast<BodySearchBehaviour>()) &&
                !UnityLife.IsAlive(behaviour.TryCast<CheckpointBehaviour>()))
                continue;
            behaviour.EnabledOnAwake = false;
            behaviour._Enabled_k__BackingField = false;
        }
        // The source officer's enabled list can be copied by Unity. Start
        // repopulates the clone's list from its own Enabled flags.
        var stack = root.GetComponentInChildren<NPCBehaviour>(true);
        if (!UnityLife.IsAlive(stack) || stack.enabledBehaviours is null)
            throw new InvalidOperationException("Clone enabled behaviour stack missing.");
        stack.enabledBehaviours.Clear();
    }

    private static bool DisableIfEnabled(Il2CppScheduleOne.NPCs.Behaviour.Behaviour? behaviour)
    {
        if (!UnityLife.IsAlive(behaviour))
            return false;
        var stack = behaviour!.beh;
        if (!UnityLife.IsAlive(stack) || stack.enabledBehaviours is null)
            throw new InvalidOperationException("Copied duty has no live behaviour stack.");
        return OcOfficerCopiedDuty.Disable(
            () => behaviour.Enabled,
            // Host-gated like the checkpoint's Enable: apply to this component
            // instead of dispatching by BehaviourIndex through an RPC.
            () => behaviour.Disable(),
            () => stack.RemoveEnabledBehaviour(behaviour),
            () => stack.enabledBehaviours.Contains(behaviour));
    }

    private static bool IsActive(Il2CppScheduleOne.NPCs.Behaviour.Behaviour? behaviour)
    {
        try { return behaviour is not null && behaviour != null && behaviour.Active; }
        catch { return false; }
    }

    private static bool PointsAt(CheckpointBehaviour checkpoint, RoadCheckpoint target)
    {
        try
        {
            var pointed = checkpoint.Checkpoint;
            return pointed is not null && pointed != null && pointed.Pointer == target.Pointer;
        }
        catch
        {
            return false;
        }
    }

    internal static string UnbindCloneForRemove(PoliceOfficer? officer, long npcPointer)
    {
        CheckpointBehaviour? checkpoint = null;
        try { checkpoint = officer?.CheckpointBehaviour; }
        catch { checkpoint = null; }
        return OcCheckpointBindSequence.TryUnbind(
            npcPointer,
            disableCheckpoint: () =>
            {
                if (checkpoint is null || checkpoint == null || !checkpoint.Enabled)
                    return;
                checkpoint.Disable_Networked(null!);
                // Native UnassignFromCheckpoint clears the override after the disable.
                if (officer is not null)
                    SetCheckpointDialogue(officer, assign: false);
            });
    }

    // f9 ISIL (PoliceOfficer.RpcLogic___AssignToCheckpoint / UnassignFromCheckpoint):
    // DialogueHandler.GetComponent<DialogueController>().OverrideContainer is set
    // to CheckpointDialogue on assign and to null on unassign.
    private static string SetCheckpointDialogue(PoliceOfficer officer, bool assign)
    {
        try
        {
            var handler = officer.DialogueHandler;
            if (handler is null || handler == null)
                return "dialogue handler missing";
            var controller = handler.GetComponent<DialogueController>();
            if (controller is null || controller == null)
                return "dialogue controller missing";
            if (!assign)
            {
                controller.OverrideContainer = null;
                return "dialogue cleared";
            }
            var conversation = officer.CheckpointDialogue;
            if (conversation is null || conversation == null)
                return "checkpoint dialogue missing on clone";
            if (controller.OverrideContainer is null || controller.OverrideContainer == null ||
                controller.OverrideContainer.Pointer != conversation.Pointer)
                controller.OverrideContainer = conversation;
            return "dialogue set";
        }
        catch (Exception exception)
        {
            return $"dialogue threw {exception.GetType().Name}";
        }
    }

    public OcOfficerWorldStatus TryClearSwapMap(out string detail)
    {
        detail = "swap map cleared";
        try
        {
            // No host gate: the roster clears on pre-load and dispose, when no
            // server is up, and clearing the static map is always safe.
            OcCheckpointSwapState.Clear();
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer swap clear failed ({exception.GetType().Name}).");
            detail = $"swap clear failed ({exception.GetType().Name})";
            return OcOfficerWorldStatus.Failed;
        }
    }

    public OcOfficerWorldStatus TryUnmapClone(OcOfficerCloneRef clone, out string detail)
    {
        detail = "swap entry cleared";
        try
        {
            // No host gate: release unmaps even when the removal itself fails
            // and the clone is orphaned, and unmapping is always safe.
            OcCheckpointSwapState.UnmapClone(clone.Pointer);
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer swap unmap failed ({exception.GetType().Name}).");
            detail = $"swap unmap failed ({exception.GetType().Name})";
            return OcOfficerWorldStatus.Failed;
        }
    }
}
