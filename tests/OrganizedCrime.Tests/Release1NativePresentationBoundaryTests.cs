using Xunit;

namespace OrganizedCrime.Tests;

/// <summary>
/// The S1API-dependent boundary files (<c>Release1NellNpc.cs</c>, <c>Release1SmallCourtesyQuest.cs</c>,
/// <c>S1ApiRelease1NativePresentation.cs</c>) are not linked into this test project, so this suite
/// verifies their shape through source-text assertions, the same convention
/// <see cref="Release1ProductionReachabilityTests"/> already uses for <c>Mod.cs</c>. Every read goes
/// through <see cref="ReadNormalized"/>, which strips <c>\r</c> so a CRLF checkout (this repo sets
/// <c>core.autocrlf=true</c>) still matches assertions written against <c>\n</c>-only source spans.
/// </summary>
public sealed class Release1NativePresentationBoundaryTests
{
    [Fact]
    public void Boundary_creates_Nell_exactly_once_guarded_by_an_NPC_All_lookup()
    {
        var nellSite = ReadNormalized(BoundaryPath);

        Assert.Equal(1, Count(nellSite, "new Release1NellNpc()"));
        Assert.Contains("Release1NativePresentationSupport.ClassifyContact(", nellSite, StringComparison.Ordinal);
        Assert.Contains("NPC.All", nellSite, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_never_creates_a_second_contact_or_quest_when_an_id_or_title_collides_with_the_wrong_type()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Contains("Release1ContactResolutionOutcome.WrongType", boundary, StringComparison.Ordinal);
        Assert.Contains("Release1QuestResolutionOutcome.WrongType", boundary, StringComparison.Ordinal);
        Assert.Contains("Release1NativePresentationSupport.ClassifyQuestLookup(", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_sends_native_messages_and_clears_responses_through_exactly_one_callsite_each()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Equal(1, Count(boundary, "SendTextMessage("));
        Assert.Equal(1, Count(boundary, "ClearResponses("));
    }

    [Fact]
    public void Boundary_files_contain_no_Harmony_patches_no_CreateSendableMessage_no_Cursor_and_only_one_NPC_subtype()
    {
        foreach (var path in new[] { NellPath, QuestPath, WrongAddressQuestPath, RoomWithNoNameQuestPath, ShortNoticeQuestPath, KeepTheLightsOffQuestPath, TheEnvelopeQuestPath, BoundaryPath })
        {
            var text = ReadNormalized(path);
            Assert.DoesNotContain("HarmonyPatch", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateSendableMessage", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Cursor.", text, StringComparison.Ordinal);
        }

        // Exactly one type derives from S1API's NPC across the eight boundary files (the Wrong
        // Address, Room With No Name, Short Notice, Keep the Lights Off, and The Envelope quests
        // derive from Quest, like the Small Courtesy quest, never from NPC).
        var total = Count(ReadNormalized(NellPath), ": NPC")
            + Count(ReadNormalized(QuestPath), ": NPC")
            + Count(ReadNormalized(WrongAddressQuestPath), ": NPC")
            + Count(ReadNormalized(RoomWithNoNameQuestPath), ": NPC")
            + Count(ReadNormalized(ShortNoticeQuestPath), ": NPC")
            + Count(ReadNormalized(KeepTheLightsOffQuestPath), ": NPC")
            + Count(ReadNormalized(TheEnvelopeQuestPath), ": NPC")
            + Count(ReadNormalized(BoundaryPath), ": NPC");
        Assert.Equal(1, total);
    }

    [Fact]
    public void Boundary_wraps_every_native_access_in_a_try_block()
    {
        var boundary = ReadNormalized(BoundaryPath);

        // A conservative, distinct-callsite count of native/S1API members the boundary touches;
        // every one of them is reached only from inside a try block in the production file.
        var nativeMemberCallsites = new[]
        {
            "TryEnsureContact", "TryReadSentMessages", "TryReadDecision", "TrySendMessage",
            "TrySetDecision", "TryClearDecision", "TryReadQuest", "TryApplyQuest", "TryEndQuest"
        };
        var catchCount = Count(boundary, "catch (Exception exception)");

        Assert.True(catchCount >= nativeMemberCallsites.Length,
            $"Expected at least {nativeMemberCallsites.Length} catch blocks (one per boundary method that touches native state), found {catchCount}.");
    }

    [Fact]
    public void Quest_is_located_by_title_since_the_installed_S1API_has_no_QuestName_attribute()
    {
        // Verified against the referenced S1API assembly (see task-5-report.md): the installed
        // build does have a [QuestName] attribute, but quests are still located through
        // QuestManager.GetQuestByName, which matches the quest's Title, because that lookup is the
        // proven path; the quest's Title doubles as its lookup key.
        var quest = ReadNormalized(QuestPath);
        var boundary = ReadNormalized(BoundaryPath);

        Assert.DoesNotContain("QuestName", quest, StringComparison.Ordinal);
        Assert.Contains("protected override string Title", quest, StringComparison.Ordinal);
        Assert.Contains("QuestManager.GetQuestByName(", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Nell_keys_loaded_responses_by_normalized_label_with_no_static_rebind_hook()
    {
        var nell = ReadNormalized(NellPath);
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Contains("protected override void OnResponseLoaded(Response response)", nell, StringComparison.Ordinal);
        Assert.Contains("_loadedResponses[Release1PlayerCopy.Normalize(label)] = response;", nell, StringComparison.Ordinal);
        Assert.Contains("LoadedResponses", nell, StringComparison.Ordinal);

        // TryGetResponse locates a restored response by normalized label, not by iterating the base
        // NPC's own (protected, non-public) Responses field, which gets cleared and replaced by
        // NPC.SendTextMessage on every fresh send and so does not reliably reflect what was restored.
        Assert.Contains(
            "public bool TryGetResponse(string label, out Response? response) =>\n        _loadedResponses.TryGetValue(Release1PlayerCopy.Normalize(label), out response);",
            nell, StringComparison.Ordinal);

        // Nothing static survives across loads: no static rebind hook anywhere in the three boundary files.
        Assert.DoesNotContain("static Action<string>? ResponseLoaded", nell, StringComparison.Ordinal);
        Assert.DoesNotContain("Release1NellNpc.ResponseLoaded", boundary, StringComparison.Ordinal);

        // OnPreLoad drops in-memory state without touching anything native.
        Assert.Contains("public void OnPreLoad()", boundary, StringComparison.Ordinal);
        Assert.Contains("_contact = null;", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_null_argument_checks_are_inside_the_try_block_so_no_public_method_can_throw()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Contains(
            "TrySendMessage(string text)\n    {\n        try\n        {\n            ArgumentNullException.ThrowIfNull(text);",
            boundary, StringComparison.Ordinal);
        Assert.Contains(
            "TrySetDecision(Release1DesiredDecision decision, Action<Release1PresentationCommand> onChosen)\n    {\n        try\n        {\n            ArgumentNullException.ThrowIfNull(decision);\n            ArgumentNullException.ThrowIfNull(onChosen);",
            boundary, StringComparison.Ordinal);
        Assert.Contains(
            "TryApplyQuest(Release1DesiredQuest quest)\n    {\n        try\n        {\n            ArgumentNullException.ThrowIfNull(quest);",
            boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_uses_native_null_semantics_for_Il2Cpp_types_instead_of_is_null()
    {
        var boundary = ReadNormalized(BoundaryPath);

        // Native MSGConversation/Message/Response/gameObject checks use == null (Unity/Il2Cpp
        // "fake null" semantics), never the `is null` pattern which bypasses overloaded operators.
        Assert.Contains("gameObject == null", boundary, StringComparison.Ordinal);
        Assert.Contains("conversation == null", boundary, StringComparison.Ordinal);
        Assert.Contains("history == null", boundary, StringComparison.Ordinal);
        Assert.Contains("message == null", boundary, StringComparison.Ordinal);
        Assert.Contains("response == null", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("conversation is null", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("conversation is not null", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("history is null", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("message is null", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Quest_entry_count_mismatch_throws_instead_of_silently_dropping_or_padding()
    {
        var quest = ReadNormalized(QuestPath);

        Assert.Contains("quest.Entries.Count != _entries.Length", quest, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException(", quest, StringComparison.Ordinal);
    }

    [Fact]
    public void Disposable_per_load_policy_ends_the_quest_on_save_start_and_persisted_policy_never_does()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Contains("public void OnSaveStart()", boundary, StringComparison.Ordinal);
        Assert.Contains("Release1QuestPersistencePolicy.DisposablePerLoad", boundary, StringComparison.Ordinal);
        Assert.Contains("TryEndQuest(Release1MissionCatalog.SmallCourtesy)", boundary, StringComparison.Ordinal);
        Assert.Contains("TryEndQuest(Release1MissionCatalog.ShortNotice);", boundary, StringComparison.Ordinal);
        Assert.Contains("TryEndQuest(Release1MissionCatalog.TheEnvelope);", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Observed_response_labels_are_read_from_native_currentResponses_only_and_normalized()
    {
        var boundary = ReadNormalized(BoundaryPath);

        // TryReadDecision/TrySetDecision observe exclusively through native currentResponses, run
        // through Release1PlayerCopy.Normalize so a native em/en dash still matches the already-
        // normalized desired labels. There is no loaded-response fallback for observation: an empty
        // native read means "no decision", full stop.
        Assert.Contains("labels.Add(Release1PlayerCopy.Normalize(response.label ?? string.Empty));", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadObservedResponseLabels(", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Loaded_response_observation_fallback_was_removed()
    {
        var boundary = ReadNormalized(BoundaryPath);
        var nell = ReadNormalized(NellPath);

        // The one-shot loaded-response fallback for TryReadDecision/TrySetDecision's observation of
        // what is on screen is gone entirely: no pending-consumption gate, no "consulted" latch, and
        // no fallback read of LoadedResponses.Keys as a source of observed labels. LoadedResponses
        // still exists on Release1NellNpc, but only to locate a restored Response for binding.
        Assert.DoesNotContain("_loadedResponsesPendingConsumption", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("_consultedLoadedResponses", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadedResponses.Keys.Select(Release1PlayerCopy.Normalize)", boundary, StringComparison.Ordinal);
        Assert.Contains("LoadedResponses", nell, StringComparison.Ordinal);
    }

    [Fact]
    public void TrySetDecision_binds_nothing_and_does_not_commit_the_decision_when_a_desired_label_has_no_restored_response()
    {
        var boundary = ReadNormalized(BoundaryPath);

        // Before mutating any restored Response, the bind-existing branch checks that every desired
        // label has a match; on a miss it returns without reaching the _decisionId/_onChosen commit
        // at the bottom of the method, so the projector retries the whole bind on its next pass
        // instead of treating a half-bound decision as settled.
        Assert.Contains("Release1NativePresentationSupport.AllDesiredLabelsAvailable(desiredLabels, availableLabels)", boundary, StringComparison.Ordinal);
        Assert.Contains(
            "if (!Release1NativePresentationSupport.AllDesiredLabelsAvailable(desiredLabels, availableLabels))",
            boundary, StringComparison.Ordinal);
    }

    // ---- Wrong Address quest routing (Task 5) -------------------------------------------------

    [Fact]
    public void Boundary_routes_quest_calls_by_key_to_two_different_quest_instances_and_rejects_unknown_keys()
    {
        var boundary = ReadNormalized(BoundaryPath);

        // TryReadQuest/TryApplyQuest/TryEndQuest each switch on the mission key (or quest.Key) over
        // both catalog missions, with a default arm that never touches either cached quest field.
        Assert.Contains("switch (key)", boundary, StringComparison.Ordinal);
        Assert.Contains("switch (quest.Key)", boundary, StringComparison.Ordinal);
        Assert.Equal(3, Count(boundary, "case Release1MissionCatalog.SmallCourtesy:"));
        Assert.Equal(3, Count(boundary, "case Release1MissionCatalog.WrongAddress:"));

        // Two distinct cached fields back the two quest types; neither is ever assigned to the
        // other's case arm.
        Assert.Contains("private Release1SmallCourtesyQuest? _smallCourtesyQuest;", boundary, StringComparison.Ordinal);
        Assert.Contains("private Release1WrongAddressQuest? _wrongAddressQuest;", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("private Release1SmallCourtesyQuest? _quest;", boundary, StringComparison.Ordinal);

        // Every switch not matching either mission key returns a non-throwing, non-mutating result:
        // Succeeded for read/end (nothing to report/end), Rejected for apply (never create for an
        // unknown key). TryApplyQuest's switch is the only one of the three whose default arm
        // returns Rejected (read/end default to Succeeded instead), so this occurrence is unique.
        Assert.Equal(1, Count(boundary, "return Release1NativePresentationStatus.Rejected;"));
    }

    [Fact]
    public void Boundary_creates_the_wrong_address_quest_exactly_once_guarded_by_a_title_lookup()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Equal(1, Count(boundary, "CreateQuest<Release1WrongAddressQuest>()"));
        Assert.Contains("native ??= CreateQuest<Release1WrongAddressQuest>();", boundary, StringComparison.Ordinal);
        // The create call is reached only after TryResolveWrongAddressQuest, which performs the
        // QuestManager.GetQuestByName title lookup before ever creating a second quest.
        Assert.Contains("TryResolveWrongAddressQuest(out var native)) return Release1NativePresentationStatus.Faulted;", boundary, StringComparison.Ordinal);
        Assert.Contains("QuestManager.GetQuestByName(displayTitle)", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Disposable_per_load_policy_ends_both_quests_on_save_start()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Contains("public void OnSaveStart()", boundary, StringComparison.Ordinal);
        Assert.Contains("Release1QuestPersistencePolicy.DisposablePerLoad", boundary, StringComparison.Ordinal);
        Assert.Contains("TryEndQuest(Release1MissionCatalog.SmallCourtesy);", boundary, StringComparison.Ordinal);
        Assert.Contains("TryEndQuest(Release1MissionCatalog.WrongAddress);", boundary, StringComparison.Ordinal);
        Assert.Contains("TryEndQuest(Release1MissionCatalog.RoomWithNoName);", boundary, StringComparison.Ordinal);
        Assert.Contains("TryEndQuest(Release1MissionCatalog.ShortNotice);", boundary, StringComparison.Ordinal);
        Assert.Contains("TryEndQuest(Release1MissionCatalog.TheEnvelope);", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void OnPreLoad_drops_both_cached_quest_fields()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Contains("_smallCourtesyQuest = null;", boundary, StringComparison.Ordinal);
        Assert.Contains("_wrongAddressQuest = null;", boundary, StringComparison.Ordinal);
        Assert.Contains("_roomWithNoNameQuest = null;", boundary, StringComparison.Ordinal);
        Assert.Contains("_shortNoticeQuest = null;", boundary, StringComparison.Ordinal);
        Assert.Contains("_theEnvelopeQuest = null;", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_address_quest_has_no_QuestName_attribute_and_its_display_title_differs_from_small_courtesys()
    {
        // Same convention as Release1SmallCourtesyQuest: the installed S1API build does have a
        // [QuestName] attribute, but lookup still goes through QuestManager.GetQuestByName
        // matching Title, because that lookup is the proven path; this quest has its own
        // Title/DisplayTitle so it never collides with the Small Courtesy quest's lookup key.
        var quest = ReadNormalized(WrongAddressQuestPath);
        var smallCourtesyQuest = ReadNormalized(QuestPath);

        Assert.DoesNotContain("QuestName", quest, StringComparison.Ordinal);
        Assert.Contains("protected override string Title", quest, StringComparison.Ordinal);
        Assert.Contains("public const string DisplayTitle = \"Wrong Address\";", quest, StringComparison.Ordinal);
        Assert.Contains("public const string DisplayTitle = \"Small Courtesy\";", smallCourtesyQuest, StringComparison.Ordinal);
        Assert.NotEqual(
            Release1WrongAddressQuestDisplayTitleLiteral(quest),
            Release1WrongAddressQuestDisplayTitleLiteral(smallCourtesyQuest));
    }

    // ---- Room With No Name quest routing and shape (Task 7) -----------------------------------

    [Fact]
    public void Room_with_no_name_quest_derives_from_quest_and_reports_its_display_title()
    {
        var quest = ReadNormalized(RoomWithNoNameQuestPath);

        Assert.Contains("public sealed class Release1RoomWithNoNameQuest : Quest", quest, StringComparison.Ordinal);
        Assert.Contains("public const string DisplayTitle = \"A Room With No Name\";", quest, StringComparison.Ordinal);
    }

    [Fact]
    public void Room_with_no_name_quest_entry_count_mismatch_throws_instead_of_silently_dropping_or_padding()
    {
        var quest = ReadNormalized(RoomWithNoNameQuestPath);

        Assert.Contains("quest.Entries.Count != _entries.Length", quest, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException(", quest, StringComparison.Ordinal);
        // Three entries created at construction, distinguishing this quest from the two-entry
        // Small Courtesy and Wrong Address quests.
        Assert.Equal(3, Count(quest, "AddEntry(placeholder, (Vector3?)null)"));
    }

    [Fact]
    public void Boundary_routes_room_with_no_name_quest_calls_by_key_alongside_the_other_two()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Equal(3, Count(boundary, "case Release1MissionCatalog.RoomWithNoName:"));
        Assert.Contains("private Release1RoomWithNoNameQuest? _roomWithNoNameQuest;", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_creates_the_room_with_no_name_quest_exactly_once_guarded_by_a_title_lookup()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Equal(1, Count(boundary, "CreateQuest<Release1RoomWithNoNameQuest>()"));
        Assert.Contains("native ??= CreateQuest<Release1RoomWithNoNameQuest>();", boundary, StringComparison.Ordinal);
        Assert.Contains("TryResolveRoomWithNoNameQuest(out var native)) return Release1NativePresentationStatus.Faulted;", boundary, StringComparison.Ordinal);
    }

    // ---- Short Notice quest routing and shape (Task 7) -----------------------------------------

    [Fact]
    public void Short_notice_quest_derives_from_quest_and_reports_its_display_title()
    {
        var quest = ReadNormalized(ShortNoticeQuestPath);

        Assert.Contains("public sealed class Release1ShortNoticeQuest : Quest", quest, StringComparison.Ordinal);
        Assert.Contains("public const string DisplayTitle = \"Short Notice\";", quest, StringComparison.Ordinal);
    }

    [Fact]
    public void Short_notice_quest_entry_count_mismatch_throws_instead_of_silently_dropping_or_padding()
    {
        var quest = ReadNormalized(ShortNoticeQuestPath);

        Assert.Contains("quest.Entries.Count != _entries.Length", quest, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException(", quest, StringComparison.Ordinal);
        // One entry created at construction: the whole manifest, in one slot, at the drop. This
        // distinguishes it from the two-entry Small Courtesy/Wrong Address quests and the three-entry
        // Room With No Name quest.
        Assert.Equal(1, Count(quest, "AddEntry(placeholder, (Vector3?)null)"));
    }

    [Fact]
    public void Boundary_routes_short_notice_quest_calls_by_key_alongside_the_other_three()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Equal(3, Count(boundary, "case Release1MissionCatalog.ShortNotice:"));
        Assert.Contains("private Release1ShortNoticeQuest? _shortNoticeQuest;", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_creates_the_short_notice_quest_exactly_once_guarded_by_a_title_lookup()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Equal(1, Count(boundary, "CreateQuest<Release1ShortNoticeQuest>()"));
        Assert.Contains("native ??= CreateQuest<Release1ShortNoticeQuest>();", boundary, StringComparison.Ordinal);
        Assert.Contains("TryResolveShortNoticeQuest(out var native)) return Release1NativePresentationStatus.Faulted;", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Short_notice_quest_has_no_QuestName_attribute_and_its_display_title_differs_from_the_others()
    {
        var quest = ReadNormalized(ShortNoticeQuestPath);
        var smallCourtesyQuest = ReadNormalized(QuestPath);
        var wrongAddressQuest = ReadNormalized(WrongAddressQuestPath);
        var roomWithNoNameQuest = ReadNormalized(RoomWithNoNameQuestPath);

        Assert.DoesNotContain("QuestName", quest, StringComparison.Ordinal);
        Assert.Contains("protected override string Title", quest, StringComparison.Ordinal);
        Assert.Contains("public const string DisplayTitle = \"Short Notice\";", quest, StringComparison.Ordinal);
        Assert.NotEqual(
            Release1WrongAddressQuestDisplayTitleLiteral(quest),
            Release1WrongAddressQuestDisplayTitleLiteral(smallCourtesyQuest));
        Assert.NotEqual(
            Release1WrongAddressQuestDisplayTitleLiteral(quest),
            Release1WrongAddressQuestDisplayTitleLiteral(wrongAddressQuest));
        Assert.NotEqual(
            Release1WrongAddressQuestDisplayTitleLiteral(quest),
            Release1WrongAddressQuestDisplayTitleLiteral(roomWithNoNameQuest));
    }

    // ---- Keep the Lights Off quest routing and shape (Task 6) ---------------------------------

    [Fact]
    public void Keep_the_lights_off_quest_derives_from_quest_and_reports_its_display_title()
    {
        var quest = ReadNormalized(KeepTheLightsOffQuestPath);

        Assert.Contains("public sealed class Release1KeepTheLightsOffQuest : Quest", quest, StringComparison.Ordinal);
        Assert.Contains("public const string DisplayTitle = \"Keep the Lights Off\";", quest, StringComparison.Ordinal);
    }

    [Fact]
    public void Keep_the_lights_off_quest_entry_count_mismatch_throws_instead_of_silently_dropping_or_padding()
    {
        var quest = ReadNormalized(KeepTheLightsOffQuestPath);

        Assert.Contains("quest.Entries.Count != _entries.Length", quest, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException(", quest, StringComparison.Ordinal);
        // One entry created at construction, mirroring the one-entry Short Notice quest exactly; the
        // entry never carries a marker at any stage, which the quest's Project/ApplyEntry code shares
        // unchanged with Short Notice (Marker is simply always null in every plan built for it).
        Assert.Equal(1, Count(quest, "AddEntry(placeholder, (Vector3?)null)"));
    }

    [Fact]
    public void Boundary_routes_keep_the_lights_off_quest_calls_by_key_alongside_the_other_four()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Equal(3, Count(boundary, "case Release1MissionCatalog.KeepTheLightsOff:"));
        Assert.Contains("private Release1KeepTheLightsOffQuest? _keepTheLightsOffQuest;", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_creates_the_keep_the_lights_off_quest_exactly_once_guarded_by_a_title_lookup()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Equal(1, Count(boundary, "CreateQuest<Release1KeepTheLightsOffQuest>()"));
        Assert.Contains("native ??= CreateQuest<Release1KeepTheLightsOffQuest>();", boundary, StringComparison.Ordinal);
        Assert.Contains("TryResolveKeepTheLightsOffQuest(out var native)) return Release1NativePresentationStatus.Faulted;", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Keep_the_lights_off_quest_has_no_QuestName_attribute_and_its_display_title_differs_from_the_others()
    {
        var quest = ReadNormalized(KeepTheLightsOffQuestPath);
        var smallCourtesyQuest = ReadNormalized(QuestPath);
        var wrongAddressQuest = ReadNormalized(WrongAddressQuestPath);
        var roomWithNoNameQuest = ReadNormalized(RoomWithNoNameQuestPath);
        var shortNoticeQuest = ReadNormalized(ShortNoticeQuestPath);

        Assert.DoesNotContain("QuestName", quest, StringComparison.Ordinal);
        Assert.Contains("protected override string Title", quest, StringComparison.Ordinal);
        Assert.Contains("public const string DisplayTitle = \"Keep the Lights Off\";", quest, StringComparison.Ordinal);
        Assert.NotEqual(
            Release1WrongAddressQuestDisplayTitleLiteral(quest),
            Release1WrongAddressQuestDisplayTitleLiteral(smallCourtesyQuest));
        Assert.NotEqual(
            Release1WrongAddressQuestDisplayTitleLiteral(quest),
            Release1WrongAddressQuestDisplayTitleLiteral(wrongAddressQuest));
        Assert.NotEqual(
            Release1WrongAddressQuestDisplayTitleLiteral(quest),
            Release1WrongAddressQuestDisplayTitleLiteral(roomWithNoNameQuest));
        Assert.NotEqual(
            Release1WrongAddressQuestDisplayTitleLiteral(quest),
            Release1WrongAddressQuestDisplayTitleLiteral(shortNoticeQuest));
    }

    [Fact]
    public void A_quest_titled_keep_the_lights_off_that_is_not_the_expected_type_is_refused_rather_than_duplicated()
    {
        var boundary = ReadNormalized(BoundaryPath);

        // TryResolveQuest<TQuest> (shared by all five quest resolvers) refuses to create a second
        // quest when a native quest with the expected title already exists but is the wrong CLR type;
        // Release1KeepTheLightsOffQuest resolves through that exact same generic helper, so this
        // wrong-type refusal already covers it, mirroring the assertion
        // Boundary_never_creates_a_second_contact_or_quest_when_an_id_or_title_collides_with_the_wrong_type
        // makes for the other four quests.
        Assert.Contains("Release1QuestResolutionOutcome.WrongType", boundary, StringComparison.Ordinal);
        Assert.Contains(
            "LogOnce($\"Release 1 native presentation found an existing quest titled '{displayTitle}' that is not a {typeof(TQuest).Name}; refusing to create a second quest.\");",
            boundary, StringComparison.Ordinal);
        Assert.Contains("private bool TryResolveKeepTheLightsOffQuest(out Release1KeepTheLightsOffQuest? quest)", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Disposable_per_load_policy_ends_the_keep_the_lights_off_quest_on_save_start_and_OnPreLoad_drops_its_cached_field()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Contains("TryEndQuest(Release1MissionCatalog.KeepTheLightsOff);", boundary, StringComparison.Ordinal);
        Assert.Contains("_keepTheLightsOffQuest = null;", boundary, StringComparison.Ordinal);
    }

    // ---- The Envelope quest routing and shape (Task 6) -----------------------------------------

    [Fact]
    public void The_envelope_quest_derives_from_quest_and_reports_its_display_title()
    {
        var quest = ReadNormalized(TheEnvelopeQuestPath);

        Assert.Contains("public sealed class Release1TheEnvelopeQuest : Quest", quest, StringComparison.Ordinal);
        Assert.Contains("public const string DisplayTitle = \"The Envelope\";", quest, StringComparison.Ordinal);
    }

    [Fact]
    public void The_envelope_quest_entry_count_mismatch_throws_instead_of_silently_dropping_or_padding()
    {
        var quest = ReadNormalized(TheEnvelopeQuestPath);

        Assert.Contains("quest.Entries.Count != _entries.Length", quest, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException(", quest, StringComparison.Ordinal);
        // One entry created at construction: the whole frozen cash amount, left in the closet.
        Assert.Equal(1, Count(quest, "AddEntry(placeholder, (Vector3?)null)"));
    }

    [Fact]
    public void Boundary_routes_the_envelope_quest_calls_by_key_alongside_the_other_four()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Equal(3, Count(boundary, "case Release1MissionCatalog.TheEnvelope:"));
        Assert.Contains("private Release1TheEnvelopeQuest? _theEnvelopeQuest;", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_creates_the_envelope_quest_exactly_once_guarded_by_a_title_lookup()
    {
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Equal(1, Count(boundary, "CreateQuest<Release1TheEnvelopeQuest>()"));
        Assert.Contains("native ??= CreateQuest<Release1TheEnvelopeQuest>();", boundary, StringComparison.Ordinal);
        Assert.Contains("TryResolveTheEnvelopeQuest(out var native)) return Release1NativePresentationStatus.Faulted;", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_envelope_quest_has_no_QuestName_attribute_and_its_display_title_differs_from_the_others()
    {
        var quest = ReadNormalized(TheEnvelopeQuestPath);
        var smallCourtesyQuest = ReadNormalized(QuestPath);
        var wrongAddressQuest = ReadNormalized(WrongAddressQuestPath);
        var roomWithNoNameQuest = ReadNormalized(RoomWithNoNameQuestPath);
        var shortNoticeQuest = ReadNormalized(ShortNoticeQuestPath);

        Assert.DoesNotContain("QuestName", quest, StringComparison.Ordinal);
        Assert.Contains("protected override string Title", quest, StringComparison.Ordinal);
        Assert.Contains("public const string DisplayTitle = \"The Envelope\";", quest, StringComparison.Ordinal);
        Assert.NotEqual(
            Release1WrongAddressQuestDisplayTitleLiteral(quest),
            Release1WrongAddressQuestDisplayTitleLiteral(smallCourtesyQuest));
        Assert.NotEqual(
            Release1WrongAddressQuestDisplayTitleLiteral(quest),
            Release1WrongAddressQuestDisplayTitleLiteral(wrongAddressQuest));
        Assert.NotEqual(
            Release1WrongAddressQuestDisplayTitleLiteral(quest),
            Release1WrongAddressQuestDisplayTitleLiteral(roomWithNoNameQuest));
        Assert.NotEqual(
            Release1WrongAddressQuestDisplayTitleLiteral(quest),
            Release1WrongAddressQuestDisplayTitleLiteral(shortNoticeQuest));
    }

    [Fact]
    public void A_quest_titled_the_envelope_that_is_not_the_envelope_type_is_refused_rather_than_duplicated()
    {
        // The Envelope's resolver routes through the same shared generic TryResolveQuest<TQuest> every
        // other quest type uses (see Boundary_never_creates_a_second_contact_or_quest_when_an_id_or_title_collides_with_the_wrong_type
        // for the WrongType outcome itself): a native quest titled "The Envelope" that is not a
        // Release1TheEnvelopeQuest is refused (WrongType), never silently duplicated by falling through
        // to CreateQuest<Release1TheEnvelopeQuest>().
        var boundary = ReadNormalized(BoundaryPath);

        Assert.Contains("TryResolveQuest(ref _theEnvelopeQuest, Release1TheEnvelopeQuest.DisplayTitle)", boundary, StringComparison.Ordinal);
        Assert.Contains("Release1QuestResolutionOutcome.WrongType", boundary, StringComparison.Ordinal);
    }

    private static string Release1WrongAddressQuestDisplayTitleLiteral(string source)
    {
        const string marker = "public const string DisplayTitle = \"";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "DisplayTitle constant not found.");
        start += marker.Length;
        var end = source.IndexOf('"', start);
        Assert.True(end >= 0, "DisplayTitle constant was not terminated.");
        return source[start..end];
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    /// <summary>Reads a source file and strips \r so \n-only expected spans match regardless of the checkout's line-ending config.</summary>
    private static string ReadNormalized(string path) => File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
        }
    }

    private static string NellPath => Path.Combine(RepositoryRoot, "tools", "OrganizedCrime", "Runtime", "Release1NellNpc.cs");
    private static string QuestPath => Path.Combine(RepositoryRoot, "tools", "OrganizedCrime", "Runtime", "Release1SmallCourtesyQuest.cs");
    private static string WrongAddressQuestPath => Path.Combine(RepositoryRoot, "tools", "OrganizedCrime", "Runtime", "Release1WrongAddressQuest.cs");
    private static string RoomWithNoNameQuestPath => Path.Combine(RepositoryRoot, "tools", "OrganizedCrime", "Runtime", "Release1RoomWithNoNameQuest.cs");
    private static string ShortNoticeQuestPath => Path.Combine(RepositoryRoot, "tools", "OrganizedCrime", "Runtime", "Release1ShortNoticeQuest.cs");
    private static string KeepTheLightsOffQuestPath => Path.Combine(RepositoryRoot, "tools", "OrganizedCrime", "Runtime", "Release1KeepTheLightsOffQuest.cs");
    private static string TheEnvelopeQuestPath => Path.Combine(RepositoryRoot, "tools", "OrganizedCrime", "Runtime", "Release1TheEnvelopeQuest.cs");
    private static string BoundaryPath => Path.Combine(RepositoryRoot, "tools", "OrganizedCrime", "Runtime", "S1ApiRelease1NativePresentation.cs");
}
