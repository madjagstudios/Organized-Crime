using OrganizedCrime.Model;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Owns a deliberately smaller, render-and-collision interior room. The room
/// is authored from inward-facing planes so it does not depend on the exterior
/// building's incomplete interior faces or culling behavior.
/// </summary>
public sealed class FishWarehouseInteriorRoomHost
{
    private GameObject? _root;
    private Mesh? _mesh;
    private IReadOnlyList<FishWarehouseWallOpening> _openings = Array.Empty<FishWarehouseWallOpening>();

    public bool IsReady => IsAlive(_root) && IsAlive(_mesh);
    public GameObject? Root => IsReady ? _root : null;
    public IReadOnlyList<FishWarehouseWallOpening> Openings => _openings;

    public bool TryPrepare(
        Transform anchor,
        Transform propertyRoot,
        FishWarehouseGarageOpeningProjection garageOpening,
        Action<string>? log = null)
    {
        if (IsReady)
            return true;

        if (anchor is null || propertyRoot is null)
            return false;

        try
        {
            if (!FishWarehouseTransformSignatureDefinition.IsCoincident(
                    ToTransformSignature(anchor),
                    ToTransformSignature(propertyRoot)))
            {
                throw new InvalidOperationException("The anchor/property-root transform signatures were not coincident.");
            }

            Transform sourceRoot = FindSourceRoot(anchor);
            Transform? door = FindEntranceDoor(sourceRoot);
            if (door is null)
                throw new InvalidOperationException("The native Industrial Metal Door (Static) transform was not found.");

            Vector3 localDoor = anchor.InverseTransformPoint(door.position);
            if (!FishWarehouseInteriorRoomDefinition.TryPlanRoomWalls(
                    localDoor.x,
                    localDoor.z,
                    garageOpening.Opening,
                    out IReadOnlyList<FishWarehouseWallOpening>? openings,
                    out IReadOnlyList<FishWarehouseWallPlan>? wallPlans) ||
                openings is null ||
                wallPlans is null)
            {
                throw new InvalidOperationException("The personnel and garage openings could not produce valid non-overlapping room-wall plans.");
            }

            Material material = FindBrickMaterial(sourceRoot) ?? CreateFallbackMaterial();
            _root = new GameObject("OC_FishWarehouse_InteriorRoom");
            _root.transform.SetParent(propertyRoot, worldPositionStays: false);
            _root.transform.localPosition = Vector3.zero;
            _root.transform.localRotation = Quaternion.identity;
            _root.transform.localScale = Vector3.one;
            _root.SetActive(false);

            IReadOnlyList<FishWarehouseClosurePiece> closurePieces = BuildClosurePieces(openings, garageOpening, log);
            MeshData data = BuildRoomMesh(wallPlans, closurePieces);
            _mesh = new Mesh { name = "OC_FishWarehouse_InteriorRoomMesh" };
            _mesh.vertices = data.Vertices.ToArray();
            _mesh.triangles = data.Triangles.ToArray();
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            MeshFilter filter = _root.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;
            MeshRenderer renderer = _root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.receiveShadows = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            MeshCollider collider = _root.AddComponent<MeshCollider>();
            collider.sharedMesh = _mesh;

            _openings = openings;
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Fish Warehouse authored interior room attachment failed safely: {ex.Message}");
            Dispose();
            return false;
        }
    }

    public bool Activate()
    {
        if (!IsReady)
            return false;

        try
        {
            _root!.SetActive(true);
            return true;
        }
        catch
        {
            _root = null;
            _mesh = null;
            _openings = Array.Empty<FishWarehouseWallOpening>();
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

        _root = null;
        _mesh = null;
        _openings = Array.Empty<FishWarehouseWallOpening>();
    }

    private static MeshData BuildRoomMesh(
        IReadOnlyList<FishWarehouseWallPlan> wallPlans,
        IReadOnlyList<FishWarehouseClosurePiece> closurePieces)
    {
        var data = new MeshData();
        float halfWidth = FishWarehouseInteriorRoomDefinition.HalfWidth;
        float halfDepth = FishWarehouseInteriorRoomDefinition.HalfDepth;
        float ceilingY = FishWarehouseInteriorRoomDefinition.CeilingY;

        foreach (FishWarehouseWallPlan plan in wallPlans)
        {
            foreach (FishWarehouseWallSegment segment in plan.Segments)
            {
                AddWallQuad(
                    data,
                    segment.Wall,
                    segment.HorizontalMinimum,
                    segment.HorizontalMaximum,
                    segment.Bottom,
                    segment.Top,
                    halfWidth,
                    halfDepth);
            }
        }

        // The enlarged closure ceiling replaces the room's own ceiling; fall back
        // to the room-sized ceiling only if no closure ceiling was planned so the
        // top is never left open.
        if (!closurePieces.Any(piece => piece.Plane == FishWarehouseClosurePlane.CeilingXZ))
            AddCeiling(data, halfWidth, halfDepth, ceilingY);

        foreach (FishWarehouseClosurePiece piece in closurePieces)
            AddClosurePiece(data, piece);

        return data;
    }

    private static IReadOnlyList<FishWarehouseClosurePiece> BuildClosurePieces(
        IReadOnlyList<FishWarehouseWallOpening> openings,
        FishWarehouseGarageOpeningProjection garageOpening,
        Action<string>? log)
    {
        var pieces = new List<FishWarehouseClosurePiece>();
        if (FishWarehouseInteriorClosureDefinition.TryPlanCeiling(out FishWarehouseClosurePiece? ceiling) && ceiling is not null)
            pieces.Add(ceiling);
        else
            log?.Invoke("Fish Warehouse interior closure: enlarged ceiling could not be planned; falling back to the room-sized ceiling.");

        FishWarehouseWallOpening? personnel = openings.FirstOrDefault(opening =>
            string.Equals(opening.SemanticId, "personnel", StringComparison.Ordinal));
        if (FishWarehouseInteriorClosureDefinition.TryPlanPersonnelSeal(personnel, out FishWarehouseClosurePiece? seal) && seal is not null)
            pieces.Add(seal);
        else
            log?.Invoke("Fish Warehouse interior closure: personnel seal could not be planned; skipping it safely.");

        if (FishWarehouseInteriorClosureDefinition.TryPlanGarageHeader(garageOpening, out FishWarehouseClosurePiece? header) && header is not null)
            pieces.Add(header);
        else
            log?.Invoke("Fish Warehouse interior closure: garage header could not be planned; skipping it safely.");

        return pieces;
    }

    private static void AddClosurePiece(MeshData data, FishWarehouseClosurePiece piece)
    {
        float h0 = piece.HorizontalMinimum;
        float h1 = piece.HorizontalMaximum;
        float v0 = piece.VerticalMinimum;
        float v1 = piece.VerticalMaximum;
        float p = piece.PlaneCoordinate;
        Vector3 a, b, c, d;
        switch (piece.Plane)
        {
            case FishWarehouseClosurePlane.CeilingXZ:
                // Horizontal = X, Vertical = Z, at Y = p.
                a = new Vector3(h0, p, v0);
                b = new Vector3(h1, p, v0);
                c = new Vector3(h1, p, v1);
                d = new Vector3(h0, p, v1);
                break;
            case FishWarehouseClosurePlane.WallX:
                // Horizontal = Z, Vertical = Y, at X = p.
                a = new Vector3(p, v0, h0);
                b = new Vector3(p, v0, h1);
                c = new Vector3(p, v1, h1);
                d = new Vector3(p, v1, h0);
                break;
            default:
                // WallZ: Horizontal = X, Vertical = Y, at Z = p.
                a = new Vector3(h0, v0, p);
                b = new Vector3(h1, v0, p);
                c = new Vector3(h1, v1, p);
                d = new Vector3(h0, v1, p);
                break;
        }

        // Emit both windings. The ceiling only needs its downward face, and the
        // native-plane infills must read from inside and outside; adding both
        // faces makes the piece correct regardless of viewing side without
        // depending on winding order or a shader cull flag.
        AddQuad(data, a, b, c, d, data.Vertices.Count);
        AddQuad(data, d, c, b, a, data.Vertices.Count);
    }

    private static void AddWallQuad(
        MeshData data,
        FishWarehouseInteriorDoorSide side,
        float start,
        float end,
        float bottom,
        float top,
        float halfWidth,
        float halfDepth)
    {
        int index = data.Vertices.Count;
        switch (side)
        {
            case FishWarehouseInteriorDoorSide.West:
                AddQuad(data, new Vector3(-halfWidth, bottom, end), new Vector3(-halfWidth, bottom, start), new Vector3(-halfWidth, top, start), new Vector3(-halfWidth, top, end), index);
                break;
            case FishWarehouseInteriorDoorSide.East:
                AddQuad(data, new Vector3(halfWidth, bottom, start), new Vector3(halfWidth, bottom, end), new Vector3(halfWidth, top, end), new Vector3(halfWidth, top, start), index);
                break;
            case FishWarehouseInteriorDoorSide.South:
                AddQuad(data, new Vector3(start, bottom, -halfDepth), new Vector3(end, bottom, -halfDepth), new Vector3(end, top, -halfDepth), new Vector3(start, top, -halfDepth), index);
                break;
            default:
                AddQuad(data, new Vector3(end, bottom, halfDepth), new Vector3(start, bottom, halfDepth), new Vector3(start, top, halfDepth), new Vector3(end, top, halfDepth), index);
                break;
        }
    }

    private static void AddCeiling(MeshData data, float halfWidth, float halfDepth, float ceilingY)
    {
        int index = data.Vertices.Count;
        AddQuad(
            data,
            new Vector3(-halfWidth, ceilingY, -halfDepth),
            new Vector3(halfWidth, ceilingY, -halfDepth),
            new Vector3(halfWidth, ceilingY, halfDepth),
            new Vector3(-halfWidth, ceilingY, halfDepth),
            index);
    }

    private static void AddQuad(MeshData data, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int index)
    {
        data.Vertices.Add(a);
        data.Vertices.Add(b);
        data.Vertices.Add(c);
        data.Vertices.Add(d);
        data.Triangles.Add(index);
        data.Triangles.Add(index + 1);
        data.Triangles.Add(index + 2);
        data.Triangles.Add(index);
        data.Triangles.Add(index + 2);
        data.Triangles.Add(index + 3);
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

    private static Transform? FindEntranceDoor(Transform sourceRoot)
    {
        Transform[] matches = sourceRoot.GetComponentsInChildren<Transform>(includeInactive: true)
            .Where(transform => string.Equals(transform.name, "Industrial Metal Door (Static)", StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static FishWarehouseTransformSignature ToTransformSignature(Transform transform) =>
        new(
            new FishWarehouseTransformVector(transform.position.x, transform.position.y, transform.position.z),
            new FishWarehouseTransformVector(transform.eulerAngles.x, transform.eulerAngles.y, transform.eulerAngles.z),
            new FishWarehouseTransformVector(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z));

    private static bool IsAlive(UnityEngine.Object? value)
    {
        try
        {
            return value is not null && value != null;
        }
        catch
        {
            return false;
        }
    }

    private static Material? FindBrickMaterial(Transform sourceRoot)
    {
        Renderer[] renderers = sourceRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
        Renderer? brick = renderers.FirstOrDefault(renderer =>
            string.Equals(renderer.transform.name, "BrickTrim", StringComparison.OrdinalIgnoreCase));
        return brick?.sharedMaterials.FirstOrDefault(material => material is not null) ??
            renderers.SelectMany(renderer => renderer.sharedMaterials).FirstOrDefault(material => material is not null);
    }

    private static Material CreateFallbackMaterial()
    {
        Shader? shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader is null)
            throw new InvalidOperationException("No usable Lit or Standard shader was found for the authored interior room.");

        var material = new Material(shader) { name = "OC_FishWarehouse_InteriorBrickFallback" };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", new Color(0.42f, 0.27f, 0.2f, 1f));
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", new Color(0.42f, 0.27f, 0.2f, 1f));
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", 0f);
        material.renderQueue = 2000;
        material.SetOverrideTag("RenderType", "Opaque");
        material.doubleSidedGI = true;
        return material;
    }

    private sealed class MeshData
    {
        public List<Vector3> Vertices { get; } = new();
        public List<int> Triangles { get; } = new();
    }
}
