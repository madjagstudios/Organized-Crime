#if OC_OWNER_SPIKES
using S1API.Entities;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-69 spike contact. A new S1API NPC subclass on the parameterless <c>protected NPC()</c>
/// constructor, the physical path in the installed S1API 3.2.0.0 build; the four argument constructor
/// Nell uses is the deprecated non physical one, and Nell herself is not reused because her contact
/// identity is load bearing for four shipped missions.
///
/// <see cref="ConfigurePrefab"/> deliberately never calls the builder's own per type spawn position
/// setter: that writes a per type static dictionary through the internal
/// <c>NPC.RegisterSpawnPositionForType</c>, driven with no instance in scope, so it cannot know where
/// the player is standing. Placement happens on the instance, through <c>NPC.Position</c>, in
/// <see cref="S1ApiRelease1FieldContactRuntime"/>, which is also the only thing that constructs this
/// type, and only from an owner QA key press.
/// </summary>
public sealed class Release1ArthurNpc : NPC
{
    /// <summary>
    /// The installed build declares <c>IsPhysical</c> as <c>public virtual bool</c> and resolves it
    /// per type through an internal role declaration resolver, so overriding it is the one public
    /// way to declare a subclass physical.
    /// </summary>
    public override bool IsPhysical => true;

    protected override void ConfigurePrefab(NPCPrefabBuilder builder)
    {
        if (builder is null) return;
        builder
            .WithIdentity(Release1ArthurFieldContactHarness.ContactId, "Arthur", "Selby")
            .WithAppearanceDefaults(avatar =>
            {
                avatar.Gender = 0f;
                avatar.Height = 1.05f;
                avatar.Weight = 0.55f;
            });
    }
}
#endif
