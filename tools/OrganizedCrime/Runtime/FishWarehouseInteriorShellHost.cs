using OrganizedCrime.Model;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Provides the reused Fish Warehouse exterior with an interior-facing visual shell.
/// The authored mesh remains untouched; clones are render-only and enabled only while
/// the player is inside the building.
/// </summary>
public sealed class FishWarehouseInteriorShellHost
{
    private GameObject? _root;
    private int _rendererCount;
    private int _fallbackMaterialCount;

    public bool IsReady => _root is not null && _rendererCount > 0;

    public bool TryAttach(Transform anchor, Transform propertyRoot, Action<string>? log = null)
    {
        if (IsReady)
            return true;

        if (anchor is null || propertyRoot is null)
            return false;

        try
        {
            Transform sourceRoot = FindSourceRoot(anchor);
            string sourcePath = BuildHierarchyPath(sourceRoot);
            if (!FishWarehouseInteriorShellDefinition.IsFishWarehouseSourcePath(sourcePath))
                throw new InvalidOperationException($"Fish Warehouse source root was outside the expected path: {sourcePath}");

            Renderer[] allRenderers = sourceRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            Renderer[] sourceRenderers = allRenderers
                .Where(renderer => renderer is not null &&
                    renderer.GetComponent<MeshFilter>()?.sharedMesh is not null &&
                    renderer.sharedMaterials.Length > 0 &&
                    renderer.sharedMaterials.Any(material => material is not null) &&
                    FishWarehouseInteriorShellDefinition.IsRendererCloneCandidate(
                        renderer.GetType().FullName ?? renderer.GetType().Name,
                        renderer.GetComponent<MeshFilter>()?.sharedMesh is not null))
                .ToArray();
            if (sourceRenderers.Length == 0)
                throw new InvalidOperationException($"No mesh-bearing Renderer/MeshFilter pairs were found below {sourcePath}");

            _root = new GameObject("OC_FishWarehouse_InteriorShell");
            _root.transform.SetParent(propertyRoot, false);
            _root.transform.localPosition = Vector3.zero;
            _root.transform.localRotation = Quaternion.identity;
            _root.transform.localScale = Vector3.one;
            _root.SetActive(false);

            Dictionary<Transform, Transform> cloneTransforms = new();
            Dictionary<Material, Material> fallbackMaterials = new();
            for (int i = 0; i < sourceRenderers.Length; i++)
                CloneRenderer(sourceRenderers[i], sourceRoot, _root.transform, cloneTransforms, fallbackMaterials);

            if (_rendererCount == 0)
                throw new InvalidOperationException("Fish Warehouse interior shell did not create any renderers");

            log?.Invoke($"Fish Warehouse interior shell prepared from '{sourcePath}' ({_rendererCount} render-only mesh renderer(s), fallback materials={_fallbackMaterialCount}, source hierarchy preserved); it will be enabled only while the player is inside.");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Fish Warehouse interior shell attachment failed safely: {ex.Message}");
            Dispose();
            return false;
        }
    }

    public void SetVisible(bool visible)
    {
        if (_root is not null)
            _root.SetActive(visible);
    }

    public void Dispose()
    {
        if (_root is not null)
        {
            try
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
            catch
            {
                // Runtime Property cleanup remains responsible for the owning root.
            }
        }

        _root = null;
        _rendererCount = 0;
        _fallbackMaterialCount = 0;
    }

    private static Transform FindSourceRoot(Transform anchor)
    {
        Transform? current = anchor;
        while (current is not null)
        {
            if (string.Equals(current.name, "Fish Warehouse", StringComparison.OrdinalIgnoreCase))
                return current;

            current = current.parent;
        }

        return anchor;
    }

    private void CloneRenderer(
        Renderer source,
        Transform sourceRoot,
        Transform shellRoot,
        Dictionary<Transform, Transform> cloneTransforms,
        Dictionary<Material, Material> fallbackMaterials)
    {
        MeshFilter? sourceFilter = source.GetComponent<MeshFilter>();
        if (sourceFilter?.sharedMesh is null)
            return;

        Transform cloneParent = GetOrCreateCloneTransform(source.transform, sourceRoot, shellRoot, cloneTransforms);
        Material[]? materials = CreateDoubleSidedMaterials(source.sharedMaterials, fallbackMaterials);
        if (materials is null)
            return;

        CreateRendererClone(source, cloneParent, sourceFilter.sharedMesh, materials, "");
    }

    private void CreateRendererClone(
        Renderer source,
        Transform cloneParent,
        Mesh mesh,
        Material[] materials,
        string suffix)
    {
        var clone = new GameObject("OC_FishWarehouse_Interior_" + source.gameObject.name + suffix);
        clone.layer = source.gameObject.layer;
        clone.transform.SetParent(cloneParent, worldPositionStays: false);
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;

        MeshFilter filter = clone.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer = clone.AddComponent<MeshRenderer>();

        renderer.sharedMaterials = materials;
        renderer.receiveShadows = source.receiveShadows;
        renderer.shadowCastingMode = source.shadowCastingMode;
        _rendererCount++;
    }

    private static Transform GetOrCreateCloneTransform(
        Transform source,
        Transform sourceRoot,
        Transform shellRoot,
        Dictionary<Transform, Transform> cloneTransforms)
    {
        if (cloneTransforms.TryGetValue(source, out Transform? existing))
            return existing;

        var clone = new GameObject("OC_FishWarehouse_InteriorNode_" + source.name);
        if (source == sourceRoot)
        {
            clone.transform.SetParent(shellRoot, worldPositionStays: true);
            clone.transform.SetPositionAndRotation(source.position, source.rotation);
            clone.transform.localScale = source.lossyScale;
        }
        else
        {
            Transform sourceParent = source.parent is not null && IsDescendantOf(source.parent, sourceRoot)
                ? source.parent
                : sourceRoot;
            Transform cloneParent = GetOrCreateCloneTransform(sourceParent, sourceRoot, shellRoot, cloneTransforms);
            clone.transform.SetParent(cloneParent, worldPositionStays: false);
            clone.transform.localPosition = source.localPosition;
            clone.transform.localRotation = source.localRotation;
            clone.transform.localScale = source.localScale;
        }

        clone.layer = source.gameObject.layer;
        cloneTransforms[source] = clone.transform;
        return clone.transform;
    }

    private static bool IsDescendantOf(Transform candidate, Transform ancestor)
    {
        Transform? current = candidate;
        int guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (current == ancestor)
                return true;

            current = current.parent;
        }

        return false;
    }

    private Material[]? CreateDoubleSidedMaterials(
        Material[] sourceMaterials,
        Dictionary<Material, Material> fallbackMaterials)
    {
        Material? fallback = sourceMaterials.FirstOrDefault(material => material is not null);
        if (!FishWarehouseInteriorShellDefinition.CanPrepareMaterialSlots(
                sourceMaterials.Length,
                sourceMaterials.Count(material => material is not null)) || fallback is null)
            return null;

        var cloned = new Material[sourceMaterials.Length];
        for (int i = 0; i < sourceMaterials.Length; i++)
        {
            Material source = sourceMaterials[i] ?? fallback;
            Material material;
            if (source.HasProperty("_Cull") || source.HasProperty("_CullMode"))
            {
                material = UnityEngine.Object.Instantiate(source);
                PrepareDoubleSidedMaterial(material);
            }
            else
            {
                if (fallbackMaterials.TryGetValue(source, out Material? cached))
                {
                    material = cached;
                }
                else
                {
                    Material? prepared = CreateFallbackMaterial(source);
                    if (prepared is null)
                        return null;

                    material = prepared;
                    fallbackMaterials[source] = material;
                    _fallbackMaterialCount++;
                }
            }

            cloned[i] = material;
        }

        return cloned;
    }

    private static Material? CreateFallbackMaterial(Material source)
    {
        Shader? shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader is null)
            return null;

        var material = new Material(shader)
        {
            name = source.name + "_OC_DoubleSidedFallback"
        };

        try
        {
            // Copy compatible values where the authored shader exposes them.
            // WorldspaceUV_New exposes no public material properties, so the
            // fallback remains safe and deterministic for that source path.
            material.CopyPropertiesFromMaterial(source);
        }
        catch
        {
            // The fallback still has a valid Lit/Standard shader and is prepared
            // below even when IL2CPP rejects a cross-shader property copy.
        }

        PrepareDoubleSidedMaterial(material);
        return material;
    }

    private static void PrepareDoubleSidedMaterial(Material material)
    {
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", 0f);
        if (material.HasProperty("_CullMode"))
            material.SetFloat("_CullMode", 0f);
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 0f);
        if (material.HasProperty("_Mode"))
            material.SetFloat("_Mode", 0f);

        material.renderQueue = 2000;
        material.SetOverrideTag("RenderType", "Opaque");
        material.doubleSidedGI = true;
    }

    private static string BuildHierarchyPath(Transform transform)
    {
        Stack<string> parts = new();
        Transform? current = transform;
        int guard = 0;
        while (current is not null && guard++ < 32)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", parts);
    }
}
