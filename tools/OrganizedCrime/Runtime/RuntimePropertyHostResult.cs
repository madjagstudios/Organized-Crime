namespace OrganizedCrime.Runtime;

public sealed record RuntimePropertyHostResult(
    bool Succeeded,
    string Stage,
    bool PropertyRegistered,
    bool NetworkRegistrationPassed,
    bool SpawnPassed,
    bool DespawnPassed,
    bool CleanupPassed,
    bool AuthoredCollectionUnchanged,
    bool DocksNetworkIdentityUnchanged,
    string? FailureReason)
{
    public static RuntimePropertyHostResult Failure(string stage, string reason) =>
        new(
            Succeeded: false,
            Stage: stage,
            PropertyRegistered: false,
            NetworkRegistrationPassed: false,
            SpawnPassed: false,
            DespawnPassed: false,
            CleanupPassed: true,
            AuthoredCollectionUnchanged: true,
            DocksNetworkIdentityUnchanged: true,
            FailureReason: reason);
}

public interface IRuntimePropertyHost
{
    RuntimePropertyHostResult Start(Model.RuntimePropertyDefinition definition);
    bool TrySetOwned();
    bool PersistenceRuntimeReady { get; }
    void ConfigurePersistenceReplay(bool objectReplayRequired, int? employeeAgentTypeId);
    void MarkPersistenceObjectsReplayed();
    bool TryAdoptRestoredEmployeeHome();
    void UpdateOwnedFeatures();
    FishWarehouseContinuousNavigationState EmployeeNavigationState { get; }
    bool EmployeeNavigationReady { get; }
    int? EmployeeNavigationAgentTypeId { get; }
    RuntimePropertyHostResult Stop();

    /// <summary>
    /// Drops every reference to a runtime whose game scene has already been unloaded
    /// (OC-108). Its objects were destroyed with the scene, so there is nothing left to
    /// restore, and a pending cleanup must not block the next load.
    /// </summary>
    void AbandonAfterSceneUnload() { }
}

public interface IFishWarehouseLightingOwnerQa
{
    bool LightingActive { get; }
    bool TryAddLampAbovePlayer();
    bool TryRemoveMostRecentLamp();
    bool TryAdjustMostRecentLampHeight(float delta);
    bool TryAdjustAllLampIntensity(float delta);
    bool TryToggleMostRecentLampYaw();
    bool TryLogLampLayout();
}
