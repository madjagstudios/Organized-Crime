using System.Text.Json.Nodes;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Chapter3StorySchemaTests
{
    private const string PlayerId = "76561190000000001";

    [Fact]
    public void Schema1_active_file_loads_with_an_empty_job_board()
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
        Assert.Equal(PlayerId, envelope.State!.PlayerId);
        Assert.Equal(Chapter3Phase.Active, envelope.State.Phase);
        Assert.Equal(11, envelope.State.StartElapsedDay);
        Assert.Equal(13, envelope.State.LastProcessedElapsedDay);
        Assert.Equal(3, envelope.State.Revision);
        AssertBoardEmpty(envelope.State.JobBoard);
    }

    [Fact]
    public void Schema1_unstarted_file_loads_with_an_empty_job_board()
    {
        const string v1 = """
            {
              "schemaVersion": 1,
              "state": {
                "playerId": "76561190000000001",
                "phase": "NotStarted",
                "startElapsedDay": null,
                "lastProcessedElapsedDay": 10,
                "revision": 1
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v1, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3Phase.NotStarted, envelope!.State!.Phase);
        AssertBoardEmpty(envelope.State.JobBoard);
    }

    [Fact]
    public void Serialized_sidecar_is_schema15_with_the_board_the_ledger_the_cuts_the_receipts_the_warehouse_offer_the_visit_series_the_beatings_and_the_checkpoint_target()
    {
        var ticked = Chapter3State.ProcessDayTick(
            Chapter3State.CreateInitial(PlayerId), elapsedDays: 11, envelopeSatisfied: true).State;
        var state = ticked with
        {
            WarehouseOffer = new Chapter3WarehouseOffer(OpenedDay: 18, FellThroughCount: 1)
        };

        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var result), result.Message);

        Assert.Contains("\"schemaVersion\": 20", json);
        Assert.Contains("\"ledger\"", json);
        Assert.Contains("\"cuts\"", json);
        Assert.Contains("\"presentationReceipts\"", json);
        Assert.Contains("\"jobBoard\"", json);
        Assert.Contains("\"jobType\": \"DeadDrop\"", json);
        Assert.Contains("\"lastCompletedJob\"", json);
        Assert.Contains("\"warehouseOffer\"", json);
        Assert.Contains("\"fellThroughCount\"", json);
        Assert.Contains("\"arthurVisitPending\"", json);
        Assert.Contains("\"arthurVisitMissedEvenings\"", json);
        Assert.Contains("\"arthurVisitLastAttemptedDay\"", json);
        Assert.Contains("\"arthurCooperationDelivered\"", json);
        Assert.Contains("\"arthurWarningFollowups\"", json);
        Assert.Contains("\"arthurRanOffs\"", json);
        Assert.Contains("\"arthurBeatings\"", json);
        Assert.Contains("\"checkpointTargetCode\"", json);
        Assert.Contains("\"checkpointTargetDay\"", json);
    }

    [Fact]
    public void Schema10_file_loads_with_a_default_visit_series()
    {
        const string v10 = """
            {
              "schemaVersion": 10,
              "state": {
                "playerId": "76561190000000001",
                "phase": "Active",
                "startElapsedDay": 11,
                "lastProcessedElapsedDay": 25,
                "revision": 9,
                "jobBoard": {
                  "openOffer": null,
                  "activeJob": null,
                  "cooldownUntilDay": null,
                  "lastProcessedDay": 14,
                  "lastTypeOffered": "DeadDrop",
                  "typesSeen": [
                    "DeadDrop"
                  ],
                  "refusalStreak": 0,
                  "nellHintIssued": false,
                  "cooperationVisitIssued": false,
                  "successfulJobs": 6,
                  "successfulJobsSinceWarehousePurchase": 1,
                  "nextOfferId": 4,
                  "nextJobId": 4,
                  "lastDeclinedOfferId": null,
                  "lastExpiredOfferId": null,
                  "lastRefusedOfferId": null,
                  "lastCompletedJob": null,
                  "lastEmergencySupplyOfferId": null
                },
                "ledger": {
                  "entries": [],
                  "lastProcessedDay": -1,
                  "nextId": 1
                },
                "cuts": {
                  "entries": [],
                  "lastProcessedDay": -1
                },
                "presentationReceipts": [],
                "paymentShortfallCount": 0,
                "warehouseOffer": {
                  "openedDay": 7,
                  "lastReminderWeek": 0,
                  "purchased": true,
                  "purchasedDay": 14,
                  "notReadyCount": 0,
                  "fellThroughCount": 0
                }
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v10, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3SaveCodec.CurrentSchemaVersion, envelope!.SchemaVersion);
        Assert.Equal(20, envelope.SchemaVersion);
        Assert.Equal(Chapter3ArthurVisitKind.None, envelope.State!.ArthurVisitPending);
        Assert.Equal(0, envelope.State.ArthurVisitMissedEvenings);
        Assert.Equal(-1, envelope.State.ArthurVisitLastAttemptedDay);
        Assert.False(envelope.State.ArthurCooperationDelivered);
        Assert.Equal(0, envelope.State.ArthurWarningFollowups);
        Assert.Equal(0, envelope.State.ArthurRanOffs);
        Assert.Equal(0, envelope.State.ArthurBeatings);
    }

    [Fact]
    public void Schema1_file_loads_with_a_default_visit_series()
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

        Assert.Equal(Chapter3ArthurVisitKind.None, envelope!.State!.ArthurVisitPending);
        Assert.Equal(0, envelope.State.ArthurVisitMissedEvenings);
        Assert.Equal(-1, envelope.State.ArthurVisitLastAttemptedDay);
        Assert.False(envelope.State.ArthurCooperationDelivered);
        Assert.Equal(0, envelope.State.ArthurWarningFollowups);
        Assert.Equal(0, envelope.State.ArthurRanOffs);
        Assert.Equal(0, envelope.State.ArthurBeatings);
    }

    [Fact]
    public void Visit_series_and_beatings_round_trip_through_schema14()
    {
        var state = new Chapter3State(PlayerId, Chapter3Phase.Active, 11, 16, 0)
            with
            {
                ArthurVisitPending = Chapter3ArthurVisitKind.Warning,
                ArthurVisitMissedEvenings = 1,
                ArthurVisitLastAttemptedDay = 5,
                ArthurCooperationDelivered = true,
                ArthurWarningFollowups = 2,
                ArthurRanOffs = 1,
                ArthurBeatings = 2
            };

        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var serializeResult), serializeResult.Message);
        Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var deserializeResult), deserializeResult.Message);

        Assert.Equal(Chapter3ArthurVisitKind.Warning, envelope!.State!.ArthurVisitPending);
        Assert.Equal(1, envelope.State.ArthurVisitMissedEvenings);
        Assert.Equal(5, envelope.State.ArthurVisitLastAttemptedDay);
        Assert.True(envelope.State.ArthurCooperationDelivered);
        Assert.Equal(2, envelope.State.ArthurWarningFollowups);
        Assert.Equal(1, envelope.State.ArthurRanOffs);
        Assert.Equal(2, envelope.State.ArthurBeatings);
    }

    [Fact]
    public void Schema9_file_loads_with_zero_fell_throughs()
    {
        const string v9 = """
            {
              "schemaVersion": 9,
              "state": {
                "playerId": "76561190000000001",
                "phase": "Active",
                "startElapsedDay": 11,
                "lastProcessedElapsedDay": 25,
                "revision": 9,
                "jobBoard": {
                  "openOffer": null,
                  "activeJob": null,
                  "cooldownUntilDay": null,
                  "lastProcessedDay": 14,
                  "lastTypeOffered": "DeadDrop",
                  "typesSeen": [
                    "DeadDrop"
                  ],
                  "refusalStreak": 0,
                  "nellHintIssued": false,
                  "cooperationVisitIssued": false,
                  "successfulJobs": 6,
                  "successfulJobsSinceWarehousePurchase": 1,
                  "nextOfferId": 4,
                  "nextJobId": 4,
                  "lastDeclinedOfferId": null,
                  "lastExpiredOfferId": null,
                  "lastRefusedOfferId": null,
                  "lastCompletedJob": null,
                  "lastEmergencySupplyOfferId": null
                },
                "ledger": {
                  "entries": [],
                  "lastProcessedDay": -1,
                  "nextId": 1
                },
                "cuts": {
                  "entries": [],
                  "lastProcessedDay": -1
                },
                "presentationReceipts": [],
                "paymentShortfallCount": 0,
                "warehouseOffer": {
                  "openedDay": 7,
                  "lastReminderWeek": 0,
                  "purchased": true,
                  "purchasedDay": 14,
                  "notReadyCount": 0
                }
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v9, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3SaveCodec.CurrentSchemaVersion, envelope!.SchemaVersion);
        Assert.Equal(20, envelope.SchemaVersion);
        Assert.True(envelope.State!.WarehouseOffer!.Purchased);
        Assert.Equal(0, envelope.State.WarehouseOffer.FellThroughCount);
    }

    [Fact]
    public void Schema8_file_loads_with_no_warehouse_offer()
    {
        const string v8 = """
            {
              "schemaVersion": 8,
              "state": {
                "playerId": "76561190000000001",
                "phase": "Active",
                "startElapsedDay": 11,
                "lastProcessedElapsedDay": 15,
                "revision": 4,
                "jobBoard": {
                  "openOffer": null,
                  "activeJob": null,
                  "cooldownUntilDay": null,
                  "lastProcessedDay": 4,
                  "lastTypeOffered": "DeadDrop",
                  "typesSeen": [
                    "DeadDrop"
                  ],
                  "refusalStreak": 0,
                  "nellHintIssued": false,
                  "cooperationVisitIssued": false,
                  "successfulJobs": 5,
                  "successfulJobsSinceWarehousePurchase": 0,
                  "nextOfferId": 3,
                  "nextJobId": 3,
                  "lastDeclinedOfferId": 1,
                  "lastExpiredOfferId": null,
                  "lastRefusedOfferId": null,
                  "lastCompletedJob": null,
                  "lastEmergencySupplyOfferId": null
                },
                "ledger": {
                  "entries": [],
                  "lastProcessedDay": -1,
                  "nextId": 1
                },
                "cuts": {
                  "entries": [],
                  "lastProcessedDay": -1
                },
                "presentationReceipts": [],
                "paymentShortfallCount": 0
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v8, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3SaveCodec.CurrentSchemaVersion, envelope!.SchemaVersion);
        Assert.Equal(20, envelope.SchemaVersion);
        Assert.Null(envelope.State!.WarehouseOffer);
        Assert.Equal(5, envelope.State.JobBoard.SuccessfulJobs);
    }

    [Fact]
    public void Warehouse_offer_round_trip_preserves_open_and_purchased()
    {
        var open = new Chapter3State(PlayerId, Chapter3Phase.Active, 0, 25, 9) with
        {
            WarehouseOffer = new Chapter3WarehouseOffer(18, 1, false, null, 2, 1)
        };
        var bought = new Chapter3State(PlayerId, Chapter3Phase.Active, 0, 30, 12) with
        {
            WarehouseOffer = new Chapter3WarehouseOffer(18, 1, true, 25, 0)
        };

        Assert.True(Chapter3SaveCodec.TrySerialize(open, out var openJson, out var openSerialize), openSerialize.Message);
        Assert.True(Chapter3SaveCodec.TryDeserialize(openJson, out var openEnvelope, out var openDeserialize), openDeserialize.Message);
        Assert.True(Chapter3SaveCodec.TrySerialize(bought, out var boughtJson, out var boughtSerialize), boughtSerialize.Message);
        Assert.True(Chapter3SaveCodec.TryDeserialize(boughtJson, out var boughtEnvelope, out var boughtDeserialize), boughtDeserialize.Message);

        Assert.Equal(open, openEnvelope!.State);
        Assert.Equal(bought, boughtEnvelope!.State);
    }

    [Fact]
    public void Unknown_warehouse_offer_field_is_rejected()
    {
        var state = Chapter3State.CreateInitial(PlayerId) with
        {
            WarehouseOffer = new Chapter3WarehouseOffer(18)
        };
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var tampered = json.Replace("\"openedDay\":", "\"smuggled\": true, \"openedDay\":");

        Assert.False(Chapter3SaveCodec.TryDeserialize(tampered, out _, out var result));

        Assert.Equal(Chapter3CodecFailureReason.UnknownField, result.Reason);
    }

    [Fact]
    public void Board_round_trip_preserves_everything()
    {
        var board = new Chapter3JobBoard(
            new Chapter3JobOffer(3, Chapter3JobType.PackageRecovery, 18_500, 7, 8),
            new Chapter3ActiveJob(2, 2, Chapter3JobType.DeadDrop, 17_500, 4, 6, true,
                new Chapter3DeadDropDelivery(
                    "drop-7", "Drop 7", 1d, 2d, 3d, "best", "Best", "brick", "Brick", 2, true)),
            9, 8, Chapter3JobType.PackageRecovery,
            new[] { Chapter3JobType.DeadDrop, Chapter3JobType.PackageRecovery },
            2, true, false, 3, 0, 4, 3, 6, 5, 4,
            new Chapter3JobCompletion(1, Chapter3JobType.DeadDrop, 15_000));
        var state = new Chapter3State(PlayerId, Chapter3Phase.Active, 11, 19, 9, board);

        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var serialize), serialize.Message);
        Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var deserialize), deserialize.Message);

        Assert.Equal(state, envelope!.State);
    }

    [Fact]
    public void Recovery_round_trip_preserves_everything()
    {
        var job = new Chapter3ActiveJob(2, 2, Chapter3JobType.PackageRecovery, 17_500, 4, 6, false,
            null,
            new Chapter3PackageRecovery(
                new Chapter3RecoveryDrop("pickup-1", "Pickup 1", 1d, 2d, 3d),
                new Chapter3RecoveryDrop("handoff-2", "Handoff 2", 4d, 5d, 6d),
                "best", "Best", "brick", "Brick", 2, 0, true, true, true));
        var state = Chapter3State.CreateInitial(PlayerId) with
        {
            JobBoard = Chapter3JobBoard.Empty with { ActiveJob = job }
        };

        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var serialize), serialize.Message);
        Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var deserialize), deserialize.Message);

        Assert.Equal(state, envelope!.State);
    }

    [Fact]
    public void Emergency_round_trip_preserves_product_window_and_counter()
    {
        var board = new Chapter3JobBoard(
            new Chapter3JobOffer(5, Chapter3JobType.EmergencySupply, 15_500, 9, 10, "cheap", "Cheap"),
            new Chapter3ActiveJob(2, 4, Chapter3JobType.EmergencySupply, 17_500, 7, 9, false,
                new Chapter3DeadDropDelivery(
                    "drop-7", "Drop 7", 1d, 2d, 3d, "cheap", "Cheap", "brick", "Brick", 3),
                null, 1_000d, 1_720d),
            null, 9, Chapter3JobType.EmergencySupply,
            new[] { Chapter3JobType.DeadDrop, Chapter3JobType.PackageRecovery, Chapter3JobType.EmergencySupply },
            0, false, false, 2, 0, 6, 3, null, null, null, null, 5);
        var state = new Chapter3State(PlayerId, Chapter3Phase.Active, 11, 20, 9, board);

        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var serialize), serialize.Message);
        Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var deserialize), deserialize.Message);

        Assert.Equal(state, envelope!.State);
    }

    [Fact]
    public void Schema7_file_loads_without_emergency_fields()
    {
        const string v7 = """
            {
              "schemaVersion": 7,
              "state": {
                "playerId": "76561190000000001",
                "phase": "Active",
                "startElapsedDay": 11,
                "lastProcessedElapsedDay": 15,
                "revision": 4,
                "jobBoard": {
                  "openOffer": {
                    "offerId": 2,
                    "jobType": "PackageRecovery",
                    "reward": 15500,
                    "offeredDay": 3,
                    "respondByDay": 4
                  },
                  "activeJob": null,
                  "cooldownUntilDay": null,
                  "lastProcessedDay": 4,
                  "lastTypeOffered": "PackageRecovery",
                  "typesSeen": [
                    "DeadDrop",
                    "PackageRecovery"
                  ],
                  "refusalStreak": 0,
                  "nellHintIssued": false,
                  "cooperationVisitIssued": false,
                  "successfulJobs": 0,
                  "successfulJobsSinceWarehousePurchase": 0,
                  "nextOfferId": 3,
                  "nextJobId": 2,
                  "lastDeclinedOfferId": null,
                  "lastExpiredOfferId": null,
                  "lastRefusedOfferId": null,
                  "lastCompletedJob": null
                },
                "ledger": {
                  "entries": [],
                  "lastProcessedDay": -1,
                  "nextId": 1
                },
                "cuts": {
                  "entries": [],
                  "lastProcessedDay": -1
                },
                "presentationReceipts": [],
                "paymentShortfallCount": 0
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v7, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3SaveCodec.CurrentSchemaVersion, envelope!.SchemaVersion);
        var board = envelope.State!.JobBoard;
        Assert.Equal(Chapter3JobType.PackageRecovery, board.OpenOffer!.JobType);
        Assert.Null(board.OpenOffer.ProductId);
        Assert.Null(board.OpenOffer.ProductName);
        Assert.Null(board.LastEmergencySupplyOfferId);
    }

    [Fact]
    public void Emergency_keys_under_schema7_are_rejected()
    {
        var state = Chapter3State.ProcessDayTick(
            Chapter3State.CreateInitial(PlayerId), elapsedDays: 11, envelopeSatisfied: true).State;
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var node = JsonNode.Parse(json)!;
        node["state"]!.AsObject().Remove("raid");
        node["state"]!.AsObject().Remove("lastRaidSuccessMinutes");
        node["schemaVersion"] = 7;
        node["state"]!.AsObject().Remove("sweep");
        node["state"]!.AsObject().Remove("lastSweepStartMinutes");
        var backdated = node.ToJsonString();

        Assert.False(Chapter3SaveCodec.TryDeserialize(backdated, out _, out var result));

        Assert.Equal(Chapter3CodecFailureReason.UnknownField, result.Reason);
    }

    [Fact]
    public void Schema6_file_loads_with_an_unfrozen_recovery()
    {
        const string v6 = """
            {
              "schemaVersion": 6,
              "state": {
                "playerId": "76561190000000001",
                "phase": "Active",
                "startElapsedDay": 11,
                "lastProcessedElapsedDay": 15,
                "revision": 4,
                "jobBoard": {
                  "openOffer": null,
                  "activeJob": {
                    "jobId": 2,
                    "offerId": 2,
                    "jobType": "DeadDrop",
                    "reward": 15000,
                    "acceptedDay": 4,
                    "deadlineDay": 6,
                    "extensionUsed": false,
                    "delivery": {
                      "dropGuid": "drop-7",
                      "dropName": "Drop 7",
                      "dropX": 1,
                      "dropY": 2,
                      "dropZ": 3,
                      "productId": "best",
                      "productName": "Best",
                      "packagingId": "brick",
                      "packagingName": "Brick",
                      "brickCount": 2,
                      "removalConfirmed": false
                    }
                  },
                  "cooldownUntilDay": null,
                  "lastProcessedDay": 4,
                  "lastTypeOffered": "DeadDrop",
                  "typesSeen": [
                    "DeadDrop"
                  ],
                  "refusalStreak": 0,
                  "nellHintIssued": false,
                  "cooperationVisitIssued": false,
                  "successfulJobs": 0,
                  "successfulJobsSinceWarehousePurchase": 0,
                  "nextOfferId": 3,
                  "nextJobId": 3,
                  "lastDeclinedOfferId": 1,
                  "lastExpiredOfferId": null,
                  "lastRefusedOfferId": null,
                  "lastCompletedJob": null
                },
                "ledger": {
                  "entries": [],
                  "lastProcessedDay": -1,
                  "nextId": 1
                },
                "cuts": {
                  "entries": [],
                  "lastProcessedDay": -1
                },
                "presentationReceipts": [],
                "paymentShortfallCount": 0
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v6, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3SaveCodec.CurrentSchemaVersion, envelope!.SchemaVersion);
        Assert.Equal(2, envelope.State!.JobBoard.ActiveJob!.JobId);
        Assert.NotNull(envelope.State.JobBoard.ActiveJob.Delivery);
        Assert.Null(envelope.State.JobBoard.ActiveJob.Recovery);
    }

    [Fact]
    public void Unknown_recovery_field_is_rejected()
    {
        var job = new Chapter3ActiveJob(2, 2, Chapter3JobType.PackageRecovery, 15_000, 4, 6, false,
            null,
            new Chapter3PackageRecovery(
                new Chapter3RecoveryDrop("pickup-1", "Pickup 1", 1d, 2d, 3d),
                new Chapter3RecoveryDrop("handoff-2", "Handoff 2", 4d, 5d, 6d),
                "best", "Best", "brick", "Brick", 2));
        var state = Chapter3State.CreateInitial(PlayerId) with
        {
            JobBoard = Chapter3JobBoard.Empty with { ActiveJob = job }
        };
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var tampered = json.Replace("\"stagedSlotIndex\":", "\"smuggled\": true, \"stagedSlotIndex\":");

        Assert.False(Chapter3SaveCodec.TryDeserialize(tampered, out _, out var result));

        Assert.Equal(Chapter3CodecFailureReason.UnknownField, result.Reason);
    }

    [Fact]
    public void Job_sequence_is_stable_across_a_save_round_trip()
    {
        var straight = OfferSequence(reloadAfterDays: null);
        var reloaded = OfferSequence(reloadAfterDays: 4);

        Assert.Equal(straight, reloaded);
    }

    [Fact]
    public void Unknown_job_board_field_is_rejected()
    {
        var state = Chapter3State.ProcessDayTick(
            Chapter3State.CreateInitial(PlayerId), elapsedDays: 11, envelopeSatisfied: true).State;
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var tampered = json.Replace("\"nextJobId\":", "\"smuggled\": true, \"nextJobId\":");

        Assert.False(Chapter3SaveCodec.TryDeserialize(tampered, out _, out var result));

        Assert.Equal(Chapter3CodecFailureReason.UnknownField, result.Reason);
    }

    [Fact]
    public void Schema5_file_loads_with_an_unfrozen_delivery()
    {
        const string v5 = """
            {
              "schemaVersion": 5,
              "state": {
                "playerId": "76561190000000001",
                "phase": "Active",
                "startElapsedDay": 11,
                "lastProcessedElapsedDay": 15,
                "revision": 4,
                "jobBoard": {
                  "openOffer": null,
                  "activeJob": {
                    "jobId": 2,
                    "offerId": 2,
                    "jobType": "DeadDrop",
                    "reward": 15000,
                    "acceptedDay": 4,
                    "deadlineDay": 6,
                    "extensionUsed": false
                  },
                  "cooldownUntilDay": null,
                  "lastProcessedDay": 4,
                  "lastTypeOffered": "DeadDrop",
                  "typesSeen": [
                    "DeadDrop"
                  ],
                  "refusalStreak": 0,
                  "nellHintIssued": false,
                  "cooperationVisitIssued": false,
                  "successfulJobs": 0,
                  "successfulJobsSinceWarehousePurchase": 0,
                  "nextOfferId": 3,
                  "nextJobId": 3,
                  "lastDeclinedOfferId": 1,
                  "lastExpiredOfferId": null
                },
                "ledger": {
                  "entries": [],
                  "lastProcessedDay": -1,
                  "nextId": 1
                },
                "cuts": {
                  "entries": [],
                  "lastProcessedDay": -1
                },
                "presentationReceipts": [],
                "paymentShortfallCount": 0
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v5, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3SaveCodec.CurrentSchemaVersion, envelope!.SchemaVersion);
        Assert.Equal(2, envelope.State!.JobBoard.ActiveJob!.JobId);
        Assert.Null(envelope.State.JobBoard.ActiveJob.Delivery);
        Assert.Null(envelope.State.JobBoard.LastRefusedOfferId);
        Assert.Null(envelope.State.JobBoard.LastCompletedJob);
        Assert.Equal(1, envelope.State.JobBoard.LastDeclinedOfferId);
    }

    [Fact]
    public void Unknown_delivery_field_is_rejected()
    {
        var job = new Chapter3ActiveJob(2, 2, Chapter3JobType.DeadDrop, 15_000, 4, 6, false,
            new Chapter3DeadDropDelivery(
                "drop-7", "Drop 7", 1d, 2d, 3d, "best", "Best", "brick", "Brick", 2));
        var state = Chapter3State.CreateInitial(PlayerId) with
        {
            JobBoard = Chapter3JobBoard.Empty with { ActiveJob = job }
        };
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var tampered = json.Replace("\"brickCount\":", "\"smuggled\": true, \"brickCount\":");

        Assert.False(Chapter3SaveCodec.TryDeserialize(tampered, out _, out var result));

        Assert.Equal(Chapter3CodecFailureReason.UnknownField, result.Reason);
    }

    [Fact]
    public void Unknown_completion_field_is_rejected()
    {
        var state = Chapter3State.CreateInitial(PlayerId) with
        {
            JobBoard = Chapter3JobBoard.Empty with
            {
                LastCompletedJob = new Chapter3JobCompletion(1, Chapter3JobType.DeadDrop, 15_000)
            }
        };
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var replaced = json.Replace(
            "\"lastCompletedJob\": {", "\"lastCompletedJob\": { \"smuggled\": true,");

        Assert.False(Chapter3SaveCodec.TryDeserialize(replaced, out _, out var result));

        Assert.Equal(Chapter3CodecFailureReason.UnknownField, result.Reason);
    }

    private static List<string> OfferSequence(int? reloadAfterDays)
    {
        var state = Chapter3State.CreateInitial(PlayerId);
        var offers = new List<string>();
        for (var elapsed = 11; elapsed < 20; elapsed++)
        {
            var tick = Chapter3State.ProcessDayTick(state, elapsed, envelopeSatisfied: true);
            state = tick.State;
            foreach (var effect in tick.Effects)
            {
                if (effect is Chapter3OfferCreatedEffect created)
                {
                    offers.Add($"{created.Offer.JobType}:{created.Offer.Reward}");
                    state = state with
                    {
                        JobBoard = Chapter3JobScheduler.Refuse(
                            state.JobBoard, created.Offer.OfferId, tick.ChapterDay!.Value).Board
                    };
                }
            }
            if (reloadAfterDays.HasValue && tick.ChapterDay == reloadAfterDays.Value)
            {
                Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
                Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out _));
                state = envelope!.State!;
            }
        }
        return offers;
    }

    private static void AssertBoardEmpty(Chapter3JobBoard board)
    {
        Assert.Null(board.OpenOffer);
        Assert.Null(board.ActiveJob);
        Assert.Null(board.CooldownUntilDay);
        Assert.Equal(-1, board.LastProcessedDay);
        Assert.Null(board.LastTypeOffered);
        Assert.Empty(board.TypesSeen);
        Assert.Equal(0, board.RefusalStreak);
        Assert.False(board.NellHintIssued);
        Assert.False(board.CooperationVisitIssued);
        Assert.Equal(0, board.SuccessfulJobs);
        Assert.Equal(0, board.SuccessfulJobsSinceWarehousePurchase);
        Assert.Equal(1, board.NextOfferId);
        Assert.Equal(1, board.NextJobId);
    }

    [Fact]
    public void Schema1_file_loads_with_an_empty_ledger()
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
        AssertLedgerEmpty(envelope.State!.Ledger);
    }

    [Fact]
    public void Schema2_file_loads_with_an_empty_ledger()
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
                  "openOffer": {
                    "offerId": 1,
                    "jobType": "DeadDrop",
                    "reward": 17500,
                    "offeredDay": 0,
                    "respondByDay": 1
                  },
                  "activeJob": null,
                  "cooldownUntilDay": null,
                  "lastProcessedDay": 0,
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
                }
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v2, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3JobType.DeadDrop, envelope!.State!.JobBoard.OpenOffer!.JobType);
        Assert.Equal(17500, envelope.State.JobBoard.OpenOffer.Reward);
        AssertLedgerEmpty(envelope.State.Ledger);
    }


    [Fact]
    public void Ledger_round_trip_preserves_everything()
    {
        var ledger = new Chapter3MakeGoodLedger(
            new[]
            {
                new Chapter3MakeGood(1, new Chapter3FailedJobSource(2, Chapter3JobType.DeadDrop), 17_000, 5, 7,
                    Chapter3MakeGoodStatus.Open, Chapter3MakeGoodStage.None, null, null),
                new Chapter3MakeGood(2, new Chapter3MissedCutSource(3), 37_500, 4, 6,
                    Chapter3MakeGoodStatus.Overdue, Chapter3MakeGoodStage.None, 7, null),
                new Chapter3MakeGood(3, new Chapter3MissedDemandSource("demand-7"), 20_000, 1, 3,
                    Chapter3MakeGoodStatus.Paid, Chapter3MakeGoodStage.None, 4, 6)
            },
            8, 4);
        var state = new Chapter3State(PlayerId, Chapter3Phase.Active, 11, 19, 9,
            Chapter3JobBoard.Empty, ledger);

        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out var serialize), serialize.Message);
        Assert.True(Chapter3SaveCodec.TryDeserialize(json, out var envelope, out var deserialize), deserialize.Message);

        Assert.Equal(state, envelope!.State);
    }

    [Fact]
    public void Unknown_ledger_field_is_rejected()
    {
        var state = Chapter3State.ProcessDayTick(
            Chapter3State.CreateInitial(PlayerId), elapsedDays: 11, envelopeSatisfied: true).State;
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var tampered = json.Replace("\"nextId\":", "\"smuggled\": true, \"nextId\":");

        Assert.False(Chapter3SaveCodec.TryDeserialize(tampered, out _, out var result));

        Assert.Equal(Chapter3CodecFailureReason.UnknownField, result.Reason);
    }

    [Fact]
    public void Unknown_make_good_source_kind_is_rejected()
    {
        var ledger = Chapter3MakeGoods.OpenForFailedJob(Chapter3MakeGoodLedger.Empty, 2, Chapter3JobType.DeadDrop, 17_000, 5).Ledger;
        var state = Chapter3State.CreateInitial(PlayerId) with { Ledger = ledger };
        Assert.True(Chapter3SaveCodec.TrySerialize(state, out var json, out _));
        var tampered = json.Replace("\"kind\": \"FailedJob\"", "\"kind\": \"Bribe\"");

        Assert.False(Chapter3SaveCodec.TryDeserialize(tampered, out _, out var result));

        Assert.Equal(Chapter3CodecFailureReason.InvalidState, result.Reason);
    }

    private static void AssertLedgerEmpty(Chapter3MakeGoodLedger ledger)
    {
        Assert.Empty(ledger.Entries);
        Assert.Equal(-1, ledger.LastProcessedDay);
        Assert.Equal(1, ledger.NextId);
        Assert.False(ledger.HasOpenMakeGood);
    }

    [Fact]
    public void Schema11_file_loads_with_the_visit_series_and_zero_beatings()
    {
        const string v11 = """
            {
              "schemaVersion": 11,
              "state": {
                "playerId": "76561190000000001",
                "phase": "Active",
                "startElapsedDay": 11,
                "lastProcessedElapsedDay": 25,
                "revision": 9,
                "jobBoard": {
                  "openOffer": null,
                  "activeJob": null,
                  "cooldownUntilDay": null,
                  "lastProcessedDay": 14,
                  "lastTypeOffered": "DeadDrop",
                  "typesSeen": [
                    "DeadDrop"
                  ],
                  "refusalStreak": 0,
                  "nellHintIssued": false,
                  "cooperationVisitIssued": false,
                  "successfulJobs": 6,
                  "successfulJobsSinceWarehousePurchase": 1,
                  "nextOfferId": 4,
                  "nextJobId": 4,
                  "lastDeclinedOfferId": null,
                  "lastExpiredOfferId": null,
                  "lastRefusedOfferId": null,
                  "lastCompletedJob": null,
                  "lastEmergencySupplyOfferId": null
                },
                "ledger": {
                  "entries": [],
                  "lastProcessedDay": -1,
                  "nextId": 1
                },
                "cuts": {
                  "entries": [],
                  "lastProcessedDay": -1
                },
                "presentationReceipts": [],
                "paymentShortfallCount": 0,
                "warehouseOffer": null,
                "arthurVisitPending": "Warning",
                "arthurVisitMissedEvenings": 1,
                "arthurVisitLastAttemptedDay": 5,
                "arthurCooperationDelivered": true,
                "arthurWarningFollowups": 2,
                "arthurRanOffs": 1
              }
            }
            """;

        Assert.True(Chapter3SaveCodec.TryDeserialize(v11, out var envelope, out var result), result.Message);

        Assert.Equal(Chapter3SaveCodec.CurrentSchemaVersion, envelope!.SchemaVersion);
        Assert.Equal(20, envelope.SchemaVersion);
        Assert.Equal(Chapter3ArthurVisitKind.Warning, envelope.State!.ArthurVisitPending);
        Assert.Equal(1, envelope.State.ArthurVisitMissedEvenings);
        Assert.Equal(5, envelope.State.ArthurVisitLastAttemptedDay);
        Assert.True(envelope.State.ArthurCooperationDelivered);
        Assert.Equal(2, envelope.State.ArthurWarningFollowups);
        Assert.Equal(1, envelope.State.ArthurRanOffs);
        Assert.Equal(0, envelope.State.ArthurBeatings);
    }

}
