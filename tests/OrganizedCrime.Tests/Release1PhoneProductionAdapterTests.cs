using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using S1API.PhoneCalls;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1PhoneProductionAdapterTests
{
    private static readonly Release1StoryHostContextSnapshot Context = new(
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        1,
        "76561190000000001",
        Path.GetFullPath(Path.GetTempPath()));

    [Fact]
    public void Production_queue_converts_the_immutable_request_to_one_custom_definition()
    {
        var queued = new List<Release1PhoneCallProjection>();
        var adapter = new S1ApiRelease1PhoneCallQueue(queueCall: queued.Add);
        var request = NellRequest();

        adapter.Invoke(request);

        var projection = Assert.Single(queued);
        Assert.Equal("Eleanor \"Nell\" Grey", projection.CallerName);
        Assert.Equal(new[] { "First line.", "Second line." }, projection.StageTexts);
        Assert.True(typeof(PhoneCallDefinition).IsAssignableFrom(typeof(OcRelease1PhoneCallDefinition)));
    }

    [Fact]
    public void Unavailable_manager_fails_preflight_without_constructing_or_queueing_a_call()
    {
        var queued = new List<Release1PhoneCallProjection>();
        var adapter = new S1ApiRelease1PhoneCallQueue(() => false, queued.Add);

        Assert.False(adapter.IsAvailable(NellRequest()));
        Assert.Empty(queued);
    }

    [Fact]
    public void Payphone_banner_expires_and_never_reconstructs_during_reconciliation()
    {
        var now = 10f;
        using var banner = new Release1PayphoneBanner(() => now, durationSeconds: 8f);

        Assert.True(banner.TryShow("phone-correlation"));
        Assert.True(banner.Visible);
        now = 18.1f;
        banner.Update();
        Assert.False(banner.Visible);

        banner.Reconcile("phone-correlation");
        Assert.False(banner.Visible);
    }

    [Fact]
    public void Payphone_banner_ends_only_for_its_active_correlation()
    {
        using var banner = new Release1PayphoneBanner(() => 10f);
        Assert.True(banner.TryShow("phone-correlation"));

        banner.End("different-correlation");
        Assert.True(banner.Visible);

        banner.End("phone-correlation");
        Assert.False(banner.Visible);
    }

    private static Release1PhoneCallRequest NellRequest() => Release1PhoneCallRequest.Create(
        Context,
        Release1MissionCatalog.SmallCourtesy,
        1,
        Release1PhoneCallCorrelation.Create(
            Context.PlayerId,
            Release1MissionCatalog.SmallCourtesy,
            1,
            Release1PhoneCallRole.Nell,
            "production-adapter"),
        Release1PhoneCallRole.Nell,
        "Eleanor \"Nell\" Grey",
        new[] { "First line.", "Second line." });
}
