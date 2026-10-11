using Il2CppScheduleOne.AvatarFramework.Equipping;
using Il2CppScheduleOne.Combat;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.Police;
using OrganizedCrime.Runtime.Platform;
using UnityEngine;
using UnityEngine.AI;
using Avatar = Il2CppScheduleOne.AvatarFramework.Avatar;
using Object = UnityEngine.Object;

namespace OrganizedCrime.Runtime.Police;

internal static class OcOfficerNativeLifetime
{
    private static readonly OcOfficerCleanupWarnings CleanupWarnings = new();
    internal static void ResetCleanupWarnings() => CleanupWarnings.Reset();

    // Fresh inactive Instantiate result only. Keep equipment bound to the owned
    // avatar; a stale copied equippable can refer to
    // the source officer's detached Light: never call Unequip on that copy.
    internal static void StripCopies(GameObject root, PoliceOfficer officer)
    {
        if (!UnityLife.IsAlive(root) || root.activeSelf || root.activeInHierarchy)
            throw new InvalidOperationException("Equippable cleanup requires an inactive clone.");
        Action<string> rejected = reason => CleanupWarnings.Observe(root.name, reason, OrganizedCrimeLog.Warning);
        var avatar = OwnedAvatar(root, officer);
        var combats = OwnedCombat(root, officer);
        var combat = combats[0];
        var punch = combat.VirtualPunchWeapon;
        foreach (var candidate in combats)
            if (!Inside(root, candidate.VirtualPunchWeapon))
                throw new InvalidOperationException("Clone needs an owned CombatBehaviour.VirtualPunchWeapon.");
        var copies = OcOfficerCloneLifetime.StaleCopies(root.GetComponentsInChildren<AvatarEquippable>(true),
            item => Inside(root, item), item => UnityLife.IsAlive(item),
            item => UnityLife.IsAlive(item.avatar) && item.avatar == avatar,
            item => item == punch || combats.Any(other => item == other.VirtualPunchWeapon));
        // Start equips this deliberately unbound weapon. Destroying another
        // stale item's GameObject must not also destroy it or its container.
        foreach (var copy in copies)
        {
            var go = copy.gameObject;
            if (Inside(go, punch) || Inside(go, combat) ||
                combats.Any(other => Inside(go, other) || Inside(go, other.VirtualPunchWeapon)))
                OcOfficerCloneLifetime.RejectCleanup("Stale equippable contains a clone combat dependency.", rejected);
        }
        var current = avatar.CurrentEquippable;
        if (!UnityLife.IsAlive(current) || !Inside(root, current) ||
            !UnityLife.IsAlive(current!.avatar) || current.avatar != avatar)
            avatar._CurrentEquippable_k__BackingField = null;
        foreach (var copy in copies)
        {
            if (!UnityLife.IsAlive(copy)) continue;
            var go = copy.gameObject;
            if (go == root || go == officer.gameObject || go == avatar.gameObject)
                OcOfficerCloneLifetime.RejectCleanup("Equippable shares a clone dependency object.", rejected);
            copy.enabled = false;
            Object.DestroyImmediate(go);
        }
    }

    internal static void BindDependencies(GameObject root, PoliceOfficer officer)
    {
        if (root.activeInHierarchy || root.activeSelf)
            throw new InvalidOperationException("Dependency binding requires an inactive clone.");
        var movements = root.GetComponentsInChildren<NPCMovement>(true);
        if (movements.Length != 1 || !Inside(root, movements[0]))
            throw new InvalidOperationException("Clone needs one owned movement.");
        var movement = movements[0];
        var agents = root.GetComponentsInChildren<NavMeshAgent>(true);
        if (agents.Length != 1 || !Inside(root, agents[0]))
            throw new InvalidOperationException("Clone needs one owned NavMeshAgent.");
        officer._Movement_k__BackingField = movement;
        movement._agent = agents[0];
        movement._npc = officer;
        officer.Avatar = OwnedAvatar(root, officer);
        if (movement._agent != agents[0] || movement._npc != officer || officer.Movement != movement)
            throw new InvalidOperationException("Clone dependency binding did not read back.");
    }

    internal static void FreshGuid(PoliceOfficer officer, OcOfficerGuidOwnership ownership)
    {
        // Start parses BakedGUID and registers GUID. SetGUID would register too
        // early and Start would register twice. Every instance, including retries,
        // gets a new baked value independent of the roster's human-readable ID.
        var guid = OcOfficerCloneLifetime.NewGuid();
        ownership.Track(officer.Pointer.ToInt64(), Guid.Parse(guid));
        officer.BakedGUID = guid;
        if (officer.BakedGUID != guid) throw new InvalidOperationException("Fresh clone GUID did not read back.");
    }

    internal static void DeregisterGuid(Guid guid, long pointer)
    {
        // Use the captured minted GUID even after Unity destruction. Native
        // DeregisterObject reads obj.GUID, which may no longer be readable.
        var nativeGuid = new Il2CppSystem.Guid(guid.ToString("D"));
        var objects = Il2Cpp.GUIDManager.guidToObject;
        if (objects is not null && objects.TryGetValue(nativeGuid, out var registered))
        {
            // Another owner replacing this key must never be removed by OC.
            if (registered is not null && registered.Pointer.ToInt64() != pointer) return;
            objects.Remove(nativeGuid);
        }
        var guids = Il2Cpp.GUIDManager.registeredGUIDs;
        if (guids is not null)
            while (guids.Remove(nativeGuid)) { }
    }

    internal static void Quiesce(GameObject root, PoliceOfficer officer) =>
        OcOfficerCloneLifetime.Quiesce(() => Unequip(root, officer), () =>
        {
            try
            {
                foreach (var movement in root.GetComponentsInChildren<NPCMovement>(true))
                {
                    if (!Inside(root, movement)) continue;
                    movement.enabled = false;
                    movement.StopAllCoroutines();
                }
            }
            finally { root.SetActive(false); }
        });

    private static void Unequip(GameObject root, PoliceOfficer officer)
    {
        var avatar = OwnedAvatar(root, officer);
        // Include non-current copied children defensively. Root ownership is
        // required even when CurrentEquippable points at a foreign instance.
        var items = root.GetComponentsInChildren<AvatarEquippable>(true).ToArray();
        foreach (var item in items)
        {
            if (!Inside(root, item)) continue;
            item.enabled = false; // LateUpdate must stop before deferred Destroy.
            var flashlight = item.TryCast<FlashlightAvatarEquippable>();
            if (item.avatar == avatar && (flashlight is null || UnityLife.IsAlive(flashlight.Light)))
                item.Unequip(); // Flashlight.Unequip destroys its detached Light.
            var go = item.gameObject;
            if (go != root && go != officer.gameObject && go != avatar.gameObject) Object.Destroy(go);
        }
        avatar._CurrentEquippable_k__BackingField = null;
    }

    private static Avatar OwnedAvatar(GameObject root, PoliceOfficer officer)
    {
        var avatars = officer.GetComponentsInChildren<Avatar>(true);
        if (avatars.Length != 1 || !Inside(root, avatars[0]))
            throw new InvalidOperationException("Clone needs one owned avatar.");
        return avatars[0];
    }

    private static CombatBehaviour[] OwnedCombat(GameObject root, PoliceOfficer officer) =>
        OcOfficerCloneLifetime.OwnedCombat(officer.GetComponentsInChildren<CombatBehaviour>(true).ToArray(),
            behaviour => Inside(root, behaviour));

    internal static bool Inside(GameObject root, Component? component) => UnityLife.IsAlive(component) &&
        (component!.transform == root.transform || component.transform.IsChildOf(root.transform));

    internal static string Dependencies(GameObject root, PoliceOfficer officer, bool awaitingCombatStart = false)
    {
        try
        {
            if (!UnityLife.IsAlive(officer)) return "missing=clone";
            var avatar = officer.Avatar;
            var behaviour = officer.Behaviour;
            var defaultCombat = UnityLife.IsAlive(behaviour) ? behaviour!.CombatBehaviour : null;
            var movement = officer.Movement;
            var movementOwned = Inside(root, movement);
            var agent = movementOwned ? movement!._agent : null;
            var linkKnown = !UnityLife.IsAlive(agent) || !agent!.enabled || !agent.isOnNavMesh ||
                !agent.isOnOffMeshLink || UnityLife.IsAlive(agent.currentOffMeshLinkData.offMeshLink);
            var state = new OcOfficerDependencyState(Inside(root, avatar), movementOwned,
                movementOwned && Inside(root, agent), movementOwned && movement!._npc == officer,
                movementOwned && movement!._speedController is not null, linkKnown);
            var equipment = new List<OcEquippableDependency>();
            var equippables = root.GetComponentsInChildren<AvatarEquippable>(true).ToArray();
            var combats = root.GetComponentsInChildren<CombatBehaviour>(true)
                .Where(other => Inside(root, other)).ToArray();
            var combatWeapons = combats.Select(other => other.VirtualPunchWeapon).ToArray();
            foreach (var item in equippables)
            {
                if (!UnityLife.IsAlive(item)) continue;
                /* Historical default-punch skip, superseded by the owned weapon set below:
                if (!root.activeInHierarchy)
                {
                    if (item == punch) continue;
                }
                Retained as a comment for existing source-characterization assertions.
                */
                // Inactive copies await native Start; active diagnostics require binding.
                if (!root.activeInHierarchy && combatWeapons.Any(weapon => item == weapon)) continue;
                var flashlight = item.TryCast<FlashlightAvatarEquippable>();
                equipment.Add(new(item.Pointer.ToString(), item.avatar == avatar && UnityLife.IsAlive(item.avatar),
                    flashlight is null || UnityLife.IsAlive(flashlight.Light),
                    Inside(root, item) && combatWeapons.Any(weapon => item == weapon)));
            }
            var missing = OcOfficerDependencyIssues.Read(state, equipment, awaitingCombatStart);
            if (!Inside(root, defaultCombat)) missing.Add("combat.behaviour");
            foreach (var combat in combats)
            {
                var punch = combat.VirtualPunchWeapon;
                var currentWeapon = combat!.currentWeapon;
                var combatState = new OcOfficerCombatState(true, UnityLife.IsAlive(punch), Inside(root, punch),
                    UnityLife.IsAlive(currentWeapon), Inside(root, currentWeapon),
                    UnityLife.IsAlive(currentWeapon) && UnityLife.IsAlive(currentWeapon!.avatar) && currentWeapon.avatar == avatar,
                    combat.GetIl2CppType().Name);
                missing.AddRange(OcOfficerCombatIssues.Read(combatState));
                // PoliceOfficer.Start resolves PoliceBelt. The immediate spawn
                // validation already allows Start to run; the regular host poll
                // observes every callback dependency without changing native state.
                if (!awaitingCombatStart)
                {
                    var pursuit = combat.TryCast<PursuitBehaviour>();
                    if (pursuit is not null && UnityLife.IsAlive(pursuit))
                    {
                        var cachedOfficer = pursuit.officer;
                        var officerAlive = UnityLife.IsAlive(cachedOfficer);
                        var officerOwned = officerAlive && cachedOfficer == officer;
                        var pursuitState = new OcOfficerPursuitState(officerAlive, officerOwned,
                            officerOwned && UnityLife.IsAlive(cachedOfficer!.PoliceBelt),
                            UnityLife.IsAlive(pursuit.Weapon_Baton), UnityLife.IsAlive(pursuit.Weapon_Taser),
                            UnityLife.IsAlive(pursuit.Weapon_Gun));
                        missing.AddRange(OcOfficerPursuitIssues.Read(pursuitState));
                    }
                }
            }
            if (UnityLife.IsAlive(avatar))
            {
                var current = avatar!.CurrentEquippable;
                if (UnityLife.IsAlive(current) && !Inside(root, current)) missing.Add("equippable.currentOwner");
            }
            return $"dependencies: {(missing.Count == 0 ? "ok" : "missing=" + string.Join(",", missing))}; active={root.activeInHierarchy}; movement enabled={movement?.enabled}; GUID={officer.GUID}";
        }
        catch (Exception exception) { return $"dependencies: unknown ({exception.GetType().Name})"; }
    }
}
