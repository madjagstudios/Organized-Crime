using OrganizedCrime.Model;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1RoomWithNoNameArthurTests
{
    [Fact]
    public void Arthur_rings_once_on_a_required_failure_and_never_on_a_clean_run()
    {
        using var failed = Release1RoomWithNoNameHarness.RequiredFailed(withPhone: true);
        failed.Service.Update();
        failed.Service.Update();

        var request = Assert.Single(failed.Queue.Invocations);
        Assert.Equal(Release1PhoneCallRole.Arthur, request.Role);
        Assert.Equal("Arthur Selby", request.CallerLabel);
        Assert.Equal(Release1MissionCatalog.RoomWithNoName, request.MissionKey);
        Assert.Collection(request.StageTexts,
            text => Assert.Equal("I am told the room is fucking empty and the day is not up. You don't want a visit from me. Fix it.", text),
            text => Assert.Equal("Nell has arranged one more run. You have seventy two hours from the moment you accept it. She's a lot more fucking forgiving than I am, don't fuck this up.", text),
            text => Assert.Equal("Do not make her ask twice.", text));
        foreach (var text in request.StageTexts) AssertPlayerCopy(text);

        using var clean = Release1RoomWithNoNameHarness.CleanlyCompleted(withPhone: true);
        clean.Service.Update();
        clean.Service.Update();
        Assert.Empty(clean.Queue.Invocations);
    }

    [Fact]
    public void Arthur_requires_a_receipted_nell_message_on_the_same_attempt()
    {
        using var harness = Release1RoomWithNoNameHarness.RequiredFailed(withPhone: true, recordNellReceipt: false);

        harness.Service.Update();

        Assert.Empty(harness.Queue.Invocations);
    }

    [Fact]
    public void A_queue_failure_never_blocks_the_mission()
    {
        using var harness = Release1RoomWithNoNameHarness.RequiredFailed(withPhone: true);
        harness.Queue.ThrowOnInvoke = true;

        harness.Service.Update();

        Assert.Equal(Release1MissionState.MakeGoodOffered, harness.Mission().State);
        Assert.Equal(Release1RoomWithNoNameOfferStatus.Available, harness.Service.OfferStatus);
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
