namespace OrganizedCrime.PropertyProbe.Model;

public readonly record struct Vector3Dto(float X, float Y, float Z);

public sealed record PropertySnapshot(
    string RuntimeType,
    string GameObjectName,
    string TransformPath,
    string PropertyName,
    string PropertyCode,
    bool IsOwned,
    bool OwnedByDefault,
    float Price,
    int EmployeeCapacity,
    int EmployeeCount,
    int EmployeeIdlePointCount,
    bool HasNpcSpawnPoint,
    bool HasEmployeeContainer,
    bool HasContentsContainer,
    bool HasBoundingBox,
    int GridCount,
    int BuildableItemCount,
    int ConfigurableCount,
    int LoadingDockCount,
    Vector3Dto Position,
    IReadOnlyList<LoadingDockSnapshot> LoadingDocks,
    string? CaptureError = null)
{
    public static PropertySnapshot Test(
        string code,
        int employeeCapacity = 0,
        int gridCount = 0,
        int loadingDockCount = 0,
        bool hasContainer = false,
        int employeeIdlePointCount = 0,
        int configurableCount = 0)
    {
        var docks = Enumerable.Range(0, loadingDockCount)
            .Select(i => new LoadingDockSnapshot(
                Guid: $"dock-{i}",
                GameObjectName: $"Dock {i}",
                TransformPath: $"Root/Dock {i}",
                ParentPropertyCode: code,
                Position: new Vector3Dto(0, 0, 0),
                AccessPointCount: 1,
                InputSlotCount: 0,
                OutputSlotCount: 0))
            .ToArray();

        return new PropertySnapshot(
            RuntimeType: "ScheduleOne.Property.Property",
            GameObjectName: code,
            TransformPath: $"World/{code}",
            PropertyName: code,
            PropertyCode: code,
            IsOwned: false,
            OwnedByDefault: false,
            Price: 0,
            EmployeeCapacity: employeeCapacity,
            EmployeeCount: 0,
            EmployeeIdlePointCount: employeeIdlePointCount,
            HasNpcSpawnPoint: true,
            HasEmployeeContainer: true,
            HasContentsContainer: hasContainer,
            HasBoundingBox: true,
            GridCount: gridCount,
            BuildableItemCount: 0,
            ConfigurableCount: configurableCount,
            LoadingDockCount: loadingDockCount,
            Position: new Vector3Dto(0, 0, 0),
            LoadingDocks: docks);
    }
}
