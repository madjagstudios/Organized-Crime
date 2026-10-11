using OrganizedCrime.Runtime.Release1.World;
using System.Globalization;
using OrganizedCrime.Core.Diagnostics;
using OrganizedCrime.Core.Scheduling;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>One officer's bind progress: Binding until the checkpoint behaviour is Active and the clone is at its stand point.</summary>
public enum Chapter3BindState
{
    Binding,
    Bound,
}

/// <summary>One held officer: its roster handle, bind state, active behaviour, and distance to its stand point.</summary>
public sealed record Chapter3CheckpointOfficerEntry(int RosterHandle, Chapter3BindState BindState, string ActiveBehaviour, double? DistanceM);

/// <summary>One managed OC checkpoint: its spot label, world handle, and held officers.</summary>
public sealed record Chapter3CheckpointStatusEntry(string Label, string? WorldHandle, bool Spawned, IReadOnlyList<Chapter3CheckpointOfficerEntry> Officers);

/// <summary>
/// The checkpoint service's read-only view for QA: the tier, protection, the
/// daily target, the desired active set, every managed and test checkpoint,
/// and the asserted native checkpoint label, if any.
/// </summary>
public sealed record Chapter3CheckpointSnapshot(
    LocalPressureTier? Tier,
    bool Protected,
    Chapter3CheckpointTarget Target,
    IReadOnlyList<string> ActiveLabels,
    IReadOnlyList<Chapter3CheckpointStatusEntry> Managed,
    IReadOnlyList<Chapter3CheckpointStatusEntry> Tests,
    string? AssertedNative);

/// <summary>
/// OC police checkpoints (OC-141, D3b). As local heat rises the service puts
/// up road checkpoints near the player's busiest property: it picks the
/// target once a day at 06:00, reconciles the planner's active set against
/// the spawned checkpoints at most one structural change per game minute,
/// staffs each with two roster officers, and re-asserts the set's native
/// checkpoint every pass. Host only. Pre-load and dispose clear local state
/// without host gates and without touching the world; load complete picks
/// and re-requests. A failed step warns once per cause and retries later.
/// </summary>
// OC-151: the checkpoint service's narrow view of the Chapter 3 runtime (the state
// revision plus the checkpoint target accessors). The service implements this, so the
// checkpoint service no longer names the concrete runtime type.
public interface IChapter3CheckpointHost
{
    Chapter3State? State { get; }
    Chapter3CheckpointTarget ReadCheckpointTarget();
    bool TryWriteCheckpointTarget(Chapter3CheckpointTarget target);
}

public sealed class Chapter3CheckpointService : IDisposable
{
    private const double PickHourMinutes = 360d;
    internal const int OfficersPerCheckpoint = 2;
    private const int NoHandle = -1;
    private const string ManagedOwnerPrefix = "checkpoint";
    private const string TestOwnerPrefix = "checkpoint-test";
    private const double BindNearMeters = 3d;
    private const double BindTimeoutGameMinutes = 20d;

    private readonly IChapter3CheckpointHost _runtime;
    private readonly Release1ChiefService _chief;
    private readonly IProductionActivityMissionWorld _world;
    private readonly IChapter3CheckpointWorld _checkpoints;
    private readonly OcOfficerRosterService _roster;
    private readonly Action<string> _log;
    private readonly GameHourGate _throttle = new();
    private readonly Dictionary<string, ManagedCheckpoint> _managed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ManagedCheckpoint> _tests = new(StringComparer.Ordinal);
    private readonly OnceLog _warned;
    private IReadOnlyList<string> _activeLabels = Array.Empty<string>();
    private double? _lastStructuralMinute;
    private string? _assertedNative;
    private bool _needsPostLoadPick;
    private bool _disposed;

    public Chapter3CheckpointService(
        IChapter3CheckpointHost runtime,
        Release1ChiefService chief,
        IProductionActivityMissionWorld world,
        IChapter3CheckpointWorld checkpoints,
        OcOfficerRosterService roster,
        Action<string>? log = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _chief = chief ?? throw new ArgumentNullException(nameof(chief));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
        _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        _log = log ?? (_ => { });
        _warned = new OnceLog(_log);
    }

    public void Update()
    {
        if (_disposed) return;
        if (!IsHost()) return;
        var minutes = _world.TryReadCanonicalTotalMinutes(out var read) == Release1SmallCourtesyWorldReadStatus.Ready
            ? (double?)read : null;
        if (!_throttle.TryPass(minutes, _runtime.State?.Revision ?? -1)) return;
        var today = minutes is { } current ? (int)Math.Floor(current / 1440d) : (int?)null;
        MaybePick(minutes, today);
        Reconcile(minutes);
        foreach (var test in _tests.Values)
            EnsureOfficers(test, minutes);
    }

    public void OnSaveStart()
    {
        if (!_disposed) _throttle.Clear();
    }

    public void OnSaveComplete()
    {
        if (_disposed) return;
        _throttle.Clear();
        MarkBindsStale(_managed);
        MarkBindsStale(_tests);
    }

    private static void MarkBindsStale(Dictionary<string, ManagedCheckpoint> set)
    {
        foreach (var managed in set.Values)
            for (var i = 0; i < OfficersPerCheckpoint; i++)
            {
                managed.OfficerBindState[i] = Chapter3BindState.Binding;
                managed.BindStartMinutes[i] = null;
                managed.OfficerActiveName[i] = "unknown";
                managed.OfficerDistanceM[i] = null;
            }
    }

    public void OnPreLoad()
    {
        if (!_disposed) ClearLocal();
    }

    public void OnLoadComplete()
    {
        if (_disposed) return;
        _throttle.Clear();
        _needsPostLoadPick = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearLocal();
    }

    public Chapter3QaResult QaSpawnTest(string label)
    {
        if (_disposed)
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint test refused: Chapter 3 is not active.");
        if (string.IsNullOrWhiteSpace(label))
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint test refused: a label is required.");
        if (!IsHost())
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint test refused: not host.");
        var spot = Chapter3CheckpointSpots.Baked.FirstOrDefault(entry => string.Equals(entry.Label, label, StringComparison.Ordinal));
        if (spot is null)
            return new Chapter3QaResult(false, $"Chapter 3 QA checkpoint test refused: unknown spot '{label}'.");
        if (_tests.ContainsKey(label))
            return new Chapter3QaResult(false, $"Chapter 3 QA checkpoint test refused: a test checkpoint '{label}' already exists.");
        if (!TrySpawnInto(_tests, TestOwnerPrefix, label, spot))
            return new Chapter3QaResult(false, $"Chapter 3 QA checkpoint test refused: the spawn failed for '{label}'.");
        return new Chapter3QaResult(true, $"Test checkpoint spawned: {label}.");
    }

    public Chapter3QaResult QaSpawnTestAt(string label, float x, float y, float z, float yawDegrees)
    {
        if (_disposed)
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint test refused: Chapter 3 is not active.");
        if (string.IsNullOrWhiteSpace(label))
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint test refused: a label is required.");
        if (!IsHost())
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint test refused: not host.");
        if (_tests.ContainsKey(label))
            return new Chapter3QaResult(false, $"Chapter 3 QA checkpoint test refused: a test checkpoint '{label}' already exists.");
        if (!TrySpawnInto(_tests, TestOwnerPrefix, label, new Chapter3CheckpointSpot(label, x, y, z, yawDegrees)))
            return new Chapter3QaResult(false, $"Chapter 3 QA checkpoint test refused: the spawn failed for '{label}'.");
        return new Chapter3QaResult(true, $"Test checkpoint spawned: {label}.");
    }

    public Chapter3QaResult QaSpotHere(string label)
    {
        if (_disposed)
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint spot here refused: Chapter 3 is not active.");
        if (string.IsNullOrWhiteSpace(label))
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint spot here refused: a label is required.");
        if (!IsHost())
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint spot here refused: not host.");
        if (TryReadCameraPose(out var pose) != Chapter3CheckpointWorldStatus.Ok)
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint spot here refused: the camera pose could not be read.");
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "new Chapter3CheckpointSpot(\"{0}\", {1:F2}f, {2:F2}f, {3:F2}f, {4:F2}f),",
            label, pose.X, pose.Y, pose.Z, pose.YawDegrees);
        var spawn = QaSpawnTestAt(label, pose.X, pose.Y, pose.Z, pose.YawDegrees);
        return new Chapter3QaResult(
            spawn.Ok,
            spawn.Ok ? $"{line} Test checkpoint spawned: {label}." : $"{line} Test checkpoint spawn failed: {spawn.Message}");
    }

    public Chapter3QaResult QaClearTests()
    {
        if (_disposed)
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint clear refused: Chapter 3 is not active.");
        var cleared = 0;
        var failures = new List<string>();
        foreach (var label in _tests.Keys.OrderBy(label => label, StringComparer.Ordinal).ToList())
        {
            if (TryRemoveFrom(_tests, label))
                cleared++;
            else
                failures.Add(label);
        }
        return failures.Count == 0
            ? new Chapter3QaResult(true, $"Cleared {cleared} test checkpoint(s).")
            : new Chapter3QaResult(false, $"Chapter 3 QA checkpoint clear refused: {string.Join(", ", failures)} could not be removed.");
    }

    public Chapter3QaResult QaPickNow()
    {
        if (_disposed)
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint pick refused: Chapter 3 is not active.");
        if (!IsHost())
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint pick refused: not host.");
        if (_world.TryReadCanonicalTotalMinutes(out var read) != Release1SmallCourtesyWorldReadStatus.Ready)
            return new Chapter3QaResult(false, "Chapter 3 QA checkpoint pick refused: the clock could not be read.");
        return PickCore((int)Math.Floor(read / 1440d));
    }

    public IReadOnlyList<string> QaStatus()
    {
        try
        {
            return QaStatusCore();
        }
        catch (Exception exception)
        {
            return new[] { $"checkpoint status unavailable: {exception.GetType().Name}." };
        }
    }

    public Chapter3CheckpointSnapshot Snapshot()
    {
        if (_disposed)
            return new Chapter3CheckpointSnapshot(null, false, new Chapter3CheckpointTarget(null, null), Array.Empty<string>(), Array.Empty<Chapter3CheckpointStatusEntry>(), Array.Empty<Chapter3CheckpointStatusEntry>(), null);
        return new Chapter3CheckpointSnapshot(
            _chief.ReadTier(),
            _chief.IsProtected(),
            _runtime.ReadCheckpointTarget(),
            _activeLabels,
            StatusEntries(_managed),
            StatusEntries(_tests),
            _assertedNative);
    }

    private IReadOnlyList<string> QaStatusCore()
    {
        if (_disposed)
            return new[] { "checkpoints: the service is disposed." };
        var snapshot = Snapshot();
        var target = snapshot.Target.PropertyCode ?? "none";
        var day = snapshot.Target.PickDay is { } pickDay ? $" (day {pickDay})" : string.Empty;
        var lines = new List<string>
        {
            $"checkpoints: tier {snapshot.Tier?.ToString() ?? "unknown"}, protected {(snapshot.Protected ? "yes" : "no")}, target {target}{day}.",
            $"checkpoints: active {(snapshot.ActiveLabels.Count == 0 ? "none" : string.Join(", ", snapshot.ActiveLabels))}.",
        };
        foreach (var entry in snapshot.Managed)
        {
            lines.AddRange(DescribeEntry(entry, test: false));
            lines.AddRange(DescribeOfficerDiagnostics(entry));
        }
        foreach (var entry in snapshot.Tests)
        {
            lines.AddRange(DescribeEntry(entry, test: true));
            lines.AddRange(DescribeOfficerDiagnostics(entry));
        }
        lines.Add(snapshot.AssertedNative is null
            ? "checkpoints: no native asserted."
            : $"checkpoints: native {snapshot.AssertedNative} asserted.");
        if (snapshot.AssertedNative is not null
            && _roster.TryReadNativeLeak(snapshot.AssertedNative, out var leaked))
        {
            if (leaked)
            {
                WarnOnce($"native-leak-{snapshot.AssertedNative}", $"Checkpoint native {snapshot.AssertedNative} lists an OC clone in its AssignedNPCs.");
                lines.Add($"checkpoints: native {snapshot.AssertedNative} lists an OC clone!");
            }
            else
            {
                ClearWarn($"native-leak-{snapshot.AssertedNative}");
            }
        }
        return lines;
    }

    private static IEnumerable<string> DescribeEntry(Chapter3CheckpointStatusEntry entry, bool test)
    {
        var held = entry.Officers.Count;
        var bound = entry.Officers.Count(officer => officer.BindState == Chapter3BindState.Bound);
        var scope = test ? $" (test)" : string.Empty;
        yield return $"checkpoint {entry.Label}{scope}: {(entry.Spawned ? "spawned" : "missing")}, officers {held} held, {bound} bound.";
        foreach (var officer in entry.Officers)
        {
            var distance = officer.DistanceM is { } meters ? $"{meters.ToString("F1", CultureInfo.InvariantCulture)} m from stand" : "distance unknown";
            yield return $"checkpoint {entry.Label} officer {officer.RosterHandle}: {officer.BindState}, active {officer.ActiveBehaviour}, {distance}.";
        }
    }

    private IEnumerable<string> DescribeOfficerDiagnostics(Chapter3CheckpointStatusEntry entry)
    {
        if (entry.WorldHandle is null)
            yield break;
        if (TryReadSpawnedNative(entry.WorldHandle, out var native) != Chapter3CheckpointWorldStatus.Ok || native is null)
            yield break;
        foreach (var officer in entry.Officers)
        {
            if (officer.RosterHandle == NoHandle)
                continue;
            if (!_roster.TryReadBehaviourDiagnostics(officer.RosterHandle, native, out var diagnostics))
                continue;
            var stack = diagnostics.EnabledTopFirst.Count == 0
                ? "none"
                : string.Join(", ", diagnostics.EnabledTopFirst.Select(info => $"{info.Name} p{info.Priority}"));
            yield return $"checkpoint {entry.Label} officer {officer.RosterHandle} stack: {stack}; active {diagnostics.ActiveName}.";
            var target = diagnostics.Checkpoint.Kind switch
            {
                OcCheckpointKind.Ours => "OC",
                OcCheckpointKind.Native => "native",
                OcCheckpointKind.None => "none",
                _ => "unknown",
            };
            yield return $"checkpoint {entry.Label} officer {officer.RosterHandle} checkpoint: enabled {diagnostics.Checkpoint.Enabled}, active {diagnostics.Checkpoint.Active}, assigned {diagnostics.Checkpoint.Assigned}, target {target}.";
        }
    }

    private static IReadOnlyList<Chapter3CheckpointStatusEntry> StatusEntries(Dictionary<string, ManagedCheckpoint> set) =>
        set.Values
            .OrderBy(entry => entry.Label, StringComparer.Ordinal)
            .Select(entry => new Chapter3CheckpointStatusEntry(
                entry.Label,
                entry.WorldHandle,
                true,
                HeldOfficers(entry)))
            .ToList();

    private void MaybePick(double? minutes, int? today)
    {
        if (today is null || minutes is null) return;
        var target = _runtime.ReadCheckpointTarget();
        if (target.PickDay == today)
        {
            _needsPostLoadPick = false;
            return;
        }
        if (!_needsPostLoadPick && minutes.Value - today.Value * 1440d < PickHourMinutes) return;
        var pick = PickCore(today.Value);
        if (pick.Ok)
        {
            _needsPostLoadPick = false;
            _log(pick.Message);
        }
    }

    private Chapter3QaResult PickCore(int today)
    {
        if (_world.TryReadProductionActivity(out var activity) != Release1SmallCourtesyWorldReadStatus.Ready)
        {
            WarnOnce("pick-snapshot", "Checkpoint pick skipped: the production snapshot could not be read.");
            return new Chapter3QaResult(false, "Checkpoint pick skipped: the production snapshot could not be read.");
        }
        string? lastVisited = null;
        if (TryReadLastVisited(out var visited) != Chapter3CheckpointWorldStatus.Ok || visited is null)
            WarnOnce("pick-lastvisited", "Checkpoint pick runs without the last-visited property; ties fall back to code order.");
        else
            lastVisited = visited;
        var scored = activity.Properties
            .Where(property => property is not null)
            .Select(property => new Chapter3CheckpointScoredProperty(property.PropertyCode, property.Employees.Count, property.Stations.Count))
            .ToList();
        var code = Chapter3CheckpointPlanner.PickTarget(scored, lastVisited);
        if (!_runtime.TryWriteCheckpointTarget(new Chapter3CheckpointTarget(code, today)))
        {
            WarnOnce("pick-write", "Checkpoint pick could not be recorded.");
            return new Chapter3QaResult(false, "Checkpoint pick could not be recorded.");
        }
        ClearWarn("pick-snapshot");
        ClearWarn("pick-write");
        return code is null
            ? new Chapter3QaResult(true, $"Checkpoint pick day {today}: no owned property.")
            : new Chapter3QaResult(true, $"Checkpoint target picked: {code} (day {today}).");
    }

    private void Reconcile(double? minutes)
    {
        var tier = _chief.ReadTier();
        if (tier is null) return;
        var isProtected = _chief.IsProtected();
        Chapter3CheckpointActiveSet desired;
        if (!isProtected && (tier == LocalPressureTier.Watched || tier == LocalPressureTier.Critical))
        {
            var target = _runtime.ReadCheckpointTarget();
            if (target.PropertyCode is null)
            {
                desired = new Chapter3CheckpointActiveSet(Array.Empty<string>(), null);
            }
            else if (!TryResolveTargetPosition(target.PropertyCode, out var targetX, out var targetZ))
            {
                return;
            }
            else
            {
                var sites = new List<Chapter3CheckpointNativeSite>();
                if (TryReadNativePositions(out var natives) != Chapter3CheckpointWorldStatus.Ok)
                    WarnOnce("reconcile-natives", "Checkpoint reconcile runs without native positions; no native is asserted.");
                else
                    sites.AddRange(natives
                        .Where(site => site is not null)
                        .Select(site => new Chapter3CheckpointNativeSite(site.Location, site.X, site.Z)));
                desired = Chapter3CheckpointPlanner.ActiveSet(
                    targetX, targetZ, tier.Value, false, Chapter3CheckpointSpots.Baked, sites);
            }
        }
        else
        {
            desired = new Chapter3CheckpointActiveSet(Array.Empty<string>(), null);
        }
        _activeLabels = desired.OcLabels;
        AssertNative(desired.NativeLabel);
        var minute = minutes is { } current ? (double?)Math.Floor(current) : null;
        if (minute is null || _lastStructuralMinute != minute)
        {
            var changed = false;
            foreach (var label in desired.OcLabels)
            {
                if (_managed.ContainsKey(label)) continue;
                if (TrySpawnManaged(label)) _lastStructuralMinute = minute;
                changed = true;
                break;
            }
            if (!changed)
                foreach (var label in _managed.Keys
                    .Where(label => !desired.OcLabels.Contains(label, StringComparer.Ordinal))
                    .OrderBy(label => label, StringComparer.Ordinal)
                    .ToList())
                {
                    if (TryDespawnManaged(label)) _lastStructuralMinute = minute;
                    break;
                }
        }
        foreach (var managed in _managed.Values)
            EnsureOfficers(managed, minutes);
    }

    private void AssertNative(string? label)
    {
        if (label is null)
        {
            _assertedNative = null;
            return;
        }
        if (TrySetNative(label, true) != Chapter3CheckpointWorldStatus.Ok)
            WarnOnce($"native-{label}", $"Checkpoint native assert failed for {label}.");
        else
            ClearWarn($"native-{label}");
        _assertedNative = label;
    }

    private bool TrySpawnManaged(string label)
    {
        var spot = Chapter3CheckpointSpots.Baked.FirstOrDefault(entry => string.Equals(entry.Label, label, StringComparison.Ordinal));
        if (spot is null) return false;
        return TrySpawnInto(_managed, ManagedOwnerPrefix, label, spot);
    }

    private bool TrySpawnInto(Dictionary<string, ManagedCheckpoint> set, string ownerPrefix, string label, Chapter3CheckpointSpot spot)
    {
        if (TrySpawn(label, spot, out var handle) != Chapter3CheckpointWorldStatus.Ok || string.IsNullOrEmpty(handle))
        {
            WarnOnce($"spawn-{label}", $"Checkpoint spawn failed for {label}.");
            return false;
        }
        ClearWarn($"spawn-{label}");
        set[label] = new ManagedCheckpoint
        {
            Label = label,
            WorldHandle = handle,
            Spot = spot,
            OwnerPrefix = ownerPrefix,
            OfficerHandles = new[] { NoHandle, NoHandle },
        };
        return true;
    }

    private bool TryDespawnManaged(string label) => TryRemoveFrom(_managed, label);

    private bool TryRemoveFrom(Dictionary<string, ManagedCheckpoint> set, string label)
    {
        if (!set.TryGetValue(label, out var managed)) return true;
        ReleaseOfficers(managed);
        if (TryDespawn(managed.WorldHandle) != Chapter3CheckpointWorldStatus.Ok)
        {
            WarnOnce($"despawn-{label}", $"Checkpoint despawn failed for {label}.");
            return false;
        }
        ClearWarn($"despawn-{label}");
        set.Remove(label);
        return true;
    }

    private void ReleaseOfficers(ManagedCheckpoint managed)
    {
        for (var i = 0; i < managed.OfficerHandles.Length; i++)
        {
            if (managed.OfficerHandles[i] != NoHandle)
                _roster.Release(managed.OfficerHandles[i]);
            managed.OfficerHandles[i] = NoHandle;
            managed.OfficerBindState[i] = Chapter3BindState.Binding;
            managed.OfficerActiveName[i] = "unknown";
            managed.OfficerDistanceM[i] = null;
            managed.BindStartMinutes[i] = null;
        }
    }

    private void EnsureOfficers(ManagedCheckpoint managed, double? minutes)
    {
        if (managed.StandPoints is null)
        {
            if (TryReadStandPoints(managed.WorldHandle, out var points) != Chapter3CheckpointWorldStatus.Ok || points.Count == 0)
            {
                WarnOnce($"standpoints-{managed.Label}", $"Checkpoint {managed.Label}: stand points unreadable; officers wait.");
                return;
            }
            ClearWarn($"standpoints-{managed.Label}");
            managed.StandPoints = points;
        }
        for (var i = 0; i < OfficersPerCheckpoint; i++)
        {
            var stand = managed.StandPoints[i % managed.StandPoints.Count];
            var standPosition = new OcOfficerPosition(stand.X, stand.Y, stand.Z, managed.Spot.YawDegrees);
            if (managed.OfficerHandles[i] != NoHandle &&
                !_roster.Snapshot().Any(slot => slot.Handle == managed.OfficerHandles[i] &&
                    slot.OwnerKey == $"{managed.OwnerPrefix}:{managed.Label}:{i}"))
            {
                // A pooled clone was released; replace at the existing minute cadence.
                managed.OfficerHandles[i] = NoHandle;
                managed.OfficerBindState[i] = Chapter3BindState.Binding;
                managed.BindStartMinutes[i] = null;
                managed.OfficerActiveName[i] = "unknown";
                managed.OfficerDistanceM[i] = null;
            }
            if (managed.OfficerHandles[i] == NoHandle)
            {
                var status = _roster.TryRequest(
                    $"{managed.OwnerPrefix}:{managed.Label}:{i}",
                    standPosition,
                    out var handle);
                if (status != OcOfficerRequestStatus.Accepted)
                {
                    WarnOnce($"officers-{managed.Label}", $"Checkpoint {managed.Label}: officer request refused ({status}).");
                    return;
                }
                ClearWarn($"officers-{managed.Label}");
                managed.OfficerHandles[i] = handle;
            }
            if (managed.OfficerBindState[i] != Chapter3BindState.Bound)
            {
                if (TryReadSpawnedNative(managed.WorldHandle, out var native) != Chapter3CheckpointWorldStatus.Ok || native is null)
                {
                    WarnOnce($"bind-{managed.Label}", $"Checkpoint {managed.Label}: the spawned checkpoint could not be read for binding.");
                    continue;
                }
                var bind = _roster.TryBindToCheckpoint(managed.OfficerHandles[i], native, standPosition);
                if (bind == OcOfficerBindStatus.Bound)
                {
                    managed.BindStartMinutes[i] ??= minutes;
                    ClearWarn($"bind-{managed.Label}");
                }
                else if (bind != OcOfficerBindStatus.SlotNotActive)
                {
                    WarnOnce($"bind-{managed.Label}", $"Checkpoint {managed.Label}: officer bind failed ({bind}).");
                }
                var behaviourRead = _roster.TryReadBehaviourState(managed.OfficerHandles[i], out var activeName, out var checkpointActive);
                if (behaviourRead)
                    managed.OfficerActiveName[i] = activeName;
                var boundRead = _roster.TryReadBoundState(managed.OfficerHandles[i], native, out var boundState);
                var isOurs = boundRead && boundState.CheckpointKind == OcCheckpointKind.Ours;
                if (_roster.TryReadClonePosition(managed.OfficerHandles[i], out var clonePosition))
                    managed.OfficerDistanceM[i] = boundRead && boundState.StandPoint is { } post
                        ? DistanceM(clonePosition, post)
                        : DistanceM(clonePosition, stand);
                if (boundRead && boundState.CheckpointKind == OcCheckpointKind.Native)
                    WarnOnce($"native-leak-{managed.Label}-{i}", $"Checkpoint {managed.Label} officer {i} sits on a native checkpoint.");
                if (boundRead && boundState.CheckpointEnabled && behaviourRead && !checkpointActive &&
                    !string.IsNullOrWhiteSpace(activeName) &&
                    !activeName.Contains("checkpoint", StringComparison.OrdinalIgnoreCase))
                    WarnOnce($"priority-{managed.Label}-{i}", $"Checkpoint {managed.Label} officer {i}: checkpoint enabled but {activeName} outranks the checkpoint.");
                if (behaviourRead && checkpointActive && isOurs && managed.OfficerDistanceM[i] is { } distance && distance <= BindNearMeters)
                {
                    managed.OfficerBindState[i] = Chapter3BindState.Bound;
                    ClearWarn($"timeout-{managed.Label}-{i}");
                    ClearWarn($"priority-{managed.Label}-{i}");
                    ClearWarn($"native-leak-{managed.Label}-{i}");
                }
                else if (managed.BindStartMinutes[i] is { } started && minutes is { } now && now - started >= BindTimeoutGameMinutes)
                {
                    WarnOnce($"timeout-{managed.Label}-{i}", $"Checkpoint {managed.Label} officer {i}: bind timed out; re-binding.");
                    managed.BindStartMinutes[i] = now;
                }
            }
        }
    }

    private bool TryResolveTargetPosition(string code, out float targetX, out float targetZ)
    {
        targetX = 0f;
        targetZ = 0f;
        if (TryReadOwnedPositions(out var positions) != Chapter3CheckpointWorldStatus.Ok)
        {
            WarnOnce("reconcile-positions", "Checkpoint reconcile skipped: owned property positions could not be read.");
            return false;
        }
        var match = positions.FirstOrDefault(entry => entry is not null && string.Equals(entry.PropertyCode, code, StringComparison.Ordinal));
        if (match is null)
        {
            WarnOnce($"target-missing-{code}", $"Checkpoint reconcile skipped: the target {code} has no world position.");
            return false;
        }
        ClearWarn("reconcile-positions");
        ClearWarn($"target-missing-{code}");
        targetX = match.X;
        targetZ = match.Z;
        return true;
    }

    private void ClearLocal()
    {
        _managed.Clear();
        _tests.Clear();
        _warned.ClearAll();
        _throttle.Clear();
        _activeLabels = Array.Empty<string>();
        _lastStructuralMinute = null;
        _assertedNative = null;
        _needsPostLoadPick = false;
    }

    private static IReadOnlyList<Chapter3CheckpointOfficerEntry> HeldOfficers(ManagedCheckpoint managed)
    {
        var entries = new List<Chapter3CheckpointOfficerEntry>(OfficersPerCheckpoint);
        for (var i = 0; i < OfficersPerCheckpoint; i++)
            if (managed.OfficerHandles[i] != NoHandle)
                entries.Add(new Chapter3CheckpointOfficerEntry(
                    managed.OfficerHandles[i], managed.OfficerBindState[i], managed.OfficerActiveName[i], managed.OfficerDistanceM[i]));
        return entries;
    }

    private static double DistanceM(OcOfficerPosition clone, OcOfficerPosition stand)
    {
        var dx = clone.X - stand.X;
        var dy = clone.Y - stand.Y;
        var dz = clone.Z - stand.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static double DistanceM(OcOfficerPosition clone, Chapter3StandPoint stand)
    {
        var dx = clone.X - stand.X;
        var dy = clone.Y - stand.Y;
        var dz = clone.Z - stand.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private void WarnOnce(string cause, string line)
    {
        _warned.Report(cause, line);
    }

    private void ClearWarn(string cause) => _warned.Clear(cause);

    private bool IsHost()
    {
        try { return _checkpoints.IsHost; }
        catch { return false; }
    }

    private Chapter3CheckpointWorldStatus TryReadLastVisited(out string? propertyCode)
    {
        try { return _checkpoints.TryReadLastVisitedPropertyCode(out propertyCode); }
        catch { propertyCode = null; return Chapter3CheckpointWorldStatus.Failed; }
    }

    private Chapter3CheckpointWorldStatus TryReadOwnedPositions(out IReadOnlyList<Chapter3OwnedPropertyPosition> positions)
    {
        try { return _checkpoints.TryReadOwnedPropertyPositions(out positions); }
        catch { positions = Array.Empty<Chapter3OwnedPropertyPosition>(); return Chapter3CheckpointWorldStatus.Failed; }
    }

    private Chapter3CheckpointWorldStatus TryReadNativePositions(out IReadOnlyList<Chapter3NativeCheckpointPosition> positions)
    {
        try { return _checkpoints.TryReadNativeCheckpointPositions(out positions); }
        catch { positions = Array.Empty<Chapter3NativeCheckpointPosition>(); return Chapter3CheckpointWorldStatus.Failed; }
    }

    private Chapter3CheckpointWorldStatus TrySpawn(string label, Chapter3CheckpointSpot spot, out string? handle)
    {
        try { return _checkpoints.TrySpawnCheckpoint(label, spot.X, spot.Y, spot.Z, spot.YawDegrees, out handle); }
        catch { handle = null; return Chapter3CheckpointWorldStatus.Failed; }
    }

    private Chapter3CheckpointWorldStatus TryDespawn(string handle)
    {
        try { return _checkpoints.TryDespawnCheckpoint(handle); }
        catch { return Chapter3CheckpointWorldStatus.Failed; }
    }

    private Chapter3CheckpointWorldStatus TryReadStandPoints(string handle, out IReadOnlyList<Chapter3StandPoint> points)
    {
        try { return _checkpoints.TryReadStandPoints(handle, out points); }
        catch { points = Array.Empty<Chapter3StandPoint>(); return Chapter3CheckpointWorldStatus.Failed; }
    }

    private Chapter3CheckpointWorldStatus TryReadSpawnedNative(string handle, out object? native)
    {
        try { return _checkpoints.TryReadSpawnedNative(handle, out native); }
        catch { native = null; return Chapter3CheckpointWorldStatus.Failed; }
    }

    private Chapter3CheckpointWorldStatus TrySetNative(string location, bool enabled)
    {
        try { return _checkpoints.TrySetNativeCheckpoint(location, enabled); }
        catch { return Chapter3CheckpointWorldStatus.Failed; }
    }

    private Chapter3CheckpointWorldStatus TryReadCameraPose(out Chapter3CameraPose pose)
    {
        try { return _checkpoints.TryReadPlayerCameraPose(out pose); }
        catch { pose = new Chapter3CameraPose(0f, 0f, 0f, 0f); return Chapter3CheckpointWorldStatus.Failed; }
    }

    private sealed class ManagedCheckpoint
    {
        public string Label { get; set; } = string.Empty;
        public string WorldHandle { get; set; } = string.Empty;
        public Chapter3CheckpointSpot Spot { get; set; } = new Chapter3CheckpointSpot(string.Empty, 0f, 0f, 0f, 0f);
        public string OwnerPrefix { get; set; } = ManagedOwnerPrefix;
        public int[] OfficerHandles { get; set; } = new[] { NoHandle, NoHandle };
        public Chapter3BindState[] OfficerBindState { get; set; } = new Chapter3BindState[OfficersPerCheckpoint];
        public string[] OfficerActiveName { get; set; } = new[] { "unknown", "unknown" };
        public double?[] OfficerDistanceM { get; set; } = new double?[OfficersPerCheckpoint];
        public double?[] BindStartMinutes { get; set; } = new double?[OfficersPerCheckpoint];
        public IReadOnlyList<Chapter3StandPoint>? StandPoints { get; set; }
    }
}
