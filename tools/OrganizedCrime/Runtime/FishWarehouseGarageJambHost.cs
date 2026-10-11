using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Platform;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Builds the garage jamb reveals as a separate render-only object under the
/// runtime Property root. OC-4 gotcha: the room mesh doubles as its MeshCollider,
/// so jambs must never be merged into it — this object carries a MeshFilter and
/// MeshRenderer only, and no collider of any kind is ever added.
/// </summary>
public sealed class FishWarehouseGarageJambHost
{
    private const string JambRootName = "OC_FishWarehouse_GarageJambs";
    private const string JambSemanticPrefix = "garage-jamb-";

    private GameObject? _root;
    private Mesh? _mesh;
    private Material? _fallbackMaterial;

    public bool IsActive => UnityLife.IsAlive(_root);

    public bool TryActivate(
        Transform propertyRoot,
        FishWarehouseGarageOpeningProjection? projection,
        Material? roomMaterial,
        Action<string>? log = null)
    {
        if (IsActive)
            return true;

        if (propertyRoot is null || projection is null)
        {
            log?.Invoke("OC-110 garage jambs were not built because the Property root or projection was unavailable.");
            return false;
        }

        try
        {
            IReadOnlyList<FishWarehouseClosurePiece> jambs =
                FishWarehouseInteriorClosureDefinition.PlanGarageJambs(projection);
            if (jambs.Count == 0)
            {
                log?.Invoke("OC-110 garage jambs: count=0.");
                return true;
            }

            Material? material = UnityLife.IsAlive(roomMaterial) ? roomMaterial : CreateFallbackMaterial();
            if (!UnityLife.IsAlive(material))
            {
                log?.Invoke("OC-110 garage jambs failed safely: no usable brick material was available.");
                return false;
            }

            _root = new GameObject(JambRootName);
            _root.transform.SetParent(propertyRoot, worldPositionStays: false);
            _root.transform.localPosition = Vector3.zero;
            _root.transform.localRotation = Quaternion.identity;
            _root.transform.localScale = Vector3.one;

            _mesh = BuildJambMesh(jambs);
            MeshFilter filter = _root.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;
            MeshRenderer renderer = _root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.receiveShadows = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            // Deliberately no collider: jambs are render-only reveals. Never add one here.

            OrganizedCrimeLog.Diagnostic(FormattableString.Invariant(
                $"OC-110 garage jambs: count={jambs.Count} {DescribeJambs(jambs)}."), log);
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"OC-110 garage jambs failed safely: {ex.Message}");
            Dispose();
            return false;
        }
    }

    public void Dispose()
    {
        if (_root is not null && _root != null)
        {
            try
            {
                _root.SetActive(false);
                UnityEngine.Object.DestroyImmediate(_root);
            }
            catch
            {
                // Runtime Property cleanup owns the parent root as a fallback.
            }
        }

        _root = null;

        if (_mesh is not null && _mesh != null)
        {
            try
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
            }
            catch
            {
                // Mesh cleanup is best-effort during game teardown.
            }
        }

        _mesh = null;

        if (UnityLife.IsAlive(_fallbackMaterial))
        {
            try
            {
                UnityEngine.Object.DestroyImmediate(_fallbackMaterial);
            }
            catch
            {
                // Fallback material cleanup is best-effort during game teardown.
            }
        }

        _fallbackMaterial = null;
    }

    private static string DescribeJambs(IReadOnlyList<FishWarehouseClosurePiece> jambs)
    {
        var parts = new List<string>(jambs.Count);
        foreach (FishWarehouseClosurePiece jamb in jambs)
        {
            string edge = jamb.SemanticId.StartsWith(JambSemanticPrefix, StringComparison.Ordinal)
                ? jamb.SemanticId[JambSemanticPrefix.Length..]
                : jamb.SemanticId;
            parts.Add(jamb.Plane == FishWarehouseClosurePlane.WallZ
                ? FormattableString.Invariant(
                    $"[{edge}: z={jamb.PlaneCoordinate:0.00} x={jamb.HorizontalMinimum:0.00}..{jamb.HorizontalMaximum:0.00} y={jamb.VerticalMinimum:0.00}..{jamb.VerticalMaximum:0.00}]")
                : FormattableString.Invariant(
                    $"[{edge}: x={jamb.PlaneCoordinate:0.00} z={jamb.HorizontalMinimum:0.00}..{jamb.HorizontalMaximum:0.00} y={jamb.VerticalMinimum:0.00}..{jamb.VerticalMaximum:0.00}]"));
        }

        return string.Join(" ", parts);
    }

    private Material? CreateFallbackMaterial()
    {
        if (UnityLife.IsAlive(_fallbackMaterial))
            return _fallbackMaterial;

        try
        {
            Shader? shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader is null)
                return null;

            var material = new Material(shader) { name = "OC_FishWarehouse_GarageJambFallback" };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", new Color(0.42f, 0.27f, 0.2f, 1f));
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", new Color(0.42f, 0.27f, 0.2f, 1f));
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);
            material.renderQueue = 2000;
            material.SetOverrideTag("RenderType", "Opaque");
            material.doubleSidedGI = true;
            _fallbackMaterial = material;
            return material;
        }
        catch
        {
            return null;
        }
    }

    private static Mesh BuildJambMesh(IReadOnlyList<FishWarehouseClosurePiece> jambs)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        foreach (FishWarehouseClosurePiece jamb in jambs)
        {
            // WallX: Horizontal = Z at X = plane; WallZ: Horizontal = X at Z = plane. Vertical = Y.
            float p = jamb.PlaneCoordinate;
            bool wallZ = jamb.Plane == FishWarehouseClosurePlane.WallZ;
            Vector3 Point(float horizontal, float vertical) =>
                wallZ ? new Vector3(horizontal, vertical, p) : new Vector3(p, vertical, horizontal);
            Vector3 a = Point(jamb.HorizontalMinimum, jamb.VerticalMinimum);
            Vector3 b = Point(jamb.HorizontalMaximum, jamb.VerticalMinimum);
            Vector3 c = Point(jamb.HorizontalMaximum, jamb.VerticalMaximum);
            Vector3 d = Point(jamb.HorizontalMinimum, jamb.VerticalMaximum);
            AddQuad(vertices, uvs, triangles, a, b, c, d, wallZ);
            if (jamb.DoubleSided)
                AddQuad(vertices, uvs, triangles, d, c, b, a, wallZ);
        }

        var mesh = new Mesh { name = "OC_FishWarehouse_GarageJambsMesh" };
        mesh.vertices = vertices.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddQuad(
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<int> triangles,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        bool wallZ)
    {
        int index = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        // Planar UVs from the in-plane axes; the world-space brick shader ignores
        // them, and the Lit fallback needs them to sample sanely.
        uvs.Add(new Vector2(wallZ ? a.x : a.z, a.y));
        uvs.Add(new Vector2(wallZ ? b.x : b.z, b.y));
        uvs.Add(new Vector2(wallZ ? c.x : c.z, c.y));
        uvs.Add(new Vector2(wallZ ? d.x : d.z, d.y));
        triangles.Add(index);
        triangles.Add(index + 1);
        triangles.Add(index + 2);
        triangles.Add(index);
        triangles.Add(index + 2);
        triangles.Add(index + 3);
    }

}
