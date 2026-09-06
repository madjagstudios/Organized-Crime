using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1NativePresentationSupportTests
{
    [Theory]
    [InlineData("S1ApiPersisted", Release1QuestPersistencePolicy.S1ApiPersisted)]
    [InlineData("s1apipersisted", Release1QuestPersistencePolicy.S1ApiPersisted)]
    [InlineData("S1APIPERSISTED", Release1QuestPersistencePolicy.S1ApiPersisted)]
    public void QuestPersistencePolicyParser_maps_s1api_persisted_ordinal_ignore_case(
        string value, Release1QuestPersistencePolicy expected)
    {
        Assert.Equal(expected, Release1QuestPersistencePolicyParser.Parse(value));
    }

    [Theory]
    [InlineData("DisposablePerLoad")]
    [InlineData("disposableperload")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData(" S1ApiPersisted")]
    public void QuestPersistencePolicyParser_maps_everything_else_to_disposable_per_load(string? value)
    {
        Assert.Equal(Release1QuestPersistencePolicy.DisposablePerLoad, Release1QuestPersistencePolicyParser.Parse(value));
    }

    [Fact]
    public void QuestPersistencePolicyParser_maps_unknown_value_to_disposable_per_load()
    {
        Assert.Equal(Release1QuestPersistencePolicy.DisposablePerLoad, Release1QuestPersistencePolicyParser.Parse("not-a-real-policy"));
    }

    [Fact]
    public void ClassifyContact_is_case_sensitive()
    {
        var result = Release1NativePresentationSupport.ClassifyContact(
            new (string?, bool)[] { ("OC_RELEASE1_NELL", true) }, Release1NativePresentationSupport.NellNpcId);
        Assert.Equal(Release1ContactResolutionOutcome.Create, result);
    }

    [Fact]
    public void NormalizeSentTexts_normalizes_em_and_en_dashes_and_drops_nulls()
    {
        var result = Release1NativePresentationSupport.NormalizeSentTexts(new[]
        {
            "Wait for it — payment is close",
            null,
            "Fine – understood"
        });

        Assert.Equal(new[] { "Wait for it - payment is close", "Fine - understood" }, result);
    }

    [Fact]
    public void NormalizeSentTexts_rejects_null_source()
    {
        Assert.Throws<ArgumentNullException>(() => Release1NativePresentationSupport.NormalizeSentTexts(null!));
    }

    [Theory]
    [InlineData(Release1DesiredEntryState.Active, Release1QuestEntryProjectionAction.Begin)]
    [InlineData(Release1DesiredEntryState.Complete, Release1QuestEntryProjectionAction.Complete)]
    [InlineData(Release1DesiredEntryState.Inactive, Release1QuestEntryProjectionAction.SetInactive)]
    public void ClassifyEntryAction_maps_each_desired_state(Release1DesiredEntryState state, Release1QuestEntryProjectionAction expected)
    {
        Assert.Equal(expected, Release1NativePresentationSupport.ClassifyEntryAction(state));
    }

    [Fact]
    public void TryMatchRebindCommand_finds_the_command_bound_to_the_label()
    {
        var options = new Dictionary<string, Release1PresentationCommand>(StringComparer.Ordinal)
        {
            ["Accept"] = Release1PresentationCommand.IntroAccept,
            ["Not now"] = Release1PresentationCommand.IntroDefer
        };

        var found = Release1NativePresentationSupport.TryMatchRebindCommand(options, "Not now", out var command);

        Assert.True(found);
        Assert.Equal(Release1PresentationCommand.IntroDefer, command);
    }

    [Fact]
    public void TryMatchRebindCommand_returns_false_for_an_unknown_label()
    {
        var options = new Dictionary<string, Release1PresentationCommand>(StringComparer.Ordinal)
        {
            ["Accept"] = Release1PresentationCommand.IntroAccept
        };

        Assert.False(Release1NativePresentationSupport.TryMatchRebindCommand(options, "Unknown", out _));
    }

    [Fact]
    public void TryMatchRebindCommand_returns_false_when_options_or_label_are_null()
    {
        Assert.False(Release1NativePresentationSupport.TryMatchRebindCommand(null, "Accept", out _));

        var options = new Dictionary<string, Release1PresentationCommand>(StringComparer.Ordinal)
        {
            ["Accept"] = Release1PresentationCommand.IntroAccept
        };
        Assert.False(Release1NativePresentationSupport.TryMatchRebindCommand(options, null, out _));
    }

    [Fact]
    public void TryMatchRebindCommand_finds_the_command_once_per_label_present_in_the_map()
    {
        var options = new Dictionary<string, Release1PresentationCommand>(StringComparer.Ordinal)
        {
            ["Accept"] = Release1PresentationCommand.IntroAccept,
            ["Not now"] = Release1PresentationCommand.IntroDefer
        };

        Assert.True(Release1NativePresentationSupport.TryMatchRebindCommand(options, "Accept", out var accept));
        Assert.Equal(Release1PresentationCommand.IntroAccept, accept);
        Assert.True(Release1NativePresentationSupport.TryMatchRebindCommand(options, "Not now", out var defer));
        Assert.Equal(Release1PresentationCommand.IntroDefer, defer);
        Assert.False(Release1NativePresentationSupport.TryMatchRebindCommand(options, "Unrecognized", out _));
    }

    [Fact]
    public void ClassifyContact_returns_create_when_no_id_matches()
    {
        var result = Release1NativePresentationSupport.ClassifyContact(
            new (string?, bool)[] { ("some_other_npc", false), ("another_npc", true) },
            Release1NativePresentationSupport.NellNpcId);
        Assert.Equal(Release1ContactResolutionOutcome.Create, result);
    }

    [Fact]
    public void ClassifyContact_returns_create_for_an_empty_candidate_list()
    {
        var result = Release1NativePresentationSupport.ClassifyContact(
            Array.Empty<(string?, bool)>(), Release1NativePresentationSupport.NellNpcId);
        Assert.Equal(Release1ContactResolutionOutcome.Create, result);
    }

    [Fact]
    public void ClassifyContact_returns_adopt_when_the_matching_id_is_a_nell_instance()
    {
        var result = Release1NativePresentationSupport.ClassifyContact(
            new (string?, bool)[] { ("some_other_npc", false), (Release1NativePresentationSupport.NellNpcId, true) },
            Release1NativePresentationSupport.NellNpcId);
        Assert.Equal(Release1ContactResolutionOutcome.Adopt, result);
    }

    [Fact]
    public void ClassifyContact_returns_wrong_type_when_the_matching_id_is_not_a_nell_instance()
    {
        var result = Release1NativePresentationSupport.ClassifyContact(
            new (string?, bool)[] { (Release1NativePresentationSupport.NellNpcId, false) },
            Release1NativePresentationSupport.NellNpcId);
        Assert.Equal(Release1ContactResolutionOutcome.WrongType, result);
    }

    [Fact]
    public void ClassifyContact_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Release1NativePresentationSupport.ClassifyContact(null!, Release1NativePresentationSupport.NellNpcId));
        Assert.Throws<ArgumentNullException>(() =>
            Release1NativePresentationSupport.ClassifyContact(Array.Empty<(string?, bool)>(), null!));
    }

    [Fact]
    public void Classify_contact_adopts_only_the_candidate_whose_id_matches_the_target()
    {
        var candidates = new[]
        {
            ((string?)Release1NativePresentationSupport.NellNpcId, true),
            ((string?)Release1NativePresentationSupport.ChiefNpcId, true)
        };
        Assert.Equal(Release1ContactResolutionOutcome.Adopt,
            Release1NativePresentationSupport.ClassifyContact(candidates, Release1NativePresentationSupport.ChiefNpcId));
        Assert.Equal(Release1ContactResolutionOutcome.Adopt,
            Release1NativePresentationSupport.ClassifyContact(candidates, Release1NativePresentationSupport.NellNpcId));
    }

    [Fact]
    public void The_two_contact_ids_are_distinct_so_neither_boundary_can_adopt_the_other()
    {
        Assert.NotEqual(Release1NativePresentationSupport.NellNpcId, Release1NativePresentationSupport.ChiefNpcId);
        Assert.Equal("oc_release1_chief_campbell", Release1NativePresentationSupport.ChiefNpcId);
        Assert.Equal(Release1ContactResolutionOutcome.Create,
            Release1NativePresentationSupport.ClassifyContact(
                new[] { ((string?)Release1NativePresentationSupport.NellNpcId, true) },
                Release1NativePresentationSupport.ChiefNpcId));
    }

    [Fact]
    public void ClassifyQuestLookup_returns_none_when_no_quest_exists()
    {
        Assert.Equal(Release1QuestResolutionOutcome.None, Release1NativePresentationSupport.ClassifyQuestLookup(false, false));
        Assert.Equal(Release1QuestResolutionOutcome.None, Release1NativePresentationSupport.ClassifyQuestLookup(false, true));
    }

    [Fact]
    public void ClassifyQuestLookup_returns_found_when_a_quest_exists_and_is_the_expected_type()
    {
        Assert.Equal(Release1QuestResolutionOutcome.Found, Release1NativePresentationSupport.ClassifyQuestLookup(true, true));
    }

    [Fact]
    public void ClassifyQuestLookup_returns_wrong_type_when_a_quest_exists_but_is_not_the_expected_type()
    {
        Assert.Equal(Release1QuestResolutionOutcome.WrongType, Release1NativePresentationSupport.ClassifyQuestLookup(true, false));
    }

    [Fact]
    public void ShouldBindExistingResponses_is_true_when_labels_match_in_order()
    {
        Assert.True(Release1NativePresentationSupport.ShouldBindExistingResponses(
            new[] { "Accept", "Not now" }, new[] { "Accept", "Not now" }));
    }

    [Fact]
    public void ShouldBindExistingResponses_is_false_when_labels_differ_or_are_reordered()
    {
        Assert.False(Release1NativePresentationSupport.ShouldBindExistingResponses(
            new[] { "Accept" }, new[] { "Accept", "Not now" }));
        Assert.False(Release1NativePresentationSupport.ShouldBindExistingResponses(
            new[] { "Not now", "Accept" }, new[] { "Accept", "Not now" }));
        Assert.False(Release1NativePresentationSupport.ShouldBindExistingResponses(
            Array.Empty<string>(), new[] { "Accept" }));
    }

    [Fact]
    public void ShouldBindExistingResponses_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Release1NativePresentationSupport.ShouldBindExistingResponses(null!, Array.Empty<string>()));
        Assert.Throws<ArgumentNullException>(() =>
            Release1NativePresentationSupport.ShouldBindExistingResponses(Array.Empty<string>(), null!));
    }

    [Fact]
    public void Normalized_dash_variant_label_matches_the_already_normalized_desired_label()
    {
        // A restored native label carrying an em or en dash normalizes to the same text as the
        // already-normalized desired label, so a lookup keyed by normalized label (as
        // Release1NellNpc.LoadedResponses and TryGetResponse now are) still finds it.
        var desiredLabel = Release1PlayerCopy.Normalize("Wait for it - payment is close");
        var restoredNativeLabel = "Wait for it — payment is close";

        Assert.Equal(desiredLabel, Release1PlayerCopy.Normalize(restoredNativeLabel));
    }

    [Fact]
    public void AllDesiredLabelsAvailable_is_true_when_every_desired_label_has_a_normalized_match()
    {
        var available = new HashSet<string>(StringComparer.Ordinal) { "Accept", "Not now" };

        Assert.True(Release1NativePresentationSupport.AllDesiredLabelsAvailable(
            new[] { "Accept", "Not now" }, available));
    }

    [Fact]
    public void AllDesiredLabelsAvailable_is_false_when_any_desired_label_has_no_match()
    {
        var available = new HashSet<string>(StringComparer.Ordinal) { "Accept" };

        Assert.False(Release1NativePresentationSupport.AllDesiredLabelsAvailable(
            new[] { "Accept", "Not now" }, available));
    }

    [Fact]
    public void AllDesiredLabelsAvailable_is_true_for_an_empty_desired_label_list()
    {
        var available = new HashSet<string>(StringComparer.Ordinal);

        Assert.True(Release1NativePresentationSupport.AllDesiredLabelsAvailable(Array.Empty<string>(), available));
    }

    [Fact]
    public void AllDesiredLabelsAvailable_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Release1NativePresentationSupport.AllDesiredLabelsAvailable(null!, new HashSet<string>()));
        Assert.Throws<ArgumentNullException>(() =>
            Release1NativePresentationSupport.AllDesiredLabelsAvailable(Array.Empty<string>(), null!));
    }
}
