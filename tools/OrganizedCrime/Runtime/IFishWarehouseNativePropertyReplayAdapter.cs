namespace OrganizedCrime.Runtime;

public sealed record FishWarehouseRestoredObjectObservation(
    bool IsResolved,
    string? ParentPropertyCode,
    bool IsEmployeeHome);

public interface IFishWarehouseNativePropertyReplayAdapter
{
    bool IsGuidRegistered(string guid);
    bool TryGetObjectLoaderLoadOrder(string dataType, out int loadOrder);
    void LoadObject(int payloadIndex);
    FishWarehouseRestoredObjectObservation ObserveObject(string guid);
}
