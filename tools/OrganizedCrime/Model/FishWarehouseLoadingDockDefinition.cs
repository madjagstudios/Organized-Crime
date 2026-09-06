namespace OrganizedCrime.Model;

public sealed record FishWarehouseDockDefinition(
    int Index,
    string Name,
    string DockGuid,
    string ParkingLotGuid,
    float CenterX,
    float CenterY,
    float CenterZ,
    float YawDegrees,
    float HalfWidth,
    float HalfLength);

public sealed record FishWarehouseDockOccupancySnapshot(
    bool HasDynamicVehicle,
    bool HasStaticVehicle,
    bool HasActiveDelivery);

public sealed record FishWarehouseDetectorColliderBindingDiagnostics(
    int RuntimeReferenceCount,
    int HierarchyColliderCount,
    int OwnedBoxColliderCount);

public static class FishWarehouseLoadingDockDefinition
{
    public const string TargetPropertyCode = "oc_fishwarehouse";
    public const string SourcePropertyCode = "barn";
    public const float FrontageMinX = 4.10f;
    public const float FrontageMaxX = 9.55f;
    public const float FrontageMinZ = 7.55f;
    public const float FrontageMaxZ = 13.95f;
    public const float DetectorHeight = 2.8f;

    public static IReadOnlyList<FishWarehouseDockDefinition> Docks { get; } = Array.AsReadOnly(new[]
    {
        new FishWarehouseDockDefinition(
            0,
            "OC Fish Warehouse Dock 1",
            "978f8d8e-e2bc-42fd-b218-3aa34cd4fb31",
            "b917f58c-6162-4586-940b-1ddbcc3cc559",
            6.82f, 0f, 12.285f, 90f, 1.5f, 2.6f),
        new FishWarehouseDockDefinition(
            1,
            "OC Fish Warehouse Dock 2",
            "520a20f6-aa83-4458-83bc-9106dcbd7a0c",
            "290a4590-136f-44f2-8df4-729548dd1940",
            6.82f, 0f, 9.195f, 90f, 1.5f, 2.6f)
    });

    public static (float X, float Z) GetAxisAlignedHalfExtents(FishWarehouseDockDefinition dock)
    {
        ArgumentNullException.ThrowIfNull(dock);

        var radians = dock.YawDegrees * (MathF.PI / 180f);
        var cosine = MathF.Abs(MathF.Cos(radians));
        var sine = MathF.Abs(MathF.Sin(radians));
        return (
            cosine * dock.HalfWidth + sine * dock.HalfLength,
            sine * dock.HalfWidth + cosine * dock.HalfLength);
    }

    public static bool IsInsideFrontageEnvelope(FishWarehouseDockDefinition dock) =>
        dock.CenterX - GetAxisAlignedHalfExtents(dock).X >= FrontageMinX &&
        dock.CenterX + GetAxisAlignedHalfExtents(dock).X <= FrontageMaxX &&
        dock.CenterZ - GetAxisAlignedHalfExtents(dock).Z >= FrontageMinZ &&
        dock.CenterZ + GetAxisAlignedHalfExtents(dock).Z <= FrontageMaxZ;

    public static bool Overlaps(FishWarehouseDockDefinition left, FishWarehouseDockDefinition right)
    {
        var leftExtents = GetAxisAlignedHalfExtents(left);
        var rightExtents = GetAxisAlignedHalfExtents(right);
        return MathF.Abs(left.CenterX - right.CenterX) < leftExtents.X + rightExtents.X &&
            MathF.Abs(left.CenterZ - right.CenterZ) < leftExtents.Z + rightExtents.Z;
    }

    public static bool HasValidLayout()
    {
        if (Docks.Count != 2 || !Docks.Select(dock => dock.Index).SequenceEqual(new[] { 0, 1 }))
            return false;

        var identities = Docks.SelectMany(dock => new[] { dock.DockGuid, dock.ParkingLotGuid }).ToArray();
        return identities.All(identity => Guid.TryParse(identity, out _)) &&
            identities.Distinct(StringComparer.OrdinalIgnoreCase).Count() == identities.Length &&
            Docks.All(IsInsideFrontageEnvelope) &&
            !Overlaps(Docks[0], Docks[1]);
    }

    public static bool CanPublish(int stagedCount, int validCount) =>
        stagedCount == Docks.Count && validCount == Docks.Count;

    public static bool TryBindDetectorCollider<TCollider, TBoxCollider>(
        IReadOnlyCollection<TCollider> runtimeReferences,
        IReadOnlyCollection<TCollider> hierarchyColliders,
        Func<TCollider, TBoxCollider?> tryCastBoxCollider,
        Func<TCollider, bool> isCloneOwned,
        out TBoxCollider? selected,
        out FishWarehouseDetectorColliderBindingDiagnostics diagnostics)
        where TCollider : class
        where TBoxCollider : class
    {
        ArgumentNullException.ThrowIfNull(runtimeReferences);
        ArgumentNullException.ThrowIfNull(hierarchyColliders);
        ArgumentNullException.ThrowIfNull(tryCastBoxCollider);
        ArgumentNullException.ThrowIfNull(isCloneOwned);

        var ownedBoxColliders = hierarchyColliders
            .Where(isCloneOwned)
            .Select(tryCastBoxCollider)
            .Where(collider => collider is not null)
            .Cast<TBoxCollider>()
            .ToArray();
        diagnostics = new FishWarehouseDetectorColliderBindingDiagnostics(
            runtimeReferences.Count,
            hierarchyColliders.Count,
            ownedBoxColliders.Length);

        if (ownedBoxColliders.Length != 1)
        {
            selected = default;
            return false;
        }

        selected = ownedBoxColliders[0];
        return true;
    }

    public static bool CanUnload(IEnumerable<FishWarehouseDockOccupancySnapshot> occupancy) =>
        occupancy.All(snapshot =>
            !snapshot.HasDynamicVehicle &&
            !snapshot.HasStaticVehicle &&
            !snapshot.HasActiveDelivery);
}
