namespace OrganizedCrime.Model;

public static class FishWarehouseInteriorShellDefinition
{
    public const string BuildingPathToken = "Map/Hyland Point/Region_Docks/Fish Warehouse";

    public static bool IsFishWarehouseSourcePath(string? hierarchyPath) =>
        !string.IsNullOrWhiteSpace(hierarchyPath) &&
        hierarchyPath.Contains(BuildingPathToken, StringComparison.OrdinalIgnoreCase);

    public static bool IsRendererCloneCandidate(string componentTypeName, bool hasMesh) =>
        hasMesh && (string.Equals(componentTypeName, "UnityEngine.Renderer", StringComparison.Ordinal) ||
                    string.Equals(componentTypeName, "UnityEngine.MeshRenderer", StringComparison.Ordinal));

    public static bool CanPrepareMaterialSlots(int slotCount, int usableSlotCount) =>
        slotCount > 0 && usableSlotCount > 0;

    public static bool RequiresMaterialFallback(IEnumerable<string>? supportedProperties) =>
        supportedProperties is null ||
        (!supportedProperties.Contains("_Cull", StringComparer.Ordinal) &&
         !supportedProperties.Contains("_CullMode", StringComparer.Ordinal));

}
