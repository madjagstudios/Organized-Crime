using Il2CppFishNet;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Police;
using OrganizedCrime.Runtime.Platform;
using S1API.Law;
using S1API.Property;
using UnityEngine;
using ApiCheckpoints = S1API.Law.CheckpointManager;
using ApiLocation = S1API.Law.CheckpointLocation;
using ApiPlayer = S1API.Entities.Player;
using NativeCheckpoints = Il2CppScheduleOne.Law.CheckpointManager;
using NativeLocation = Il2CppScheduleOne.Law.CheckpointManager.ECheckpointLocation;
using Object = UnityEngine.Object;

namespace OrganizedCrime.Runtime;

/// <summary>
/// The production Chapter 3 checkpoint world: native checkpoint and property
/// reads plus OC checkpoint clone spawn and despawn, ported from the OC-106
/// police proof and re-verified against the 0.4.7f9 interop by reflection.
/// Every call returns a status and never throws. f9 facts: the native
/// manager is <c>Il2CppScheduleOne.Law.CheckpointManager</c>, a
/// <c>NetworkSingleton</c> whose instance <c>GetCheckpoint</c> takes the
/// location enum; <c>RoadCheckpoint.Enable</c> takes one
/// <c>NetworkConnection</c> (null enables server-side) with a local
/// <c>ActivationState</c> plus <c>ApplyState</c> fallback; S1API's
/// <c>CheckpointManager.GetCheckpointInfo</c> carries each native
/// checkpoint's world position; owned properties come from S1API's
/// <c>PropertyManager.GetOwnedProperties</c> with the target position read
/// from <c>PropertyWrapper.ExteriorSpawnPosition</c>; and the camera pose
/// reads from the <c>PlayerCamera</c> singleton. Native enables go through
/// S1API's <c>SetCheckpointEnabled</c>, so D2's protection gate applies.
/// </summary>
public sealed class Il2CppChapter3CheckpointWorld : IChapter3CheckpointWorld
{
    private static readonly ApiLocation[] NativeLocations =
    [
        ApiLocation.Western,
        ApiLocation.Docks,
        ApiLocation.NorthResidential,
        ApiLocation.WestResidential,
    ];

    private readonly Action<string> _warn;
    private readonly Action<string> _log;
    private readonly Dictionary<string, SpawnedCheckpoint> _spawned = new(StringComparer.Ordinal);
    private int _nextHandle;

    public Il2CppChapter3CheckpointWorld(Action<string>? warn = null, Action<string>? log = null)
    {
        _warn = warn ?? (_ => { });
        _log = log ?? (_ => { });
    }

    public bool IsHost => FishNetHostGate.IsHostNowDoubleRead();

    public Chapter3CheckpointWorldStatus TryReadOwnedPropertyPositions(out IReadOnlyList<Chapter3OwnedPropertyPosition> positions)
    {
        positions = Array.Empty<Chapter3OwnedPropertyPosition>();
        try
        {
            if (!IsHost)
                return Chapter3CheckpointWorldStatus.NotHost;
            var wrappers = PropertyManager.GetOwnedProperties();
            var found = new List<Chapter3OwnedPropertyPosition>();
            if (wrappers is not null)
                foreach (var wrapper in wrappers)
                {
                    if (wrapper is null || string.IsNullOrWhiteSpace(wrapper.PropertyCode))
                        continue;
                    var point = wrapper.ExteriorSpawnPosition;
                    if (!IsFinite(point))
                        continue;
                    found.Add(new Chapter3OwnedPropertyPosition(wrapper.PropertyCode, point.x, point.y, point.z));
                }
            positions = found;
            return Chapter3CheckpointWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint owned-property read failed ({exception.GetType().Name}).");
            positions = Array.Empty<Chapter3OwnedPropertyPosition>();
            return Chapter3CheckpointWorldStatus.Failed;
        }
    }

    public Chapter3CheckpointWorldStatus TryReadLastVisitedPropertyCode(out string? propertyCode)
    {
        propertyCode = null;
        try
        {
            if (!IsHost)
                return Chapter3CheckpointWorldStatus.NotHost;
            propertyCode = ApiPlayer.Local?.LastVisitedProperty?.PropertyCode;
            return Chapter3CheckpointWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint last-visited read failed ({exception.GetType().Name}).");
            propertyCode = null;
            return Chapter3CheckpointWorldStatus.Failed;
        }
    }

    public Chapter3CheckpointWorldStatus TryReadNativeCheckpointPositions(out IReadOnlyList<Chapter3NativeCheckpointPosition> positions)
    {
        positions = Array.Empty<Chapter3NativeCheckpointPosition>();
        try
        {
            if (!IsHost)
                return Chapter3CheckpointWorldStatus.NotHost;
            var found = new List<Chapter3NativeCheckpointPosition>();
            foreach (var location in NativeLocations)
            {
                CheckpointInfo? info;
                try
                {
                    info = ApiCheckpoints.GetCheckpointInfo(location);
                }
                catch
                {
                    continue;
                }
                if (info is null || !IsFinite(info.Position))
                    continue;
                var point = info.Position;
                found.Add(new Chapter3NativeCheckpointPosition(info.Location.ToString(), point.x, point.y, point.z));
            }
            if (found.Count == 0)
                return Chapter3CheckpointWorldStatus.NotReady;
            positions = found;
            return Chapter3CheckpointWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint native-position read failed ({exception.GetType().Name}).");
            positions = Array.Empty<Chapter3NativeCheckpointPosition>();
            return Chapter3CheckpointWorldStatus.Failed;
        }
    }

    public Chapter3CheckpointWorldStatus TrySpawnCheckpoint(string label, float x, float y, float z, float yawDegrees, out string? handle)
    {
        handle = null;
        GameObject? cloneObject = null;
        try
        {
            if (!IsHost)
                return Chapter3CheckpointWorldStatus.NotHost;
            if (string.IsNullOrWhiteSpace(label))
                return Chapter3CheckpointWorldStatus.Failed;
            if (!TryGetDocksSource(out var source))
                return Chapter3CheckpointWorldStatus.NotReady;
            var copyRoot = FindCopyRoot(source);
            if (copyRoot is null)
            {
                _warn($"OC checkpoint spawn failed for {label}: no bounded copy root holds the Docks checkpoint and its equipment.");
                return Chapter3CheckpointWorldStatus.Failed;
            }
            var spot = new Vector3(x, y, z);
            var groundY = y;
            if (TryGroundHeight(spot, out var hit))
                groundY = hit;
            else
                _warn($"OC checkpoint {label}: no road hit under the spot; keeping height {y}.");

            var staging = new GameObject("OC_CheckpointStaging");
            staging.SetActive(false);
            try
            {
                cloneObject = Object.Instantiate(copyRoot, staging.transform, false);
                cloneObject.SetActive(false);
                cloneObject.transform.SetParent(null, true);
            }
            finally
            {
                Object.Destroy(staging);
            }
            cloneObject.name = $"OC_Checkpoint_{label}_{_nextHandle + 1}";

            var checkpoint = cloneObject.GetComponentInChildren<RoadCheckpoint>(true);
            if (checkpoint is null || checkpoint == null)
            {
                DestroyQuiet(cloneObject);
                cloneObject = null;
                return Chapter3CheckpointWorldStatus.Failed;
            }
            var desiredRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            var rotationChange = desiredRotation * Quaternion.Inverse(checkpoint.transform.rotation);
            cloneObject.transform.rotation = rotationChange * cloneObject.transform.rotation;
            // Review fix (owner run 1: "a bit in the air", root 0.5-0.7 m over the
            // road). Resting the lowest child bound on the road lifted the barrier,
            // because the lowest bound isn't the barrier's footing. Copy the native
            // Docks height instead: the RoadCheckpoint transform sits a measured
            // distance above the road at the Docks, so place it the same distance
            // above the road at the spot. The lowest-child rule is the fallback.
            if (TryNativeRoadOffset(source, copyRoot, out var nativeOffset))
            {
                cloneObject.transform.position += new Vector3(x, groundY + nativeOffset, z) - checkpoint.transform.position;
            }
            else if (!TryLowestChildHeight(cloneObject, out var lowest))
            {
                cloneObject.transform.position += new Vector3(x, groundY, z) - checkpoint.transform.position;
            }
            else
            {
                var rootPosition = cloneObject.transform.position;
                cloneObject.transform.position = new Vector3(rootPosition.x, rootPosition.y + (groundY - lowest), rootPosition.z);
                cloneObject.transform.position += new Vector3(x - checkpoint.transform.position.x, 0f, z - checkpoint.transform.position.z);
            }
            checkpoint.AssignedNPCs = new Il2CppSystem.Collections.Generic.List<NPC>();
            if (!CloneReferencesLocal(cloneObject, checkpoint))
            {
                _warn($"OC checkpoint spawn failed for {label}: essential references point outside the clone.");
                DestroyQuiet(cloneObject);
                cloneObject = null;
                return Chapter3CheckpointWorldStatus.Failed;
            }
            ApplyBarrierCorrection(label, cloneObject, checkpoint, x, z);

            cloneObject.SetActive(true);
            var network = cloneObject.GetComponentInChildren<NetworkObject>(true);
            var spawned = false;
            if (network is not null && network != null)
            {
                try
                {
                    if (network.SceneId != 0)
                        network.SceneId = 0;
                    InstanceFinder.ServerManager.Spawn(network);
                    spawned = network.IsSpawned;
                }
                catch (Exception exception)
                {
                    _warn($"OC checkpoint {label}: FishNet spawn failed ({exception.GetType().Name}); plain instantiate.");
                }
            }
            EnableClone(checkpoint);
            var finalRoot = cloneObject.transform.position.y;
            TryLowestChildHeight(cloneObject, out var finalLowest);
            var receipt = $"OC checkpoint {label} placed: root {finalRoot:F2}, road {finalLowest:F2}, network spawned {spawned}.";
            if (spawned) OrganizedCrimeLog.Diagnostic(receipt, _log);
            else _log(receipt);

            _nextHandle++;
            handle = $"oc-checkpoint-{_nextHandle}";
            _spawned[handle] = new SpawnedCheckpoint(label, cloneObject, checkpoint, network);
            cloneObject = null;
            return Chapter3CheckpointWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint spawn failed for {label} ({exception.GetType().Name}).");
            if (cloneObject is not null)
                DestroyQuiet(cloneObject);
            handle = null;
            return Chapter3CheckpointWorldStatus.Failed;
        }
    }

    public Chapter3CheckpointWorldStatus TryDespawnCheckpoint(string handle)
    {
        try
        {
            if (!IsHost)
                return Chapter3CheckpointWorldStatus.NotHost;
            if (!_spawned.TryGetValue(handle, out var entry))
                return Chapter3CheckpointWorldStatus.InvalidHandle;
            _spawned.Remove(handle);
            try
            {
                if (entry.Checkpoint != null)
                    entry.Checkpoint.Disable();
            }
            catch (Exception exception)
            {
                _warn($"OC checkpoint {entry.Label}: disable threw ({exception.GetType().Name}).");
            }
            var despawned = false;
            try
            {
                if (entry.Network != null && entry.Network.IsSpawned && InstanceFinder.ServerManager != null)
                {
                    InstanceFinder.ServerManager.Despawn(entry.Network, new Il2CppSystem.Nullable<DespawnType>(DespawnType.Destroy));
                    despawned = true;
                }
            }
            catch (Exception exception)
            {
                _warn($"OC checkpoint {entry.Label}: despawn threw ({exception.GetType().Name}).");
            }
            try
            {
                if (entry.Root != null)
                    Object.Destroy(entry.Root);
            }
            catch (Exception exception)
            {
                _warn($"OC checkpoint {entry.Label}: destroy threw ({exception.GetType().Name}).");
            }
            OrganizedCrimeLog.Diagnostic($"OC checkpoint {entry.Label} removed (network despawned {despawned}).", _log);
            return Chapter3CheckpointWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint despawn failed ({exception.GetType().Name}).");
            return Chapter3CheckpointWorldStatus.Failed;
        }
    }

    public Chapter3CheckpointWorldStatus TryReadSpawnedNative(string handle, out object? nativeCheckpoint)
    {
        nativeCheckpoint = null;
        try
        {
            if (!_spawned.TryGetValue(handle, out var entry))
                return Chapter3CheckpointWorldStatus.InvalidHandle;
            if (entry.Checkpoint == null)
            {
                _spawned.Remove(handle);
                return Chapter3CheckpointWorldStatus.InvalidHandle;
            }
            nativeCheckpoint = entry.Checkpoint;
            return Chapter3CheckpointWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint native read failed ({exception.GetType().Name}).");
            nativeCheckpoint = null;
            return Chapter3CheckpointWorldStatus.Failed;
        }
    }

    public Chapter3CheckpointWorldStatus TryReadStandPoints(string handle, out IReadOnlyList<Chapter3StandPoint> points)
    {
        points = Array.Empty<Chapter3StandPoint>();
        try
        {
            if (!IsHost)
                return Chapter3CheckpointWorldStatus.NotHost;
            if (!_spawned.TryGetValue(handle, out var entry))
                return Chapter3CheckpointWorldStatus.InvalidHandle;
            if (entry.Checkpoint == null)
            {
                _spawned.Remove(handle);
                return Chapter3CheckpointWorldStatus.InvalidHandle;
            }
            var stands = entry.Checkpoint.StandPoints;
            if (stands is null || stands.Length == 0)
                return Chapter3CheckpointWorldStatus.Failed;
            var found = new List<Chapter3StandPoint>();
            for (var i = 0; i < stands.Length; i++)
            {
                var stand = stands[i];
                if (stand == null)
                    continue;
                var position = stand.position;
                if (!IsFinite(position))
                    continue;
                if (TryGroundHeight(position, out var groundY))
                    position.y = groundY;
                found.Add(new Chapter3StandPoint(position.x, position.y, position.z));
            }
            if (found.Count == 0)
                return Chapter3CheckpointWorldStatus.Failed;
            points = found;
            return Chapter3CheckpointWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint stand read failed ({exception.GetType().Name}).");
            points = Array.Empty<Chapter3StandPoint>();
            return Chapter3CheckpointWorldStatus.Failed;
        }
    }

    public Chapter3CheckpointWorldStatus TrySetNativeCheckpoint(string location, bool enabled)
    {
        try
        {
            if (!IsHost)
                return Chapter3CheckpointWorldStatus.NotHost;
            if (!TryParseLocation(location, out var parsed))
                return Chapter3CheckpointWorldStatus.InvalidHandle;
            // Review fix: the service re-asserts every game minute, and each native
            // enable pulls officers from the station pool even when the checkpoint is
            // already on (the D2 EnableIfOff lesson). Only write a real change.
            if (ApiCheckpoints.IsCheckpointEnabled(parsed) != enabled)
                ApiCheckpoints.SetCheckpointEnabled(parsed, enabled);
            return ApiCheckpoints.IsCheckpointEnabled(parsed) == enabled
                ? Chapter3CheckpointWorldStatus.Ok
                : Chapter3CheckpointWorldStatus.Failed;
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint native set failed for {location} ({exception.GetType().Name}).");
            return Chapter3CheckpointWorldStatus.Failed;
        }
    }

    public Chapter3CheckpointWorldStatus TryReadPlayerCameraPose(out Chapter3CameraPose pose)
    {
        pose = new Chapter3CameraPose(0f, 0f, 0f, 0f);
        try
        {
            if (!IsHost)
                return Chapter3CheckpointWorldStatus.NotHost;
            if (!PlayerCamera.InstanceExists)
                return Chapter3CheckpointWorldStatus.NotReady;
            var camera = PlayerCamera.Instance;
            if (camera == null || camera.transform == null)
                return Chapter3CheckpointWorldStatus.NotReady;
            var point = camera.transform.position;
            var forward = camera.transform.forward;
            var yaw = (float)(Math.Atan2(forward.x, forward.z) * 180d / Math.PI);
            if (!IsFinite(point) || !float.IsFinite(yaw))
                return Chapter3CheckpointWorldStatus.NotReady;
            pose = new Chapter3CameraPose(point.x, point.y, point.z, yaw);
            return Chapter3CheckpointWorldStatus.Ok;
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint camera read failed ({exception.GetType().Name}).");
            pose = new Chapter3CameraPose(0f, 0f, 0f, 0f);
            return Chapter3CheckpointWorldStatus.Failed;
        }
    }

    private static bool TryParseLocation(string? location, out ApiLocation parsed)
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(location)
            && Enum.TryParse(location, ignoreCase: true, out parsed)
            && Enum.IsDefined(typeof(ApiLocation), parsed);
    }

    private bool TryGetDocksSource(out RoadCheckpoint source)
    {
        source = null!;
        try
        {
            if (!NetworkSingleton<NativeCheckpoints>.InstanceExists)
                return false;
            var manager = NetworkSingleton<NativeCheckpoints>.Instance;
            if (manager == null)
                return false;
            source = manager.GetCheckpoint(NativeLocation.Docks);
            return source != null;
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint Docks lookup failed ({exception.GetType().Name}).");
            return false;
        }
    }

    private GameObject? FindCopyRoot(RoadCheckpoint checkpoint)
    {
        var candidate = checkpoint.transform;
        for (var level = 0; candidate != null && level < 6; level++, candidate = candidate.parent)
        {
            var roads = candidate.GetComponentsInChildren<RoadCheckpoint>(true);
            var children = candidate.GetComponentsInChildren<Transform>(true);
            if (roads.Length != 1 || children.Length > 300)
                continue;
            var parts = CheckpointParts(checkpoint);
            if (parts.All(part => part.Part != null && part.Part.transform.IsChildOf(candidate)))
                return candidate.gameObject;
        }
        return null;
    }

    private static bool CloneReferencesLocal(GameObject root, RoadCheckpoint checkpoint) =>
        CheckpointParts(checkpoint).All(part => part.Part != null && part.Part.transform.IsChildOf(root.transform));

    private static List<(string Name, Component? Part)> CheckpointParts(RoadCheckpoint checkpoint)
    {
        var parts = new List<(string Name, Component? Part)>
        {
            ("container", checkpoint.container?.transform),
            ("stopper1", checkpoint.Stopper1),
            ("stopper2", checkpoint.Stopper2),
            ("search1", checkpoint.SearchArea1),
            ("search2", checkpoint.SearchArea2),
            ("npcDetector1", checkpoint.NPCVehicleDetectionArea1),
            ("npcDetector2", checkpoint.NPCVehicleDetectionArea2),
        };
        if (checkpoint.VehicleObstacle1 != null)
            parts.Add(("vehicleObstacle1", checkpoint.VehicleObstacle1));
        if (checkpoint.VehicleObstacle2 != null)
            parts.Add(("vehicleObstacle2", checkpoint.VehicleObstacle2));
        if (checkpoint.ImmediateVehicleDetector != null)
            parts.Add(("immediateDetector", checkpoint.ImmediateVehicleDetector));
        if (checkpoint.StandPoints != null)
            for (var i = 0; i < checkpoint.StandPoints.Length; i++)
                parts.Add(($"standPoint{i}", checkpoint.StandPoints[i]));
        if (checkpoint.TrafficCones != null)
            for (var i = 0; i < checkpoint.TrafficCones.Length; i++)
                parts.Add(($"cone{i}", checkpoint.TrafficCones[i]));
        return parts;
    }

    private bool TryGroundHeight(Vector3 spot, out float groundY)
    {
        groundY = spot.y;
        try
        {
            Transform? player = null;
            try { player = Il2CppScheduleOne.PlayerScripts.Player.Local?.transform; }
            catch { player = null; }
            var hits = Physics.RaycastAll(new Vector3(spot.x, spot.y + 3f, spot.z), Vector3.down, 30f);
            var bestDistance = float.PositiveInfinity;
            var found = false;
            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.collider.isTrigger)
                    continue;
                if (player != null && hit.collider.transform.IsChildOf(player))
                    continue;
                if (Vector3.Dot(hit.normal, Vector3.up) < 0.5f)
                    continue;
                if (hit.distance >= bestDistance)
                    continue;
                bestDistance = hit.distance;
                groundY = hit.point.y;
                found = true;
            }
            return found;
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint ground raycast failed ({exception.GetType().Name}).");
            groundY = spot.y;
            return false;
        }
    }

    // How far the native Docks RoadCheckpoint transform sits above its own road:
    // the nearest upward-facing, non-trigger hit below it that isn't part of the
    // checkpoint's own hierarchy. Cached once per session.
    //
    // OC-141 owner run 7 (0.4.7f12): the live Docks root measured -1.90 m (f9 runs
    // measured -0.03 m) while the copied geometry was unchanged (lowest child 0.68 m
    // below the root on both builds), so every OC barrier sank under the road. The
    // native checkpoint's live height isn't a stable reference; a reading outside
    // +/-0.5 m falls back to the f9 measurement.
    private const float KnownDocksRoadOffset = -0.03f;
    private const float MaxPlausibleRoadOffset = 0.5f;
    private float? _nativeRoadOffset;

    private bool TryNativeRoadOffset(RoadCheckpoint source, GameObject copyRoot, out float offset)
    {
        offset = 0f;
        if (_nativeRoadOffset is { } cached)
        {
            offset = cached;
            return true;
        }
        try
        {
            var origin = source.transform.position;
            var hits = Physics.RaycastAll(origin + Vector3.up * 3f, Vector3.down, 30f);
            var bestDistance = float.PositiveInfinity;
            var roadY = 0f;
            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.collider.isTrigger)
                    continue;
                if (hit.collider.transform.IsChildOf(copyRoot.transform))
                    continue;
                if (Vector3.Dot(hit.normal, Vector3.up) < 0.5f)
                    continue;
                if (hit.distance >= bestDistance)
                    continue;
                bestDistance = hit.distance;
                roadY = hit.point.y;
            }
            var measured = float.IsPositiveInfinity(bestDistance) ? float.NaN : origin.y - roadY;
            if (!float.IsFinite(measured) || Math.Abs(measured) > MaxPlausibleRoadOffset)
            {
                _warn($"OC checkpoint native Docks road offset {measured:F2} m is implausible; using the known {KnownDocksRoadOffset:F2} m.");
                _nativeRoadOffset = KnownDocksRoadOffset;
                offset = KnownDocksRoadOffset;
                return true;
            }
            _nativeRoadOffset = measured;
            OrganizedCrimeLog.Diagnostic($"OC checkpoint native Docks road offset measured: {measured:F2} m.", _log);
            offset = measured;
            return true;
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint native road offset failed ({exception.GetType().Name}).");
            return false;
        }
    }

    private void ApplyBarrierCorrection(string label, GameObject cloneObject, RoadCheckpoint checkpoint, float spotX, float spotZ)
    {
        try
        {
            if (!TryReadBarrierHalves(cloneObject, checkpoint, out var first, out var second, out var source))
            {
                _warn($"OC checkpoint {label}: barrier halves unreadable; keeping transform alignment.");
                return;
            }
            var rootPosition = cloneObject.transform.position;
            var correction = Model.Chapter3CheckpointPlacement.CorrectBarrierMidpoint(
                spotX, spotZ, rootPosition.x, rootPosition.z,
                first.x, first.z, second.x, second.z);
            cloneObject.transform.position = new Vector3(correction.CorrectedX, rootPosition.y, correction.CorrectedZ);
            OrganizedCrimeLog.Diagnostic($"OC checkpoint {label}: barrier midpoint aligned via {source}; lateral correction {correction.LateralMetres:F2} m.", _log);
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint {label}: barrier correction failed ({exception.GetType().Name}); keeping transform alignment.");
        }
    }

    private static bool TryReadBarrierHalves(GameObject cloneObject, RoadCheckpoint checkpoint, out Vector3 first, out Vector3 second, out string source)
    {
        first = default;
        second = default;
        source = string.Empty;
        try
        {
            var stopper1 = checkpoint.Stopper1;
            var stopper2 = checkpoint.Stopper2;
            if (TryTransformPosition(stopper1?.blocker, cloneObject, out var blocker1) &&
                TryTransformPosition(stopper2?.blocker, cloneObject, out var blocker2))
            {
                first = blocker1;
                second = blocker2;
                source = "stopper blockers";
                return true;
            }
            if (TryTransformPosition(stopper1, cloneObject, out var post1) &&
                TryTransformPosition(stopper2, cloneObject, out var post2))
            {
                first = post1;
                second = post2;
                source = "stopper posts";
                return true;
            }
            var obstacle1 = checkpoint.VehicleObstacle1;
            var obstacle2 = checkpoint.VehicleObstacle2;
            if (TryTransformPosition(obstacle1, cloneObject, out var wedge1) &&
                TryTransformPosition(obstacle2, cloneObject, out var wedge2))
            {
                first = wedge1;
                second = wedge2;
                source = "vehicle obstacles";
                return true;
            }
            if (TryRendererBoundsCentre(cloneObject, out var centre))
            {
                first = centre;
                second = centre;
                source = "renderer bounds";
                return true;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    private static bool TryTransformPosition(Component? part, GameObject cloneObject, out Vector3 position) =>
        TryPlacedPosition(part?.transform, cloneObject, out position);

    private static bool TryTransformPosition(Transform? placed, GameObject cloneObject, out Vector3 position) =>
        TryPlacedPosition(placed, cloneObject, out position);

    private static bool TryPlacedPosition(Transform? placed, GameObject cloneObject, out Vector3 position)
    {
        position = default;
        try
        {
            if (placed is null || placed == null || !IsFinite(placed.position))
                return false;
            if (!placed.IsChildOf(cloneObject.transform))
                return false;
            position = placed.position;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryRendererBoundsCentre(GameObject cloneObject, out Vector3 centre)
    {
        centre = default;
        try
        {
            var renderers = cloneObject.GetComponentsInChildren<Renderer>(true);
            var found = false;
            var bounds = new Bounds();
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !IsFinite(renderer.bounds.center))
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            if (!found)
                return false;
            centre = bounds.center;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLowestChildHeight(GameObject root, out float lowest)
    {
        lowest = float.PositiveInfinity;
        var found = false;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || !IsFinite(renderer.bounds.min))
                continue;
            lowest = Math.Min(lowest, renderer.bounds.min.y);
            found = true;
        }
        var colliders = root.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !IsFinite(collider.bounds.min))
                continue;
            lowest = Math.Min(lowest, collider.bounds.min.y);
            found = true;
        }
        return found;
    }

    private void EnableClone(RoadCheckpoint checkpoint)
    {
        try
        {
            checkpoint.Enable(null!);
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint enable RPC threw ({exception.GetType().Name}).");
        }
        if (checkpoint.ActivationState != RoadCheckpoint.ECheckpointState.Enabled)
        {
            checkpoint.ActivationState = RoadCheckpoint.ECheckpointState.Enabled;
            checkpoint.ApplyState();
        }
    }

    private void DestroyQuiet(GameObject root)
    {
        try
        {
            Object.Destroy(root);
        }
        catch (Exception exception)
        {
            _warn($"OC checkpoint cleanup threw ({exception.GetType().Name}).");
        }
    }

    private static bool IsFinite(Vector3 point) =>
        float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);

    private sealed record SpawnedCheckpoint(string Label, GameObject Root, RoadCheckpoint Checkpoint, NetworkObject? Network);
}
