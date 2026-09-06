using MelonLoader;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Reads the owner QA keys preference from MelonPreferences. Category <c>OrganizedCrime</c>,
/// entry <c>OwnerQaKeys</c>, default <c>false</c>. When true, the owner's developer key
/// handlers (Backslash, O, P, and F8) are live regardless of build configuration.
/// MelonLoader-dependent, so this file is not linked into the test project.
/// </summary>
public static class OwnerQaKeysPreference
{
    private const string CategoryIdentifier = "OrganizedCrime";
    private const string EntryIdentifier = "OwnerQaKeys";
    private const bool DefaultValue = false;

    public static bool Read()
    {
        var category = MelonPreferences.GetCategory(CategoryIdentifier) ?? MelonPreferences.CreateCategory(CategoryIdentifier);
        var entry = category.GetEntry<bool>(EntryIdentifier) ?? category.CreateEntry(EntryIdentifier, DefaultValue);
        return entry.Value;
    }
}
