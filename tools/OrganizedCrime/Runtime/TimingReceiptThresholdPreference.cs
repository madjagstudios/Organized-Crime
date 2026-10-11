using MelonLoader;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Reads the timing receipt threshold from MelonPreferences. Category <c>OrganizedCrime</c>,
/// entry <c>TimingReceiptThresholdMs</c>, default <c>50</c>. A phase that took at least this many
/// milliseconds reports one receipt line; zero or less disables the seam entirely.
/// OC-84: receipts print only with OwnerQaKeys diagnostics enabled, even if an
/// existing saved threshold is positive. The stored threshold is never rewritten.
/// MelonLoader-dependent, so this file is not linked into the test project.
/// </summary>
public static class TimingReceiptThresholdPreference
{
    private const string CategoryIdentifier = "OrganizedCrime";
    private const string EntryIdentifier = "TimingReceiptThresholdMs";

    public static int Read()
    {
        var category = MelonPreferences.GetCategory(CategoryIdentifier) ?? MelonPreferences.CreateCategory(CategoryIdentifier);
        var entry = category.GetEntry<int>(EntryIdentifier) ?? category.CreateEntry(EntryIdentifier, OrganizedCrimeTimingReceipts.DefaultThresholdMs);
        return entry.Value;
    }
}
