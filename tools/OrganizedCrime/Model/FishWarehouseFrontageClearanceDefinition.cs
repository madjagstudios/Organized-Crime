namespace OrganizedCrime.Model;

public sealed record FishWarehouseFrontageBounds(
    float MinX,
    float MaxX,
    float MinZ,
    float MaxZ)
{
    public bool Contains(FishWarehouseDockDefinition dock) =>
        dock.CenterX - FishWarehouseLoadingDockDefinition.GetAxisAlignedHalfExtents(dock).X >= MinX &&
        dock.CenterX + FishWarehouseLoadingDockDefinition.GetAxisAlignedHalfExtents(dock).X <= MaxX &&
        dock.CenterZ - FishWarehouseLoadingDockDefinition.GetAxisAlignedHalfExtents(dock).Z >= MinZ &&
        dock.CenterZ + FishWarehouseLoadingDockDefinition.GetAxisAlignedHalfExtents(dock).Z <= MaxZ;

    public bool Overlaps(FishWarehouseFrontageBounds other) =>
        MinX <= other.MaxX && MaxX >= other.MinX &&
        MinZ <= other.MaxZ && MaxZ >= other.MinZ;
}

public sealed record FishWarehouseFrontageCandidate(
    string Path,
    bool ActiveSelf,
    bool IsStatic,
    bool HasRenderer,
    bool HasCollider,
    IReadOnlyCollection<string> ComponentTypeNames,
    FishWarehouseFrontageBounds Bounds,
    bool HasProtectedAncestor = false,
    bool HasUnknownBehaviour = false);

public sealed record FishWarehouseFrontageCandidateState(string Path, bool OriginalActiveSelf);

public enum FishWarehouseFrontageClearanceDecision
{
    Clear,
    OutsideEnvelope,
    Protected,
    NotEligible
}

public static class FishWarehouseFrontageClearanceDefinition
{
    private const string DocksRegionPath = "Map/Hyland Point/Region_Docks";
    public const float ClearanceMargin = 0.5f;

    private static readonly string[] ApprovedShippingContainerNames =
    {
        "Red Shipping Container",
        "Red Shipping Container (1)",
        "Blue Shipping Container",
        "Green Shipping Container"
    };

    private static readonly string[] ProtectedTypeTokens =
    {
        "LandVehicle",
        "ParkingLot",
        "LoadingDock",
        "VehicleDetector",
        "NPC",
        "DealLocation",
        "TransitEntity",
        "NetworkObject",
        "Property"
    };

    public static FishWarehouseFrontageBounds DockEnvelope { get; } = CreateDockEnvelope();

    public static IReadOnlyList<FishWarehouseFrontageBounds> DockClearanceBounds { get; } =
        Array.AsReadOnly(FishWarehouseLoadingDockDefinition.Docks
            .Select(CreateDockClearanceBounds)
            .ToArray());

    public static FishWarehouseFrontageBounds ClearanceEnvelope { get; } = new(
        DockEnvelope.MinX - ClearanceMargin,
        DockEnvelope.MaxX + ClearanceMargin,
        DockEnvelope.MinZ - ClearanceMargin,
        DockEnvelope.MaxZ + ClearanceMargin);

    public static FishWarehouseFrontageClearanceDecision Evaluate(FishWarehouseFrontageCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(candidate.ComponentTypeNames);
        ArgumentNullException.ThrowIfNull(candidate.Bounds);

        var approvedShippingContainer = IsApprovedShippingContainerPath(candidate.Path);
        if (!approvedShippingContainer && !IsInsideAnyDockClearanceScope(candidate.Bounds))
            return FishWarehouseFrontageClearanceDecision.OutsideEnvelope;

        if (candidate.HasProtectedAncestor ||
            IsProtectedPath(candidate.Path) ||
            candidate.ComponentTypeNames.Any(IsProtectedTypeName))
            return FishWarehouseFrontageClearanceDecision.Protected;

        if (!approvedShippingContainer && candidate.HasUnknownBehaviour)
            return FishWarehouseFrontageClearanceDecision.NotEligible;

        return (candidate.IsStatic || approvedShippingContainer) &&
               (candidate.HasRenderer || candidate.HasCollider) &&
               candidate.ComponentTypeNames.All(IsAllowedStaticComponentTypeName)
            ? FishWarehouseFrontageClearanceDecision.Clear
            : FishWarehouseFrontageClearanceDecision.NotEligible;
    }

    public static bool IsApprovedShippingContainerPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var prefix = DocksRegionPath + "/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var objectName = path[prefix.Length..];
        return !objectName.Contains('/') &&
               ApprovedShippingContainerNames.Contains(objectName, StringComparer.Ordinal);
    }

    public static IReadOnlyList<FishWarehouseFrontageCandidateState> CaptureOriginalStates(
        IEnumerable<FishWarehouseFrontageCandidateState> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return Array.AsReadOnly(candidates.ToArray());
    }

    public static bool CanRestore(
        IEnumerable<FishWarehouseFrontageCandidateState> states,
        int restoredCount)
    {
        ArgumentNullException.ThrowIfNull(states);
        return restoredCount == states.Count();
    }

    public static bool IsProtectedTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        return ProtectedTypeTokens.Any(token =>
            typeName.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsProtectedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return path.Contains("parking", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("billy", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<int> GetOverlappingDockIndices(FishWarehouseFrontageBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        return Array.AsReadOnly(DockClearanceBounds
            .Select((clearance, index) => (clearance, index))
            .Where(entry => entry.clearance.Overlaps(bounds))
            .Select(entry => entry.index)
            .ToArray());
    }

    public static bool IsInsideAnyDockClearanceScope(FishWarehouseFrontageBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        return DockClearanceBounds.Any(clearance => clearance.Overlaps(bounds));
    }

    public static bool IsAllowedStaticComponentTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        var simpleName = typeName[(typeName.LastIndexOf('.') + 1)..];
        return simpleName is
            "Transform" or
            "MeshFilter" or
            "Renderer" or
            "MeshRenderer" or
            "SkinnedMeshRenderer" or
            "Collider" or
            "BoxCollider" or
            "MeshCollider" or
            "SphereCollider" or
            "CapsuleCollider" or
            "LODGroup";
    }

    private static FishWarehouseFrontageBounds CreateDockEnvelope()
    {
        var docks = FishWarehouseLoadingDockDefinition.Docks;
        return new FishWarehouseFrontageBounds(
            docks.Min(dock => dock.CenterX - FishWarehouseLoadingDockDefinition.GetAxisAlignedHalfExtents(dock).X),
            docks.Max(dock => dock.CenterX + FishWarehouseLoadingDockDefinition.GetAxisAlignedHalfExtents(dock).X),
            docks.Min(dock => dock.CenterZ - FishWarehouseLoadingDockDefinition.GetAxisAlignedHalfExtents(dock).Z),
            docks.Max(dock => dock.CenterZ + FishWarehouseLoadingDockDefinition.GetAxisAlignedHalfExtents(dock).Z));
    }

    private static FishWarehouseFrontageBounds CreateDockClearanceBounds(FishWarehouseDockDefinition dock)
    {
        var extents = FishWarehouseLoadingDockDefinition.GetAxisAlignedHalfExtents(dock);
        return new(
            dock.CenterX - extents.X - ClearanceMargin,
            dock.CenterX + extents.X + ClearanceMargin,
            dock.CenterZ - extents.Z - ClearanceMargin,
            dock.CenterZ + extents.Z + ClearanceMargin);
    }
}
