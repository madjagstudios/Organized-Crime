using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ChiefTierObserverTests
{
    private static readonly Guid SessionEpoch = Guid.NewGuid();

    [Fact]
    public void Every_notification_queues_an_arrest_because_only_arrests_publish()
    {
        var observer = new Release1ChiefTierObserver();

        observer.Publish(Note(LocalPressureTier.Quiet, LocalPressureTier.Quiet));

        Assert.True(observer.TryDequeue("player-1", SessionEpoch, 4, out var queued));
        Assert.Equal(Release1ChiefObservedEvent.ArrestObserved, queued.Kind);
        Assert.Equal(0, observer.QueueDepth);
    }

    [Theory]
    [InlineData(LocalPressureTier.Noticed, LocalPressureTier.Watched, Release1ChiefObservedEvent.RoseToWatched)]
    [InlineData(LocalPressureTier.Quiet, LocalPressureTier.Watched, Release1ChiefObservedEvent.RoseToWatched)]
    [InlineData(LocalPressureTier.Watched, LocalPressureTier.Critical, Release1ChiefObservedEvent.RoseToCritical)]
    [InlineData(LocalPressureTier.Noticed, LocalPressureTier.Critical, Release1ChiefObservedEvent.RoseToCritical)]
    public void A_supported_rising_edge_queues_the_arrest_first_and_the_crossing_second(
        LocalPressureTier previous, LocalPressureTier current, Release1ChiefObservedEvent expectedCrossing)
    {
        var observer = new Release1ChiefTierObserver();

        observer.Publish(Note(previous, current));

        Assert.True(observer.TryDequeue("player-1", SessionEpoch, 4, out var first));
        Assert.Equal(Release1ChiefObservedEvent.ArrestObserved, first.Kind);
        Assert.True(observer.TryDequeue("player-1", SessionEpoch, 4, out var second));
        Assert.Equal(expectedCrossing, second.Kind);
        Assert.Equal(0, observer.QueueDepth);
    }

    [Theory]
    [InlineData(LocalPressureTier.Quiet, LocalPressureTier.Noticed)]
    [InlineData(LocalPressureTier.Critical, LocalPressureTier.Watched)]
    [InlineData(LocalPressureTier.Watched, LocalPressureTier.Watched)]
    public void An_unsupported_or_falling_edge_queues_only_the_arrest(LocalPressureTier previous, LocalPressureTier current)
    {
        var observer = new Release1ChiefTierObserver();

        observer.Publish(Note(previous, current));

        Assert.True(observer.TryDequeue("player-1", SessionEpoch, 4, out var only));
        Assert.Equal(Release1ChiefObservedEvent.ArrestObserved, only.Kind);
        Assert.Equal(0, observer.QueueDepth);
    }

    [Fact]
    public void A_drain_for_the_wrong_player_epoch_or_load_epoch_skips_the_stale_heads()
    {
        // OC-77. A mismatched head used to wedge the queue: every drain returned false and left
        // it, so a stale entry starved everything behind it forever. Skipped heads count with
        // the overflow drops, which the service already reports once per load.
        foreach (var drain in new Func<Release1ChiefTierObserver, bool>[]
        {
            o => o.TryDequeue("player-2", SessionEpoch, 4, out _),
            o => o.TryDequeue("player-1", Guid.NewGuid(), 4, out _),
            o => o.TryDequeue("player-1", SessionEpoch, 5, out _),
        })
        {
            var observer = new Release1ChiefTierObserver();
            observer.Publish(Note(LocalPressureTier.Noticed, LocalPressureTier.Watched));

            Assert.False(drain(observer));
            Assert.Equal(0, observer.QueueDepth);
            Assert.Equal(2, observer.DroppedCount);
        }
    }

    [Fact]
    public void A_matching_event_behind_a_stale_head_is_reached_and_the_head_is_counted()
    {
        var observer = new Release1ChiefTierObserver();
        observer.Publish(Note(LocalPressureTier.Quiet, LocalPressureTier.Quiet, playerId: "player-9"));
        observer.Publish(Note(LocalPressureTier.Noticed, LocalPressureTier.Watched));

        Assert.True(observer.TryDequeue("player-1", SessionEpoch, 4, out var first));
        Assert.Equal(Release1ChiefObservedEvent.ArrestObserved, first.Kind);
        Assert.Equal(1, observer.QueueDepth);
        Assert.Equal(1, observer.DroppedCount);
        Assert.True(observer.TryDequeue("player-1", SessionEpoch, 4, out var second));
        Assert.Equal(Release1ChiefObservedEvent.RoseToWatched, second.Kind);
        Assert.Equal(0, observer.QueueDepth);
    }

    [Fact]
    public void ResetForEpoch_clears_the_queue_so_a_reload_never_replays_an_old_arrest()
    {
        var observer = new Release1ChiefTierObserver();
        observer.Publish(Note(LocalPressureTier.Noticed, LocalPressureTier.Watched));

        observer.ResetForEpoch(Guid.NewGuid(), 1);

        Assert.Equal(0, observer.QueueDepth);
    }

    [Fact]
    public void The_queue_is_bounded_and_drops_the_oldest_entries_once_full()
    {
        var observer = new Release1ChiefTierObserver();

        for (var i = 0; i < Release1ChiefTierObserver.MaximumQueueDepth + 5; i++)
            observer.Publish(Note(LocalPressureTier.Quiet, LocalPressureTier.Quiet, correlationId: $"corr-{i}"));

        Assert.Equal(32, observer.QueueDepth);
        Assert.Equal(5, observer.DroppedCount);
    }

    [Fact]
    public void Publish_null_never_throws_and_leaves_the_queue_empty()
    {
        var observer = new Release1ChiefTierObserver();

        var exception = Record.Exception(() => observer.Publish(null!));

        Assert.Null(exception);
        Assert.Equal(0, observer.QueueDepth);
    }

    private static LocalPressureTierTransitionNotification Note(
        LocalPressureTier previous,
        LocalPressureTier current,
        string correlationId = "corr-1",
        long loadEpoch = 4,
        string playerId = "player-1") =>
        new(SessionEpoch, loadEpoch, playerId, null, correlationId, previous, current, null, null);
}
