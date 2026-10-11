using System.Text.Json.Nodes;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// OC-141: the checkpoint target (property code + pick day) rides the
/// Chapter 3 save at schema 15; schema 14 files load with no target.
/// </summary>
public sealed class Chapter3CheckpointPersistenceTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Round_trip_preserves_the_checkpoint_target()
    {
        var state = Chapter3State.CreateInitial(PlayerId) with
        {
            CheckpointTargetCode = "barn",
            CheckpointTargetDay = 41
        };

        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var serializeResult), serializeResult.Message);
        Assert.Contains("\"checkpointTargetCode\": \"barn\"", json, StringComparison.Ordinal);
        Assert.Contains("\"checkpointTargetDay\": 41", json, StringComparison.Ordinal);
        Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var result), result.Message);

        Assert.Equal(state, envelope!.State);
    }

    [Fact]
    public void Schema14_file_loads_with_no_checkpoint_target()
    {
        var state = Chapter3State.CreateInitial(PlayerId) with
        {
            CheckpointTargetCode = "barn",
            CheckpointTargetDay = 41
        };

        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var serializeResult), serializeResult.Message);
        var node = JsonNode.Parse(json)!.AsObject();
        node["state"]!.AsObject().Remove("sweep");
        node["state"]!.AsObject().Remove("lastSweepStartMinutes");
        node["state"]!.AsObject().Remove("raid");
        node["state"]!.AsObject().Remove("lastRaidSuccessMinutes");
        node["schemaVersion"] = 14;
        node["state"]!.AsObject().Remove("checkpointTargetCode");
        node["state"]!.AsObject().Remove("checkpointTargetDay");
        Assert.True(Chapter3SaveCodec.TryDeserialize(node.ToJsonString(), out var envelope, out var result), result.Message);

        Assert.Equal(20, envelope!.SchemaVersion);
        Assert.Null(envelope.State!.CheckpointTargetCode);
        Assert.Null(envelope.State.CheckpointTargetDay);
    }

    [Fact]
    public void Blank_checkpoint_target_code_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Chapter3State(
            PlayerId, Chapter3Phase.NotStarted, null, -1, 0, CheckpointTargetCode: " "));
        Assert.Throws<ArgumentException>(() => new Chapter3State(
            PlayerId, Chapter3Phase.NotStarted, null, -1, 0, CheckpointTargetDay: -1));
    }
}
