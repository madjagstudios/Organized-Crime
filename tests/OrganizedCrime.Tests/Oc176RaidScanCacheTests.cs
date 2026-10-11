using OrganizedCrime.Model;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Oc176RaidScanCacheTests
{
    private readonly Guid _guid = Guid.Parse("00000001-0000-0000-0000-000000000000");
    private Chapter3RaidSite Site(int quantity = 12) => new("a", "Property", true,
        new[] { new Chapter3RaidSlot(new(_guid, 0), "fp", "ogkush", "baggie", quantity, 1) }, Chapter3RaidPresence.Clear);

    [Fact]
    public void Hourly_stock_cache_keeps_presence_and_job_exclusions_live()
    {
        var cache = new Chapter3RaidScanCache(); var scans = 0; var exclusions = new Chapter3RaidExclusions();
        Chapter3RaidSite Scan() { scans++; return Site(); }
        cache.Read("a", 1, 100, false, Scan, Chapter3RaidPresence.Clear, exclusions);
        exclusions.Storage.Add(_guid);
        var site = cache.Read("a", 1, 119, false, Scan, Chapter3RaidPresence.Inside, exclusions);
        Assert.Equal(1, scans); Assert.Empty(site.Slots); Assert.Equal(Chapter3RaidPresence.Inside, site.Presence);
        exclusions.Storage.Clear();
        Assert.Equal(12, cache.Read("a", 1, 119, false, Scan, Chapter3RaidPresence.Near, exclusions).TotalUnits);
        cache.Read("a", 1, 120, false, Scan, Chapter3RaidPresence.Clear, exclusions);
        Assert.Equal(2, scans);
    }

    [Fact]
    public void Explicit_preview_and_eligible_raid_read_current_stock_between_slow_scans()
    {
        var cache = new Chapter3RaidScanCache(); var quantity = 12; var scans = 0;
        Chapter3RaidSite Scan() { scans++; return Site(quantity); }
        cache.Read("a", 1, 100, false, Scan, Chapter3RaidPresence.Clear, new()); quantity = 20;
        var live = cache.Read("a", 1, 100, true, Scan, Chapter3RaidPresence.Clear, new());
        Assert.Equal(20, live.TotalUnits); Assert.Equal(2, scans);
        Assert.Equal(20, cache.Read("a", 1, 101, false, Scan, Chapter3RaidPresence.Clear, new()).TotalUnits);
        Assert.Equal(2, scans);
    }

    [Fact]
    public void Replaced_property_reversal_disappearance_and_reset_invalidate_stock()
    {
        var cache = new Chapter3RaidScanCache(); var scans = 0;
        Chapter3RaidSite Read(long identity, double minute) => cache.Read("a", identity, minute, false,
            () => { scans++; return Site(); }, Chapter3RaidPresence.Clear, new());
        Read(1, 100); Read(2, 101); Read(2, 99); Assert.Equal(3, scans);
        cache.Retain(new HashSet<string>()); Read(2, 100); Assert.Equal(4, scans);
        cache.Reset(); Read(2, 100); Assert.Equal(5, scans);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(-1d)]
    public void Unknown_clock_does_not_scan_or_present_stale_stock(double? minute)
    {
        var cache = new Chapter3RaidScanCache(); var scans = 0;
        var site = cache.Read("a", 1, minute, false, () => { scans++; return Site(); }, Chapter3RaidPresence.Clear, new());
        Assert.Equal(0, scans); Assert.False(site.StockKnown); Assert.Empty(site.Slots);
    }

    [Fact]
    public void Failed_scan_cannot_return_old_stock_or_retry_at_poll_rate()
    {
        var cache = new Chapter3RaidScanCache(); var scans = 0;
        var site = cache.Read("a", 1, 100, false, () => { scans++; throw new InvalidOperationException(); }, Chapter3RaidPresence.Clear, new());
        Assert.False(site.StockKnown);
        site = cache.Read("a", 1, 101, false, () => { scans++; return Site(); }, Chapter3RaidPresence.Clear, new());
        Assert.False(site.StockKnown); Assert.Equal(1, scans);
        Assert.True(cache.Read("a", 1, 101, true, () => Site(), Chapter3RaidPresence.Clear, new()).StockKnown);
    }

    [Fact]
    public void Wrappers_reused_for_live_identity_but_replaced_or_removed_storage_rebuilds()
    {
        var cache = new Chapter3RaidWrapperCache<object>(); var creates = 0;
        object Create() { creates++; return new(); }
        var first = cache.Get(_guid, 1, Create);
        Assert.Same(first, cache.Get(_guid, 1, Create)); Assert.Equal(1, creates);
        Assert.NotSame(first, cache.Get(_guid, 2, Create)); Assert.Equal(2, creates);
        cache.Retain(new HashSet<Guid>()); cache.Get(_guid, 2, Create); Assert.Equal(3, creates);
        cache.Reset(); cache.Get(_guid, 2, Create); Assert.Equal(4, creates);
    }

    [Fact]
    public void Failed_wrapper_factory_is_not_cached()
    {
        var cache = new Chapter3RaidWrapperCache<object>();
        Assert.Throws<InvalidOperationException>(() => cache.Get(_guid, 1, () => throw new InvalidOperationException()));
        var value = new object();
        Assert.Same(value, cache.Get(_guid, 1, () => value));
    }
}
