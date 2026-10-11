using OrganizedCrime.Core.Persistence;
using OrganizedCrime.Model;

namespace OrganizedCrime.Persistence;

public sealed record Chapter3SaveEnvelope(int SchemaVersion, Chapter3State? State) : SidecarEnvelope(SchemaVersion)
{
    public static Chapter3SaveEnvelope CreateEmpty() => new(Chapter3SaveCodec.CurrentSchemaVersion, null);
    public bool Equals(Chapter3SaveEnvelope? other) => other is not null && SchemaVersion == other.SchemaVersion && State == other.State;
    public override int GetHashCode() => HashCode.Combine(SchemaVersion, State);
}
