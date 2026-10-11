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
using TimeManager = S1API.GameTime.TimeManager;


namespace OrganizedCrime.Runtime.Police;

internal sealed class OcOfficerDiagnostics
{
    private readonly OcOfficerWorldContext _context;
    private readonly OcOfficerDependencyCheck _dependencies = new();

    private readonly OcOfficerDependencyWarnings _dependencyWarnings = new();

    internal void ResetDependencyWarnings() { _dependencies.Reset(); _dependencyWarnings.Reset(); OcOfficerNativeLifetime.ResetCleanupWarnings(); }

    internal string ReadDependencies(OcOfficerCloneRef clone) =>
        $"{ReadDependencyState(clone)}; {_dependencyWarnings.Status(clone)}; {_dependencyWarnings.CombatStatus(clone)}";

    private string ReadDependencyState(OcOfficerCloneRef clone)
    {
        if (!_context.IsHost) return "dependencies: host only";
        if (!_context._ownPointers.Contains(clone.Pointer)) return "dependencies: not owned";
        if (!_context._ownCloneRoots.TryGetValue(clone.Pointer, out var root) || !UnityLife.IsAlive(root))
            return "dependencies: missing=root";
        return OcOfficerSpawner.TryGetOfficer(clone, out var officer) && officer is not null
            ? OcOfficerNativeLifetime.Dependencies(root, officer) : "dependencies: missing=clone";
    }

    internal void PollDependencies() => _dependencies.Poll(_context.IsHost, Environment.TickCount64, () =>
    {
        foreach (var pair in _context._ownClones.ToArray())
        {
            var status = ReadDependencyState(pair.Value);
            _dependencyWarnings.Observe(pair.Value, OcOfficerDependencyWarnings.Problems(status), problem =>
                _context._warn($"OC clone dependency failure: clone={_context._ownCloneIds.GetValueOrDefault(pair.Key, "unknown")} pointer={pair.Key} missing={problem}. Warning once per load per clone/problem; use oc_ch3 officers status."));
        }
        return Array.Empty<string>();
    }, _context._warn);

    internal OcOfficerDiagnostics(OcOfficerWorldContext context)
    {
        _context = context;
    }

    internal OcOfficerWorldStatus TryReadStaleAttachmentCount(OcOfficerCloneRef clone, out int count)
    {
        count = -1;
        try
        {
            if (!_context.IsHost) return OcOfficerWorldStatus.NotHost;
            if (!_context._ownPointers.Contains(clone.Pointer) ||
                !OcOfficerSpawner.TryGetOfficer(clone, out var officer) || !UnityLife.IsAlive(officer))
                return OcOfficerWorldStatus.InvalidHandle;
            var avatar = officer!.Avatar;
            if (!UnityLife.IsAlive(avatar) || !avatar.transform.IsChildOf(officer.transform))
                return OcOfficerWorldStatus.NotReady;
            count = OcOfficerAttachmentCleanup.Count(avatar);
            return OcOfficerWorldStatus.Ok;
        }
        catch
        {
            return OcOfficerWorldStatus.Failed;
        }
    }

    public OcOfficerWorldStatus TryReadGameMinutes(out double minutes)
    {
        minutes = 0d;
        try
        {
            if (!_context.IsHost)
                return OcOfficerWorldStatus.NotHost;
            var elapsedDays = TimeManager.ElapsedDays;
            var minuteOfDay = TimeManager.GetMinutesFrom24HourTime(TimeManager.CurrentTime);
            if (elapsedDays < 0 || minuteOfDay is < 0 or >= 1440)
                return OcOfficerWorldStatus.NotReady;
            minutes = GameClock.TotalMinutes(elapsedDays, minuteOfDay);
            return double.IsFinite(minutes) ? OcOfficerWorldStatus.Ok : OcOfficerWorldStatus.NotReady;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer clock read failed ({exception.GetType().Name}).");
            minutes = 0d;
            return OcOfficerWorldStatus.Failed;
        }
    }

    public OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef clone, out bool knockedOut)
    {
        knockedOut = false;
        try
        {
            if (!_context.IsHost)
                return OcOfficerWorldStatus.NotHost;
            if (!OcOfficerSpawner.TryGetOfficer(clone, out var officer) || officer is null)
                return OcOfficerWorldStatus.InvalidHandle;
            var health = officer.Health;
            if (health == null)
                return OcOfficerWorldStatus.Failed;
            knockedOut = health.IsKnockedOut;
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer knockout read failed ({exception.GetType().Name}).");
            return OcOfficerWorldStatus.Failed;
        }
    }

    public OcOfficerWorldStatus TryReadClonePosition(OcOfficerCloneRef clone, out OcOfficerPosition position)
    {
        position = default;
        try
        {
            if (!_context.IsHost)
                return OcOfficerWorldStatus.NotHost;
            if (!OcOfficerSpawner.TryGetOfficer(clone, out var officer) || officer is null)
                return OcOfficerWorldStatus.InvalidHandle;
            var transform = officer.transform;
            var point = transform.position;
            position = new OcOfficerPosition(point.x, point.y, point.z, transform.rotation.eulerAngles.y);
            return position.IsFinite ? OcOfficerWorldStatus.Ok : OcOfficerWorldStatus.Failed;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer position read failed ({exception.GetType().Name}).");
            return OcOfficerWorldStatus.Failed;
        }
    }

    public OcOfficerWorldStatus TryReadBehaviourState(OcOfficerCloneRef clone, out string activeBehaviour, out bool checkpointActive)
    {
        activeBehaviour = "unknown";
        checkpointActive = false;
        try
        {
            if (!_context.IsHost)
                return OcOfficerWorldStatus.NotHost;
            if (!OcOfficerSpawner.TryGetOfficer(clone, out var officer) || officer is null)
                return OcOfficerWorldStatus.InvalidHandle;
            try
            {
                var stack = officer.Behaviour;
                var active = stack != null ? stack.activeBehaviour : null;
                if (active != null && !string.IsNullOrWhiteSpace(active.Name))
                    activeBehaviour = active.Name;
            }
            catch
            {
            }
            try
            {
                var checkpoint = officer.CheckpointBehaviour;
                if (checkpoint != null)
                    checkpointActive = checkpoint.Active;
            }
            catch
            {
            }
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer behaviour read failed ({exception.GetType().Name}).");
            activeBehaviour = "unknown";
            checkpointActive = false;
            return OcOfficerWorldStatus.Failed;
        }
    }

    public OcOfficerWorldStatus TryReadBoundState(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBoundState state)
    {
        state = new OcOfficerBoundState(false, false, OcCheckpointKind.Unknown, null);
        try
        {
            if (!_context.IsHost)
                return OcOfficerWorldStatus.NotHost;
            if (!OcOfficerSpawner.TryGetOfficer(clone, out var officer) || officer is null)
                return OcOfficerWorldStatus.InvalidHandle;
            var behaviour = officer.CheckpointBehaviour;
            if (behaviour == null)
                return OcOfficerWorldStatus.Failed;
            OcOfficerPosition? stand = null;
            try
            {
                var standPoint = behaviour.standPoint;
                if (standPoint != null)
                {
                    var at = standPoint.position;
                    stand = new OcOfficerPosition(at.x, at.y, at.z);
                }
            }
            catch
            {
            }
            state = new OcOfficerBoundState(behaviour.Active, behaviour.Enabled, ClassifyCheckpoint(behaviour.Checkpoint, ocCheckpoint), stand);
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer bound read failed ({exception.GetType().Name}).");
            state = new OcOfficerBoundState(false, false, OcCheckpointKind.Unknown, null);
            return OcOfficerWorldStatus.Failed;
        }
    }

    public OcOfficerWorldStatus TryReadBehaviourDiagnostics(OcOfficerCloneRef clone, object? ocCheckpoint, out OcOfficerBehaviourDiagnostics diagnostics)
    {
        diagnostics = new OcOfficerBehaviourDiagnostics(
            Array.Empty<OcOfficerBehaviourInfo>(),
            "unknown",
            new OcOfficerCheckpointDetail(false, false, "unknown", OcCheckpointKind.Unknown));
        try
        {
            if (!_context.IsHost)
                return OcOfficerWorldStatus.NotHost;
            if (!OcOfficerSpawner.TryGetOfficer(clone, out var officer) || officer is null)
                return OcOfficerWorldStatus.InvalidHandle;
            var stack = officer.Behaviour;
            if (stack == null)
                return OcOfficerWorldStatus.Failed;
            var infos = new List<OcOfficerBehaviourInfo>();
            try
            {
                var enabled = stack.enabledBehaviours;
                if (enabled != null)
                {
                    for (var i = 0; i < enabled.Count; i++)
                    {
                        Il2CppScheduleOne.NPCs.Behaviour.Behaviour? item = null;
                        try { item = enabled[i]; }
                        catch { continue; }
                        if (item == null)
                            continue;
                        infos.Add(ReadBehaviourInfo(item));
                    }
                }
            }
            catch
            {
            }
            var activeName = "unknown";
            try
            {
                var active = stack.activeBehaviour;
                if (active != null && !string.IsNullOrWhiteSpace(active.Name))
                    activeName = active.Name;
            }
            catch
            {
            }
            var detail = new OcOfficerCheckpointDetail(false, false, "unknown", OcCheckpointKind.Unknown);
            try
            {
                var checkpoint = officer.CheckpointBehaviour;
                if (checkpoint != null)
                    detail = new OcOfficerCheckpointDetail(
                        checkpoint.Enabled,
                        checkpoint.Active,
                        checkpoint.AssignedCheckpoint.ToString(),
                        ClassifyCheckpoint(checkpoint.Checkpoint, ocCheckpoint));
            }
            catch
            {
            }
            diagnostics = new OcOfficerBehaviourDiagnostics(infos, activeName, detail);
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer diagnostics read failed ({exception.GetType().Name}).");
            diagnostics = new OcOfficerBehaviourDiagnostics(
                Array.Empty<OcOfficerBehaviourInfo>(),
                "unknown",
                new OcOfficerCheckpointDetail(false, false, "unknown", OcCheckpointKind.Unknown));
            return OcOfficerWorldStatus.Failed;
        }
    }

    private static OcOfficerBehaviourInfo ReadBehaviourInfo(Il2CppScheduleOne.NPCs.Behaviour.Behaviour item)
    {
        string name = "?";
        var priority = 0;
        var enabled = false;
        var active = false;
        try { name = string.IsNullOrWhiteSpace(item.Name) ? "?" : item.Name; }
        catch { }
        try { priority = item.Priority; }
        catch { }
        try { enabled = item.Enabled; }
        catch { }
        try { active = item.Active; }
        catch { }
        return new OcOfficerBehaviourInfo(name, priority, enabled, active);
    }

    private static OcCheckpointKind ClassifyCheckpoint(RoadCheckpoint? pointed, object? ocCheckpoint)
    {
        try
        {
            if (pointed is null || pointed == null)
                return OcCheckpointKind.None;
            if (ocCheckpoint is not RoadCheckpoint ours || ours == null)
                return OcCheckpointKind.Unknown;
            return pointed.Pointer == ours.Pointer ? OcCheckpointKind.Ours : OcCheckpointKind.Native;
        }
        catch
        {
            return OcCheckpointKind.Unknown;
        }
    }

    public OcOfficerWorldStatus TryReadNativeLeak(string? nativeLabel, out bool leaked, out string detail)
    {
        leaked = false;
        detail = string.Empty;
        try
        {
            // The label parses before the host gate, so an unknown label is
            // no-leak on any peer instead of a QA dead end.
            if (!TryParseCheckpointLocation(nativeLabel, out var location))
            {
                detail = $"unknown native checkpoint '{nativeLabel}'";
                return OcOfficerWorldStatus.Ok;
            }
            if (!_context.IsHost)
            {
                detail = "not host";
                return OcOfficerWorldStatus.NotHost;
            }
            if (!NetworkSingleton<CheckpointManager>.InstanceExists)
            {
                detail = "checkpoint manager unavailable";
                return OcOfficerWorldStatus.NotReady;
            }
            var manager = NetworkSingleton<CheckpointManager>.Instance;
            if (manager is null || manager == null)
            {
                detail = "checkpoint manager unavailable";
                return OcOfficerWorldStatus.NotReady;
            }
            RoadCheckpoint? native;
            try
            {
                native = manager.GetCheckpoint(location);
            }
            catch (Exception exception)
            {
                detail = $"native {location} unreadable ({exception.GetType().Name})";
                return OcOfficerWorldStatus.Failed;
            }
            if (native is null || native == null)
            {
                detail = $"native {location} missing";
                return OcOfficerWorldStatus.NotReady;
            }
            var count = 0;
            try
            {
                var assigned = native.AssignedNPCs;
                if (assigned is not null)
                {
                    for (var i = 0; i < assigned.Count; i++)
                    {
                        NPC? entry;
                        try { entry = assigned[i]; }
                        catch { continue; }
                        if (entry is null || entry == null)
                            continue;
                        long pointer;
                        try { pointer = entry.Pointer.ToInt64(); }
                        catch { continue; }
                        if (_context._ownPointers.Contains(pointer))
                            count++;
                    }
                }
            }
            catch (Exception exception)
            {
                detail = $"native {location} roster unreadable ({exception.GetType().Name})";
                return OcOfficerWorldStatus.Failed;
            }
            leaked = count > 0;
            detail = leaked
                ? $"native {location} lists {count} OC clone(s)"
                : $"native {location} lists no OC clone";
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer native leak read failed ({exception.GetType().Name}).");
            leaked = false;
            detail = $"native leak read failed ({exception.GetType().Name})";
            return OcOfficerWorldStatus.Failed;
        }
    }

    private static bool TryParseCheckpointLocation(string? label, out CheckpointManager.ECheckpointLocation location)
    {
        location = default;
        return !string.IsNullOrWhiteSpace(label)
            && Enum.TryParse(label, ignoreCase: true, out location)
            && Enum.IsDefined(typeof(CheckpointManager.ECheckpointLocation), location);
    }

    public OcOfficerWorldStatus TryReadNativeCounts(out OcOfficerNativeCounts counts)
    {
        counts = new OcOfficerNativeCounts(-1, -1);
        try
        {
            if (!_context.IsHost)
                return OcOfficerWorldStatus.NotHost;
            var officers = -1;
            var registry = -1;
            try { officers = PoliceOfficer.Officers?.Count ?? -1; }
            catch { officers = -1; }
            try { registry = NPCManager.NPCRegistry?.Count ?? -1; }
            catch { registry = -1; }
            counts = new OcOfficerNativeCounts(officers, registry);
            return OcOfficerWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer native counts failed ({exception.GetType().Name}).");
            return OcOfficerWorldStatus.Failed;
        }
    }

    public OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yawDegrees)
    {
        position = default;
        yawDegrees = 0f;
        try
        {
            if (!_context.IsHost)
                return OcOfficerWorldStatus.NotHost;
            var player = Il2CppScheduleOne.PlayerScripts.Player.Local;
            if (player == null)
                return OcOfficerWorldStatus.NotReady;
            var transform = player.transform;
            var point = transform.position;
            var forward = transform.forward;
            position = new OcOfficerPosition(point.x, point.y, point.z);
            yawDegrees = (float)(Math.Atan2(forward.x, forward.z) * 180d / Math.PI);
            return position.IsFinite && float.IsFinite(yawDegrees) ? OcOfficerWorldStatus.Ok : OcOfficerWorldStatus.NotReady;
        }
        catch (Exception exception)
        {
            _context._warn($"OC officer player pose failed ({exception.GetType().Name}).");
            return OcOfficerWorldStatus.Failed;
        }
    }

    /// <summary>
    /// The OC-107 ApplyNPCData input probe, kept as a diagnostic: every value
    /// the native method dereferences without a null check, read with the
    /// clone still inactive. Only missing inputs are listed.
    /// </summary>
    internal static string SnapshotApplyInputs(PoliceOfficer officer)
    {
        var missing = new List<string>();
        try
        {
            NPCData? data;
            try { data = officer._NPCData_k__BackingField; }
            catch { data = null; }
            if (data is null)
            {
                missing.Add("data=NULL");
            }
            else
            {
                if (Presence(() => data.Appearance) != "present") missing.Add("data.Appearance");
                if (Presence(() => data.Health) != "present") missing.Add("data.Health");
                if (Presence(() => data.Inventory) != "present") missing.Add("data.Inventory");
                if (Presence(() => data.Movement) != "present") missing.Add("data.Movement");
                if (Presence(() => data.Messaging) != "present") missing.Add("data.Messaging");
                if (Presence(() => data.WeatherBehaviour) != "present") missing.Add("data.WeatherBehaviour");
            }
            if (Presence(() => officer.Avatar) != "present") missing.Add("Avatar");
            else if (Presence(() => officer.Avatar._appearance) != "present") missing.Add("Avatar._appearance");
            if (Presence(() => officer.AggressionController) != "present") missing.Add("AggressionController");
            if (Presence(() => officer.Health) != "present") missing.Add("Health");
            if (Presence(() => officer.Inventory) != "present") missing.Add("Inventory");
            if (Presence(() => officer.Behaviour) != "present") missing.Add("Behaviour");
            else if (Presence(() => officer.Behaviour.CombatBehaviour) != "present") missing.Add("Behaviour.CombatBehaviour");
            if (Presence(() => officer.DialogueHandler) != "present") missing.Add("DialogueHandler");
            if (Presence(() => officer.Movement) != "present") missing.Add("Movement");
            else if (Presence(() => officer.Movement._speedController) != "present") missing.Add("Movement._speedController");
            if (Presence(() => officer.VoiceOverEmitter) != "present") missing.Add("VoiceOverEmitter");
            if (Presence(() => officer.RelationData) != "present") missing.Add("RelationData");
        }
        catch (Exception exception)
        {
            missing.Add($"probe threw ({exception.GetType().Name})");
        }
        return missing.Count == 0 ? "apply-inputs: all present" : $"apply-inputs missing: {string.Join(", ", missing)}";
    }

    private static string Presence<T>(Func<T> read)
    {
        try
        {
            var value = read();
            return value is null || (value is Object unity && unity == null) ? "missing" : "present";
        }
        catch
        {
            return "unreadable";
        }
    }

    internal static int CountPointer<T>(Il2CppSystem.Collections.Generic.List<T>? list, long pointer) where T : NPC
    {
        if (list is null)
            return 0;
        var count = 0;
        for (var i = 0; i < list.Count; i++)
        {
            try
            {
                var entry = list[i];
                if (entry != null && entry.Pointer.ToInt64() == pointer)
                    count++;
            }
            catch
            {
            }
        }
        return count;
    }

    private static bool ContainsPointer<T>(Il2CppSystem.Collections.Generic.List<T>? list, long pointer) where T : NPC =>
        CountPointer(list, pointer) > 0;
}
