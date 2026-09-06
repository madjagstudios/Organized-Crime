namespace OrganizedCrime.PropertyProbe.Model;

public sealed record LoadingDockSnapshot(
    string Guid,
    string GameObjectName,
    string TransformPath,
    string ParentPropertyCode,
    Vector3Dto Position,
    int AccessPointCount,
    int InputSlotCount,
    int OutputSlotCount);
