using OrganizedCrime.Model;
using S1API.Quests;
using S1API.Quests.Constants;
using UnityEngine;

namespace OrganizedCrime.Runtime;

/// <summary>
/// The one Chapter 3 quest, reused per job: one Active entry with its marker
/// on the frozen drop for a dead drop, two entries (pickup, handoff) for a
/// package recovery, plus OC-117's markerless expansion line. Job and
/// demand entries always carry their drop marker; a null marker clears
/// the POI for a journal-only line, never a zero POI that would pin the
/// world origin (the OC-93 lesson). S1API offers <c>AddEntry</c> but
/// no entry removal, so a quest created narrow grows here while a
/// re-adopted wider quest is ended and recreated by the presentation
/// layer instead of projected.
/// </summary>
public sealed class Chapter3JobQuest : Quest
{
    private readonly List<QuestEntry> _entries;

    public Chapter3JobQuest()
    {
        var placeholder = Release1PlayerCopy.Normalize(Chapter3QuestCopy.QuestTitle);
        _entries = new List<QuestEntry> { AddEntry(placeholder, (Vector3?)null) };
    }

    /// <summary>
    /// The native entry count, so the presentation layer can end and recreate
    /// this quest when the desired job type needs a different entry count.
    /// </summary>
    public int EntryCount => _entries.Count;

    protected override string Title => Release1PlayerCopy.Normalize(Chapter3QuestCopy.QuestTitle);

    protected override string Description => Release1PlayerCopy.Normalize(Chapter3QuestCopy.QuestDescription);

    public void Project(Release1DesiredQuest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);
        while (_entries.Count < quest.Entries.Count)
            _entries.Add(AddEntry(Release1PlayerCopy.Normalize(Chapter3QuestCopy.QuestTitle), (Vector3?)null));
        if (quest.Entries.Count != _entries.Count)
            throw new InvalidOperationException(
                $"Chapter 3 job quest desired entry count ({quest.Entries.Count}) does not match the quest's native entry count ({_entries.Count}).");

        for (var i = 0; i < _entries.Count; i++)
            ApplyEntry(_entries[i], quest.Entries[i]);
    }

    /// <summary>Reads the tracked entries' native states back into the plan's desired-state enum.</summary>
    public IReadOnlyList<Release1DesiredEntryState> ReadEntryStates() =>
        _entries.Select(entry => MapState(entry.State)).ToArray();

    private static void ApplyEntry(QuestEntry entry, Release1DesiredEntry desired)
    {
        entry.Title = Release1PlayerCopy.Normalize(desired.Text);
        if (desired.Marker is null)
            entry.ClearPOI();
        else
            entry.POIPosition = new Vector3(desired.Marker.X, desired.Marker.Y, desired.Marker.Z);

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
