using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using Xunit;
using Harness = OrganizedCrime.Tests.Chapter3CashRuntimeTests.Harness;

namespace OrganizedCrime.Tests;

public sealed class Chapter3CashMovementTests
{
    [Fact]
    public void Current_save_writes_schema18()
    {
        Assert.True(Chapter3SaveCodec.TrySerialize(Chapter3State.CreateInitial("player"), out var json, out _));
        Assert.Contains("\"schemaVersion\": 20", json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Schema17_migrates_without_cash_assignment_and_schema18_round_trips(bool accepted)
    {
        var h = new Harness(); h.Freeze(); if (accepted) h.Accept();
        var original = h.Ctx.State; h.Reload(); Assert.Equal(original, h.Ctx.State);
        var node = System.Text.Json.Nodes.JsonNode.Parse(h.Serialize())!;
        node["state"]!.AsObject().Remove("raid");
        node["state"]!.AsObject().Remove("lastRaidSuccessMinutes");
        node["schemaVersion"] = 17;
        var board = node["state"]!["jobBoard"]!;
        Assert.False(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out _, out var invalid));
        Assert.Equal(Chapter3CodecFailureReason.UnknownField, invalid.Reason);
        board["openOffer"]?.AsObject().Remove("cashMovement");
        board["activeJob"]?.AsObject().Remove("cashMovement");
        board["lastCompletedJob"]?.AsObject().Remove("cashMovement");
        board["openOffer"]?.AsObject().Remove("shutdown");
        board["activeJob"]?.AsObject().Remove("shutdown");
        board["lastCompletedJob"]?.AsObject().Remove("shutdown");
        Assert.True(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out var loaded, out var read), read.Message);
        Assert.Equal(20, loaded!.SchemaVersion);
        Assert.Null(loaded.State!.JobBoard.OpenOffer?.CashMovement); Assert.Null(loaded.State.JobBoard.ActiveJob?.CashMovement);
    }

    [Theory]
    [InlineData("advance", "10000")]
    [InlineData("confirmedSlots", "1")]
    [InlineData("paid", "true")]
    [InlineData("creditBefore", "50")]
    [InlineData("unknown", "true")]
    [InlineData("plan", "\"te-closet-v1|0,1000,0\"")]
    public void Invalid_cash_progress_is_rejected(string field, string value)
    {
        var h = new Harness(); h.Accept(); var node = System.Text.Json.Nodes.JsonNode.Parse(h.Serialize())!;
        node["state"]!["jobBoard"]!["activeJob"]!["cashMovement"]![field] = System.Text.Json.Nodes.JsonNode.Parse(value);
        Assert.False(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out _, out _));
    }
}
