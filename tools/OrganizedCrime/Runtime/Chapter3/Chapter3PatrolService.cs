using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Police;

namespace OrganizedCrime.Runtime.Chapter3;

internal sealed record Chapter3PatrolInput(bool IsHost, bool Ready, double? Minutes,
    LocalPressureTier? Tier, bool Protected, bool Lockdown, OcOfficerPosition? Target,
    int CheckpointDesired, Chapter3PatrolProfile? Surge = null);

// Main-thread only. Composition invokes Poll through the existing host 1 Hz timer.
// Desired profiles are transient; the shared roster remains the sole clone owner.
internal sealed class Chapter3PatrolService : IDisposable
{
    internal const string OwnerPrefix = "patrol:auto:";
    private readonly OcOfficerRosterService _roster;
    private readonly Func<Chapter3PatrolInput> _read;
    private readonly Action<string> _log;
    private readonly Action<string> _warn;
    private readonly HashSet<Type> _readWarnings = new();
    private readonly Dictionary<int, (string CloneId, OcOfficerPosition Target)> _bound = new();
    private Chapter3PatrolProfile _profile = Chapter3PatrolRules.None;
    private int _cap = Chapter3PatrolRules.OrdinaryCap;
    private int _checkpointDesired;
    private long _poll;
    private long _nextAction;
    private int _serial;
    private bool _enabled = true;
    private bool _suspended;
    private bool _disposed;
    private string? _lastFailure;

    internal Chapter3PatrolService(OcOfficerRosterService roster, Func<Chapter3PatrolInput> read,
        Action<string>? log = null, Action<string>? warn = null)
    {
        _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _log = log ?? (static _ => { });
        _warn = warn ?? _log;
    }

    internal void Poll()
    {
        if (_disposed || _suspended) return;
        Chapter3PatrolInput input;
        try { input = _read(); }
        catch (Exception ex)
        {
            if (_readWarnings.Add(ex.GetType()))
            {
                try { _warn($"OC automatic patrol input read failed ({ex.GetType().FullName}); current duty retained. Warning once per exception type per load."); }
                catch { } // Diagnostics must not turn an unavailable read into a throwing poll.
            }
            return;
        }
        if (!input.IsHost) return;
        _poll++;
        if (!input.Ready) return;
        _profile = _enabled
            ? Chapter3PatrolRules.Select(input.Tier, input.Protected, input.Lockdown, input.Minutes, input.Surge)
            : Chapter3PatrolRules.None;
        _cap = input.Lockdown && !input.Protected ? Chapter3PatrolRules.LockdownCap : Chapter3PatrolRules.OrdinaryCap;
        _checkpointDesired = Math.Max(0, input.CheckpointDesired);
        _roster.SetPatrolCapacity(_cap);

        var all = _roster.Snapshot();
        var own = all.Where(IsOurs).ToArray();
        foreach (var stale in _bound.Keys.Where(h => !own.Any(s => s.Handle == h)).ToArray())
            _bound.Remove(stale);
        // Reserve checkpoint demand before D3 finishes its minute-throttled spawning.
        var checkpoints = all.Count(IsCheckpoint);
        var other = all.Count - own.Length + _roster.PendingRemovalCount;
        var reserved = Math.Max(0, _checkpointDesired - checkpoints);
        var budget = Math.Min(_profile.Total, Math.Max(0, _cap - other - reserved));
        var reads = own.Select(s => ReadSlot(s)).ToArray();
        if (_poll < _nextAction) return;

        // Busy/unknown live reads retain slots even when protection/window/profile changes.
        // A dead/KO/gone clone cannot respond and is removed before any replacement.
        foreach (var entry in reads)
        {
            if (entry.Unavailable || entry.Slot.State == OcOfficerSlotState.Failed)
            {
                Release(entry.Slot.Handle);
                return;
            }
        }
        var kept = reads.Where(s => s.Held).ToList();
        foreach (var entry in reads.Where(s => !s.Held))
        {
            var desired = entry.Duty == OcPatrolDuty.Foot ? _profile.Foot : _profile.Vehicle;
            if (kept.Count >= budget || kept.Count(s => s.Duty == entry.Duty) >= desired)
            {
                Release(entry.Slot.Handle);
                return;
            }
            kept.Add(entry);
        }
        if (input.Target is not { IsFinite: true } target) return;
        // Occupied responses of either duty consume the whole patrol budget.
        // This also prevents adding a new foot patrol beside a retained response driver.
        if (kept.Count < budget && _roster.OccupiedCount < _cap)
        {
            var duty = kept.Count(s => s.Duty == OcPatrolDuty.Foot) < _profile.Foot
                ? OcPatrolDuty.Foot : OcPatrolDuty.Vehicle;
            var result = _roster.TryRequest($"{OwnerPrefix}{duty}:{_serial++}", target, out _);
            ActionTaken(result == OcOfficerRequestStatus.Accepted ? null : result.ToString());
            return;
        }
        foreach (var entry in kept)
        {
            if (entry.Held || entry.Slot.State != OcOfficerSlotState.Active) continue;
            if (_bound.TryGetValue(entry.Slot.Handle, out var bound) &&
                bound.CloneId == entry.Slot.CloneId && bound.Target == target) continue;
            var result = _roster.TryBindPatrol(entry.Slot.Handle, entry.Duty, target, out var detail);
            if (result == OcOfficerWorldStatus.Ok)
                _bound[entry.Slot.Handle] = (entry.Slot.CloneId, target);
            ActionTaken(result == OcOfficerWorldStatus.Ok ? null : $"{result}: {detail}");
            return;
        }
    }

    private sealed record SlotRead(OcOfficerSlotSnapshot Slot, OcPatrolDuty Duty,
        bool Unavailable, bool Held, bool Busy);

    private SlotRead ReadSlot(OcOfficerSlotSnapshot slot)
    {
        var duty = slot.OwnerKey.StartsWith(OwnerPrefix + "Vehicle:", StringComparison.Ordinal)
            ? OcPatrolDuty.Vehicle : OcPatrolDuty.Foot;
        if (!slot.HasLiveClone) return new(slot, duty, false, false, false);
        var result = _roster.TryReadPatrolState(slot.Handle, out var state);
        if (result == OcOfficerWorldStatus.InvalidHandle) return new(slot, duty, true, false, false);
        if (result != OcOfficerWorldStatus.Ok) return new(slot, duty, false, true, false);
        return new(slot, duty, state.Unavailable, !state.Unavailable && state.Busy, !state.Unavailable && state.Busy);
    }

    private void Release(int handle)
    {
        _roster.Release(handle); // D5a stop/return precedes native removal; failures remain orphans.
        _bound.Remove(handle);
        ActionTaken(null);
    }

    private void ActionTaken(string? failure)
    {
        _nextAction = _poll + Chapter3PatrolRules.RetryPolls;
        if (failure is not null && failure != _lastFailure) _log($"OC automatic patrol held: {failure}.");
        _lastFailure = failure;
    }

    internal bool IsHost => !_disposed && _read().IsHost;

    internal Chapter3QaResult SetAuto(bool enabled)
    {
        if (_disposed || !_read().IsHost) return new(false, "patrol auto refused: host only, live service required.");
        _enabled = enabled;
        return new(true, $"patrol auto {(enabled ? "on" : "off")}; convergence runs at the next eligible host poll.");
    }

    internal IReadOnlyList<string> Status()
    {
        if (_disposed || !_read().IsHost) return new[] { "patrol status: host only, live service required." };
        var all = _roster.Snapshot();
        var own = all.Where(IsOurs).Select(ReadSlot).ToArray();
        var duties = _roster.PatrolDuties();
        var busy = duties.Keys.Count(h => _roster.TryReadPatrolState(h, out var state) == OcOfficerWorldStatus.Ok && !state.Unavailable && state.Busy);
        // Counts include QA bindings, held responses and pending clone slots.
        var foot = duties.Values.Count(d => d == OcPatrolDuty.Foot);
        var vehicle = duties.Values.Count(d => d == OcPatrolDuty.Vehicle);
        var occupied = _roster.OccupiedCount;
        var effectiveCap = Math.Max(_cap, occupied);
        var draining = occupied > _cap
            ? $" (draining: {occupied - _cap} over cap; {busy} responding officers keep their slots)"
            : string.Empty;
        return new[]
        {
            $"patrol auto={(_enabled ? "on" : "off")}, profile={_profile.Name}, cap={_cap}, occupied={occupied}, slots {occupied}/{_cap}{draining}, draining cap={effectiveCap}, busy-in-response={busy}, unknown-held={own.Count(s => s.Held && !s.Busy)}.",
            $"checkpoint desired={_checkpointDesired} actual={all.Count(IsCheckpoint)}; foot desired={_profile.Foot} actual={foot}; vehicle desired={_profile.Vehicle} actual={vehicle}."
        };
    }

    private static bool IsOurs(OcOfficerSlotSnapshot slot) => slot.OwnerKey.StartsWith(OwnerPrefix, StringComparison.Ordinal);
    private static bool IsCheckpoint(OcOfficerSlotSnapshot slot) => slot.OwnerKey.StartsWith("checkpoint", StringComparison.Ordinal);

    internal void OnSaveStart()
    {
        if (_disposed) return;
        _suspended = true;
        ReleaseAll();
    }
    internal void OnSaveComplete() { if (!_disposed) _suspended = false; }
    internal void OnPreLoad() { if (!_disposed) { _suspended = true; ReleaseAll(); } }
    internal void OnLoadComplete() { if (!_disposed) { _bound.Clear(); _readWarnings.Clear(); _profile = Chapter3PatrolRules.None; _suspended = false; } }
    private void ReleaseAll()
    {
        foreach (var slot in _roster.Snapshot().Where(IsOurs)) _roster.Release(slot.Handle);
        _bound.Clear();
        _profile = Chapter3PatrolRules.None;
        // Do not reset the retry clock across save/load or QA toggles.
    }
    public void Dispose() { if (_disposed) return; ReleaseAll(); _disposed = true; }
}
