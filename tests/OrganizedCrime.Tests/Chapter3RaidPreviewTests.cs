using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3RaidPreviewTests
{
    private sealed class World : IChapter3RaidWorld
    {
        private bool _host = true;
        public bool IsHost { get => HostThrows ? throw new InvalidOperationException() : _host; set => _host = value; }
        internal bool HostThrows;
        internal bool InvalidStock;
        internal bool ResetThrows;
        internal bool AmbiguousSites;
        internal Chapter3RaidSite? Site;
        public void Reset() { if (ResetThrows) throw new InvalidOperationException(); }
        internal int Reads;
        internal bool Throws;
        internal Chapter3RaidPresence Presence = Chapter3RaidPresence.Clear;
        public IReadOnlyList<Chapter3RaidSite> Read(Chapter3RaidExclusions exclusions)
        {
            Reads++;
            if (Throws) throw new InvalidOperationException();
            var site = Site ?? new Chapter3RaidSite("a", "Property", true,
                new[] { new Chapter3RaidSlot(new(InvalidStock ? Guid.Empty : Guid.Parse("00000001-0000-0000-0000-000000000000"), 2), "fp", "ogkush", "baggie", 12, 1) }, Presence);
            return AmbiguousSites ? new[] { site, new("A", "Other", true, site.Slots, Presence) } : new[] { site };
        }
    }

    private sealed class CountingSlots : IReadOnlyList<Chapter3RaidSlot>
    {
        private readonly Chapter3RaidSlot _slot = new(new(Guid.Parse("00000001-0000-0000-0000-000000000000"), 2),
            "fp", "ogkush", "baggie", 12, 1);
        internal int Enumerations;
        public int Count => 1;
        public Chapter3RaidSlot this[int index] => index == 0 ? _slot : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<Chapter3RaidSlot> GetEnumerator() { Enumerations++; yield return _slot; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void Preview_reuses_site_total_for_display_selection_and_plan()
    {
        var slots = new CountingSlots();
        var site = new Chapter3RaidSite("a", "Property", true, slots, Chapter3RaidPresence.Clear);
        Assert.Equal(1, slots.Enumerations);
        var world = new World { Site = site };
        var preview = new Chapter3RaidPreview(world, () => 100, () => null);
        var lines = preview.Preview(null);
        Assert.Contains(lines, line => line.Contains("eligible units=12"));
        Assert.Contains(lines, line => line.Contains("total units=12; budget=3; planned units=3"));
        // One total traversal at capture and one sort traversal when planning.
        Assert.Equal(2, slots.Enumerations);
        preview.Preview("a");
        Assert.Equal(3, slots.Enumerations);
    }

    [Fact]
    public void Exception_during_site_selection_is_contained_and_resets_absence()
    {
        var world = new World();
        double now = 100;
        var preview = new Chapter3RaidPreview(world, () => now, () => null);
        for (; now <= 110; now++) preview.Poll();
        now = 110;
        world.AmbiguousSites = true;
        Assert.Contains("unknown, blocked", preview.Preview("a").Single());
        world.AmbiguousSites = false;
        Assert.Contains(preview.Preview("a"), line => line.Contains("away minutes=0"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Host_read_or_invalid_stock_cannot_escape_preview(bool hostThrows)
    {
        var world = new World { HostThrows = hostThrows, InvalidStock = !hostThrows, ResetThrows = true };
        var preview = new Chapter3RaidPreview(world, () => 100, () => null);
        var lines = preview.Preview(null);
        Assert.Contains(lines, line => line.Contains("unknown, blocked"));
    }

    [Fact]
    public void Client_refusal_contains_world_reset_failure()
    {
        var world = new World { IsHost = false, ResetThrows = true };
        var preview = new Chapter3RaidPreview(world, () => 100, () => null);
        Assert.Contains("host only", preview.Preview(null).Single());
    }

    [Fact]
    public void Client_preview_and_poll_do_not_read_world_or_clock()
    {
        var world = new World { IsHost = false };
        var preview = new Chapter3RaidPreview(world, () => throw new InvalidOperationException(), () => null);
        preview.Poll();
        Assert.Contains("host only", preview.Preview(null).Single());
        Assert.Equal(0, world.Reads);
    }

    [Fact]
    public void Preview_prints_complete_plan_and_never_changes_frozen_stock()
    {
        var world = new World();
        var preview = new Chapter3RaidPreview(world, () => 100, () => null);
        var lines = preview.Preview(null);
        Assert.Contains(lines, s => s.Contains("eligible units=12"));
        Assert.Contains(lines, s => s.Contains("picked=a"));
        Assert.Contains(lines, s => s.Contains("packages=3") && s.Contains("pre=12") && s.Contains("fingerprint=fp"));
        Assert.Contains(lines, s => s.Contains("planned units=3"));
        Assert.Equal(12, world.Read(new()).Single().Slots.Single().Quantity);
        Assert.Contains("unknown site", preview.Preview("missing").Last());
    }

    [Fact]
    public void Poll_tracks_continuity_and_failure_or_lifecycle_resets_it()
    {
        var world = new World();
        double now = 100;
        var preview = new Chapter3RaidPreview(world, () => now, () => null);
        for (; now <= 110; now++) preview.Poll();
        now = 110;
        Assert.Contains(preview.Preview("a"), s => s.Contains("clear=True"));
        world.Throws = true;
        preview.Poll();
        world.Throws = false;
        Assert.Contains(preview.Preview("a"), s => s.Contains("away minutes=0"));
        preview.Reset();
        Assert.Contains(preview.Preview("a"), s => s.Contains("clear=False"));
    }
}
