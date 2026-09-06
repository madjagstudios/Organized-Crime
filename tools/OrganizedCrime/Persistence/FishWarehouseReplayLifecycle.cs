namespace OrganizedCrime.Persistence;

public sealed record FishWarehouseCaptureRecoveryPrerequisites(
    bool ByteProtectionSucceeded,
    bool InspectionSucceeded,
    bool TypedPayloadRetentionSucceeded,
    bool CheckpointPersistenceSucceeded)
{
    public bool AllSucceeded =>
        ByteProtectionSucceeded &&
        InspectionSucceeded &&
        TypedPayloadRetentionSucceeded &&
        CheckpointPersistenceSucceeded;
}

public sealed class FishWarehouseReplayLifecycle
{
    private readonly Func<FishWarehouseReplayCheckpoint, FishWarehouseCaptureRecoveryPrerequisites> _captureRecoveryPrerequisites;

    public FishWarehouseReplayLifecycle(
        Func<FishWarehouseReplayCheckpoint, FishWarehouseCaptureRecoveryPrerequisites>? captureRecoveryPrerequisites = null)
    {
        _captureRecoveryPrerequisites = captureRecoveryPrerequisites ?? (_ => new FishWarehouseCaptureRecoveryPrerequisites(
            ByteProtectionSucceeded: false,
            InspectionSucceeded: false,
            TypedPayloadRetentionSucceeded: false,
            CheckpointPersistenceSucceeded: false));
    }

    public FishWarehouseReplayCheckpoint? CurrentCheckpoint { get; private set; }

    public string? LastRecoveredFailureReason { get; private set; }

    public bool RequiresSnapshotProtection => CurrentCheckpoint is { Phase: >= FishWarehouseReplayPhase.Captured and <= FishWarehouseReplayPhase.DeliveriesReleased } ||
        CurrentCheckpoint?.Phase == FishWarehouseReplayPhase.Failed;

    public bool Begin(FishWarehouseReplayCheckpoint checkpoint, out string? failureReason)
    {
        failureReason = null;
        if (!IsReplayCheckpoint(checkpoint))
        {
            failureReason = "Fish Warehouse replay checkpoint was invalid.";
            return false;
        }

        if (CurrentCheckpoint is null || CurrentCheckpoint.Phase == FishWarehouseReplayPhase.Complete)
        {
            CurrentCheckpoint = checkpoint;
            return true;
        }

        if (!string.Equals(CurrentCheckpoint.SaveFolderKey, checkpoint.SaveFolderKey, StringComparison.Ordinal))
        {
            failureReason = "Fish Warehouse replay checkpoint belongs to a different save folder.";
            return false;
        }

        if (!string.Equals(CurrentCheckpoint.GenerationId, checkpoint.GenerationId, StringComparison.Ordinal))
        {
            failureReason = "Fish Warehouse replay generation is already active.";
            return false;
        }

        if (CurrentCheckpoint == checkpoint)
            return true;

        failureReason = "Fish Warehouse replay generation is already active.";
        return false;
    }

    public bool TryAdvance(FishWarehouseReplayPhase phase, out string? failureReason)
    {
        failureReason = null;
        if (CurrentCheckpoint is null)
        {
            failureReason = "Fish Warehouse replay has not started.";
            return false;
        }

        if (CurrentCheckpoint.Phase == FishWarehouseReplayPhase.Failed)
        {
            failureReason = "Fish Warehouse replay has failed and is terminal.";
            return false;
        }

        if (CurrentCheckpoint.Phase == phase)
            return true;

        var expected = GetNextPhase(CurrentCheckpoint.Phase);
        if (expected != phase)
        {
            failureReason = phase == FishWarehouseReplayPhase.Captured
                ? $"Fish Warehouse replay is already at {CurrentCheckpoint.Phase}."
                : $"Fish Warehouse replay cannot advance to {phase}; expected {expected?.ToString() ?? "no later phase"}.";
            return false;
        }

        CurrentCheckpoint = CurrentCheckpoint with { Phase = phase, FailurePhase = null, FailureReason = null };
        return true;
    }

    public bool RecordInventoryRestoredEmployeeGuids(
        IReadOnlyCollection<string> employeeGuids,
        out string? failureReason)
    {
        failureReason = null;
        if (CurrentCheckpoint is null)
        {
            failureReason = "Fish Warehouse replay has not started.";
            return false;
        }

        if (CurrentCheckpoint.Phase == FishWarehouseReplayPhase.Failed)
        {
            failureReason = "Fish Warehouse replay has failed and cannot record inventory restoration.";
            return false;
        }

        var merged = (CurrentCheckpoint.InventoryRestoredEmployeeGuids ?? Array.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var employeeGuid in employeeGuids ?? Array.Empty<string>())
        {
            if (!System.Guid.TryParse(employeeGuid, out var parsedGuid))
            {
                failureReason = $"Fish Warehouse inventory restoration marker contained a malformed employee GUID: {employeeGuid}";
                return false;
            }

            merged.Add(parsedGuid.ToString("D"));
        }

        CurrentCheckpoint = CurrentCheckpoint with
        {
            InventoryRestoredEmployeeGuids = merged.OrderBy(guid => guid, StringComparer.Ordinal).ToArray()
        };
        return true;
    }

    public void Fail(FishWarehouseReplayPhase phase, string reason)
    {
        if (CurrentCheckpoint is null || CurrentCheckpoint.Phase == FishWarehouseReplayPhase.Failed)
            return;

        CurrentCheckpoint = CurrentCheckpoint with
        {
            Phase = FishWarehouseReplayPhase.Failed,
            FailurePhase = CurrentCheckpoint.Phase.ToString(),
            FailureReason = phase == CurrentCheckpoint.Phase
                ? reason
                : $"{reason} (reported phase {phase} did not match active phase {CurrentCheckpoint.Phase})"
        };
    }

    public bool TryRecoverCaptureFailure(
        FishWarehouseReplayCheckpoint recoveredCapturedCheckpoint,
        out string? failureReason)
    {
        failureReason = null;
        if (CurrentCheckpoint is not { Phase: FishWarehouseReplayPhase.Failed })
        {
            failureReason = "Fish Warehouse replay failure is terminal.";
            return false;
        }

        if (!IsReplayCheckpoint(recoveredCapturedCheckpoint) || recoveredCapturedCheckpoint.Phase != FishWarehouseReplayPhase.Captured)
        {
            failureReason = "Fish Warehouse recovered checkpoint must be fully persisted at Captured.";
            return false;
        }

        if (!string.Equals(CurrentCheckpoint.GenerationId, recoveredCapturedCheckpoint.GenerationId, StringComparison.Ordinal))
        {
            failureReason = "Fish Warehouse recovered checkpoint used a different generation.";
            return false;
        }

        if (!string.Equals(CurrentCheckpoint.SaveFolderKey, recoveredCapturedCheckpoint.SaveFolderKey, StringComparison.Ordinal))
        {
            failureReason = "Fish Warehouse recovered checkpoint belongs to a different save folder.";
            return false;
        }

        if (!_captureRecoveryPrerequisites(recoveredCapturedCheckpoint).AllSucceeded)
        {
            failureReason = "Fish Warehouse capture recovery prerequisites were not all verified.";
            return false;
        }

        LastRecoveredFailureReason = CurrentCheckpoint.FailureReason;
        CurrentCheckpoint = recoveredCapturedCheckpoint;
        return true;
    }

    public void ResetRuntimeReferences()
    {
        // The checkpoint is serialized state and deliberately survives runtime reference resets.
    }

    public void ResetForNewCaptureGeneration()
    {
        CurrentCheckpoint = null;
    }

    private static bool IsReplayCheckpoint(FishWarehouseReplayCheckpoint? checkpoint) =>
        checkpoint is not null &&
        !string.IsNullOrWhiteSpace(checkpoint.GenerationId) &&
        !string.IsNullOrWhiteSpace(checkpoint.SaveFolderKey) &&
        (checkpoint.Phase is >= FishWarehouseReplayPhase.Captured and <= FishWarehouseReplayPhase.Complete ||
         checkpoint.Phase == FishWarehouseReplayPhase.Failed) &&
        !string.IsNullOrWhiteSpace(checkpoint.SnapshotRelativePath) &&
        !string.IsNullOrWhiteSpace(checkpoint.SnapshotSha256);

    private static FishWarehouseReplayPhase? GetNextPhase(FishWarehouseReplayPhase phase) => phase switch
    {
        FishWarehouseReplayPhase.Captured => FishWarehouseReplayPhase.RuntimeReady,
        FishWarehouseReplayPhase.RuntimeReady => FishWarehouseReplayPhase.ObjectsReplayed,
        FishWarehouseReplayPhase.ObjectsReplayed => FishWarehouseReplayPhase.NavigationReady,
        FishWarehouseReplayPhase.NavigationReady => FishWarehouseReplayPhase.DeliveriesReleased,
        FishWarehouseReplayPhase.EmployeesReplayed => FishWarehouseReplayPhase.DeliveriesReleased,
        FishWarehouseReplayPhase.DeliveriesReleased => FishWarehouseReplayPhase.Complete,
        _ => null
    };
}
