using OrganizedCrime.Census;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressureRuntimeCensusObserverTests
{
    [Fact]
    public void Observe_records_one_bounded_read_only_snapshot()
    {
        var source = new FakeSource
        {
            Snapshot = new LocalPressureRuntimeCensusSnapshot(
                true,
                true,
                1441,
                1,
                1,
                23.5,
                false,
                true,
                1,
                true,
                true,
                true)
        };
        using var observer = new LocalPressureRuntimeCensusObserver();

        observer.Observe("OnLoadComplete", source);

        var row = Assert.Single(observer.Rows);
        Assert.Equal(1, row.Sequence);
        Assert.Equal("OnLoadComplete", row.CallbackName);
        Assert.True(row.SnapshotAvailable);
        Assert.Equal(1441, row.TotalGameMinutes);
        Assert.Equal(1, row.ElapsedDays);
        Assert.Equal(1, row.Time24h);
        Assert.Equal(23.5, row.CurrentTime);
        Assert.True(row.SaveFolderPresent);
        Assert.Equal(1, row.PlayerCount);
        Assert.True(row.HostOwnedPlayerCodeUnique);
    }

    [Fact]
    public void Source_failure_records_diagnostic_row_without_throwing()
    {
        using var observer = new LocalPressureRuntimeCensusObserver();

        observer.Observe("onHourPass", new FakeSource { Succeeds = false });

        var row = Assert.Single(observer.Rows);
        Assert.False(row.SnapshotAvailable);
        Assert.Equal("onHourPass", row.CallbackName);
        Assert.Null(row.TotalGameMinutes);
    }

    [Fact]
    public void Observer_is_bounded_and_drops_rows_after_capacity()
    {
        using var observer = new LocalPressureRuntimeCensusObserver(maxRows: 2);
        var source = new FakeSource
        {
            Snapshot = new LocalPressureRuntimeCensusSnapshot(
                true,
                true,
                1,
                0,
                1,
                0,
                false,
                true,
                1,
                true,
                true,
                true)
        };

        observer.Observe("first", source);
        observer.Observe("second", source);
        observer.Observe("dropped", source);

        Assert.Equal(2, observer.Rows.Count);
        Assert.Equal(2, observer.Rows[^1].Sequence);
        Assert.True(observer.CapacityReached);
    }

    [Fact]
    public void Time_binding_state_requires_rebind_for_a_new_manager_instance()
    {
        var state = new CensusTimeEventBindingState();
        var firstManager = new object();
        var replacementManager = new object();

        Assert.False(state.IsBoundTo(firstManager));

        state.MarkBound(firstManager);

        Assert.True(state.IsBoundTo(firstManager));
        Assert.False(state.IsBoundTo(replacementManager));

        state.Clear();

        Assert.False(state.IsBoundTo(firstManager));
    }

    [Fact]
    public void Capacity_notice_reports_once_after_the_bounded_buffer_is_full()
    {
        var notice = new CensusCapacityNotice();

        Assert.False(notice.ShouldReport(capacityReached: false));
        Assert.True(notice.ShouldReport(capacityReached: true));
        Assert.False(notice.ShouldReport(capacityReached: true));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Host_projection_requires_server_and_local_client(
        bool isServer,
        bool isClient,
        bool expected)
    {
        Assert.Equal(expected, CensusAuthorityProjection.IsHost(isServer, isClient));
    }

    private sealed class FakeSource : ILocalPressureRuntimeCensusSource
    {
        public bool Succeeds { get; set; } = true;
        public LocalPressureRuntimeCensusSnapshot Snapshot { get; set; }

        public bool TryReadSnapshot(out LocalPressureRuntimeCensusSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Succeeds;
        }
    }
}
