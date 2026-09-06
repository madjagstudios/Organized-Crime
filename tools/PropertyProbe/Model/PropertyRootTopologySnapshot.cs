namespace OrganizedCrime.PropertyProbe.Model;

public sealed record PropertyRootTopologyEntry(
    string PropertyCode,
    string PropertyName,
    string PropertyPath,
    bool LocalNetworkObjectPresent,
    bool ResolvedNetworkObjectPresent,
    bool ParentNetworkObjectPresent,
    NetworkObjectMetadata LocalNetworkObject,
    NetworkObjectMetadata ResolvedNetworkObject,
    NetworkObjectMetadata ParentNetworkObject,
    IReadOnlyList<string> ComponentTypes);

public sealed record PropertyRootTopologySnapshot(
    IReadOnlyList<PropertyRootTopologyEntry> Properties,
    string FishWarehousePath,
    bool FishWarehouseLocalNetworkObjectPresent,
    bool FishWarehouseParentNetworkObjectPresent,
    NetworkObjectMetadata FishWarehouseParentNetworkObject,
    bool DocksNetworkIdentityUnchanged,
    bool MutationAttempted,
    bool SpawnAttempted,
    bool OwnershipAttempted,
    bool PersistenceAttempted,
    string Gate,
    string? FailureReason)
{
    public static PropertyRootTopologySnapshot Test()
    {
        var docks = NetworkObjectMetadata.Empty with
        {
            Present = true,
            Networked = true,
            SceneObject = true,
            Nested = true,
            State = "Spawned",
            ObjectId = 20724,
            PrefabId = 15,
            SceneId = 1693455143299770325,
            NetworkManagerPresent = true,
            ServerManagerPresent = true,
            NetworkBehaviourCount = 1
        };

        var map = NetworkObjectMetadata.Empty with
        {
            Present = true,
            Networked = true,
            SceneObject = true,
            State = "Spawned",
            ObjectId = 36153,
            SceneId = 1693455146337029371,
            NetworkManagerPresent = true,
            ServerManagerPresent = true
        };

        return new PropertyRootTopologySnapshot(
            Properties: new[]
            {
                new PropertyRootTopologyEntry(
                    PropertyCode: "dockswarehouse",
                    PropertyName: "Docks Warehouse",
                    PropertyPath: "@Properties/DocksWarehouse",
                    LocalNetworkObjectPresent: true,
                    ResolvedNetworkObjectPresent: true,
                    ParentNetworkObjectPresent: false,
                    LocalNetworkObject: docks,
                    ResolvedNetworkObject: docks,
                    ParentNetworkObject: NetworkObjectMetadata.Empty,
                    ComponentTypes: new[] { "Property", "NetworkObject" })
            },
            FishWarehousePath: "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse",
            FishWarehouseLocalNetworkObjectPresent: false,
            FishWarehouseParentNetworkObjectPresent: true,
            FishWarehouseParentNetworkObject: map,
            DocksNetworkIdentityUnchanged: true,
            MutationAttempted: false,
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            Gate: "read-only",
            FailureReason: null);
    }
}
