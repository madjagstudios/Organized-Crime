using OrganizedCrime.Runtime.Police;
using System.Diagnostics;
using System.Globalization;

namespace OrganizedCrime.Runtime;

public enum OcOfficerBindStatus
{
    Bound,
    SlotNotActive,
    InvalidCheckpoint,
    NotHost,
    WorldFailed,
    Disposed
}

/// <summary>
/// Owns every police officer OC adds to the world. Callers request slots by
/// owner key, release them, and bind live clones to checkpoints; the service
/// spreads spawn work to one slot per host <see cref="Update"/> pass, retries
/// failed spawns across frames, and respawns knocked-out clones after
/// <see cref="OcOfficerRoster.KnockoutRespawnMinutes"/> game minutes while
/// their slot is still held. No public member throws: world failures become
/// statuses, and even a throwing world is contained.
/// </summary>
public sealed class OcOfficerRosterService : IDisposable
{
    private readonly IOcOfficerWorld _world;
    private readonly OcOfficerRoster _roster;
    private readonly Action<string> _log;
    private readonly string _sessionId;
    private readonly string _templateId;
    private readonly List<OcOfficerCloneRef> _orphans = new();
    private long _updateIndex;
    private bool _saveSuspended;
    private readonly HashSet<int> _saveRespawnPending = new();
    private readonly Func<double> _nowMilliseconds;
    private int _saveRespawnTotal;
    private int _saveRespawnCompleted;
    private int _saveRespawnFailed;
    private int _saveRespawnReleased;
    private double _saveRespawnStarted;
    private bool _saveRespawnReporting;
    private const int TemplateRebuildsPerGameHour = 3;
    private bool _ensureWarned;
    private readonly HashSet<string> _templateFailures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _spawnReadySince = new(StringComparer.Ordinal);
    private readonly HashSet<string> _spawnStallWarned = new(StringComparer.Ordinal);
    private double? _rebuildHour;
    private int _rebuildsThisHour;
    private bool _rebuildCapWarned;
    private bool _removalWarned;
    private bool _disposed;
    private readonly Dictionary<int, (OcPatrolDuty Duty, OcOfficerPosition Target)> _qaPatrolRequests = new();

    internal int PendingRemovalCount => _orphans.Count;
    internal void PollDependencies()
    {
        if (_disposed || _saveSuspended || !IsHostSafe()) return;
        try { if (_world is IOcOfficerDependencyWorld dependencies) dependencies.PollDependencies(); }
        catch { }
    }
    internal IOcOfficerPoolRecoveryWorld? OfficerPools => _world as IOcOfficerPoolRecoveryWorld;
    internal int OccupiedCount => Count + PendingRemovalCount;
    internal void SetPatrolCapacity(int capacity) => _roster.SetPatrolCapacity(capacity);
    internal IReadOnlyDictionary<int, OcPatrolDuty> PatrolDuties()
    {
        var result = new Dictionary<int, OcPatrolDuty>();
        if (_disposed) return result;
        var bindings = _world is IOcPatrolWorld patrol ? patrol.PatrolSnapshot() : Array.Empty<OcPatrolSnapshot>();
        foreach (var slot in _roster.Snapshot())
        {
            if (slot.OwnerKey.StartsWith(Chapter3.Chapter3PatrolService.OwnerPrefix, StringComparison.Ordinal))
                result[slot.Handle] = slot.OwnerKey.StartsWith(Chapter3.Chapter3PatrolService.OwnerPrefix + "Vehicle:", StringComparison.Ordinal)
                    ? OcPatrolDuty.Vehicle : OcPatrolDuty.Foot;
            else if (_qaPatrolRequests.TryGetValue(slot.Handle, out var request)) result[slot.Handle] = request.Duty;
            else if (_roster.TryGetLiveClone(slot.Handle, out var live))
            {
                var binding = bindings.FirstOrDefault(b => b.Officer == live);
                if (binding is not null) result[slot.Handle] = binding.Duty;
            }
        }
        return result;
    }
    internal OcOfficerWorldStatus TryReadPatrolState(int handle, out OcPatrolCloneState state)
    {
        state = new(false, false);
        if (_disposed || !IsHostSafe()) return OcOfficerWorldStatus.NotHost;
        if (!_roster.TryGetLiveClone(handle, out var live)) return OcOfficerWorldStatus.InvalidHandle;
        if (_world is not IOcPatrolWorld patrol) return OcOfficerWorldStatus.NotReady;
        try { return patrol.TryReadPatrolState(live, out state); }
        catch { return OcOfficerWorldStatus.Failed; }
    }

    internal OcOfficerWorldStatus TryBindPatrol(int handle, OcPatrolDuty duty, OcOfficerPosition target, out string detail)
    {
        detail = "patrol binding unavailable";
        if (_disposed) return OcOfficerWorldStatus.Rejected;
        if (!IsHostSafe()) return OcOfficerWorldStatus.NotHost;
        if (_world is not IOcPatrolWorld patrol) return OcOfficerWorldStatus.NotReady;
        var snapshot = FindSnapshot(handle);
        if (snapshot?.State != OcOfficerSlotState.Active || !_roster.TryGetLiveClone(handle, out var live))
            return OcOfficerWorldStatus.InvalidHandle;
        try { return patrol.TryBindPatrol(live, duty, target, out detail); }
        catch (Exception ex) { detail = ex.GetType().Name; return OcOfficerWorldStatus.Failed; }
    }

    internal OcOfficerWorldStatus TryUnbindPatrol(int handle, out string detail)
    {
        detail = "patrol binding unavailable";
        if (_disposed) return OcOfficerWorldStatus.Rejected;
        if (!IsHostSafe()) return OcOfficerWorldStatus.NotHost;
        if (_world is not IOcPatrolWorld patrol) return OcOfficerWorldStatus.NotReady;
        if (!_roster.TryGetLiveClone(handle, out var live)) return OcOfficerWorldStatus.InvalidHandle;
        try { return patrol.TryUnbindPatrol(live, out detail); }
        catch (Exception ex) { detail = ex.GetType().Name; return OcOfficerWorldStatus.Failed; }
    }

    internal Chapter3QaResult QaPatrolHere(OcPatrolDuty duty)
    {
        if (_disposed || !IsHostSafe()) return new(false, "patrol refused: host only, live roster required.");
        if (_world is not IOcPatrolWorld) return new(false, "patrol refused: world has no patrol binding.");
        if (TryReadPlayerPose(out var player, out var yaw) != OcOfficerWorldStatus.Ok || !player.IsFinite || !float.IsFinite(yaw))
            return new(false, "patrol refused: no finite player pose.");
        var status = TryRequest($"qa:patrol:{Guid.NewGuid():N}", QaSpawnPosition(player, yaw, 0, 1), out var handle);
        if (status != OcOfficerRequestStatus.Accepted) return new(false, $"patrol refused: {status}.");
        _qaPatrolRequests.Add(handle, (duty, player));
        return new(true, $"patrol {duty}: requested fresh clone [{handle}]; binding runs after spawn. Use oc_ch3 patrol status.");
    }

    internal Chapter3QaResult QaPatrolStop()
    {
        if (_disposed || !IsHostSafe()) return new(false, "patrol stop refused: host only, live roster required.");
        var stopped = 0;
        foreach (var snapshot in _roster.Snapshot())
        {
            if (!snapshot.OwnerKey.StartsWith("qa:patrol:", StringComparison.Ordinal)) continue;
            _qaPatrolRequests.Remove(snapshot.Handle);
            if (Release(snapshot.Handle)) stopped++;
        }
        var pending = _world is IOcPatrolWorld patrol ? patrol.PatrolSnapshot().Count(p => p.State == "cleanup pending") : 0;
        return new(pending == 0, $"patrol stop: released {stopped} QA slots; cleanup pending {pending} (see status).");
    }

    internal IReadOnlyList<string> QaPatrolStatus()
    {
        if (_disposed || !IsHostSafe()) return new[] { "patrol status: host only, live roster required." };
        var lines = new List<string>();
        var bindings = _world is IOcPatrolWorld patrol ? patrol.PatrolSnapshot() : Array.Empty<OcPatrolSnapshot>();
        lines.Add($"patrols: {bindings.Count}; leased cars {bindings.Count(p => p.CarLeased)}; waiting QA binds {_qaPatrolRequests.Count}.");
        if (_world is IOcPatrolWorld patrolWorld)
        {
            lines.Add($"OC patrol groups={patrolWorld.PatrolGroupCount()} (owned references); native group tracking=UNKNOWN.");
            lines.AddRange(patrolWorld.PatrolPoolStatus());
        }
        foreach (var snapshot in _roster.Snapshot())
        {
            if (!_roster.TryGetLiveClone(snapshot.Handle, out var live))
            {
                if (snapshot.OwnerKey.StartsWith("qa:patrol:", StringComparison.Ordinal))
                    lines.Add($"[{snapshot.Handle}] {snapshot.CloneId}: {snapshot.State}, duty pending, route pending, car leased=False.");
                continue;
            }
            var binding = bindings.FirstOrDefault(p => p.Officer == live);
            if (binding is null && !snapshot.OwnerKey.StartsWith("qa:patrol:", StringComparison.Ordinal)) continue;
            lines.Add($"[{snapshot.Handle}] {snapshot.CloneId}: duty={binding?.Duty.ToString() ?? "none"}, route={binding?.Route ?? "none"}, car leased={binding?.CarLeased ?? false}, state={binding?.State ?? "unbound"}.");
            if (TryReadBehaviourDiagnostics(snapshot.Handle, null, out var diagnostics))
            {
                lines.Add("  stack: " + string.Join("; ", diagnostics.EnabledTopFirst.Select(b => $"{b.Name} p{b.Priority} enabled={b.Enabled} active={b.Active}")));
                lines.Add($"  active: {diagnostics.ActiveName}.");
            }
            else lines.Add("  stack: unknown.");
        }
        foreach (var binding in bindings.Where(p => !_roster.Snapshot().Any(s =>
            _roster.TryGetLiveClone(s.Handle, out var live) && live == p.Officer)))
            lines.Add($"orphan {binding.Officer.Pointer}: duty={binding.Duty}, route={binding.Route}, car leased={binding.CarLeased}, state={binding.State}.");
        return lines;
    }

    private bool BindOneQaPatrol()
    {
        foreach (var request in _qaPatrolRequests.ToArray())
        {
            var slot = FindSnapshot(request.Key);
            if (slot is null || slot.State == OcOfficerSlotState.Failed)
            {
                _qaPatrolRequests.Remove(request.Key);
                if (slot is not null) Release(request.Key);
                continue;
            }
            if (slot.State != OcOfficerSlotState.Active) continue;
            _qaPatrolRequests.Remove(request.Key);
            var bound = TryBindPatrol(request.Key, request.Value.Duty, request.Value.Target, out var detail);
            _log($"OC patrol [{request.Key}]: {bound}; {detail}.");
            // D5a QA makes one attempt; D5b owns retry rules.
            if (bound != OcOfficerWorldStatus.Ok) Release(request.Key);
            return true;
        }
        return false;
    }

    public OcOfficerRosterService(IOcOfficerWorld world, string? sessionId = null, Action<string>? log = null)
        : this(world, sessionId, log, nowMilliseconds: null) { }

    internal OcOfficerRosterService(IOcOfficerWorld world, string? sessionId, Action<string>? log, Func<double>? nowMilliseconds)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _sessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N")[..8] : sessionId;
        _roster = new OcOfficerRoster(_sessionId);
        _templateId = $"oc_officer_{_sessionId}_template";
        _log = log ?? (_ => { });
        _nowMilliseconds = nowMilliseconds ?? (() => Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency);
        OfficerPools?.SetPoolRelease(ReleasePooledClone);
    }

    private void ReleasePooledClone(long pointer)
    {
        if (_disposed || !IsHostSafe()) return;
        foreach (var slot in _roster.Snapshot())
        {
            if (_roster.TryGetLiveClone(slot.Handle, out var live) && live.Pointer == pointer)
            {
                Release(slot.Handle);
                return;
            }
        }
        // Already released cleanup orphans retain their normal retry path.
    }

    public int Count => _disposed ? 0 : _roster.Count;

    public IReadOnlyList<OcOfficerSlotSnapshot> Snapshot() => _disposed
        ? Array.Empty<OcOfficerSlotSnapshot>()
        : _roster.Snapshot();

    public OcOfficerRequestStatus TryRequest(string? ownerKey, OcOfficerPosition position, out int handle)
    {
        handle = -1;
        if (_disposed)
            return OcOfficerRequestStatus.RefusedDisposed;
        if (!IsHostSafe())
            return OcOfficerRequestStatus.RefusedNotHost;
        if (string.IsNullOrWhiteSpace(ownerKey) || !position.IsFinite) return OcOfficerRequestStatus.RefusedInvalid;
        if (OccupiedCount >= _roster.Capacity) return OcOfficerRequestStatus.RefusedCap;
        return _roster.TryRequest(ownerKey, position, _updateIndex, out handle);
    }

    public bool Release(int handle)
    {
        if (_disposed)
            return false;
        if (!_roster.Release(handle, out var live))
            return false;
        if (_saveRespawnPending.Remove(handle)) _saveRespawnReleased++;
        _qaPatrolRequests.Remove(handle);
        if (live is { } clone)
        {
            TryUnmap(clone);
            RemoveOrOrphan(clone);
        }
        return true;
    }

    public OcOfficerBindStatus TryBindToCheckpoint(int handle, object? nativeCheckpoint, OcOfficerPosition? standPoint = null)
    {
        if (_disposed)
            return OcOfficerBindStatus.Disposed;
        if (nativeCheckpoint is null)
            return OcOfficerBindStatus.InvalidCheckpoint;
        var snapshot = FindSnapshot(handle);
        if (snapshot is null || snapshot.State != OcOfficerSlotState.Active || !_roster.TryGetLiveClone(handle, out var live))
            return OcOfficerBindStatus.SlotNotActive;
        if (!IsHostSafe())
            return OcOfficerBindStatus.NotHost;
        try
        {
            var status = _world.TryBindToCheckpoint(live, nativeCheckpoint, standPoint, out var detail);
            if (status != OcOfficerWorldStatus.Ok)
                _log($"OC officer {snapshot.CloneId}: checkpoint bind failed ({status}: {detail}).");
            return status == OcOfficerWorldStatus.Ok ? OcOfficerBindStatus.Bound : OcOfficerBindStatus.WorldFailed;
        }
        catch (Exception exception)
        {
            _log($"OC officer {snapshot.CloneId}: checkpoint bind threw ({exception.GetType().Name}).");
            return OcOfficerBindStatus.WorldFailed;
        }
    }

    public bool TryReadClonePosition(int handle, out OcOfficerPosition position)
    {
        position = default;
        if (_disposed)
            return false;
        if (!_roster.TryGetLiveClone(handle, out var live))
            return false;
        try
        {
            return _world.TryReadClonePosition(live, out position) == OcOfficerWorldStatus.Ok;
        }
        catch
        {
            position = default;
            return false;
        }
    }

    public bool TryReadBehaviourState(int handle, out string activeBehaviour, out bool checkpointActive)
    {
        activeBehaviour = "unknown";
        checkpointActive = false;
        if (_disposed)
            return false;
        if (!_roster.TryGetLiveClone(handle, out var live))
            return false;
        try
        {
            return _world.TryReadBehaviourState(live, out activeBehaviour, out checkpointActive) == OcOfficerWorldStatus.Ok;
        }
        catch
        {
            activeBehaviour = "unknown";
            checkpointActive = false;
            return false;
        }
    }

    public bool TryReadBoundState(int handle, object? ocCheckpoint, out OcOfficerBoundState state)
    {
        state = new OcOfficerBoundState(false, false, OcCheckpointKind.Unknown, null);
        if (_disposed)
            return false;
        if (!_roster.TryGetLiveClone(handle, out var live))
            return false;
        try
        {
            return _world.TryReadBoundState(live, ocCheckpoint, out state) == OcOfficerWorldStatus.Ok;
        }
        catch
        {
            state = new OcOfficerBoundState(false, false, OcCheckpointKind.Unknown, null);
            return false;
        }
    }

    public bool TryReadBehaviourDiagnostics(int handle, object? ocCheckpoint, out OcOfficerBehaviourDiagnostics diagnostics)
    {
        diagnostics = new OcOfficerBehaviourDiagnostics(Array.Empty<OcOfficerBehaviourInfo>(), "unknown", new OcOfficerCheckpointDetail(false, false, "unknown", OcCheckpointKind.Unknown));
        if (_disposed)
            return false;
        if (!_roster.TryGetLiveClone(handle, out var live))
            return false;
        try
        {
            return _world.TryReadBehaviourDiagnostics(live, ocCheckpoint, out diagnostics) == OcOfficerWorldStatus.Ok;
        }
        catch
        {
            diagnostics = new OcOfficerBehaviourDiagnostics(Array.Empty<OcOfficerBehaviourInfo>(), "unknown", new OcOfficerCheckpointDetail(false, false, "unknown", OcCheckpointKind.Unknown));
            return false;
        }
    }

    public bool TryReadNativeLeak(string? nativeLabel, out bool leaked)
    {
        leaked = false;
        if (_disposed)
            return false;
        try
        {
            return _world.TryReadNativeLeak(nativeLabel, out leaked, out _) == OcOfficerWorldStatus.Ok;
        }
        catch
        {
            leaked = false;
            return false;
        }
    }

    public void Update()
    {
        try { UpdateCore(); }
        finally { ObserveSpawnWait(); CompleteSaveRespawnIfReady(); }
    }

    private void ObserveSpawnWait()
    {
        var waiting = _roster.Snapshot().Where(slot => slot.State is OcOfficerSlotState.Requested or OcOfficerSlotState.Spawning).ToArray();
        if (_disposed || _saveSuspended || waiting.Length == 0 || !IsHostSafe() ||
            TryReadTemplate(out var template) != OcOfficerWorldStatus.Ok || !template.Registered)
        {
            _spawnReadySince.Clear();
            _spawnStallWarned.Clear();
            return;
        }
        var ids = waiting.Select(slot => slot.CloneId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _spawnReadySince.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _spawnReadySince.Remove(id);
            _spawnStallWarned.Remove(id);
        }
        var now = _nowMilliseconds();
        foreach (var slot in waiting)
        {
            if (!_spawnReadySince.TryGetValue(slot.CloneId, out var since))
            {
                _spawnReadySince[slot.CloneId] = now;
                continue;
            }
            if (now - since <= 30000d || !_spawnStallWarned.Add(slot.CloneId)) continue;
            _log($"Warning: OC officer {slot.CloneId} ({slot.OwnerKey}): spawn work stalled more than 30s after template ready; " +
                $"state={slot.State}, attempts={slot.Attempts}, pending removals={_orphans.Count}.");
        }
    }

    private void UpdateCore()
    {
        if (_disposed || _saveSuspended)
            return;
        if (!IsHostSafe())
            return;
        // OC-174: sweep even with no roster slots or a pending QA bind early return.
        // World ownership includes unbound clones and cleanup orphans.
        try { if (_world is IOcOfficerPoolWorld pools) pools.PollOfficerPools(); }
        catch { }
        _updateIndex++;
        if (BindOneQaPatrol()) return;

        var snapshots = _roster.Snapshot();
        var haveMinutes = false;
        var minutes = 0d;
        if (snapshots.Count > 0)
        {
            haveMinutes = TryReadMinutes(out minutes);
        }

        foreach (var snapshot in snapshots)
        {
            if (snapshot.OwnerKey.StartsWith(Chapter3.Chapter3PatrolService.OwnerPrefix, StringComparison.Ordinal)) continue;
            if (snapshot.State != OcOfficerSlotState.Active || !_roster.TryGetLiveClone(snapshot.Handle, out var live))
                continue;
            var read = TryReadKnockedOut(live, out var knockedOut);
            if (read == OcOfficerWorldStatus.InvalidHandle)
            {
                _log($"OC officer {snapshot.CloneId}: clone is gone; its slot requeues.");
                _roster.MarkKnockedOut(snapshot.Handle, haveMinutes ? minutes : 0d);
            }
            else if (read == OcOfficerWorldStatus.Ok && knockedOut && haveMinutes)
            {
                _roster.MarkKnockedOut(snapshot.Handle, minutes);
            }
        }

        // One mutating world call per pass: drained orphans first, then one
        // knocked-out expiry, then one slot of spawn work.
        if (_orphans.Count > 0)
        {
            RemoveOrphan(_orphans[0]);
            return;
        }

        if (haveMinutes)
        {
            foreach (var snapshot in snapshots)
            {
                if (snapshot.State != OcOfficerSlotState.KnockedOut || snapshot.KnockedOutGameMinutes is not { } knockedAt)
                    continue;
                if (minutes - knockedAt < OcOfficerRoster.KnockoutRespawnMinutes)
                    continue;
                ExpireKnockedOut(snapshot);
                return;
            }
        }

        if (haveMinutes)
        {
            foreach (var snapshot in snapshots)
            {
                if (snapshot.State != OcOfficerSlotState.Failed)
                    continue;
                if (!_roster.IsRetryDue(snapshot.Handle, minutes))
                    continue;
                if (_roster.MarkRetryFromFailed(snapshot.Handle, _roster.MintCloneId(), _updateIndex))
                    _log($"OC officer slot {snapshot.Handle} ({snapshot.OwnerKey}) retrying after {OcOfficerRoster.FailedRetryMinutes} game minutes.");
                return;
            }
        }

        foreach (var snapshot in snapshots)
        {
            if (!_roster.IsAttemptDue(snapshot.Handle, _updateIndex))
                continue;
            SpawnOne(snapshot, haveMinutes ? minutes : (double?)null);
            return;
        }
    }

    public void OnSaveStart()
    {
        if (_disposed || _saveSuspended)
            return;
        ResetSaveRespawnReceipt();
        // QA duty is deliberately transient; D5b owns post-save profile restoration.
        QaPatrolStop();
        // Ordinary callers also lose duty before SuspendForSave clears live roster handles.
        foreach (var slot in _roster.Snapshot())
        {
            if (!slot.HasLiveClone) continue;
            var stopped = TryUnbindPatrol(slot.Handle, out var detail);
            if (_world is IOcPatrolWorld && stopped != OcOfficerWorldStatus.Ok)
                _log($"OC officer [{slot.Handle}]: save patrol cleanup {stopped}: {detail}; removal will retry.");
        }
        var suspended = 0;
        foreach (var slot in _roster.Snapshot())
            if (slot.HasLiveClone) _saveRespawnPending.Add(slot.Handle);
        foreach (var live in _roster.SuspendForSave())
        {
            RemoveOrOrphan(live);
            suspended++;
        }
        _saveSuspended = true;
        _saveRespawnTotal = suspended;
        OrganizedCrimeLog.Diagnostic($"OC officers: despawned {suspended} for save.", _log);
    }

    public void OnSaveComplete()
    {
        if (_disposed || !_saveSuspended)
            return;
        _saveSuspended = false;
        _saveRespawnStarted = _nowMilliseconds();
        _saveRespawnReporting = true;
        CompleteSaveRespawnIfReady();
    }

    private void CompleteSaveRespawnIfReady()
    {
        if (_disposed || _saveSuspended || !_saveRespawnReporting) return;
        foreach (var handle in _saveRespawnPending.ToArray())
        {
            var slot = FindSnapshot(handle);
            if (slot is null) _saveRespawnReleased++;
            else if (slot.State == OcOfficerSlotState.Active && slot.HasLiveClone) _saveRespawnCompleted++;
            else if (slot.State == OcOfficerSlotState.Failed) _saveRespawnFailed++;
            else continue;
            _saveRespawnPending.Remove(handle);
        }
        if (_saveRespawnPending.Count != 0) return;
        var elapsed = Math.Max(0d, _nowMilliseconds() - _saveRespawnStarted).ToString("F1", CultureInfo.InvariantCulture);
        OrganizedCrimeLog.Diagnostic($"OC officers: respawned {_saveRespawnCompleted} after save in {elapsed} ms " +
            $"(requested={_saveRespawnTotal}, failed={_saveRespawnFailed}, released={_saveRespawnReleased}).", _log);
        ResetSaveRespawnReceipt();
    }

    private void ResetSaveRespawnReceipt()
    {
        _saveRespawnPending.Clear();
        _saveRespawnReporting = false;
        _saveRespawnTotal = _saveRespawnCompleted = _saveRespawnFailed = _saveRespawnReleased = 0;
    }

    public void OnPreLoad()
    {
        if (_disposed)
            return;
        ClearAll();
    }

    public void OnLoadComplete()
    {
        if (_disposed)
            return;
        ClearAll();
        try { if (_world is IOcOfficerPoolWorld pools) pools.ResetOfficerPoolWarnings(); }
        catch { }
        try { if (_world is IOcOfficerDependencyWorld dependencies) dependencies.ResetDependencyWarnings(); }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ClearAll();
    }

    public IReadOnlyList<string> QaStatus()
    {
        try
        {
            return QaStatusCore();
        }
        catch (Exception exception)
        {
            return new[] { $"officers status unavailable: {exception.GetType().Name}." };
        }
    }

    public Chapter3QaResult QaSpawn(int count)
    {
        try
        {
            if (_disposed)
                return new Chapter3QaResult(false, "Chapter 3 QA officers spawn refused: the roster is disposed.");
            if (!IsHostSafe())
                return new Chapter3QaResult(false, "Chapter 3 QA officers spawn refused: host only.");
            if (count < 1 || count > OcOfficerRoster.MaxOfficers)
                return new Chapter3QaResult(false, $"Chapter 3 QA officers spawn refused: n must be 1-{OcOfficerRoster.MaxOfficers}.");
            if (TryReadPlayerPose(out var player, out var yaw) != OcOfficerWorldStatus.Ok)
                return new Chapter3QaResult(false, "Chapter 3 QA officers spawn refused: no player pose.");
            var accepted = 0;
            var refused = 0;
            for (var i = 0; i < count; i++)
            {
                var status = TryRequest($"qa:{i}", QaSpawnPosition(player, yaw, i, count), out _);
                if (status == OcOfficerRequestStatus.Accepted)
                    accepted++;
                else
                    refused++;
            }
            return accepted == 0
                ? new Chapter3QaResult(false, $"Chapter 3 QA officers spawn refused: 0 of {count} accepted.")
                : new Chapter3QaResult(true, $"Chapter 3 QA officers spawn: {accepted} of {count} accepted ({refused} refused).");
        }
        catch (Exception exception)
        {
            return new Chapter3QaResult(false, $"Chapter 3 QA officers spawn refused: {exception.GetType().Name}.");
        }
    }

    public Chapter3QaResult QaClear()
    {
        try
        {
            if (_disposed)
                return new Chapter3QaResult(false, "Chapter 3 QA officers clear refused: the roster is disposed.");
            var released = 0;
            foreach (var snapshot in _roster.Snapshot())
            {
                if (snapshot.OwnerKey.StartsWith("qa:", StringComparison.Ordinal) && Release(snapshot.Handle))
                    released++;
            }
            return new Chapter3QaResult(true, $"Chapter 3 QA officers clear: released {released} slots.");
        }
        catch (Exception exception)
        {
            return new Chapter3QaResult(false, $"Chapter 3 QA officers clear refused: {exception.GetType().Name}.");
        }
    }

    private static OcOfficerPosition QaSpawnPosition(OcOfficerPosition player, float playerYaw, int index, int count)
    {
        var yawRadians = playerYaw * Math.PI / 180d;
        var forwardX = Math.Sin(yawRadians);
        var forwardZ = Math.Cos(yawRadians);
        var lateral = (index - (count - 1) / 2d) * 1.5d;
        return new OcOfficerPosition(
            (float)(player.X + forwardX * 3d + forwardZ * lateral),
            player.Y,
            (float)(player.Z + forwardZ * 3d - forwardX * lateral),
            YawDegrees: (float)(((playerYaw + 180d + 540d) % 360d) - 180d));
    }

    private bool TrySpendTemplateRebuild(double? minutes)
    {
        if (minutes is not { } now)
            return true;
        var hour = Math.Floor(now / 60d);
        if (_rebuildHour != hour)
        {
            _rebuildHour = hour;
            _rebuildsThisHour = 0;
            _rebuildCapWarned = false;
        }
        if (_rebuildsThisHour >= TemplateRebuildsPerGameHour)
        {
            if (!_rebuildCapWarned)
            {
                _rebuildCapWarned = true;
                _log($"OC officer template: rebuild cap ({TemplateRebuildsPerGameHour} per game hour) reached; the fault counts against the slot.");
            }
            return false;
        }
        _rebuildsThisHour++;
        return true;
    }

    private void SpawnOne(OcOfficerSlotSnapshot snapshot, double? minutes)
    {
        var ensure = TryEnsureTemplate(out var detail);
        if (ensure != OcOfficerWorldStatus.Ok)
        {
            if (ensure != OcOfficerWorldStatus.NotReady && ensure != OcOfficerWorldStatus.NotHost)
            {
                if (_templateFailures.Add($"{ensure}: {detail}"))
                    _log($"Warning: OC officer template failed ({ensure}: {detail}); spawn work waits.");
                return;
            }
            if (!_ensureWarned)
            {
                _ensureWarned = true;
                _log($"OC officer template not ready ({ensure}: {detail}); spawn work waits.");
            }
            return;
        }
        _ensureWarned = false;
        _templateFailures.Clear();
        if (!_roster.MarkSpawning(snapshot.Handle, _updateIndex))
            return;
        var spawn = TrySpawnClone(snapshot.Position, snapshot.CloneId, out var live, out var spawnDetail);
        if (spawn == OcOfficerWorldStatus.Ok)
        {
            _roster.MarkActive(snapshot.Handle, live);
            return;
        }
        if (spawn == OcOfficerWorldStatus.TemplateFault && TrySpendTemplateRebuild(minutes))
        {
            TryDestroyTemplate();
            _roster.MarkSpawnAborted(snapshot.Handle, _updateIndex);
            _log($"OC officer {snapshot.CloneId}: template fault ({spawnDetail}); template rebuilt, attempt not counted.");
            return;
        }
        _roster.MarkAttemptFailed(snapshot.Handle, _updateIndex, minutes);
        var after = FindSnapshot(snapshot.Handle);
        if (after?.State == OcOfficerSlotState.Failed)
            _log($"OC officer slot {snapshot.Handle} ({snapshot.OwnerKey}) spawn failed after {OcOfficerRoster.MaxAttempts} attempts; slot retries in {OcOfficerRoster.FailedRetryMinutes} game minutes. Last: {spawnDetail}");
        else
            _log($"OC officer {snapshot.CloneId}: spawn attempt {after?.Attempts} failed ({spawn}: {spawnDetail}).");
    }

    private void ExpireKnockedOut(OcOfficerSlotSnapshot snapshot)
    {
        if (_roster.TryGetLiveClone(snapshot.Handle, out var live))
        {
            var removal = TryRemove(live, out var detail);
            if (removal != OcOfficerWorldStatus.Ok && removal != OcOfficerWorldStatus.InvalidHandle)
            {
                if (!_removalWarned)
                {
                    _removalWarned = true;
                    _log($"OC officer {snapshot.CloneId}: knocked-out removal failed ({removal}: {detail}); retrying.");
                }
                return;
            }
            _removalWarned = false;
        }
        if (_roster.MarkRespawnRequested(snapshot.Handle, _roster.MintCloneId(), _updateIndex, out _))
            _log($"OC officer {snapshot.CloneId}: knocked out for {OcOfficerRoster.KnockoutRespawnMinutes} game minutes; respawning.");
    }

    private void RemoveOrOrphan(OcOfficerCloneRef live)
    {
        var removal = TryRemove(live, out _);
        if (removal != OcOfficerWorldStatus.Ok && removal != OcOfficerWorldStatus.InvalidHandle)
            _orphans.Add(live);
        else
            _removalWarned = false;
    }

    private void RemoveOrphan(OcOfficerCloneRef orphan)
    {
        var removal = TryRemove(orphan, out var detail);
        if (removal == OcOfficerWorldStatus.Ok || removal == OcOfficerWorldStatus.InvalidHandle)
        {
            _orphans.Remove(orphan);
            _removalWarned = false;
            return;
        }
        if (!_removalWarned)
        {
            _removalWarned = true;
            _log($"OC officer orphan removal failed ({removal}: {detail}); retrying.");
        }
    }

    private void ClearAll()
    {
        ResetSaveRespawnReceipt();
        _templateFailures.Clear();
        _spawnReadySince.Clear();
        _spawnStallWarned.Clear();
        _qaPatrolRequests.Clear();
        try { if (_world is IOcOfficerGuidWorld guids) guids.ClearOwnedGuids(); }
        catch { }
        // Clear while live clones still exist, and before the non-host early return.
        // Teardown must not retain dead group/car/route wrappers across scenes.
        try { if (_world is IOcPatrolWorld patrol) patrol.ClearPatrols(); }
        catch { }
        TryClearSwapMap();
        var slots = _roster.Count;
        var lives = _roster.Clear();
        if (!IsHostSafe())
        {
            // OC-141: with no server the scene is already gone, so removals
            // are skipped and the clear logs one summary instead of
            // per-clone failures.
            _orphans.Clear();
            _saveSuspended = false;
            _removalWarned = false;
            _ensureWarned = false;
            _log($"OC officer clear: no server is up; dropped {slots} held slot(s) without removal.");
            return;
        }
        // Load boundaries and disposal attempt every removal once; a failure
        // here points at a dying world, so it is logged, not queued.
        foreach (var live in lives)
        {
            var removal = TryRemove(live, out var detail);
            if (removal != OcOfficerWorldStatus.Ok && removal != OcOfficerWorldStatus.InvalidHandle)
                _log($"OC officer clear: removal failed ({removal}: {detail}).");
        }
        foreach (var orphan in _orphans)
            TryRemove(orphan, out _);
        _orphans.Clear();
        _saveSuspended = false;
        _removalWarned = false;
        _ensureWarned = false;
        TryDestroyTemplate();
    }

    private IReadOnlyList<string> QaStatusCore()
    {
        if (_disposed)
            return new[] { "officers: the roster is disposed." };
        var lines = new List<string>
        {
            $"OC officers: {Count}/{_roster.Capacity} slots held (session {_sessionId}, {(IsHostSafe() ? "host" : "not host")})."
        };
        if (TryReadTemplate(out var template) == OcOfficerWorldStatus.Ok)
            lines.Add($"template: {template.TemplateId ?? "unknown"}, PrefabId {template.PrefabId}, {(template.Registered ? "registered" : template.Present ? "present, not registered" : "absent")}.");
        else
            lines.Add("template: unknown.");
        if (TryReadCounts(out var counts) == OcOfficerWorldStatus.Ok)
            lines.Add($"native: Officers {counts.Officers}, NPCRegistry {counts.Registry}.");
        else
            lines.Add("native counts unavailable.");
        var playerKnown = TryReadPlayerPose(out var player, out _) == OcOfficerWorldStatus.Ok;
        var haveMinutes = TryReadMinutes(out var minutes);
        foreach (var snapshot in _roster.Snapshot())
        {
            var position = snapshot.Position;
            if (snapshot.HasLiveClone &&
                _roster.TryGetLiveClone(snapshot.Handle, out var live) &&
                TryReadPosition(live, out var read) == OcOfficerWorldStatus.Ok)
                position = read;
            var line = $"[{snapshot.Handle}] {snapshot.OwnerKey} {snapshot.State} {snapshot.CloneId} attempts {snapshot.Attempts} " +
                $"at ({position.X:F1}, {position.Y:F1}, {position.Z:F1})" +
                (playerKnown ? $" {position.DistanceTo(player):F1} m from player" : " (player pose unknown)");
            if (snapshot.State == OcOfficerSlotState.KnockedOut)
                line += snapshot.KnockedOutGameMinutes is { } knockedAt && haveMinutes
                    ? $"; knocked out {minutes - knockedAt:F0} min ago"
                    : "; knocked out (clock unknown)";
            lines.Add(line + ".");
            if (snapshot.HasLiveClone && _world is IOcOfficerAttachmentDiagnostics attachments &&
                _roster.TryGetLiveClone(snapshot.Handle, out var attachmentClone))
                lines.Add($"[{snapshot.Handle}] {snapshot.CloneId} {OcOfficerAttachmentStatus.Read(attachments, attachmentClone)}.");
            if (snapshot.HasLiveClone && _world is IOcOfficerDependencyWorld dependencies &&
                _roster.TryGetLiveClone(snapshot.Handle, out var dependencyClone))
            {
                try { lines.Add($"[{snapshot.Handle}] {snapshot.CloneId} pointer={dependencyClone.Pointer} {dependencies.ReadDependencies(dependencyClone)}."); }
                catch { lines.Add($"[{snapshot.Handle}] {snapshot.CloneId} dependencies: unknown."); }
            }
        }
        if (_world is IOcOfficerDependencyWorld orphanDependencies)
            foreach (var orphan in _orphans)
            {
                try { lines.Add($"orphan pointer={orphan.Pointer} {orphanDependencies.ReadDependencies(orphan)}."); }
                catch { lines.Add($"orphan pointer={orphan.Pointer} dependencies: unknown."); }
            }
        return lines;
    }

    private OcOfficerSlotSnapshot? FindSnapshot(int handle)
    {
        foreach (var snapshot in _roster.Snapshot())
        {
            if (snapshot.Handle == handle)
                return snapshot;
        }
        return null;
    }

    private bool IsHostSafe()
    {
        try
        {
            return _world.IsHost;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadMinutes(out double minutes)
    {
        minutes = 0d;
        try
        {
            return _world.TryReadGameMinutes(out minutes) == OcOfficerWorldStatus.Ok;
        }
        catch
        {
            minutes = 0d;
            return false;
        }
    }

    private OcOfficerWorldStatus TryReadKnockedOut(OcOfficerCloneRef live, out bool knockedOut)
    {
        knockedOut = false;
        try
        {
            return _world.TryReadKnockedOut(live, out knockedOut);
        }
        catch
        {
            return OcOfficerWorldStatus.Failed;
        }
    }

    private OcOfficerWorldStatus TryEnsureTemplate(out string detail)
    {
        detail = string.Empty;
        try
        {
            return _world.TryEnsureTemplate(_templateId, out _, out detail);
        }
        catch (Exception exception)
        {
            detail = exception.GetType().Name;
            return OcOfficerWorldStatus.Failed;
        }
    }

    private OcOfficerWorldStatus TrySpawnClone(OcOfficerPosition position, string cloneId, out OcOfficerCloneRef live, out string detail)
    {
        live = default;
        detail = string.Empty;
        try
        {
            return _world.TrySpawnClone(position, cloneId, out live, out detail);
        }
        catch (Exception exception)
        {
            detail = exception.GetType().Name;
            return OcOfficerWorldStatus.Failed;
        }
    }

    private OcOfficerWorldStatus TryRemove(OcOfficerCloneRef live, out string detail)
    {
        detail = string.Empty;
        try
        {
            if (_world is IOcPatrolWorld patrol)
            {
                var stopped = patrol.TryUnbindPatrol(live, out detail);
                if (stopped != OcOfficerWorldStatus.Ok) return stopped;
            }
            return _world.TryRemoveClone(live, out detail);
        }
        catch (Exception exception)
        {
            detail = exception.GetType().Name;
            return OcOfficerWorldStatus.Failed;
        }
    }

    private void TryUnmap(OcOfficerCloneRef live)
    {
        try
        {
            _world.TryUnmapClone(live, out _);
        }
        catch
        {
        }
    }

    private void TryClearSwapMap()
    {
        try
        {
            _world.TryClearSwapMap(out _);
        }
        catch
        {
        }
    }

    private void TryDestroyTemplate()
    {
        try
        {
            var status = _world.TryDestroyTemplate(out var detail);
            if (status != OcOfficerWorldStatus.Ok)
                _log($"OC officer template destroy failed ({status}: {detail}).");
        }
        catch (Exception exception)
        {
            _log($"OC officer template destroy threw ({exception.GetType().Name}).");
        }
    }

    private OcOfficerWorldStatus TryReadTemplate(out OcOfficerTemplateStatus template)
    {
        template = new OcOfficerTemplateStatus(Present: false, Registered: false, PrefabId: 0, TemplateId: null);
        try
        {
            return _world.TryReadTemplateStatus(out template);
        }
        catch
        {
            return OcOfficerWorldStatus.Failed;
        }
    }

    private OcOfficerWorldStatus TryReadCounts(out OcOfficerNativeCounts counts)
    {
        counts = new OcOfficerNativeCounts(-1, -1);
        try
        {
            return _world.TryReadNativeCounts(out counts);
        }
        catch
        {
            return OcOfficerWorldStatus.Failed;
        }
    }

    private OcOfficerWorldStatus TryReadPlayerPose(out OcOfficerPosition position, out float yaw)
    {
        position = default;
        yaw = 0f;
        try
        {
            return _world.TryReadPlayerPose(out position, out yaw);
        }
        catch
        {
            return OcOfficerWorldStatus.Failed;
        }
    }

    private OcOfficerWorldStatus TryReadPosition(OcOfficerCloneRef live, out OcOfficerPosition position)
    {
        position = default;
        try
        {
            return _world.TryReadClonePosition(live, out position);
        }
        catch
        {
            return OcOfficerWorldStatus.Failed;
        }
    }
}
