using S1API.Quests;
using S1API.Quests.Constants;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// The Small Courtesy quest rendered through S1API. The installed S1API build does carry a naming
/// attribute for quest classes (verified against the referenced assembly; see task-5-report.md),
/// but this quest is still located through <c>QuestManager.GetQuestByName</c>, which matches the
/// quest's (get-only, abstract) <see cref="Title"/>, because that lookup is the proven path;
/// <see cref="DisplayTitle"/> doubles as both the player-facing title and the S1API lookup key
/// for this quest.
/// </summary>
public sealed class Release1SmallCourtesyQuest : Quest
{
    public const string DisplayTitle = "Small Courtesy";

    private readonly QuestEntry[] _entries;

    public Release1SmallCourtesyQuest()
    {
        var placeholder = Release1PlayerCopy.Normalize(DisplayTitle);
        _entries = new[]
        {
            AddEntry(placeholder, (Vector3?)null),
            AddEntry(placeholder, (Vector3?)null)
        };
    }

    protected override string Title => Release1PlayerCopy.Normalize(DisplayTitle);

    protected override string Description => Release1PlayerCopy.Normalize(DisplayTitle);

    /// <summary>
    /// Projects a desired plan onto the tracked entries: title, POI, and Begin/Complete/SetState.
    /// The desired entry count must match the quest's native entry count exactly (fixed at
    /// construction); a mismatch throws rather than silently dropping extra entries or leaving
    /// missing ones at their placeholder state, so the boundary's catch-all maps it to a logged,
    /// reported failure instead of a silent partial projection.
    /// </summary>
    public void Project(Release1DesiredQuest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);
        if (quest.Entries.Count != _entries.Length)
            throw new InvalidOperationException(
                $"Release 1 Small Courtesy quest desired entry count ({quest.Entries.Count}) does not match the quest's native entry count ({_entries.Length}).");

        for (var i = 0; i < _entries.Length; i++)
            ApplyEntry(_entries[i], quest.Entries[i]);
    }

    /// <summary>Reads the tracked entries' native states back into the plan's desired-state enum.</summary>
    public IReadOnlyList<Release1DesiredEntryState> ReadEntryStates() =>
        _entries.Select(entry => MapState(entry.State)).ToArray();

    private static void ApplyEntry(QuestEntry entry, Release1DesiredEntry desired)
    {
        entry.Title = Release1PlayerCopy.Normalize(desired.Text);
        entry.POIPosition = desired.Marker is { } marker
            ? new Vector3(marker.X, marker.Y, marker.Z)
            : Vector3.zero;

        switch (Release1NativePresentationSupport.ClassifyEntryAction(desired.State))
        {
            case Release1QuestEntryProjectionAction.Begin:
                entry.Begin();
                break;
            case Release1QuestEntryProjectionAction.Complete:
                entry.Complete();
                break;
            default:
                entry.SetState(QuestState.Inactive);
                break;
        }
    }

    private static Release1DesiredEntryState MapState(QuestState state) => state switch
    {
        QuestState.Completed => Release1DesiredEntryState.Complete,
        QuestState.Active => Release1DesiredEntryState.Active,
        _ => Release1DesiredEntryState.Inactive
    };
}
