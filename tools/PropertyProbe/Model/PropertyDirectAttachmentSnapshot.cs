namespace OrganizedCrime.PropertyProbe.Model;

public sealed record PropertyDirectAttachmentSnapshot(
    string TargetName,
    string TargetPath,
    bool RegistrationPassed,
    bool AttachmentAttempted,
    bool AttachmentPassed,
    bool TargetLocalNetworkObjectPresentBefore,
    bool TargetLocalNetworkObjectPresentAfter,
    bool PropertyNetworkObjectResolved,
    bool PropertyNetworked,
    bool PropertyNetworkInitialized,
    bool PropertyClientInitialized,
    bool PropertyServerInitialized,
    bool PropertySpawned,
    bool TargetRegistered,
    bool TargetInUnownedCollection,
    int ParentNetworkBehaviourCountBefore,
    int ParentNetworkBehaviourCountAfter,
    int ParentNetworkBehaviourCountAfterCleanup,
    NetworkObjectMetadata ParentMetadata,
    NetworkObjectMetadata DocksMetadataBefore,
    NetworkObjectMetadata DocksMetadataAfter,
    bool SpawnAttempted,
    bool OwnershipAttempted,
    bool PersistenceAttempted,
    bool CleanupAttempted,
    bool CleanupPassed,
    string Gate,
    string? FailureReason)
{
    public bool DocksNetworkIdentityUnchanged => DocksMetadataBefore == DocksMetadataAfter;

    public static PropertyDirectAttachmentSnapshot Test(
        bool attachmentPassed = false,
        bool propertyNetworkObjectResolved = false,
        bool propertySpawned = false,
        bool cleanupPassed = false)
    {
        var parent = NetworkObjectMetadata.Empty with
        {
            Present = true,
            Networked = true,
            SceneObject = true,
            State = "Spawned",
            ObjectId = 36153,
            SceneId = 1693455146337029371,
            NetworkManagerPresent = true,
            ServerManagerPresent = true
        };

        return new PropertyDirectAttachmentSnapshot(
            TargetName: "Fish Warehouse",
            TargetPath: "Map/Hyland Point/Region_Docks/Fish Warehouse/fishwarehouse",
            RegistrationPassed: true,
            AttachmentAttempted: true,
            AttachmentPassed: attachmentPassed,
            TargetLocalNetworkObjectPresentBefore: false,
            TargetLocalNetworkObjectPresentAfter: false,
            PropertyNetworkObjectResolved: propertyNetworkObjectResolved,
            PropertyNetworked: propertyNetworkObjectResolved,
            PropertyNetworkInitialized: propertyNetworkObjectResolved,
            PropertyClientInitialized: false,
            PropertyServerInitialized: false,
            PropertySpawned: propertySpawned,
            TargetRegistered: attachmentPassed,
            TargetInUnownedCollection: attachmentPassed,
            ParentNetworkBehaviourCountBefore: 0,
            ParentNetworkBehaviourCountAfter: 1,
            ParentNetworkBehaviourCountAfterCleanup: 0,
            ParentMetadata: parent,
            DocksMetadataBefore: parent with { ObjectId = 10938, SceneId = 1693455143299770325 },
            DocksMetadataAfter: parent with { ObjectId = 10938, SceneId = 1693455143299770325 },
            SpawnAttempted: false,
            OwnershipAttempted: false,
            PersistenceAttempted: false,
            CleanupAttempted: cleanupPassed,
            CleanupPassed: cleanupPassed,
            Gate: attachmentPassed ? "direct-attachment" : "attachment-failed",
            FailureReason: null);
    }
}
