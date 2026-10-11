namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-141. The native checkpoint bind order behind
/// <c>Il2CppOcOfficerWorld.TryBindToCheckpoint</c>: the OC checkpoint must be
/// ready, then the clone's copied behaviours go off, then the clone is
/// mapped, then <c>SetCheckpoint</c> points it at the fixed OC location, and
/// only then is the CheckpointBehaviour enabled, so the patched Activate
/// resolves our checkpoint and registers the officer itself. The native calls
/// arrive as delegates, so tests drive the real order with recording fakes;
/// the steps run straight through and the caller owns the throw policy.
/// <c>TryUnbind</c> is the removal mirror: disable the CheckpointBehaviour
/// first, so the native Deactivate removes the clone from our checkpoint's
/// <c>AssignedNPCs</c> and its walk-through listener while both are alive,
/// then unmap the clone. The disable is fail-open; the unmap always runs.
/// </summary>
internal static class OcCheckpointBindSequence
{
    internal static OcOfficerWorldStatus TryBind(
        Func<bool> checkpointReady,
        Func<string> disableCopied,
        Action mapClone,
        Action setCheckpoint,
        Func<bool> enable,
        out string detail)
    {
        detail = "checkpoint not ready; bind aborted";
        if (!checkpointReady())
            return OcOfficerWorldStatus.Failed;
        var disabledNote = disableCopied() ?? "copied behaviours off";
        mapClone();
        setCheckpoint();
        var enabled = enable();
        detail = enabled
            ? $"bound ({disabledNote}; clone mapped; checkpoint set; enabled)"
            : $"bind failed ({disabledNote}; clone mapped; checkpoint set; not enabled)";
        return enabled ? OcOfficerWorldStatus.Ok : OcOfficerWorldStatus.Failed;
    }

    internal static string TryUnbind(long npcPointer, Action disableCheckpoint)
    {
        string disabledNote;
        try
        {
            disableCheckpoint();
            disabledNote = "checkpoint disabled";
        }
        catch (Exception exception)
        {
            disabledNote = $"checkpoint disable failed ({exception.GetType().Name})";
        }
        OcCheckpointSwapState.UnmapClone(npcPointer);
        return $"{disabledNote}; swap entry cleared";
    }
}
