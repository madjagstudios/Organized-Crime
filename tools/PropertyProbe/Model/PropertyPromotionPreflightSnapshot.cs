namespace OrganizedCrime.PropertyProbe.Model;

public sealed record PropertyPromotionPreflightSnapshot(
    string ProposedPropertyCode,
    string TargetName,
    string TargetPath,
    bool HasNpcEnterableBuilding,
    string? BuildingName,
    string? BuildingGuid,
    int DoorCount,
    bool HasPropertyComponent,
    bool HasBusinessComponent,
    bool HasTransitComponent,
    bool IsInPropertyCollection,
    bool IsInOwnedPropertyCollection,
    bool IsInUnownedPropertyCollection,
    int ExistingPropertyCount,
    int ExistingOwnedPropertyCount,
    int ExistingUnownedPropertyCount)
{
    public static PropertyPromotionPreflightSnapshot Test(
        string proposedPropertyCode,
        string targetName,
        string targetPath,
        bool hasNpcEnterableBuilding = false,
        bool hasPropertyComponent = false,
        bool isInPropertyCollection = false,
        bool isInOwnedPropertyCollection = false,
        bool isInUnownedPropertyCollection = false)
    {
        return new PropertyPromotionPreflightSnapshot(
            ProposedPropertyCode: proposedPropertyCode,
            TargetName: targetName,
            TargetPath: targetPath,
            HasNpcEnterableBuilding: hasNpcEnterableBuilding,
            BuildingName: hasNpcEnterableBuilding ? targetName : null,
            BuildingGuid: hasNpcEnterableBuilding ? "test-building-guid" : null,
            DoorCount: hasNpcEnterableBuilding ? 1 : 0,
            HasPropertyComponent: hasPropertyComponent,
            HasBusinessComponent: false,
            HasTransitComponent: false,
            IsInPropertyCollection: isInPropertyCollection,
            IsInOwnedPropertyCollection: isInOwnedPropertyCollection,
            IsInUnownedPropertyCollection: isInUnownedPropertyCollection,
            ExistingPropertyCount: 0,
            ExistingOwnedPropertyCount: 0,
            ExistingUnownedPropertyCount: 0);
    }
}
