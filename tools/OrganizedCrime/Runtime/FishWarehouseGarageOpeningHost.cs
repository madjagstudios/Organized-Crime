using OrganizedCrime.Model;
using UnityEngine;

namespace OrganizedCrime.Runtime;

public sealed record FishWarehouseGarageOpeningPreflightResult(
    Transform Anchor,
    FishWarehouseGarageOpeningProjection Projection,
    IReadOnlyList<Renderer> TargetFaces,
    IReadOnlyList<Collider> TargetColliders,
    FishWarehouseTransitionPrism TransitionPrism);

/// <summary>
/// Performs the read-only garage preflight and owns only the recorded selected
/// garage-panel renderer/collider state. It deliberately does not activate a
/// room or make ownership decisions; the later lifecycle coordinator owns that.
/// </summary>
public sealed class FishWarehouseGarageOpeningHost
{
    private const float FloorSupportTolerance = 0.20f;
    private const float FloorRayStartAboveCeiling = 1f;

    private FishWarehouseGarageOpeningPreflightResult? _preflight;
    private FishWarehouseGarageTargetMutationState? _mutation;
    private IFishWarehouseGarageOverlapBoxQuery? _physicsQuery;
    private bool _opened;

    public FishWarehouseGarageOpeningHost(IFishWarehouseGarageOverlapBoxQuery? physicsQuery = null)
    {
        _physicsQuery = physicsQuery;
    }

    public FishWarehouseGarageOpeningPreflightResult? Preflight => _preflight;

    public bool TryPreflight(
        Transform anchor,
        Transform propertyRoot,
        out FishWarehouseGarageOpeningPreflightResult? result,
        Action<string>? log = null)
    {
        result = null;
        if (_preflight is not null)
        {
            result = _preflight;
            return true;
        }

        if (!IsAlive(anchor) || !IsAlive(propertyRoot))
        {
            log?.Invoke("Fish Warehouse garage preflight rejected: anchor or property root was unavailable.");
            return false;
        }

        try
        {
            if (!FishWarehouseTransformSignatureDefinition.IsCoincident(
                    ToTransformSignature(anchor),
                    ToTransformSignature(propertyRoot)))
            {
                log?.Invoke("Fish Warehouse garage preflight rejected: anchor/property-root transform signatures were not coincident.");
                return false;
            }

            FishWarehouseGarageRuntimeAnchor runtimeAnchor = ToRuntimeAnchor(anchor);

            if (!TryResolveApprovedScene(
                    out Transform? primaryRoot,
                    out Transform? primaryPanel,
                    out Transform? alternateRoot,
                    out FishWarehouseGarageSceneSelection? selection,
                    log) ||
                primaryRoot is null ||
                primaryPanel is null ||
                alternateRoot is null ||
                selection is null)
            {
                return false;
            }

            if (!TryCreateExactTargetSet(
                    selection,
                    primaryRoot,
                    primaryPanel,
                    alternateRoot,
                    out FishWarehouseGarageTargetSet? targetSet,
                    out Renderer[] targetFaces,
                    out Collider[] targetColliders,
                    out Renderer? primaryPanelRenderer,
                    out Renderer? alternateRenderer,
                    log) ||
                targetSet is null ||
                primaryPanelRenderer is null ||
                alternateRenderer is null)
            {
                return false;
            }

            if (!TryCreateRuntimeEvidence(
                    primaryRoot,
                    primaryPanel,
                    primaryPanelRenderer,
                    out FishWarehouseGaragePanelRuntimeEvidence primaryEvidence) ||
                !TryCreateRuntimeEvidence(
                    alternateRoot,
                    alternateRoot,
                    alternateRenderer,
                    out FishWarehouseGaragePanelRuntimeEvidence alternateEvidence) ||
                !FishWarehouseGarageRuntimeEvidenceMapper.TryMap(
                    runtimeAnchor,
                    primaryEvidence,
                    out FishWarehouseGaragePanelRuntimeMapping primaryMapping) ||
                !FishWarehouseGarageRuntimeEvidenceMapper.TryMap(
                    runtimeAnchor,
                    alternateEvidence,
                    out FishWarehouseGaragePanelRuntimeMapping alternateMapping))
            {
                log?.Invoke("Fish Warehouse garage preflight rejected: selected renderer runtime evidence could not produce a safety envelope and physical plane.");
                return false;
            }

            FishWarehouseAnchorLocalBounds panelBounds = primaryMapping.SafetyEnvelope;
            FishWarehousePhysicalPanelPlane physicalPanelPlane = primaryMapping.PhysicalPanelPlane;
            FishWarehouseAnchorLocalBounds alternateBounds = alternateMapping.SafetyEnvelope;
            FishWarehousePhysicalPanelPlane alternatePhysicalPanelPlane = alternateMapping.PhysicalPanelPlane;
            if (
                !FishWarehouseGarageOpeningGeometryDefinition.IsNorthWallPhysicalPlane(panelBounds, physicalPanelPlane) ||
                !FishWarehouseGarageOpeningGeometryDefinition.IsNorthWallPhysicalPlane(alternateBounds, alternatePhysicalPanelPlane) ||
                !FishWarehouseGarageOpeningGeometryDefinition.AreCoincidentPhysicalFaces(
                    panelBounds,
                    physicalPanelPlane,
                    alternateBounds,
                    alternatePhysicalPanelPlane))
            {
                log?.Invoke($"Fish Warehouse garage preflight rejected: independently observed primary/alternate renderers did not agree with coincident physical north-wall faces (primaryEnvelope={FormatBounds(panelBounds)}, alternateEnvelope={FormatBounds(alternateBounds)}).");
                return false;
            }
            FishWarehouseGarageOpeningCandidate[] preliminaryCandidates = CreateCandidates(
                primaryRoot,
                primaryPanel,
                alternateRoot,
                panelBounds,
                physicalPanelPlane,
                alternateBounds,
                alternatePhysicalPanelPlane,
                Array.Empty<FishWarehouseAnchorLocalBounds>(),
                hasFloorSupport: true);
            if (!FishWarehouseGarageOpeningDefinition.TryProject(preliminaryCandidates, out FishWarehouseGarageOpeningProjection? preliminaryProjection) ||
                preliminaryProjection is null)
            {
                log?.Invoke("Fish Warehouse garage preflight rejected: live panel extrema did not satisfy the approved opening projection contract.");
                return false;
            }

            var targetColliderIds = targetColliders
                .Select(collider => collider.GetInstanceID())
                .ToHashSet();
            IFishWarehouseGarageOverlapBoxQuery physicsQuery = _physicsQuery ??
                new UnityOverlapBoxQuery(runtimeAnchor);
            if (!FishWarehouseGarageOverlapBoxClearanceDefinition.TryQueryNonTargetBounds(
                preliminaryProjection.TransitionPrism,
                runtimeAnchor,
                physicsQuery,
                targetColliderIds.Select(id => id.ToString()),
                out IReadOnlyList<FishWarehouseAnchorLocalBounds> survivingNonTargetColliders))
            {
                log?.Invoke("Fish Warehouse garage preflight rejected: the elevated transition-prism overlap query could not produce valid non-target bounds.");
                return false;
            }
            bool transitionPrismClear = FishWarehouseGarageOpeningClearanceDefinition.IsTraversableVolumeClear(
                preliminaryProjection.TransitionPrism,
                survivingNonTargetColliders);
            bool floorBandClear = FishWarehouseGarageOpeningClearanceDefinition.IsFloorBandClear(
                preliminaryProjection.TransitionPrism,
                survivingNonTargetColliders);
            bool hasFloorSupport = HasContinuousFloorSupport(
                anchor,
                preliminaryProjection.TransitionPrism,
                targetColliderIds,
                log);
            FishWarehouseGarageOpeningCandidate[] finalCandidates = CreateCandidates(
                primaryRoot,
                primaryPanel,
                alternateRoot,
                panelBounds,
                physicalPanelPlane,
                alternateBounds,
                alternatePhysicalPanelPlane,
                survivingNonTargetColliders,
                hasFloorSupport);
            if (!transitionPrismClear ||
                !floorBandClear ||
                !hasFloorSupport ||
                !FishWarehouseGarageOpeningDefinition.TryProject(finalCandidates, out FishWarehouseGarageOpeningProjection? projection) ||
                projection is null)
            {
                log?.Invoke("Fish Warehouse garage preflight rejected: transition-prism clearance, floor support, or final projection validation failed before mutation.");
                return false;
            }

            result = new FishWarehouseGarageOpeningPreflightResult(
                anchor,
                projection,
                Array.AsReadOnly(targetFaces),
                Array.AsReadOnly(targetColliders),
                projection.TransitionPrism);
            _preflight = result;
            _mutation = new FishWarehouseGarageTargetMutationState(targetSet);
            _physicsQuery = physicsQuery;
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Fish Warehouse garage preflight failed safely before mutation: {ex.Message}");
            return false;
        }
    }

    public bool TryOpen(Action<string>? log = null)
    {
        if (_opened)
            return true;
        if (_preflight is null || _mutation is null)
        {
            log?.Invoke("Fish Warehouse garage opening rejected: no successful preflight was available.");
            return false;
        }

        try
        {
            var state = new UnityTargetState(_preflight.TargetFaces, _preflight.TargetColliders);
            bool opened = _mutation.TryOpen(
                preflightPassed: true,
                state,
                () =>
                {
                    var targetColliderIds = _preflight.TargetColliders
                        .Where(IsAlive)
                        .Select(collider => collider.GetInstanceID())
                        .ToHashSet();
                    return _physicsQuery is not null &&
                        FishWarehouseGarageOverlapBoxClearanceDefinition.TryQueryNonTargetBounds(
                        _preflight.TransitionPrism,
                        ToRuntimeAnchor(_preflight.Anchor),
                        _physicsQuery,
                        targetColliderIds.Select(id => id.ToString()),
                        out IReadOnlyList<FishWarehouseAnchorLocalBounds> nonTargetBounds) &&
                        FishWarehouseGarageOpeningClearanceDefinition.IsTraversableVolumeClear(
                            _preflight.TransitionPrism,
                            nonTargetBounds);
                });
            if (!opened)
            {
                log?.Invoke("Fish Warehouse garage opening rejected after mutation: the target-only state change or post-open transition-prism validation failed; recorded target states were restored.");
                return false;
            }

            _opened = true;
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Fish Warehouse garage opening failed safely after mutation: {ex.Message}. Restoring recorded states.");
            _ = Restore(log);
            return false;
        }
    }

    public bool Restore(Action<string>? log = null)
    {
        bool restored = _preflight is null || _mutation is null ||
            _mutation.Restore(new UnityTargetState(_preflight.TargetFaces, _preflight.TargetColliders));
        _opened = false;
        if (!restored)
            log?.Invoke("Fish Warehouse garage restore was partial because a recorded direct target component no longer existed or rejected restoration.");
        return restored;
    }

    public bool Dispose(Action<string>? log = null)
    {
        bool restored = Restore(log);
        _preflight = null;
        _mutation = null;
        return restored;
    }

    private static FishWarehouseGarageOpeningCandidate[] CreateCandidates(
        Transform primaryRoot,
        Transform primaryPanel,
        Transform alternateRoot,
        FishWarehouseAnchorLocalBounds panelBounds,
        FishWarehousePhysicalPanelPlane physicalPanelPlane,
        FishWarehouseAnchorLocalBounds alternateBounds,
        FishWarehousePhysicalPanelPlane alternatePhysicalPanelPlane,
        IReadOnlyList<FishWarehouseAnchorLocalBounds> survivingNonTargetColliders,
        bool hasFloorSupport)
    {
        return new[]
        {
            new FishWarehouseGarageOpeningCandidate(
                GetHierarchyPath(primaryRoot),
                primaryPanel.name,
                panelBounds,
                physicalPanelPlane,
                survivingNonTargetColliders,
                hasFloorSupport),
            new FishWarehouseGarageOpeningCandidate(
                GetHierarchyPath(alternateRoot),
                alternateRoot.name,
                alternateBounds,
                alternatePhysicalPanelPlane,
                survivingNonTargetColliders,
                hasFloorSupport)
        };
    }

    private static bool TryResolveApprovedScene(
        out Transform? primaryRoot,
        out Transform? primaryPanel,
        out Transform? alternateRoot,
        out FishWarehouseGarageSceneSelection? selection,
        Action<string>? log)
    {
        primaryRoot = null;
        primaryPanel = null;
        alternateRoot = null;
        selection = null;
        GameObject[] sceneObjects = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(gameObject => IsAlive(gameObject) && gameObject.scene.IsValid())
            .DistinctBy(gameObject => gameObject.GetInstanceID())
            .ToArray();
        var nodes = sceneObjects.Select(gameObject => new FishWarehouseGarageSceneNode(
            GetHierarchyPath(gameObject.transform),
            gameObject.name,
            gameObject.transform.parent is null ? null : GetHierarchyPath(gameObject.transform.parent)));
        if (!FishWarehouseGarageSceneSelectionDefinition.TrySelect(nodes, out selection) || selection is null)
        {
            log?.Invoke("Fish Warehouse garage preflight rejected: the approved primary root, exact direct child, and alternate root did not resolve uniquely.");
            return false;
        }

        FishWarehouseGarageSceneSelection resolvedSelection = selection;
        primaryRoot = sceneObjects.Single(gameObject => string.Equals(
            GetHierarchyPath(gameObject.transform), resolvedSelection.PrimaryRootPath, StringComparison.Ordinal)).transform;
        primaryPanel = sceneObjects.Single(gameObject => string.Equals(
            GetHierarchyPath(gameObject.transform), resolvedSelection.PrimaryPanelPath, StringComparison.Ordinal)).transform;
        alternateRoot = sceneObjects.Single(gameObject => string.Equals(
            GetHierarchyPath(gameObject.transform), resolvedSelection.AlternateRootPath, StringComparison.Ordinal)).transform;
        return true;
    }

    private static bool TryCreateExactTargetSet(
        FishWarehouseGarageSceneSelection selection,
        Transform primaryRoot,
        Transform primaryPanel,
        Transform alternateRoot,
        out FishWarehouseGarageTargetSet? targetSet,
        out Renderer[] targetFaces,
        out Collider[] targetColliders,
        out Renderer? primaryPanelRenderer,
        out Renderer? alternateRenderer,
        Action<string>? log)
    {
        targetSet = null;
        targetFaces = Array.Empty<Renderer>();
        targetColliders = Array.Empty<Collider>();
        primaryPanelRenderer = null;
        alternateRenderer = null;
        var components = new List<FishWarehouseGarageTargetComponent>();
        var renderersByIdentity = new Dictionary<string, Renderer>(StringComparer.Ordinal);
        var collidersByIdentity = new Dictionary<string, Collider>(StringComparer.Ordinal);

        AddDirectComponents(primaryRoot, selection.PrimaryRootPath, components, renderersByIdentity, collidersByIdentity);
        AddDirectComponents(primaryPanel, selection.PrimaryPanelPath, components, renderersByIdentity, collidersByIdentity);
        AddDirectComponents(alternateRoot, selection.AlternateRootPath, components, renderersByIdentity, collidersByIdentity);
        if (!FishWarehouseGarageTargetSetDefinition.TryCreate(selection, components, out targetSet) || targetSet is null)
        {
            log?.Invoke("Fish Warehouse garage preflight rejected: the direct primary-root, selected-child, and alternate-root renderer/collider census did not match the approved target set.");
            return false;
        }

        targetFaces = targetSet.Renderers
            .Select(component => renderersByIdentity[component.Identity])
            .ToArray();
        targetColliders = targetSet.Colliders
            .Select(component => collidersByIdentity[component.Identity])
            .ToArray();
        string panelRendererIdentity = targetSet.Renderers
            .Single(component => string.Equals(component.NodePath, selection.PrimaryPanelPath, StringComparison.Ordinal))
            .Identity;
        primaryPanelRenderer = renderersByIdentity[panelRendererIdentity];
        string alternateRendererIdentity = targetSet.Renderers
            .Single(component => string.Equals(component.NodePath, selection.AlternateRootPath, StringComparison.Ordinal))
            .Identity;
        alternateRenderer = renderersByIdentity[alternateRendererIdentity];
        return true;
    }

    private static void AddDirectComponents(
        Transform node,
        string nodePath,
        ICollection<FishWarehouseGarageTargetComponent> components,
        IDictionary<string, Renderer> renderersByIdentity,
        IDictionary<string, Collider> collidersByIdentity)
    {
        foreach (Renderer renderer in node.GetComponents<Renderer>().Where(IsAlive))
        {
            string identity = renderer.GetInstanceID().ToString();
            components.Add(new FishWarehouseGarageTargetComponent(
                nodePath,
                identity,
                FishWarehouseGarageTargetComponentKind.Renderer,
                renderer.enabled));
            renderersByIdentity.Add(identity, renderer);
        }

        foreach (Collider collider in node.GetComponents<Collider>().Where(IsAlive))
        {
            string identity = collider.GetInstanceID().ToString();
            components.Add(new FishWarehouseGarageTargetComponent(
                nodePath,
                identity,
                FishWarehouseGarageTargetComponentKind.Collider,
                collider.enabled));
            collidersByIdentity.Add(identity, collider);
        }
    }

    private static bool TryCreateRuntimeEvidence(
        Transform panelRoot,
        Transform expectedRendererTransform,
        Renderer renderer,
        out FishWarehouseGaragePanelRuntimeEvidence evidence)
    {
        evidence = default;
        Mesh? mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh is null || renderer.transform != expectedRendererTransform)
            return false;

        Vector3 lossyScale = renderer.transform.lossyScale;
        Bounds rendererBounds = renderer.bounds;
        Vector3 rendererPosition = renderer.transform.position;
        evidence = new FishWarehouseGaragePanelRuntimeEvidence(
            new FishWarehouseWorldBounds(
                new FishWarehouseGarageVector(rendererBounds.center.x, rendererBounds.center.y, rendererBounds.center.z),
                new FishWarehouseGarageVector(rendererBounds.extents.x, rendererBounds.extents.y, rendererBounds.extents.z)),
            new FishWarehouseGarageVector(mesh.bounds.center.x, mesh.bounds.center.y, mesh.bounds.center.z),
            new FishWarehouseGarageVector(mesh.bounds.extents.x, mesh.bounds.extents.y, mesh.bounds.extents.z),
            new FishWarehouseGarageVector(lossyScale.x, lossyScale.y, lossyScale.z),
            new FishWarehouseGarageVector(rendererPosition.x, rendererPosition.y, rendererPosition.z),
            ToGarageQuaternion(panelRoot.rotation),
            ToGarageQuaternion(renderer.transform.rotation),
            renderer.isPartOfStaticBatch);
        return true;
    }

    private static bool HasContinuousFloorSupport(
        Transform anchor,
        FishWarehouseTransitionPrism prism,
        ISet<int> targetColliderIds,
        Action<string>? log)
    {
        float[] xSamples = { prism.MinimumX, (prism.MinimumX + prism.MaximumX) / 2f, prism.MaximumX };
        float[] zSamples = { prism.MinimumZ, (prism.MinimumZ + prism.MaximumZ) / 2f, prism.MaximumZ };
        float rayOriginY = FishWarehouseInteriorRoomDefinition.CeilingY + FloorRayStartAboveCeiling;
        float rayDistance = rayOriginY - FishWarehouseInteriorRoomDefinition.FloorY + FloorSupportTolerance;
        foreach (float localX in xSamples)
        {
            foreach (float localZ in zSamples)
            {
                Vector3 origin = anchor.TransformPoint(new Vector3(localX, rayOriginY, localZ));
                RaycastHit[] hits = Physics.RaycastAll(
                    origin,
                    -anchor.up,
                    rayDistance,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore);
                float[] nonTargetHitLocalYs = hits
                    .Where(hit => IsAlive(hit.collider))
                    .Where(hit => !targetColliderIds.Contains(hit.collider.GetInstanceID()))
                    .Select(hit => anchor.InverseTransformPoint(hit.point).y)
                    .ToArray();
                if (!FishWarehouseGarageOpeningClearanceDefinition.IsFloorSupportedAt(
                        nonTargetHitLocalYs,
                        FloorSupportTolerance))
                {
                    log?.Invoke($"Fish Warehouse garage floor support missing at anchorLocal=({localX:0.000},{localZ:0.000}); missing floor is fail-closed data.");
                    return false;
                }
            }
        }

        return true;
    }

    private static FishWarehouseAnchorLocalBounds ToAnchorLocalBounds(
        IEnumerable<Bounds> worldBounds,
        Transform anchor)
    {
        var worldCorners = new List<FishWarehouseGarageVector>();
        foreach (Bounds bounds in worldBounds)
        {
            Vector3 minimum = bounds.min;
            Vector3 maximum = bounds.max;
            Vector3[] corners =
            {
                new(minimum.x, minimum.y, minimum.z),
                new(minimum.x, minimum.y, maximum.z),
                new(minimum.x, maximum.y, minimum.z),
                new(minimum.x, maximum.y, maximum.z),
                new(maximum.x, minimum.y, minimum.z),
                new(maximum.x, minimum.y, maximum.z),
                new(maximum.x, maximum.y, minimum.z),
                new(maximum.x, maximum.y, maximum.z)
            };
            foreach (Vector3 corner in corners)
                worldCorners.Add(new FishWarehouseGarageVector(corner.x, corner.y, corner.z));
        }

        return FishWarehouseGarageOpeningGeometryDefinition.ToAnchorLocalBounds(
            worldCorners,
            worldCorner =>
            {
                Vector3 local = anchor.InverseTransformPoint(new Vector3(worldCorner.X, worldCorner.Y, worldCorner.Z));
                return new FishWarehouseGarageVector(local.x, local.y, local.z);
            });
    }

    private static FishWarehouseAnchorLocalBounds ToAnchorLocalBounds(Bounds worldBounds, Transform anchor) =>
        ToAnchorLocalBounds(new[] { worldBounds }, anchor);

    private static FishWarehouseTransformSignature ToTransformSignature(Transform transform) =>
        new(
            new FishWarehouseTransformVector(transform.position.x, transform.position.y, transform.position.z),
            new FishWarehouseTransformVector(transform.eulerAngles.x, transform.eulerAngles.y, transform.eulerAngles.z),
            new FishWarehouseTransformVector(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z));

    private static FishWarehouseGarageRuntimeAnchor ToRuntimeAnchor(Transform anchor) =>
        new(
            new FishWarehouseGarageVector(anchor.position.x, anchor.position.y, anchor.position.z),
            ToGarageQuaternion(anchor.rotation));

    private static FishWarehouseGarageQuaternion ToGarageQuaternion(Quaternion rotation) =>
        new(rotation.x, rotation.y, rotation.z, rotation.w);

    private static string GetHierarchyPath(Transform transform)
    {
        var parts = new Stack<string>();
        for (Transform? current = transform; current is not null; current = current.parent)
            parts.Push(current.name);
        return string.Join("/", parts);
    }

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

    private static string FormatBounds(Bounds bounds) =>
        $"center=({bounds.center.x:0.000},{bounds.center.y:0.000},{bounds.center.z:0.000});extents=({bounds.extents.x:0.000},{bounds.extents.y:0.000},{bounds.extents.z:0.000})";

    private static string FormatBounds(FishWarehouseAnchorLocalBounds bounds) =>
        $"min=({bounds.MinimumX:0.000},{bounds.MinimumY:0.000},{bounds.MinimumZ:0.000});max=({bounds.MaximumX:0.000},{bounds.MaximumY:0.000},{bounds.MaximumZ:0.000})";

    private static string FormatPrism(FishWarehouseTransitionPrism prism) =>
        $"min=({prism.MinimumX:0.000},{prism.MinimumY:0.000},{prism.MinimumZ:0.000});max=({prism.MaximumX:0.000},{prism.MaximumY:0.000},{prism.MaximumZ:0.000})";

    /// <summary>
    /// The sole production OverlapBox caller. It translates Unity collider
    /// results into the pure boundary payload used by preflight and rollback.
    /// </summary>
    private sealed class UnityOverlapBoxQuery : IFishWarehouseGarageOverlapBoxQuery
    {
        private readonly FishWarehouseGarageRuntimeAnchor _anchor;

        public UnityOverlapBoxQuery(FishWarehouseGarageRuntimeAnchor anchor)
        {
            _anchor = anchor;
        }

        public IReadOnlyList<FishWarehouseGarageOverlapBoxHit> OverlapBox(FishWarehouseGarageOverlapBoxRequest request)
        {
            Collider[] overlap = Physics.OverlapBox(
                new Vector3(request.WorldCenter.X, request.WorldCenter.Y, request.WorldCenter.Z),
                new Vector3(request.HalfExtents.X, request.HalfExtents.Y, request.HalfExtents.Z),
                new Quaternion(request.Rotation.X, request.Rotation.Y, request.Rotation.Z, request.Rotation.W),
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            return overlap
                .Where(IsAlive)
                .DistinctBy(collider => collider.GetInstanceID())
                .Select(collider =>
                {
                    Bounds bounds = collider.bounds;
                    return new FishWarehouseGarageOverlapBoxHit(
                        collider.GetInstanceID().ToString(),
                        FishWarehouseGarageRuntimeEvidenceMapper.ToAnchorLocalBounds(
                            new FishWarehouseWorldBounds(
                                new FishWarehouseGarageVector(bounds.center.x, bounds.center.y, bounds.center.z),
                                new FishWarehouseGarageVector(bounds.extents.x, bounds.extents.y, bounds.extents.z)),
                            _anchor));
                })
                .ToArray();
        }
    }

    private sealed class UnityTargetState : IFishWarehouseGarageTargetState
    {
        private readonly IReadOnlyDictionary<string, Renderer> _renderers;
        private readonly IReadOnlyDictionary<string, Collider> _colliders;

        public UnityTargetState(IEnumerable<Renderer> renderers, IEnumerable<Collider> colliders)
        {
            _renderers = renderers.ToDictionary(renderer => renderer.GetInstanceID().ToString(), StringComparer.Ordinal);
            _colliders = colliders.ToDictionary(collider => collider.GetInstanceID().ToString(), StringComparer.Ordinal);
        }

        public bool TryGetEnabled(FishWarehouseGarageTargetComponent component, out bool enabled)
        {
            enabled = false;
            try
            {
                switch (component.Kind)
                {
                    case FishWarehouseGarageTargetComponentKind.Renderer when
                        _renderers.TryGetValue(component.Identity, out Renderer? renderer) && IsAlive(renderer):
                        enabled = renderer.enabled;
                        return true;
                    case FishWarehouseGarageTargetComponentKind.Collider when
                        _colliders.TryGetValue(component.Identity, out Collider? collider) && IsAlive(collider):
                        enabled = collider.enabled;
                        return true;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        public bool TrySetEnabled(FishWarehouseGarageTargetComponent component, bool enabled)
        {
            try
            {
                switch (component.Kind)
                {
                    case FishWarehouseGarageTargetComponentKind.Renderer when
                        _renderers.TryGetValue(component.Identity, out Renderer? renderer) && IsAlive(renderer):
                        renderer.enabled = enabled;
                        return true;
                    case FishWarehouseGarageTargetComponentKind.Collider when
                        _colliders.TryGetValue(component.Identity, out Collider? collider) && IsAlive(collider):
                        collider.enabled = enabled;
                        return true;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
