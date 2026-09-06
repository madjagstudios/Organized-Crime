using OrganizedCrime.Model;
using S1API.Entities;
using S1API.Messaging;
using S1API.Quests;
using NativeNpc = Il2CppScheduleOne.NPCs.NPC;
using NativeConversation = Il2CppScheduleOne.Messaging.MSGConversation;
using NativeMessage = Il2CppScheduleOne.Messaging.Message;

namespace OrganizedCrime.Runtime;

/// <summary>
/// The S1API/Unity-backed implementation of <see cref="IRelease1NativePresentation"/>: passive
/// messages and at most one decision prompt through the Nell contact (<see cref="Release1NellNpc"/>),
/// and Small Courtesy quest entries through <see cref="Release1SmallCourtesyQuest"/>. Every native
/// access is wrapped in a try block and mapped to <see cref="Release1NativePresentationStatus"/>;
/// nothing here is called from tests directly (this file is not linked into the test project), so
/// every piece of logic that can be pure lives in the test-linked
/// <see cref="Release1NativePresentationSupport"/> instead. No Harmony patches.
///
/// Nothing here survives a load by itself: <see cref="OnPreLoad"/> only drops in-memory state. S1API
/// re-instantiates <see cref="Release1NellNpc"/> and replays its restored responses onto that fresh
/// instance before the next reconcile pass; <see cref="TryEnsureContact"/> re-adopts it from
/// <see cref="NPC.All"/> by id on that pass. <see cref="TryReadDecision"/> observes exclusively from
/// native <c>MSGConversation.currentResponses</c>, never from anything S1API restored onto the fresh
/// instance; <see cref="TrySetDecision"/>'s bind-existing branch locates a restored
/// <see cref="Response"/> to reattach a callback to through <see cref="Release1NellNpc.LoadedResponses"/>
/// instead, which is the only remaining use of that restored state. Neither relies on any cross-load
/// bookkeeping.
/// </summary>
public sealed class S1ApiRelease1NativePresentation : IRelease1NativePresentation
{
    private readonly Release1QuestPersistencePolicy _policy;
    private readonly Action<string> _log;
    private readonly HashSet<string> _loggedMessages = new(StringComparer.Ordinal);
    private readonly string _contactId;
    private readonly Func<NPC> _createContact;
    private readonly Func<NPC, bool> _isContact;

    private NPC? _contact;
    private IRelease1MessagingNpc? _messaging;
    private Release1SmallCourtesyQuest? _smallCourtesyQuest;
    private Release1WrongAddressQuest? _wrongAddressQuest;
    private Release1RoomWithNoNameQuest? _roomWithNoNameQuest;
    private Release1ShortNoticeQuest? _shortNoticeQuest;
    private Release1KeepTheLightsOffQuest? _keepTheLightsOffQuest;
    private Release1TheEnvelopeQuest? _theEnvelopeQuest;
    private string? _decisionId;
    private Action<Release1PresentationCommand>? _onChosen;

    public S1ApiRelease1NativePresentation(Release1QuestPersistencePolicy policy, Action<string>? log = null)
        : this(policy, Release1NativePresentationSupport.NellNpcId, () => new Release1NellNpc(), npc => npc is Release1NellNpc, log) { }

    public S1ApiRelease1NativePresentation(
        Release1QuestPersistencePolicy policy, string contactId, Func<NPC> createContact,
        Func<NPC, bool> isContact, Action<string>? log = null)
    {
        _policy = policy;
        _contactId = contactId ?? throw new ArgumentNullException(nameof(contactId));
        _createContact = createContact ?? throw new ArgumentNullException(nameof(createContact));
        _isContact = isContact ?? throw new ArgumentNullException(nameof(isContact));
        _log = log ?? (_ => { });
    }

    public Release1NativePresentationStatus TryEnsureContact()
    {
        try
        {
            if (_contact is not null) return Release1NativePresentationStatus.Succeeded;

            var outcome = Release1NativePresentationSupport.ClassifyContact(
                SelectContactCandidates(), _contactId);

            switch (outcome)
            {
                case Release1ContactResolutionOutcome.Adopt:
                    _contact = FindContactById(_contactId);
                    _messaging = _contact as IRelease1MessagingNpc;
                    return _contact is not null && _messaging is not null
                        ? Release1NativePresentationStatus.Succeeded : Release1NativePresentationStatus.Unavailable;
                case Release1ContactResolutionOutcome.WrongType:
                    LogOnce($"Release 1 native presentation found an existing NPC with the id '{_contactId}' that is not this mod's contact type; refusing to create a second contact.");
                    return Release1NativePresentationStatus.Faulted;
                default:
                    var created = _createContact();
                    _contact = created;
                    _messaging = created as IRelease1MessagingNpc;
                    return Release1NativePresentationStatus.Succeeded;
            }
        }
        catch (Exception exception)
        {
            LogOnce($"Release 1 native presentation contact ensure faulted: {exception.GetType().Name}");
            return Release1NativePresentationStatus.Faulted;
        }
    }

    public Release1NativePresentationStatus TryReadSentMessages(out IReadOnlyList<string> texts)
    {
        texts = Array.Empty<string>();
        if (_contact is null) return Release1NativePresentationStatus.Unavailable;
        try
        {
            var conversation = ResolveConversation(_contact);
            if (conversation == null) return Release1NativePresentationStatus.Unavailable;
            var history = conversation.messageHistory;
            if (history == null) return Release1NativePresentationStatus.Unavailable;

            var raw = new List<string?>();
            foreach (var message in history)
            {
                if (message == null) continue;
                if (message.sender != NativeMessage.ESenderType.Other) continue;
                raw.Add(message.text);
            }

            texts = Release1NativePresentationSupport.NormalizeSentTexts(raw);
            return Release1NativePresentationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            LogOnce($"Release 1 native presentation read sent messages faulted: {exception.GetType().Name}");
            return Release1NativePresentationStatus.Faulted;
        }
    }

    public Release1NativePresentationStatus TryReadDecision(out Release1ObservedDecision? decision)
    {
        decision = null;
        if (_contact is null) return Release1NativePresentationStatus.Unavailable;
        try
        {
            var labels = ReadNativeResponseLabels(_contact);
            if (labels.Count == 0) return Release1NativePresentationStatus.Succeeded;

            var prompt = ReadMostRecentNpcMessageText(_contact);
            decision = new Release1ObservedDecision(_decisionId, labels, prompt);
            return Release1NativePresentationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            LogOnce($"Release 1 native presentation read decision faulted: {exception.GetType().Name}");
            return Release1NativePresentationStatus.Faulted;
        }
    }

    public Release1NativePresentationStatus TrySendMessage(string text)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(text);
            return SendNative(Release1PlayerCopy.Normalize(text), null);
        }
        catch (Exception exception)
        {
            LogOnce($"Release 1 native presentation send message faulted: {exception.GetType().Name}");
            return Release1NativePresentationStatus.Faulted;
        }
    }

    /// <summary>
    /// Binds the desired options to a label-&gt;command map, then either binds the callback onto
    /// existing native responses (when their labels already match, in order, so nothing is sent) or
    /// sends the prompt with fresh responses through the single <c>SendTextMessage</c> call site.
    /// </summary>
    public Release1NativePresentationStatus TrySetDecision(Release1DesiredDecision decision, Action<Release1PresentationCommand> onChosen)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(decision);
            ArgumentNullException.ThrowIfNull(onChosen);
            if (_contact is null || _messaging is null) return Release1NativePresentationStatus.Unavailable;

            var options = new Dictionary<string, Release1PresentationCommand>(StringComparer.Ordinal);
            var desiredLabels = new List<string>();
            foreach (var option in decision.Options)
            {
                var label = Release1PlayerCopy.Normalize(option.Label);
                options[label] = option.Command;
                desiredLabels.Add(label);
            }

            var existingLabels = ReadNativeResponseLabels(_contact);
            if (Release1NativePresentationSupport.ShouldBindExistingResponses(existingLabels, desiredLabels))
            {
                var availableLabels = new HashSet<string>(_messaging.LoadedResponses.Keys, StringComparer.Ordinal);
                if (!Release1NativePresentationSupport.AllDesiredLabelsAvailable(desiredLabels, availableLabels))
                {
                    // At least one desired label has no restored Response to bind a callback onto.
                    // Bind nothing and do not commit _decisionId/_onChosen: committing here would make
                    // the next pass's "observed.Id is not null" check in the projector treat this
                    // decision as already bound, when in fact no callback was ever attached, leaving
                    // the prompt inert for the rest of the session. Returning non-Succeeded instead
                    // lets the projector retry this same diff on its next reconcile pass.
                    LogOnce("Release 1 native presentation could not find a restored response for every desired decision option; deferring the bind to a later reconcile pass.");
                    return Release1NativePresentationStatus.Unavailable;
                }

                foreach (var label in desiredLabels)
                {
                    if (!Release1NativePresentationSupport.TryMatchRebindCommand(options, label, out var command)) continue;
                    if (!_messaging.TryGetResponse(label, out var response) || response is null) continue;
                    response.OnTriggered = () => InvokeChosen(command);
                }
            }
            else
            {
                var responses = new List<Response>();
                foreach (var label in desiredLabels)
                {
                    var command = options[label];
                    responses.Add(new Response
                    {
                        Label = label,
                        Text = label,
                        OnTriggered = () => InvokeChosen(command)
                    });
                }

                var status = SendNative(Release1PlayerCopy.Normalize(decision.Prompt), responses.ToArray());
                if (status != Release1NativePresentationStatus.Succeeded) return status;
            }

            _decisionId = decision.Id;
            _onChosen = onChosen;
            return Release1NativePresentationStatus.Succeeded;
        }
        catch (Exception exception)
        {
            LogOnce($"Release 1 native presentation set decision faulted: {exception.GetType().Name}");
            return Release1NativePresentationStatus.Faulted;
        }
    }

    public Release1NativePresentationStatus TryClearDecision()
    {
        try
        {
            var status = ClearNativeResponses();
            if (status == Release1NativePresentationStatus.Succeeded)
            {
                _decisionId = null;
                _onChosen = null;
            }
            return status;
        }
        catch (Exception exception)
        {
            LogOnce($"Release 1 native presentation clear decision faulted: {exception.GetType().Name}");
            return Release1NativePresentationStatus.Faulted;
        }
    }

    public Release1NativePresentationStatus TryReadQuest(string key, out Release1ObservedQuest? quest)
    {
        quest = null;
        try
        {
            switch (key)
            {
                case Release1MissionCatalog.SmallCourtesy:
                {
                    if (!TryResolveSmallCourtesyQuest(out var found)) return Release1NativePresentationStatus.Faulted;
                    if (found is null) return Release1NativePresentationStatus.Succeeded;
                    quest = new Release1ObservedQuest(key, found.ReadEntryStates());
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.WrongAddress:
                {
                    if (!TryResolveWrongAddressQuest(out var found)) return Release1NativePresentationStatus.Faulted;
                    if (found is null) return Release1NativePresentationStatus.Succeeded;
                    quest = new Release1ObservedQuest(key, found.ReadEntryStates());
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.RoomWithNoName:
                {
                    if (!TryResolveRoomWithNoNameQuest(out var found)) return Release1NativePresentationStatus.Faulted;
                    if (found is null) return Release1NativePresentationStatus.Succeeded;
                    quest = new Release1ObservedQuest(key, found.ReadEntryStates());
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.ShortNotice:
                {
                    if (!TryResolveShortNoticeQuest(out var found)) return Release1NativePresentationStatus.Faulted;
                    if (found is null) return Release1NativePresentationStatus.Succeeded;
                    quest = new Release1ObservedQuest(key, found.ReadEntryStates());
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.KeepTheLightsOff:
                {
                    if (!TryResolveKeepTheLightsOffQuest(out var found)) return Release1NativePresentationStatus.Faulted;
                    if (found is null) return Release1NativePresentationStatus.Succeeded;
                    quest = new Release1ObservedQuest(key, found.ReadEntryStates());
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.TheEnvelope:
                {
                    if (!TryResolveTheEnvelopeQuest(out var found)) return Release1NativePresentationStatus.Faulted;
                    if (found is null) return Release1NativePresentationStatus.Succeeded;
                    quest = new Release1ObservedQuest(key, found.ReadEntryStates());
                    return Release1NativePresentationStatus.Succeeded;
                }
                default:
                    return Release1NativePresentationStatus.Succeeded;
            }
        }
        catch (Exception exception)
        {
            LogOnce($"Release 1 native presentation read quest faulted: {exception.GetType().Name}");
            return Release1NativePresentationStatus.Faulted;
        }
    }

    public Release1NativePresentationStatus TryApplyQuest(Release1DesiredQuest quest)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(quest);
            switch (quest.Key)
            {
                case Release1MissionCatalog.SmallCourtesy:
                {
                    if (!TryResolveSmallCourtesyQuest(out var native)) return Release1NativePresentationStatus.Faulted;
                    native ??= CreateQuest<Release1SmallCourtesyQuest>();
                    if (native is null) return Release1NativePresentationStatus.Unavailable;
                    _smallCourtesyQuest = native;
                    native.Project(quest);
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.WrongAddress:
                {
                    if (!TryResolveWrongAddressQuest(out var native)) return Release1NativePresentationStatus.Faulted;
                    native ??= CreateQuest<Release1WrongAddressQuest>();
                    if (native is null) return Release1NativePresentationStatus.Unavailable;
                    _wrongAddressQuest = native;
                    native.Project(quest);
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.RoomWithNoName:
                {
                    if (!TryResolveRoomWithNoNameQuest(out var native)) return Release1NativePresentationStatus.Faulted;
                    native ??= CreateQuest<Release1RoomWithNoNameQuest>();
                    if (native is null) return Release1NativePresentationStatus.Unavailable;
                    _roomWithNoNameQuest = native;
                    native.Project(quest);
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.ShortNotice:
                {
                    if (!TryResolveShortNoticeQuest(out var native)) return Release1NativePresentationStatus.Faulted;
                    native ??= CreateQuest<Release1ShortNoticeQuest>();
                    if (native is null) return Release1NativePresentationStatus.Unavailable;
                    _shortNoticeQuest = native;
                    native.Project(quest);
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.KeepTheLightsOff:
                {
                    if (!TryResolveKeepTheLightsOffQuest(out var native)) return Release1NativePresentationStatus.Faulted;
                    native ??= CreateQuest<Release1KeepTheLightsOffQuest>();
                    if (native is null) return Release1NativePresentationStatus.Unavailable;
                    _keepTheLightsOffQuest = native;
                    native.Project(quest);
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.TheEnvelope:
                {
                    if (!TryResolveTheEnvelopeQuest(out var native)) return Release1NativePresentationStatus.Faulted;
                    native ??= CreateQuest<Release1TheEnvelopeQuest>();
                    if (native is null) return Release1NativePresentationStatus.Unavailable;
                    _theEnvelopeQuest = native;
                    native.Project(quest);
                    return Release1NativePresentationStatus.Succeeded;
                }
                default:
                    return Release1NativePresentationStatus.Rejected;
            }
        }
        catch (Exception exception)
        {
            LogOnce($"Release 1 native presentation apply quest faulted: {exception.GetType().Name}");
            return Release1NativePresentationStatus.Faulted;
        }
    }

    public Release1NativePresentationStatus TryEndQuest(string key)
    {
        try
        {
            switch (key)
            {
                case Release1MissionCatalog.SmallCourtesy:
                {
                    if (!TryResolveSmallCourtesyQuest(out var native)) return Release1NativePresentationStatus.Faulted;
                    if (native is null) return Release1NativePresentationStatus.Succeeded;
                    native.End();
                    _smallCourtesyQuest = null;
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.WrongAddress:
                {
                    if (!TryResolveWrongAddressQuest(out var native)) return Release1NativePresentationStatus.Faulted;
                    if (native is null) return Release1NativePresentationStatus.Succeeded;
                    native.End();
                    _wrongAddressQuest = null;
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.RoomWithNoName:
                {
                    if (!TryResolveRoomWithNoNameQuest(out var native)) return Release1NativePresentationStatus.Faulted;
                    if (native is null) return Release1NativePresentationStatus.Succeeded;
                    native.End();
                    _roomWithNoNameQuest = null;
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.ShortNotice:
                {
                    if (!TryResolveShortNoticeQuest(out var native)) return Release1NativePresentationStatus.Faulted;
                    if (native is null) return Release1NativePresentationStatus.Succeeded;
                    native.End();
                    _shortNoticeQuest = null;
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.KeepTheLightsOff:
                {
                    if (!TryResolveKeepTheLightsOffQuest(out var native)) return Release1NativePresentationStatus.Faulted;
                    if (native is null) return Release1NativePresentationStatus.Succeeded;
                    native.End();
                    _keepTheLightsOffQuest = null;
                    return Release1NativePresentationStatus.Succeeded;
                }
                case Release1MissionCatalog.TheEnvelope:
                {
                    if (!TryResolveTheEnvelopeQuest(out var native)) return Release1NativePresentationStatus.Faulted;
                    if (native is null) return Release1NativePresentationStatus.Succeeded;
                    native.End();
                    _theEnvelopeQuest = null;
                    return Release1NativePresentationStatus.Succeeded;
                }
                default:
                    return Release1NativePresentationStatus.Succeeded;
            }
        }
        catch (Exception exception)
        {
            LogOnce($"Release 1 native presentation end quest faulted: {exception.GetType().Name}");
            return Release1NativePresentationStatus.Faulted;
        }
    }

    /// <summary>
    /// Drops in-memory state only; performs no native mutation. S1API re-instantiates
    /// <see cref="Release1NellNpc"/> itself on the next load, and <see cref="TryEnsureContact"/>
    /// re-adopts it from <see cref="NPC.All"/> on the next reconcile pass, so nothing static needs
    /// clearing or re-arming here (or in a caller's Dispose).
    /// </summary>
    public void OnPreLoad()
    {
        _contact = null;
        _messaging = null;
        _smallCourtesyQuest = null;
        _wrongAddressQuest = null;
        _roomWithNoNameQuest = null;
        _shortNoticeQuest = null;
        _keepTheLightsOffQuest = null;
        _theEnvelopeQuest = null;
        _decisionId = null;
        _onChosen = null;
    }

    public void OnSaveStart()
    {
        try
        {
            if (_policy == Release1QuestPersistencePolicy.DisposablePerLoad)
            {
                TryEndQuest(Release1MissionCatalog.SmallCourtesy);
                TryEndQuest(Release1MissionCatalog.WrongAddress);
                TryEndQuest(Release1MissionCatalog.RoomWithNoName);
                TryEndQuest(Release1MissionCatalog.ShortNotice);
                TryEndQuest(Release1MissionCatalog.KeepTheLightsOff);
                TryEndQuest(Release1MissionCatalog.TheEnvelope);
            }
        }
        catch (Exception exception)
        {
            LogOnce($"Release 1 native presentation OnSaveStart faulted: {exception.GetType().Name}");
        }
    }

    private void InvokeChosen(Release1PresentationCommand command) => _onChosen?.Invoke(command);

    private Release1NativePresentationStatus SendNative(string text, Response[]? responses)
    {
        if (_contact is null) return Release1NativePresentationStatus.Unavailable;
        _contact.SendTextMessage(text, responses, 1f, false);
        return Release1NativePresentationStatus.Succeeded;
    }

    private Release1NativePresentationStatus ClearNativeResponses()
    {
        if (_contact is null) return Release1NativePresentationStatus.Unavailable;
        var conversation = ResolveConversation(_contact);
        if (conversation == null) return Release1NativePresentationStatus.Unavailable;
        conversation.ClearResponses(false);
        return Release1NativePresentationStatus.Succeeded;
    }

    /// <summary>
    /// Resolves the tracked Small Courtesy quest, adopting it from <see cref="QuestManager"/> by
    /// title when not already cached. Returns false (never creating) when a quest with the expected
    /// title exists but is not a <see cref="Release1SmallCourtesyQuest"/>; <paramref name="quest"/>
    /// is null in that case as well as the "no quest yet" case, distinguished by the return value.
    /// </summary>
    private bool TryResolveSmallCourtesyQuest(out Release1SmallCourtesyQuest? quest)
    {
        var ok = TryResolveQuest(ref _smallCourtesyQuest, Release1SmallCourtesyQuest.DisplayTitle);
        quest = _smallCourtesyQuest;
        return ok;
    }

    /// <summary>Resolves the tracked Wrong Address quest; mirrors <see cref="TryResolveSmallCourtesyQuest"/> exactly.</summary>
    private bool TryResolveWrongAddressQuest(out Release1WrongAddressQuest? quest)
    {
        var ok = TryResolveQuest(ref _wrongAddressQuest, Release1WrongAddressQuest.DisplayTitle);
        quest = _wrongAddressQuest;
        return ok;
    }

    /// <summary>Resolves the tracked Room With No Name quest; mirrors <see cref="TryResolveSmallCourtesyQuest"/> exactly.</summary>
    private bool TryResolveRoomWithNoNameQuest(out Release1RoomWithNoNameQuest? quest)
    {
        var ok = TryResolveQuest(ref _roomWithNoNameQuest, Release1RoomWithNoNameQuest.DisplayTitle);
        quest = _roomWithNoNameQuest;
        return ok;
    }

    /// <summary>Resolves the tracked Short Notice quest; mirrors <see cref="TryResolveSmallCourtesyQuest"/> exactly.</summary>
    private bool TryResolveShortNoticeQuest(out Release1ShortNoticeQuest? quest)
    {
        var ok = TryResolveQuest(ref _shortNoticeQuest, Release1ShortNoticeQuest.DisplayTitle);
        quest = _shortNoticeQuest;
        return ok;
    }

    /// <summary>Resolves the tracked Keep the Lights Off quest; mirrors <see cref="TryResolveSmallCourtesyQuest"/> exactly.</summary>
    private bool TryResolveKeepTheLightsOffQuest(out Release1KeepTheLightsOffQuest? quest)
    {
        var ok = TryResolveQuest(ref _keepTheLightsOffQuest, Release1KeepTheLightsOffQuest.DisplayTitle);
        quest = _keepTheLightsOffQuest;
        return ok;
    }

    /// <summary>Resolves the tracked The Envelope quest; mirrors <see cref="TryResolveSmallCourtesyQuest"/> exactly.</summary>
    private bool TryResolveTheEnvelopeQuest(out Release1TheEnvelopeQuest? quest)
    {
        var ok = TryResolveQuest(ref _theEnvelopeQuest, Release1TheEnvelopeQuest.DisplayTitle);
        quest = _theEnvelopeQuest;
        return ok;
    }

    /// <summary>
    /// Generalizes quest resolution over three catalog quest types: adopts a cached instance if one
    /// is already tracked, otherwise looks the quest up by title through <see cref="QuestManager"/>.
    /// Returns false (never creating) when a quest with the expected title exists but is not
    /// <typeparamref name="TQuest"/>; <paramref name="cache"/> stays null in that case as well as
    /// the "no quest yet" case, distinguished by the return value.
    /// </summary>
    private bool TryResolveQuest<TQuest>(ref TQuest? cache, string displayTitle) where TQuest : Quest
    {
        if (cache is not null) return true;

        var found = QuestManager.GetQuestByName(displayTitle);
        var outcome = Release1NativePresentationSupport.ClassifyQuestLookup(found is not null, found is TQuest);
        switch (outcome)
        {
            case Release1QuestResolutionOutcome.WrongType:
                LogOnce($"Release 1 native presentation found an existing quest titled '{displayTitle}' that is not a {typeof(TQuest).Name}; refusing to create a second quest.");
                return false;
            case Release1QuestResolutionOutcome.Found:
                cache = (TQuest)found!;
                return true;
            default:
                return true; // None: no quest yet, cache stays null, caller may create.
        }
    }

    private static TQuest CreateQuest<TQuest>() where TQuest : Quest =>
        (TQuest)QuestManager.CreateQuest(typeof(TQuest), null);

    private IEnumerable<(string? Id, bool IsContact)> SelectContactCandidates()
    {
        foreach (var npc in NPC.All) yield return (npc.ID, _isContact(npc));
    }

    private NPC? FindContactById(string id)
    {
        foreach (var npc in NPC.All)
            if (_isContact(npc) && string.Equals(npc.ID, id, StringComparison.Ordinal)) return npc;
        return null;
    }

    /// <summary>
    /// The labels currently observed for Nell's decision prompt, read exclusively from native
    /// <c>MSGConversation.currentResponses</c> and normalized through
    /// <see cref="Release1PlayerCopy.Normalize"/> so they compare symmetrically against the already-
    /// normalized desired labels. Empty whenever native reports nothing showing, including in the
    /// window right after a load before S1API has repopulated <c>currentResponses</c>: in that case
    /// there is genuinely nothing on screen yet, so reporting "no decision" is correct, and sending is
    /// the right response rather than treating a load-time snapshot as if it were still on screen.
    /// </summary>
    private static IReadOnlyList<string> ReadNativeResponseLabels(NPC contact)
    {
        var conversation = ResolveConversation(contact);
        if (conversation == null) return Array.Empty<string>();
        var responses = conversation.currentResponses;
        if (responses == null) return Array.Empty<string>();

        var labels = new List<string>();
        foreach (var response in responses)
        {
            if (response == null) continue;
            labels.Add(Release1PlayerCopy.Normalize(response.label ?? string.Empty));
        }
        return labels;
    }

    /// <summary>
    /// The normalized text of the most recently received NPC message in native conversation history,
    /// or null when there is none. Backs <see cref="Release1ObservedDecision.Prompt"/>: a decision's
    /// prompt is the message that carries it (S1API sends it via <c>NPC.SendTextMessage</c>), so the
    /// most recent NPC-sender message in history is that prompt, whether it landed there this session
    /// or was restored across a load.
    /// </summary>
    private static string? ReadMostRecentNpcMessageText(NPC contact)
    {
        var conversation = ResolveConversation(contact);
        if (conversation == null) return null;
        var history = conversation.messageHistory;
        if (history == null) return null;

        string? mostRecent = null;
        foreach (var message in history)
        {
            if (message == null) continue;
            if (message.sender != NativeMessage.ESenderType.Other) continue;
            mostRecent = message.text;
        }
        return mostRecent is null ? null : Release1PlayerCopy.Normalize(mostRecent);
    }

    private static NativeConversation? ResolveConversation(NPC npc)
    {
        var gameObject = npc.gameObject;
        if (gameObject == null) return null;
        var native = gameObject.GetComponent<NativeNpc>();
        return native == null ? null : native.MSGConversation;
    }

    private void LogOnce(string message)
    {
        if (!_loggedMessages.Add(message)) return;
        _log(message);
    }
}
