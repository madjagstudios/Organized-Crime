namespace OrganizedCrime.PropertyProbe.Model;

public sealed record BuildingCensusSnapshot(
    string Candidate,
    string GameObjectName,
    string TransformPath,
    bool ActiveSelf,
    Vector3Dto Position,
    float DistanceFromReference,
    int ChildCount,
    int ComponentCount,
    IReadOnlyList<string> ComponentTypes,
    bool HasPropertyComponent,
    bool HasBusinessComponent,
    bool HasTransitComponent,
    bool HasConfigurableComponent,
    bool HasGridComponent,
    bool HasContainerComponent,
    bool HasCollider,
    bool HasRenderer,
    string? DeliveryLocationName = null,
    string? DeliveryLocationDescription = null,
    string? DeliveryLocationGuid = null)
{
    public static BuildingCensusSnapshot Test(
        string candidate,
        string gameObjectName,
        IReadOnlyList<string> componentTypes,
        bool hasPropertyComponent = false,
        float distanceFromReference = float.PositiveInfinity,
        string? deliveryLocationName = null,
        string? deliveryLocationDescription = null,
        string? deliveryLocationGuid = null)
    {
        return new BuildingCensusSnapshot(
            Candidate: candidate,
            GameObjectName: gameObjectName,
            TransformPath: $"World/{gameObjectName}",
            ActiveSelf: true,
            Position: new Vector3Dto(0, 0, 0),
            DistanceFromReference: distanceFromReference,
            ChildCount: 0,
            ComponentCount: componentTypes.Count,
            ComponentTypes: componentTypes,
            HasPropertyComponent: hasPropertyComponent,
            HasBusinessComponent: false,
            HasTransitComponent: false,
            HasConfigurableComponent: false,
            HasGridComponent: false,
            HasContainerComponent: false,
            HasCollider: componentTypes.Any(x => x.Contains("Collider", StringComparison.OrdinalIgnoreCase)),
            HasRenderer: componentTypes.Any(x => x.Contains("Renderer", StringComparison.OrdinalIgnoreCase)),
            DeliveryLocationName: deliveryLocationName,
            DeliveryLocationDescription: deliveryLocationDescription,
            DeliveryLocationGuid: deliveryLocationGuid);
    }
}
