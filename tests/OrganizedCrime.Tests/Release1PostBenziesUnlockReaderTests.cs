using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1PostBenziesUnlockReaderTests
{
    private static readonly Release1StoryHostContextSnapshot ReadyHost = new(
        Guid.Parse("ab1b9168-49b8-405a-aec1-c42f35c42dd5"),
        7,
        "76561190000000001",
        @"C:\Saves\76561190000000001\SaveGame_4");

    [Fact]
    public void Defeated_cartel_is_the_only_unlocking_status()
    {
        var reader = Create(Release1CartelStatus.Defeated);

        var status = reader.TryRead(out var snapshot);

        Assert.Equal(Release1PostBenziesUnlockReadStatus.Unlocked, status);
        Assert.Equal(Release1CartelStatus.Defeated, snapshot.CartelStatus);
        Assert.Equal(ReadyHost, snapshot.HostContext);
    }

    [Theory]
    [InlineData(Release1CartelStatus.Unknown)]
    [InlineData(Release1CartelStatus.Hostile)]
    [InlineData(Release1CartelStatus.Truced)]
    public void Every_non_defeated_cartel_status_remains_locked(Release1CartelStatus cartelStatus)
    {
        var reader = Create(cartelStatus);

        var status = reader.TryRead(out var snapshot);

        Assert.Equal(Release1PostBenziesUnlockReadStatus.Locked, status);
        Assert.Equal(cartelStatus, snapshot.CartelStatus);
        Assert.Equal(ReadyHost, snapshot.HostContext);
    }

    [Theory]
    [InlineData(Release1StoryHostContextReadStatus.Pending, Release1PostBenziesUnlockReadStatus.Pending)]
    [InlineData(Release1StoryHostContextReadStatus.NotAuthoritative, Release1PostBenziesUnlockReadStatus.NotAuthoritative)]
    [InlineData(Release1StoryHostContextReadStatus.UnsupportedMultiplayer, Release1PostBenziesUnlockReadStatus.UnsupportedMultiplayer)]
    [InlineData(Release1StoryHostContextReadStatus.AmbiguousIdentity, Release1PostBenziesUnlockReadStatus.AmbiguousIdentity)]
    [InlineData(Release1StoryHostContextReadStatus.Faulted, Release1PostBenziesUnlockReadStatus.Faulted)]
    public void Host_gate_failure_prevents_cartel_read(
        Release1StoryHostContextReadStatus hostStatus,
        Release1PostBenziesUnlockReadStatus expected)
    {
        var source = new FakeCartelStatusSource(Release1CartelStatusReadStatus.Ready, Release1CartelStatus.Defeated);
        var reader = new Release1PostBenziesUnlockReader(new FakeHostContext(hostStatus), source);

        var status = reader.TryRead(out var snapshot);

        Assert.Equal(expected, status);
        Assert.Equal(default, snapshot);
        Assert.Equal(0, source.ReadCount);
    }

    [Theory]
    [InlineData(Release1CartelStatusReadStatus.Pending, Release1PostBenziesUnlockReadStatus.Pending)]
    [InlineData(Release1CartelStatusReadStatus.UnsupportedValue, Release1PostBenziesUnlockReadStatus.Faulted)]
    [InlineData(Release1CartelStatusReadStatus.Faulted, Release1PostBenziesUnlockReadStatus.Faulted)]
    public void Cartel_read_failure_fails_closed(
        Release1CartelStatusReadStatus sourceStatus,
        Release1PostBenziesUnlockReadStatus expected)
    {
        var reader = new Release1PostBenziesUnlockReader(
            new FakeHostContext(Release1StoryHostContextReadStatus.Ready),
            new FakeCartelStatusSource(sourceStatus, Release1CartelStatus.Defeated));

        var status = reader.TryRead(out var snapshot);

        Assert.Equal(expected, status);
        Assert.Equal(default, snapshot);
    }

    [Fact]
    public void Throwing_collaborator_fails_closed()
    {
        var reader = new Release1PostBenziesUnlockReader(
            new FakeHostContext(Release1StoryHostContextReadStatus.Ready),
            new ThrowingCartelStatusSource());

        var status = reader.TryRead(out var snapshot);

        Assert.Equal(Release1PostBenziesUnlockReadStatus.Faulted, status);
        Assert.Equal(default, snapshot);
    }

    private static Release1PostBenziesUnlockReader Create(Release1CartelStatus status) => new(
        new FakeHostContext(Release1StoryHostContextReadStatus.Ready),
        new FakeCartelStatusSource(Release1CartelStatusReadStatus.Ready, status));

    private sealed class FakeHostContext : IRelease1StoryHostContext
    {
        private readonly Release1StoryHostContextReadStatus _status;

        public FakeHostContext(Release1StoryHostContextReadStatus status) => _status = status;

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = _status == Release1StoryHostContextReadStatus.Ready ? ReadyHost : default;
            return _status;
        }
    }

    private sealed class FakeCartelStatusSource : IRelease1CartelStatusSource
    {
        private readonly Release1CartelStatusReadStatus _readStatus;
        private readonly Release1CartelStatus _status;

        public FakeCartelStatusSource(Release1CartelStatusReadStatus readStatus, Release1CartelStatus status)
        {
            _readStatus = readStatus;
            _status = status;
        }

        public int ReadCount { get; private set; }

        public Release1CartelStatusReadStatus TryRead(out Release1CartelStatus status)
        {
            ReadCount++;
            status = _status;
            return _readStatus;
        }
    }

    private sealed class ThrowingCartelStatusSource : IRelease1CartelStatusSource
    {
        public Release1CartelStatusReadStatus TryRead(out Release1CartelStatus status) =>
            throw new InvalidOperationException("synthetic read failure");
    }
}
