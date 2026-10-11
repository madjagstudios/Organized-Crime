using System.Text.Json.Nodes;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime.Chapter3;
using Xunit;
using CashHarness = OrganizedCrime.Tests.Chapter3CashRuntimeTests.Harness;
using ShutdownHarness = OrganizedCrime.Tests.Chapter3ShutdownTests.Harness;

namespace OrganizedCrime.Tests;

public sealed class Chapter3ShutdownReviewTests
{
    [Theory]
    [InlineData(Chapter3JobType.CashMovement, false)]
    [InlineData(Chapter3JobType.OperationalShutdown, false)]
    [InlineData(Chapter3JobType.CashMovement, true)]
    [InlineData(Chapter3JobType.OperationalShutdown, true)]
    public void Mixed_terms_never_drain_or_reimburse_cash(Chapter3JobType type, bool reconciling)
    {
        var h = new CashHarness(); h.Accept(); var job = h.Job; h.Deposit(5);
        var cash = h.Run;
        if (reconciling)
        {
            h.Poll(); cash = h.Ctx.State!.JobBoard.LastCompletedJob!.CashMovement! with { Paid = false };
            h.Wallet.CashBalance = 0; h.Wallet.CashChanges.Clear(); h.Room.Changes.Clear();
        }
        h.Ctx.State = h.Ctx.State! with { JobBoard = h.Ctx.State.JobBoard with
            { LastCompletedJob = null, ActiveJob = job with
                { JobType = type, CashMovement = cash, Shutdown = new("target", "target name") } } };
        var before = h.Ctx.State;
        h.Poll(); h.Poll();
        Assert.Empty(h.Room.Changes); Assert.Empty(h.Wallet.CashChanges);
        Assert.Same(before, h.Ctx.State);
    }

    [Theory]
    [InlineData(Chapter3JobType.CashMovement, false)]
    [InlineData(Chapter3JobType.OperationalShutdown, false)]
    [InlineData(Chapter3JobType.CashMovement, true)]
    [InlineData(Chapter3JobType.OperationalShutdown, true)]
    public void Mixed_terms_never_progress_or_pay_shutdown(Chapter3JobType type, bool creditIntent)
    {
        var h = new ShutdownHarness(); h.Accept(); h.Quiet();
        h.Ctx.State = h.Ctx.State! with { JobBoard = h.Ctx.State.JobBoard with
            { ActiveJob = h.Job with { JobType = type, CashMovement = new(5000),
                Shutdown = h.Run with { QuietSince = 0, QuietComplete = true,
                    CreditBefore = creditIntent ? 0d : null } } } };
        var before = h.Ctx.State;
        h.Poll(); h.Poll();
        Assert.Empty(h.Wallet.CashChanges); Assert.Same(before, h.Ctx.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mixed_terms_cannot_be_accepted_or_failed(bool shutdown)
    {
        if (shutdown)
        {
            var h = new ShutdownHarness(); h.Freeze();
            Assert.False(h.Shutdown.TryAccept(h.Offer with { CashMovement = new(5000) }));
            h.Accept();
            h.Ctx.State = h.Ctx.State! with { JobBoard = h.Ctx.State.JobBoard with
                { ActiveJob = h.Job with { CashMovement = new(5000) } } };
            var before = h.Ctx.State; h.Shutdown.Fail(3);
            Assert.Same(before, h.Ctx.State); Assert.Empty(h.Ctx.State!.Ledger.Entries);
        }
        else
        {
            var h = new CashHarness(); h.Freeze();
            Assert.False(h.Cash.TryAccept(h.Offer with { Shutdown = new("target", "target name") }));
            h.Accept();
            h.Ctx.State = h.Ctx.State! with { JobBoard = h.Ctx.State.JobBoard with
                { ActiveJob = h.Job with { Shutdown = new("target", "target name") } } };
            var before = h.Ctx.State; h.Cash.Fail(3);
            Assert.Same(before, h.Ctx.State); Assert.Empty(h.Ctx.State!.Ledger.Entries);
        }
    }

    [Theory]
    [InlineData("openOffer", false)]
    [InlineData("activeJob", false)]
    [InlineData("lastCompletedJob", false)]
    [InlineData("openOffer", true)]
    [InlineData("activeJob", true)]
    [InlineData("lastCompletedJob", true)]
    public void Codec_rejects_mixed_terms_as_invalid_state(string owner, bool shutdown)
    {
        var cash = new CashHarness(); cash.Freeze();
        var quiet = new ShutdownHarness(); quiet.Freeze();
        if (owner != "openOffer") { cash.Accept(); quiet.Accept(); }
        if (owner == "lastCompletedJob")
        {
            cash.Deposit(5); cash.Poll(); quiet.Quiet(); quiet.Poll(); quiet.Advance(720); quiet.Poll();
        }
        var cashRoot = JsonNode.Parse(cash.Serialize())!;
        var quietRoot = JsonNode.Parse(quiet.Serialize())!;
        var root = shutdown ? quietRoot : cashRoot;
        var other = shutdown ? cashRoot : quietRoot;
        var field = shutdown ? "cashMovement" : "shutdown";
        root["state"]!["jobBoard"]![owner]![field] = JsonNode.Parse(
            other["state"]!["jobBoard"]![owner]![field]!.ToJsonString());
        Assert.False(Chapter3SaveCodec.TryDeserialize(root.ToJsonString(), out var loaded, out var result));
        Assert.Null(loaded); Assert.Equal(Chapter3CodecFailureReason.InvalidState, result.Reason);
    }

    [Fact]
    public void Board_validation_and_serialization_reject_mixed_active_terms()
    {
        var h = new ShutdownHarness(); h.Accept();
        h.Ctx.State = h.Ctx.State! with { JobBoard = h.Ctx.State.JobBoard with
            { ActiveJob = h.Job with { CashMovement = new(5000) } } };
        Assert.Throws<ArgumentException>(() => h.Ctx.State.JobBoard.Validate());
        Assert.False(Chapter3SaveCodec.TrySerialize(h.Ctx.State, out var json, out var result));
        Assert.Equal(string.Empty, json); Assert.Equal(Chapter3CodecFailureReason.InvalidState, result.Reason);
    }

    [Fact]
    public void Shutdown_poll_prunes_old_job_holds_and_preserves_current_job_suppression()
    {
        var h = new ShutdownHarness(); h.Accept(); h.World.Readable = false; h.Poll();
        var holds = h.Ctx.LoggedDeliveryHolds;
        var current = $"{h.Job.JobId}:shutdown-unreadable";
        Assert.True(holds.Contains(current));
        Assert.True(holds.Report("99:shutdown-unreadable", "old shutdown"));
        Assert.True(holds.Report("100:cash-unreadable", "old cash"));
        h.Poll();
        Assert.False(holds.Contains("99:shutdown-unreadable"));
        Assert.False(holds.Contains("100:cash-unreadable"));
        Assert.True(holds.Contains(current));
        Assert.Single(h.Logs, line => line.Contains("shutdown holding"));
    }
}
