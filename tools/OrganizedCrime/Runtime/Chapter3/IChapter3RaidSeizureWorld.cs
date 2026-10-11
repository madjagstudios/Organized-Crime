using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Chapter3;

// PlanFingerprint corroborates D7a identity at the same live read. HQ planning
// includes value; mutation identity excludes value and quantity.
internal sealed record Chapter3RaidSlotRead(int Quantity, string? Fingerprint, string? PlanFingerprint = null);

// OC-183: host adapters supply native reads/writes, never managed API slots.
// Sync the actual native result so a partial write remains visible as divergence.
// Save enlistment also runs after faults; a receipt does not own native saving.
internal static class Chapter3RaidNativeQuantity
{
    internal static void Take(int packages, Func<int> nativeQuantity,
        Action<int, bool> changeQuantity, Action<int> synchronize, Action markChanged)
    {
        if (packages < 1 || nativeQuantity() < packages) return;
        markChanged();
        try
        {
            changeQuantity(-packages, true);
            synchronize(nativeQuantity());
        }
        finally { markChanged(); }
    }
}

internal interface IChapter3RaidHqSlotReader
{
    bool TryReadRaidSlot(string closetGuid, int slotIndex, out Chapter3RaidSlotRead read);
}

// Separate from D7a's read-only capability. Read must distinguish an empty
// restored slot from missing storage, unreadable stock or a replacement item.
internal interface IChapter3RaidSeizureWorld
{
    bool IsRestored(string siteCode);
    bool TryReadSlot(string siteCode, Chapter3RaidSlotIdentity identity, out Chapter3RaidSlotRead read);
    bool TryLock(string siteCode, Chapter3RaidSlotIdentity identity);
    void Unlock(string siteCode, Chapter3RaidSlotIdentity identity);
    void Take(string siteCode, Chapter3RaidSlotIdentity identity, int packages);
}

// Complete, unfiltered native storage read. Excluded stacks still participate
// in restacking and therefore in the quantity of this fingerprint.
internal interface IChapter3RaidStorageReader
{
    bool TryReadStorageQuantity(string siteCode, Guid storageGuid, string fingerprint, out long quantity);
}

internal static class Chapter3RaidStorageQuantity
{
    internal static bool TrySum(int slotCount, Func<int, Chapter3RaidSlotRead?> readSlot,
        string fingerprint, out long quantity)
    {
        quantity = 0;
        if (slotCount < 1 || string.IsNullOrWhiteSpace(fingerprint)) return false;
        try
        {
            long total = 0;
            for (var i = 0; i < slotCount; i++)
            {
                var read = readSlot(i);
                if (read is null || read.Quantity < 0 ||
                    (read.Quantity > 0 && string.IsNullOrWhiteSpace(read.Fingerprint))) return false;
                if (read.Fingerprint == fingerprint) total = checked(total + read.Quantity);
            }
            quantity = total;
            return true;
        }
        catch { return false; }
    }
}

internal sealed record Chapter3RaidInput(bool IsHost, double? Minutes,
    LocalPressureTier? Tier, bool? Protected, bool? ArrestTransition);

internal interface IChapter3RaidSeizureQaTarget
{
    Chapter3QaResult RaidNow(string? siteCode, bool forceAway);
    Chapter3QaResult RaidSkipCap();
    IReadOnlyList<string> RaidStatus();
}
