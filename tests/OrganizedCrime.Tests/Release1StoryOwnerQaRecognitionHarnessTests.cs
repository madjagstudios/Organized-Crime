using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1StoryOwnerQaRecognitionHarnessTests
{
    private const string Player = "76561190000000001";
    private static readonly Guid Session = Guid.Parse("48484848-4848-4848-4848-484848484848");

    [Fact]
    public void Recognize_advances_all_six_missions_through_the_real_story_runtime()
    {
        var fixture = Fixture.Create();

        var result = Release1StoryOwnerQaRecognitionHarness.TryRecognize(fixture.Context, fixture.Service);

        Assert.True(result.Status == Release1StoryOwnerQaRecognitionStatus.Recognized, result.Message);
        Assert.True(fixture.Service.State!.Release1Recognized);
        Assert.Equal(80, fixture.Service.State.Standing);
        Assert.All(fixture.Service.State.Missions, mission => Assert.Equal(Release1MissionState.Satisfied, mission.State));
        Assert.Equal(6, fixture.Service.State.Missions.Count(mission => mission.RewardAuthorizationReceiptId is not null));
    }

    [Theory]
    [InlineData(Release1StoryHostContextReadStatus.NotAuthoritative)]
    [InlineData(Release1StoryHostContextReadStatus.AmbiguousIdentity)]
    public void Recognize_rejects_noncanonical_or_nonauthoritative_context_without_story_mutation(Release1StoryHostContextReadStatus status)
    {
        var fixture = Fixture.Create();
        fixture.Context.Status = status;

        var result = Release1StoryOwnerQaRecognitionHarness.TryRecognize(fixture.Context, fixture.Service);

        Assert.Equal(Release1StoryOwnerQaRecognitionStatus.Rejected, result.Status);
        Assert.Null(fixture.Service.State);
    }

    [Fact]
    public void Repeated_recognition_is_an_idempotent_noop()
    {
        var fixture = Fixture.Create();
        var first = Release1StoryOwnerQaRecognitionHarness.TryRecognize(fixture.Context, fixture.Service);
        Assert.True(first.Status == Release1StoryOwnerQaRecognitionStatus.Recognized, first.Message);
        var revision = fixture.Service.State!.Revision;

        var repeated = Release1StoryOwnerQaRecognitionHarness.TryRecognize(fixture.Context, fixture.Service);

        Assert.Equal(Release1StoryOwnerQaRecognitionStatus.AlreadyRecognized, repeated.Status);
        Assert.Equal(revision, fixture.Service.State!.Revision);
        Assert.Single(fixture.Service.State.RecognitionLogicalCorrelationIds);
    }

    private sealed class Fixture
    {
        private Fixture(MutableContext context, Release1StoryRuntimeService service)
        {
            Context = context;
            Service = service;
        }

        public MutableContext Context { get; }
        public Release1StoryRuntimeService Service { get; }

        public static Fixture Create()
        {
            var context = new MutableContext();
            var service = new Release1StoryRuntimeService(context, new MemoryRepository());
            service.OnPreLoad();
            service.OnLoadComplete();
            return new(context, service);
        }
    }

    private sealed class MutableContext : IRelease1StoryHostContext
    {
        public Release1StoryHostContextReadStatus Status { get; set; } = Release1StoryHostContextReadStatus.Ready;

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = new(Session, 1, Player, @"C:\Saves\76561190000000001\SaveGame_4");
            return Status;
        }
    }

    private sealed class MemoryRepository : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public string BoundSaveFolder => @"C:\Saves\76561190000000001\SaveGame_4";
        public Release1StoryState? State { get; private set; }
        public Release1StoryStoreLoadResult Load() => new(true, Release1StoryStoreLoadStatus.Empty, null, Release1StoryStoreFailureReason.None, "Owner QA fixture started empty.");
        public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
        {
            State = state;
            return new(true, Release1StoryStoreUpdateStatus.Updated, null, Release1StoryStoreFailureReason.None, "Owner QA fixture persisted.");
        }
    }
}
