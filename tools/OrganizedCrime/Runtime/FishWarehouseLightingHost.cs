using System.Reflection;
using Il2CppScheduleOne.Misc;
using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Platform;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Builds OC's own warehouse light bars under the runtime Property root. The
/// game fixture is never cloned (its batched meshes would keep drawing at the
/// original's position); only its shared body material and lit panel material
/// are reused, and they are never modified.
/// </summary>
public sealed class FishWarehouseLightingHost
{
    private const string LampRootName = "OC_FishWarehouse_Lighting";
    private const string SourceFixturePrefix = "Segmented Light Bar";
    private const string SourceBodyRendererName = "Body";
    private const string SourceLightOnMaterialName = "lightOnMat";
    private const float BarHeight = 0.10f;
    private const float PanelLength = 1.90f;
    private const float PanelWidth = 0.40f;
    private const float PanelThickness = 0.02f;
    private const float CableWidth = 0.04f;
    private const float CableDepth = 0.03f;
    private const float CableOffsetX = 0.78f;
    private const float LightLocalY = -0.07f;
    private const float SpotAngle = 152.15f;
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private GameObject? _root;
    private readonly List<LampInstance> _lamps = new();
    private LampMaterials? _materials;
    private Material? _fallbackBodyMaterial;
    private Material? _fallbackPanelMaterial;

    public bool IsActive => UnityLife.IsAlive(_root);
    public int LampCount => _lamps.Count;

    public IReadOnlyList<FishWarehouseLightingLamp> CurrentLayout =>
        _lamps.Select(lamp => new FishWarehouseLightingLamp(lamp.X, lamp.Z, lamp.HeightY, lamp.Yaw, lamp.Intensity, lamp.Range)).ToList().AsReadOnly();

    public bool TryActivate(Transform propertyRoot, FishWarehouseLightingDefinition layout, Action<string>? log = null)
    {
        if (IsActive)
            return true;

        if (propertyRoot is null || layout is null)
        {
            log?.Invoke("OC-109 warehouse lighting was not built because the Property root or layout was unavailable.");
            return false;
        }

        try
        {
            _root = new GameObject(LampRootName);
            _root.transform.SetParent(propertyRoot, worldPositionStays: false);
            _root.transform.localPosition = Vector3.zero;
            _root.transform.localRotation = Quaternion.identity;
            _root.transform.localScale = Vector3.one;

            _materials = ResolveMaterials();
            var built = 0;
            foreach (FishWarehouseLightingLamp lamp in layout.Lamps)
            {
                try
                {
                    if (TryBuildLamp(lamp, _materials))
                        built++;
                }
                catch
                {
                    // One failed bar never blocks the rest of the layout.
                }
            }

            if (built == 0)
            {
                log?.Invoke("OC-109 warehouse lighting failed safely: no lamp could be built.");
                Dispose();
                return false;
            }

            var receipt = FormattableString.Invariant(
                $"OC-109 warehouse lighting enabled: lamps={built} materials={_materials.Source}{DescribeUrpLimit()}.");
            if (built == layout.Lamps.Count)
                OrganizedCrimeLog.Diagnostic(receipt, log);
            else
                log?.Invoke(receipt);
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"OC-109 warehouse lighting failed safely: {ex.Message}");
            Dispose();
            return false;
        }
    }

    public bool TryAddLamp(FishWarehouseLightingLamp spec, out int index)
    {
        index = -1;
        if (!IsActive || _root is null || _materials is null)
            return false;

        try
        {
            if (!TryBuildLamp(spec, _materials))
                return false;

            index = _lamps.Count - 1;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryRemoveMostRecent(out int removedIndex)
    {
        removedIndex = -1;
        if (!IsActive || _lamps.Count == 0)
            return false;

        try
        {
            removedIndex = _lamps.Count - 1;
            LampInstance lamp = _lamps[removedIndex];
            _lamps.RemoveAt(removedIndex);
            DestroyLamp(lamp);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryAdjustMostRecentHeight(float delta, out float newHeight)
    {
        newHeight = 0f;
        if (!IsActive || _lamps.Count == 0 || !float.IsFinite(delta))
            return false;

        try
        {
            LampInstance lamp = _lamps[^1];
            if (!UnityLife.IsAlive(lamp.Root))
                return false;

            newHeight = Clamp(
                lamp.HeightY + delta,
                FishWarehouseLightingDefinition.MinTuningHeight,
                FishWarehouseLightingDefinition.MaxTuningHeight);
            lamp.HeightY = newHeight;
            lamp.Root!.transform.localPosition = new Vector3(lamp.X, newHeight, lamp.Z);
            LayoutCables(lamp);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryAdjustAllIntensity(float delta, out float newIntensity)
    {
        newIntensity = 0f;
        if (!IsActive || _lamps.Count == 0 || !float.IsFinite(delta))
            return false;

        try
        {
            newIntensity = Clamp(
                _lamps[^1].Intensity + delta,
                FishWarehouseLightingDefinition.MinIntensity,
                FishWarehouseLightingDefinition.MaxIntensity);
            foreach (LampInstance lamp in _lamps)
            {
                lamp.Intensity = newIntensity;
                if (UnityLife.IsAlive(lamp.Light))
                    lamp.Light!.intensity = newIntensity;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryToggleMostRecentYaw(out int newYaw)
    {
        newYaw = 0;
        if (!IsActive || _lamps.Count == 0)
            return false;

        try
        {
            LampInstance lamp = _lamps[^1];
            if (!UnityLife.IsAlive(lamp.Root))
                return false;

            newYaw = lamp.Yaw == 0 ? 90 : 0;
            lamp.Yaw = newYaw;
            lamp.Root!.transform.localRotation = Quaternion.Euler(0f, newYaw, 0f);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (LampInstance lamp in _lamps)
        {
            try
            {
                if (UnityLife.IsAlive(lamp.Light))
                    lamp.Light!.enabled = false;
            }
            catch
            {
                // Lamp shutdown is best-effort; the root destroy below is the fallback.
            }
        }

        _lamps.Clear();
        _materials = null;

        if (_root is not null && _root != null)
        {
            try
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
            catch
            {
                // Runtime Property cleanup owns the parent root as a fallback.
            }
        }

        _root = null;

        DestroyOwnedMaterial(_fallbackBodyMaterial);
        DestroyOwnedMaterial(_fallbackPanelMaterial);
        _fallbackBodyMaterial = null;
        _fallbackPanelMaterial = null;
    }

    private bool TryBuildLamp(FishWarehouseLightingLamp spec, LampMaterials materials)
    {
        if (_root is null || !UnityLife.IsAlive(_root))
            return false;

        var index = _lamps.Count;
        var root = new GameObject(FormattableString.Invariant($"OC_FishWarehouse_Lamp{index}"));
        root.transform.SetParent(_root.transform, worldPositionStays: false);
        root.transform.localPosition = new Vector3(spec.X, spec.HeightY, spec.Z);
        root.transform.localRotation = Quaternion.Euler(0f, spec.Yaw, 0f);
        root.transform.localScale = Vector3.one;

        GameObject body = CreatePart(root.transform, "Body", Vector3.zero, new Vector3(
            FishWarehouseLightingDefinition.BarLength,
            BarHeight,
            FishWarehouseLightingDefinition.BarWidth));
        SetSharedMaterial(body, materials.Body);

        GameObject panel = CreatePart(root.transform, "Panel", new Vector3(0f, -(BarHeight / 2f) - (PanelThickness / 2f), 0f), new Vector3(
            PanelLength,
            PanelThickness,
            PanelWidth));
        SetSharedMaterial(panel, materials.Panel);

        var lamp = new LampInstance(spec.X, spec.Z, spec.HeightY, spec.Yaw, spec.Intensity, spec.Range, root);
        lamp.CableLeft = CreatePart(root.transform, "CableL", Vector3.zero, Vector3.one);
        lamp.CableRight = CreatePart(root.transform, "CableR", Vector3.zero, Vector3.one);
        SetSharedMaterial(lamp.CableLeft, materials.Body);
        SetSharedMaterial(lamp.CableRight, materials.Body);
        LayoutCables(lamp);

        var lightObject = new GameObject("Spot Light");
        lightObject.transform.SetParent(root.transform, worldPositionStays: false);
        lightObject.transform.localPosition = new Vector3(0f, LightLocalY, 0f);
        lightObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        lightObject.transform.localScale = Vector3.one;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Spot;
        light.range = spec.Range;
        light.spotAngle = SpotAngle;
        light.intensity = spec.Intensity;
        light.color = Color.white;
        light.shadows = LightShadows.None;
        light.enabled = true;
        lamp.Light = light;

        _lamps.Add(lamp);
        return true;
    }

    private void DestroyLamp(LampInstance lamp)
    {
        try
        {
            if (UnityLife.IsAlive(lamp.Light))
                lamp.Light!.enabled = false;
        }
        catch
        {
            // Lamp shutdown is best-effort; the object destroy below is the fallback.
        }

        try
        {
            if (UnityLife.IsAlive(lamp.Root))
                UnityEngine.Object.DestroyImmediate(lamp.Root);
        }
        catch
        {
            // The lighting root destroy remains the cleanup fallback.
        }
    }

    private static void LayoutCables(LampInstance lamp)
    {
        float length = FishWarehouseInteriorRoomDefinition.CeilingY - lamp.HeightY;
        if (!float.IsFinite(length) || length <= 0f)
            return;

        LayoutCable(lamp.CableLeft, -CableOffsetX, length);
        LayoutCable(lamp.CableRight, CableOffsetX, length);
    }

    private static void LayoutCable(GameObject? cable, float offsetX, float length)
    {
        if (!UnityLife.IsAlive(cable))
            return;

        cable!.transform.localPosition = new Vector3(offsetX, length / 2f, 0f);
        cable.transform.localScale = new Vector3(CableWidth, length, CableDepth);
    }

    private static GameObject CreatePart(Transform parent, string name, Vector3 localPosition, Vector3 localScale)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, worldPositionStays: false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = Quaternion.identity;
        part.transform.localScale = localScale;

        try
        {
            BoxCollider? collider = part.GetComponent<BoxCollider>();
            if (collider is not null && collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
        }
        catch
        {
            // A leftover primitive collider never blocks the lamp build.
        }

        return part;
    }

    private static void SetSharedMaterial(GameObject part, Material material)
    {
        try
        {
            MeshRenderer? renderer = part.GetComponent<MeshRenderer>();
            if (renderer is not null && renderer != null)
                renderer.sharedMaterial = material;
        }
        catch
        {
            // A missing renderer never blocks the lamp build.
        }
    }

    private LampMaterials ResolveMaterials()
    {
        Material? body = null;
        Material? panel = null;
        try
        {
            ToggleableLight? source = UnityEngine.Object.FindObjectsOfType<ToggleableLight>(includeInactive: true)
                .FirstOrDefault(candidate =>
                    UnityLife.IsAlive(candidate) &&
                    candidate!.gameObject.name.StartsWith(SourceFixturePrefix, StringComparison.Ordinal));
            if (source is not null)
            {
                body = FindSourceBodyMaterial(source);
                panel = GetMember(source, SourceLightOnMaterialName) as Material;
            }
        }
        catch
        {
            body = null;
            panel = null;
        }

        var bodySource = "game";
        if (!UnityLife.IsAlive(body))
        {
            _fallbackBodyMaterial ??= CreateFallbackBodyMaterial();
            body = _fallbackBodyMaterial;
            bodySource = "fallback";
        }

        var panelSource = "game";
        if (!UnityLife.IsAlive(panel))
        {
            _fallbackPanelMaterial ??= CreateFallbackPanelMaterial();
            panel = _fallbackPanelMaterial;
            panelSource = "fallback";
        }

        return new LampMaterials(body!, panel!, FormattableString.Invariant($"body:{bodySource},panel:{panelSource}"));
    }

    private static Material? FindSourceBodyMaterial(ToggleableLight source)
    {
        try
        {
            Renderer? body = source.GetComponentsInChildren<Renderer>(includeInactive: true)
                .FirstOrDefault(renderer =>
                    UnityLife.IsAlive(renderer) &&
                    string.Equals(renderer!.transform.name, SourceBodyRendererName, StringComparison.Ordinal));
            return body?.sharedMaterials.FirstOrDefault(material => UnityLife.IsAlive(material));
        }
        catch
        {
            return null;
        }
    }

    private static Material CreateFallbackBodyMaterial()
    {
        Shader? shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader is null)
            throw new InvalidOperationException("No usable Lit or Standard shader was found for the lamp fallback body.");

        var material = new Material(shader) { name = "OC_FishWarehouse_LampBodyFallback" };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", new Color(0.3f, 0.3f, 0.3f, 1f));
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", new Color(0.3f, 0.3f, 0.3f, 1f));
        return material;
    }

    private static Material CreateFallbackPanelMaterial()
    {
        Shader? shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader is null)
            throw new InvalidOperationException("No usable Lit or Standard shader was found for the lamp fallback panel.");

        var material = new Material(shader) { name = "OC_FishWarehouse_LampPanelFallback" };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Color.white);
        if (material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", Color.white);
            try
            {
                material.EnableKeyword("_EMISSION");
            }
            catch
            {
                // The white base color still reads as a lit panel without emission.
            }
        }

        return material;
    }

    private static string DescribeUrpLimit() =>
        TryGetMaxAdditionalLights(out int maxLights)
            ? FormattableString.Invariant($" maxAdditionalLights={maxLights}")
            : string.Empty;

    private static bool TryGetMaxAdditionalLights(out int maxLights)
    {
        maxLights = 0;
        try
        {
            var pipeline = UnityEngine.Rendering.GraphicsSettings.renderPipelineAsset;
            if (pipeline is null || pipeline == null)
                return false;

            object? value = pipeline.GetType().GetProperty("maxAdditionalLightsCount", MemberFlags)?.GetValue(pipeline) ??
                pipeline.GetType().GetField("maxAdditionalLightsCount", MemberFlags)?.GetValue(pipeline);
            if (value is int limit)
            {
                maxLights = limit;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static object? GetMember(object target, string name)
    {
        try
        {
            var type = target.GetType();
            return type.GetProperty(name, MemberFlags)?.GetValue(target) ??
                type.GetField(name, MemberFlags)?.GetValue(target);
        }
        catch
        {
            return null;
        }
    }

    private static void DestroyOwnedMaterial(Material? material)
    {
        if (!UnityLife.IsAlive(material))
            return;

        try
        {
            UnityEngine.Object.DestroyImmediate(material);
        }
        catch
        {
            // Fallback material cleanup is best-effort during game teardown.
        }
    }

    private static float Clamp(float value, float minimum, float maximum) =>
        MathF.Min(MathF.Max(value, minimum), maximum);

    private sealed record LampMaterials(Material Body, Material Panel, string Source);

    private sealed class LampInstance
    {
        public LampInstance(float x, float z, float heightY, int yaw, float intensity, float range, GameObject root)
        {
            X = x;
            Z = z;
            HeightY = heightY;
            Yaw = yaw;
            Intensity = intensity;
            Range = range;
            Root = root;
        }

        public float X { get; }
        public float Z { get; }
        public float HeightY { get; set; }
        public int Yaw { get; set; }
        public float Intensity { get; set; }
        public float Range { get; }
        public GameObject? Root { get; }
        public Light? Light { get; set; }
        public GameObject? CableLeft { get; set; }
        public GameObject? CableRight { get; set; }
    }
}
