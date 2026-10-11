namespace OrganizedCrime.Runtime.Police;

// Engine-free ordering and ownership policy; adapters supply native operations.
internal static class OcOfficerCloneLifetime
{
    internal static void RejectCleanup(string reason, Action<string>? warn)
    {
        try { warn?.Invoke(reason); }
        catch { }
        throw new InvalidOperationException(reason);
    }

    // PursuitBehaviour also inherits CombatBehaviour: validate the entire owned set.
    internal static T[] OwnedCombat<T>(T[] behaviours, Func<T, bool> owned)
    {
        if (behaviours.Length == 0 || behaviours.Any(behaviour => !owned(behaviour)))
            throw new InvalidOperationException("Clone needs a nonempty owned CombatBehaviour set.");
        return behaviours;
    }

    internal static string NewGuid() => Guid.NewGuid().ToString("D");
    internal static void Prepare(Action stripCopies, Action bindDependencies, Action freshGuid)
    {
        stripCopies();
        bindDependencies();
        freshGuid();
    }
    internal static void Quiesce(Action unequip, Action stopUpdates)
    {
        try { unequip(); }
        finally { stopUpdates(); }
    }
    internal static void RetireMissingRoot(Action stop, Action despawn, Action destroy, Action forget)
    {
        foreach (var cleanup in new[] { stop, despawn, destroy, forget })
        {
            try { cleanup(); }
            catch { }
        }
    }

    internal static IReadOnlyList<T> StaleCopies<T>(IEnumerable<T> items, Func<T, bool> owned, Func<T, bool> alive, Func<T, bool> boundToClone) =>
        Copies(items, owned, alive).Where(item => !boundToClone(item)).ToArray();

    internal static IReadOnlyList<T> StaleCopies<T>(IEnumerable<T> items, Func<T, bool> owned, Func<T, bool> alive,
        Func<T, bool> boundToClone, Func<T, bool> requiredForCombat) =>
        Copies(items, owned, alive).Where(item => !requiredForCombat(item) && !boundToClone(item)).ToArray();

    internal static IReadOnlyList<T> Copies<T>(IEnumerable<T> items, Func<T, bool> owned, Func<T, bool> alive) =>
        items.Where(item => owned(item) && alive(item)).ToArray();
}

internal interface IOcOfficerGuidWorld
{
    void ClearOwnedGuids();
}

internal interface IOcOfficerDependencyWorld
{
    void PollDependencies();
    void ResetDependencyWarnings();
    string ReadDependencies(OcOfficerCloneRef clone);
}

internal readonly record struct OcOfficerDependencyState(bool Avatar, bool Movement, bool Agent, bool MovementOwner, bool SpeedController, bool OffMeshLink = true);
internal readonly record struct OcEquippableDependency(string Id, bool Avatar, bool Light, bool RequiredCombatWeapon = false);

internal readonly record struct OcOfficerCombatState(bool Behaviour, bool VirtualWeapon, bool VirtualWeaponOwned,
    bool CurrentWeapon, bool CurrentWeaponOwned, bool CurrentAvatar, string? ComponentKind = null);

internal static class OcOfficerCombatIssues
{
    internal static IReadOnlyList<string> Read(OcOfficerCombatState state)
    {
        var missing = new List<string>();
        if (!state.Behaviour) missing.Add("combat.behaviour");
        else
        {
            if (!state.VirtualWeapon) missing.Add("combat.VirtualPunchWeapon");
            else if (!state.VirtualWeaponOwned) missing.Add("combat.VirtualPunchWeapon.owner");
            // Absence of a drawn weapon is valid; the virtual fallback is mandatory.
            if (state.CurrentWeapon)
            {
                if (!state.CurrentWeaponOwned) missing.Add("combat.currentWeapon.owner");
                if (!state.CurrentAvatar) missing.Add("combat.currentWeapon.avatar");
            }
        }
        return state.ComponentKind is null ? missing :
            missing.Select(issue => $"{issue}@{state.ComponentKind}").ToArray();
    }
}

internal readonly record struct OcOfficerPursuitState(bool Officer, bool OfficerOwned, bool PoliceBelt,
    bool Baton, bool Taser, bool Gun);

internal static class OcOfficerPursuitIssues
{
    internal static IReadOnlyList<string> Read(OcOfficerPursuitState state)
    {
        var missing = new List<string>();
        if (!state.Officer) missing.Add("combat.pursuit.officer");
        else if (!state.OfficerOwned) missing.Add("combat.pursuit.officer.owner");
        else if (!state.PoliceBelt) missing.Add("combat.pursuit.officer.PoliceBelt");
        // These are reference weapons used for AssetPath comparisons, not the
        // drawn currentWeapon. Do not impose clone/avatar ownership on prefabs.
        if (!state.Baton) missing.Add("combat.pursuit.Weapon_Baton");
        if (!state.Taser) missing.Add("combat.pursuit.Weapon_Taser");
        if (!state.Gun) missing.Add("combat.pursuit.Weapon_Gun");
        return missing;
    }
}

internal static class OcOfficerDependencyIssues
{
    internal static List<string> Read(OcOfficerDependencyState state, IEnumerable<OcEquippableDependency> equipment, bool awaitingCombatStart = false)
    {
        var missing = new List<string>();
        if (!state.Avatar) missing.Add("avatar");
        if (!state.Movement) missing.Add("movement");
        else
        {
            if (!state.Agent) missing.Add("movement.agent");
            if (!state.MovementOwner) missing.Add("movement.npc");
            if (!state.SpeedController) missing.Add("movement.speedController");
            if (!state.OffMeshLink) missing.Add("movement.offMeshLink");
        }
        foreach (var item in equipment)
        {
            if (!item.Avatar && !(awaitingCombatStart && item.RequiredCombatWeapon)) missing.Add($"equippable.avatar@{item.Id}");
            if (!item.Light) missing.Add($"equippable.Light@{item.Id}");
        }
        return missing;
    }
}

internal sealed class OcOfficerDependencyCheck
{
    private long? _last;
    private bool _warned;
    internal void Reset() { _last = null; _warned = false; }
    internal void Poll(bool host, long now, Func<IReadOnlyList<string>> failures, Action<string> warn)
    {
        if (!host || (_last is { } last && now >= last && now - last < 5000)) return;
        _last = now;
        IReadOnlyList<string> issues;
        try { issues = failures(); }
        catch (Exception exception) { issues = new[] { $"dependency scan unreadable ({exception.GetType().Name})" }; }
        if (_warned || issues.Count == 0) return;
        _warned = true;
        try { warn($"OC clone dependency failure: {string.Join("; ", issues)}. Warning once per load; use oc_ch3 officers status."); }
        catch { }
    }
}

// Count repeated observations without repeating warnings for a clone/problem.
internal sealed class OcOfficerDependencyWarnings
{
    private readonly Dictionary<(long Pointer, int Serial, string Problem), long> _counts = new();
    internal void Reset() => _counts.Clear();
    internal void Observe(OcOfficerCloneRef clone, IEnumerable<string> problems, Action<string> warn)
    {
        foreach (var problem in problems.Distinct(StringComparer.Ordinal))
        {
            var key = (clone.Pointer, clone.Serial, problem);
            var count = _counts.GetValueOrDefault(key);
            _counts[key] = count + 1;
            if (count != 0) continue;
            try { warn(problem); }
            catch { }
        }
    }
    internal string Status(OcOfficerCloneRef clone)
    {
        var counts = _counts.Where(pair => pair.Key.Pointer == clone.Pointer && pair.Key.Serial == clone.Serial).ToArray();
        return $"dependency warnings={counts.Length}; observations={counts.Sum(pair => pair.Value)}";
    }
    internal string CombatStatus(OcOfficerCloneRef clone)
    {
        var counts = _counts.Where(pair => pair.Key.Pointer == clone.Pointer && pair.Key.Serial == clone.Serial &&
            pair.Key.Problem.StartsWith("combat.", StringComparison.Ordinal)).ToArray();
        return $"combat warnings={counts.Length}; observations={counts.Sum(pair => pair.Value)}";
    }
    internal static IEnumerable<string> Problems(string status)
    {
        if (status.StartsWith("dependencies: ok;", StringComparison.Ordinal)) return Array.Empty<string>();
        var start = status.IndexOf("missing=", StringComparison.Ordinal);
        if (start < 0) return new[] { status };
        var missing = status[(start + "missing=".Length)..].Split(';')[0];
        return missing.Split(',', StringSplitOptions.RemoveEmptyEntries);
    }
}

// Retain failed native deregistrations for a later teardown retry.
internal sealed class OcOfficerGuidOwnership
{
    private readonly Dictionary<long, HashSet<Guid>> _owned = new();
    internal int Count => _owned.Values.Sum(guids => guids.Count);
    internal void Track(long pointer, Guid guid)
    {
        if (pointer == 0 || guid == Guid.Empty) throw new ArgumentException("Clone identity unavailable.");
        if (!_owned.TryGetValue(pointer, out var guids)) _owned[pointer] = guids = new();
        guids.Add(guid);
    }
    internal void Release(long pointer, Action<Guid, long> deregister)
    {
        if (!_owned.TryGetValue(pointer, out var guids)) return;
        foreach (var guid in guids.ToArray())
        {
            try { deregister(guid, pointer); guids.Remove(guid); }
            catch { }
        }
        if (guids.Count == 0) _owned.Remove(pointer);
    }
    internal void Clear(Action<Guid, long> deregister)
    {
        foreach (var pointer in _owned.Keys.ToArray()) Release(pointer, deregister);
    }
}

// Preparation failures happen before a native pointer/serial is registered.
// Use the requested clone/template ID to bound retry warnings per load.
internal sealed class OcOfficerCleanupWarnings
{
    private readonly HashSet<(string CloneId, string Reason)> _warned = new();
    internal void Reset() => _warned.Clear();
    internal void Observe(string cloneId, string reason, Action<string> warn)
    {
        if (!_warned.Add((cloneId, reason))) return;
        try { warn($"OC clone cleanup rejected: clone={cloneId} reason={reason}. Warning once per load per clone/reason."); }
        catch { }
    }
}
