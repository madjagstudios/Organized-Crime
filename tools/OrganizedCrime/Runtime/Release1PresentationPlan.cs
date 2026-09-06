using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public enum Release1PresentationCommand
{
    IntroAccept,
    IntroDefer,
    SmallCourtesyReview,
    SmallCourtesyAccept,
    SmallCourtesyDefer,
    WrongAddressReview,
    WrongAddressAccept,
    WrongAddressDefer,
    RoomWithNoNameReview,
    RoomWithNoNameAccept,
    RoomWithNoNameDefer,
    ShortNoticeReview,
    ShortNoticeAccept,
    ShortNoticeDefer,
    KeepTheLightsOffReview,
    KeepTheLightsOffAccept,
    KeepTheLightsOffDefer,
    TheEnvelopeReview,
    TheEnvelopeAccept,
    TheEnvelopeDefer,

    /// <summary>OC-73. The Chief is not a mission, so neither of his two commands ever reaches Release1PresentationCommandRouter.TryInvoke's mission presenters.</summary>
    ChiefCampbellPay,
    ChiefCampbellDecline
}

public enum Release1DesiredEntryState
{
    Inactive,
    Active,
    Complete
}

public sealed record Release1DesiredMessage(string CorrelationId, string Text);

public sealed record Release1DecisionOption(string Label, Release1PresentationCommand Command);

public sealed record Release1DesiredDecision(string Id, string Prompt, IReadOnlyList<Release1DecisionOption> Options);

public sealed record Release1WorldPoint(float X, float Y, float Z);

public sealed record Release1DesiredEntry(string Text, Release1DesiredEntryState State, Release1WorldPoint? Marker);

public sealed record Release1DesiredQuest(string Key, string Title, IReadOnlyList<Release1DesiredEntry> Entries);

public sealed record Release1PresentationPlan(
    IReadOnlyList<Release1DesiredMessage> Messages,
    Release1DesiredDecision? Decision,
    IReadOnlyList<Release1DesiredQuest> Quests,
    bool DecisionUndetermined = false)
{
    public static Release1PresentationPlan Empty { get; } =
        new(Array.Empty<Release1DesiredMessage>(), null, Array.Empty<Release1DesiredQuest>());
}

public sealed record Release1PresentationInputs(
    Release1StoryState? Story,
    bool IntroPromptVisible,
    bool IntroPromptPending,
    Release1SmallCourtesyViewModel? SmallCourtesy,
    Release1SmallCourtesyAssignment? SmallCourtesyAssignment,
    Release1WrongAddressViewModel? WrongAddress = null,
    Release1WrongAddressAssignment? WrongAddressAssignment = null,
    Release1WrongAddressProgress? WrongAddressProgress = null,
    Release1RoomWithNoNameViewModel? RoomWithNoName = null,
    Release1RoomWithNoNameAssignment? RoomWithNoNameAssignment = null,
    Release1RoomWithNoNameProgress? RoomWithNoNameProgress = null,
    Release1WorldPoint? HoldRoomMarker = null,
    Release1ShortNoticeViewModel? ShortNotice = null,
    Release1ShortNoticeAssignment? ShortNoticeAssignment = null,
    Release1ShortNoticeProgress? ShortNoticeProgress = null,
    Release1KeepTheLightsOffViewModel? KeepTheLightsOff = null,
    Release1KeepTheLightsOffAssignment? KeepTheLightsOffAssignment = null,
    Release1KeepTheLightsOffProgress? KeepTheLightsOffProgress = null,
    Release1TheEnvelopeViewModel? TheEnvelope = null,
    Release1TheEnvelopeAssignment? TheEnvelopeAssignment = null,
    Release1TheEnvelopeProgress? TheEnvelopeProgress = null);

/// <summary>
/// Pure, deterministic projection from Release 1 story state (plus the mission view models built
/// on top of it) to the native presentation the game should show: passive/queued messages, at most
/// one active decision prompt, and desired quest entries. Never touches Unity or S1API, and never
/// mutates its inputs. A later reconciler is responsible for diffing this plan against
/// <see cref="Release1StoryState.PresentationReceipts"/> and the live native UI.
/// </summary>
public static class Release1PresentationPlanner
{
    private const string IntroDecisionId = "release1-intro";
    private const string IntroDeferredDecisionId = "release1-intro-deferred";
    private const string SmallCourtesyReviewDecisionId = "release1-sc-review";
    private const string SmallCourtesyDecisionId = "release1-sc-decision";

    private const string IntroAcceptedReceipt = "presentation-intro-accepted-v1";
    private const string IntroDeferredReceipt = "presentation-intro-deferred-v1";
    private const string SmallCourtesyAcceptedReceipt = "presentation-sc-accepted-v1";
    private const string SmallCourtesyCompleteReceipt = "presentation-sc-complete-v1";

    private const string IntroOfferText = "A number you do not know is offering a discreet shipping arrangement.";
    private const string IntroAcceptedText = "You are in. Expect instructions soon.";
    private const string IntroDeferredText = "Understood. Reach out when you think you can handle it.";
    private const string IntroDeferredNudgeText = "Still here. The shipping arrangement is on the table when you have the courage to take it up.";
    private const string SmallCourtesyAcceptedText = "We know you have the package. Stop fucking around and drop it where instructed.";
    private const string SmallCourtesyCompleteText = "Received and paid. Easy enough.";
    private const string WaitForPaymentText = "Wait for payment";
    private const string PaymentOnItsWayText = "Payment is on its way";
    private const string DeliveryOnHoldText = "Delivery on hold";

    private const string WrongAddressReviewDecisionId = "release1-wa-review";
    private const string WrongAddressDecisionId = "release1-wa-decision";
    private const string WrongAddressAcceptedReceipt = "presentation-nell-wa-accepted-v1";
    private const string WrongAddressCustodyReceipt = "presentation-nell-wa-custody-v1";
    private const string WrongAddressDeliveredReceipt = "presentation-nell-wa-delivered-v1";
    private const string WrongAddressDeliveredText = "Received and paid. Mr. Selby noticed.";
    private const string WrongAddressHoldText = "Delivery on hold";
    private const string WrongAddressQuestTitle = "Wrong Address";

    private const string RoomWithNoNameReviewDecisionId = "release1-rn-review";
    private const string RoomWithNoNameDecisionId = "release1-rn-decision";
    private const string RoomWithNoNameAcceptedReceipt = "presentation-nell-rn-accepted-v1";
    private const string RoomWithNoNameCustodyReceipt = "presentation-nell-rn-custody-v1";
    private const string RoomWithNoNameStowedReceipt = "presentation-nell-rn-stowed-v1";
    private const string RoomWithNoNameReleaseReceipt = "presentation-nell-rn-release-v1";
    private const string RoomWithNoNameDeliveredReceipt = "presentation-nell-rn-delivered-v1";
    private const string RoomWithNoNameDeliveredText = "Received and paid. We now know we can trust HQ.";
    private const string RoomWithNoNameCustodyText = "You have it. Take it to the room and leave it there.";
    private const string RoomWithNoNameStowedText = "Good. It stays in the room until I call. One day. Fuck off until then.";
    private const string RoomWithNoNameHoldEntryText = "Hold it in the room until Nell calls";
    private const string RoomWithNoNameHoldText = "Delivery on hold";
    private const string RoomWithNoNameQuestTitle = "A Room With No Name";

    private const string ShortNoticeReviewDecisionId = "release1-sn-review";
    private const string ShortNoticeDecisionId = "release1-sn-decision";
    private const string ShortNoticeAcceptedReceipt = "presentation-nell-sn-accepted-v1";
    private const string ShortNoticeSpreadReceipt = "presentation-nell-sn-spread-v1";
    private const string ShortNoticeDeliveredReceipt = "presentation-nell-sn-delivered-v1";
    private const string ShortNoticeQuestTitle = "Short Notice";
    private const string ShortNoticeSpreadText = "Put the whole order in one slot. I am not counting two piles.";
    private const string ShortNoticeShortfallOneText = "That is not the whole order. One more of the same, in the same drop, in one slot. Jesus, do we need to find someone else that is competent?";
    private const string ShortNoticeShortfallManyFormat = "That is not the whole order. {0} more of the same, in the same drop, in one slot. Get it together, you fuck.";
    private const string ShortNoticeDeliveredText = "Received and paid. That is what short notice is worth.";
    private const string ShortNoticeEntryOpenFormat = "Leave {0} of {1} at {2} before the window closes";
    private const string ShortNoticeEntryShortfallFormat = "Leave {0} of {1} at {2}, {3} still needed";
    private const string ShortNoticeEntryHoldText = "Delivery on hold";

    private static string ShortNoticeShortfallReceipt(int remaining) => $"presentation-nell-sn-shortfall-r{remaining}-v1";

    private const string KeepTheLightsOffReviewDecisionId = "release1-ktlo-review";
    private const string KeepTheLightsOffDecisionId = "release1-ktlo-decision";
    private const string KeepTheLightsOffAcceptedReceipt = "presentation-nell-ktlo-accepted-v1";
    private const string KeepTheLightsOffClearReceipt = "presentation-nell-ktlo-clear-v1";
    private const string KeepTheLightsOffCompleteReceipt = "presentation-nell-ktlo-complete-v1";
    private const string KeepTheLightsOffQuestTitle = "Keep the Lights Off";
    private const string KeepTheLightsOffAcceptedText = "Nobody works. Stop all production. Every property, no noise, no grunts working. Nothing.";
    private const string KeepTheLightsOffClearConfirmedText = "All stopped. Keep it this way for 24 hours.";
    private const string KeepTheLightsOffCompletedText = "One quiet day. Thank god, you dodged a bullet there.";
    private const string KeepTheLightsOffEntryOpenText = "No production activities from you or employees for 24 hours.";
    private const string KeepTheLightsOffEntryHoldingText = "Hold quiet. Nothing moves for one day";
    private const string KeepTheLightsOffEntryHoldText = "Job on hold";

    private const string TheEnvelopeReviewDecisionId = "release1-te-review";
    private const string TheEnvelopeDecisionId = "release1-te-decision";
    private const string TheEnvelopeAcceptedReceipt = "presentation-nell-te-accepted-v1";
    private const string TheEnvelopeSpreadReceipt = "presentation-nell-te-spread-v1";
    private const string TheEnvelopeDeliveredReceipt = "presentation-nell-te-delivered-v1";
    private const string TheEnvelopeRecognitionReceipt = "presentation-nell-te-recognition-v1";
    private const string TheEnvelopeQuestTitle = "The Envelope";
    private static string TheEnvelopeAcceptedText(double amountWholeDollars) =>
        $"The closet is in the HQ. {Release1TheEnvelopePresentation.StackCount(amountWholeDollars)} stacks of cash, all in one closet.";
    private const string TheEnvelopeSpreadText = "Stop fucking around. I told you, all in one storage.";
    private const string TheEnvelopeShortfallManyFormat = "Are you trying to scam us? You really want to try and test the Selby's?";
    private const string TheEnvelopeShortfallSmallFormat = "We know how to count. You are missing {0} dollars more. Put it in the same storage";
    private const string TheEnvelopeDeliveredText = "Received. Well done.";
    private const string TheEnvelopeRecognitionText = $"Welcome to {Release1PlayerCopy.FamilyName}. You are part of the family now. Don't get cocky though, you will earn the respect for increased operations. For now, the HQ room is yours. Use it as you please, but there are times the family may need to use it, and you don't have a fucking choice. I'll be in touch.";
    private const string TheEnvelopeEntryOpenFormat = "Leave ${0} in cash in the closet at the Syndicate HQ";
    private const string TheEnvelopeEntryShortfallFormat = "Leave ${0} in cash in the closet at the Syndicate HQ, ${1} still needed";
    private const string TheEnvelopeEntryHoldText = "Delivery on hold";

    private static string TheEnvelopeShortfallReceipt(double remaining) => $"presentation-nell-te-shortfall-r{Release1TheEnvelopePresentation.Dollars(remaining)}-v1";

    public static Release1PresentationPlan Build(Release1PresentationInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var messages = new List<Release1DesiredMessage>();
        Release1DesiredDecision? decision = null;
        var quests = new List<Release1DesiredQuest>();
        var decisionUndetermined = false;

        var story = inputs.Story;
        var relationship = story?.RelationshipState ?? Release1RelationshipState.Unstarted;

        switch (relationship)
        {
            case Release1RelationshipState.Unstarted:
                if (inputs.IntroPromptVisible)
                {
                    decision = new Release1DesiredDecision(
                        IntroDecisionId,
                        Copy(IntroOfferText),
                        IntroAcceptOrDeferOptions());
                }
                else if (inputs.IntroPromptPending)
                {
                    decisionUndetermined = true;
                }
                break;

            case Release1RelationshipState.Deferred:
                messages.Add(IntroMessage(story!.PlayerId, Release1TransitionKind.IntroDeferred, IntroDeferredReceipt, IntroDeferredText));
                if (inputs.IntroPromptVisible)
                {
                    decision = new Release1DesiredDecision(
                        IntroDeferredDecisionId,
                        Copy(IntroDeferredNudgeText),
                        IntroAcceptOrDeferOptions());
                }
                else if (inputs.IntroPromptPending)
                {
                    decisionUndetermined = true;
                }
                break;

            case Release1RelationshipState.Accepted:
                messages.Add(IntroMessage(story!.PlayerId, Release1TransitionKind.IntroAccepted, IntroAcceptedReceipt, IntroAcceptedText));
                break;
        }

        if (inputs.SmallCourtesy is not null)
            BuildSmallCourtesy(story, inputs.SmallCourtesy, inputs.SmallCourtesyAssignment, messages, quests, ref decision);

        if (inputs.WrongAddress is not null)
            BuildWrongAddress(story, inputs.WrongAddress, inputs.WrongAddressAssignment, inputs.WrongAddressProgress, messages, quests, ref decision);

        if (inputs.RoomWithNoName is not null)
            BuildRoomWithNoName(story, inputs.RoomWithNoName, inputs.RoomWithNoNameAssignment, inputs.RoomWithNoNameProgress, inputs.HoldRoomMarker, messages, quests, ref decision);

        if (inputs.ShortNotice is not null)
            BuildShortNotice(story, inputs.ShortNotice, inputs.ShortNoticeAssignment, inputs.ShortNoticeProgress, messages, quests, ref decision);

        if (inputs.KeepTheLightsOff is not null)
            BuildKeepTheLightsOff(story, inputs.KeepTheLightsOff, inputs.KeepTheLightsOffAssignment, inputs.KeepTheLightsOffProgress, messages, quests, ref decision);

        if (inputs.TheEnvelope is not null)
            BuildTheEnvelope(story, inputs.TheEnvelope, inputs.TheEnvelopeAssignment, inputs.TheEnvelopeProgress, inputs.HoldRoomMarker, messages, quests, ref decision);

        return new Release1PresentationPlan(messages, decision, quests, decisionUndetermined);
    }

    private static void BuildSmallCourtesy(
        Release1StoryState? story,
        Release1SmallCourtesyViewModel smallCourtesy,
        Release1SmallCourtesyAssignment? assignment,
        List<Release1DesiredMessage> messages,
        List<Release1DesiredQuest> quests,
        ref Release1DesiredDecision? decision)
    {
        var mission = story?.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];

        if (smallCourtesy.CanReview)
        {
            // The offer body is the review decision's prompt; it is not also queued as a passive
            // message, since S1API sends the decision prompt as a message on the boundary's behalf
            // (NPC.SendTextMessage). Emitting it twice would show the offer text twice in the phone.
            decision = new Release1DesiredDecision(
                SmallCourtesyReviewDecisionId,
                Copy(smallCourtesy.Body),
                new[] { new Release1DecisionOption(Copy("Review terms"), Release1PresentationCommand.SmallCourtesyReview) });
        }
        else if (smallCourtesy.CanAccept)
        {
            // The reviewed terms carry forward into the accept prompt: smallCourtesy.Body here is the
            // accept-state view's full terms, already ending in the question (Release1PlayerCopy
            // formats it that way), so nothing further is appended.
            decision = new Release1DesiredDecision(
                SmallCourtesyDecisionId,
                Copy(smallCourtesy.Body),
                new[]
                {
                    new Release1DecisionOption(Copy("Accept"), Release1PresentationCommand.SmallCourtesyAccept),
                    new Release1DecisionOption(Copy("Not now"), Release1PresentationCommand.SmallCourtesyDefer)
                });
        }

        if (story is null || mission is null || assignment is null) return;
        if (mission.State is not (Release1MissionState.Accepted or Release1MissionState.Active or Release1MissionState.Satisfied)) return;

        quests.Add(new Release1DesiredQuest(
            Release1MissionCatalog.SmallCourtesy,
            Copy("Small Courtesy"),
            BuildEntries(mission.State, smallCourtesy.Stage, assignment)));

        if (mission.State == Release1MissionState.Satisfied)
            messages.Add(SmallCourtesyMessage(story.PlayerId, mission.Attempt, Release1TransitionKind.MissionCompleted, SmallCourtesyCompleteReceipt, SmallCourtesyCompleteText));
        else
            messages.Add(SmallCourtesyMessage(story.PlayerId, mission.Attempt, Release1TransitionKind.MissionAccepted, SmallCourtesyAcceptedReceipt, SmallCourtesyAcceptedText));
    }

    private static IReadOnlyList<Release1DesiredEntry> BuildEntries(
        Release1MissionState missionState,
        Release1SmallCourtesyCardStage stage,
        Release1SmallCourtesyAssignment assignment)
    {
        var deliveryText = Copy($"Deliver one {assignment.PackagingName} of {assignment.ProductName} to {assignment.DeadDropName}");

        if (missionState == Release1MissionState.Satisfied)
            return new[]
            {
                new Release1DesiredEntry(deliveryText, Release1DesiredEntryState.Complete, null),
                new Release1DesiredEntry(Copy(WaitForPaymentText), Release1DesiredEntryState.Complete, null)
            };

        if (stage is Release1SmallCourtesyCardStage.AwaitingFirstSave or Release1SmallCourtesyCardStage.AwaitingSecondSave or Release1SmallCourtesyCardStage.AwaitingFinalSave)
            return new[]
            {
                new Release1DesiredEntry(deliveryText, Release1DesiredEntryState.Complete, null),
                new Release1DesiredEntry(Copy(PaymentOnItsWayText), Release1DesiredEntryState.Active, null)
            };

        if (stage == Release1SmallCourtesyCardStage.Ambiguous)
            return new[]
            {
                new Release1DesiredEntry(Copy(DeliveryOnHoldText), Release1DesiredEntryState.Active, null),
                new Release1DesiredEntry(Copy(WaitForPaymentText), Release1DesiredEntryState.Inactive, null)
            };

        var marker = new Release1WorldPoint((float)assignment.DeadDropX, (float)assignment.DeadDropY, (float)assignment.DeadDropZ);
        return new[]
        {
            new Release1DesiredEntry(deliveryText, Release1DesiredEntryState.Active, marker),
            new Release1DesiredEntry(Copy(WaitForPaymentText), Release1DesiredEntryState.Inactive, null)
        };
    }

    private static void BuildWrongAddress(
        Release1StoryState? story,
        Release1WrongAddressViewModel wrongAddress,
        Release1WrongAddressAssignment? assignment,
        Release1WrongAddressProgress? progress,
        List<Release1DesiredMessage> messages,
        List<Release1DesiredQuest> quests,
        ref Release1DesiredDecision? decision)
    {
        var mission = story?.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)];

        if (wrongAddress.CanReview)
        {
            decision = new Release1DesiredDecision(
                WrongAddressReviewDecisionId,
                Copy(wrongAddress.Body),
                new[] { new Release1DecisionOption(Copy("Review terms"), Release1PresentationCommand.WrongAddressReview) });
        }
        else if (wrongAddress.CanAccept)
        {
            decision = new Release1DesiredDecision(
                WrongAddressDecisionId,
                Copy(wrongAddress.Body),
                new[]
                {
                    new Release1DecisionOption(Copy("Accept"), Release1PresentationCommand.WrongAddressAccept),
                    new Release1DecisionOption(Copy("Not now"), Release1PresentationCommand.WrongAddressDefer)
                });
        }

        if (story is null || mission is null || assignment is null) return;
        if (mission.State is not (Release1MissionState.Accepted or Release1MissionState.Active or
            Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive or Release1MissionState.Satisfied)) return;

        var blocked = story.NativeEffects.Any(effect =>
            effect.MissionKey == Release1MissionCatalog.WrongAddress &&
            effect.Attempt == mission.Attempt &&
            effect.ExecutionBlocked);
        var custody = progress?.Custody == true;

        quests.Add(new Release1DesiredQuest(
            Release1MissionCatalog.WrongAddress,
            Copy(WrongAddressQuestTitle),
            BuildWrongAddressEntries(mission.State, assignment, custody, blocked)));

        if (mission.State is Release1MissionState.Accepted or Release1MissionState.Active or
            Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive)
            messages.Add(WrongAddressMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionAccepted, WrongAddressAcceptedReceipt,
                $"The package is at {assignment.SourceDropName}. Bring it to {assignment.HandoffDropName}."));
        if (custody || mission.State == Release1MissionState.Satisfied)
            messages.Add(WrongAddressMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionActivated, WrongAddressCustodyReceipt,
                $"We have eyes everywhere, we know you have the package. Take it to {assignment.HandoffDropName} NOW."));
        if (mission.State == Release1MissionState.Satisfied)
            messages.Add(WrongAddressMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionCompleted, WrongAddressDeliveredReceipt,
                WrongAddressDeliveredText));
    }

    private static IReadOnlyList<Release1DesiredEntry> BuildWrongAddressEntries(
        Release1MissionState missionState,
        Release1WrongAddressAssignment assignment,
        bool custody,
        bool blocked)
    {
        var collect = Copy($"Fix the fuckup and get the package from {assignment.SourceDropName}");
        var handoff = Copy($"Return it to {assignment.HandoffDropName}");
        var sourceMarker = new Release1WorldPoint((float)assignment.SourceDropX, (float)assignment.SourceDropY, (float)assignment.SourceDropZ);
        var handoffMarker = new Release1WorldPoint((float)assignment.HandoffDropX, (float)assignment.HandoffDropY, (float)assignment.HandoffDropZ);

        if (missionState == Release1MissionState.Satisfied)
            return new[]
            {
                new Release1DesiredEntry(collect, Release1DesiredEntryState.Complete, null),
                new Release1DesiredEntry(handoff, Release1DesiredEntryState.Complete, null)
            };

        if (blocked)
            return new[]
            {
                custody
                    ? new Release1DesiredEntry(collect, Release1DesiredEntryState.Complete, null)
                    : new Release1DesiredEntry(collect, Release1DesiredEntryState.Active, sourceMarker),
                new Release1DesiredEntry(Copy(WrongAddressHoldText), Release1DesiredEntryState.Active, null)
            };

        if (custody)
            return new[]
            {
                new Release1DesiredEntry(collect, Release1DesiredEntryState.Complete, null),
                new Release1DesiredEntry(handoff, Release1DesiredEntryState.Active, handoffMarker)
            };

        return new[]
        {
            new Release1DesiredEntry(collect, Release1DesiredEntryState.Active, sourceMarker),
            new Release1DesiredEntry(handoff, Release1DesiredEntryState.Inactive, null)
        };
    }

    private static Release1DesiredMessage WrongAddressMessage(string playerId, int attempt, Release1TransitionKind kind, string receipt, string text) =>
        new(Release1LogicalCorrelation.Create(playerId, Release1MissionCatalog.WrongAddress, attempt, kind, receipt).Value, Copy(text));

    private static void BuildRoomWithNoName(
        Release1StoryState? story,
        Release1RoomWithNoNameViewModel room,
        Release1RoomWithNoNameAssignment? assignment,
        Release1RoomWithNoNameProgress? progress,
        Release1WorldPoint? holdRoomMarker,
        List<Release1DesiredMessage> messages,
        List<Release1DesiredQuest> quests,
        ref Release1DesiredDecision? decision)
    {
        var mission = story?.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)];

        if (room.CanReview)
        {
            decision = new Release1DesiredDecision(
                RoomWithNoNameReviewDecisionId,
                Copy(room.Body),
                new[] { new Release1DecisionOption(Copy("Review terms"), Release1PresentationCommand.RoomWithNoNameReview) });
        }
        else if (room.CanAccept)
        {
            decision = new Release1DesiredDecision(
                RoomWithNoNameDecisionId,
                Copy(room.Body),
                new[]
                {
                    new Release1DecisionOption(Copy("Accept"), Release1PresentationCommand.RoomWithNoNameAccept),
                    new Release1DecisionOption(Copy("Not now"), Release1PresentationCommand.RoomWithNoNameDefer)
                });
        }

        if (story is null || mission is null || assignment is null) return;
        if (mission.State is not (Release1MissionState.Accepted or Release1MissionState.Active or
            Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive or Release1MissionState.Satisfied)) return;

        var blocked = story.NativeEffects.Any(effect =>
            effect.MissionKey == Release1MissionCatalog.RoomWithNoName &&
            effect.Attempt == mission.Attempt &&
            effect.ExecutionBlocked);
        var custody = progress?.Custody == true;
        var stowed = progress?.Stowed == true;
        var released = progress?.HoldSatisfied == true;

        quests.Add(new Release1DesiredQuest(
            Release1MissionCatalog.RoomWithNoName,
            Copy(RoomWithNoNameQuestTitle),
            BuildRoomWithNoNameEntries(mission.State, assignment, custody, released, blocked, holdRoomMarker)));

        if (mission.State is Release1MissionState.Accepted or Release1MissionState.Active or
            Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive)
            messages.Add(RoomWithNoNameMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionAccepted, RoomWithNoNameAcceptedReceipt,
                $"It is at {assignment.SourceDropName}. Bring it to the room and leave it there."));
        if (custody || mission.State == Release1MissionState.Satisfied)
            messages.Add(RoomWithNoNameMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionActivated, RoomWithNoNameCustodyReceipt,
                RoomWithNoNameCustodyText));
        if (stowed || mission.State == Release1MissionState.Satisfied)
            messages.Add(RoomWithNoNameMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionActivated, RoomWithNoNameStowedReceipt,
                RoomWithNoNameStowedText));
        if (released || mission.State == Release1MissionState.Satisfied)
            messages.Add(RoomWithNoNameMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionActivated, RoomWithNoNameReleaseReceipt,
                $"That is long enough. Take it to {assignment.HandoffDropName} now."));
        if (mission.State == Release1MissionState.Satisfied)
            messages.Add(RoomWithNoNameMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionCompleted, RoomWithNoNameDeliveredReceipt,
                RoomWithNoNameDeliveredText));
    }

    private static IReadOnlyList<Release1DesiredEntry> BuildRoomWithNoNameEntries(
        Release1MissionState missionState,
        Release1RoomWithNoNameAssignment assignment,
        bool custody,
        bool released,
        bool blocked,
        Release1WorldPoint? holdRoomMarker)
    {
        var collect = Copy($"Collect the consignment from {assignment.SourceDropName}");
        var hold = Copy(RoomWithNoNameHoldEntryText);
        var handoff = Copy($"Take it to {assignment.HandoffDropName}");
        var sourceMarker = new Release1WorldPoint((float)assignment.SourceDropX, (float)assignment.SourceDropY, (float)assignment.SourceDropZ);
        var handoffMarker = new Release1WorldPoint((float)assignment.HandoffDropX, (float)assignment.HandoffDropY, (float)assignment.HandoffDropZ);

        if (missionState == Release1MissionState.Satisfied)
            return new[]
            {
                new Release1DesiredEntry(collect, Release1DesiredEntryState.Complete, null),
                new Release1DesiredEntry(hold, Release1DesiredEntryState.Complete, null),
                new Release1DesiredEntry(handoff, Release1DesiredEntryState.Complete, null)
            };

        if (blocked)
            return new[]
            {
                custody
                    ? new Release1DesiredEntry(collect, Release1DesiredEntryState.Complete, null)
                    : new Release1DesiredEntry(collect, Release1DesiredEntryState.Active, sourceMarker),
                new Release1DesiredEntry(Copy(RoomWithNoNameHoldText), Release1DesiredEntryState.Active, null),
                new Release1DesiredEntry(handoff, Release1DesiredEntryState.Inactive, null)
            };

        if (released)
            return new[]
            {
                new Release1DesiredEntry(collect, Release1DesiredEntryState.Complete, null),
                new Release1DesiredEntry(hold, Release1DesiredEntryState.Complete, null),
                new Release1DesiredEntry(handoff, Release1DesiredEntryState.Active, handoffMarker)
            };

        if (custody)
            return new[]
            {
                new Release1DesiredEntry(collect, Release1DesiredEntryState.Complete, null),
                new Release1DesiredEntry(hold, Release1DesiredEntryState.Active, holdRoomMarker),
                new Release1DesiredEntry(handoff, Release1DesiredEntryState.Inactive, null)
            };

        return new[]
        {
            new Release1DesiredEntry(collect, Release1DesiredEntryState.Active, sourceMarker),
            new Release1DesiredEntry(hold, Release1DesiredEntryState.Inactive, null),
            new Release1DesiredEntry(handoff, Release1DesiredEntryState.Inactive, null)
        };
    }

    private static Release1DesiredMessage RoomWithNoNameMessage(string playerId, int attempt, Release1TransitionKind kind, string receipt, string text) =>
        new(Release1LogicalCorrelation.Create(playerId, Release1MissionCatalog.RoomWithNoName, attempt, kind, receipt).Value, Copy(text));

    private static void BuildShortNotice(
        Release1StoryState? story,
        Release1ShortNoticeViewModel shortNotice,
        Release1ShortNoticeAssignment? assignment,
        Release1ShortNoticeProgress? progress,
        List<Release1DesiredMessage> messages,
        List<Release1DesiredQuest> quests,
        ref Release1DesiredDecision? decision)
    {
        var mission = story?.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)];

        if (shortNotice.CanReview)
        {
            decision = new Release1DesiredDecision(
                ShortNoticeReviewDecisionId,
                Copy(shortNotice.Body),
                new[] { new Release1DecisionOption(Copy("Review terms"), Release1PresentationCommand.ShortNoticeReview) });
        }
        else if (shortNotice.CanAccept)
        {
            decision = new Release1DesiredDecision(
                ShortNoticeDecisionId,
                Copy(shortNotice.Body),
                new[]
                {
                    new Release1DecisionOption(Copy("Accept"), Release1PresentationCommand.ShortNoticeAccept),
                    new Release1DecisionOption(Copy("Not now"), Release1PresentationCommand.ShortNoticeDefer)
                });
        }

        if (story is null || mission is null || assignment is null) return;
        if (mission.State is not (Release1MissionState.Accepted or Release1MissionState.Active or
            Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive or Release1MissionState.Satisfied)) return;

        var blocked = story.NativeEffects.Any(effect =>
            effect.MissionKey == Release1MissionCatalog.ShortNotice &&
            effect.Attempt == mission.Attempt &&
            effect.ExecutionBlocked);

        quests.Add(new Release1DesiredQuest(
            Release1MissionCatalog.ShortNotice,
            Copy(ShortNoticeQuestTitle),
            BuildShortNoticeEntries(mission.State, assignment, progress?.LastShortfallNoticed, blocked)));

        if (mission.State is Release1MissionState.Accepted or Release1MissionState.Active or
            Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive)
            messages.Add(ShortNoticeMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionAccepted, ShortNoticeAcceptedReceipt,
                $"The drop is {assignment.HandoffDropName}. Leave the whole order in one slot."));
        if (progress?.SpreadNoticed == true)
            messages.Add(ShortNoticeMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionActivated, ShortNoticeSpreadReceipt,
                ShortNoticeSpreadText));
        if (progress?.LastShortfallNoticed is { } remaining && mission.State != Release1MissionState.Satisfied)
            messages.Add(ShortNoticeMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionActivated,
                ShortNoticeShortfallReceipt(remaining),
                remaining == 1 ? ShortNoticeShortfallOneText : string.Format(ShortNoticeShortfallManyFormat, remaining)));
        if (mission.State == Release1MissionState.Satisfied)
            messages.Add(ShortNoticeMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionCompleted, ShortNoticeDeliveredReceipt,
                ShortNoticeDeliveredText));
    }

    private static IReadOnlyList<Release1DesiredEntry> BuildShortNoticeEntries(
        Release1MissionState missionState,
        Release1ShortNoticeAssignment assignment,
        int? remaining,
        bool blocked)
    {
        var units = Release1ShortNoticePresentation.Units(assignment.RequiredQuantity, assignment.PackagingName);
        var open = Copy(string.Format(ShortNoticeEntryOpenFormat, units, assignment.ProductName, assignment.HandoffDropName));
        var marker = new Release1WorldPoint((float)assignment.HandoffDropX, (float)assignment.HandoffDropY, (float)assignment.HandoffDropZ);

        if (missionState == Release1MissionState.Satisfied)
            return new[] { new Release1DesiredEntry(open, Release1DesiredEntryState.Complete, null) };

        if (blocked)
            return new[] { new Release1DesiredEntry(Copy(ShortNoticeEntryHoldText), Release1DesiredEntryState.Active, null) };

        if (remaining is { } outstanding)
            return new[]
            {
                new Release1DesiredEntry(
                    Copy(string.Format(ShortNoticeEntryShortfallFormat, units, assignment.ProductName, assignment.HandoffDropName, outstanding)),
                    Release1DesiredEntryState.Active,
                    marker)
            };

        return new[] { new Release1DesiredEntry(open, Release1DesiredEntryState.Active, marker) };
    }

    private static Release1DesiredMessage ShortNoticeMessage(string playerId, int attempt, Release1TransitionKind kind, string receipt, string text) =>
        new(Release1LogicalCorrelation.Create(playerId, Release1MissionCatalog.ShortNotice, attempt, kind, receipt).Value, Copy(text));

    private static void BuildKeepTheLightsOff(
        Release1StoryState? story,
        Release1KeepTheLightsOffViewModel keepTheLightsOff,
        Release1KeepTheLightsOffAssignment? assignment,
        Release1KeepTheLightsOffProgress? progress,
        List<Release1DesiredMessage> messages,
        List<Release1DesiredQuest> quests,
        ref Release1DesiredDecision? decision)
    {
        var mission = story?.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];

        if (keepTheLightsOff.CanReview)
        {
            decision = new Release1DesiredDecision(
                KeepTheLightsOffReviewDecisionId,
                Copy(keepTheLightsOff.Body),
                new[] { new Release1DecisionOption(Copy("Review terms"), Release1PresentationCommand.KeepTheLightsOffReview) });
        }
        else if (keepTheLightsOff.CanAccept)
        {
            decision = new Release1DesiredDecision(
                KeepTheLightsOffDecisionId,
                Copy(keepTheLightsOff.Body),
                new[]
                {
                    new Release1DecisionOption(Copy("Accept"), Release1PresentationCommand.KeepTheLightsOffAccept),
                    new Release1DecisionOption(Copy("Not now"), Release1PresentationCommand.KeepTheLightsOffDefer)
                });
        }

        if (story is null || mission is null) return;
        if (mission.State == Release1MissionState.Satisfied)
        {
            quests.Add(new Release1DesiredQuest(
                Release1MissionCatalog.KeepTheLightsOff,
                Copy(KeepTheLightsOffQuestTitle),
                new[] { new Release1DesiredEntry(Copy(KeepTheLightsOffEntryOpenText), Release1DesiredEntryState.Complete, null) }));
            messages.Add(KeepTheLightsOffMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionCompleted,
                KeepTheLightsOffCompleteReceipt, KeepTheLightsOffCompletedText));
            return;
        }
        if (assignment is null) return;
        if (mission.State is not (Release1MissionState.Accepted or Release1MissionState.Active or
            Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive)) return;

        var blocked = story.NativeEffects.Any(effect =>
            effect.MissionKey == Release1MissionCatalog.KeepTheLightsOff &&
            effect.Attempt == mission.Attempt &&
            effect.ExecutionBlocked);

        quests.Add(new Release1DesiredQuest(
            Release1MissionCatalog.KeepTheLightsOff,
            Copy(KeepTheLightsOffQuestTitle),
            BuildKeepTheLightsOffEntries(mission.State, progress, blocked)));

        if (mission.State is Release1MissionState.Accepted or Release1MissionState.Active or
            Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive)
            messages.Add(KeepTheLightsOffMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionAccepted, KeepTheLightsOffAcceptedReceipt,
                KeepTheLightsOffAcceptedText));
        if (progress?.ClearConfirmedAtGameMinutes is not null)
            messages.Add(KeepTheLightsOffMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionActivated, KeepTheLightsOffClearReceipt,
                KeepTheLightsOffClearConfirmedText));
    }

    /// <summary>
    /// The Satisfied branch below is unreachable from production: BuildKeepTheLightsOff now handles
    /// Satisfied before this method is ever called (decision 14), so an assignment free attempt never
    /// reaches here. Left in place deliberately as a defensive fallback rather than deleted.
    /// </summary>
    private static IReadOnlyList<Release1DesiredEntry> BuildKeepTheLightsOffEntries(
        Release1MissionState missionState,
        Release1KeepTheLightsOffProgress? progress,
        bool blocked)
    {
        var open = Copy(KeepTheLightsOffEntryOpenText);

        if (missionState == Release1MissionState.Satisfied)
            return new[] { new Release1DesiredEntry(open, Release1DesiredEntryState.Complete, null) };

        if (blocked)
            return new[] { new Release1DesiredEntry(Copy(KeepTheLightsOffEntryHoldText), Release1DesiredEntryState.Active, null) };

        if (progress?.BreachSincePassGameMinutes is not null)
            return new[] { new Release1DesiredEntry(Copy(Release1KeepTheLightsOffPresentation.BreachStatusText), Release1DesiredEntryState.Active, null) };

        if (progress?.ClearConfirmedAtGameMinutes is not null)
            return new[] { new Release1DesiredEntry(Copy(KeepTheLightsOffEntryHoldingText), Release1DesiredEntryState.Active, null) };

        return new[] { new Release1DesiredEntry(open, Release1DesiredEntryState.Active, null) };
    }

    private static Release1DesiredMessage KeepTheLightsOffMessage(string playerId, int attempt, Release1TransitionKind kind, string receipt, string text) =>
        new(Release1LogicalCorrelation.Create(playerId, Release1MissionCatalog.KeepTheLightsOff, attempt, kind, receipt).Value, Copy(text));

    private static void BuildTheEnvelope(
        Release1StoryState? story,
        Release1TheEnvelopeViewModel theEnvelope,
        Release1TheEnvelopeAssignment? assignment,
        Release1TheEnvelopeProgress? progress,
        Release1WorldPoint? holdRoomMarker,
        List<Release1DesiredMessage> messages,
        List<Release1DesiredQuest> quests,
        ref Release1DesiredDecision? decision)
    {
        var mission = story?.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope)];

        if (theEnvelope.CanReview)
        {
            decision = new Release1DesiredDecision(
                TheEnvelopeReviewDecisionId,
                Copy(theEnvelope.Body),
                new[] { new Release1DecisionOption(Copy("Review terms"), Release1PresentationCommand.TheEnvelopeReview) });
        }
        else if (theEnvelope.CanAccept)
        {
            decision = new Release1DesiredDecision(
                TheEnvelopeDecisionId,
                Copy(theEnvelope.Body),
                new[]
                {
                    new Release1DecisionOption(Copy("Accept"), Release1PresentationCommand.TheEnvelopeAccept),
                    new Release1DecisionOption(Copy("Not now"), Release1PresentationCommand.TheEnvelopeDefer)
                });
        }

        if (story is null || mission is null || assignment is null) return;
        if (mission.State is not (Release1MissionState.Accepted or Release1MissionState.Active or
            Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive or Release1MissionState.Satisfied)) return;

        var blocked = story.NativeEffects.Any(effect =>
            effect.MissionKey == Release1MissionCatalog.TheEnvelope &&
            effect.Attempt == mission.Attempt &&
            effect.ExecutionBlocked);

        quests.Add(new Release1DesiredQuest(
            Release1MissionCatalog.TheEnvelope,
            Copy(TheEnvelopeQuestTitle),
            BuildTheEnvelopeEntries(mission.State, assignment, progress?.LastShortfallNoticed, blocked, holdRoomMarker)));

        if (mission.State is Release1MissionState.Accepted or Release1MissionState.Active or
            Release1MissionState.MakeGoodActive or Release1MissionState.RecoveryActive)
            messages.Add(TheEnvelopeMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionAccepted, TheEnvelopeAcceptedReceipt,
                TheEnvelopeAcceptedText(assignment.AmountWholeDollars)));
        if (progress?.SpreadNoticed == true)
            messages.Add(TheEnvelopeMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionActivated, TheEnvelopeSpreadReceipt,
                TheEnvelopeSpreadText));
        if (progress?.LastShortfallNoticed is { } remaining && mission.State != Release1MissionState.Satisfied)
            messages.Add(TheEnvelopeMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionActivated,
                TheEnvelopeShortfallReceipt(remaining),
                string.Format(remaining > 100d ? TheEnvelopeShortfallManyFormat : TheEnvelopeShortfallSmallFormat,
                    Release1TheEnvelopePresentation.Dollars(remaining))));
        if (mission.State == Release1MissionState.Satisfied)
        {
            messages.Add(TheEnvelopeMessage(
                story.PlayerId, mission.Attempt, Release1TransitionKind.MissionCompleted, TheEnvelopeDeliveredReceipt,
                TheEnvelopeDeliveredText));
            messages.Add(RecognitionMessage(story.PlayerId, TheEnvelopeRecognitionReceipt, TheEnvelopeRecognitionText));
        }
    }

    private static IReadOnlyList<Release1DesiredEntry> BuildTheEnvelopeEntries(
        Release1MissionState missionState,
        Release1TheEnvelopeAssignment assignment,
        double? remaining,
        bool blocked,
        Release1WorldPoint? holdRoomMarker)
    {
        var amount = Release1TheEnvelopePresentation.Dollars(assignment.AmountWholeDollars);
        var open = Copy(string.Format(TheEnvelopeEntryOpenFormat, amount));

        if (missionState == Release1MissionState.Satisfied)
            return new[] { new Release1DesiredEntry(open, Release1DesiredEntryState.Complete, null) };

        if (blocked)
            return new[] { new Release1DesiredEntry(Copy(TheEnvelopeEntryHoldText), Release1DesiredEntryState.Active, null) };

        if (remaining is { } outstanding)
            return new[]
            {
                new Release1DesiredEntry(
                    Copy(string.Format(TheEnvelopeEntryShortfallFormat, amount, Release1TheEnvelopePresentation.Dollars(outstanding))),
                    Release1DesiredEntryState.Active,
                    holdRoomMarker)
            };

        return new[] { new Release1DesiredEntry(open, Release1DesiredEntryState.Active, holdRoomMarker) };
    }

    private static Release1DesiredMessage TheEnvelopeMessage(string playerId, int attempt, Release1TransitionKind kind, string receipt, string text) =>
        new(Release1LogicalCorrelation.Create(playerId, Release1MissionCatalog.TheEnvelope, attempt, kind, receipt).Value, Copy(text));

    /// <summary>
    /// The recognition message's correlation mirrors <c>Release1StoryTransitions.CompleteMission</c>'s
    /// own construction of <c>Release1StoryState.RecognitionLogicalCorrelationIds</c>: intro scope
    /// key, attempt zero, <see cref="Release1TransitionKind.Release1Recognized"/>. This is the one
    /// planner message keyed to the intro scope rather than to The Envelope's own mission key,
    /// because it is a whole-story fact, not a per-mission one.
    /// </summary>
    private static Release1DesiredMessage RecognitionMessage(string playerId, string receipt, string text) =>
        new(Release1LogicalCorrelation.Create(playerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.Release1Recognized, receipt).Value, Copy(text));

    private static Release1DesiredMessage IntroMessage(string playerId, Release1TransitionKind kind, string receipt, string text) =>
        new(Release1LogicalCorrelation.Create(playerId, Release1MissionCatalog.IntroScopeKey, 0, kind, receipt).Value, Copy(text));

    private static Release1DesiredMessage SmallCourtesyMessage(string playerId, int attempt, Release1TransitionKind kind, string receipt, string text) =>
        new(Release1LogicalCorrelation.Create(playerId, Release1MissionCatalog.SmallCourtesy, attempt, kind, receipt).Value, Copy(text));

    private static IReadOnlyList<Release1DecisionOption> IntroAcceptOrDeferOptions() =>
        new[]
        {
            new Release1DecisionOption(Copy("Accept"), Release1PresentationCommand.IntroAccept),
            new Release1DecisionOption(Copy("Not now"), Release1PresentationCommand.IntroDefer)
        };

    private static string Copy(string value) => Release1PlayerCopy.Normalize(value);
}
