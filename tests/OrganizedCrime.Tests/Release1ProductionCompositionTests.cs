using System.Linq;
using System.Reflection;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ProductionCompositionTests
{
    [Fact]
    public void Native_mode_exposes_a_wrong_address_child_reachable_from_the_router_through_exactly_one_acceptance_attempt()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var native = new NativeFake();
        var logs = new List<string>();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            log: logs.Add,
            bannerNow: () => 0f,
            mode: Release1PresentationMode.Native,
            native: native);

        Assert.NotNull(composition.WrongAddress);

        composition.OnLoadComplete();
        composition.Update();
        Assert.NotNull(native.LastOnChosen);

        // Wrong Address is not eligible yet (Small Courtesy is not satisfied in this fixture), so this
        // proves the router reaches exactly the Wrong Address presenter's own TryAccept once, rather
        // than being silently dropped or routed to Small Courtesy, by the one decision-failed log line
        // only Release1WrongAddressPresenter.Handle emits.
        native.LastOnChosen!(Release1PresentationCommand.WrongAddressAccept);

        Assert.Equal(1, logs.Count(message => message.Contains("Wrong Address decision failed", StringComparison.Ordinal)));
    }

    [Fact]
    public void Fallback_mode_still_constructs_the_wrong_address_child_but_builds_no_wrong_address_surface_and_logs_once_per_load()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var logs = new List<string>();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            log: logs.Add,
            bannerNow: () => 0f,
            promptPlatform: new PromptPlatformFake());

        Assert.NotNull(composition.WrongAddress);

        composition.OnLoadComplete();

        var inputsMethod = typeof(Release1ProductionComposition).GetMethod(
            "BuildPresentationInputs", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var inputs = (Release1PresentationInputs)inputsMethod.Invoke(composition, null)!;
        Assert.Null(inputs.WrongAddress);
        // ImguiFallback mode builds no native surface for any mission, not only the one under test:
        // Keep the Lights Off's child is constructed too, but contributes nothing to the presentation
        // inputs while the mode is ImguiFallback.
        Assert.Null(inputs.KeepTheLightsOff);

        const string fallbackLine =
            "Wrong Address has no fallback presenter; the mission stays offered while the presentation mode is ImguiFallback.";
        Assert.Equal(1, logs.Count(message => message == fallbackLine));

        composition.OnLoadComplete();
        Assert.Equal(2, logs.Count(message => message == fallbackLine));
    }

    [Fact]
    public void Native_mode_exposes_a_room_with_no_name_child_reachable_from_the_router_through_exactly_one_acceptance_attempt()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var native = new NativeFake();
        var logs = new List<string>();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            log: logs.Add,
            bannerNow: () => 0f,
            mode: Release1PresentationMode.Native,
            native: native);

        Assert.NotNull(composition.RoomWithNoName);

        composition.OnLoadComplete();
        composition.Update();
        Assert.NotNull(native.LastOnChosen);

        // Room With No Name is not eligible yet (Wrong Address is not satisfied in this fixture), so
        // this proves the router reaches exactly the Room With No Name presenter's own TryAccept once,
        // rather than being silently dropped or routed to another mission, by the one decision-failed
        // log line only Release1RoomWithNoNamePresenter.Handle emits.
        native.LastOnChosen!(Release1PresentationCommand.RoomWithNoNameAccept);

        Assert.Equal(1, logs.Count(message => message.Contains("Room With No Name decision failed", StringComparison.Ordinal)));
    }

    [Fact]
    public void Fallback_mode_still_constructs_the_room_with_no_name_child_but_builds_no_room_surface_and_logs_once_per_load()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var logs = new List<string>();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            log: logs.Add,
            bannerNow: () => 0f,
            promptPlatform: new PromptPlatformFake());

        Assert.NotNull(composition.RoomWithNoName);

        composition.OnLoadComplete();

        var inputsMethod = typeof(Release1ProductionComposition).GetMethod(
            "BuildPresentationInputs", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var inputs = (Release1PresentationInputs)inputsMethod.Invoke(composition, null)!;
        Assert.Null(inputs.RoomWithNoName);
        Assert.Null(inputs.KeepTheLightsOff);

        var plan = Release1PresentationPlanner.Build(inputs);
        Assert.DoesNotContain(plan.Quests, quest => quest.Key == Release1MissionCatalog.RoomWithNoName);
        Assert.DoesNotContain(plan.Quests, quest => quest.Key == Release1MissionCatalog.KeepTheLightsOff);

        const string fallbackLine =
            "A Room With No Name has no fallback presenter; the mission stays offered while the presentation mode is ImguiFallback.";
        Assert.Equal(1, logs.Count(message => message == fallbackLine));

        composition.OnLoadComplete();
        Assert.Equal(2, logs.Count(message => message == fallbackLine));
    }

    [Fact]
    public void Native_mode_exposes_a_short_notice_child_reachable_from_the_router_through_exactly_one_acceptance_attempt()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var native = new NativeFake();
        var logs = new List<string>();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            log: logs.Add,
            bannerNow: () => 0f,
            mode: Release1PresentationMode.Native,
            native: native);

        Assert.NotNull(composition.ShortNotice);

        composition.OnLoadComplete();
        composition.Update();
        Assert.NotNull(native.LastOnChosen);

        // Short Notice is not eligible yet (Room With No Name is not satisfied in this fixture), so
        // this proves the router reaches exactly the Short Notice presenter's own TryAccept once,
        // rather than being silently dropped or routed to another mission, by the one decision-failed
        // log line only Release1ShortNoticePresenter.Handle emits.
        native.LastOnChosen!(Release1PresentationCommand.ShortNoticeAccept);

        Assert.Equal(1, logs.Count(message => message.Contains("Short Notice decision failed", StringComparison.Ordinal)));
    }

    [Fact]
    public void Fallback_mode_still_constructs_the_short_notice_child_but_builds_no_short_notice_surface_and_logs_once_per_load()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var logs = new List<string>();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            log: logs.Add,
            bannerNow: () => 0f,
            promptPlatform: new PromptPlatformFake());

        Assert.NotNull(composition.ShortNotice);

        composition.OnLoadComplete();

        var inputsMethod = typeof(Release1ProductionComposition).GetMethod(
            "BuildPresentationInputs", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var inputs = (Release1PresentationInputs)inputsMethod.Invoke(composition, null)!;
        Assert.Null(inputs.ShortNotice);
        Assert.Null(inputs.KeepTheLightsOff);

        var plan = Release1PresentationPlanner.Build(inputs);
        Assert.DoesNotContain(plan.Quests, quest => quest.Key == Release1MissionCatalog.ShortNotice);
        Assert.DoesNotContain(plan.Quests, quest => quest.Key == Release1MissionCatalog.KeepTheLightsOff);

        const string fallbackLine =
            "Short Notice has no fallback presenter; the mission stays offered while the presentation mode is ImguiFallback.";
        Assert.Equal(1, logs.Count(message => message == fallbackLine));

        composition.OnLoadComplete();
        Assert.Equal(2, logs.Count(message => message == fallbackLine));
    }

    [Fact]
    public void Native_mode_exposes_a_keep_the_lights_off_child_reachable_from_the_router_through_exactly_one_acceptance_attempt()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var native = new NativeFake();
        var logs = new List<string>();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            log: logs.Add,
            bannerNow: () => 0f,
            mode: Release1PresentationMode.Native,
            native: native);

        Assert.NotNull(composition.KeepTheLightsOff);

        composition.OnLoadComplete();
        composition.Update();
        Assert.NotNull(native.LastOnChosen);

        // Keep the Lights Off is not eligible yet (Short Notice is not satisfied in this fixture), so
        // this proves the router reaches exactly the Keep the Lights Off presenter's own TryAccept
        // once, rather than being silently dropped or routed to another mission, by the one decision-
        // failed log line only Release1KeepTheLightsOffPresenter.Handle emits.
        native.LastOnChosen!(Release1PresentationCommand.KeepTheLightsOffAccept);

        Assert.Equal(1, logs.Count(message => message.Contains("Keep the Lights Off decision failed", StringComparison.Ordinal)));
    }

    [Fact]
    public void Fallback_mode_still_constructs_the_keep_the_lights_off_child_but_builds_no_keep_the_lights_off_surface_and_logs_once_per_load()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var logs = new List<string>();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            log: logs.Add,
            bannerNow: () => 0f,
            promptPlatform: new PromptPlatformFake());

        Assert.NotNull(composition.KeepTheLightsOff);

        composition.OnLoadComplete();

        var inputsMethod = typeof(Release1ProductionComposition).GetMethod(
            "BuildPresentationInputs", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var inputs = (Release1PresentationInputs)inputsMethod.Invoke(composition, null)!;
        Assert.Null(inputs.KeepTheLightsOff);

        var plan = Release1PresentationPlanner.Build(inputs);
        Assert.DoesNotContain(plan.Quests, quest => quest.Key == Release1MissionCatalog.KeepTheLightsOff);

        const string fallbackLine =
            "Keep the Lights Off has no fallback presenter; the mission stays offered while the presentation mode is ImguiFallback.";
        Assert.Equal(1, logs.Count(message => message == fallbackLine));

        composition.OnLoadComplete();
        Assert.Equal(2, logs.Count(message => message == fallbackLine));
    }

    [Fact]
    public void Native_mode_exposes_a_the_envelope_child_reachable_from_the_router_through_exactly_one_acceptance_attempt()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var native = new NativeFake();
        var logs = new List<string>();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            log: logs.Add,
            bannerNow: () => 0f,
            mode: Release1PresentationMode.Native,
            native: native);

        Assert.NotNull(composition.TheEnvelope);

        composition.OnLoadComplete();
        composition.Update();
        Assert.NotNull(native.LastOnChosen);

        // The Envelope is not eligible yet (Keep the Lights Off is not satisfied in this fixture), so
        // this proves the router reaches exactly the The Envelope presenter's own TryAccept once,
        // rather than being silently dropped or routed to another mission, by the one decision-failed
        // log line only Release1TheEnvelopePresenter.Handle emits.
        native.LastOnChosen!(Release1PresentationCommand.TheEnvelopeAccept);

        Assert.Equal(1, logs.Count(message => message.Contains("The Envelope decision failed", StringComparison.Ordinal)));
    }

    [Fact]
    public void Fallback_mode_still_constructs_the_the_envelope_child_but_builds_no_the_envelope_surface_and_logs_once_per_load()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var logs = new List<string>();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            log: logs.Add,
            bannerNow: () => 0f,
            promptPlatform: new PromptPlatformFake());

        Assert.NotNull(composition.TheEnvelope);

        composition.OnLoadComplete();

        var inputsMethod = typeof(Release1ProductionComposition).GetMethod(
            "BuildPresentationInputs", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var inputs = (Release1PresentationInputs)inputsMethod.Invoke(composition, null)!;
        Assert.Null(inputs.TheEnvelope);

        var plan = Release1PresentationPlanner.Build(inputs);
        Assert.DoesNotContain(plan.Quests, quest => quest.Key == Release1MissionCatalog.TheEnvelope);

        const string fallbackLine =
            "The Envelope has no fallback presenter; the mission stays offered while the presentation mode is ImguiFallback.";
        Assert.Equal(1, logs.Count(message => message == fallbackLine));

        composition.OnLoadComplete();
        Assert.Equal(2, logs.Count(message => message == fallbackLine));
    }

    [Fact]
    public void No_hold_marker_delegate_leaves_the_hold_marker_null_without_logging()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var logs = new List<string>();
        var native = new NativeFake();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            log: logs.Add,
            bannerNow: () => 0f,
            mode: Release1PresentationMode.Native,
            native: native,
            holdRoomMarker: null);

        composition.OnLoadComplete();

        var inputsMethod = typeof(Release1ProductionComposition).GetMethod(
            "BuildPresentationInputs", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var inputs = (Release1PresentationInputs)inputsMethod.Invoke(composition, null)!;
        Assert.Null(inputs.HoldRoomMarker);

        const string missingMarkerLine =
            "The Syndicate HQ door has not resolved yet; the hold objective is showing without a marker.";
        Assert.DoesNotContain(logs, message => message == missingMarkerLine);
    }

    [Fact]
    public void A_hold_marker_delegate_that_resolves_to_null_leaves_the_hold_marker_null_and_logs_once_per_load()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var logs = new List<string>();
        var native = new NativeFake();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            log: logs.Add,
            bannerNow: () => 0f,
            mode: Release1PresentationMode.Native,
            native: native,
            holdRoomMarker: () => null);

        var inputsMethod = typeof(Release1ProductionComposition).GetMethod(
            "BuildPresentationInputs", BindingFlags.NonPublic | BindingFlags.Instance)!;

        const string missingMarkerLine =
            "The Syndicate HQ door has not resolved yet; the hold objective is showing without a marker.";

        composition.OnLoadComplete();
        var firstInputs = (Release1PresentationInputs)inputsMethod.Invoke(composition, null)!;
        Assert.Null(firstInputs.HoldRoomMarker);
        var secondInputs = (Release1PresentationInputs)inputsMethod.Invoke(composition, null)!;
        Assert.Null(secondInputs.HoldRoomMarker);
        Assert.Equal(1, logs.Count(message => message == missingMarkerLine));

        composition.OnPreLoad();
        composition.OnLoadComplete();
        _ = (Release1PresentationInputs)inputsMethod.Invoke(composition, null)!;
        Assert.Equal(2, logs.Count(message => message == missingMarkerLine));
    }

    // Release1PresentationInputs carries exactly one HoldRoomMarker field (Release1PresentationPlan.cs),
    // and Release1PresentationPlanner.Build reads that same property twice: once inside
    // BuildRoomWithNoName and once inside BuildTheEnvelope. Neither build has a marker source of its
    // own, so proving the composition resolves the delegate once into that single field is proving it
    // reaches both builds identically; the real story graph can never make Room With No Name's custody
    // entry and The Envelope's entry eligible at once (The Envelope requires Keep the Lights Off
    // satisfied, which itself requires Room With No Name already satisfied), so this is the only
    // fixture that can pin the shared wiring without contradicting the story's own linear order.
    [Fact]
    public void A_hold_marker_delegate_that_resolves_to_a_point_passes_the_identical_value_toward_both_the_room_with_no_name_and_the_envelope_build()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var logs = new List<string>();
        var native = new NativeFake();
        var marker = new Release1WorldPoint(1f, 2f, 3f);
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            log: logs.Add,
            bannerNow: () => 0f,
            mode: Release1PresentationMode.Native,
            native: native,
            holdRoomMarker: () => marker);

        composition.OnLoadComplete();

        var inputsMethod = typeof(Release1ProductionComposition).GetMethod(
            "BuildPresentationInputs", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var inputs = (Release1PresentationInputs)inputsMethod.Invoke(composition, null)!;

        Assert.Same(marker, inputs.HoldRoomMarker);

        const string missingMarkerLine =
            "The Syndicate HQ door has not resolved yet; the hold objective is showing without a marker.";
        Assert.DoesNotContain(logs, message => message == missingMarkerLine);
    }

    [Fact]
    public void Fallback_mode_exposes_the_prompt_host_and_no_projector()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            bannerNow: () => 0f,
            promptPlatform: new PromptPlatformFake());

        Assert.NotNull(composition.DecisionPromptHost);
        Assert.Null(composition.Projector);
    }

    [Fact]
    public void Native_mode_requires_a_native_implementation()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();

        var exception = Assert.Throws<ArgumentException>(() => new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            bannerNow: () => 0f,
            mode: Release1PresentationMode.Native,
            native: null));

        Assert.Contains("native", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Native_mode_exposes_a_projector_and_no_prompt_host_and_drives_intro_accept_through_the_native_decision_callback_with_no_intro_call_queued()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var queue = new QueueFake();
        var native = new NativeFake();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            queue,
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            bannerNow: () => 0f,
            mode: Release1PresentationMode.Native,
            native: native);

        Assert.NotNull(composition.Projector);
        Assert.Null(composition.DecisionPromptHost);

        composition.OnLoadComplete();
        composition.Update();

        Assert.NotNull(native.LastOnChosen);

        native.LastOnChosen!(Release1PresentationCommand.IntroAccept);

        Assert.Equal(Release1RelationshipState.Accepted, repository.StoredState!.RelationshipState);
        Assert.Equal(0, queue.Invocations);
    }

    [Fact]
    public void Small_courtesy_no_longer_exposes_a_persistent_card_draw_in_either_mode()
    {
        Assert.Null(typeof(Release1SmallCourtesyComposition).GetMethod("OnGUI"));
        Assert.Null(typeof(Release1SmallCourtesyPresenter).GetMethod("OnGUI"));
    }

    [Fact]
    public void Accepted_prompt_flows_through_durable_story_and_exactly_one_phone_boundary()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var queue = new QueueFake();
        var promptPlatform = new PromptPlatformFake();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            queue,
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            bannerNow: () => 0f,
            promptPlatform: promptPlatform);

        composition.OnLoadComplete();
        composition.Update();

        Assert.True(composition.Publisher.PromptVisible);
        Assert.False(composition.DecisionPromptHost!.ModalOpen);
        Assert.Empty(promptPlatform.Events);

        promptPlatform.NextInput = Release1DecisionPromptInput.Open;
        composition.Update();
        Assert.True(composition.DecisionPromptHost!.ModalOpen);

        promptPlatform.NextInput = Release1DecisionPromptInput.Primary;
        composition.Update();
        Assert.False(composition.Publisher.PromptVisible);
        Assert.False(composition.DecisionPromptHost!.ModalOpen);
        Assert.Equal(1, queue.Invocations);
        Assert.NotNull(repository.StoredState);
        Assert.Equal(Release1RelationshipState.Accepted, repository.StoredState!.RelationshipState);
        Assert.Equal(repository.StoredState.Revision, story.LastPersistedRevision);
        Assert.Single(repository.StoredState.PhonePresentationAttempts);
        Assert.Equal(Release1PhonePresentationAttemptState.Delivered, repository.StoredState.PhonePresentationAttempts[0].State);
    }

    [Fact]
    public void Preload_dismisses_the_transient_prompt_without_authoring_story_state()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var promptPlatform = new PromptPlatformFake();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            bannerNow: () => 0f,
            promptPlatform: promptPlatform);
        composition.OnLoadComplete();
        composition.Update();
        Assert.True(composition.Publisher.PromptVisible);
        promptPlatform.NextInput = Release1DecisionPromptInput.Open;
        composition.Update();
        Assert.True(composition.DecisionPromptHost!.ModalOpen);

        composition.OnPreLoad();

        Assert.False(composition.Publisher.PromptVisible);
        Assert.False(composition.DecisionPromptHost!.ModalOpen);
        Assert.Contains($"Remove:{Release1DecisionPromptHost.UiToken}", promptPlatform.Events);
        Assert.Null(repository.StoredState);
    }

    [Fact]
    public void Scene_change_releases_a_composed_modal_without_authoring_a_decision()
    {
        var context = new ContextFake();
        var repository = new RepositoryFake();
        using var story = new Release1StoryRuntimeService(context, repository);
        story.OnPreLoad();
        story.OnLoadComplete();
        var promptPlatform = new PromptPlatformFake();
        using var composition = new Release1ProductionComposition(
            story,
            new ReaderFake(context.Snapshot),
            new QueueFake(),
            new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot),
            bannerNow: () => 0f,
            promptPlatform: promptPlatform);
        composition.OnLoadComplete();
        composition.Update();
        promptPlatform.NextInput = Release1DecisionPromptInput.Open;
        composition.Update();

        composition.OnPreSceneChange();

        Assert.False(composition.DecisionPromptHost!.ModalOpen);
        Assert.Null(repository.StoredState);
    }

    // Task 6: the production composition never reaches into the shared window engine or reads
    // production activity directly; the mission service owns both, and this composition only ever
    // calls through Release1KeepTheLightsOffComposition/Service.
    //
    // OC-73 Task 5: the production composition also never mentions Chief Campbell. He is composed
    // beside it in Mod.cs (Release1ChiefComposition), never inside it, so the mission ladder, the
    // Standing table and the quest projection this file owns stay entirely untouched by him.
    [Fact]
    public void The_composition_never_reaches_the_shared_window_engine_or_the_production_read_directly()
    {
        var root = FindRepositoryRoot();
        var composition = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ProductionComposition.cs"));

        Assert.Equal(0, Count(composition, "Release1ConditionWindow"));
        Assert.Equal(0, Count(composition, "TryReadProductionActivity"));
        Assert.Equal(0, Count(composition, "Chief"));
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private sealed class ContextFake : IRelease1StoryHostContext
    {
        public Release1StoryHostContextSnapshot Snapshot { get; } = new(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            1,
            "76561190000000001",
            Path.GetFullPath(Path.GetTempPath()));

        public Release1StoryHostContextReadStatus TryRead(out Release1StoryHostContextSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Release1StoryHostContextReadStatus.Ready;
        }
    }

    private sealed class ReaderFake : IRelease1PostBenziesUnlockReader
    {
        private readonly Release1StoryHostContextSnapshot _context;
        public ReaderFake(Release1StoryHostContextSnapshot context) => _context = context;
        public Release1PostBenziesUnlockReadStatus TryRead(out Release1PostBenziesUnlockSnapshot snapshot)
        {
            snapshot = new(_context, Release1CartelStatus.Defeated);
            return Release1PostBenziesUnlockReadStatus.Unlocked;
        }
    }

    private sealed class QueueFake : IRelease1PhoneCallQueue
    {
        public int Invocations { get; private set; }
        public bool IsAvailable(Release1PhoneCallRequest request) => true;
        public void Invoke(Release1PhoneCallRequest request) => Invocations++;
    }

    private sealed class RepositoryFake : IRelease1StoryRepository, IRelease1StorySaveFolderBoundRepository
    {
        public string BoundSaveFolder => Path.GetFullPath(Path.GetTempPath());
        public Release1StoryState? StoredState { get; private set; }
        public Release1StoryStoreLoadResult Load() => new(
            true,
            StoredState is null ? Release1StoryStoreLoadStatus.Empty : Release1StoryStoreLoadStatus.Loaded,
            new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, StoredState),
            Release1StoryStoreFailureReason.None,
            string.Empty);
        public Release1StoryStoreUpdateResult Update(Release1StoryState? state)
        {
            StoredState = state;
            return new(
                true,
                Release1StoryStoreUpdateStatus.Updated,
                new Release1StorySaveEnvelope(Release1StorySaveCodec.CurrentSchemaVersion, state),
                Release1StoryStoreFailureReason.None,
                string.Empty);
        }
    }

    private sealed class NativeFake : IRelease1NativePresentation
    {
        private readonly List<string> _sent = new();
        public Release1ObservedDecision? ObservedDecision { get; private set; }
        public Action<Release1PresentationCommand>? LastOnChosen { get; private set; }

        public Release1NativePresentationStatus TryEnsureContact() => Release1NativePresentationStatus.Succeeded;

        public Release1NativePresentationStatus TryReadSentMessages(out IReadOnlyList<string> texts)
        {
            texts = _sent.ToArray();
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TryReadDecision(out Release1ObservedDecision? decision)
        {
            decision = ObservedDecision;
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TrySendMessage(string text)
        {
            _sent.Add(text);
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TrySetDecision(Release1DesiredDecision decision, Action<Release1PresentationCommand> onChosen)
        {
            _sent.Add(decision.Prompt);
            ObservedDecision = new Release1ObservedDecision(decision.Id, decision.Options.Select(option => option.Label).ToArray(), decision.Prompt);
            LastOnChosen = onChosen;
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TryClearDecision()
        {
            ObservedDecision = null;
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TryReadQuest(string key, out Release1ObservedQuest? quest)
        {
            quest = null;
            return Release1NativePresentationStatus.Succeeded;
        }

        public Release1NativePresentationStatus TryApplyQuest(Release1DesiredQuest quest) => Release1NativePresentationStatus.Succeeded;

        public Release1NativePresentationStatus TryEndQuest(string key) => Release1NativePresentationStatus.Succeeded;

        public void OnPreLoad() { }
        public void OnSaveStart() { }
    }

    private sealed class PromptPlatformFake : IRelease1DecisionPromptPlatform
    {
        public bool CameraAvailable => true;
        public bool VanillaUiOwnsCursor => false;
        public Release1CursorLockState CursorLockState { get; private set; } = Release1CursorLockState.Locked;
        public bool CursorVisible { get; private set; }
        public string OpenKeyLabel => "F7";
        public Release1DecisionPromptInput NextInput { get; set; }
        public List<string> Events { get; } = new();

        public Release1DecisionPromptInput ReadInput(bool modalOpen)
        {
            var input = NextInput;
            NextInput = Release1DecisionPromptInput.None;
            return input;
        }

        public void AddActiveUiElement(string token) => Events.Add($"Add:{token}");
        public void RemoveActiveUiElement(string token) => Events.Add($"Remove:{token}");
        public void SetCanLook(bool canLook) => Events.Add($"Look:{canLook}");
        public void FreeMouse() => Events.Add("FreeMouse");
        public void LockMouse() => Events.Add("LockMouse");
        public void SetCursor(Release1CursorLockState lockState, bool visible)
        {
            CursorLockState = lockState;
            CursorVisible = visible;
            Events.Add($"Cursor:{lockState}:{visible}");
        }
    }
}
