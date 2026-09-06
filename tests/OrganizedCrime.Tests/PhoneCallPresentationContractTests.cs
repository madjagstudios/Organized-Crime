using OrganizedCrime.PhoneCallProof;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-43 specification vectors for the production proof adapter. The queue is
/// a recording boundary so these tests never enter Unity/IL2CPP native code.
/// </summary>
public sealed class PhoneCallPresentationContractTests
{
    [Fact]
    public void Specification_vector_preserves_custom_name_and_ordered_stages()
    {
        // Catches a production change that invents NPC identity or reorders authored stages.
        var request = PhoneCallProofRequest.Create(
            "Eleanor \"Nell\" Grey",
            "The first line.",
            "The second line.");

        Assert.Equal("Eleanor \"Nell\" Grey", request.CallerName);
        Assert.Equal(new[] { "The first line.", "The second line." }, request.StageTexts);
    }

    [Fact]
    public void Specification_vector_canonical_host_queues_one_request_per_durable_correlation()
    {
        // Catches a production change that admits a duplicate or marks before queue success.
        var queue = new RecordingQueue();
        var adapter = new PhoneCallProofAdapter(new PhoneCallProofContext(true), queue);
        var request = PhoneCallProofRequest.Create("Arthur Selby", "A warning.");

        Assert.Equal(PhoneCallProofResult.Queued, adapter.TryRequest("oc-10.m2.warning", request));
        Assert.Equal(PhoneCallProofResult.Duplicate, adapter.TryRequest("oc-10.m2.warning", request));
        Assert.Single(queue.Requests);
    }

    [Fact]
    public void Specification_vector_non_canonical_host_is_inert()
    {
        // Catches a production change that lets a client/non-host reach the public queue boundary.
        var queue = new RecordingQueue();
        var adapter = new PhoneCallProofAdapter(new PhoneCallProofContext(false), queue);

        Assert.Equal(
            PhoneCallProofResult.NotCanonicalHost,
            adapter.TryRequest("oc-10.intro", PhoneCallProofRequest.Create("Eleanor \"Nell\" Grey", "Hello.")));
        Assert.Empty(queue.Requests);
    }

    [Fact]
    public void Specification_vector_missing_manager_defers_without_durable_admission()
    {
        // Catches a production change that claims delivery while CallManager is unavailable.
        var queue = new RecordingQueue { IsAvailable = false };
        var adapter = new PhoneCallProofAdapter(new PhoneCallProofContext(true), queue);
        var request = PhoneCallProofRequest.Create("Eleanor \"Nell\" Grey", "Try again later.");

        Assert.Equal(PhoneCallProofResult.Deferred, adapter.TryRequest("oc-10.retry", request));
        Assert.Empty(adapter.Snapshot().QueuedCorrelations);

        queue.IsAvailable = true;
        Assert.Equal(PhoneCallProofResult.Queued, adapter.TryRequest("oc-10.retry", request));
        Assert.Single(queue.Requests);
    }

    [Fact]
    public void Specification_vector_keeps_nell_before_arthur()
    {
        // Catches a production change that queues the authored conversation out of order.
        var queue = new RecordingQueue();
        var adapter = new PhoneCallProofAdapter(new PhoneCallProofContext(true), queue);

        adapter.TryRequest("oc-10.intro", PhoneCallProofRequest.Create("Eleanor \"Nell\" Grey", "Start."));
        adapter.TryRequest("oc-10.warning", PhoneCallProofRequest.Create("Arthur Selby", "Warning."));

        Assert.Equal(
            new[] { "Eleanor \"Nell\" Grey", "Arthur Selby" },
            queue.Requests.Select(request => request.CallerName));
    }

    [Fact]
    public void Specification_vector_completion_records_presentation_observation_only()
    {
        // Catches a production change that makes Completed/observation progression authority.
        var queue = new RecordingQueue();
        var adapter = new PhoneCallProofAdapter(new PhoneCallProofContext(true), queue);
        var correlation = "oc-10.m2.warning";
        adapter.TryRequest(correlation, PhoneCallProofRequest.Create("Arthur Selby", "Warning."));

        Assert.True(adapter.ObserveCompleted(correlation));
        Assert.Contains(correlation, adapter.Snapshot().CompletedCorrelations);
        Assert.Equal(0, adapter.Snapshot().StoryWrites);
        Assert.Equal(0, adapter.Snapshot().StandingWrites);
        Assert.Equal(0, adapter.Snapshot().RewardWrites);
    }

    [Fact]
    public void Specification_vector_reload_preserves_durable_state_and_teardown_ignores_late_completion()
    {
        // Catches a production change that requeues after reload or accepts callbacks after teardown.
        var queue = new RecordingQueue();
        var beforeReload = new PhoneCallProofAdapter(new PhoneCallProofContext(true), queue);
        var correlation = "oc-10.final.nell";
        var request = PhoneCallProofRequest.Create("Eleanor \"Nell\" Grey", "Recognition.");

        Assert.Equal(PhoneCallProofResult.Queued, beforeReload.TryRequest(correlation, request));
        var afterReload = PhoneCallProofAdapter.Restore(new PhoneCallProofContext(true), queue, beforeReload.Snapshot());
        Assert.Equal(PhoneCallProofResult.Duplicate, afterReload.TryRequest(correlation, request));

        afterReload.Dispose();
        Assert.False(afterReload.ObserveCompleted(correlation));
        Assert.Empty(afterReload.Snapshot().CompletedCorrelations);
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
}
