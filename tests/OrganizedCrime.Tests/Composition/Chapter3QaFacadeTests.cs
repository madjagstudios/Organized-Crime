// OC-151: unit tests for the Chapter 3 QA facade's checkpoint members. Refusal tests
// pin the exact disposed/unwired shapes; delegation tests drive a real checkpoint
// service (built over the shared fakes) through the facade; the pick test proves a
// throwing world maps instead of escaping.
using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3QaFacadeTests
{
    [Fact]
    public void Qa_members_refuse_when_disposed()
    {
        using var setup = ServiceSetup.Create();
        var facade = new Chapter3QaFacade(new object(), () => true) { Checkpoints = setup.Service };

        Assert.Equal(new[] { "checkpoints: Chapter 3 is not active." }, facade.QaCheckpointStatus());
        Assert.Equal("Chapter 3 QA checkpoint spot here refused: Chapter 3 is not active.", facade.QaCheckpointSpotHere("a").Message);
        Assert.Equal("Chapter 3 QA checkpoint test refused: Chapter 3 is not active.", facade.QaCheckpointTest("a").Message);
        Assert.Equal("Chapter 3 QA checkpoint clear refused: Chapter 3 is not active.", facade.QaCheckpointClear().Message);
        Assert.Equal("Chapter 3 QA checkpoint pick refused: Chapter 3 is not active.", facade.QaCheckpointPick().Message);
    }

    [Fact]
    public void Qa_members_refuse_when_unwired()
    {
        var facade = new Chapter3QaFacade(new object(), () => false);

        Assert.Equal(new[] { "checkpoints: the service is not wired." }, facade.QaCheckpointStatus());
        var spot = facade.QaCheckpointSpotHere("a");
        Assert.False(spot.Ok);
        Assert.Equal("Chapter 3 QA checkpoint spot here refused: the service is not wired.", spot.Message);
        var test = facade.QaCheckpointTest("a");
        Assert.False(test.Ok);
        Assert.Equal("Chapter 3 QA checkpoint test refused: the service is not wired.", test.Message);
        var clear = facade.QaCheckpointClear();
        Assert.False(clear.Ok);
        Assert.Equal("Chapter 3 QA checkpoint clear refused: the service is not wired.", clear.Message);
        var pick = facade.QaCheckpointPick();
        Assert.False(pick.Ok);
        Assert.Equal("Chapter 3 QA checkpoint pick refused: the service is not wired.", pick.Message);
    }

    [Fact]
    public void Qa_status_and_clear_delegate_to_the_wired_service()
    {
        using var setup = ServiceSetup.Create();
        var facade = new Chapter3QaFacade(new object(), () => false) { Checkpoints = setup.Service };

        var status = facade.QaCheckpointStatus();

        Assert.Equal("checkpoints: tier unknown, protected no, target none.", status[0]);
        var clear = facade.QaCheckpointClear();
        Assert.True(clear.Ok);
        Assert.Equal("Cleared 0 test checkpoint(s).", clear.Message);
    }

    [Fact]
    public void Qa_spot_and_test_refusals_come_from_the_wired_service()
    {
        using var setup = ServiceSetup.Create();
        var facade = new Chapter3QaFacade(new object(), () => false) { Checkpoints = setup.Service };

        var spot = facade.QaCheckpointSpotHere(string.Empty);
        Assert.False(spot.Ok);
        Assert.Equal("Chapter 3 QA checkpoint spot here refused: a label is required.", spot.Message);
        var test = facade.QaCheckpointTest("nope");
        Assert.False(test.Ok);
        Assert.Equal("Chapter 3 QA checkpoint test refused: unknown spot 'nope'.", test.Message);
    }

    [Fact]
    public void Qa_pick_refuses_when_the_clock_is_unreadable()
    {
        using var setup = ServiceSetup.Create();
        setup.World.Minutes = () => Release1SmallCourtesyWorldReadStatus.Unavailable;
        var facade = new Chapter3QaFacade(new object(), () => false) { Checkpoints = setup.Service };

        var pick = facade.QaCheckpointPick();

        Assert.False(pick.Ok);
        Assert.Equal("Chapter 3 QA checkpoint pick refused: the clock could not be read.", pick.Message);
    }

    [Fact]
    public void Qa_pick_maps_a_throwing_world()
    {
        using var setup = ServiceSetup.Create();
        setup.World.Minutes = () => throw new InvalidOperationException("boom");
        var facade = new Chapter3QaFacade(new object(), () => false) { Checkpoints = setup.Service };

        var pick = facade.QaCheckpointPick();

        Assert.False(pick.Ok);
        Assert.Equal("Chapter 3 QA checkpoint pick refused: the service threw (boom).", pick.Message);
    }

    private sealed class ServiceSetup : IDisposable
    {
        public CompositionThrowingCourtesyWorld World { get; }
        public Chapter3CheckpointService Service { get; }

        private ServiceSetup(Chapter3CheckpointService service, CompositionThrowingCourtesyWorld world)
        {
            Service = service;
            World = world;
        }

        public static ServiceSetup Create()
        {
            var world = new CompositionThrowingCourtesyWorld();
            var story = new Release1StoryRuntimeService(
                new CompositionFakeStoryContext(),
                _ => new CompositionFakeRelease1StoryRepository());
            var chief = new Release1ChiefService(
                story,
                world,
                new Release1ChiefTierObserver(),
                _ => null,
                _ => new LocalPressureEvidenceWriteResult(false, LocalPressureEvidenceWriteRejectReason.None, null, "unused"),
                (out Guid sessionEpoch, out long loadEpoch) => { sessionEpoch = Guid.Empty; loadEpoch = 0; return false; },
                LocalPressureProfile.Moderate);
            var roster = new OcOfficerRosterService(new CompositionFakeOfficerWorld(), "oc151-test", _ => { });
            var service = new Chapter3CheckpointService(
                new CompositionFakeCheckpointHost(),
                chief,
                world,
                new CompositionFakeCheckpointWorld { IsHostValue = true },
                roster,
                _ => { });
            return new ServiceSetup(service, world);
        }

        public void Dispose() => Service.Dispose();
    }
}
