using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class LocalPressureTierTransitionFanOutTests
{
    private static readonly Guid SessionEpoch = Guid.NewGuid();

    [Fact]
    public void Publish_reaches_the_law_response_sink_first_and_the_chief_sink_second()
    {
        var order = new List<string>();
        using var fanOut = new LocalPressureTierTransitionFanOut(
            new RecordingSink("law", order), new RecordingSink("chief", order));

        fanOut.Publish(Notification());

        Assert.Equal(new[] { "law", "chief" }, order);
    }

    [Fact]
    public void A_throwing_first_sink_never_suppresses_the_second()
    {
        var order = new List<string>();
        var logs = new List<string>();
        using var fanOut = new LocalPressureTierTransitionFanOut(
            new ThrowingSink(), new RecordingSink("chief", order), logs.Add);

        fanOut.Publish(Notification());

        Assert.Equal(new[] { "chief" }, order);
        Assert.Single(logs);
    }

    [Fact]
    public void A_throwing_second_sink_never_undoes_the_first()
    {
        var order = new List<string>();
        var logs = new List<string>();
        using var fanOut = new LocalPressureTierTransitionFanOut(
            new RecordingSink("law", order), new ThrowingSink(), logs.Add);

        fanOut.Publish(Notification());

        Assert.Equal(new[] { "law" }, order);
        Assert.Single(logs);
    }

    [Fact]
    public void ResetForEpoch_reaches_both_and_survives_one_of_them_throwing()
    {
        var order = new List<string>();
        using var fanOut = new LocalPressureTierTransitionFanOut(
            new ThrowingSink(), new RecordingSink("chief", order));

        fanOut.ResetForEpoch(SessionEpoch, 9);

        Assert.Equal(new[] { "chief:reset:9" }, order);
    }

    [Fact]
    public void A_disposed_fan_out_forwards_nothing()
    {
        var order = new List<string>();
        var fanOut = new LocalPressureTierTransitionFanOut(
            new RecordingSink("law", order), new RecordingSink("chief", order));

        fanOut.Dispose();
        fanOut.Publish(Notification());
        fanOut.ResetForEpoch(SessionEpoch, 1);

        Assert.Empty(order);
    }

    [Fact]
    public void A_null_sink_in_either_position_throws_at_construction()
    {
        var recorded = new List<string>();
        Assert.Throws<ArgumentNullException>(() =>
            new LocalPressureTierTransitionFanOut(null!, new RecordingSink("chief", recorded)));
        Assert.Throws<ArgumentNullException>(() =>
            new LocalPressureTierTransitionFanOut(new RecordingSink("law", recorded), null!));
    }

    private static LocalPressureTierTransitionNotification Notification() => new(
        SessionEpoch, 4, "player-1", null, "corr-1",
        LocalPressureTier.Quiet, LocalPressureTier.Noticed, null, null);

    private sealed class RecordingSink : ILocalPressureTierTransitionSink
    {
        private readonly string _name;
        private readonly List<string> _order;

        public RecordingSink(string name, List<string> order)
        {
            _name = name;
            _order = order;
        }

        public void Publish(LocalPressureTierTransitionNotification notification) => _order.Add(_name);

        public void ResetForEpoch(Guid sessionEpoch, long loadEpoch) => _order.Add($"{_name}:reset:{loadEpoch}");
    }

    private sealed class ThrowingSink : ILocalPressureTierTransitionSink
    {
        public void Publish(LocalPressureTierTransitionNotification notification) =>
            throw new InvalidOperationException("sink failed");

        public void ResetForEpoch(Guid sessionEpoch, long loadEpoch) =>
            throw new InvalidOperationException("reset failed");
    }
}
