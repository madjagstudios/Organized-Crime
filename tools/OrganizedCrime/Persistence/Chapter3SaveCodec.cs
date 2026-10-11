using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizedCrime.Model;

namespace OrganizedCrime.Persistence;

public enum Chapter3CodecFailureReason
{
    None, EmptyJson, MalformedJson, InvalidRoot, UnknownField, MissingRequiredField,
    InvalidJsonType, InvalidSchemaVersion, UnsupportedSchema, InvalidState, SerializationFailed
}

public sealed record Chapter3CodecResult(bool Succeeded, Chapter3CodecFailureReason Reason, string Message)
{
    public static Chapter3CodecResult Success() => new(true, Chapter3CodecFailureReason.None, string.Empty);
    public static Chapter3CodecResult Failure(Chapter3CodecFailureReason reason, string message) => new(false, reason, message);
}

public static class Chapter3SaveCodec
{
    public const int CurrentSchemaVersion = 20;
    private static readonly HashSet<int> SupportedReadSchemaVersions = new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, CurrentSchemaVersion };
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(null, allowIntegerValues: false) }
    };
    private static readonly HashSet<string> EnvelopeFields = Set("schemaVersion", "state");
    private static readonly HashSet<string> StateFieldsV1 = Set("playerId", "phase", "startElapsedDay", "lastProcessedElapsedDay", "revision");
    private static readonly HashSet<string> StateFieldsV2 = Set("playerId", "phase", "startElapsedDay", "lastProcessedElapsedDay", "revision", "jobBoard");
    private static readonly HashSet<string> StateFieldsV3 = Set("playerId", "phase", "startElapsedDay", "lastProcessedElapsedDay", "revision", "jobBoard", "ledger");
    private static readonly HashSet<string> StateFieldsV4 = Set("playerId", "phase", "startElapsedDay", "lastProcessedElapsedDay", "revision", "jobBoard", "ledger", "cuts");
    private static readonly HashSet<string> StateFieldsV14 = Set("playerId", "phase", "startElapsedDay", "lastProcessedElapsedDay", "revision", "jobBoard", "ledger", "cuts", "presentationReceipts", "paymentShortfallCount", "warehouseOffer", "arthurVisitPending", "arthurVisitMissedEvenings", "arthurVisitLastAttemptedDay", "arthurCooperationDelivered", "arthurWarningFollowups", "arthurRanOffs", "arthurBeatings", "activeDemand", "madeProductIds", "lastDemandWeek", "nextDemandId", "lastDemandOutcome", "warehouseCapacityTier", "demandsCompleted");
    private static readonly HashSet<string> StateFieldsV15 = Set("playerId", "phase", "startElapsedDay", "lastProcessedElapsedDay", "revision", "jobBoard", "ledger", "cuts", "presentationReceipts", "paymentShortfallCount", "warehouseOffer", "arthurVisitPending", "arthurVisitMissedEvenings", "arthurVisitLastAttemptedDay", "arthurCooperationDelivered", "arthurWarningFollowups", "arthurRanOffs", "arthurBeatings", "activeDemand", "madeProductIds", "lastDemandWeek", "nextDemandId", "lastDemandOutcome", "warehouseCapacityTier", "demandsCompleted", "checkpointTargetCode", "checkpointTargetDay");
    private static readonly HashSet<string> StateFieldsV18 = new(StateFieldsV15, StringComparer.Ordinal) { "sweep", "lastSweepStartMinutes" };
    private static readonly HashSet<string> StateFields = new(StateFieldsV18, StringComparer.Ordinal) { "raid", "lastRaidSuccessMinutes" };
    private static readonly HashSet<string> RaidFields = Set("id", "siteCode", "siteName", "startMinutes", "deadlineMinutes", "totalUnits", "budget", "slots", "completed", "coolingApplied", "noticeReceipt");
    private static readonly HashSet<string> RaidSlotRequiredFields = Set("take", "state");
    private static readonly HashSet<string> RaidSlotFields = new(RaidSlotRequiredFields, StringComparer.Ordinal) { "preFingerprintTotal" };
    private static readonly HashSet<string> RaidTakeFields = Set("identity", "fingerprint", "preQuantity", "packages", "units");
    private static readonly HashSet<string> RaidIdentityFields = Set("storageGuid", "slotIndex");
    private static readonly HashSet<string> SweepFields = Set("id", "targetCode", "plannedStartMinutes", "small", "warningReceipt", "warningMinutes", "activeStartMinutes", "activeEndMinutes");
    private static readonly HashSet<string> StateFieldsV13 = Set("playerId", "phase", "startElapsedDay", "lastProcessedElapsedDay", "revision", "jobBoard", "ledger", "cuts", "presentationReceipts", "paymentShortfallCount", "warehouseOffer", "arthurVisitPending", "arthurVisitMissedEvenings", "arthurVisitLastAttemptedDay", "arthurCooperationDelivered", "arthurWarningFollowups", "arthurRanOffs", "arthurBeatings", "activeDemand", "madeProductIds", "lastDemandWeek", "nextDemandId", "lastDemandOutcome");
    private static readonly HashSet<string> StateFieldsV12 = Set("playerId", "phase", "startElapsedDay", "lastProcessedElapsedDay", "revision", "jobBoard", "ledger", "cuts", "presentationReceipts", "paymentShortfallCount", "warehouseOffer", "arthurVisitPending", "arthurVisitMissedEvenings", "arthurVisitLastAttemptedDay", "arthurCooperationDelivered", "arthurWarningFollowups", "arthurRanOffs", "arthurBeatings");
    private static readonly HashSet<string> StateFieldsV11 = Set("playerId", "phase", "startElapsedDay", "lastProcessedElapsedDay", "revision", "jobBoard", "ledger", "cuts", "presentationReceipts", "paymentShortfallCount", "warehouseOffer", "arthurVisitPending", "arthurVisitMissedEvenings", "arthurVisitLastAttemptedDay", "arthurCooperationDelivered", "arthurWarningFollowups", "arthurRanOffs");
    private static readonly HashSet<string> StateFieldsV10 = Set("playerId", "phase", "startElapsedDay", "lastProcessedElapsedDay", "revision", "jobBoard", "ledger", "cuts", "presentationReceipts", "paymentShortfallCount", "warehouseOffer");
    private static readonly HashSet<string> StateFieldsV8 = Set("playerId", "phase", "startElapsedDay", "lastProcessedElapsedDay", "revision", "jobBoard", "ledger", "cuts", "presentationReceipts", "paymentShortfallCount");
    private static readonly HashSet<string> WarehouseOfferFields = Set("openedDay", "lastReminderWeek", "purchased", "purchasedDay", "notReadyCount", "fellThroughCount");
    private static readonly HashSet<string> WarehouseOfferFieldsV9 = Set("openedDay", "lastReminderWeek", "purchased", "purchasedDay", "notReadyCount");
    private static readonly HashSet<string> JobBoardFieldsV2 = Set(
        "openOffer", "activeJob", "cooldownUntilDay", "lastProcessedDay", "lastTypeOffered", "typesSeen",
        "refusalStreak", "nellHintIssued", "cooperationVisitIssued", "successfulJobs",
        "successfulJobsSinceWarehousePurchase", "nextOfferId", "nextJobId");
    private static readonly HashSet<string> JobBoardFieldsV5 = Set(
        "openOffer", "activeJob", "cooldownUntilDay", "lastProcessedDay", "lastTypeOffered", "typesSeen",
        "refusalStreak", "nellHintIssued", "cooperationVisitIssued", "successfulJobs",
        "successfulJobsSinceWarehousePurchase", "nextOfferId", "nextJobId",
        "lastDeclinedOfferId", "lastExpiredOfferId");
    private static readonly HashSet<string> JobBoardFields = Set(
        "openOffer", "activeJob", "cooldownUntilDay", "lastProcessedDay", "lastTypeOffered", "typesSeen",
        "refusalStreak", "nellHintIssued", "cooperationVisitIssued", "successfulJobs",
        "successfulJobsSinceWarehousePurchase", "nextOfferId", "nextJobId",
        "lastDeclinedOfferId", "lastExpiredOfferId", "lastRefusedOfferId", "lastCompletedJob",
        "lastEmergencySupplyOfferId");
    private static readonly HashSet<string> JobBoardFieldsV7 = Set(
        "openOffer", "activeJob", "cooldownUntilDay", "lastProcessedDay", "lastTypeOffered", "typesSeen",
        "refusalStreak", "nellHintIssued", "cooperationVisitIssued", "successfulJobs",
        "successfulJobsSinceWarehousePurchase", "nextOfferId", "nextJobId",
        "lastDeclinedOfferId", "lastExpiredOfferId", "lastRefusedOfferId", "lastCompletedJob");
    private static readonly HashSet<string> OfferFieldsV16 = Set("offerId", "jobType", "reward", "offeredDay", "respondByDay", "productId", "productName");
    private static readonly HashSet<string> OfferFieldsV7 = Set("offerId", "jobType", "reward", "offeredDay", "respondByDay");
    private static readonly HashSet<string> JobFieldsV5 = Set("jobId", "offerId", "jobType", "reward", "acceptedDay", "deadlineDay", "extensionUsed");
    private static readonly HashSet<string> JobFieldsV6 = Set("jobId", "offerId", "jobType", "reward", "acceptedDay", "deadlineDay", "extensionUsed", "delivery");
    private static readonly HashSet<string> JobFieldsV7 = Set("jobId", "offerId", "jobType", "reward", "acceptedDay", "deadlineDay", "extensionUsed", "delivery", "recovery");
    private static readonly HashSet<string> JobFieldsV16 = Set("jobId", "offerId", "jobType", "reward", "acceptedDay", "deadlineDay", "extensionUsed", "delivery", "recovery", "acceptedGameMinutes", "expiresGameMinutes");
    private static readonly HashSet<string> OfferFieldsV17 = new(OfferFieldsV16, StringComparer.Ordinal) { "storageRun" };
    private static readonly HashSet<string> JobFieldsV17 = new(JobFieldsV16, StringComparer.Ordinal) { "storageRun" };
    private static readonly HashSet<string> OfferFieldsV19 = new(OfferFieldsV17, StringComparer.Ordinal) { "cashMovement" };
    private static readonly HashSet<string> OfferFields = new(OfferFieldsV19, StringComparer.Ordinal) { "shutdown" };
    private static readonly HashSet<string> JobFieldsV19 = new(JobFieldsV17, StringComparer.Ordinal) { "cashMovement" };
    private static readonly HashSet<string> JobFields = new(JobFieldsV19, StringComparer.Ordinal) { "shutdown" };
    private static readonly HashSet<string> ShutdownFields = Set("propertyCode", "propertyName", "quietSince", "breachSince", "breachObservations", "quietComplete", "creditBefore", "creditAmbiguous", "paid");
    private static readonly HashSet<string> CashFields = Set("advance", "plan", "confirmedSlots", "pendingDecrement", "ambiguous", "creditBefore", "paid");
    private static readonly HashSet<string> StorageFields = Set("route", "holdMinutes", "stowedAt", "closetGuid", "missingSince", "released", "pendingSlot", "pendingPreQuantity", "creditBefore");
    private static readonly HashSet<string> StorageRouteFields = Set("pickup", "handoff", "productId", "productName", "packagingId", "packagingName", "brickCount", "stagedSlotIndex", "staged", "custody", "removalConfirmed");
    private static readonly HashSet<string> StorageDropFields = Set("guid", "name", "x", "y", "z");
    private static readonly HashSet<string> DeliveryFields = Set(
        "dropGuid", "dropName", "dropX", "dropY", "dropZ", "productId", "productName",
        "packagingId", "packagingName", "brickCount", "removalConfirmed");
    private static readonly HashSet<string> RecoveryFields = Set(
        "pickupGuid", "pickupName", "pickupX", "pickupY", "pickupZ",
        "handoffGuid", "handoffName", "handoffX", "handoffY", "handoffZ",
        "productId", "productName", "packagingId", "packagingName", "brickCount",
        "stagedSlotIndex", "staged", "custody", "removalConfirmed");
    private static readonly HashSet<string> CompletionFieldsV17 = Set("jobId", "jobType", "reward");
    private static readonly HashSet<string> CompletionFieldsV19 = new(CompletionFieldsV17, StringComparer.Ordinal) { "cashMovement" };
    private static readonly HashSet<string> CompletionFields = new(CompletionFieldsV19, StringComparer.Ordinal) { "shutdown" };
    private static readonly HashSet<string> LedgerFields = Set("entries", "lastProcessedDay", "nextId");
    private static readonly HashSet<string> MakeGoodFields = Set(
        "id", "source", "amount", "openedDay", "dueDay", "status", "stage",
        "warningDeliveredDay", "beatingDeliveredDay");
    private static readonly HashSet<string> FailedJobSourceFields = Set("kind", "jobId", "jobType");
    private static readonly HashSet<string> MissedCutSourceFields = Set("kind", "weekIndex");
    private static readonly HashSet<string> MissedDemandSourceFields = Set("kind", "demandId");
    private static readonly HashSet<string> CutsFields = Set("entries", "lastProcessedDay");
    private static readonly HashSet<string> CutFields = Set("weekIndex", "amount", "dueDay", "graceUntilDay", "status");
    private static readonly HashSet<string> ReceiptFields = Set("correlationId", "revision");
    private static readonly HashSet<string> DemandFields = Set(
        "demandId", "productId", "productName", "productType", "quantity", "unitValue",
        "issueDay", "dueDay", "freshCount", "delivered", "paidTotal", "lastTakeUnits",
        "handOverCount", "hqDoorX", "hqDoorY", "hqDoorZ", "pendingRemoval");
    private static readonly HashSet<string> DemandPendingRemovalFields = Set("slotIndex", "preQuantity", "packageCount", "units");
    private static readonly HashSet<string> DemandOutcomeFields = Set("demandId", "productName", "leftUnits", "amount", "completed");

    public static bool TrySerialize(Chapter3SaveEnvelope? envelope, out string json, out Chapter3CodecResult result)
    {
        json = string.Empty;
        if (envelope is null || envelope.SchemaVersion != CurrentSchemaVersion)
        {
            result = Chapter3CodecResult.Failure(Chapter3CodecFailureReason.UnsupportedSchema, "Chapter 3 envelope schema was unsupported.");
            return false;
        }
        try
        {
            envelope.State?.Validate();
            var dto = new EnvelopeDto
            {
                SchemaVersion = CurrentSchemaVersion,
                State = envelope.State is null ? null : ToDto(envelope.State)
            };
            json = JsonSerializer.Serialize(dto, Options);
            result = Chapter3CodecResult.Success();
            return true;
        }
        catch (ArgumentException ex)
        {
            result = Chapter3CodecResult.Failure(Chapter3CodecFailureReason.InvalidState, ex.Message);
            return false;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            result = Chapter3CodecResult.Failure(Chapter3CodecFailureReason.SerializationFailed, ex.Message);
            return false;
        }
    }

    public static bool TrySerialize(Chapter3State? state, out string json, out Chapter3CodecResult result) =>
        TrySerialize(new Chapter3SaveEnvelope(CurrentSchemaVersion, state), out json, out result);

    public static bool TryDeserialize(string? json, out Chapter3SaveEnvelope? envelope, out Chapter3CodecResult result)
    {
        envelope = null;
        if (string.IsNullOrWhiteSpace(json)) { result = Chapter3CodecResult.Failure(Chapter3CodecFailureReason.EmptyJson, "Chapter 3 sidecar JSON was empty."); return false; }
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidRoot, "Chapter 3 sidecar root was not an object.");
            if (!ValidateFields(document.RootElement, EnvelopeFields, out result)) return false;
            if (!RequireFields(document.RootElement, EnvelopeFields, out result)) return false;
            if (!document.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version)) return Fail(out result, Chapter3CodecFailureReason.InvalidSchemaVersion, "schemaVersion was invalid.");
            if (!SupportedReadSchemaVersions.Contains(version)) return Fail(out result, Chapter3CodecFailureReason.UnsupportedSchema, "Chapter 3 sidecar schema was unsupported.");
            var dto = JsonSerializer.Deserialize<EnvelopeDto>(json, Options);
            if (dto is null || dto.SchemaVersion != version) return Fail(out result, Chapter3CodecFailureReason.InvalidState, "Chapter 3 envelope was invalid.");
            if (dto.State is null) { envelope = Chapter3SaveEnvelope.CreateEmpty(); result = Chapter3CodecResult.Success(); return true; }
            var stateElement = document.RootElement.GetProperty("state");
            if (stateElement.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 state was not an object.");
            // Schema 1 files predate the job board; schemas 1 and 2 predate the ledger;
            // schemas 1 to 3 predate the weekly cuts; schemas 1 to 5 predate the
            // frozen delivery, the refusal marker and the completion record, so
            // their jobs load unfrozen with no markers. Schemas 1 to 6 predate
            // the frozen recovery, so their jobs load with no recovery. Schemas
            // 1 to 7 predate Emergency Supply, so their offers name no product,
            // their jobs carry no window, and the emergency counter is unknown;
            // the nominal value was always the reward field, so it needs no
            // migration of its own. Schemas 1 to 8 predate the warehouse offer,
            // so their offer is unopened. Schema 9 predates the build
            // fell-through count, so its offer never fell through.
            // Schemas 1 to 10 predate Arthur's visit series, so their
            // pending kind is none, their missed evenings are zero, and
            // their cooperation visit is undelivered.
            // Schemas 1 to 11 predate Arthur's beating count, so no
            // beating text is owed. Schemas 1 to 12 predate production
            // demands, so no demand is open, nothing was made in the
            // warehouse, no demand week passed, and no demand closed.
            // Schemas 1 to 13 predate the warehouse capacity tiers, so
            // their tier is 1 and their completed count is what the last
            // outcome still shows: one when it completed, else zero.
            // Schemas 1 to 14 predate the checkpoint target, so no
            // target is picked until the next daily pick.
            var expectedStateFields = version >= 19 ? StateFields : version >= 16 ? StateFieldsV18 : version >= 15 ? StateFieldsV15 : version >= 14 ? StateFieldsV14 : version >= 13 ? StateFieldsV13 : version >= 12 ? StateFieldsV12 : version >= 11 ? StateFieldsV11 : version >= 9 ? StateFieldsV10 : version == 1 ? StateFieldsV1 : version == 2 ? StateFieldsV2 : version == 3 ? StateFieldsV3 : version == 4 ? StateFieldsV4 : StateFieldsV8;
            if (!ValidateFields(stateElement, expectedStateFields, out result)) return false;
            if (!RequireFields(stateElement, version >= 16 ? StateFieldsV15 : expectedStateFields, out result)) return false;
            if (version >= 2 && !ValidateJobBoard(stateElement, version, out result)) return false;
            if (version >= 3 && !ValidateLedger(stateElement, out result)) return false;
            if (version >= 4 && !ValidateCuts(stateElement, out result)) return false;
            if (version >= 5 && !ValidateReceipts(stateElement, out result)) return false;
            if (version >= 9 && !ValidateNested(stateElement, "warehouseOffer", version >= 10 ? WarehouseOfferFields : WarehouseOfferFieldsV9, out result)) return false;
            if (version >= 13 && !ValidateDemand(stateElement, out result)) return false;
            // New nullable sweep fields may be omitted in current-version fixtures.
            // Older versions reject them and migrate with no sweep or cooldown.
            if (version >= 16 && !ValidateNested(stateElement, "sweep", SweepFields, out result)) return false;
            if (version >= 19 && !ValidateRaid(stateElement, out result)) return false;
            var state = FromDto(dto.State);
            state.Validate();
            envelope = new Chapter3SaveEnvelope(CurrentSchemaVersion, state);
            result = Chapter3CodecResult.Success();
            return true;
        }
        catch (JsonException ex) { result = Chapter3CodecResult.Failure(Chapter3CodecFailureReason.MalformedJson, ex.Message); return false; }
        catch (ArgumentException ex) { result = Chapter3CodecResult.Failure(Chapter3CodecFailureReason.InvalidState, ex.Message); return false; }
        catch (InvalidOperationException ex) { result = Chapter3CodecResult.Failure(Chapter3CodecFailureReason.InvalidJsonType, ex.Message); return false; }
    }

    private static bool ValidateRaid(JsonElement state, out Chapter3CodecResult result)
    {
        result = Chapter3CodecResult.Success();
        if (!state.TryGetProperty("raid", out var raid) || raid.ValueKind == JsonValueKind.Null) return true;
        if (raid.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Raid must be an object.");
        if (!ValidateFields(raid, RaidFields, out result) || !RequireFields(raid, RaidFields, out result)) return false;
        var slots = raid.GetProperty("slots");
        if (slots.ValueKind != JsonValueKind.Array) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Raid slots must be an array.");
        foreach (var slot in slots.EnumerateArray())
        {
            if (slot.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Raid slot must be an object.");
            if (!ValidateFields(slot, RaidSlotFields, out result) || !RequireFields(slot, RaidSlotRequiredFields, out result)) return false;
            var take = slot.GetProperty("take");
            if (take.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Raid take must be an object.");
            if (!ValidateFields(take, RaidTakeFields, out result) || !RequireFields(take, RaidTakeFields, out result)) return false;
            var identity = take.GetProperty("identity");
            if (identity.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Raid identity must be an object.");
            if (!ValidateFields(identity, RaidIdentityFields, out result) || !RequireFields(identity, RaidIdentityFields, out result)) return false;
        }
        return true;
    }

    private static bool ValidateJobBoard(JsonElement stateElement, int version, out Chapter3CodecResult result)
    {
        var boardElement = stateElement.GetProperty("jobBoard");
        if (boardElement.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 job board was not an object.");
        var expectedBoardFields = version >= 8 ? JobBoardFields : version >= 6 ? JobBoardFieldsV7 : version >= 5 ? JobBoardFieldsV5 : JobBoardFieldsV2;
        if (!ValidateFields(boardElement, expectedBoardFields, out result)) return false;
        if (!RequireFields(boardElement, expectedBoardFields, out result)) return false;
        if (!ValidateNested(boardElement, "openOffer", version >= 20 ? OfferFields : version >= 18 ? OfferFieldsV19 : version >= 17 ? OfferFieldsV17 : version >= 8 ? OfferFieldsV16 : OfferFieldsV7, out result)) return false;
        var expectedJobFields = version >= 20 ? JobFields : version >= 18 ? JobFieldsV19 : version >= 17 ? JobFieldsV17 : version >= 8 ? JobFieldsV16 : version >= 7 ? JobFieldsV7 : version >= 6 ? JobFieldsV6 : JobFieldsV5;
        if (!ValidateNested(boardElement, "activeJob", expectedJobFields, out result)) return false;
        var jobElement = boardElement.GetProperty("activeJob");
        if (jobElement.ValueKind == JsonValueKind.Object &&
            !ValidateNested(jobElement, "delivery", DeliveryFields, out result)) return false;
        if (jobElement.ValueKind == JsonValueKind.Object &&
            !ValidateNested(jobElement, "recovery", RecoveryFields, out result)) return false;
        if (version >= 17 &&
            (!ValidateStorage(boardElement.GetProperty("openOffer"), out result) ||
             !ValidateStorage(jobElement, out result))) return false;
        if (!ValidateNested(boardElement, "lastCompletedJob", version >= 20 ? CompletionFields : version >= 18 ? CompletionFieldsV19 : CompletionFieldsV17, out result)) return false;
        if (version >= 18)
            foreach (var field in new[] { "openOffer", "activeJob", "lastCompletedJob" })
            {
                var owner = boardElement.GetProperty(field);
                if (owner.ValueKind == JsonValueKind.Object && !ValidateNested(owner, "cashMovement", CashFields, out result)) return false;
                if (version >= 20 && owner.ValueKind == JsonValueKind.Object && !ValidateNested(owner, "shutdown", ShutdownFields, out result)) return false;
            }
        if (!boardElement.TryGetProperty("typesSeen", out var seen) || seen.ValueKind != JsonValueKind.Array) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 seen types were not an array.");
        result = Chapter3CodecResult.Success();
        return true;
    }

    private static bool ValidateStorage(JsonElement owner, out Chapter3CodecResult result)
    {
        result = Chapter3CodecResult.Success();
        if (owner.ValueKind != JsonValueKind.Object) return true;
        if (!ValidateNested(owner, "storageRun", StorageFields, out result)) return false;
        if (!owner.TryGetProperty("storageRun", out var run) || run.ValueKind == JsonValueKind.Null) return true;
        if (!ValidateNested(run, "route", StorageRouteFields, out result)) return false;
        var route = run.GetProperty("route");
        if (route.ValueKind != JsonValueKind.Object)
            return Fail(out result, Chapter3CodecFailureReason.InvalidState, "Storage route was missing.");
        return ValidateNested(route, "pickup", StorageDropFields, out result) &&
            ValidateNested(route, "handoff", StorageDropFields, out result);
    }

    private static bool ValidateLedger(JsonElement stateElement, out Chapter3CodecResult result)
    {
        var ledgerElement = stateElement.GetProperty("ledger");
        if (ledgerElement.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 ledger was not an object.");
        if (!ValidateFields(ledgerElement, LedgerFields, out result)) return false;
        if (!RequireFields(ledgerElement, LedgerFields, out result)) return false;
        if (!ledgerElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 make-good entries were not an array.");
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 make-good entry was not an object.");
            if (!ValidateFields(entry, MakeGoodFields, out result)) return false;
            if (!RequireFields(entry, MakeGoodFields, out result)) return false;
            if (!ValidateSource(entry, out result)) return false;
        }
        result = Chapter3CodecResult.Success();
        return true;
    }

    private static bool ValidateCuts(JsonElement stateElement, out Chapter3CodecResult result)
    {
        var cutsElement = stateElement.GetProperty("cuts");
        if (cutsElement.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 cuts were not an object.");
        if (!ValidateFields(cutsElement, CutsFields, out result)) return false;
        if (!RequireFields(cutsElement, CutsFields, out result)) return false;
        if (!cutsElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 cut entries were not an array.");
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 cut entry was not an object.");
            if (!ValidateFields(entry, CutFields, out result)) return false;
            if (!RequireFields(entry, CutFields, out result)) return false;
        }
        result = Chapter3CodecResult.Success();
        return true;
    }

    private static bool ValidateReceipts(JsonElement stateElement, out Chapter3CodecResult result)
    {
        if (!stateElement.TryGetProperty("presentationReceipts", out var receipts) || receipts.ValueKind != JsonValueKind.Array) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 presentation receipts were not an array.");
        foreach (var receipt in receipts.EnumerateArray())
        {
            if (receipt.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 presentation receipt was not an object.");
            if (!ValidateFields(receipt, ReceiptFields, out result)) return false;
            if (!RequireFields(receipt, ReceiptFields, out result)) return false;
        }
        result = Chapter3CodecResult.Success();
        return true;
    }

    private static bool ValidateDemand(JsonElement stateElement, out Chapter3CodecResult result)
    {
        if (!ValidateNested(stateElement, "activeDemand", DemandFields, out result)) return false;
        if (stateElement.TryGetProperty("activeDemand", out var demand) && demand.ValueKind == JsonValueKind.Object &&
            !ValidateNested(demand, "pendingRemoval", DemandPendingRemovalFields, out result)) return false;
        if (!stateElement.TryGetProperty("madeProductIds", out var made) || made.ValueKind != JsonValueKind.Array) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 made products were not an array.");
        foreach (var id in made.EnumerateArray())
        {
            if (id.ValueKind != JsonValueKind.String) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 made product id was not a string.");
        }
        if (!ValidateNested(stateElement, "lastDemandOutcome", DemandOutcomeFields, out result)) return false;
        result = Chapter3CodecResult.Success();
        return true;
    }

    private static bool ValidateSource(JsonElement entry, out Chapter3CodecResult result)
    {
        if (!entry.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 make-good source was not an object.");
        if (!source.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, "Chapter 3 make-good source kind was invalid.");
        var expected = kind.GetString() switch
        {
            "FailedJob" => FailedJobSourceFields,
            "MissedCut" => MissedCutSourceFields,
            "MissedDemand" => MissedDemandSourceFields,
            _ => null
        };
        if (expected is null) return Fail(out result, Chapter3CodecFailureReason.InvalidState, $"Chapter 3 make-good source kind was unknown: {kind}.");
        if (!ValidateFields(source, expected, out result)) return false;
        if (!RequireFields(source, expected, out result)) return false;
        result = Chapter3CodecResult.Success();
        return true;
    }

    private static bool ValidateNested(JsonElement boardElement, string property, HashSet<string> fields, out Chapter3CodecResult result)
    {
        // A missing key predates the nested object: schemas before 6 carry no
        // delivery or completion record, schemas before 7 no recovery. A
        // present key must be null or valid.
        if (!boardElement.TryGetProperty(property, out var nested)) { result = Chapter3CodecResult.Success(); return true; }
        if (nested.ValueKind == JsonValueKind.Null) { result = Chapter3CodecResult.Success(); return true; }
        if (nested.ValueKind != JsonValueKind.Object) return Fail(out result, Chapter3CodecFailureReason.InvalidJsonType, $"Chapter 3 {property} was not an object.");
        if (!ValidateFields(nested, fields, out result)) return false;
        if (!RequireFields(nested, fields, out result)) return false;
        result = Chapter3CodecResult.Success();
        return true;
    }

    private static bool ValidateFields(JsonElement element, HashSet<string> fields, out Chapter3CodecResult result)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!fields.Contains(property.Name) || !seen.Add(property.Name)) return Fail(out result, Chapter3CodecFailureReason.UnknownField, $"Unknown or duplicate field: {property.Name}.");
        }
        result = Chapter3CodecResult.Success(); return true;
    }
    private static bool RequireFields(JsonElement element, HashSet<string> fields, out Chapter3CodecResult result)
    {
        foreach (var field in fields)
            if (!element.TryGetProperty(field, out _)) return Fail(out result, Chapter3CodecFailureReason.MissingRequiredField, $"Required field was missing: {field}.");
        result = Chapter3CodecResult.Success(); return true;
    }
    private static bool Fail(out Chapter3CodecResult result, Chapter3CodecFailureReason reason, string message) { result = Chapter3CodecResult.Failure(reason, message); return false; }
    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);

    private static StateDto ToDto(Chapter3State state) => new()
    {
        PlayerId = state.PlayerId, Phase = state.Phase, StartElapsedDay = state.StartElapsedDay,
        LastProcessedElapsedDay = state.LastProcessedElapsedDay, Revision = state.Revision,
        JobBoard = ToBoardDto(state.JobBoard),
        Ledger = ToLedgerDto(state.Ledger),
        Cuts = ToCutsDto(state.Cuts),
        PresentationReceipts = state.PresentationReceipts.Select(receipt => new ReceiptDto { CorrelationId = receipt.CorrelationId, Revision = receipt.Revision }).ToList(),
        PaymentShortfallCount = state.PaymentShortfallCount,
        WarehouseOffer = state.WarehouseOffer is null ? null : new WarehouseOfferDto
        {
            OpenedDay = state.WarehouseOffer.OpenedDay,
            LastReminderWeek = state.WarehouseOffer.LastReminderWeek,
            Purchased = state.WarehouseOffer.Purchased,
            PurchasedDay = state.WarehouseOffer.PurchasedDay,
            NotReadyCount = state.WarehouseOffer.NotReadyCount,
            FellThroughCount = state.WarehouseOffer.FellThroughCount
        },
        ArthurVisitPending = state.ArthurVisitPending,
        ArthurVisitMissedEvenings = state.ArthurVisitMissedEvenings,
        ArthurVisitLastAttemptedDay = state.ArthurVisitLastAttemptedDay,
        ArthurCooperationDelivered = state.ArthurCooperationDelivered,
        ArthurWarningFollowups = state.ArthurWarningFollowups,
        ArthurRanOffs = state.ArthurRanOffs,
        ArthurBeatings = state.ArthurBeatings,
        ActiveDemand = state.ActiveDemand is null ? null : ToDemandDto(state.ActiveDemand),
        MadeProductIds = state.MadeProductIds.ToList(),
        LastDemandWeek = state.LastDemandWeek,
        NextDemandId = state.NextDemandId,
        LastDemandOutcome = state.LastDemandOutcome is null ? null : new DemandOutcomeDto
        {
            DemandId = state.LastDemandOutcome.DemandId,
            ProductName = state.LastDemandOutcome.ProductName,
            LeftUnits = state.LastDemandOutcome.LeftUnits,
            Amount = state.LastDemandOutcome.Amount,
            Completed = state.LastDemandOutcome.Completed
        },
        WarehouseCapacityTier = state.WarehouseCapacityTier,
        DemandsCompleted = state.DemandsCompleted,
        CheckpointTargetCode = state.CheckpointTargetCode,
        CheckpointTargetDay = state.CheckpointTargetDay,
        Sweep = state.Sweep,
        LastSweepStartMinutes = state.LastSweepStartMinutes,
        Raid = state.Raid is null ? null : JsonSerializer.SerializeToElement(state.Raid, Options),
        LastRaidSuccessMinutes = state.LastRaidSuccessMinutes
    };
    private static Chapter3State FromDto(StateDto dto) => new Chapter3State(
        dto.PlayerId!, dto.Phase, dto.StartElapsedDay, dto.LastProcessedElapsedDay, dto.Revision,
        dto.JobBoard is null ? Chapter3JobBoard.Empty : FromBoardDto(dto.JobBoard),
        dto.Ledger is null ? Chapter3MakeGoodLedger.Empty : FromLedgerDto(dto.Ledger),
        dto.Cuts is null ? Chapter3WeeklyCuts.Empty : FromCutsDto(dto.Cuts),
        dto.PresentationReceipts is null ? new List<Chapter3PresentationReceipt>() : dto.PresentationReceipts.Select(receipt => new Chapter3PresentationReceipt(receipt.CorrelationId!, receipt.Revision)).ToList(),
        dto.PaymentShortfallCount,
        dto.WarehouseOffer is null ? null : new Chapter3WarehouseOffer(
            dto.WarehouseOffer.OpenedDay, dto.WarehouseOffer.LastReminderWeek, dto.WarehouseOffer.Purchased,
            dto.WarehouseOffer.PurchasedDay, dto.WarehouseOffer.NotReadyCount, dto.WarehouseOffer.FellThroughCount),
        dto.ArthurVisitPending,
        dto.ArthurVisitMissedEvenings,
        dto.ArthurVisitLastAttemptedDay ?? -1,
        dto.ArthurCooperationDelivered,
        dto.ArthurWarningFollowups,
        dto.ArthurRanOffs,
        dto.ArthurBeatings,
        dto.ActiveDemand is null ? null : FromDemandDto(dto.ActiveDemand),
        dto.MadeProductIds ?? new List<string>(),
        dto.LastDemandWeek ?? -1,
        dto.NextDemandId ?? 1,
        dto.LastDemandOutcome is null ? null : new Chapter3DemandOutcome(
            dto.LastDemandOutcome.DemandId!,
            dto.LastDemandOutcome.ProductName!,
            dto.LastDemandOutcome.LeftUnits,
            dto.LastDemandOutcome.Amount,
            dto.LastDemandOutcome.Completed),
        dto.WarehouseCapacityTier ?? 1,
        dto.DemandsCompleted ?? (dto.LastDemandOutcome?.Completed == true ? 1 : 0),
        dto.CheckpointTargetCode,
        dto.CheckpointTargetDay,
        dto.Sweep,
        dto.LastSweepStartMinutes) { Raid = dto.Raid is { ValueKind: not JsonValueKind.Null } raid ? raid.Deserialize<Chapter3RaidJournal>(Options) : null, LastRaidSuccessMinutes = dto.LastRaidSuccessMinutes };

    private static DemandDto ToDemandDto(Chapter3ProductionDemand demand) => new()
    {
        DemandId = demand.DemandId,
        ProductId = demand.ProductId,
        ProductName = demand.ProductName,
        ProductType = demand.ProductType,
        Quantity = demand.Quantity,
        UnitValue = demand.UnitValue,
        IssueDay = demand.IssueDay,
        DueDay = demand.DueDay,
        FreshCount = demand.FreshCount,
        Delivered = demand.Delivered,
        PaidTotal = demand.PaidTotal,
        LastTakeUnits = demand.LastTakeUnits,
        HandOverCount = demand.HandOverCount,
        HqDoorX = demand.HqDoorX,
        HqDoorY = demand.HqDoorY,
        HqDoorZ = demand.HqDoorZ,
        PendingRemoval = demand.PendingRemoval is null ? null : new DemandPendingRemovalDto
        {
            SlotIndex = demand.PendingRemoval.SlotIndex,
            PreQuantity = demand.PendingRemoval.PreQuantity,
            PackageCount = demand.PendingRemoval.PackageCount,
            Units = demand.PendingRemoval.Units
        }
    };

    private static Chapter3ProductionDemand FromDemandDto(DemandDto dto) => new(
        dto.DemandId!,
        dto.ProductId!,
        dto.ProductName!,
        dto.ProductType,
        dto.Quantity,
        dto.UnitValue,
        dto.IssueDay,
        dto.DueDay,
        dto.FreshCount,
        dto.Delivered,
        dto.PaidTotal,
        dto.LastTakeUnits,
        dto.HandOverCount,
        dto.HqDoorX,
        dto.HqDoorY,
        dto.HqDoorZ,
        dto.PendingRemoval is null ? null : new Chapter3DemandPendingRemoval(
            dto.PendingRemoval.SlotIndex,
            dto.PendingRemoval.PreQuantity,
            dto.PendingRemoval.PackageCount,
            dto.PendingRemoval.Units));

    private static JobBoardDto ToBoardDto(Chapter3JobBoard board) => new()
    {
        OpenOffer = board.OpenOffer is null ? null : new OfferDto
        {
            OfferId = board.OpenOffer.OfferId, JobType = board.OpenOffer.JobType,
            Reward = board.OpenOffer.Reward, OfferedDay = board.OpenOffer.OfferedDay,
            RespondByDay = board.OpenOffer.RespondByDay,
            ProductId = board.OpenOffer.ProductId, ProductName = board.OpenOffer.ProductName,
            StorageRun = board.OpenOffer.StorageRun,
            Shutdown = board.OpenOffer.Shutdown,
            CashMovement = board.OpenOffer.CashMovement
        },
        ActiveJob = board.ActiveJob is null ? null : new JobDto
        {
            JobId = board.ActiveJob.JobId, OfferId = board.ActiveJob.OfferId, JobType = board.ActiveJob.JobType,
            Reward = board.ActiveJob.Reward, AcceptedDay = board.ActiveJob.AcceptedDay,
            DeadlineDay = board.ActiveJob.DeadlineDay, ExtensionUsed = board.ActiveJob.ExtensionUsed,
            AcceptedGameMinutes = board.ActiveJob.AcceptedGameMinutes,
            ExpiresGameMinutes = board.ActiveJob.ExpiresGameMinutes,
            StorageRun = board.ActiveJob.StorageRun,
            Shutdown = board.ActiveJob.Shutdown,
            CashMovement = board.ActiveJob.CashMovement,
            Delivery = board.ActiveJob.Delivery is null ? null : new DeliveryDto
            {
                DropGuid = board.ActiveJob.Delivery.DropGuid, DropName = board.ActiveJob.Delivery.DropName,
                DropX = board.ActiveJob.Delivery.DropX, DropY = board.ActiveJob.Delivery.DropY,
                DropZ = board.ActiveJob.Delivery.DropZ, ProductId = board.ActiveJob.Delivery.ProductId,
                ProductName = board.ActiveJob.Delivery.ProductName,
                PackagingId = board.ActiveJob.Delivery.PackagingId,
                PackagingName = board.ActiveJob.Delivery.PackagingName,
                BrickCount = board.ActiveJob.Delivery.BrickCount,
                RemovalConfirmed = board.ActiveJob.Delivery.RemovalConfirmed
            },
            Recovery = board.ActiveJob.Recovery is null ? null : new RecoveryDto
            {
                PickupGuid = board.ActiveJob.Recovery.Pickup.Guid, PickupName = board.ActiveJob.Recovery.Pickup.Name,
                PickupX = board.ActiveJob.Recovery.Pickup.X, PickupY = board.ActiveJob.Recovery.Pickup.Y,
                PickupZ = board.ActiveJob.Recovery.Pickup.Z,
                HandoffGuid = board.ActiveJob.Recovery.Handoff.Guid, HandoffName = board.ActiveJob.Recovery.Handoff.Name,
                HandoffX = board.ActiveJob.Recovery.Handoff.X, HandoffY = board.ActiveJob.Recovery.Handoff.Y,
                HandoffZ = board.ActiveJob.Recovery.Handoff.Z, ProductId = board.ActiveJob.Recovery.ProductId,
                ProductName = board.ActiveJob.Recovery.ProductName,
                PackagingId = board.ActiveJob.Recovery.PackagingId,
                PackagingName = board.ActiveJob.Recovery.PackagingName,
                BrickCount = board.ActiveJob.Recovery.BrickCount,
                StagedSlotIndex = board.ActiveJob.Recovery.StagedSlotIndex,
                Staged = board.ActiveJob.Recovery.Staged,
                Custody = board.ActiveJob.Recovery.Custody,
                RemovalConfirmed = board.ActiveJob.Recovery.RemovalConfirmed
            }
        },
        CooldownUntilDay = board.CooldownUntilDay,
        LastProcessedDay = board.LastProcessedDay,
        LastTypeOffered = board.LastTypeOffered,
        TypesSeen = board.TypesSeen.ToList(),
        RefusalStreak = board.RefusalStreak,
        NellHintIssued = board.NellHintIssued,
        CooperationVisitIssued = board.CooperationVisitIssued,
        SuccessfulJobs = board.SuccessfulJobs,
        SuccessfulJobsSinceWarehousePurchase = board.SuccessfulJobsSinceWarehousePurchase,
        NextOfferId = board.NextOfferId,
        LastDeclinedOfferId = board.LastDeclinedOfferId,
        LastExpiredOfferId = board.LastExpiredOfferId,
        LastRefusedOfferId = board.LastRefusedOfferId,
        LastCompletedJob = board.LastCompletedJob is null ? null : new CompletionDto
        {
            JobId = board.LastCompletedJob.JobId, JobType = board.LastCompletedJob.JobType,
            Reward = board.LastCompletedJob.Reward,
            Shutdown = board.LastCompletedJob.Shutdown,
            CashMovement = board.LastCompletedJob.CashMovement
        },
        LastEmergencySupplyOfferId = board.LastEmergencySupplyOfferId,
        NextJobId = board.NextJobId
    };

    private static Chapter3JobBoard FromBoardDto(JobBoardDto dto) => new(
        dto.OpenOffer is null ? null : new Chapter3JobOffer(
            dto.OpenOffer.OfferId, dto.OpenOffer.JobType, dto.OpenOffer.Reward,
            dto.OpenOffer.OfferedDay, dto.OpenOffer.RespondByDay,
            dto.OpenOffer.ProductId, dto.OpenOffer.ProductName) { StorageRun = dto.OpenOffer.StorageRun, CashMovement = dto.OpenOffer.CashMovement, Shutdown = dto.OpenOffer.Shutdown },
        dto.ActiveJob is null ? null : new Chapter3ActiveJob(
            dto.ActiveJob.JobId, dto.ActiveJob.OfferId, dto.ActiveJob.JobType, dto.ActiveJob.Reward,
            dto.ActiveJob.AcceptedDay, dto.ActiveJob.DeadlineDay, dto.ActiveJob.ExtensionUsed,
            dto.ActiveJob.Delivery is null ? null : new Chapter3DeadDropDelivery(
                dto.ActiveJob.Delivery.DropGuid!, dto.ActiveJob.Delivery.DropName!,
                dto.ActiveJob.Delivery.DropX, dto.ActiveJob.Delivery.DropY, dto.ActiveJob.Delivery.DropZ,
                dto.ActiveJob.Delivery.ProductId!, dto.ActiveJob.Delivery.ProductName!,
                dto.ActiveJob.Delivery.PackagingId!, dto.ActiveJob.Delivery.PackagingName!,
                dto.ActiveJob.Delivery.BrickCount, dto.ActiveJob.Delivery.RemovalConfirmed),
            dto.ActiveJob.Recovery is null ? null : new Chapter3PackageRecovery(
                new Chapter3RecoveryDrop(
                    dto.ActiveJob.Recovery.PickupGuid!, dto.ActiveJob.Recovery.PickupName!,
                    dto.ActiveJob.Recovery.PickupX, dto.ActiveJob.Recovery.PickupY, dto.ActiveJob.Recovery.PickupZ),
                new Chapter3RecoveryDrop(
                    dto.ActiveJob.Recovery.HandoffGuid!, dto.ActiveJob.Recovery.HandoffName!,
                    dto.ActiveJob.Recovery.HandoffX, dto.ActiveJob.Recovery.HandoffY, dto.ActiveJob.Recovery.HandoffZ),
                dto.ActiveJob.Recovery.ProductId!, dto.ActiveJob.Recovery.ProductName!,
                dto.ActiveJob.Recovery.PackagingId!, dto.ActiveJob.Recovery.PackagingName!,
                dto.ActiveJob.Recovery.BrickCount, dto.ActiveJob.Recovery.StagedSlotIndex,
                dto.ActiveJob.Recovery.Staged, dto.ActiveJob.Recovery.Custody,
                dto.ActiveJob.Recovery.RemovalConfirmed),
            dto.ActiveJob.AcceptedGameMinutes, dto.ActiveJob.ExpiresGameMinutes) { StorageRun = dto.ActiveJob.StorageRun, CashMovement = dto.ActiveJob.CashMovement, Shutdown = dto.ActiveJob.Shutdown },
        dto.CooldownUntilDay,
        dto.LastProcessedDay,
        dto.LastTypeOffered,
        dto.TypesSeen ?? new List<Chapter3JobType>(),
        dto.RefusalStreak,
        dto.NellHintIssued,
        dto.CooperationVisitIssued,
        dto.SuccessfulJobs,
        dto.SuccessfulJobsSinceWarehousePurchase,
        dto.NextOfferId,
        dto.NextJobId,
        dto.LastDeclinedOfferId,
        dto.LastExpiredOfferId,
        dto.LastRefusedOfferId,
        dto.LastCompletedJob is null ? null : new Chapter3JobCompletion(
            dto.LastCompletedJob.JobId, dto.LastCompletedJob.JobType, dto.LastCompletedJob.Reward) { CashMovement = dto.LastCompletedJob.CashMovement, Shutdown = dto.LastCompletedJob.Shutdown },
        dto.LastEmergencySupplyOfferId);

    private static LedgerDto ToLedgerDto(Chapter3MakeGoodLedger ledger) => new()
    {
        Entries = ledger.Entries.Select(entry => new MakeGoodDto
        {
            Id = entry.Id,
            Source = ToSourceDto(entry.Source),
            Amount = entry.Amount,
            OpenedDay = entry.OpenedDay,
            DueDay = entry.DueDay,
            Status = entry.Status,
            Stage = entry.Stage,
            WarningDeliveredDay = entry.WarningDeliveredDay,
            BeatingDeliveredDay = entry.BeatingDeliveredDay
        }).ToList(),
        LastProcessedDay = ledger.LastProcessedDay,
        NextId = ledger.NextId
    };

    private static SourceDto ToSourceDto(Chapter3MakeGoodSource source) => source switch
    {
        Chapter3FailedJobSource failed => new SourceDto { Kind = "FailedJob", JobId = failed.JobId, JobType = failed.JobType },
        Chapter3MissedCutSource cut => new SourceDto { Kind = "MissedCut", WeekIndex = cut.WeekIndex },
        Chapter3MissedDemandSource demand => new SourceDto { Kind = "MissedDemand", DemandId = demand.DemandId },
        _ => throw new ArgumentException($"Chapter 3 make-good source was unknown: {source.GetType().Name}.", nameof(source))
    };

    private static Chapter3MakeGoodLedger FromLedgerDto(LedgerDto dto) => new(
        dto.Entries is null
            ? new List<Chapter3MakeGood>()
            : dto.Entries.Select(entry => new Chapter3MakeGood(
                entry.Id,
                FromSourceDto(entry.Source),
                entry.Amount,
                entry.OpenedDay,
                entry.DueDay,
                entry.Status,
                entry.Stage,
                entry.WarningDeliveredDay,
                entry.BeatingDeliveredDay)).ToList(),
        dto.LastProcessedDay,
        dto.NextId);

    private static Chapter3MakeGoodSource FromSourceDto(SourceDto? dto) => dto?.Kind switch
    {
        "FailedJob" => new Chapter3FailedJobSource(
            dto.JobId ?? throw new ArgumentException("Chapter 3 make-good job id was missing.", nameof(dto)),
            dto.JobType ?? throw new ArgumentException("Chapter 3 make-good job type was missing.", nameof(dto))),
        "MissedCut" => new Chapter3MissedCutSource(
            dto.WeekIndex ?? throw new ArgumentException("Chapter 3 make-good week index was missing.", nameof(dto))),
        "MissedDemand" => new Chapter3MissedDemandSource(
            dto.DemandId ?? throw new ArgumentException("Chapter 3 make-good demand id was missing.", nameof(dto))),
        _ => throw new ArgumentException($"Chapter 3 make-good source kind was unknown: {dto?.Kind}.", nameof(dto))
    };

    private static CutsDto ToCutsDto(Chapter3WeeklyCuts cuts) => new()
    {
        Entries = cuts.Entries.Select(entry => new CutDto
        {
            WeekIndex = entry.WeekIndex,
            Amount = entry.Amount,
            DueDay = entry.DueDay,
            GraceUntilDay = entry.GraceUntilDay,
            Status = entry.Status
        }).ToList(),
        LastProcessedDay = cuts.LastProcessedDay
    };

    private static Chapter3WeeklyCuts FromCutsDto(CutsDto dto) => new(
        dto.Entries is null
            ? new List<Chapter3WeeklyCut>()
            : dto.Entries.Select(entry => new Chapter3WeeklyCut(
                entry.WeekIndex,
                entry.Amount,
                entry.DueDay,
                entry.GraceUntilDay,
                entry.Status)).ToList(),
        dto.LastProcessedDay);

    private sealed class EnvelopeDto { public int SchemaVersion { get; set; } public StateDto? State { get; set; } }
    private sealed class StateDto
    {
        public string? PlayerId { get; set; }
        public Chapter3Phase Phase { get; set; }
        public int? StartElapsedDay { get; set; }
        public int LastProcessedElapsedDay { get; set; }
        public long Revision { get; set; }
        public JobBoardDto? JobBoard { get; set; }
        public LedgerDto? Ledger { get; set; }
        public CutsDto? Cuts { get; set; }
        public List<ReceiptDto>? PresentationReceipts { get; set; }
        public int PaymentShortfallCount { get; set; }
        public WarehouseOfferDto? WarehouseOffer { get; set; }
        public Chapter3ArthurVisitKind ArthurVisitPending { get; set; }
        public int ArthurVisitMissedEvenings { get; set; }
        public int? ArthurVisitLastAttemptedDay { get; set; }
        public bool ArthurCooperationDelivered { get; set; }
        public int ArthurWarningFollowups { get; set; }
        public int ArthurRanOffs { get; set; }
        public int ArthurBeatings { get; set; }
        public DemandDto? ActiveDemand { get; set; }
        public List<string>? MadeProductIds { get; set; }
        public int? LastDemandWeek { get; set; }
        public int? NextDemandId { get; set; }
        public DemandOutcomeDto? LastDemandOutcome { get; set; }
        public int? WarehouseCapacityTier { get; set; }
        public int? DemandsCompleted { get; set; }
        public string? CheckpointTargetCode { get; set; }
        public int? CheckpointTargetDay { get; set; }
        public Chapter3Sweep? Sweep { get; set; }
        public double? LastSweepStartMinutes { get; set; }
        public JsonElement? Raid { get; set; }
        public double? LastRaidSuccessMinutes { get; set; }
    }
    private sealed class DemandDto
    {
        public string? DemandId { get; set; }
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
        public Chapter3DemandProductType ProductType { get; set; }
        public int Quantity { get; set; }
        public int UnitValue { get; set; }
        public int IssueDay { get; set; }
        public int DueDay { get; set; }
        public int FreshCount { get; set; }
        public int Delivered { get; set; }
        public int PaidTotal { get; set; }
        public int LastTakeUnits { get; set; }
        public int HandOverCount { get; set; }
        public double? HqDoorX { get; set; }
        public double? HqDoorY { get; set; }
        public double? HqDoorZ { get; set; }
        public DemandPendingRemovalDto? PendingRemoval { get; set; }
    }
    private sealed class DemandPendingRemovalDto
    {
        public int SlotIndex { get; set; }
        public int PreQuantity { get; set; }
        public int PackageCount { get; set; }
        public int Units { get; set; }
    }
    private sealed class DemandOutcomeDto
    {
        public string? DemandId { get; set; }
        public string? ProductName { get; set; }
        public int LeftUnits { get; set; }
        public int Amount { get; set; }
        public bool Completed { get; set; }
    }
    private sealed class WarehouseOfferDto
    {
        public int OpenedDay { get; set; }
        public int LastReminderWeek { get; set; }
        public bool Purchased { get; set; }
        public int? PurchasedDay { get; set; }
        public int NotReadyCount { get; set; }
        public int FellThroughCount { get; set; }
    }
    private sealed class JobBoardDto
    {
        public OfferDto? OpenOffer { get; set; }
        public JobDto? ActiveJob { get; set; }
        public int? CooldownUntilDay { get; set; }
        public int LastProcessedDay { get; set; }
        public Chapter3JobType? LastTypeOffered { get; set; }
        public List<Chapter3JobType>? TypesSeen { get; set; }
        public int RefusalStreak { get; set; }
        public bool NellHintIssued { get; set; }
        public bool CooperationVisitIssued { get; set; }
        public int SuccessfulJobs { get; set; }
        public int SuccessfulJobsSinceWarehousePurchase { get; set; }
        public int NextOfferId { get; set; }
        public int? LastDeclinedOfferId { get; set; }
        public int? LastExpiredOfferId { get; set; }
        public int? LastRefusedOfferId { get; set; }
        public CompletionDto? LastCompletedJob { get; set; }
        public int? LastEmergencySupplyOfferId { get; set; }
        public int NextJobId { get; set; }
    }
    private sealed class OfferDto
    {
        public Chapter3CashMovement? CashMovement { get; set; }
        public Chapter3Shutdown? Shutdown { get; set; }
        public Chapter3StorageRun? StorageRun { get; set; }
        public int OfferId { get; set; }
        public Chapter3JobType JobType { get; set; }
        public int Reward { get; set; }
        public int OfferedDay { get; set; }
        public int RespondByDay { get; set; }
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
    }
    private sealed class JobDto
    {
        public Chapter3CashMovement? CashMovement { get; set; }
        public Chapter3Shutdown? Shutdown { get; set; }
        public Chapter3StorageRun? StorageRun { get; set; }
        public int JobId { get; set; }
        public int OfferId { get; set; }
        public Chapter3JobType JobType { get; set; }
        public int Reward { get; set; }
        public int AcceptedDay { get; set; }
        public int DeadlineDay { get; set; }
        public bool ExtensionUsed { get; set; }
        public DeliveryDto? Delivery { get; set; }
        public RecoveryDto? Recovery { get; set; }
        public double? AcceptedGameMinutes { get; set; }
        public double? ExpiresGameMinutes { get; set; }
    }
    private sealed class RecoveryDto
    {
        public string? PickupGuid { get; set; }
        public string? PickupName { get; set; }
        public double PickupX { get; set; }
        public double PickupY { get; set; }
        public double PickupZ { get; set; }
        public string? HandoffGuid { get; set; }
        public string? HandoffName { get; set; }
        public double HandoffX { get; set; }
        public double HandoffY { get; set; }
        public double HandoffZ { get; set; }
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
        public string? PackagingId { get; set; }
        public string? PackagingName { get; set; }
        public int BrickCount { get; set; }
        public int StagedSlotIndex { get; set; }
        public bool Staged { get; set; }
        public bool Custody { get; set; }
        public bool RemovalConfirmed { get; set; }
    }
    private sealed class DeliveryDto
    {
        public string? DropGuid { get; set; }
        public string? DropName { get; set; }
        public double DropX { get; set; }
        public double DropY { get; set; }
        public double DropZ { get; set; }
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
        public string? PackagingId { get; set; }
        public string? PackagingName { get; set; }
        public int BrickCount { get; set; }
        public bool RemovalConfirmed { get; set; }
    }
    private sealed class CompletionDto
    {
        public Chapter3CashMovement? CashMovement { get; set; }
        public Chapter3Shutdown? Shutdown { get; set; }
        public int JobId { get; set; }
        public Chapter3JobType JobType { get; set; }
        public int Reward { get; set; }
    }
    private sealed class LedgerDto
    {
        public List<MakeGoodDto>? Entries { get; set; }
        public int LastProcessedDay { get; set; }
        public int NextId { get; set; }
    }
    private sealed class MakeGoodDto
    {
        public int Id { get; set; }
        public SourceDto? Source { get; set; }
        public int Amount { get; set; }
        public int OpenedDay { get; set; }
        public int DueDay { get; set; }
        public Chapter3MakeGoodStatus Status { get; set; }
        public Chapter3MakeGoodStage Stage { get; set; }
        public int? WarningDeliveredDay { get; set; }
        public int? BeatingDeliveredDay { get; set; }
    }
    private sealed class CutsDto
    {
        public List<CutDto>? Entries { get; set; }
        public int LastProcessedDay { get; set; }
    }
    private sealed class CutDto
    {
        public int WeekIndex { get; set; }
        public int Amount { get; set; }
        public int DueDay { get; set; }
        public int GraceUntilDay { get; set; }
        public Chapter3WeeklyCutStatus Status { get; set; }
    }
    private sealed class ReceiptDto
    {
        public string? CorrelationId { get; set; }
        public long Revision { get; set; }
    }
    private sealed class SourceDto
    {
        public string? Kind { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? JobId { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Chapter3JobType? JobType { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? WeekIndex { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DemandId { get; set; }
    }
}
