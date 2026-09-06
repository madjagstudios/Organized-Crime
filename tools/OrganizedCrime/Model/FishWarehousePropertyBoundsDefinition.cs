namespace OrganizedCrime.Model;

public static class FishWarehousePropertyBoundsDefinition
{
    // Keep the authored room and its employee idle points inside the gameplay
    // bounds so the player and employee remain in the same management space.
    public const float MinX = -15.5f;
    public const float MaxX = FishWarehouseInteriorRoomDefinition.HalfWidth;
    public const float MinY = FishWarehouseInteriorRoomDefinition.FloorY - 0.5f;
    public const float MaxY = FishWarehouseInteriorRoomDefinition.CeilingY + 0.5f;
    public const float MinZ = -FishWarehouseInteriorRoomDefinition.HalfDepth;
    public const float MaxZ = FishWarehouseInteriorRoomDefinition.HalfDepth;

    public static bool Contains(float x, float y, float z) =>
        x >= MinX && x <= MaxX &&
        y >= MinY && y <= MaxY &&
        z >= MinZ && z <= MaxZ;

    public static bool IsStagedAttachmentUsable(bool boundsRootActiveSelf, bool colliderEnabled) =>
        boundsRootActiveSelf && colliderEnabled;
}
