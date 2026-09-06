using S1API.Quests;
using S1API.Quests.Constants;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// The Short Notice quest rendered through S1API. Structurally identical to
/// <see cref="Release1WrongAddressQuest"/> and <see cref="Release1SmallCourtesyQuest"/>, except this
/// quest carries exactly one entry created at construction: leave the whole manifest at the drop.
/// Located through <c>QuestManager.GetQuestByName</c>, which matches the quest's (get-only, abstract)
/// <see cref="Title"/>; <see cref="DisplayTitle"/> doubles as both the player-facing title and the
/// S1API lookup key for this quest.
/// </summary>
public sealed class Release1ShortNoticeQuest : Quest
{
    public const string DisplayTitle = "Short Notice";

    private readonly QuestEntry[] _entries;

    public Release1ShortNoticeQuest()
    {
        var placeholder = Release1PlayerCopy.Normalize(DisplayTitle);
        _entries = new[] { AddEntry(placeholder, (Vector3?)null) };
    }

    protected override string Title => Release1PlayerCopy.Normalize(DisplayTitle);

    protected override string Description => Release1PlayerCopy.Normalize(DisplayTitle);

    /// <summary>
    /// Projects a desired plan onto the tracked entry: title, POI, and Begin/Complete/SetState. The
    /// desired entry count must match the quest's native entry count exactly (fixed at construction,
    /// one); a mismatch throws rather than silently dropping extra entries or leaving missing ones at
    /// their placeholder state, so the boundary's catch-all maps it to a logged, reported failure
    /// instead of a silent partial projection.
    /// </summary>
    public void Project(Release1DesiredQuest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);
        if (quest.Entries.Count != _entries.Length)
            throw new InvalidOperationException(
                $"Release 1 Short Notice quest desired entry count ({quest.Entries.Count}) does not match the quest's native entry count ({_entries.Length}).");

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
