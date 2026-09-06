using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// Arthur's Envelope calls: the warning on RequiredFailure reaching MakeGoodOffered, and the ending
/// line exactly once when the mission reaches Satisfied, regardless of which stage cleared it.
/// Mirrors <see cref="Release1WrongAddressArthurTests"/> in shape; the ending call has no reward to
/// wait on, but still waits on the consumption effect leaving Applied (via a native save), the same
/// way Wrong Address's own courteous call waits on its two effects.
/// </summary>
public sealed class Release1TheEnvelopeArthurTests
{
    [Fact]
    public void A_required_failure_queues_the_warning_call_exactly_once()
    {
        using var harness = Release1TheEnvelopeHarness.Active(withPhone: true);
        harness.RecordNellAcceptedReceipt();
        harness.World.TotalMinutes = 200d * 60d;

        harness.Service.Update();
        harness.Service.Update();

        var request = Assert.Single(harness.Queue.Invocations);
        Assert.Equal("Arthur Selby", request.CallerLabel);
        Assert.Equal(Release1PhoneCallRole.Arthur, request.Role);
        Assert.Equal(3, request.StageTexts.Count);
        Assert.StartsWith("The envelope did not land and the window is closed.", request.StageTexts[0], StringComparison.Ordinal);
        Assert.Contains("twenty four hours", request.StageTexts[1], StringComparison.Ordinal);
        foreach (var stage in request.StageTexts) AssertPlayerCopy(stage);
        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        AssertNellPrerequisite(request, harness.Mission().Attempt);

        harness.Service.Update();
        Assert.Single(harness.Queue.Invocations);
    }

    [Fact]
    public void A_clean_primary_completion_never_queues_the_warning_call()
    {
        using var harness = Release1TheEnvelopeHarness.Active(withPhone: true);
        harness.RecordNellAcceptedReceipt();

        Release1TheEnvelopeHarness.Save(harness);

        Assert.DoesNotContain(harness.Queue.Invocations, invocation =>
            invocation.StageTexts.Any(text => text.Contains("window is closed", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_make_good_failure_never_queues_the_warning_call()
    {
        using var harness = Release1TheEnvelopeHarness.MakeGoodActive(withPhone: true);
        harness.RecordNellAcceptedReceipt();
        harness.World.TotalMinutes = 400d * 60d;

        harness.Service.Update();
        harness.Service.Update();

        Assert.Equal(Release1MissionState.RecoveryAvailable, harness.Mission().State);
        Assert.Equal(Release1MissionOutcome.MakeGoodFailure, harness.Mission().LastOutcome);
        Assert.DoesNotContain(harness.Queue.Invocations, invocation =>
            invocation.StageTexts.Any(text => text.Contains("window is closed", StringComparison.Ordinal)));
    }

    private static void AssertNellPrerequisite(Release1PhoneCallRequest request, int attempt)
    {
        Assert.True(Release1LogicalCorrelation.TryParse(request.RequiredPriorCorrelationId, out var prior));
        Assert.Equal(Release1MissionCatalog.TheEnvelope, prior.MissionKey);
        Assert.Equal(attempt, prior.Attempt);
        Assert.Equal(Release1TransitionKind.MissionAccepted, prior.TransitionKind);
        Assert.EndsWith("presentation-nell-te-accepted-v1", prior.ReceiptId, StringComparison.Ordinal);
        Assert.True(Release1PhoneCallCorrelation.IsNellPrerequisite(prior));
    }

    private static void AssertPlayerCopy(string value)
    {
        Assert.DoesNotContain('—', value);
        Assert.DoesNotContain('–', value);
        Assert.DoesNotContain("--", value, StringComparison.Ordinal);
        Assert.DoesNotContain('‘', value);
        Assert.DoesNotContain('’', value);
        Assert.DoesNotContain('%', value);
        Assert.Equal(value, Release1PlayerCopy.Normalize(value));
    }
}
