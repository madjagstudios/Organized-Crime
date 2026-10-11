using OrganizedCrime.Model;
using S1API.Entities;
using UnityEngine;
using UnityEngine.AI;
using NativeNetworkObject = Il2CppFishNet.Object.NetworkObject;
using NativeAccessory = Il2CppScheduleOne.AvatarFramework.Accessory;
using NativeAvatarLayer = Il2CppScheduleOne.AvatarFramework.AvatarLayer;
using NativeAvatarObjectLibrary = Il2CppScheduleOne.Avatar.AvatarObjectLibrary;
using NativeNpc = Il2CppScheduleOne.NPCs.NPC;
using NativeNpcMovement = Il2CppScheduleOne.NPCs.NPCMovement;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-130: the one place S1API entity, movement, dialogue, and native
/// avatar types are touched for Arthur's proof visit. Every entry point
/// is total: it returns a status and a reason and never lets an
/// exception out. It never requests a save, never damages, kills,
/// knocks out, heals, or revives anything, and never writes
/// aggressiveness, give up range, give up time, or a weapon path.
/// There is no combat member on this class at all: no provoke, no
/// target, no attack.
/// Every deactivating or destroying member refuses until FishNet
/// reports the root <c>NetworkObject.IsSpawned</c>: S1API's spawn
/// guard drops a custom NPC whose Avatar is inactive, so a pending
/// spawn is only ever moved, never deactivated or destroyed.
/// </summary>
public sealed class S1ApiChapter3ArthurRuntime : IChapter3ArthurWorld, IChapter3ArthurHqBody
{
    /// <summary>
    /// The radius come/leave snap each ring candidate to the navmesh
    /// with, matching the spike's own guard before every position write.
    /// </summary>
    private const float NavMeshSampleRadius = 1.5f;

    /// <summary>
    /// The fixed park coordinate: far beneath any player-reachable
    /// area of the map, and far from the world origin. Parked is the
    /// position plus a deactivated game object plus a forced-invisible
    /// native NPC, the spike's own sequence.
    /// </summary>
    public static readonly (float X, float Y, float Z) ParkingPoint = (0f, -10000f, 0f);

    /// <summary>
    /// How long a <c>say</c> line stays in the world, matching the
    /// native five-second worldspace default.
    /// </summary>
    private const float SaySeconds = 5f;

    private const int MaxPlacementTries = 8;

    /// <summary>
    /// The view-cone half angle for the leave park: past this angle
    /// off the player's facing (or past <see cref="ViewMaxMetres"/>)
    /// he reads as out of view. Generous on purpose, so a leave
    /// parks late rather than early.
    /// </summary>
    private const float ViewConeHalfAngleDegrees = 60f;

    private const float ViewMaxMetres = 80f;

    private readonly Action<string> _warn;
    private readonly Func<bool> _hasAuthority;
    private bool _disposed;
    private bool _resumeToPlayer;
    private Vector3 _resumeTarget;
    private bool _hasResumeTarget;
    private Vector3? _lastHqSpot;
    private float _lastHqYaw = Chapter3ArthurHqSpot.YawDegrees;
    private ((float X, float Y, float Z) Spot, float Yaw)? _hqSpotOverride;
    private readonly IChapter3ArthurSpeedControl _walkSpeed;
    private Vector3 _walkIssuedFrom;
    private float _walkIssuedAt;
    private bool _walkTracked;

    public S1ApiChapter3ArthurRuntime(Action<string> warn, Func<bool> hasAuthority)
    {
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
        _hasAuthority = hasAuthority ?? throw new ArgumentNullException(nameof(hasAuthority));
        _walkSpeed = new S1ApiChapter3ArthurSpeedControl(ResolveSpeedBody, warn);
    }

    public bool HasAuthority
    {
        get
        {
            try
            {
                return _hasAuthority();
            }
            catch
            {
                return false;
            }
        }
    }

    public Chapter3ArthurReadStatus TryReadArthur(out Chapter3ArthurSnapshot snapshot)
    {
        snapshot = Chapter3ArthurSnapshot.Absent();
        if (_disposed) return Chapter3ArthurReadStatus.Unavailable;
        try
        {
            var player = Player.Local;
            if (player is null) return Chapter3ArthurReadStatus.Unavailable;

            var contact = NPC.Get(Chapter3ArthurIds.NpcId);
            if (contact is null)
            {
                snapshot = Chapter3ArthurSnapshot.Absent() with { RegisteredCount = CountRegistrations() };
                return Chapter3ArthurReadStatus.Ready;
            }

            var position = contact.Position;
            var movement = contact.Movement;
            var body = contact.gameObject;
            var spawned = body != null && IsSpawned(body);
            var playerPosition = player.Position;
            var flat = new Vector3(position.x - playerPosition.x, 0f, position.z - playerPosition.z);
            snapshot = new Chapter3ArthurSnapshot(
                true,
                spawned,
                contact.IsVisible,
                movement is not null && movement.IsMoving,
                contact.IsConscious,
                (position.x, position.y, position.z),
                flat.magnitude,
                CountRegistrations(),
                ReadHealth(contact),
                ReadInPlayerView(player, position));
            return Chapter3ArthurReadStatus.Ready;
        }
        catch (Exception exception)
        {
            _warn($"OC-130 Arthur read failed ({exception.GetType().Name}): {Truncate(exception.Message)}");
            snapshot = Chapter3ArthurSnapshot.Absent();
            return Chapter3ArthurReadStatus.Faulted;
        }
    }

    /// <summary>
    /// Drains one suppressed native impact on Arthur: true once
    /// per recorded hit, false when nothing was recorded since
    /// the last poll. Total: never throws, false when disposed.
    /// The pump polls this every pass as the attack signal; the
    /// health-drop polling stays as the fallback.
    /// </summary>
    public bool PollArthurImpact()
    {
        if (_disposed)
            return false;
        try
        {
            return Chapter3ArthurResponseFilter.PollImpact();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Pre-spawn hiding: warps to <see cref="ParkingPoint"/> and
    /// nothing else. No stop, no deactivate, no visibility write, so
    /// the spawn guard still sees every child it checks. Runs on
    /// Refuses until FishNet reports spawned: never park a pending spawn.
    /// every pre-spawn reconcile pass; idempotent by construction.
    /// </summary>
    public Chapter3ArthurMutationStatus TryMoveOutOfSight(out string reason)
    {
        reason = "the Arthur pre-spawn move did not run.";
        if (_disposed)
        { reason = "the Arthur runtime is disposed."; return Chapter3ArthurMutationStatus.Unavailable; }
        if (!HasAuthority)
        { reason = "the Arthur runtime has no host authority."; return Chapter3ArthurMutationStatus.Rejected; }
        try
        {
            var contact = NPC.Get(Chapter3ArthurIds.NpcId);
            if (contact is null)
            { reason = "no Arthur resolved to that id."; return Chapter3ArthurMutationStatus.Unavailable; }
            if (contact.Movement is null)
            { reason = "Arthur has no movement."; return Chapter3ArthurMutationStatus.Unavailable; }

            contact.Position = new Vector3(ParkingPoint.X, ParkingPoint.Y, ParkingPoint.Z);
            reason = $"moved to {contact.Position}; spawn still pending.";
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-130 Arthur pre-spawn move failed ({exception.GetType().Name}).");
            reason = $"the Arthur pre-spawn move threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// The spike's own park sequence: stop, move to
    /// <see cref="ParkingPoint"/>, deactivate the game object, force
    /// the native NPC invisible (networked), in that order.
    /// </summary>
    public Chapter3ArthurMutationStatus TryParkArthur(out string reason)
    {
        reason = "the Arthur park did not run.";
        if (_disposed)
        { reason = "the Arthur runtime is disposed."; return Chapter3ArthurMutationStatus.Unavailable; }
        if (!HasAuthority)
        { reason = "the Arthur runtime has no host authority."; return Chapter3ArthurMutationStatus.Rejected; }
        try
        {
            var contact = NPC.Get(Chapter3ArthurIds.NpcId);
            if (contact is null)
            { reason = "no Arthur resolved to that id."; return Chapter3ArthurMutationStatus.Unavailable; }

            var movement = contact.Movement;
            if (movement is not null) movement.Stop();

            // IsUnityNull safe compare, matching every other gameObject read on this boundary.
            var body = contact.gameObject;
            if (body == null)
            { reason = "Arthur has no game object."; return Chapter3ArthurMutationStatus.Unavailable; }
            if (!IsSpawned(body))
            { reason = "Arthur isn't spawned yet; refusing to park a pending spawn."; return Chapter3ArthurMutationStatus.Rejected; }
            Chapter3ArthurWalkSpeed.EndWalk(_walkSpeed, isSpawned: true);
            _walkTracked = false;

            // Back from a possible HQ hold: the agent path warps again.
            RestoreMovementForWalk();

            contact.Position = new Vector3(ParkingPoint.X, ParkingPoint.Y, ParkingPoint.Z);
            body.SetActive(false);
            var native = body.GetComponent<NativeNpc>();
            if (native != null) native.SetVisible(false, networked: true);

            reason = $"stopped, moved to {contact.Position}, deactivated, visible forced false.";
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-130 Arthur park failed ({exception.GetType().Name}).");
            reason = $"the Arthur park threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// Verifies every <see cref="Chapter3ArthurAppearance.ExpectedPaths"/>
    /// entry resolves through the same three steps S1API's own
    /// appearance resolution uses: a Resources accessory prefab, a
    /// Resources avatar layer, then the avatar object library by full
    /// id and by final segment. One warning line per unresolved path;
    /// the reason carries the count either way. Runs on the load
    /// reconcile, once per load.
    /// </summary>
    public Chapter3ArthurMutationStatus TryVerifyAppearancePaths(out string reason)
    {
        reason = "the Arthur appearance verification did not run.";
        if (_disposed)
        { reason = "the Arthur runtime is disposed."; return Chapter3ArthurMutationStatus.Unavailable; }
        try
        {
            var unresolved = new List<string>();
            foreach (var path in Chapter3ArthurAppearance.ExpectedPaths)
            {
                if (!TryResolveAvatarObject(path))
                {
                    unresolved.Add(path);
                    _warn($"OC-130 Arthur appearance: unresolved path '{path}'.");
                }
            }

            var total = Chapter3ArthurAppearance.ExpectedPaths.Count;
            reason = unresolved.Count == 0
                ? $"all {total} appearance paths resolved."
                : $"{unresolved.Count} of {total} appearance paths unresolved: {string.Join(", ", unresolved)}.";
            return unresolved.Count == 0 ? Chapter3ArthurMutationStatus.Succeeded : Chapter3ArthurMutationStatus.Ambiguous;
        }
        catch (Exception exception)
        {
            _warn($"OC-130 Arthur appearance verification failed ({exception.GetType().Name}).");
            reason = $"the Arthur appearance verification threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// Unparks at a reachable navmesh point about 20-30 m from the
    /// player, ideally behind them, and walks to the player. The
    /// pure <see cref="Chapter3ArthurPlacement"/> selector picks
    /// from evaluated ring candidates; a <c>CanGetTo</c> refusal
    /// excludes that candidate and retries the next, bounded, and a
    /// total refusal re-parks, so a failed come never leaves him
    /// standing where he cannot walk from.
    /// </summary>
    public Chapter3ArthurMutationStatus TryComeArthur(out string reason)
    {
        reason = "the Arthur come did not run.";
        if (_disposed)
        { reason = "the Arthur runtime is disposed."; return Chapter3ArthurMutationStatus.Unavailable; }
        try
        {
            var player = Player.Local;
            if (player is null)
            { reason = "the local player was not available."; return Chapter3ArthurMutationStatus.Unavailable; }
            var contact = NPC.Get(Chapter3ArthurIds.NpcId);
            if (contact is null)
            { reason = "no Arthur resolved to that id."; return Chapter3ArthurMutationStatus.Unavailable; }
            var transform = player.Transform;
            if (transform == null)
            { reason = "the local player transform was not available."; return Chapter3ArthurMutationStatus.Unavailable; }
            var body = contact.gameObject;
            if (body == null)
            { reason = "Arthur has no game object."; return Chapter3ArthurMutationStatus.Unavailable; }
            if (!IsSpawned(body))
            { reason = "Arthur isn't spawned yet."; return Chapter3ArthurMutationStatus.Rejected; }
            var movement = contact.Movement;
            if (movement is null)
            { reason = "Arthur has no movement."; return Chapter3ArthurMutationStatus.Unavailable; }

            var playerPosition = player.Position;
            // Back from a possible HQ hold: the walk needs the agent.
            RestoreMovementForWalk();

            var forward = transform.forward;
            var candidates = EvaluateRingCandidates(playerPosition, forward, new[] { 20f, 25f, 30f });
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            for (var attempt = 0; attempt < MaxPlacementTries; attempt++)
            {
                var pick = Chapter3ArthurPlacement.SelectComeCandidate(
                    candidates.Where(candidate => !excluded.Contains(Describe(candidate))));
                if (pick is null) break;

                var snapped = new Vector3(pick.Point.X, pick.Point.Y, pick.Point.Z);
                contact.Position = snapped;
                body.SetActive(true);
                var native = body.GetComponent<NativeNpc>();
                if (native != null) native.SetVisible(true, networked: true);

                if (movement.CanGetTo(playerPosition, 2f))
                {
                    // The look is defined at prefab time; come applies no
                    // look of its own.
                    contact.Goto(playerPosition);
                    _resumeToPlayer = true;
                    _hasResumeTarget = false;
                    var walkSpeedNote = Chapter3ArthurWalkSpeed.BeginWalk(_walkSpeed, isSpawned: true);
                    _walkIssuedFrom = snapped;
                    _walkIssuedAt = Time.realtimeSinceStartup;
                    _walkTracked = true;
                    reason = WithWalkNote($"unparked at {snapped} ({pick.DistanceToPlayer:0.0} m, " +
                        $"{(pick.BehindPlayer ? "behind" : "ahead of")} the player), CanGetTo true, approach issued.", walkSpeedNote);
                    return Chapter3ArthurMutationStatus.Succeeded;
                }

                excluded.Add(Describe(pick));
            }

            _resumeToPlayer = false;
            _hasResumeTarget = false;
            TryParkArthur(out var reparkReason);
            reason = candidates.Count == 0
                ? "no navmesh point found on the 20-30 m ring; re-parked."
                : $"no reachable come point after {excluded.Count} tries; re-parked ({reparkReason}).";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
        catch (Exception exception)
        {
            _warn($"OC-130 Arthur come failed ({exception.GetType().Name}).");
            reason = $"the Arthur come threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// Shows a spoken line in the world through S1API's own
    /// <c>NPC.Dialogue.ShowWorldText</c>, which shows native
    /// worldspace dialogue with no interaction needed: no press E,
    /// no conversation start, just the text above him.
    /// </summary>
    public Chapter3ArthurMutationStatus TrySayArthur(string text, out string reason)
    {
        reason = "the Arthur say did not run.";
        if (_disposed)
        { reason = "the Arthur runtime is disposed."; return Chapter3ArthurMutationStatus.Unavailable; }
        if (string.IsNullOrWhiteSpace(text))
        { reason = "there was no text to say."; return Chapter3ArthurMutationStatus.Rejected; }
        try
        {
            var contact = NPC.Get(Chapter3ArthurIds.NpcId);
            if (contact is null)
            { reason = "no Arthur resolved to that id."; return Chapter3ArthurMutationStatus.Unavailable; }
            var body = contact.gameObject;
            if (body == null)
            { reason = "Arthur has no game object."; return Chapter3ArthurMutationStatus.Unavailable; }
            if (!IsSpawned(body))
            { reason = "Arthur isn't spawned yet."; return Chapter3ArthurMutationStatus.Rejected; }
            var dialogue = contact.Dialogue;
            if (dialogue is null)
            { reason = "Arthur has no dialogue."; return Chapter3ArthurMutationStatus.Unavailable; }

            dialogue.ShowWorldText(text, SaySeconds);
            reason = $"showed \"{Truncate(text, 80)}\" for {SaySeconds} seconds.";
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-130 Arthur say failed ({exception.GetType().Name}).");
            reason = $"the Arthur say threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// Plays one avatar animation trigger through the native NPC.SetAnimationTrigger, which
    /// only forwards to the avatar animator: no combat state, no damage, no target. The f7
    /// punch path plays "Punch" the same way on the shared rig.
    /// </summary>
    public Chapter3ArthurMutationStatus TryPlayAnimationTrigger(string trigger, out string reason)
    {
        reason = "the Arthur animation did not run.";
        if (_disposed)
        { reason = "the Arthur runtime is disposed."; return Chapter3ArthurMutationStatus.Unavailable; }
        if (string.IsNullOrWhiteSpace(trigger))
        { reason = "there was no trigger to play."; return Chapter3ArthurMutationStatus.Rejected; }
        try
        {
            var contact = NPC.Get(Chapter3ArthurIds.NpcId);
            if (contact is null)
            { reason = "no Arthur resolved to that id."; return Chapter3ArthurMutationStatus.Unavailable; }
            var body = contact.gameObject;
            if (body == null)
            { reason = "Arthur has no game object."; return Chapter3ArthurMutationStatus.Unavailable; }
            if (!IsSpawned(body))
            { reason = "Arthur isn't spawned yet."; return Chapter3ArthurMutationStatus.Rejected; }
            var native = body.GetComponent<NativeNpc>();
            if (native == null)
            { reason = "Arthur has no native NPC."; return Chapter3ArthurMutationStatus.Unavailable; }

            native.SetAnimationTrigger(trigger);
            reason = $"played animation trigger \"{Truncate(trigger, 40)}\".";
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-135 Arthur animation failed ({exception.GetType().Name}).");
            reason = $"the Arthur animation threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// Walks about 30 m away, ideally out of view; the visit pump
    /// parks him once the walk finishes or its bound expires.
    /// </summary>
    public Chapter3ArthurMutationStatus TryLeaveArthur(out string reason)
    {
        reason = "the Arthur leave did not run.";
        if (_disposed)
        { reason = "the Arthur runtime is disposed."; return Chapter3ArthurMutationStatus.Unavailable; }
        try
        {
            var player = Player.Local;
            if (player is null)
            { reason = "the local player was not available."; return Chapter3ArthurMutationStatus.Unavailable; }
            var contact = NPC.Get(Chapter3ArthurIds.NpcId);
            if (contact is null)
            { reason = "no Arthur resolved to that id."; return Chapter3ArthurMutationStatus.Unavailable; }
            var body = contact.gameObject;
            if (body == null)
            { reason = "Arthur has no game object."; return Chapter3ArthurMutationStatus.Unavailable; }
            if (!IsSpawned(body))
            { reason = "Arthur isn't spawned yet."; return Chapter3ArthurMutationStatus.Rejected; }
            var transform = player.Transform;
            if (transform == null)
            { reason = "the local player transform was not available."; return Chapter3ArthurMutationStatus.Unavailable; }
            var movement = contact.Movement;
            if (movement is null)
            { reason = "Arthur has no movement."; return Chapter3ArthurMutationStatus.Unavailable; }

            // Back from a possible HQ hold: the walk needs the agent.
            RestoreMovementForWalk();

            var playerPosition = player.Position;
            var candidates = EvaluateRingCandidates(playerPosition, transform.forward, new[] { 25f, 30f, 35f });
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            for (var attempt = 0; attempt < MaxPlacementTries; attempt++)
            {
                var pick = Chapter3ArthurPlacement.SelectLeaveCandidate(
                    candidates.Where(candidate => !excluded.Contains(Describe(candidate))));
                if (pick is null) break;

                var destination = new Vector3(pick.Point.X, pick.Point.Y, pick.Point.Z);
                if (movement.CanGetTo(destination))
                {
                    contact.Goto(destination);
                    _resumeToPlayer = false;
                    _resumeTarget = destination;
                    _hasResumeTarget = true;
                    var walkSpeedNote = Chapter3ArthurWalkSpeed.BeginWalk(_walkSpeed, isSpawned: true);
                    _walkIssuedFrom = contact.Position;
                    _walkIssuedAt = Time.realtimeSinceStartup;
                    _walkTracked = true;
                    reason = WithWalkNote($"walking to {destination} ({pick.DistanceToPlayer:0.0} m, " +
                        $"{(pick.BehindPlayer ? "behind" : "ahead of")} the player); the pump parks him on arrival.", walkSpeedNote);
                    return Chapter3ArthurMutationStatus.Succeeded;
                }

                excluded.Add(Describe(pick));
            }

            _resumeToPlayer = false;
            _hasResumeTarget = false;
            reason = candidates.Count == 0
                ? "no navmesh point found on the 30 m ring."
                : $"no reachable leave point after {excluded.Count} tries.";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
        catch (Exception exception)
        {
            _warn($"OC-130 Arthur leave failed ({exception.GetType().Name}).");
            reason = $"the Arthur leave threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// Stops the approach and faces the player. Runs once, when the
    /// visit pump observes the arrival.
    /// </summary>
    public Chapter3ArthurMutationStatus TryFacePlayer(out string reason)
    {
        reason = "the Arthur face did not run.";
        if (_disposed)
        { reason = "the Arthur runtime is disposed."; return Chapter3ArthurMutationStatus.Unavailable; }
        try
        {
            var player = Player.Local;
            if (player is null)
            { reason = "the local player was not available."; return Chapter3ArthurMutationStatus.Unavailable; }
            var contact = NPC.Get(Chapter3ArthurIds.NpcId);
            if (contact is null)
            { reason = "no Arthur resolved to that id."; return Chapter3ArthurMutationStatus.Unavailable; }
            var body = contact.gameObject;
            if (body == null)
            { reason = "Arthur has no game object."; return Chapter3ArthurMutationStatus.Unavailable; }
            if (!IsSpawned(body))
            { reason = "Arthur isn't spawned yet."; return Chapter3ArthurMutationStatus.Rejected; }
            var movement = contact.Movement;
            if (movement is null)
            { reason = "Arthur has no movement."; return Chapter3ArthurMutationStatus.Unavailable; }

            Chapter3ArthurWalkSpeed.EndWalk(_walkSpeed, isSpawned: true);
            movement.Stop();
            movement.FacePoint(player.Position);
            reason = WithWalkNote("stopped and faced the player.", DescribeComeArrival(contact));
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-130 Arthur face failed ({exception.GetType().Name}).");
            reason = $"the Arthur face threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// OC-132 tuning: captures the player's current position and yaw
    /// as Arthur's HQ spot for the session (in memory only), behind
    /// the owner-QA <c>oc_ch3 arthur hq here</c> command. Converts
    /// with <see cref="Chapter3ArthurHqSpot.ToLocal"/> against the
    /// interior root (the pocket root by construction), snaps the
    /// height to the definition's floor top, and refuses outside the
    /// HQ frame. A read: never touches Arthur, so no spawn gate.
    /// </summary>
    public Chapter3ArthurMutationStatus TryCaptureHqSpot(out (float X, float Y, float Z) local, out float yawDegrees, out string reason)
    {
        local = (0f, 0f, 0f);
        yawDegrees = Chapter3ArthurHqSpot.YawDegrees;
        reason = "the HQ spot capture did not run.";
        if (_disposed)
        {
            reason = "the Arthur runtime is disposed.";
            return Chapter3ArthurMutationStatus.Unavailable;
        }
        try
        {
            var root = GameObject.Find(SyndicateHqContract.RootName);
            if (root == null || !root.activeInHierarchy)
            {
                reason = "the HQ interior is not created.";
                return Chapter3ArthurMutationStatus.Unavailable;
            }
            var player = Player.Local;
            if (player is null)
            {
                reason = "the local player was not available.";
                return Chapter3ArthurMutationStatus.Unavailable;
            }
            var transform = player.Transform;
            if (transform == null)
            {
                reason = "the local player transform was not available.";
                return Chapter3ArthurMutationStatus.Unavailable;
            }
            var pocketRoot = root.transform.position;
            var playerPosition = player.Position;
            var captured = Chapter3ArthurHqSpot.ToLocal(
                (playerPosition.x, playerPosition.y, playerPosition.z),
                (pocketRoot.x, pocketRoot.y, pocketRoot.z));
            if (!Chapter3ArthurHqSpot.IsInsideFrame(captured))
            {
                reason = "Arthur HQ spot capture refused: the player is not inside the HQ.";
                return Chapter3ArthurMutationStatus.Rejected;
            }
            local = (captured.X, FloorTopY(), captured.Z);
            yawDegrees = transform.rotation.eulerAngles.y;
            _hqSpotOverride = (local, yawDegrees);
            reason = "captured the player's spot as Arthur's HQ spot for the session.";
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-132 Arthur HQ spot capture failed ({exception.GetType().Name}).");
            reason = $"the HQ spot capture threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// OC-132: turns him toward the player with the transform only
    /// (yaw), for the HQ maintain pass. The pocket room has no
    /// navmesh, so the agent-backed face is never used there.
    /// Refuses until FishNet reports spawned.
    /// </summary>
    public Chapter3ArthurMutationStatus TryFacePlayerAtHq(out string reason)
    {
        reason = "the Arthur HQ face did not run.";
        if (_disposed)
        { reason = "the Arthur runtime is disposed."; return Chapter3ArthurMutationStatus.Unavailable; }
        try
        {
            var contact = NPC.Get(Chapter3ArthurIds.NpcId);
            if (contact is null)
            { reason = "no Arthur resolved to that id."; return Chapter3ArthurMutationStatus.Unavailable; }
            var body = contact.gameObject;
            if (body == null)
            { reason = "Arthur has no game object."; return Chapter3ArthurMutationStatus.Unavailable; }
            if (!IsSpawned(body))
            { reason = "Arthur isn't spawned yet."; return Chapter3ArthurMutationStatus.Rejected; }

            return Chapter3ArthurHqPlacer.TryFacePlayer(this, out reason);
        }
        catch (Exception exception)
        {
            _warn($"OC-132 Arthur HQ face failed ({exception.GetType().Name}).");
            reason = $"the Arthur HQ face threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// OC-132: stands him at the HQ spot through
    /// <see cref="Chapter3ArthurHqPlacer"/>: activate first, agent
    /// off, physics held, direct transform set, visible, then a
    /// read-back that only succeeds within half a metre of the spot.
    /// The first attempt warped through <c>Movement</c> while the
    /// body was still inactive from the park, which moves nothing,
    /// and the pocket room has no navmesh for the agent anyway. The
    /// interior root position is the pocket root by construction, so
    /// the spot math mirrors the HQ service without touching
    /// Release 1 code, and he is never parented to the root. While
    /// the interior is gone (the save-time rebuild destroys it
    /// first) this holds the last spot instead of placing, so he
    /// never falls through the missing floor. Idempotent: safe to
    /// call every maintain pass. Refuses until FishNet reports
    /// spawned: never place a pending spawn.
    /// </summary>
    public Chapter3ArthurMutationStatus TryPlaceAtHq(out string reason)
    {
        reason = "the Arthur HQ place did not run.";
        if (_disposed)
        { reason = "the Arthur runtime is disposed."; return Chapter3ArthurMutationStatus.Unavailable; }
        try
        {
            var contact = NPC.Get(Chapter3ArthurIds.NpcId);
            if (contact is null)
            { reason = "no Arthur resolved to that id."; return Chapter3ArthurMutationStatus.Unavailable; }
            var body = contact.gameObject;
            if (body == null)
            { reason = "Arthur has no game object."; return Chapter3ArthurMutationStatus.Unavailable; }
            if (!IsSpawned(body))
            { reason = "Arthur isn't spawned yet."; return Chapter3ArthurMutationStatus.Rejected; }
            Chapter3ArthurWalkSpeed.EndWalk(_walkSpeed, isSpawned: true);
            _walkTracked = false;

            var root = GameObject.Find(SyndicateHqContract.RootName);
            if (root == null || !root.activeInHierarchy)
            {
                if (_lastHqSpot.HasValue)
                {
                    // The agent is off at the HQ, so the hold sets the
                    // transform directly: a warp would go nowhere.
                    body.transform.position = _lastHqSpot.Value;
                    body.transform.rotation = Quaternion.Euler(0f, _lastHqYaw, 0f);
                    reason = "the HQ interior is gone; holding the last HQ spot.";
                    return Chapter3ArthurMutationStatus.Unavailable;
                }
                reason = "the HQ interior is not created.";
                return Chapter3ArthurMutationStatus.Unavailable;
            }

            // A session override from hq here wins over the default spot.
            var spotLocal = _hqSpotOverride?.Spot ?? Chapter3ArthurHqSpot.Local;
            var spotYaw = _hqSpotOverride?.Yaw ?? Chapter3ArthurHqSpot.YawDegrees;
            var pocketRoot = root.transform.position;
            var spot = new Vector3(
                pocketRoot.x + spotLocal.X,
                pocketRoot.y + FloorTopY(),
                pocketRoot.z + spotLocal.Z);
            _lastHqSpot = spot;
            _lastHqYaw = spotYaw;
            return Chapter3ArthurHqPlacer.TryPlace(
                this,
                (spot.x, spot.y, spot.z),
                spotYaw,
                out reason);
        }
        catch (Exception exception)
        {
            _warn($"OC-132 Arthur HQ place failed ({exception.GetType().Name}).");
            reason = $"the Arthur HQ place threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// OC-132: re-issues the current walk target after a stall: the
    /// player's live position for a come, the stored destination for
    /// a leave. The pump bounds the calls; this only refuses when no
    /// walk was ever issued. Refuses until FishNet reports spawned.
    /// </summary>
    public Chapter3ArthurMutationStatus TryResumeWalk(out string reason)
    {
        reason = "the Arthur walk re-issue did not run.";
        if (_disposed)
        { reason = "the Arthur runtime is disposed."; return Chapter3ArthurMutationStatus.Unavailable; }
        try
        {
            var contact = NPC.Get(Chapter3ArthurIds.NpcId);
            if (contact is null)
            { reason = "no Arthur resolved to that id."; return Chapter3ArthurMutationStatus.Unavailable; }
            var body = contact.gameObject;
            if (body == null)
            { reason = "Arthur has no game object."; return Chapter3ArthurMutationStatus.Unavailable; }
            if (!IsSpawned(body))
            { reason = "Arthur isn't spawned yet."; return Chapter3ArthurMutationStatus.Rejected; }
            var movement = contact.Movement;
            if (movement is null)
            { reason = "Arthur has no movement."; return Chapter3ArthurMutationStatus.Unavailable; }

            RestoreMovementForWalk();

            Vector3 target;
            if (_resumeToPlayer)
            {
                var player = Player.Local;
                if (player is null)
                { reason = "the local player was not available."; return Chapter3ArthurMutationStatus.Unavailable; }
                target = player.Position;
            }
            else if (_hasResumeTarget)
            {
                target = _resumeTarget;
            }
            else
            {
                reason = "no walk target was issued to resume.";
                return Chapter3ArthurMutationStatus.Unavailable;
            }

            contact.Goto(target);
            var walkSpeedNote = Chapter3ArthurWalkSpeed.BeginWalk(_walkSpeed, isSpawned: true);
            reason = WithWalkNote($"re-issued the walk to {target}.", walkSpeedNote);
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-132 Arthur walk re-issue failed ({exception.GetType().Name}).");
            reason = $"the Arthur walk re-issue threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// OC-132 fix: <see cref="IChapter3ArthurHqBody"/>, one thin
    /// engine step each. The agent goes through the native
    /// movement's own <c>SetAgentEnabled</c> (stopping first when
    /// disabling); physics through its <c>SetGravityMultiplier</c>
    /// (NPCs carry no rigidbody to park, only a capsule collider, so
    /// zero gravity is the hold and the assumed default of one is
    /// the restore). Transforms bypass <c>Movement</c> entirely:
    /// its warp is agent-backed and the pocket room has no navmesh.
    /// </summary>
    Chapter3ArthurMutationStatus IChapter3ArthurHqBody.TryActivateBody(out string reason)
    {
        reason = "the HQ body activate did not run.";
        var status = TryResolveHqContact(out _, out var body, out var resolveReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = resolveReason;
            return status;
        }
        try
        {
            body!.SetActive(true);
            reason = "activated the body.";
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-132 Arthur HQ activate failed ({exception.GetType().Name}).");
            reason = $"the HQ body activate threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    Chapter3ArthurMutationStatus IChapter3ArthurHqBody.TrySetAgentEnabled(bool enabled, out string reason)
    {
        reason = "the HQ agent step did not run.";
        var status = TryResolveHqMovement(out var contact, out var movement, out var resolveReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = resolveReason;
            return status;
        }
        try
        {
            if (!enabled)
            {
                try
                {
                    contact!.Movement?.Stop();
                }
                catch
                {
                }
            }
            movement!.SetAgentEnabled(enabled);
            // The movement's own switch was not enough: an owner run
            // read the agent back on. The movement's agent may live on
            // a child while another sits on the root, so every agent
            // under his root goes the same way, and the count is
            // reported for the log.
            var count = 0;
            try
            {
                var owned = movement._agent;
                if (owned != null)
                {
                    owned.enabled = enabled;
                    count++;
                }
            }
            catch
            {
            }
            try
            {
                var found = contact!.gameObject.GetComponentsInChildren<NavMeshAgent>(true);
                if (found != null)
                {
                    foreach (var agent in found)
                    {
                        if (agent == null) continue;
                        agent.enabled = enabled;
                        count++;
                    }
                }
            }
            catch
            {
            }
            reason = enabled ? $"agent enabled ({count} components)." : $"agent disabled ({count} components).";
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-132 Arthur HQ agent step failed ({exception.GetType().Name}).");
            reason = $"the HQ agent step threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }


    Chapter3ArthurMutationStatus IChapter3ArthurHqBody.TrySetPhysicsHeld(bool held, out string reason)
    {
        reason = "the HQ physics step did not run.";
        var status = TryResolveHqMovement(out _, out var movement, out var resolveReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = resolveReason;
            return status;
        }
        try
        {
            movement!.SetGravityMultiplier(held ? 0f : 1f);
            reason = held ? "gravity held at zero." : "gravity restored to one.";
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-132 Arthur HQ physics step failed ({exception.GetType().Name}).");
            reason = $"the HQ physics step threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    Chapter3ArthurMutationStatus IChapter3ArthurHqBody.TrySetTransform((float X, float Y, float Z) position, float yawDegrees, out string reason)
    {
        reason = "the HQ transform step did not run.";
        var status = TryResolveHqContact(out _, out var body, out var resolveReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = resolveReason;
            return status;
        }
        try
        {
            body!.transform.position = new Vector3(position.X, position.Y, position.Z);
            body.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            reason = "transform set directly.";
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-132 Arthur HQ transform step failed ({exception.GetType().Name}).");
            reason = $"the HQ transform step threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    Chapter3ArthurMutationStatus IChapter3ArthurHqBody.TrySetVisible(bool visible, out string reason)
    {
        reason = "the HQ visibility step did not run.";
        var status = TryResolveHqContact(out _, out var body, out var resolveReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = resolveReason;
            return status;
        }
        try
        {
            var native = body!.GetComponent<NativeNpc>();
            if (native == null)
            {
                reason = "Arthur has no native NPC.";
                return Chapter3ArthurMutationStatus.Unavailable;
            }
            native.SetVisible(visible, networked: true);
            reason = visible ? "visible forced true." : "visible forced false.";
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-132 Arthur HQ visibility step failed ({exception.GetType().Name}).");
            reason = $"the HQ visibility step threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    Chapter3ArthurMutationStatus IChapter3ArthurHqBody.TryFacePlayerDirect(out string reason)
    {
        reason = "the HQ face step did not run.";
        var status = TryResolveHqContact(out _, out var body, out var resolveReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = resolveReason;
            return status;
        }
        try
        {
            var player = Player.Local;
            if (player is null)
            {
                reason = "the local player was not available.";
                return Chapter3ArthurMutationStatus.Unavailable;
            }
            var offset = player.Position - body!.transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude < 1e-6f)
            {
                reason = "at the player's feet; no turn needed.";
                return Chapter3ArthurMutationStatus.Succeeded;
            }
            var yawDegrees = MathF.Atan2(offset.x, offset.z) * (180f / MathF.PI);
            body.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            reason = $"faced the player directly (yaw {yawDegrees:0}).";
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-132 Arthur HQ face step failed ({exception.GetType().Name}).");
            reason = $"the HQ face step threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    Chapter3ArthurMutationStatus IChapter3ArthurHqBody.TryReadPlacement(out (float X, float Y, float Z) position, out bool agentEnabled, out string reason)
    {
        position = (0f, 0f, 0f);
        agentEnabled = true;
        reason = "the HQ read-back did not run.";
        var status = TryResolveHqContact(out _, out var body, out var resolveReason);
        if (status != Chapter3ArthurMutationStatus.Succeeded)
        {
            reason = resolveReason;
            return status;
        }
        try
        {
            var actual = body!.transform.position;
            position = (actual.x, actual.y, actual.z);
            // Any-on across every agent under his root: the old
            // root-only read reported a different agent than the one
            // the movement owns.
            var count = 0;
            var anyOn = false;
            var found = body.GetComponentsInChildren<NavMeshAgent>(true);
            if (found != null)
            {
                foreach (var agent in found)
                {
                    if (agent == null) continue;
                    count++;
                    if (agent.enabled) anyOn = true;
                }
            }
            agentEnabled = anyOn;
            reason = count == 1 ? "1 agent component read." : $"{count} agent components read.";
            return Chapter3ArthurMutationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            _warn($"OC-132 Arthur HQ read-back failed ({exception.GetType().Name}).");
            reason = $"the HQ read-back threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    private static string WithWalkNote(string reason, string note) =>
        note.Length == 0 ? reason : reason + " " + note;

    private GameObject? ResolveSpeedBody()
    {
        try
        {
            return NPC.Get(Chapter3ArthurIds.NpcId)?.gameObject;
        }
        catch
        {
            return null;
        }
    }

    private string DescribeComeArrival(NPC contact)
    {
        if (!_walkTracked)
            return string.Empty;
        _walkTracked = false;
        try
        {
            var end = contact.Position;
            var flat = new Vector3(end.x - _walkIssuedFrom.x, 0f, end.z - _walkIssuedFrom.z);
            return Chapter3ArthurWalkSpeed.DescribeArrival(flat.magnitude, Time.realtimeSinceStartup - _walkIssuedAt);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// OC-132 fix: restores the agent and physics before any park,
    /// come, leave, or re-issue, warning and continuing on failure:
    /// the walk or park itself is the safe direction.
    /// </summary>
    private void RestoreMovementForWalk()
    {
        try
        {
            var status = Chapter3ArthurHqPlacer.TryRestoreForWalk(this, out var restoreReason);
            if (status != Chapter3ArthurMutationStatus.Succeeded)
                _warn($"OC-132 Arthur movement restore {status}: {restoreReason}");
        }
        catch (Exception exception)
        {
            _warn($"OC-132 Arthur movement restore threw {exception.GetType().Name}.");
        }
    }

    private Chapter3ArthurMutationStatus TryResolveHqContact(out NPC? contact, out GameObject? body, out string reason)
    {
        contact = null;
        body = null;
        reason = "the HQ resolve did not run.";
        if (_disposed)
        {
            reason = "the Arthur runtime is disposed.";
            return Chapter3ArthurMutationStatus.Unavailable;
        }
        try
        {
            contact = NPC.Get(Chapter3ArthurIds.NpcId);
        }
        catch
        {
            contact = null;
        }
        if (contact is null)
        {
            reason = "no Arthur resolved to that id.";
            return Chapter3ArthurMutationStatus.Unavailable;
        }
        try
        {
            body = contact.gameObject;
        }
        catch
        {
            body = null;
        }
        if (body == null)
        {
            reason = "Arthur has no game object.";
            return Chapter3ArthurMutationStatus.Unavailable;
        }
        reason = "resolved.";
        return Chapter3ArthurMutationStatus.Succeeded;
    }

    private Chapter3ArthurMutationStatus TryResolveHqMovement(out NPC? contact, out NativeNpcMovement? movement, out string reason)
    {
        movement = null;
        var status = TryResolveHqContact(out contact, out var body, out reason);
        if (status != Chapter3ArthurMutationStatus.Succeeded) return status;
        try
        {
            movement = body!.GetComponent<NativeNpc>()?.Movement;
        }
        catch
        {
            movement = null;
        }
        if (movement == null)
        {
            reason = "Arthur has no native movement.";
            return Chapter3ArthurMutationStatus.Unavailable;
        }
        reason = "resolved.";
        return Chapter3ArthurMutationStatus.Succeeded;
    }

    /// <summary>
    /// Duplicate safety: exactly one registered NPC may carry
    /// Arthur's id. Extras are stopped, deactivated, destroyed, and
    /// removed from <c>NPC.All</c>, keeping the first registration.
    /// Only spawned instances are ever destroyed; unspawned
    /// duplicates are left untouched.
    /// Runs on the load reconcile; <c>status</c> reports the count.
    /// </summary>
    public Chapter3ArthurMutationStatus TryDedupeArthur(out string reason)
    {
        reason = "the Arthur dedupe did not run.";
        if (_disposed)
        { reason = "the Arthur runtime is disposed."; return Chapter3ArthurMutationStatus.Unavailable; }
        if (!HasAuthority)
        { reason = "the Arthur runtime has no host authority."; return Chapter3ArthurMutationStatus.Rejected; }
        try
        {
            var all = NPC.All;
            if (all is null)
            { reason = "NPC.All was not available."; return Chapter3ArthurMutationStatus.Unavailable; }

            var matches = all.Where(npc => npc is not null && string.Equals(npc.ID, Chapter3ArthurIds.NpcId, StringComparison.Ordinal)).ToArray();
            if (matches.Length <= 1)
            {
                reason = matches.Length == 1 ? "already exactly one registration." : "no registration found; nothing to dedupe.";
                return Chapter3ArthurMutationStatus.Succeeded;
            }

            var spawned = matches.Where(IsLiveAndSpawned).ToArray();
            if (spawned.Length == 0)
            {
                reason = $"{matches.Length} registrations, none spawned yet; leaving them untouched.";
                return Chapter3ArthurMutationStatus.Rejected;
            }

            var removed = 0;
            foreach (var duplicate in spawned.Skip(1))
            {
                try
                {
                    duplicate.Movement?.Stop();
                    var body = duplicate.gameObject;
                    if (body != null)
                    {
                        body.SetActive(false);
                        UnityEngine.Object.Destroy(body);
                    }
                    all.Remove(duplicate);
                    removed++;
                }
                catch (Exception exception)
                {
                    _warn($"OC-130 Arthur dedupe failed on one duplicate ({exception.GetType().Name}).");
                }
            }

            var pending = matches.Length - spawned.Length;
            if (removed != spawned.Length - 1)
            {
                reason = $"kept 1 spawned of {matches.Length} registrations, removed {removed}.";
                return Chapter3ArthurMutationStatus.Ambiguous;
            }
            reason = pending == 0
                ? $"kept 1 of {matches.Length} registrations, removed {removed}."
                : $"kept 1 spawned of {matches.Length} registrations, removed {removed}, {pending} still pending.";
            return pending == 0 ? Chapter3ArthurMutationStatus.Succeeded : Chapter3ArthurMutationStatus.Ambiguous;
        }
        catch (Exception exception)
        {
            _warn($"OC-130 Arthur dedupe failed ({exception.GetType().Name}).");
            reason = $"the Arthur dedupe threw {exception.GetType().Name}: {exception.Message}";
            return Chapter3ArthurMutationStatus.Ambiguous;
        }
    }

    /// <summary>
    /// The spawned signal: FishNet's own <c>IsSpawned</c> on the root
    /// <c>NetworkObject</c>, set during the server spawn that runs
    /// S1API's guard first. Total: any failure reads as unspawned.
    /// </summary>
    private static bool IsSpawned(GameObject body)
    {
        try
        {
            var netObject = body.GetComponent<NativeNetworkObject>();
            return netObject != null && netObject.IsSpawned;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLiveAndSpawned(NPC npc)
    {
        try
        {
            if (npc is null) return false;
            var body = npc.gameObject;
            return body != null && IsSpawned(body);
        }
        catch
        {
            return false;
        }
    }
    public void Dispose() => _disposed = true;

    /// <summary>
    /// OC-132: the live current-health read behind attack logging.
    /// Total: any failure reads as NaN, which the pump skips.
    /// </summary>
    private static float ReadHealth(NPC contact)
    {
        try
        {
            return contact.CurrentHealth;
        }
        catch
        {
            return float.NaN;
        }
    }

    /// <summary>
    /// OC-132: whether Arthur reads as inside the player's view: a
    /// generous horizontal cone plus a max range. Total: any failure
    /// reads as visible, so a leave never parks early on a bad read.
    /// </summary>
    private static bool ReadInPlayerView(Player player, Vector3 position)
    {
        try
        {
            var transform = player.Transform;
            if (transform == null) return true;
            var forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return true;
            var toArthur = position - player.Position;
            toArthur.y = 0f;
            var distance = toArthur.magnitude;
            if (distance > ViewMaxMetres) return false;
            if (distance < 0.5f) return true;
            return Vector3.Angle(forward, toArthur) <= ViewConeHalfAngleDegrees;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// OC-132: the HQ floor top, read off the Floor primitive in the
    /// interior definition (centre plus half height), so the spot
    /// height follows the definition instead of a second constant.
    /// </summary>
    private static float FloorTopY()
    {
        try
        {
            var floor = SyndicateHqInteriorDefinition.Primitives
                .FirstOrDefault(primitive => primitive is not null && primitive.Name == "Floor");
            if (floor is not null) return floor.Position.Y + floor.Scale.Y / 2f;
        }
        catch
        {
        }
        return Chapter3ArthurHqSpot.Local.Y;
    }

    private static int CountRegistrations()
    {
        try
        {
            var all = NPC.All;
            return all?.Count(npc => npc is not null && string.Equals(npc.ID, Chapter3ArthurIds.NpcId, StringComparison.Ordinal)) ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Mirrors S1API's own appearance resolution order for one path: a
    /// Resources accessory prefab, a Resources avatar layer, then the
    /// avatar object library by full id and by final segment. Total:
    /// any failure reads as unresolved.
    /// </summary>
    private static bool TryResolveAvatarObject(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab != null && prefab.GetComponent<NativeAccessory>()?.AvatarObjectEquivalent is not null)
                return true;
        }
        catch
        {
        }
        try
        {
            if (Resources.Load<NativeAvatarLayer>(path)?.AvatarObjectEquivalent is not null)
                return true;
        }
        catch
        {
        }
        try
        {
            if (NativeAvatarObjectLibrary.TryGetAvatarObjectById(path, out _))
                return true;
            var finalSegment = path.Substring(path.LastIndexOf('/') + 1);
            return NativeAvatarObjectLibrary.TryGetAvatarObjectById(finalSegment, out _);
        }
        catch
        {
            return false;
        }
    }

    private static List<Chapter3ArthurCandidate> EvaluateRingCandidates(Vector3 playerPosition, Vector3 playerForward, float[] radii)
    {
        var evaluated = new List<Chapter3ArthurCandidate>();
        var flat = new Vector3(playerForward.x, 0f, playerForward.z);
        if (flat.sqrMagnitude < 0.001f) flat = Vector3.forward;
        flat.Normalize();
        foreach (var radius in radii)
        {
            for (var step = 0; step < 12; step++)
            {
                var angle = step * (Mathf.PI * 2f / 12f);
                var offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                var point = playerPosition + offset;
                var behind = Vector3.Dot(offset, -flat) > 0f;
                if (!NavMesh.SamplePosition(point, out var hit, NavMeshSampleRadius, NavMesh.AllAreas))
                    continue;
                var snapped = hit.position;
                evaluated.Add(new Chapter3ArthurCandidate(
                    (snapped.x, snapped.y, snapped.z),
                    Vector3.Distance(snapped, playerPosition),
                    behind,
                    true));
            }
        }
        return evaluated;
    }

    private static string Describe(Chapter3ArthurCandidate candidate) =>
        $"{candidate.Point.X:0.00},{candidate.Point.Y:0.00},{candidate.Point.Z:0.00}";

    private const int MaxWarnMessageLength = 160;

    private static string Truncate(string? text, int maxLength = MaxWarnMessageLength)
    {
        text ??= string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
