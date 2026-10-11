namespace OrganizedCrime.Model;

/// <summary>
/// One OC checkpoint spot: a label plus the world position and the yaw
/// facing along the road. Spots are baked by hand; the owner picks new ones
/// in game with `oc_ch3 checkpoint spot here`, and they land in
/// <see cref="Chapter3CheckpointSpots.Baked"/>.
/// </summary>
public sealed record Chapter3CheckpointSpot(string Label, float X, float Y, float Z, float YawDegrees);
