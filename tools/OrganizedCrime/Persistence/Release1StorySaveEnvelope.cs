using OrganizedCrime.Model;

namespace OrganizedCrime.Persistence;

public sealed record Release1StorySaveEnvelope
{
    public Release1StorySaveEnvelope(int SchemaVersion, Release1StoryState? Story)
    {
        this.SchemaVersion = SchemaVersion;
        this.Story = Story;
    }
    public int SchemaVersion { get; init; }
    public Release1StoryState? Story { get; init; }
    public static Release1StorySaveEnvelope CreateEmpty() => new(Release1StorySaveCodec.CurrentSchemaVersion, null);
    public bool Equals(Release1StorySaveEnvelope? other) => other is not null && SchemaVersion == other.SchemaVersion && ((Story is null && other.Story is null) || (Story is not null && Story.ValueEquals(other.Story)));
    public override int GetHashCode() => HashCode.Combine(SchemaVersion, Story);
}
