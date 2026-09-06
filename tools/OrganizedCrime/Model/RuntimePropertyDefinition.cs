namespace OrganizedCrime.Model;

public sealed record RuntimePropertyDefinition(
    string PropertyCode,
    string DisplayName,
    string NativeName,
    string AnchorPath,
    string RootName,
    ushort RuntimeCollectionId,
    string BuildGridRootName,
    string BuildGridGuid)
{
    public static RuntimePropertyDefinition FishWarehouse { get; } = new(
        PropertyCode: "oc_fishwarehouse",
        DisplayName: "Syndicate Warehouse",
        NativeName: "Fish Warehouse",
        AnchorPath: "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse",
        RootName: "OC_FishWarehouse_PropertyRoot",
        RuntimeCollectionId: 65000,
        BuildGridRootName: "OC_FishWarehouse_BuildGrid",
        BuildGridGuid: "d8b7e1d4-3f1d-4cf0-9b7f-2e2fc8d6c6d1");

    public bool HasBuildGridGuid(string? gridGuid) =>
        !string.IsNullOrWhiteSpace(gridGuid) &&
        string.Equals(BuildGridGuid, gridGuid, StringComparison.OrdinalIgnoreCase);
}

public sealed record FishWarehouseSavedObjectDescriptor(
    int PayloadIndex,
    string DataType,
    string Guid,
    int SavedLoadOrder,
    bool IsDependent,
    bool IsSavedEmployeeHome);
