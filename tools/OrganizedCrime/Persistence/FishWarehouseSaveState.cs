namespace OrganizedCrime.Persistence;

public sealed record FishWarehouseSaveState(
    int SchemaVersion,
    string PropertyCode,
    bool Unlocked,
    bool Owned,
    bool EmployeeHomePlaced,
    int? EmployeeAgentTypeId,
    FishWarehouseReplayCheckpoint? Replay,
    IReadOnlyList<FishWarehouseUnsupportedEmployeeRecord> UnsupportedEmployees)
{
    public const int CurrentSchemaVersion = 2;
    public const string ExpectedPropertyCode = "oc_fishwarehouse";

    public static FishWarehouseSaveState OwnedState() =>
        new(
            CurrentSchemaVersion,
            ExpectedPropertyCode,
            Unlocked: true,
            Owned: true,
            EmployeeHomePlaced: false,
            EmployeeAgentTypeId: null,
            Replay: null,
            UnsupportedEmployees: Array.Empty<FishWarehouseUnsupportedEmployeeRecord>());

    public static FishWarehouseSaveState UnownedState() =>
        new(
            CurrentSchemaVersion,
            ExpectedPropertyCode,
            Unlocked: false,
            Owned: false,
            EmployeeHomePlaced: false,
            EmployeeAgentTypeId: null,
            Replay: null,
            UnsupportedEmployees: Array.Empty<FishWarehouseUnsupportedEmployeeRecord>());
}
