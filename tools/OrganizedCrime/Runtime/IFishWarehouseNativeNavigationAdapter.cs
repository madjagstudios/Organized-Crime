using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum FishWarehouseNativeNavigationBuildResult
{
    Pending,
    Succeeded,
    Failed
}

public sealed record FishWarehouseNativeNavigationLiveEmployeeAgent(string Identity, int AgentTypeId);

public enum FishWarehouseNativeNavigationEmployeeScope
{
    PropertyAssigned,
    Global
}

public readonly record struct FishWarehouseNativeNavigationRuntimeGraphProbe(
    int AgentTypeId,
    bool ExteriorSampled,
    FishWarehouseNativeNavigationVector ExteriorPosition,
    bool InteriorSampled,
    bool PathComplete);

public readonly record struct FishWarehouseNativeNavigationPathObservation(
    bool StartSampled,
    bool EndSampled,
    FishWarehouseNativeNavigationPathStatus PathStatus);

public sealed record FishWarehouseNativeNavigationWorldDescriptor(
    FishWarehouseNativeNavigationSurface Surface,
    FishWarehouseNativeNavigationBox BakeBounds,
    FishWarehouseNativeNavigationLink Link,
    FishWarehouseNativeNavigationVector DoorwayInterior,
    float SampleRadius);

public sealed record FishWarehouseNativeNavigationBuildRequest(
    FishWarehouseNativeNavigationWorldDescriptor World,
    IReadOnlyList<FishWarehouseNativeNavigationVector> IdlePoints,
    FishWarehouseNativeNavigationVector? LockerPoint,
    FishWarehouseNativeNavigationVector? PackagingStationPoint,
    int? PreferredEmployeeAgentTypeId = null);

/// <summary>
/// The sole native-navigation boundary. Implementations own native handles and
/// make the supplied teardown batch idempotent.
/// </summary>
public interface IFishWarehouseNativeNavigationAdapter
{
    IReadOnlyList<FishWarehouseNativeNavigationAgentSetting> GetAgentSettings();

    IReadOnlyList<FishWarehouseNativeNavigationLiveEmployeeAgent> FindLiveEmployeeAgents(
        FishWarehouseNativeNavigationEmployeeScope scope);

    FishWarehouseNativeNavigationRuntimeGraphProbe ProbeGraph(
        int agentTypeId,
        FishWarehouseNativeNavigationWorldDescriptor world);

    int AddSyntheticSurface(int agentTypeId, FishWarehouseNativeNavigationWorldDescriptor world);

    int AddLink(
        int agentTypeId,
        FishWarehouseNativeNavigationWorldDescriptor world,
        FishWarehouseNativeNavigationVector exteriorEndpoint);

    void ClearNativeSampleCache();

    FishWarehouseNativeNavigationPathObservation ValidatePath(
        FishWarehouseNativeNavigationGraphRole graphRole,
        int agentTypeId,
        string targetName,
        FishWarehouseNativeNavigationVector start,
        FishWarehouseNativeNavigationVector end);

    void Remove(FishWarehouseNativeNavigationTeardownOperation operation);
}
