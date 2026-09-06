using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1PresentationReceiptTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Valid_receipt_round_trips_through_with()
    {
        var receipt = new Release1PresentationReceipt(Correlation("receipt-1"), 1);
        var moved = receipt with { Revision = 2 };

        Assert.Equal(2, moved.Revision);
        Assert.Equal(receipt.CorrelationId, moved.CorrelationId);
        moved.Validate();
    }

    [Fact]
    public void Blank_correlation_throws()
    {
        Assert.Throws<ArgumentException>(() => new Release1PresentationReceipt("", 1).Validate());
    }

    [Fact]
    public void Non_canonical_correlation_throws()
    {
        Assert.Throws<ArgumentException>(() => new Release1PresentationReceipt("not-canonical", 1).Validate());
    }

    [Fact]
    public void Negative_revision_throws()
    {
        Assert.Throws<ArgumentException>(() => new Release1PresentationReceipt(Correlation("receipt-1"), -1).Validate());
    }

    [Fact]
    public void State_rejects_a_receipt_whose_player_id_differs_from_player_id()
    {
        var otherPlayerCorrelation = Release1LogicalCorrelation.Create(
            "76561197984645370", Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "mismatch").Value;
        var story = Story() with { PresentationReceipts = new[] { new Release1PresentationReceipt(otherPlayerCorrelation, 1) } };

        Assert.Throws<ArgumentException>(() => story.Validate());
    }

    [Fact]
    public void State_rejects_duplicate_correlations()
    {
        var receipt = new Release1PresentationReceipt(Correlation("dup"), 1);
        var story = Story() with { PresentationReceipts = new[] { receipt, receipt } };

        Assert.Throws<ArgumentException>(() => story.Validate());
    }

    [Fact]
    public void ValueEquals_and_hashcode_change_when_receipts_differ()
    {
        var a = Story() with { PresentationReceipts = new[] { new Release1PresentationReceipt(Correlation("a"), 1) } };
        var b = Story() with { PresentationReceipts = new[] { new Release1PresentationReceipt(Correlation("b"), 1) } };

        Assert.False(a.ValueEquals(b));
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void TryRecordPresentationReceipt_is_accepted_and_leaves_repository_and_last_persisted_revision_unchanged()
    {
        var harness = ActiveHarness();
        var before = harness.Service.LastPersistedRevision;
        var correlationId = Correlation("native-1");

        var result = harness.Service.TryRecordPresentationReceipt(correlationId);

        Assert.Equal(Release1StoryCommandStatus.Accepted, result.Status);
        Assert.Null(harness.Repository.StoredState);
        Assert.Equal(before, harness.Service.LastPersistedRevision);
        Assert.Contains(harness.Service.State!.PresentationReceipts, receipt => receipt.CorrelationId == correlationId);
    }

    [Fact]
    public void Second_identical_call_is_noop()
    {
        var harness = ActiveHarness();
        var correlationId = Correlation("native-2");
        harness.Service.TryRecordPresentationReceipt(correlationId);
        var afterFirst = harness.Service.State;

        var result = harness.Service.TryRecordPresentationReceipt(correlationId);

        Assert.Equal(Release1StoryCommandStatus.NoOp, result.Status);
        Assert.Same(afterFirst, harness.Service.State);
        Assert.Single(harness.Service.State!.PresentationReceipts, receipt => receipt.CorrelationId == correlationId);
    }

    [Fact]
    public void Receipt_is_written_by_OnSaveStart_plus_OnSaveComplete()
    {
        var harness = ActiveHarness();
        var correlationId = Correlation("native-3");
        harness.Service.TryRecordPresentationReceipt(correlationId);
        Assert.Null(harness.Repository.StoredState);

        harness.Service.OnSaveStart();
        harness.Service.OnSaveComplete();

        Assert.NotNull(harness.Repository.StoredState);
        Assert.Contains(harness.Repository.StoredState!.PresentationReceipts, receipt => receipt.CorrelationId == correlationId);
    }

    [Fact]
    public void Malformed_existing_state_is_rejected_in_memory_instead_of_compounding()
    {
        // A receipt built through `with` bypasses Release1StoryState's validating constructor (that
        // constructor only runs for `new`, not for the compiler-generated copy used by `with`), so a
        // repository could hand back an already-invalid state without Validate() ever having run on
        // it (OnLoadComplete does not re-validate what it loads). TryRecordPresentationReceipt must
        // catch that before folding a new, otherwise-valid receipt on top of it.
        var malformed = Story() with
        {
            PresentationReceipts = new[] { new Release1PresentationReceipt("not-canonical", 1) }
        };
        var harness = ActiveHarness();
        harness.Repository.Update(malformed);
        harness.Service.OnPreLoad();
        harness.Service.OnLoadComplete();
        Assert.Same(malformed, harness.Service.State);
        var correlationId = Correlation("native-6");

        var result = harness.Service.TryRecordPresentationReceipt(correlationId);

        Assert.Equal(Release1StoryCommandStatus.Rejected, result.Status);
        Assert.Same(malformed, harness.Service.State);
        Assert.DoesNotContain(harness.Service.State!.PresentationReceipts, receipt => receipt.CorrelationId == correlationId);
    }

    [Fact]
    public void During_saving_the_receipt_is_deferred()
    {
        var harness = ActiveHarness();
        var correlationId = Correlation("native-4");
        harness.Service.OnSaveStart();

        var result = harness.Service.TryRecordPresentationReceipt(correlationId);

        Assert.Equal(Release1StoryCommandStatus.DeferredSaving, result.Status);
        harness.Service.OnSaveComplete();
        Assert.DoesNotContain(harness.Repository.StoredState!.PresentationReceipts, receipt => receipt.CorrelationId == correlationId);
    }

    [Fact]
    public void After_preload_without_a_save_the_receipt_is_gone()
    {
        var harness = ActiveHarness();
        var correlationId = Correlation("native-5");
        harness.Service.TryRecordPresentationReceipt(correlationId);
        Assert.Contains(harness.Service.State!.PresentationReceipts, receipt => receipt.CorrelationId == correlationId);

        harness.Service.OnPreLoad();
        harness.Service.OnLoadComplete();

        Assert.Null(harness.Service.State);
    }

    private static string Correlation(string receiptId) =>
        Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receiptId).Value;

    private static Release1StoryState Story() => Release1StoryState.CreateAccepted(
        PlayerId, Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro").Value);

    private static Harness ActiveHarness()
    {
        var context = new FakeContext();
        var repository = new FakeRepository();
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();
        service.OnLoadComplete();
        var intro = Release1LogicalCorrelation.Create(PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-receipt-harness").Value;
        service.TryExecute(new(
            context.Snapshot.SessionEpoch, context.Snapshot.LoadEpoch, context.Snapshot.PlayerId,
            Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, "intro-receipt-harness", intro));
        return new Harness(service, context, repository);
    }

    private sealed record Harness(Release1StoryRuntimeService Service, FakeContext Context, FakeRepository Repository);

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; set; } = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), 1, PlayerId, Path.GetFullPath(Path.GetTempPath()));
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot) { snapshot = Snapshot; return Release1StoryHostContextReadStatus.Ready; }
    }

    private sealed class FakeRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public string BoundSaveFolder { get; } = Path.GetFullPath(Path.GetTempPath());
        public Release1StoryState? StoredState { get; private set; }
        public Release1StoryStoreLoadResult Load() => new(true, StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded, new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState), Release1StoryStoreFailureReason.None, string.Empty);
        public Release1StoryStoreUpdateResult Update(Release1StoryState? state) { StoredState = state; return new(true, Release1StoryStoreUpdateStatus.Updated, new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state), Release1StoryStoreFailureReason.None, string.Empty); }
    }
}
