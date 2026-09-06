using OrganizedCrime.Model;
using Il2CppScheduleOne;
using UnityEngine;

namespace OrganizedCrime.Runtime;

public sealed class SyndicateHqInteriorRuntime : ISyndicateHqInterior, IDisposable
{
    private readonly Action<string> _log;
    private readonly ISyndicateHqStorageRuntime? _storage;
    private GameObject? _root;
    private readonly Dictionary<string, Transform> _markers = new(StringComparer.Ordinal);
    private readonly Dictionary<SyndicateHqMaterialRole, Material> _materials = new();
    private bool _destroyPending;
    private bool _disposed;

    public SyndicateHqInteriorRuntime(
        Action<string>? log = null,
        ISyndicateHqStorageRuntime? storage = null)
    {
        _log = log ?? (_ => { });
        _storage = storage;
    }

    public bool IsCreated => !_destroyPending && !IsUnityNull(_root) && ValidateOwnedRuntime(out _);
    public bool IsDestroyPending => _destroyPending;

    /// <summary>
    /// The timing seam the interior create and destroy receipts report through. Assigned by the
    /// mod shell after construction; the default disabled seam changes nothing.
    /// </summary>
    public OrganizedCrimeTimingReceipts Timing { get; set; } = OrganizedCrimeTimingReceipts.Disabled;

    public bool TryEnsure(SyndicateHqVector3 exteriorAnchor) =>
        Timing.Measure("hq-interior-create", () => TryEnsureCore(exteriorAnchor));

    private bool TryEnsureCore(SyndicateHqVector3 exteriorAnchor)
    {
        if (_disposed) { _log("Syndicate HQ interior creation was rejected after disposal."); return false; }
        if (_destroyPending)
        {
            if (!IsUnityNull(_root)) { _log("Syndicate HQ interior creation is waiting for deferred root destruction."); return false; }
            _root = null;
            _destroyPending = false;
        }
        if (!IsUnityNull(_root)) return ValidateOwnedRuntime(out _);
        if (!SyndicateHqInteriorDefinition.IsValid(out var definitionReason)) { _log($"Syndicate HQ interior definition was invalid: {definitionReason}"); return false; }
        var stage = "scene snapshot";
        try
        {
            // OC-63: one full scene enumeration serves the foreign-root check, the material palette,
            // and the native prop sources. Each of those three used to run its own full scene
            // enumeration, and the palette ran one per distinct material role, so a single interior
            // rebuild walked every loaded GameObject six times and built a hierarchy path string
            // for every object on five of those walks. The
            // snapshot is taken before the owned root exists, which is exactly what all three uses
            // want: the foreign-root check must not see the root it is about to create, and neither
            // the nightclub shell nor any catalog prop path can name an object this method builds.
            var sceneObjects = SnapshotSceneObjects();
            var foreignRoots = sceneObjects
                .Where(gameObject => string.Equals(gameObject.name, SyndicateHqContract.RootName, StringComparison.Ordinal))
                .ToArray();
            if (foreignRoots.Length != 0) { _log($"Syndicate HQ interior creation found {foreignRoots.Length} foreign or stale root(s)."); return false; }
            stage = "root creation";
            _root = new GameObject(SyndicateHqContract.RootName) { hideFlags = HideFlags.DontSave };
            var pocketRoot = SyndicateHqInteriorDefinition.GetPocketRoot(exteriorAnchor);
            _root.transform.position = ToUnity(pocketRoot);
            stage = "material palette";
            CreateMaterialPalette(sceneObjects);
            foreach (var primitive in SyndicateHqInteriorDefinition.Primitives)
            {
                stage = $"primitive '{primitive.Name}'";
                BuildPrimitive(primitive);
            }
            var nativeSources = ResolveNativePropSources(sceneObjects);
            foreach (var nativeProp in SyndicateHqInteriorDefinition.NativeProps)
            {
                stage = $"native prop '{nativeProp.Name}'";
                if (nativeSources.TryGetValue(nativeProp.Name, out var source))
                    BuildNativeProp(nativeProp, source);
            }
            foreach (var marker in SyndicateHqInteriorDefinition.Markers)
            {
                stage = $"marker '{marker.Name}'";
                var gameObject = new GameObject(marker.Name);
                gameObject.transform.SetParent(_root.transform, false);
                gameObject.transform.localPosition = new Vector3(marker.Position.X, marker.Position.Y, marker.Position.Z);
                _markers.Add(marker.Name, gameObject.transform);
            }
            foreach (var lightDefinition in SyndicateHqInteriorDefinition.Lights)
            {
                stage = $"light '{lightDefinition.Name}'";
                BuildLight(lightDefinition);
            }
            stage = "physics synchronization";
            Physics.SyncTransforms();
            stage = "owned-runtime validation";
            if (ValidateOwnedRuntime(out var validationReason))
            {
                var storageResult = _storage?.PlaceAtPocket(pocketRoot);
#if DEBUG
                if (storageResult is not null)
                    _log($"Syndicate HQ storage receipt: pocket placement result={storageResult.Status}; ready={storageResult.IsReady}; reason={storageResult.Reason}");
#endif
                if (storageResult is not null && !storageResult.IsReady)
                    _log($"Syndicate HQ native storage remained inert: {storageResult.Status} — {storageResult.Reason}");
                return true;
            }
            _log($"Syndicate HQ interior construction failed owned-runtime validation: {validationReason}");
            Destroy();
            return false;
        }
        catch (Exception ex)
        {
            _log($"Syndicate HQ interior construction failed at {stage}: {ex.GetType().Name}: {ex.Message}");
            Destroy();
            return false;
        }
    }

    public bool TryGetMarker(string name, out SyndicateHqVector3 position)
    {
        if (_markers.TryGetValue(name, out var marker) && !IsUnityNull(marker))
        {
            position = new(marker.position.x, marker.position.y, marker.position.z);
            return true;
        }
        position = default;
        return false;
    }

    public void Destroy() => Timing.Measure("hq-interior-destroy", DestroyCore);

    private void DestroyCore()
    {
        _storage?.DisableInteractions();
        _markers.Clear();
        if (!IsUnityNull(_root) && !_destroyPending)
        {
            UnityEngine.Object.Destroy(_root);
            _destroyPending = true;
        }
        if (IsUnityNull(_root))
        {
            _root = null;
            _destroyPending = false;
        }
        foreach (var material in _materials.Values)
            if (!IsUnityNull(material)) UnityEngine.Object.Destroy(material);
        _materials.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Destroy();
    }

    private void BuildPrimitive(SyndicateHqPrimitiveDefinition definition)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = definition.Name;
        cube.transform.SetParent(_root!.transform, false);
        cube.transform.localPosition = ToUnity(definition.Position);
        cube.transform.localScale = ToUnity(definition.Scale);

        var renderer = cube.GetComponent<MeshRenderer>();
        if (!IsUnityNull(renderer))
        {
            renderer!.sharedMaterial = _materials[definition.MaterialRole];
            renderer.enabled = definition.IsVisible;
            renderer.receiveShadows = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        var collider = cube.GetComponent<Collider>();
        if (!definition.HasCollider && !IsUnityNull(collider))
        {
            collider!.enabled = false;
            UnityEngine.Object.Destroy(collider);
        }
    }

    private void BuildLight(SyndicateHqLightDefinition definition)
    {
        var lightObject = new GameObject(definition.Name);
        lightObject.transform.SetParent(_root!.transform, false);
        lightObject.transform.localPosition = ToUnity(definition.Position);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(definition.Color.R, definition.Color.G, definition.Color.B, 1f);
        light.intensity = definition.Intensity;
        light.range = definition.Range;
        light.shadows = LightShadows.None;
    }

    /// <summary>
    /// The one full scene enumeration an interior rebuild takes, materialized so every consumer
    /// filters the same array instead of walking every loaded object again.
    /// </summary>
    private static GameObject[] SnapshotSceneObjects() =>
        Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(gameObject => !IsUnityNull(gameObject) && gameObject.scene.IsValid())
            .ToArray();

    /// <summary>
    /// The leaf name of a hierarchy path: an object whose full path equals the catalog path must
    /// carry this name, so comparing names first is an exact prefilter that skips the per object
    /// path construction for everything that cannot match.
    /// </summary>
    private static string LeafNameOf(string hierarchyPath)
    {
        var separator = hierarchyPath.LastIndexOf('/');
        return separator < 0 ? hierarchyPath : hierarchyPath[(separator + 1)..];
    }

    private void CreateMaterialPalette(GameObject[] sceneObjects)
    {
        var shellRoot = ResolveNightclubShellRoot(sceneObjects);
        foreach (var role in SyndicateHqInteriorDefinition.Primitives.Select(item => item.MaterialRole).Distinct())
            _materials.Add(role, CreateMaterial(role, shellRoot));
    }

    private Dictionary<string, GameObject> ResolveNativePropSources(GameObject[] sceneObjects)
    {
        var catalogPaths = SyndicateHqInteriorDefinition.NativeProps
            .Select(item => item.SourceHierarchyPath)
            .ToHashSet(StringComparer.Ordinal);
        var catalogLeafNames = catalogPaths.Select(LeafNameOf).ToHashSet(StringComparer.Ordinal);
        var byPath = sceneObjects
            .Where(gameObject => catalogLeafNames.Contains(gameObject.name))
            .Select(gameObject => new { GameObject = gameObject, Path = BuildHierarchyPath(gameObject.transform) })
            .Where(candidate => catalogPaths.Contains(candidate.Path))
            .GroupBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(candidate => candidate.GameObject).ToArray(), StringComparer.Ordinal);
        var resolved = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        foreach (var definition in SyndicateHqInteriorDefinition.NativeProps)
        {
            if (!byPath.TryGetValue(definition.SourceHierarchyPath, out var matches) || matches.Length != 1)
            {
                if (definition.IsRequired)
                    throw new InvalidOperationException($"Native HQ prop '{definition.Name}' resolved {matches?.Length ?? 0} source objects at '{definition.SourceHierarchyPath}'.");
                _log($"Optional native HQ prop '{definition.Name}' was unavailable and was omitted.");
                continue;
            }
            resolved.Add(definition.Name, matches[0]);
        }
        return resolved;
    }

    private void BuildNativeProp(SyndicateHqNativePropDefinition definition, GameObject source)
    {
        var container = new GameObject(definition.Name) { hideFlags = HideFlags.DontSave };
        container.transform.SetParent(_root!.transform, false);
        container.transform.localPosition = ToUnity(definition.Position);
        container.transform.localRotation = Quaternion.Euler(
            definition.PitchDegrees,
            definition.YawDegrees,
            definition.RollDegrees);
        container.transform.localScale = Vector3.one * definition.UniformScale;
        CloneVisualTree(source.transform, container.transform, true, definition.KeepColliders, definition.IncludeInactiveChildren);

        var renderers = container.GetComponentsInChildren<Renderer>(includeInactive: true)
            .Where(renderer => !IsUnityNull(renderer))
            .ToArray();
        if (renderers.Length == 0)
            throw new InvalidOperationException($"Native HQ prop '{definition.Name}' had no visual renderers.");
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        var target = _root.transform.TransformPoint(ToUnity(definition.Position));
        var offset = SyndicateHqNativePropPlacement.AlignmentOffset(
            new(target.x, target.y, target.z),
            new(
                new(bounds.center.x, bounds.center.y, bounds.center.z),
                new(bounds.min.x, bounds.min.y, bounds.min.z)),
            definition.Anchor);
        container.transform.position += ToUnity(offset);
    }

    private static void CloneVisualTree(
        Transform source,
        Transform parent,
        bool isRoot,
        bool keepColliders,
        bool includeInactiveChildren)
    {
        var clone = new GameObject(isRoot ? "Visual" : source.name) { hideFlags = HideFlags.DontSave };
        clone.transform.SetParent(parent, false);
        clone.transform.localPosition = isRoot ? Vector3.zero : source.localPosition;
        clone.transform.localRotation = isRoot ? Quaternion.identity : source.localRotation;
        clone.transform.localScale = source.localScale;

        var sourceFilter = source.GetComponent<MeshFilter>();
        if (!IsUnityNull(sourceFilter) && !IsUnityNull(sourceFilter!.sharedMesh))
            clone.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
        var sourceRenderer = source.GetComponent<MeshRenderer>();
        if (!IsUnityNull(sourceRenderer))
        {
            var renderer = clone.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer!.sharedMaterials;
            renderer.enabled = sourceRenderer.enabled;
            renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            renderer.receiveShadows = sourceRenderer.receiveShadows;
        }
        if (keepColliders)
            CopyVisualColliders(source, clone);

        for (var index = 0; index < source.childCount; index++)
        {
            var child = source.GetChild(index);
            if (!IsUnityNull(child) && (includeInactiveChildren || child!.gameObject.activeSelf))
                CloneVisualTree(child!, clone.transform, false, keepColliders, includeInactiveChildren);
        }
    }

    private static void CopyVisualColliders(Transform source, GameObject destination)
    {
        foreach (var box in source.GetComponents<BoxCollider>().Where(collider => !IsUnityNull(collider) && collider.enabled))
        {
            var clone = destination.AddComponent<BoxCollider>();
            clone.center = box.center;
            clone.size = box.size;
            clone.isTrigger = false;
        }
        foreach (var sphere in source.GetComponents<SphereCollider>().Where(collider => !IsUnityNull(collider) && collider.enabled))
        {
            var clone = destination.AddComponent<SphereCollider>();
            clone.center = sphere.center;
            clone.radius = sphere.radius;
            clone.isTrigger = false;
        }
        foreach (var capsule in source.GetComponents<CapsuleCollider>().Where(collider => !IsUnityNull(collider) && collider.enabled))
        {
            var clone = destination.AddComponent<CapsuleCollider>();
            clone.center = capsule.center;
            clone.radius = capsule.radius;
            clone.height = capsule.height;
            clone.direction = capsule.direction;
            clone.isTrigger = false;
        }
        foreach (var mesh in source.GetComponents<MeshCollider>().Where(collider => !IsUnityNull(collider) && collider.enabled && !IsUnityNull(collider.sharedMesh)))
        {
            var clone = destination.AddComponent<MeshCollider>();
            clone.sharedMesh = mesh.sharedMesh;
            clone.convex = mesh.convex;
            clone.isTrigger = false;
        }
    }

    private static Material CreateMaterial(SyndicateHqMaterialRole role, GameObject? shellRoot)
    {
        Material? source = FindNativeMaterial(role, shellRoot);
        Shader? shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (source is null && shader is null)
            throw new InvalidOperationException("No usable native or fallback shader was available for the HQ interior.");

        var material = source is not null ? UnityEngine.Object.Instantiate(source) : new Material(shader!);
        material.name = "OC_SyndicateHQ_" + role;
        material.hideFlags = HideFlags.DontSave;
        var color = ColorFor(role);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        if (material.HasProperty("_CullMode")) material.SetFloat("_CullMode", 0f);
        if (role == SyndicateHqMaterialRole.Accent && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.5f);
        }
        material.renderQueue = 2000;
        material.SetOverrideTag("RenderType", "Opaque");
        material.doubleSidedGI = true;
        return material;
    }

    /// <summary>
    /// The nightclub shell root every material role sources from, resolved once per rebuild off the
    /// shared scene snapshot. Resolving it per role is what made the palette the most expensive
    /// stage of an interior rebuild: it walked every loaded object and built a hierarchy path for
    /// each one, once for every distinct role in the primitive catalog.
    /// </summary>
    private static GameObject? ResolveNightclubShellRoot(GameObject[] sceneObjects)
    {
        try
        {
            var shellLeafName = LeafNameOf(SyndicateHqContract.NightclubShellPath);
            var roots = sceneObjects
                .Where(gameObject => string.Equals(gameObject.name, shellLeafName, StringComparison.Ordinal) &&
                    string.Equals(BuildHierarchyPath(gameObject.transform), SyndicateHqContract.NightclubShellPath, StringComparison.Ordinal))
                .ToArray();
            return roots.Length == 1 ? roots[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static Material? FindNativeMaterial(SyndicateHqMaterialRole role, GameObject? shellRoot)
    {
        if (role == SyndicateHqMaterialRole.Accent) return null;
        string[] keywords = role switch
        {
            SyndicateHqMaterialRole.Floor => new[] { "floor", "concrete", "tile" },
            SyndicateHqMaterialRole.Brick => new[] { "brick", "masonry" },
            SyndicateHqMaterialRole.Ceiling => new[] { "ceiling", "plaster", "concrete" },
            SyndicateHqMaterialRole.Wood => new[] { "wood", "timber" },
            SyndicateHqMaterialRole.Metal => new[] { "metal", "steel" },
            _ => Array.Empty<string>()
        };
        try
        {
            if (IsUnityNull(shellRoot)) return null;
            return shellRoot!.GetComponentsInChildren<Renderer>(includeInactive: true)
                .Where(renderer => !IsUnityNull(renderer))
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => !IsUnityNull(material) && keywords.Any(keyword => material.name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(material => material.name, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static Color ColorFor(SyndicateHqMaterialRole role) => role switch
    {
        SyndicateHqMaterialRole.Floor => new Color(0.10f, 0.11f, 0.12f, 1f),
        SyndicateHqMaterialRole.Brick => new Color(0.31f, 0.16f, 0.10f, 1f),
        SyndicateHqMaterialRole.Ceiling => new Color(0.075f, 0.08f, 0.09f, 1f),
        SyndicateHqMaterialRole.Wood => new Color(0.22f, 0.095f, 0.045f, 1f),
        SyndicateHqMaterialRole.Metal => new Color(0.18f, 0.20f, 0.22f, 1f),
        _ => new Color(0.86f, 0.36f, 0.07f, 1f)
    };

    private bool ValidateOwnedRuntime(out string reason)
    {
        if (IsUnityNull(_root)) { reason = "the owned root was unavailable"; return false; }
        if (_destroyPending) { reason = "the owned root was pending destruction"; return false; }
        if (!string.Equals(_root!.name, SyndicateHqContract.RootName, StringComparison.Ordinal)) { reason = $"the owned root name was '{_root.name}'"; return false; }
        var sameNameRoots = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(gameObject => !IsUnityNull(gameObject) && gameObject.scene.IsValid() && string.Equals(gameObject.name, SyndicateHqContract.RootName, StringComparison.Ordinal))
            .ToArray();
        if (!SyndicateHqInteriorOwnership.IsRetainedNativeRoot(
                _root.GetInstanceID(),
                sameNameRoots.Select(gameObject => gameObject.GetInstanceID()).ToArray(),
                out reason)) return false;
        foreach (var marker in SyndicateHqInteriorDefinition.Markers)
            if (IsUnityNull(_root.transform.Find(marker.Name))) { reason = $"marker '{marker.Name}' was unavailable"; return false; }
        foreach (var geometryName in SyndicateHqInteriorDefinition.RequiredColliderNames)
        {
            var geometry = _root.transform.Find(geometryName);
            if (IsUnityNull(geometry)) { reason = $"required geometry '{geometryName}' was unavailable"; return false; }
            if (IsUnityNull(geometry!.GetComponent<Collider>())) { reason = $"required collider '{geometryName}' was unavailable"; return false; }
        }
        foreach (var nativeProp in SyndicateHqInteriorDefinition.NativeProps.Where(item => item.IsRequired))
        {
            var prop = _root.transform.Find(nativeProp.Name);
            if (IsUnityNull(prop)) { reason = $"required native prop '{nativeProp.Name}' was unavailable"; return false; }
            if (prop!.GetComponentsInChildren<Renderer>(includeInactive: true).All(IsUnityNull)) { reason = $"required native prop '{nativeProp.Name}' had no renderer"; return false; }
            if (nativeProp.KeepColliders && prop.GetComponentsInChildren<Collider>(includeInactive: true).All(IsUnityNull)) { reason = $"required native prop '{nativeProp.Name}' had no collider"; return false; }
        }
        foreach (var lightDefinition in SyndicateHqInteriorDefinition.Lights)
        {
            var light = _root.transform.Find(lightDefinition.Name);
            if (IsUnityNull(light)) { reason = $"light object '{lightDefinition.Name}' was unavailable"; return false; }
            if (IsUnityNull(light!.GetComponent<Light>())) { reason = $"light component '{lightDefinition.Name}' was unavailable"; return false; }
        }
        reason = "the exact owned root, markers, collision shell, and lights were present";
        return true;
    }

    private static string BuildHierarchyPath(Transform transform)
    {
        var parts = new Stack<string>();
        Transform? current = transform;
        var guard = 0;
        while (!IsUnityNull(current) && guard++ < 64)
        {
            parts.Push(current!.name);
            current = current.parent;
        }
        return string.Join("/", parts);
    }

    private static Vector3 ToUnity(SyndicateHqVector3 value) => new(value.X, value.Y, value.Z);
    private static bool IsUnityNull(object? value) => value is null || value == null;
    private static bool IsUnityNull(UnityEngine.Object? value) => value == null;
}
