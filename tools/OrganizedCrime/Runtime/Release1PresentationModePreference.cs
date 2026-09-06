using MelonLoader;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Reads the Release 1 presentation mode from MelonPreferences. Category <c>OrganizedCrime</c>,
/// entry <c>Release1PresentationMode</c>, default <c>Native</c>. Unknown or missing values
/// map to <see cref="Release1PresentationMode.Native"/> via <see cref="Release1PresentationModeParser"/>.
/// MelonLoader-dependent, so this file is not linked into the test project.
/// </summary>
public static class Release1PresentationModePreference
{
    private const string CategoryIdentifier = "OrganizedCrime";
    private const string EntryIdentifier = "Release1PresentationMode";
    private const string DefaultValue = "Native";

    public static Release1PresentationMode Read()
    {
        var category = MelonPreferences.GetCategory(CategoryIdentifier) ?? MelonPreferences.CreateCategory(CategoryIdentifier);
        var entry = category.GetEntry<string>(EntryIdentifier) ?? category.CreateEntry(EntryIdentifier, DefaultValue);
        return Release1PresentationModeParser.Parse(entry.Value);
    }
}

/// <summary>
/// Reads the Small Courtesy quest persistence policy from MelonPreferences. Category
/// <c>OrganizedCrime</c>, entry <c>Release1QuestPersistencePolicy</c>, default
/// <c>DisposablePerLoad</c>. Unknown or missing values map to
/// <see cref="Release1QuestPersistencePolicy.DisposablePerLoad"/> via
/// <see cref="Release1QuestPersistencePolicyParser"/>. MelonLoader-dependent, so this file is not
/// linked into the test project.
/// </summary>
public static class Release1QuestPersistencePolicyPreference
{
    private const string CategoryIdentifier = "OrganizedCrime";
    private const string EntryIdentifier = "Release1QuestPersistencePolicy";
    private const string DefaultValue = "DisposablePerLoad";

    public static Release1QuestPersistencePolicy Read()
    {
        var category = MelonPreferences.GetCategory(CategoryIdentifier) ?? MelonPreferences.CreateCategory(CategoryIdentifier);
        var entry = category.GetEntry<string>(EntryIdentifier) ?? category.CreateEntry(EntryIdentifier, DefaultValue);
        return Release1QuestPersistencePolicyParser.Parse(entry.Value);
    }
}

/// <summary>
/// Reads the Chief Campbell portrait NPC id preference from MelonPreferences. Category
/// <c>OrganizedCrime</c>, entry <c>ChiefPortraitNpcId</c>, default the empty string. Empty means the
/// fixed default (<see cref="Release1ChiefCampbellNpc"/>'s own <c>DefaultPortraitNpcId</c>,
/// <c>officerdavis</c>) is used; a non-empty value overrides it, naming a vanilla NPC id whose icon
/// <see cref="Release1ChiefCampbellNpc"/> tries to borrow (see its constructor). No art ships in this
/// ticket: the value is opt-in and owner-set. MelonLoader-dependent, so this file is not linked into
/// the test project.
/// </summary>
public static class Release1ChiefPortraitNpcIdPreference
{
    private const string CategoryIdentifier = "OrganizedCrime";
    private const string EntryIdentifier = "ChiefPortraitNpcId";
    private const string DefaultValue = "";

    public static string Read()
    {
        var category = MelonPreferences.GetCategory(CategoryIdentifier) ?? MelonPreferences.CreateCategory(CategoryIdentifier);
        var entry = category.GetEntry<string>(EntryIdentifier) ?? category.CreateEntry(EntryIdentifier, DefaultValue);
        return entry.Value ?? DefaultValue;
    }
}
