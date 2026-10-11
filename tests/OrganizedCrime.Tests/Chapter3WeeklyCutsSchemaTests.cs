using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3WeeklyCutsSchemaTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Schema1_file_loads_with_empty_cuts()
    {
        const string v1 = """
            {
              "schemaVersion": 1,
              "state": {
                "playerId": "76561190000000001",
                "phase": "Active",
                "startElapsedDay": 11,
                "lastProcessedElapsedDay": 13,
                "revision": 3
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v1, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3SaveCodec.CurrentSchemaVersion, envelope!.SchemaVersion);
        AssertCutsEmpty(envelope.State!.Cuts);
    }

    [Fact]
    public void Schema2_file_loads_with_empty_cuts()
    {
        const string v2 = """
            {
              "schemaVersion": 2,
              "state": {
                "playerId": "76561190000000001",
                "phase": "Active",
                "startElapsedDay": 11,
                "lastProcessedElapsedDay": 11,
                "revision": 1,
                "jobBoard": {
                  "openOffer": null,
                  "activeJob": null,
                  "cooldownUntilDay": null,
                  "lastProcessedDay": 0,
                  "lastTypeOffered": null,
                  "typesSeen": [],
                  "refusalStreak": 0,
                  "nellHintIssued": false,
                  "cooperationVisitIssued": false,
                  "successfulJobs": 0,
                  "successfulJobsSinceWarehousePurchase": 0,
                  "nextOfferId": 1,
                  "nextJobId": 1
                }
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v2, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3SaveCodec.CurrentSchemaVersion, envelope!.SchemaVersion);
        AssertCutsEmpty(envelope.State!.Cuts);
    }

    [Fact]
    public void Schema3_file_loads_with_empty_cuts_and_keeps_the_ledger()
    {
        const string v3 = """
            {
              "schemaVersion": 3,
              "state": {
                "playerId": "76561190000000001",
                "phase": "Active",
                "startElapsedDay": 11,
                "lastProcessedElapsedDay": 16,
                "revision": 6,
                "jobBoard": {
                  "openOffer": null,
                  "activeJob": null,
                  "cooldownUntilDay": null,
                  "lastProcessedDay": 5,
                  "lastTypeOffered": "DeadDrop",
                  "typesSeen": [
                    "DeadDrop"
                  ],
                  "refusalStreak": 0,
                  "nellHintIssued": false,
                  "cooperationVisitIssued": false,
                  "successfulJobs": 0,
                  "successfulJobsSinceWarehousePurchase": 0,
                  "nextOfferId": 2,
                  "nextJobId": 1
                },
                "ledger": {
                  "entries": [
                    {
                      "id": 1,
                      "source": {
                        "kind": "FailedJob",
                        "jobId": 1,
                        "jobType": "DeadDrop"
                      },
                      "amount": 17500,
                      "openedDay": 3,
                      "dueDay": 5,
                      "status": "Open",
                      "stage": "None",
                      "warningDeliveredDay": null,
                      "beatingDeliveredDay": null
                    }
                  ],
                  "lastProcessedDay": 5,
                  "nextId": 2
                }
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v3, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3SaveCodec.CurrentSchemaVersion, envelope!.SchemaVersion);
        var entry = Assert.Single(envelope.State!.Ledger.Entries);
        Assert.Equal(1, entry.Id);
        Assert.Equal(17500, entry.Amount);
        AssertCutsEmpty(envelope.State.Cuts);
    }

    [Fact]
    public void Serialized_sidecar_is_schema14_with_the_cuts()
    {
        var cuts = Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 1, 7, warehouseOwned: true).Cuts;
        var state = Chapter3State.ProcessDayTick(
            Chapter3State.CreateInitial(PlayerId), elapsedDays: 11, envelopeSatisfied: true).State with { Cuts = cuts };

        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var result), result.Message);

        Assert.Contains("\"schemaVersion\": 20", json);
        Assert.Contains("\"cuts\"", json);
        Assert.Contains("\"weekIndex\": 1", json);
    }

    [Fact]
    public void Cuts_round_trip_preserves_everything()
    {
        var cuts = new Chapter3WeeklyCuts(
            new[]
            {
                new Chapter3WeeklyCut(1, 30_000, 7, 9, Chapter3WeeklyCutStatus.Paid),
                new Chapter3WeeklyCut(2, 30_000, 14, 16, Chapter3WeeklyCutStatus.Due),
                new Chapter3WeeklyCut(3, 30_000, 21, 23, Chapter3WeeklyCutStatus.Missed)
            },
            22);
        var state = new Chapter3State(PlayerId, Chapter3Phase.Active, 11, 33, 9,
            Chapter3JobBoard.Empty, Chapter3MakeGoodLedger.Empty, cuts);

        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var serialize), serialize.Message);
        Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var deserialize), deserialize.Message);

        Assert.Equal(state, envelope!.State);
    }

    [Fact]
    public void Unknown_cuts_field_is_rejected()
    {
        var cuts = Chapter3WeeklyCutTransitions.OnWeekPass(Chapter3WeeklyCuts.Empty, 1, 7, warehouseOwned: true).Cuts;
        var state = Chapter3State.CreateInitial(PlayerId) with { Cuts = cuts };
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var tampered = json.Replace("\"graceUntilDay\":", "\"smuggled\": true, \"graceUntilDay\":");

        Assert.False(Chapter3SaveCodec.TryDeserialize(tampered, out _, out var result));

        Assert.Equal(Chapter3CodecFailureReason.UnknownField, result.Reason);
    }

    private static void AssertCutsEmpty(Chapter3WeeklyCuts cuts)
    {
        Assert.Empty(cuts.Entries);
        Assert.Equal(-1, cuts.LastProcessedDay);
    }
}
