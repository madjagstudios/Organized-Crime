using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime.Release1.Missions;

// OC-153: the service-side native-effect journal region, moved verbatim from
// Release1StoryRuntimeService (TryPrepareNativeEffect, the two Chief prepare
// paths, TryMarkNativeEffectApplied, TryCommitNativeEffect,
// TryRecordNativeEffectIssuance, TryGetExecutablePreparedEffect and
// IsCurrentEffectAuthorization). Only the receiver changed (_state and
// friends to the shared command context); the pure journal itself
// (Release1NativeEffectJournal) already lives in Model since OC-146. Takes no
// lock: call only while holding the service gate.
internal sealed class Release1NativeEffectJournalHost
{
    private readonly Release1MissionCommandContext _context;

    public Release1NativeEffectJournalHost(Release1MissionCommandContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Release1StoryRuntimeCommandResult TryPrepareNativeEffect(Release1NativeEffectJournalEntry effect)
    {
        var gate = _context.CheckEffectMutationGate(); if (gate is not null) return gate;
        if (effect is null) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect request was null.");
        try { effect.Validate(); } catch (ArgumentException ex) { return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, ex.Message); }
        if (effect.Phase != Release1NativeEffectPhase.Prepared) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Only Prepared effects may be issued for native execution.");
        if (_context.GetState() is null) return _context.Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
        if (string.Equals(effect.MissionKey, Release1MissionCatalog.ChiefCampbell, StringComparison.Ordinal))
        {
            // OC-118. Demand payments and protection purchases share the Chief's scope key and
            // journal but authorize against different counters (DemandRound vs the never-resetting
            // ProtectionPurchases), so the transition kind on the authorization dispatches them.
            if (effect.AuthorizedStoryCorrelationId is not null &&
                Release1LogicalCorrelation.TryParse(effect.AuthorizedStoryCorrelationId, out var chiefKind) &&
                chiefKind.TransitionKind == Release1TransitionKind.ChiefProtectionAccepted)
                return PrepareChiefProtectionEffect(effect);
            return PrepareChiefEffect(effect);
        }
        if (!Release1LogicalCorrelation.TryParse(effect.AuthorizedStoryCorrelationId, out var authorization) || authorization.PlayerId != _context.GetState()!.PlayerId || authorization.MissionKey != effect.MissionKey || authorization.Attempt != effect.Attempt || authorization.TransitionKind is not (Release1TransitionKind.MissionActivated or Release1TransitionKind.MakeGoodAccepted or Release1TransitionKind.RecoveryAccepted))
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect did not reference an eligible accepted story transition.");
        var mission = _context.GetState()!.Missions.FirstOrDefault(m => m.MissionKey == effect.MissionKey && m.Attempt == effect.Attempt);
        if (mission is null) return _context.Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Effect mission/attempt was not active in story state.");
        if (!mission.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId!, StringComparer.Ordinal) ||
            (authorization.TransitionKind == Release1TransitionKind.MissionActivated && mission.State != Release1MissionState.Active) ||
            (authorization.TransitionKind == Release1TransitionKind.MakeGoodAccepted && mission.State != Release1MissionState.MakeGoodActive) ||
            (authorization.TransitionKind == Release1TransitionKind.RecoveryAccepted && mission.State != Release1MissionState.RecoveryActive) ||
            (effect.AuthorizedMissionRevision >= 0 && effect.AuthorizedMissionRevision != mission.Revision) ||
            string.Equals(effect.EffectKind, "Reward", StringComparison.Ordinal))
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect authorization did not match the mission state or effect type.");
        var existing = _context.GetState()!.NativeEffects.FirstOrDefault(e => e.EffectId == effect.EffectId);
        if (existing is null && effect.PreparedStoryRevision != _context.GetState()!.Revision + 1)
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Prepared effect revision did not match the next story revision.");
        if (existing is not null && !IsCurrentEffectAuthorization(existing)) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect authorization was superseded by a later mission transition.");
        var journal = Release1NativeEffectJournal.Apply(_context.GetState()!.NativeEffects, new(Release1NativeEffectCommandKind.Prepare, effect.EffectId, effect.MissionKey, effect.Attempt, effect.EffectKind, effect.SourceIdentity, effect.DestinationIdentity, effect.AmountOrCargoIdentity, StoryCorrelationId: effect.AuthorizedStoryCorrelationId, AuthorizedMissionRevision: mission.Revision + 1), _context.GetState()!.Revision + 1);
        if (!journal.Accepted) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, journal.Message);
        if (!journal.Idempotent)
        {
            var missions = _context.GetState()!.Missions.Select(m => m.MissionKey == mission.MissionKey ? m with { NativeEffectIds = m.NativeEffectIds.Append(effect.EffectId).ToArray(), Revision = m.Revision + 1 } : m).ToArray();
            _context.SetState(_context.GetState()! with { NativeEffects = journal.Effects, Missions = missions, Revision = _context.GetState()!.Revision + 1 });
        }
        return new(journal.Idempotent ? Release1StoryCommandStatus.NoOp : Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _context.GetState(), journal.Message);
    }

    // OC-73 spec decisions 4 and 6. The Chief's scope key is deliberately absent from
    // Release1MissionCatalog.All, so the mission lookup in TryPrepareNativeEffect would find nothing
    // and refuse every Chief effect. This branch is the whole authorization path for that scope key
    // and never touches Missions.
    //
    // The DemandOpen to Paying transition and the effect's prepare are folded into one atomic write
    // here (rather than a separate TrySetChiefRecord call ahead of this one): Release1ChiefRecord's
    // own Validate forbids a Paying record with an empty NativeEffectIds, and Release1StoryState's
    // reverse check forbids a ChiefRecord that names an effect id absent from NativeEffects, so no
    // intermediate state between DemandOpen and a Paying record with its effect already prepared can
    // ever be persisted. Refused here, the record is left exactly as it was (still DemandOpen or
    // still Paying on a retried prepare), so nothing is ever left half done.
    private Release1StoryRuntimeCommandResult PrepareChiefEffect(Release1NativeEffectJournalEntry effect)
    {
        var record = _context.GetState()!.ChiefRecord;
        if (record is null) return _context.Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Chief record was absent.");
        if (effect.Attempt != record.DemandRound || record.State is not (Release1ChiefState.DemandOpen or Release1ChiefState.Declined or Release1ChiefState.Paying))
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Chief effect did not match a payable round.");
        if (!Release1LogicalCorrelation.TryParse(effect.AuthorizedStoryCorrelationId, out var authorization) ||
            authorization.PlayerId != _context.GetState()!.PlayerId || authorization.MissionKey != effect.MissionKey ||
            authorization.Attempt != effect.Attempt ||
            authorization.TransitionKind != Release1TransitionKind.ChiefPaymentAccepted ||
            !string.Equals(effect.EffectKind, Release1ChiefEffect.Kind, StringComparison.Ordinal) ||
            (effect.AuthorizedMissionRevision >= 0 && effect.AuthorizedMissionRevision != record.Revision))
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Chief effect authorization did not match his record.");
        if (record.State == Release1ChiefState.Paying &&
            !record.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId!, StringComparer.Ordinal))
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Chief effect authorization did not match his record.");
        var existing = _context.GetState()!.NativeEffects.FirstOrDefault(e => e.EffectId == effect.EffectId);
        if (existing is null && effect.PreparedStoryRevision != _context.GetState()!.Revision + 1)
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Prepared effect revision did not match the next story revision.");
        if (existing is not null && !IsCurrentEffectAuthorization(existing))
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect authorization was superseded.");
        var journal = Release1NativeEffectJournal.Apply(
            _context.GetState()!.NativeEffects,
            new(Release1NativeEffectCommandKind.Prepare, effect.EffectId, effect.MissionKey, effect.Attempt,
                effect.EffectKind, effect.SourceIdentity, effect.DestinationIdentity, effect.AmountOrCargoIdentity,
                StoryCorrelationId: effect.AuthorizedStoryCorrelationId, AuthorizedMissionRevision: record.Revision + 1),
            _context.GetState()!.Revision + 1);
        if (!journal.Accepted) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, journal.Message);
        if (!journal.Idempotent)
        {
            var nextRecord = record.State == Release1ChiefState.Paying
                ? record with { Revision = record.Revision + 1 }
                : record with
                {
                    State = Release1ChiefState.Paying,
                    LastShortfallNoticed = null,
                    AcceptedLogicalCorrelations = record.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId!, StringComparer.Ordinal)
                        ? record.AcceptedLogicalCorrelations
                        : record.AcceptedLogicalCorrelations.Append(effect.AuthorizedStoryCorrelationId!).ToArray(),
                    NativeEffectIds = record.NativeEffectIds.Contains(effect.EffectId, StringComparer.Ordinal)
                        ? record.NativeEffectIds
                        : record.NativeEffectIds.Append(effect.EffectId).ToArray(),
                    Revision = record.Revision + 1
                };
            _context.SetState(_context.GetState()! with
            {
                NativeEffects = journal.Effects,
                ChiefRecord = nextRecord,
                Revision = _context.GetState()!.Revision + 1
            });
        }
        return new(journal.Idempotent ? Release1StoryCommandStatus.NoOp : Release1StoryCommandStatus.Accepted,
            Release1StoryRuntimeRejectReason.None, _context.GetState(), journal.Message);
    }

    // OC-118. The protection mirror of PrepareChiefEffect: the same atomic fold of the record
    // transition (Adopted or Settled into PayingProtection) and the effect's prepare, the same
    // revision discipline, and the same superseded rejection for a redelivered prepare. The one
    // structural difference is the purchase number: DemandRound is set by Observe ahead of the tap,
    // but nothing sets the purchase number before this write, so the fresh prepare claims
    // ProtectionPurchases + 1 and stores it on the record in the same write.
    private Release1StoryRuntimeCommandResult PrepareChiefProtectionEffect(Release1NativeEffectJournalEntry effect)
    {
        var record = _context.GetState()!.ChiefRecord;
        if (record is null) return _context.Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Chief record was absent.");
        var fresh = effect.Attempt == record.ProtectionPurchases + 1 &&
            record.State is (Release1ChiefState.Adopted or Release1ChiefState.Settled);
        var retry = effect.Attempt == record.ProtectionPurchases &&
            record.State == Release1ChiefState.PayingProtection;
        if (!fresh && !retry)
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Chief protection effect did not match a buyable purchase.");
        if (!Release1LogicalCorrelation.TryParse(effect.AuthorizedStoryCorrelationId, out var authorization) ||
            authorization.PlayerId != _context.GetState()!.PlayerId || authorization.MissionKey != effect.MissionKey ||
            authorization.Attempt != effect.Attempt ||
            authorization.TransitionKind != Release1TransitionKind.ChiefProtectionAccepted ||
            !string.Equals(effect.EffectKind, Release1ChiefEffect.Kind, StringComparison.Ordinal) ||
            (effect.AuthorizedMissionRevision >= 0 && effect.AuthorizedMissionRevision != record.Revision))
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Chief protection effect authorization did not match his record.");
        if (record.State == Release1ChiefState.PayingProtection &&
            !record.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId!, StringComparer.Ordinal))
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Chief protection effect authorization did not match his record.");
        var existing = _context.GetState()!.NativeEffects.FirstOrDefault(e => e.EffectId == effect.EffectId);
        if (existing is null && effect.PreparedStoryRevision != _context.GetState()!.Revision + 1)
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Prepared effect revision did not match the next story revision.");
        if (existing is not null && !IsCurrentEffectAuthorization(existing))
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect authorization was superseded.");
        var journal = Release1NativeEffectJournal.Apply(
            _context.GetState()!.NativeEffects,
            new(Release1NativeEffectCommandKind.Prepare, effect.EffectId, effect.MissionKey, effect.Attempt,
                effect.EffectKind, effect.SourceIdentity, effect.DestinationIdentity, effect.AmountOrCargoIdentity,
                StoryCorrelationId: effect.AuthorizedStoryCorrelationId, AuthorizedMissionRevision: record.Revision + 1),
            _context.GetState()!.Revision + 1);
        if (!journal.Accepted) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, journal.Message);
        if (!journal.Idempotent)
        {
            var nextRecord = record.State == Release1ChiefState.PayingProtection
                ? record with { Revision = record.Revision + 1 }
                : record with
                {
                    State = Release1ChiefState.PayingProtection,
                    LastShortfallNoticed = null,
                    ProtectionPurchases = effect.Attempt,
                    AcceptedLogicalCorrelations = record.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId!, StringComparer.Ordinal)
                        ? record.AcceptedLogicalCorrelations
                        : record.AcceptedLogicalCorrelations.Append(effect.AuthorizedStoryCorrelationId!).ToArray(),
                    NativeEffectIds = record.NativeEffectIds.Contains(effect.EffectId, StringComparer.Ordinal)
                        ? record.NativeEffectIds
                        : record.NativeEffectIds.Append(effect.EffectId).ToArray(),
                    Revision = record.Revision + 1
                };
            _context.SetState(_context.GetState()! with
            {
                NativeEffects = journal.Effects,
                ChiefRecord = nextRecord,
                Revision = _context.GetState()!.Revision + 1
            });
        }
        return new(journal.Idempotent ? Release1StoryCommandStatus.NoOp : Release1StoryCommandStatus.Accepted,
            Release1StoryRuntimeRejectReason.None, _context.GetState(), journal.Message);
    }

    public Release1StoryRuntimeCommandResult TryMarkNativeEffectApplied(
        string effectId,
        string nativeReceiptId,
        Release1NativeEffectPersistenceMode mode = Release1NativeEffectPersistenceMode.SaveGated)
    {
        var gate = _context.CheckEffectMutationGate(); if (gate is not null) return gate;
        if (_context.GetState() is null) return _context.Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
        if (!Enum.IsDefined(mode)) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Native effect persistence mode was not defined.");
        var existing = _context.GetState()!.NativeEffects.FirstOrDefault(e => e.EffectId == effectId);
        if (existing is null) return _context.Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Effect was not found.");
        if (!IsCurrentEffectAuthorization(existing)) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect authorization was superseded by a later mission transition.");
        if (mode == Release1NativeEffectPersistenceMode.RevertTolerant &&
            !Release1MissionCatalog.AllowsRevertTolerantEffects(existing.MissionKey))
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Only revert-tolerant missions may use the revert-tolerant mode.");
        if (mode == Release1NativeEffectPersistenceMode.SaveGated &&
            existing.Phase == Release1NativeEffectPhase.Prepared && existing.PreparedStoryRevision > _context.GetLastPersistedRevision())
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Prepared effect was not durably persisted before native application.");
        var journal = Release1NativeEffectJournal.Apply(_context.GetState()!.NativeEffects, new(Release1NativeEffectCommandKind.MarkApplied, effectId, existing.MissionKey, existing.Attempt, existing.EffectKind, existing.SourceIdentity, existing.DestinationIdentity, existing.AmountOrCargoIdentity, nativeReceiptId), _context.GetState()!.Revision + 1);
        if (!journal.Accepted) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, journal.Message);
        if (!journal.Idempotent)
        {
            _context.SetState(_context.GetState()! with { NativeEffects = journal.Effects, Revision = _context.GetState()!.Revision + 1 });
            _context.SetLastAppliedRevision(_context.GetState()!.Revision); // This applied mutation is not covered until the next native save
        }
        return new(journal.Idempotent ? Release1StoryCommandStatus.NoOp : Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _context.GetState(), journal.Message);
    }

    // capturedPersistedRevision lets a caller that is committing more than one native effect in the
    // same post-save pass (Wrong Address's consumption and reward effects) gate the Applied check
    // below on the story revision it captured once, at the start of that pass, instead of the live
    // _state.Revision, which advances by one with each commit the pass itself performs. Left null,
    // behaviour is unchanged: the live revision is used, exactly as Small Courtesy relies on today.
    public Release1StoryRuntimeCommandResult TryCommitNativeEffect(string effectId, string storyCorrelationId, long? capturedPersistedRevision = null)
    {
        var gate = _context.CheckEffectMutationGate(); if (gate is not null) return gate;
        if (_context.GetState() is null) return _context.Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
        var effect = _context.GetState()!.NativeEffects.FirstOrDefault(e => e.EffectId == effectId);
        if (effect is null) return _context.Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Effect was not found.");
        if (!IsCurrentEffectAuthorization(effect)) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect authorization was superseded by a later mission transition.");
        if (capturedPersistedRevision is not null && !Release1MissionCatalog.AllowsRevertTolerantEffects(effect.MissionKey))
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Only revert-tolerant missions may commit against a captured revision.");
        var revisionForAppliedGate = capturedPersistedRevision ?? _context.GetState()!.Revision;
        if (effect.Phase == Release1NativeEffectPhase.Applied && revisionForAppliedGate > _context.GetLastPersistedRevision())
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Applied effect was not durably persisted with its native mutation before commit.");
        if (effect.PreparedStoryRevision > _context.GetLastPersistedRevision() || effect.AuthorizedStoryCorrelationId is null || effect.AuthorizedStoryCorrelationId != storyCorrelationId)
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect commit did not reference its durable authorization correlation.");
        if (!Release1LogicalCorrelation.TryParse(storyCorrelationId, out var correlation) || correlation.PlayerId != _context.GetState()!.PlayerId || correlation.MissionKey != effect.MissionKey || correlation.Attempt != effect.Attempt)
            return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect commit correlation did not match the effect.");
        var journal = Release1NativeEffectJournal.Apply(_context.GetState()!.NativeEffects, new(Release1NativeEffectCommandKind.Commit, effectId, effect.MissionKey, effect.Attempt, effect.EffectKind, effect.SourceIdentity, effect.DestinationIdentity, effect.AmountOrCargoIdentity, effect.NativeReceiptId, storyCorrelationId), _context.GetState()!.Revision + 1);
        if (!journal.Accepted) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, journal.Message);
        if (!journal.Idempotent) _context.SetState(_context.GetState()! with { NativeEffects = journal.Effects, Revision = _context.GetState()!.Revision + 1 });
        return new(journal.Idempotent ? Release1StoryCommandStatus.NoOp : Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.None, _context.GetState(), journal.Message);
    }

    public Release1StoryRuntimeCommandResult TryRecordNativeEffectIssuance(string effectId, Release1NativeEffectIssuanceOutcome outcome, string? nativeReceiptId = null)
    {
        var gate = _context.CheckEffectMutationGate(); if (gate is not null) return gate;
        if (outcome == Release1NativeEffectIssuanceOutcome.Applied) return TryMarkNativeEffectApplied(effectId, nativeReceiptId ?? string.Empty);
        if (_context.GetState() is null) return _context.Reject(Release1StoryRuntimeRejectReason.Inactive, "Story state was absent.");
        var existing = _context.GetState()!.NativeEffects.FirstOrDefault(e => e.EffectId == effectId);
        if (existing is null) return _context.Reject(Release1StoryRuntimeRejectReason.EffectNotFound, "Effect was not found.");
        if (outcome == Release1NativeEffectIssuanceOutcome.Ambiguous)
        {
            if (!IsCurrentEffectAuthorization(existing)) return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect authorization was superseded by a later mission transition.");
            if (existing.ExecutionBlocked)
                return new(Release1StoryCommandStatus.NoOp, Release1StoryRuntimeRejectReason.EffectAmbiguous, _context.GetState(), "Ambiguous issuance was already recorded.");
            if (existing.Phase != Release1NativeEffectPhase.Prepared)
                return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Only a prepared effect may be marked ambiguous.");
            var effects = _context.GetState()!.NativeEffects.Select(e => e.EffectId == effectId ? e with { ExecutionBlocked = true } : e).ToArray();
            _context.SetState(_context.GetState()! with { NativeEffects = effects, Revision = _context.GetState()!.Revision + 1 });
            return new(Release1StoryCommandStatus.Accepted, Release1StoryRuntimeRejectReason.EffectAmbiguous, _context.GetState(), "Ambiguous issuance is blocked pending authoritative reconciliation.");
        }
        return _context.Reject(Release1StoryRuntimeRejectReason.InvalidTransition, "Effect issuance failed; no native mutation was authorized.");
    }

    public bool TryGetExecutablePreparedEffect(string effectId, out Release1NativeEffectJournalEntry effect)
    {
        effect = null!;
        if (_context.GetPhase() is not (Release1StoryRuntimePhase.Active or Release1StoryRuntimePhase.Saving) || _context.GetState() is null || _context.CheckCurrentAuthority() is not null) return false;
        var candidate = _context.GetState()!.NativeEffects.FirstOrDefault(e => e.EffectId == effectId);
        if (candidate is null || !IsCurrentEffectAuthorization(candidate) || candidate.Phase != Release1NativeEffectPhase.Prepared || candidate.ExecutionBlocked || candidate.PreparedStoryRevision > _context.GetLastPersistedRevision()) return false;
        effect = candidate; return true;
    }

    private bool IsCurrentEffectAuthorization(Release1NativeEffectJournalEntry effect)
    {
        if (_context.GetState() is null || effect.AuthorizedStoryCorrelationId is null || !Release1LogicalCorrelation.TryParse(effect.AuthorizedStoryCorrelationId, out var authorization)) return false;
        if (string.Equals(effect.MissionKey, Release1MissionCatalog.ChiefCampbell, StringComparison.Ordinal))
        {
            var record = _context.GetState()!.ChiefRecord;
            if (record is null) return false;
            // OC-118. Protection purchases authorize against the never-resetting purchase count,
            // demand payments against the ladder round; the completed successor lands back on the
            // unpaid state the buy started from (Adopted or Settled) instead of Settled.
            var protection = authorization.TransitionKind == Release1TransitionKind.ChiefProtectionAccepted;
            var expectedAttempt = protection ? record.ProtectionPurchases : record.DemandRound;

            // OC-73 settled drain fix. An Applied Chief cash effect already passed this exact check
            // once, under the strict rule below, while it was still Prepared: TryMarkNativeEffectApplied
            // calls this method before the journal entry's own phase flips to Applied, so nothing here
            // is granting a first authorization, only recognizing one already given. That is what makes
            // it safe to stop re-deriving it from the live record: Release1ChiefLedger.Observe's
            // Adopted-or-Settled ArrestObserved branch resets DemandRound to 1 and clears both
            // AcceptedLogicalCorrelations and NativeEffectIds the moment a new arrest lands, which the
            // settled drain fix now allows to happen before this same effect's post save commit runs,
            // so none of round, the correlation list, or Paying-or-Settled still describe it by then.
            // Applied is the narrow gate: a still-Prepared effect (RunPay's own debit and confirm path,
            // untouched by this fix) falls through to the unwidened rule that follows.
            if (effect.Phase == Release1NativeEffectPhase.Applied && !effect.ExecutionBlocked)
            {
                // OC-77. The relaxed shape below exists for one case only: an orphaned effect,
                // one the live record no longer describes because a new arrest reset the ladder
                // (round advanced, both lists cleared) while the payoff was still Applied. An
                // effect the live record still describes falls through to the strict rule, so a
                // same-round revision drift the rule does not bless reads as superseded instead
                // of being waved through.
                var orphaned = effect.Attempt != expectedAttempt ||
                    !record.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId, StringComparer.Ordinal);
                if (orphaned)
                    return authorization.PlayerId == _context.GetState()!.PlayerId &&
                        authorization.MissionKey == Release1MissionCatalog.ChiefCampbell &&
                        authorization.TransitionKind is (Release1TransitionKind.ChiefPaymentAccepted or Release1TransitionKind.ChiefProtectionAccepted);
            }

            if (effect.Attempt != expectedAttempt) return false;
            // The same completedConsumptionSuccessor allowance the mission branch below relies on:
            // Release1ChiefLedger.Settle bumps the record's Revision by exactly one on top of the
            // Revision the effect was authorized against, the moment the wipe settles the record on
            // the pass after Applied. The effect still has to commit after the next save, so its
            // authorization must survive that one-step revision advance.
            var completedPaymentSuccessor =
                (protection
                    ? record.State is (Release1ChiefState.Adopted or Release1ChiefState.Settled)
                    : record.State == Release1ChiefState.Settled) &&
                record.Revision > 0 &&
                effect.AuthorizedMissionRevision == record.Revision - 1;
            if (effect.AuthorizedMissionRevision != record.Revision && !completedPaymentSuccessor) return false;
            var settledState = protection
                ? record.State is (Release1ChiefState.PayingProtection or Release1ChiefState.Adopted or Release1ChiefState.Settled)
                : record.State is (Release1ChiefState.Paying or Release1ChiefState.Settled);
            return record.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId, StringComparer.Ordinal) &&
                authorization.PlayerId == _context.GetState()!.PlayerId &&
                authorization.MissionKey == Release1MissionCatalog.ChiefCampbell &&
                authorization.Attempt == expectedAttempt &&
                authorization.TransitionKind == (protection ? Release1TransitionKind.ChiefProtectionAccepted : Release1TransitionKind.ChiefPaymentAccepted) &&
                settledState;
        }
        var mission = _context.GetState()!.Missions.SingleOrDefault(m => m.MissionKey == effect.MissionKey && m.Attempt == effect.Attempt);
        if (mission is null || !mission.AcceptedLogicalCorrelations.Contains(effect.AuthorizedStoryCorrelationId, StringComparer.Ordinal)) return false;
        // A consumption effect (cargo or cash) whose own mission has already completed on the
        // strength of that same effect: The Envelope's CashTransfer completes and commits after
        // Satisfied exactly the way Wrong Address, Room With No Name, and Short Notice's
        // CargoTransfer do, since none of them prepare a reward effect that could otherwise carry
        // the post-completion authorization forward.
        var completedConsumptionSuccessor =
            effect.EffectKind is "CargoTransfer" or "CashTransfer" &&
            effect.Phase is Release1NativeEffectPhase.Applied or Release1NativeEffectPhase.Committed &&
            mission.State == Release1MissionState.Satisfied &&
            mission.Revision > 0 &&
            effect.AuthorizedMissionRevision == mission.Revision - 1 &&
            authorization.TransitionKind is Release1TransitionKind.MissionActivated or Release1TransitionKind.MakeGoodAccepted or Release1TransitionKind.RecoveryAccepted;
        if (mission.Revision != effect.AuthorizedMissionRevision && !completedConsumptionSuccessor) return false;
        return authorization.PlayerId == _context.GetState()!.PlayerId && authorization.MissionKey == mission.MissionKey && authorization.Attempt == mission.Attempt && authorization.TransitionKind switch
        {
            Release1TransitionKind.MissionAccepted => mission.State == Release1MissionState.Accepted,
            Release1TransitionKind.MissionActivated => mission.State == Release1MissionState.Active || completedConsumptionSuccessor,
            Release1TransitionKind.MissionCompleted => mission.State == Release1MissionState.Satisfied,
            Release1TransitionKind.MakeGoodAccepted => mission.State == Release1MissionState.MakeGoodActive || completedConsumptionSuccessor,
            Release1TransitionKind.RecoveryAccepted => mission.State == Release1MissionState.RecoveryActive || completedConsumptionSuccessor,
            _ => false
        };
    }
}
