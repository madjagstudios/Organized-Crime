using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.PropertyProbe.Model;

namespace OrganizedCrime.PropertyProbe.Reporting;

public static class PropertyDirectAttachmentFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FormatText(PropertyDirectAttachmentSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FISH WAREHOUSE DIRECT PROPERTY ATTACHMENT");
        builder.AppendLine("Temporary component only: no spawn, ownership, persistence, or existing-property mutation was attempted.");
        builder.AppendLine();
        builder.AppendLine($"TARGET_NAME: {snapshot.TargetName}");
        builder.AppendLine($"TARGET_PATH: {snapshot.TargetPath}");
        builder.AppendLine($"REGISTRATION_PASSED: {snapshot.RegistrationPassed}");
        builder.AppendLine($"ATTACHMENT_ATTEMPTED: {snapshot.AttachmentAttempted}");
        builder.AppendLine($"ATTACHMENT_PASSED: {snapshot.AttachmentPassed}");
        builder.AppendLine($"TARGET_LOCAL_NETWORK_OBJECT_PRESENT_BEFORE: {snapshot.TargetLocalNetworkObjectPresentBefore}");
        builder.AppendLine($"TARGET_LOCAL_NETWORK_OBJECT_PRESENT_AFTER: {snapshot.TargetLocalNetworkObjectPresentAfter}");
        builder.AppendLine($"PROPERTY_NETWORK_OBJECT_RESOLVED: {snapshot.PropertyNetworkObjectResolved}");
        builder.AppendLine($"PROPERTY_IS_NETWORKED: {snapshot.PropertyNetworked}");
        builder.AppendLine($"PROPERTY_NETWORK_INITIALIZED: {snapshot.PropertyNetworkInitialized}");
        builder.AppendLine($"PROPERTY_CLIENT_INITIALIZED: {snapshot.PropertyClientInitialized}");
        builder.AppendLine($"PROPERTY_SERVER_INITIALIZED: {snapshot.PropertyServerInitialized}");
        builder.AppendLine($"PROPERTY_SPAWNED: {snapshot.PropertySpawned}");
        builder.AppendLine($"TARGET_REGISTERED: {snapshot.TargetRegistered}");
        builder.AppendLine($"TARGET_IN_UNOWNED_COLLECTION: {snapshot.TargetInUnownedCollection}");
        builder.AppendLine($"PARENT_NETWORK_BEHAVIOUR_COUNT_BEFORE: {snapshot.ParentNetworkBehaviourCountBefore}");
        builder.AppendLine($"PARENT_NETWORK_BEHAVIOUR_COUNT_AFTER: {snapshot.ParentNetworkBehaviourCountAfter}");
        builder.AppendLine($"PARENT_NETWORK_BEHAVIOUR_COUNT_AFTER_CLEANUP: {snapshot.ParentNetworkBehaviourCountAfterCleanup}");
        builder.AppendLine($"PARENT_OBJECT_ID: {snapshot.ParentMetadata.ObjectId}");
        builder.AppendLine($"PARENT_SCENE_ID: {snapshot.ParentMetadata.SceneId}");
        builder.AppendLine($"PARENT_IS_SCENE_OBJECT: {snapshot.ParentMetadata.SceneObject}");
        builder.AppendLine($"PARENT_STATE: {snapshot.ParentMetadata.State}");
        builder.AppendLine($"DOCKS_NETWORK_IDENTITY_UNCHANGED: {snapshot.DocksNetworkIdentityUnchanged}");
        builder.AppendLine($"SPAWN_ATTEMPTED: {snapshot.SpawnAttempted}");
        builder.AppendLine($"OWNERSHIP_ATTEMPTED: {snapshot.OwnershipAttempted}");
        builder.AppendLine($"PERSISTENCE_ATTEMPTED: {snapshot.PersistenceAttempted}");
        builder.AppendLine($"CLEANUP_ATTEMPTED: {snapshot.CleanupAttempted}");
        builder.AppendLine($"CLEANUP_PASSED: {snapshot.CleanupPassed}");
        builder.AppendLine($"GATE: {snapshot.Gate}");
        builder.AppendLine($"FAILURE_REASON: {snapshot.FailureReason ?? "n/a"}");
        return builder.ToString();
    }

    public static string FormatJson(PropertyDirectAttachmentSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
