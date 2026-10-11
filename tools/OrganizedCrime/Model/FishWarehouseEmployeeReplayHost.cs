using OrganizedCrime.Persistence;

namespace OrganizedCrime.Model;

public enum FishWarehouseEmployeeReplayState
{
    Pending,
    Succeeded,
    Failed
}

public sealed record FishWarehouseEmployeeReplayResult(
    FishWarehouseEmployeeReplayState State,
    int CapturedEmployeeCount,
    int SupportedCapturedEmployeeCount,
    int ReplayedEmployeeCount,
    IReadOnlyList<FishWarehouseUnsupportedEmployeeRecord> UnsupportedEmployees,
    bool CanAdvanceEmployeesReplayed,
    string? PrimaryFailureReason,
    string? FailureReason,
    IReadOnlyList<string>? InventoryRestoredEmployeeGuids = null,
    IReadOnlyDictionary<string, int>? ReplayedEmployeesByIdentity = null);
