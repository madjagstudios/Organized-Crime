using Il2CppScheduleOne.NPCs;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-56 owner QA harness, gate key Insert, OwnerQaKeys only. Read only. Lists every NPC in the
/// game's own registry, <see cref="NPCManager.NPCRegistry"/> (a plain static
/// <c>List&lt;NPC&gt;</c> the native NPC manager owns and appends to as NPCs are created), not
/// S1API's <c>NPC.All</c>. For each entry this reads only <see cref="NPC.ID"/>,
/// <see cref="NPC.FirstName"/>, <see cref="NPC.LastName"/>, and whether
/// <see cref="NPC.Avatar"/> carries avatar settings (<c>Avatar.CurrentSettings</c> non null); it
/// never resolves a behaviour, a schedule, or any other subsystem, and it never writes anything.
/// Every member here was verified by reflection against the referenced Il2Cpp assemblies.
/// </summary>
public static class Release1NpcRegistryDumpHarness
{
    public static Release1StagingHarnessResult TryDump()
    {
        var lines = new List<string>();
        try
        {
            var registry = NPCManager.NPCRegistry;
            if (registry is null)
            {
                lines.Add("the native NPC registry was not available.");
                return new(Release1StagingHarnessStatus.Unavailable, lines);
            }

            var count = registry.Count;
            lines.Add($"registry count {count}.");
            for (var i = 0; i < count; i++)
            {
                var npc = registry[i];
                if (npc is null) continue;
                string id;
                string firstName;
                string lastName;
                bool hasAvatarSettings;
                try
                {
                    id = npc.ID;
                    firstName = npc.FirstName;
                    lastName = npc.LastName;
                    hasAvatarSettings = npc.Avatar is not null && npc.Avatar.CurrentSettings is not null;
                }
                catch (Exception ex)
                {
                    lines.Add($"entry {i} could not be read: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                lines.Add($"id {id} firstName {firstName} lastName {lastName} hasAvatarSettings {hasAvatarSettings}");
            }

            return new(Release1StagingHarnessStatus.Succeeded, lines);
        }
        catch (Exception ex)
        {
            lines.Add("an exception was thrown while dumping the NPC registry: " + ex.Message);
            return new(Release1StagingHarnessStatus.Faulted, lines);
        }
    }
}
