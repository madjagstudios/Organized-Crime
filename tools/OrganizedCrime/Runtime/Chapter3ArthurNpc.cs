using S1API.Entities;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-130: Arthur Selby, the shipping NPC. A new S1API NPC subclass on
/// the parameterless <c>protected NPC()</c> constructor, the physical
/// path the OC-69 spike proved: the deprecated four-argument constructor
/// Nell uses is the non-physical one, and Nell's contact identity is
/// load bearing for four shipped missions, so it is not reused.
/// Persistent by design: S1API writes custom NPCs to the save's
/// <c>NPCs.json</c> with no opt-out, and a Chapter 3 Arthur who
/// survives reloads is the point of this ticket.
/// </summary>
public sealed class Chapter3ArthurNpc : NPC
{
    /// <summary>
    /// The installed build declares <c>IsPhysical</c> as
    /// <c>public virtual bool</c> resolved per type through an internal
    /// role declaration resolver, so overriding it is the one public
    /// way to declare a subclass physical.
    /// </summary>
    public override bool IsPhysical => true;

    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
    {
        if (builder is null) return;
        builder.WithIdentity(Chapter3ArthurIds.NpcId, Chapter3ArthurIds.FirstName, Chapter3ArthurIds.LastName);
        // The full look lives in Chapter3ArthurAppearance and is defined
        // here, at prefab time: S1API refuses to spawn a physical custom
        // NPC whose Avatar is not active, and a post-spawn clone comes
        // too late because the NPC never spawns.
        Chapter3ArthurAppearance.ApplyTo(builder);
    }
}
