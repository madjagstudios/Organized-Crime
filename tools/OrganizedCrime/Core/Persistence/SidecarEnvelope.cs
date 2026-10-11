namespace OrganizedCrime.Core.Persistence;

/// <summary>
/// The shared sidecar envelope contract: one schema version plus a
/// service-typed payload. Each service keeps its own envelope record (its
/// payload member, codec wiring and golden tests freeze the shape) derived
/// from this base, so the version rides in one place.
/// <para>
/// The wire rules stay per service, unchanged: serializing requires the
/// service's current schema version, and deserializing an older supported
/// version normalizes the envelope to current. Codecs serialize DTOs, never
/// these records, so deriving here cannot change a single byte.
/// </para>
/// </summary>
/// <param name="SchemaVersion">The envelope schema version.</param>
public abstract record SidecarEnvelope(int SchemaVersion);
