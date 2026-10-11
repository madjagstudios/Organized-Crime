using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ArthurHqPresenceTests
{
    [Fact]
    public void Spot_is_beside_his_desk_facing_the_door()
    {
        Assert.Equal((-2.9f, 0f, 3.7f), Chapter3ArthurHqSpot.Local);
        Assert.Equal(180f, Chapter3ArthurHqSpot.YawDegrees);
        // Inside the interior frame: x -7..7, z -5..5; west side,
        // towards the back wall.
        Assert.InRange(
            Chapter3ArthurHqSpot.Local.X,
            -SyndicateHqInteriorDefinition.Width / 2f,
            SyndicateHqInteriorDefinition.Width / 2f);
        Assert.InRange(
            Chapter3ArthurHqSpot.Local.Z,
            -SyndicateHqInteriorDefinition.Depth / 2f,
            SyndicateHqInteriorDefinition.Depth / 2f);
        Assert.True(Chapter3ArthurHqSpot.Local is { X: < 0f, Z: > 0f });
    }

    [Fact]
    public void Spot_clears_every_floor_prop_and_the_walls()
    {
        var spot = Chapter3ArthurHqSpot.Local;
        // Every furniture position from the definition: the nearest
        // centre is his own armchair at 1.25 m, so 1.2 m pins the
        // spot out of every footprint. Whisky/radio decor excluded:
        // small items riding on desks, handled by their furniture.
        var nearest = SyndicateHqInteriorDefinition.NativeProps
            .Where(prop => prop.Anchor == SyndicateHqNativePropAnchor.Floor)
            .Min(prop => Horizontal(prop.Position.X - spot.X, prop.Position.Z - spot.Z));
        Assert.True(nearest >= 1.2f, $"nearest floor prop centre is {nearest:0.00} m");
        var bossArmchair = SyndicateHqInteriorDefinition.NativeProps.Single(prop => prop.Name == "BossArmchairNative");
        Assert.True(
            Horizontal(bossArmchair.Position.X - spot.X, bossArmchair.Position.Z - spot.Z) >= 1.5f,
            "keeps 1.5 m from the boss armchair centre, over 0.8 m from its edge");
        // 1 m from the nearest wall face (north, inner face at 4.875).
        Assert.True(4.875f - spot.Z >= 1f);
    }

    [Fact]
    public void ToLocal_inverts_ToWorld_and_IsInsideFrame_matches_the_room()
    {
        var pocketRoot = (1050f, 2f, 1060f);
        var world = Chapter3ArthurHqSpot.ToWorld(pocketRoot, 0f);
        var local = Chapter3ArthurHqSpot.ToLocal(world, pocketRoot);

        Assert.Equal(Chapter3ArthurHqSpot.Local.X, local.X, precision: 4);
        Assert.Equal(0f, local.Y, precision: 4);
        Assert.Equal(Chapter3ArthurHqSpot.Local.Z, local.Z, precision: 4);
        Assert.True(Chapter3ArthurHqSpot.IsInsideFrame(local));
        Assert.True(Chapter3ArthurHqSpot.IsInsideFrame((-7f, 0f, -5f)));
        Assert.False(Chapter3ArthurHqSpot.IsInsideFrame((-7.1f, 0f, 0f)));
        Assert.False(Chapter3ArthurHqSpot.IsInsideFrame((0f, 0f, 5.1f)));
        Assert.False(Chapter3ArthurHqSpot.IsInsideFrame((1000f, 0f, 1000f)));
    }

    private static float Horizontal(float dx, float dz) => (float)Math.Sqrt(dx * dx + dz * dz);

    [Fact]
    public void Presence_adapter_reads_both_delegates()
    {
        var chapter = false;
        var inside = false;
        var presence = new Chapter3SyndicateHqPresence(() => chapter, () => inside);

        Assert.False(presence.ChapterActive);
        Assert.False(presence.PlayerInsideHq);

        chapter = true;
        inside = true;

        Assert.True(presence.ChapterActive);
        Assert.True(presence.PlayerInsideHq);
    }

    [Fact]
    public void Presence_adapter_throws_read_as_not_present()
    {
        var presence = new Chapter3SyndicateHqPresence(
            () => throw new InvalidOperationException("chapter"),
            () => throw new InvalidOperationException("inside"));

        Assert.False(presence.ChapterActive);
        Assert.False(presence.PlayerInsideHq);
    }
}
