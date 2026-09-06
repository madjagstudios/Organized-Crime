using Il2CppScheduleOne.Property;
using UnityEngine;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class FishWarehouseFrontageClearanceHost
{
    private const string RegionPath = "Map/Hyland Point/Region_Docks";

    private readonly List<ClearedObject> _clearedObjects = new();
    private bool _ready;

    public bool IsReady => _ready;

    public bool TryAttach(Property property, Action<string>? log = null)
    {
        if (_ready)
            return true;
        if (property is null)
            return false;

        var allObjects = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(gameObject => gameObject is not null && gameObject.scene.IsValid())
            .ToArray();
        var containerRoots = allObjects
            .Where(gameObject => gameObject.name.Contains("Container", StringComparison.OrdinalIgnoreCase))
            .Select(gameObject => FindContainerRoot(gameObject.transform))
            .Where(root => root is not null)
            .DistinctBy(root => root!.GetInstanceID())
            .Where(root => GetTransformPath(root!.transform).StartsWith(RegionPath, StringComparison.Ordinal))
            .Select(root => root!)
            .ToArray();

        var candidates = containerRoots
            .Where(root => !GetTransformPath(root.transform).Contains("Fish Warehouse", StringComparison.OrdinalIgnoreCase))
            .Select(root => CreateCandidate(root, property.transform))
            .ToArray();
        var clearable = candidates
            .Where(candidate => FishWarehouseFrontageClearanceDefinition.Evaluate(candidate.Snapshot) == FishWarehouseFrontageClearanceDecision.Clear)
            .ToArray();

        if (clearable.Length == 0)
        {
            log?.Invoke($"Fish Warehouse frontage clearance found no safe static container roots in the dock envelope (containerRoots={containerRoots.Length}, candidates={candidates.Length}); no scene objects were changed.");
            return false;
        }

        try
        {
            foreach (var candidate in clearable)
            {
                _clearedObjects.Add(new ClearedObject(candidate.Root, candidate.Root.activeSelf));
                candidate.Root.SetActive(false);
            }

            _ready = true;
            return true;
        }
        catch (Exception ex)
        {
            _ = Restore(log);
            log?.Invoke($"Fish Warehouse frontage clearance failed safely: {ex.Message}");
            return false;
        }
    }

    public bool Dispose(Action<string>? log = null)
    {
        return Restore(log);
    }

    private bool Restore(Action<string>? log)
    {
        var restored = 0;
        var remaining = new List<ClearedObject>();
        foreach (var cleared in _clearedObjects)
        {
            try
            {
                if (cleared.Root == null)
                {
                    remaining.Add(cleared);
                    log?.Invoke($"Fish Warehouse frontage clearance could not restore destroyed scene object (originally active={cleared.OriginalActiveSelf}).");
                    continue;
                }

                cleared.Root.SetActive(cleared.OriginalActiveSelf);
                restored++;
            }
            catch (Exception ex)
            {
                remaining.Add(cleared);
                log?.Invoke($"Fish Warehouse frontage clearance could not restore scene object: {ex.Message}");
            }
        }

        _clearedObjects.Clear();
        _clearedObjects.AddRange(remaining);

        _ready = false;
        if (restored > 0)
            log?.Invoke($"Fish Warehouse frontage clearance restored {restored} scene object(s).");
        if (remaining.Count > 0)
            log?.Invoke($"Fish Warehouse frontage clearance retained {remaining.Count} scene object(s) for diagnostics after partial restore; cleanup remains partial.");
        return remaining.Count == 0;
    }

    private static Candidate CreateCandidate(GameObject root, Transform propertyRoot)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var colliders = root.GetComponentsInChildren<Collider>(true);
        var worldBounds = GetCombinedBounds(renderers, colliders);
        var localBounds = ToLocalBounds(worldBounds, propertyRoot);
        var componentTypeNames = root.GetComponentsInChildren<Transform>(true)
            .Select(component => (Component)component)
            .Concat(renderers.Select(component => (Component)component))
            .Concat(colliders.Select(component => (Component)component))
            .Select(component => component.GetType().FullName ?? component.GetType().Name)
            .ToArray();
        var hasUnknownBehaviour = root.GetComponentsInChildren<MonoBehaviour>(true).Length > 0;
        var snapshot = new FishWarehouseFrontageCandidate(
            GetTransformPath(root.transform),
            root.activeSelf,
            root.isStatic,
            renderers.Length > 0,
            colliders.Length > 0,
            componentTypeNames,
            new FishWarehouseFrontageBounds(localBounds.min.x, localBounds.max.x, localBounds.min.z, localBounds.max.z),
            HasProtectedAncestor(root.transform),
            hasUnknownBehaviour);
        return new Candidate(root, snapshot);
    }

    private static bool HasProtectedAncestor(Transform candidate)
    {
        for (var current = candidate.parent; current is not null; current = current.parent)
        {
            if (FishWarehouseFrontageClearanceDefinition.IsProtectedPath(GetTransformPath(current)) ||
                current.GetComponents<MonoBehaviour>().Any(component =>
                    FishWarehouseFrontageClearanceDefinition.IsProtectedTypeName(
                        component.GetType().FullName ?? component.GetType().Name)))
            {
                return true;
            }
        }

        return false;
    }

    private static GameObject? FindContainerRoot(Transform start)
    {
        GameObject? result = null;
        for (Transform? current = start; current is not null; current = current.parent)
        {
            if (!current.name.Contains("Container", StringComparison.OrdinalIgnoreCase))
                continue;

            result = current.gameObject;
        }

        return result;
    }

    private static Bounds GetCombinedBounds(IReadOnlyList<Renderer> renderers, IReadOnlyList<Collider> colliders)
    {
        var bounds = new Bounds(Vector3.zero, Vector3.zero);
        var initialized = false;
        foreach (var renderer in renderers)
        {
            if (!initialized)
            {
                bounds = renderer.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        foreach (var collider in colliders)
        {
            if (!initialized)
            {
                bounds = collider.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return bounds;
    }

    private static Bounds ToLocalBounds(Bounds worldBounds, Transform reference)
    {
        var corners = new[]
        {
            new Vector3(worldBounds.min.x, worldBounds.min.y, worldBounds.min.z),
            new Vector3(worldBounds.min.x, worldBounds.min.y, worldBounds.max.z),
            new Vector3(worldBounds.min.x, worldBounds.max.y, worldBounds.min.z),
            new Vector3(worldBounds.min.x, worldBounds.max.y, worldBounds.max.z),
            new Vector3(worldBounds.max.x, worldBounds.min.y, worldBounds.min.z),
            new Vector3(worldBounds.max.x, worldBounds.min.y, worldBounds.max.z),
            new Vector3(worldBounds.max.x, worldBounds.max.y, worldBounds.min.z),
            new Vector3(worldBounds.max.x, worldBounds.max.y, worldBounds.max.z)
        }.Select(reference.InverseTransformPoint).ToArray();
        var local = new Bounds(corners[0], Vector3.zero);
        foreach (var corner in corners.Skip(1))
            local.Encapsulate(corner);
        return local;
    }

    private static string GetTransformPath(Transform transform)
    {
        var names = new Stack<string>();
        for (Transform? current = transform; current is not null; current = current.parent)
            names.Push(current.name);
        return string.Join("/", names);
    }

    private sealed record Candidate(GameObject Root, FishWarehouseFrontageCandidate Snapshot);

    private sealed record ClearedObject(GameObject Root, bool OriginalActiveSelf);

}
