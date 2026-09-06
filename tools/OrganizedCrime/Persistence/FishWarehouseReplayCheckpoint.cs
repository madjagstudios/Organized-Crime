namespace OrganizedCrime.Persistence;

public enum FishWarehouseReplayPhase
{
    Inactive,
    Captured,
    RuntimeReady,
    ObjectsReplayed,
    NavigationReady,
    EmployeesReplayed,
    DeliveriesReleased,
    Complete,
    Failed
}

public sealed record FishWarehouseReplayCheckpoint(
    string GenerationId,
    string SaveFolderKey,
    FishWarehouseReplayPhase Phase,
    string SnapshotRelativePath,
    string SnapshotSha256,
    int CapturedObjectCount,
    int CapturedEmployeeCount,
    int ReplayedObjectCount = 0,
    int ReplayedEmployeeCount = 0,
    string? FailurePhase = null,
    string? FailureReason = null,
    IReadOnlyList<string>? InventoryRestoredEmployeeGuids = null);

public sealed record FishWarehouseUnsupportedEmployeeRecord(
    string Guid,
    string DataType,
    string Identity,
    string RawJson);
