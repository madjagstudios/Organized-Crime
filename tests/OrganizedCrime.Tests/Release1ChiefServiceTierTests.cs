using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ChiefServiceTierTests
{
    [Fact]
    public void ReadTier_returns_the_ledger_tier()
    {
        using var harness = TierHarness.Create();
        harness.Ledger.State = new LocalPressureState(60, false, null, null, null, harness.PlayerId, null, null, 0);

        Assert.Equal(LocalPressureTier.Watched, harness.Service.ReadTier());
    }

    [Fact]
    public void ReadTier_returns_Critical_for_high_heat()
    {
        using var harness = TierHarness.Create();
        harness.Ledger.State = new LocalPressureState(80, false, null, null, null, harness.PlayerId, null, null, 0);

        Assert.Equal(LocalPressureTier.Critical, harness.Service.ReadTier());
    }

    [Fact]
    public void ReadTier_returns_null_when_the_ledger_is_unreadable()
    {
        using var harness = TierHarness.Create();
        harness.Ledger.State = null;

        Assert.Null(harness.Service.ReadTier());
    }

    [Fact]
    public void ReadTier_returns_null_after_dispose()
    {
        using var harness = TierHarness.Create();
        harness.Ledger.State = new LocalPressureState(60, false, null, null, null, harness.PlayerId, null, null, 0);
        harness.Service.Dispose();

        Assert.Null(harness.Service.ReadTier());
    }

    private sealed class TierHarness : IDisposable
    {
        public Release1StoryRuntimeService Story { get; }
        public TierLedger Ledger { get; }
        public Release1ChiefService Service { get; }
        public string PlayerId { get; }

        private TierHarness(Release1StoryRuntimeService story, TierLedger ledger, Release1ChiefService service, string playerId)
        {
            Story = story;
            Ledger = ledger;
            Service = service;
            PlayerId = playerId;
        }

        public static TierHarness Create()
        {
            var context = new Release1SmallCourtesyDepositTests.FakeContext();
            var repository = new Release1SmallCourtesyDepositTests.FakeRepository();
            var story = new Release1StoryRuntimeService(context, repository);
            story.OnPreLoad();
            story.OnLoadComplete();

            const string receipt = "chief-tier-test-intro";
            var correlation = Release1LogicalCorrelation.Create(
                context.Snapshot.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value;
            Assert.True(story.TryExecuteDurably(new(
                context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
                Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt, correlation)).Accepted);

            var ledger = new TierLedger
            {
                SessionEpoch = context.Snapshot.SessionEpoch,
                LoadEpoch = context.Snapshot.LoadEpoch,
                State = LocalPressureState.Quiet(context.Snapshot.PlayerId)
            };
            var service = new Release1ChiefService(
                story, new TierWorld(), new Release1ChiefTierObserver(), ledger.Read, ledger.Wipe, ledger.Epochs, LocalPressureProfile.Moderate);
            return new TierHarness(story, ledger, service, context.Snapshot.PlayerId);
        }

        public void Dispose()
        {
            Service.Dispose();
            Story.Dispose();
        }
    }

    private sealed class TierWorld : Release1ChiefWorldStub
    {
    }

    private sealed class TierLedger
    {
        public LocalPressureState? State { get; set; }
        public Guid SessionEpoch { get; set; }
        public long LoadEpoch { get; set; }

        public LocalPressureState? Read(string playerId) => State;

        public LocalPressureEvidenceWriteResult Wipe(string playerId) =>
            new(true, LocalPressureEvidenceWriteRejectReason.None, null, "wiped");

        public bool Epochs(out Guid sessionEpoch, out long loadEpoch)
        {
            sessionEpoch = SessionEpoch;
            loadEpoch = LoadEpoch;
            return true;
        }
    }
}
