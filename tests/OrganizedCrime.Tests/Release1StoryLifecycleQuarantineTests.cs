using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-10: a quarantined story runtime used to log nothing and let the vanilla save routine
/// delete the sidecar it never rewrote. These tests pin the fix: every non-accepted lifecycle
/// result is logged once through the OrganizedCrimeLog.Warning seam, and a load that quarantines
/// because the sidecar could not be parsed has its exact bytes rewritten on the next save start
/// instead of the runtime staying silent and writing nothing.
/// </summary>
public sealed class Release1StoryLifecycleQuarantineTests
{
    [Fact]
    public void Non_ready_host_context_logs_the_OnLoadComplete_rejection_once()
    {
        var context = new FakeContext { Status = Release1StoryHostContextReadStatus.Pending };
        var repository = new FakeRepository();
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();

        var warnings = new List<string>();
        var previous = OrganizedCrimeLog.Warning;
        OrganizedCrimeLog.Warning = warnings.Add;
        try
        {
            var result = service.OnLoadComplete();
            Release1StoryLifecycleLogging.LogIfRejected("OnLoadComplete", result);

            Assert.Equal(Release1StoryRuntimeRejectReason.ContextPending, result.RejectReason);
            var warning = Assert.Single(warnings);
            Assert.Equal("Story runtime OnLoadComplete rejected: ContextPending: Host context is pending.", warning);
        }
        finally { OrganizedCrimeLog.Warning = previous; }
    }

    [Fact]
    public void Accepted_lifecycle_result_logs_nothing()
    {
        var context = new FakeContext();
        var repository = new FakeRepository();
        var service = new Release1StoryRuntimeService(context, repository);

        var warnings = new List<string>();
        var previous = OrganizedCrimeLog.Warning;
        OrganizedCrimeLog.Warning = warnings.Add;
        try
        {
            Release1StoryLifecycleLogging.LogIfRejected("OnPreLoad", service.OnPreLoad());
            Release1StoryLifecycleLogging.LogIfRejected("OnLoadComplete", service.OnLoadComplete());

            Assert.Empty(warnings);
        }
        finally { OrganizedCrimeLog.Warning = previous; }
    }

    [Fact]
    public void Quarantined_load_failure_preserves_exact_bytes_on_the_next_save_start()
    {
        var context = new FakeContext();
        const string corruptSidecarContents = "{ this is not valid story json";
        var repository = new FakeRepository { RawLoadFailureContents = corruptSidecarContents };
        var service = new Release1StoryRuntimeService(context, repository);

        service.OnPreLoad();
        var loadResult = service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimeRejectReason.SidecarLoadFailed, loadResult.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, service.Phase);

        var saveStartResult = service.OnSaveStart();

        Assert.Equal(corruptSidecarContents, repository.PreservedRawContents);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(Release1StoryRuntimeRejectReason.Quarantined, saveStartResult.RejectReason);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, service.Phase);

        var saveCompleteResult = service.OnSaveComplete();
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(Release1StoryRuntimePhase.Quarantined, service.Phase);
        Assert.Equal(Release1StoryRuntimeRejectReason.None, saveCompleteResult.RejectReason);
    }

    [Fact]
    public void Missing_sidecar_on_load_writes_nothing_on_save_start()
    {
        var context = new FakeContext();
        var repository = new FakeRepository();
        var service = new Release1StoryRuntimeService(context, repository);
        service.OnPreLoad();
        service.OnLoadComplete();
        Assert.Equal(Release1StoryRuntimePhase.Active, service.Phase);
        Assert.Equal(0, repository.PreserveCallCount);
    }

    private sealed class FakeContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus Status { get; set; } = Release1StoryHostContextReadStatus.Ready;
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 1, "76561190000000001", System.IO.Path.GetTempPath());
        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot) { snapshot = Snapshot; return Status; }
    }

    private sealed class FakeRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository, IRelease1StoryRawContentPreservingRepository
    {
        public string BoundSaveFolder { get; } = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public string? RawLoadFailureContents { get; set; }
        public string? PreservedRawContents { get; private set; }
        public int UpdateCallCount { get; private set; }
        public int PreserveCallCount { get; private set; }

        public Release1StoryStoreLoadResult Load() =>
            RawLoadFailureContents is null
                ? new(true, Release1StoryStoreLoadStatus.Empty, Release1StorySaveEnvelope.CreateEmpty(), Release1StoryStoreFailureReason.None, "Story sidecar was absent; using empty envelope.")
                : new(false, Release1StoryStoreLoadStatus.Failed, null, Release1StoryStoreFailureReason.EmptyOrMalformedJson, "Story sidecar JSON was malformed.", RawLoadFailureContents);

        public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
        {
            UpdateCallCount++;
            return new(true, Release1StoryStoreUpdateStatus.Updated, new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state), Release1StoryStoreFailureReason.None, string.Empty);
        }

        public Release1StoryStoreUpdateResult PreserveRawContents(string rawContents)
        {
            PreserveCallCount++;
            PreservedRawContents = rawContents;
            return new(true, Release1StoryStoreUpdateStatus.Updated, null, Release1StoryStoreFailureReason.None, "Quarantined story sidecar bytes were preserved.");
        }
    }
}
