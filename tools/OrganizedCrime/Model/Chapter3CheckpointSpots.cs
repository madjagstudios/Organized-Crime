namespace OrganizedCrime.Model;

/// <summary>
/// The owner-picked OC checkpoint spots. Positions are world coordinates at
/// eye height; yaw faces along the road. To extend the list, append one line
/// in the same shape (the `oc_ch3 checkpoint spot here` QA prints it).
/// </summary>
public static class Chapter3CheckpointSpots
{
    // OC-141 owner run 8 (2026-10-08, 0.4.7f12): the owner re-placed every spot in game and
    // replaced the earlier set. The sweatshop spot is gone; the HQ spot covers that stretch.
    // The Fish Warehouse needs none: the native Docks checkpoint already sits right outside it.
    public static IReadOnlyList<Chapter3CheckpointSpot> Baked { get; } = new[]
    {
        new Chapter3CheckpointSpot("outside-bungalow", -154.36f, -2.25f, 102.05f, 1.87f),
        new Chapter3CheckpointSpot("outside-storage-unit", -18.29f, 1.75f, 81.79f, -174.57f),
        new Chapter3CheckpointSpot("outside-barn", 115.56f, 1.75f, 10.79f, 88.24f),
        new Chapter3CheckpointSpot("outside-manor", 125.69f, 5.75f, -104.89f, 91.21f),
        new Chapter3CheckpointSpot("outside-hq", -38.57f, -2.25f, 148.59f, 2.03f),
    };
}
