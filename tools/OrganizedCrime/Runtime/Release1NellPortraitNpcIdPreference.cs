using MelonLoader;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Reads the Nell portrait NPC id preference from MelonPreferences. Category <c>OrganizedCrime</c>,
/// entry <c>NellPortraitNpcId</c>, default the empty string. Empty means the fixed default
/// (<see cref="Release1NellNpc.DefaultPortraitNpcId"/>, <c>lily_turner</c>) is used; a non-empty value
/// overrides it, naming a vanilla NPC id whose icon <see cref="Release1NellNpc"/> tries to borrow (see
/// its constructor). No art ships in this ticket: the value is opt-in and owner-set. MelonLoader-dependent,
/// so this file is not linked into the test project.
/// </summary>
public static class Release1NellPortraitNpcIdPreference
{
    private const string CategoryIdentifier = "OrganizedCrime";
    private const string EntryIdentifier = "NellPortraitNpcId";
    private const string DefaultValue = "";

    public static string Read()
    {
        var category = MelonPreferences.GetCategory(CategoryIdentifier) ?? MelonPreferences.CreateCategory(CategoryIdentifier);
        var entry = category.GetEntry<string>(EntryIdentifier) ?? category.CreateEntry(EntryIdentifier, DefaultValue);
        return entry.Value ?? DefaultValue;
    }
}
