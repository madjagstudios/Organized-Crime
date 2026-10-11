namespace OrganizedCrime.Runtime.Police;

// Engine-free identity selection, with liveness supplied by the Unity adapter.
internal static class OcOfficerStaleAttachments
{
    internal static IReadOnlyList<T> Select<T>(
        IEnumerable<T> attachments,
        Func<T, bool> isOwned,
        Func<T, bool> isAlive,
        Func<T, bool> hasLiveParent) =>
        attachments.Where(a => isOwned(a) && isAlive(a) && !hasLiveParent(a)).ToArray();
}

// Optional internal diagnostics: no change to the public officer-world contract.
internal interface IOcOfficerAttachmentDiagnostics
{
    OcOfficerWorldStatus TryReadStaleAttachmentCount(OcOfficerCloneRef clone, out int count);
}

internal static class OcOfficerAttachmentStatus
{
    internal static string Read(IOcOfficerAttachmentDiagnostics world, OcOfficerCloneRef clone)
    {
        try
        {
            return world.TryReadStaleAttachmentCount(clone, out var count) == OcOfficerWorldStatus.Ok && count >= 0
                ? $"stale attachments: {count}"
                : "stale attachments: unknown";
        }
        catch
        {
            return "stale attachments: unknown";
        }
    }
}
