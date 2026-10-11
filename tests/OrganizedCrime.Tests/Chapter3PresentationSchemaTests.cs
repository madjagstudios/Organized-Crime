using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3PresentationSchemaTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Schema4_file_loads_with_empty_receipts_and_no_shortfalls()
    {
        const string v4 = """
            {
              "schemaVersion": 4,
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
                },
                "cuts": {
                  "entries": [
                    {
                      "weekIndex": 1,
                      "amount": 30000,
                      "dueDay": 7,
                      "graceUntilDay": 9,
                      "status": "Due"
                    }
                  ],
                  "lastProcessedDay": 7
                }
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v4, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3SaveCodec.CurrentSchemaVersion, envelope!.SchemaVersion);
        Assert.Empty(envelope.State!.PresentationReceipts);
        Assert.Equal(0, envelope.State.PaymentShortfallCount);
        Assert.Null(envelope.State.JobBoard.LastDeclinedOfferId);
        Assert.Null(envelope.State.JobBoard.LastExpiredOfferId);
        Assert.Single(envelope.State.Ledger.Entries);
        Assert.Single(envelope.State.Cuts.Entries);
    }

    [Fact]
    public void Schema1_file_loads_with_empty_receipts_and_no_shortfalls()
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
        Assert.Empty(envelope.State!.PresentationReceipts);
        Assert.Equal(0, envelope.State.PaymentShortfallCount);
    }

    [Fact]
    public void Receipts_shortfalls_and_closed_offers_round_trip()
    {
        var board = Chapter3JobBoard.Empty with { LastDeclinedOfferId = 4, LastExpiredOfferId = 2 };
        var state = new Chapter3State(PlayerId, Chapter3Phase.Active, 11, 33, 9,
            board, Chapter3MakeGoodLedger.Empty, Chapter3WeeklyCuts.Empty,
            new[] { new Chapter3PresentationReceipt("oc11-ch3/v1/76561190000000001/offer-4", 9) }, 2);

        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var serialize), serialize.Message);
        Assert.Contains("\"schemaVersion\": 20", json);
        Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var deserialize), deserialize.Message);

        Assert.Equal(state, envelope!.State);
    }

    [Fact]
    public void Unknown_receipts_field_is_rejected()
    {
        var state = Chapter3State.CreateInitial(PlayerId) with
        {
            PresentationReceipts = new[] { new Chapter3PresentationReceipt("oc11-ch3/v1/76561190000000001/nell-hint", 1) }
        };
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var tampered = json.Replace("\"presentationReceipts\":", "\"smuggled\": true, \"presentationReceipts\":");

        Assert.False(Chapter3SaveCodec.TryDeserialize(tampered, out _, out var result));

        Assert.Equal(Chapter3CodecFailureReason.UnknownField, result.Reason);
    }

    [Fact]
    public void Unknown_board_field_is_rejected()
    {
        var state = Chapter3State.CreateInitial(PlayerId);
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var tampered = json.Replace("\"nextJobId\":", "\"smuggled\": true, \"nextJobId\":");

        Assert.False(Chapter3SaveCodec.TryDeserialize(tampered, out _, out var result));

        Assert.Equal(Chapter3CodecFailureReason.UnknownField, result.Reason);
    }
}
