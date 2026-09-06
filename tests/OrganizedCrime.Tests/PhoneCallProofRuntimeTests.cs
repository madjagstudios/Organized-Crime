using OrganizedCrime.PhoneCallProof;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class PhoneCallProofRuntimeTests
{
    [Fact]
    public void Owner_keys_are_the_only_path_and_keep_nell_before_arthur()
    {
        // Catches a production change that auto-queues or permits Arthur before Nell.
        var queue = new RecordingQueue();
        var keys = new KeySequence();
        var now = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var runtime = new PhoneCallProofRuntime(new PhoneCallProofContext(true), queue, keys.IsDown, _ => { }, () => now);

        runtime.Initialize();
        runtime.Update();
        Assert.Empty(queue.Requests);

        keys.Press(PhoneProofKey.Nell);
        runtime.Update();
        keys.Press(PhoneProofKey.Arthur);
        runtime.Update();

        Assert.Equal(
            new[] { "Nell Grey", "Arthur Selby" },
            queue.Requests.Select(request => request.CallerName));
    }

    [Fact]
    public void Deadline_stops_the_protocol_without_a_background_retry()
    {
        // Catches a production change that retries or queues after the bounded deadline.
        var queue = new RecordingQueue();
        var keys = new KeySequence();
        var now = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var runtime = new PhoneCallProofRuntime(new PhoneCallProofContext(true), queue, keys.IsDown, _ => { }, () => now);

        runtime.Initialize();
        now = now.AddMinutes(16);
        runtime.Update();
        keys.Press(PhoneProofKey.Nell);
        runtime.Update();

        Assert.Equal(PhoneProofProtocolState.Stop, runtime.State);
        Assert.Empty(queue.Requests);
        Assert.Equal(PhoneProofClassification.Stop, runtime.Evidence.Classification);
    }

    [Fact]
    public void Missing_manager_is_deferred_and_classifies_inconclusive()
    {
        // Catches a production change that treats an unavailable CallManager as PASS.
        var queue = new RecordingQueue { IsAvailable = false };
        var keys = new KeySequence(PhoneProofKey.Nell, PhoneProofKey.Classify);
        var now = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var runtime = new PhoneCallProofRuntime(new PhoneCallProofContext(true), queue, keys.IsDown, _ => { }, () => now);

        runtime.Initialize();
        runtime.Update();
        runtime.Update();

        Assert.Equal(PhoneProofClassification.Inconclusive, runtime.Evidence.Classification);
        Assert.Equal(0, runtime.Evidence.StoryWrites);
        Assert.Equal(0, runtime.Evidence.StandingWrites);
        Assert.Equal(0, runtime.Evidence.RewardWrites);
    }

    [Fact]
    public void Boundary_stop_disposes_callbacks_and_classification_is_stop()
    {
        // Catches a production change that accepts late completion after reload/teardown.
        var queue = new RecordingQueue();
        var keys = new KeySequence(PhoneProofKey.Nell);
        var now = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var runtime = new PhoneCallProofRuntime(new PhoneCallProofContext(true), queue, keys.IsDown, _ => { }, () => now);

        runtime.Initialize();
        runtime.Update();
        runtime.StopForBoundary("reload");

        Assert.Equal(PhoneProofProtocolState.Stop, runtime.State);
        Assert.False(runtime.ObserveCompleted("oc-43.nell"));
        Assert.Equal(PhoneProofClassification.Stop, runtime.Evidence.Classification);
    }

    [Fact]
    public void Classification_requires_all_owner_observations_for_pass()
    {
        // Catches a production change that promotes static/partial evidence to PASS.
        var queue = new RecordingQueue();
        var keys = new KeySequence(PhoneProofKey.Nell, PhoneProofKey.Arthur, PhoneProofKey.Classify);
        var now = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var runtime = new PhoneCallProofRuntime(new PhoneCallProofContext(true), queue, keys.IsDown, _ => { }, () => now);

        runtime.Initialize();
        runtime.Update();
        runtime.Update();
        runtime.Update();
        Assert.Equal(PhoneProofClassification.Inconclusive, runtime.Evidence.Classification);

        runtime.RecordOwnerObservations(new(true, true, true, true, true, true));
        keys.Press(PhoneProofKey.Classify);
        runtime.Update();

        Assert.Equal(PhoneProofClassification.Pass, runtime.Evidence.Classification);
        Assert.Equal(0, runtime.Evidence.StoryWrites);
        Assert.Equal(0, runtime.Evidence.StandingWrites);
        Assert.Equal(0, runtime.Evidence.RewardWrites);
    }

    private sealed class RecordingQueue : IPhoneCallQueue
    {
        public bool IsAvailable { get; set; } = true;
        public List<PhoneCallProofRequest> Requests { get; } = new();

        public bool TryQueue(PhoneCallProofRequest request)
        {
            if (!IsAvailable)
                return false;
            Requests.Add(request);
            return true;
        }
    }

    private sealed class KeySequence
    {
        private readonly Queue<PhoneProofKey> _keys;

        public KeySequence(params PhoneProofKey[] keys) => _keys = new(keys);

        public void Press(PhoneProofKey key) => _keys.Enqueue(key);

        public bool IsDown(PhoneProofKey key) => _keys.Count > 0 && _keys.Peek() == key && _keys.Dequeue() == key;
    }
}
