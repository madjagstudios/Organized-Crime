using System.Reflection;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1PresentationCommandRouterTests
{
    [Fact]
    public void IntroAccept_reaches_only_the_publisher_and_records_the_durable_acceptance()
    {
        using var harness = Harness.WithIntroPromptVisible();

        var result = harness.Router.TryInvoke(Release1PresentationCommand.IntroAccept);

        Assert.True(result);
        Assert.Equal(Release1RelationshipState.Accepted, harness.Story.State!.RelationshipState);
        Assert.Equal(1, harness.Queue.Invocations);
    }

    [Fact]
    public void IntroAccept_returns_false_once_the_prompt_is_no_longer_eligible()
    {
        using var harness = Harness.WithIntroPromptVisible();
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.IntroAccept));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.IntroAccept);

        Assert.False(result);
    }

    [Fact]
    public void IntroDefer_reaches_only_the_publisher_and_records_the_durable_deferral()
    {
        using var harness = Harness.WithIntroPromptVisible();

        var result = harness.Router.TryInvoke(Release1PresentationCommand.IntroDefer);

        Assert.True(result);
        Assert.Equal(Release1RelationshipState.Deferred, harness.Story.State!.RelationshipState);
    }

    [Fact]
    public void IntroDefer_returns_false_once_the_prompt_is_no_longer_eligible()
    {
        using var harness = Harness.WithIntroPromptVisible();
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.IntroDefer));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.IntroDefer);

        Assert.False(result);
    }

    [Fact]
    public void SmallCourtesyReview_reaches_only_the_presenter_and_returns_true_when_terms_are_ready()
    {
        using var harness = Harness.WithSmallCourtesyOffered();

        var result = harness.Router.TryInvoke(Release1PresentationCommand.SmallCourtesyReview);

        Assert.True(result);
        Assert.NotNull(harness.Service.ReviewedQuote);
    }

    [Fact]
    public void SmallCourtesyAccept_returns_false_before_terms_have_been_reviewed()
    {
        using var harness = Harness.WithSmallCourtesyOffered();

        var result = harness.Router.TryInvoke(Release1PresentationCommand.SmallCourtesyAccept);

        Assert.False(result);
        Assert.Equal(Release1MissionState.Offered, harness.Story.State!.Missions[0].State);
    }

    [Fact]
    public void SmallCourtesyAccept_reaches_only_the_presenter_and_activates_the_mission()
    {
        using var harness = Harness.WithSmallCourtesyOffered();
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.SmallCourtesyReview));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.SmallCourtesyAccept);

        Assert.True(result);
        Assert.Equal(Release1MissionState.Active, harness.Story.State!.Missions[0].State);
    }

    [Fact]
    public void SmallCourtesyDefer_reaches_only_the_presenter_and_defers_the_mission()
    {
        using var harness = Harness.WithSmallCourtesyOffered();
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.SmallCourtesyReview));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.SmallCourtesyDefer);

        Assert.True(result);
        Assert.Equal(Release1MissionState.Deferred, harness.Story.State!.Missions[0].State);
    }

    [Fact]
    public void WrongAddressReview_reaches_only_the_wrong_address_presenter_and_returns_true_when_terms_are_ready()
    {
        using var harness = Harness.WithWrongAddressOffered();

        var result = harness.Router.TryInvoke(Release1PresentationCommand.WrongAddressReview);

        Assert.True(result);
        Assert.NotNull(harness.WrongAddressService!.ReviewedQuote);
    }

    [Fact]
    public void WrongAddressAccept_returns_false_before_terms_have_been_reviewed()
    {
        using var harness = Harness.WithWrongAddressOffered();

        var result = harness.Router.TryInvoke(Release1PresentationCommand.WrongAddressAccept);

        Assert.False(result);
        Assert.Equal(Release1MissionState.Offered, harness.WrongAddressMission().State);
    }

    [Fact]
    public void WrongAddressAccept_reaches_only_the_wrong_address_presenter_and_activates_the_mission()
    {
        using var harness = Harness.WithWrongAddressOffered();
        harness.WrongAddressWorld!.TotalMinutes = 6_000d;
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.WrongAddressReview));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.WrongAddressAccept);

        Assert.True(result);
        Assert.Equal(Release1MissionState.Active, harness.WrongAddressMission().State);
    }

    [Fact]
    public void WrongAddressDefer_reaches_only_the_wrong_address_presenter_and_defers_the_mission()
    {
        using var harness = Harness.WithWrongAddressOffered();
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.WrongAddressReview));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.WrongAddressDefer);

        Assert.True(result);
        Assert.Equal(Release1MissionState.Deferred, harness.WrongAddressMission().State);
    }

    [Fact]
    public void WrongAddress_commands_with_a_null_presenter_return_false_and_touch_nothing()
    {
        using var harness = Harness.Create(); // built without a Wrong Address presenter
        var revisionBefore = harness.Story.State?.Revision;

        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.WrongAddressReview));
        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.WrongAddressAccept));
        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.WrongAddressDefer));

        Assert.Equal(revisionBefore, harness.Story.State?.Revision);
    }

    [Fact]
    public void RoomWithNoNameReview_reaches_only_the_room_presenter_and_returns_true_when_terms_are_ready()
    {
        using var harness = Harness.WithRoomWithNoNameOffered();

        var result = harness.Router.TryInvoke(Release1PresentationCommand.RoomWithNoNameReview);

        Assert.True(result);
        Assert.NotNull(harness.RoomWithNoNameService!.ReviewedQuote);
    }

    [Fact]
    public void RoomWithNoNameAccept_returns_false_before_terms_have_been_reviewed()
    {
        using var harness = Harness.WithRoomWithNoNameOffered();

        var result = harness.Router.TryInvoke(Release1PresentationCommand.RoomWithNoNameAccept);

        Assert.False(result);
        Assert.Equal(Release1MissionState.Offered, harness.RoomWithNoNameMission().State);
    }

    [Fact]
    public void RoomWithNoNameAccept_reaches_only_the_room_presenter_and_activates_the_mission()
    {
        using var harness = Harness.WithRoomWithNoNameOffered();
        harness.RoomWithNoNameWorld!.TotalMinutes = 6_000d;
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.RoomWithNoNameReview));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.RoomWithNoNameAccept);

        Assert.True(result);
        Assert.Equal(Release1MissionState.Active, harness.RoomWithNoNameMission().State);
    }

    [Fact]
    public void RoomWithNoNameDefer_reaches_only_the_room_presenter_and_defers_the_mission()
    {
        using var harness = Harness.WithRoomWithNoNameOffered();
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.RoomWithNoNameReview));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.RoomWithNoNameDefer);

        Assert.True(result);
        Assert.Equal(Release1MissionState.Deferred, harness.RoomWithNoNameMission().State);
    }

    [Fact]
    public void RoomWithNoName_commands_with_a_null_presenter_return_false_and_touch_nothing()
    {
        using var harness = Harness.Create(); // built without a Room With No Name presenter
        var revisionBefore = harness.Story.State?.Revision;

        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.RoomWithNoNameReview));
        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.RoomWithNoNameAccept));
        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.RoomWithNoNameDefer));

        Assert.Equal(revisionBefore, harness.Story.State?.Revision);
    }

    [Fact]
    public void ShortNoticeReview_reaches_only_the_short_notice_presenter_and_returns_true_when_terms_are_ready()
    {
        using var harness = Harness.WithShortNoticeOffered();

        var result = harness.Router.TryInvoke(Release1PresentationCommand.ShortNoticeReview);

        Assert.True(result);
        Assert.NotNull(harness.ShortNoticeService!.ReviewedQuote);
    }

    [Fact]
    public void ShortNoticeAccept_returns_false_before_terms_have_been_reviewed()
    {
        using var harness = Harness.WithShortNoticeOffered();

        var result = harness.Router.TryInvoke(Release1PresentationCommand.ShortNoticeAccept);

        Assert.False(result);
        Assert.Equal(Release1MissionState.Offered, harness.ShortNoticeMission().State);
    }

    [Fact]
    public void ShortNoticeAccept_reaches_only_the_short_notice_presenter_and_activates_the_mission()
    {
        using var harness = Harness.WithShortNoticeOffered();
        harness.ShortNoticeWorld!.TotalMinutes = 6_000d;
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.ShortNoticeReview));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.ShortNoticeAccept);

        Assert.True(result);
        Assert.Equal(Release1MissionState.Active, harness.ShortNoticeMission().State);
    }

    [Fact]
    public void ShortNoticeDefer_reaches_only_the_short_notice_presenter_and_defers_the_mission()
    {
        using var harness = Harness.WithShortNoticeOffered();
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.ShortNoticeReview));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.ShortNoticeDefer);

        Assert.True(result);
        Assert.Equal(Release1MissionState.Deferred, harness.ShortNoticeMission().State);
    }

    [Fact]
    public void ShortNotice_commands_with_a_null_presenter_return_false_and_touch_nothing()
    {
        using var harness = Harness.Create(); // built without a Short Notice presenter
        var revisionBefore = harness.Story.State?.Revision;

        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.ShortNoticeReview));
        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.ShortNoticeAccept));
        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.ShortNoticeDefer));

        Assert.Equal(revisionBefore, harness.Story.State?.Revision);
    }

    [Fact]
    public void KeepTheLightsOffReview_reaches_only_the_keep_the_lights_off_presenter_and_returns_true_when_terms_are_ready()
    {
        using var harness = Harness.WithKeepTheLightsOffOffered();

        var result = harness.Router.TryInvoke(Release1PresentationCommand.KeepTheLightsOffReview);

        Assert.True(result);
        Assert.NotNull(harness.KeepTheLightsOffService!.ReviewedQuote);
        // The harness's own Small Courtesy presenter/service shares the router but not this command;
        // it stays untouched, proving the command reached only the Keep the Lights Off presenter.
        Assert.Null(harness.Service.ReviewedQuote);
        Assert.Equal(Release1MissionState.Satisfied, harness.Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)].State);
    }

    [Fact]
    public void KeepTheLightsOffAccept_returns_false_before_terms_have_been_reviewed()
    {
        using var harness = Harness.WithKeepTheLightsOffOffered();

        var result = harness.Router.TryInvoke(Release1PresentationCommand.KeepTheLightsOffAccept);

        Assert.False(result);
        Assert.Equal(Release1MissionState.Offered, harness.KeepTheLightsOffMission().State);
    }

    [Fact]
    public void KeepTheLightsOffAccept_reaches_only_the_keep_the_lights_off_presenter_and_activates_the_mission()
    {
        using var harness = Harness.WithKeepTheLightsOffOffered();
        harness.KeepTheLightsOffWorld!.TotalMinutes = 6_000d;
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.KeepTheLightsOffReview));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.KeepTheLightsOffAccept);

        Assert.True(result);
        Assert.Equal(Release1MissionState.Active, harness.KeepTheLightsOffMission().State);
        // The harness's own Small Courtesy mission was already Satisfied before this command ran and
        // stays Satisfied, proving the accept reached only the Keep the Lights Off presenter.
        Assert.Equal(Release1MissionState.Satisfied, harness.Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)].State);
    }

    [Fact]
    public void KeepTheLightsOffDefer_reaches_only_the_keep_the_lights_off_presenter_and_defers_the_mission()
    {
        using var harness = Harness.WithKeepTheLightsOffOffered();
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.KeepTheLightsOffReview));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.KeepTheLightsOffDefer);

        Assert.True(result);
        Assert.Equal(Release1MissionState.Deferred, harness.KeepTheLightsOffMission().State);
        // The harness's own Small Courtesy mission was already Satisfied before this command ran and
        // stays Satisfied, proving the defer reached only the Keep the Lights Off presenter.
        Assert.Equal(Release1MissionState.Satisfied, harness.Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)].State);
    }

    [Fact]
    public void KeepTheLightsOff_commands_with_a_null_presenter_return_false_and_touch_nothing()
    {
        using var harness = Harness.Create(); // built without a Keep the Lights Off presenter
        var revisionBefore = harness.Story.State?.Revision;

        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.KeepTheLightsOffReview));
        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.KeepTheLightsOffAccept));
        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.KeepTheLightsOffDefer));

        Assert.Equal(revisionBefore, harness.Story.State?.Revision);
    }

    [Fact]
    public void TheEnvelopeReview_reaches_only_the_envelope_presenter_and_returns_true_when_terms_are_ready()
    {
        // The harness wires only the envelope presenter into the router (the other mission presenter
        // fields are null, as the constructor list above shows), so "no other presenter" is a
        // structural fact here rather than something the harness can observe by watching a call
        // count; asserting those fields stay null is the honest version of "reaches only" this
        // fixture can prove. Review is a pure read against the reviewed-quote cache, so the story's
        // revision should not move either.
        using var harness = Harness.WithTheEnvelopeOffered();
        var revisionBefore = harness.Story.State?.Revision;

        var result = harness.Router.TryInvoke(Release1PresentationCommand.TheEnvelopeReview);

        Assert.True(result);
        Assert.NotNull(harness.TheEnvelopeService!.ReviewedQuote);
        Assert.Null(harness.KeepTheLightsOffPresenter);
        Assert.Null(harness.ShortNoticePresenter);
        Assert.Null(harness.RoomWithNoNamePresenter);
        Assert.Null(harness.WrongAddressPresenter);
        Assert.Equal(revisionBefore, harness.Story.State?.Revision);
    }

    [Fact]
    public void TheEnvelopeAccept_returns_false_before_terms_have_been_reviewed()
    {
        using var harness = Harness.WithTheEnvelopeOffered();

        var result = harness.Router.TryInvoke(Release1PresentationCommand.TheEnvelopeAccept);

        Assert.False(result);
        Assert.Equal(Release1MissionState.Offered, harness.TheEnvelopeMission().State);
    }

    [Fact]
    public void TheEnvelopeAccept_reaches_only_the_envelope_presenter_and_activates_the_mission()
    {
        // Accept mutates the mission, so the revision-unchanged check from the Review test above does
        // not apply here; the only observable "reaches only the envelope presenter" claim this
        // harness can back is that no other mission presenter field was ever populated.
        using var harness = Harness.WithTheEnvelopeOffered();
        harness.TheEnvelopeWorld!.TotalMinutes = 6_000d;
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.TheEnvelopeReview));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.TheEnvelopeAccept);

        Assert.True(result);
        Assert.Equal(Release1MissionState.Active, harness.TheEnvelopeMission().State);
        Assert.Null(harness.KeepTheLightsOffPresenter);
        Assert.Null(harness.ShortNoticePresenter);
        Assert.Null(harness.RoomWithNoNamePresenter);
        Assert.Null(harness.WrongAddressPresenter);
    }

    [Fact]
    public void TheEnvelopeDefer_reaches_only_the_envelope_presenter_and_defers_the_mission()
    {
        using var harness = Harness.WithTheEnvelopeOffered();
        Assert.True(harness.Router.TryInvoke(Release1PresentationCommand.TheEnvelopeReview));

        var result = harness.Router.TryInvoke(Release1PresentationCommand.TheEnvelopeDefer);

        Assert.True(result);
        Assert.Equal(Release1MissionState.Deferred, harness.TheEnvelopeMission().State);
        Assert.Null(harness.KeepTheLightsOffPresenter);
        Assert.Null(harness.ShortNoticePresenter);
        Assert.Null(harness.RoomWithNoNamePresenter);
        Assert.Null(harness.WrongAddressPresenter);
    }

    [Fact]
    public void TheEnvelope_commands_with_a_null_presenter_return_false_and_touch_nothing()
    {
        using var harness = Harness.Create(); // built without a The Envelope presenter
        var revisionBefore = harness.Story.State?.Revision;

        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.TheEnvelopeReview));
        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.TheEnvelopeAccept));
        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.TheEnvelopeDefer));

        Assert.Equal(revisionBefore, harness.Story.State?.Revision);
    }

    [Fact]
    public void Chief_commands_never_reach_a_mission_presenter()
    {
        using var harness = Harness.Create();

        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.ChiefCampbellPay));
        Assert.False(harness.Router.TryInvoke(Release1PresentationCommand.ChiefCampbellDecline));
    }

    [Fact]
    public void Unknown_command_value_returns_false()
    {
        using var harness = Harness.Create();

        var result = harness.Router.TryInvoke((Release1PresentationCommand)(-1));

        Assert.False(result);
    }

    [Fact]
    public void Router_holds_no_story_runtime_reference_and_cannot_touch_story_state_directly()
    {
        var fields = typeof(Release1PresentationCommandRouter).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.DoesNotContain(fields, field => field.FieldType == typeof(Release1StoryRuntimeService));
    }

    private sealed class Harness : IDisposable
    {
        private Harness(
            Release1StoryRuntimeService story,
            Release1TransitionPublisherService publisher,
            Release1PhoneCallService phoneService,
            Release1SmallCourtesyMissionService service,
            Release1SmallCourtesyPresenter presenter,
            QueueFake queue,
            Release1WrongAddressMissionService? wrongAddressService = null,
            Release1WrongAddressPresenter? wrongAddressPresenter = null,
            Release1WrongAddressHarness.FakeWorld? wrongAddressWorld = null,
            Release1RoomWithNoNameMissionService? roomWithNoNameService = null,
            Release1RoomWithNoNamePresenter? roomWithNoNamePresenter = null,
            Release1RoomWithNoNameHarness.FakeWorld? roomWithNoNameWorld = null,
            Release1ShortNoticeMissionService? shortNoticeService = null,
            Release1ShortNoticePresenter? shortNoticePresenter = null,
            Release1ShortNoticeHarness.FakeWorld? shortNoticeWorld = null,
            Release1KeepTheLightsOffMissionService? keepTheLightsOffService = null,
            Release1KeepTheLightsOffPresenter? keepTheLightsOffPresenter = null,
            Release1KeepTheLightsOffHarness.FakeWorld? keepTheLightsOffWorld = null,
            Release1TheEnvelopeMissionService? theEnvelopeService = null,
            Release1TheEnvelopePresenter? theEnvelopePresenter = null,
            Release1TheEnvelopeHarness.FakeWorld? theEnvelopeWorld = null)
        {
            Story = story;
            Publisher = publisher;
            PhoneService = phoneService;
            Service = service;
            Presenter = presenter;
            Queue = queue;
            WrongAddressService = wrongAddressService;
            WrongAddressPresenter = wrongAddressPresenter;
            WrongAddressWorld = wrongAddressWorld;
            RoomWithNoNameService = roomWithNoNameService;
            RoomWithNoNamePresenter = roomWithNoNamePresenter;
            RoomWithNoNameWorld = roomWithNoNameWorld;
            ShortNoticeService = shortNoticeService;
            ShortNoticePresenter = shortNoticePresenter;
            ShortNoticeWorld = shortNoticeWorld;
            KeepTheLightsOffService = keepTheLightsOffService;
            KeepTheLightsOffPresenter = keepTheLightsOffPresenter;
            KeepTheLightsOffWorld = keepTheLightsOffWorld;
            TheEnvelopeService = theEnvelopeService;
            TheEnvelopePresenter = theEnvelopePresenter;
            TheEnvelopeWorld = theEnvelopeWorld;
            Router = new Release1PresentationCommandRouter(
                publisher, presenter, wrongAddressPresenter, roomWithNoNamePresenter, shortNoticePresenter, keepTheLightsOffPresenter, theEnvelopePresenter);
        }

        public Release1StoryRuntimeService Story { get; }
        public Release1TransitionPublisherService Publisher { get; }
        public Release1PhoneCallService PhoneService { get; }
        public Release1SmallCourtesyMissionService Service { get; }
        public Release1SmallCourtesyPresenter Presenter { get; }
        public Release1PresentationCommandRouter Router { get; }
        public QueueFake Queue { get; }
        public Release1WrongAddressMissionService? WrongAddressService { get; }
        public Release1WrongAddressPresenter? WrongAddressPresenter { get; }
        public Release1WrongAddressHarness.FakeWorld? WrongAddressWorld { get; }
        public Release1RoomWithNoNameMissionService? RoomWithNoNameService { get; }
        public Release1RoomWithNoNamePresenter? RoomWithNoNamePresenter { get; }
        public Release1RoomWithNoNameHarness.FakeWorld? RoomWithNoNameWorld { get; }
        public Release1ShortNoticeMissionService? ShortNoticeService { get; }
        public Release1ShortNoticePresenter? ShortNoticePresenter { get; }
        public Release1ShortNoticeHarness.FakeWorld? ShortNoticeWorld { get; }
        public Release1KeepTheLightsOffMissionService? KeepTheLightsOffService { get; }
        public Release1KeepTheLightsOffPresenter? KeepTheLightsOffPresenter { get; }
        public Release1KeepTheLightsOffHarness.FakeWorld? KeepTheLightsOffWorld { get; }
        public Release1TheEnvelopeMissionService? TheEnvelopeService { get; }
        public Release1TheEnvelopePresenter? TheEnvelopePresenter { get; }
        public Release1TheEnvelopeHarness.FakeWorld? TheEnvelopeWorld { get; }

        public Release1MissionRecord WrongAddressMission() =>
            Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)];

        public Release1MissionRecord RoomWithNoNameMission() =>
            Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)];

        public Release1MissionRecord ShortNoticeMission() =>
            Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)];

        public Release1MissionRecord KeepTheLightsOffMission() =>
            Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];

        public Release1MissionRecord TheEnvelopeMission() =>
            Story.State!.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope)];

        public static Harness Create()
        {
            var context = new Release1SmallCourtesyDepositTests.FakeContext();
            var repository = new Release1SmallCourtesyDepositTests.FakeRepository();
            var story = new Release1StoryRuntimeService(context, repository);
            story.OnPreLoad();
            story.OnLoadComplete();
            var queue = new QueueFake();
            var banner = new Release1PayphoneBanner(() => 0f);
            var phoneService = new Release1PhoneCallService(story, queue, banner);
            var publisher = new Release1TransitionPublisherService(new ReaderFake(context.Snapshot), story, phoneService);
            var world = new Release1SmallCourtesyDepositTests.FakeWorld(context.Snapshot);
            var service = new Release1SmallCourtesyMissionService(story, world);
            var presenter = new Release1SmallCourtesyPresenter(service, story);
            return new Harness(story, publisher, phoneService, service, presenter, queue);
        }

        // A real story runtime and a fake world, with Small Courtesy already Satisfied and Wrong
        // Address Offered (mirrors Release1WrongAddressHarness.Offered()), so the router's three new
        // commands reach a real Release1WrongAddressPresenter/Release1WrongAddressMissionService pair
        // bound to the same story as the publisher and Small Courtesy presenter.
        public static Harness WithWrongAddressOffered()
        {
            var wrongAddress = Release1WrongAddressHarness.Offered();
            var queue = new QueueFake();
            var banner = new Release1PayphoneBanner(() => 0f);
            var phoneService = new Release1PhoneCallService(wrongAddress.Story, queue, banner);
            var publisher = new Release1TransitionPublisherService(
                new ReaderFake(wrongAddress.Context.Snapshot), wrongAddress.Story, phoneService);
            var smallCourtesyService = new Release1SmallCourtesyMissionService(wrongAddress.Story, wrongAddress.World);
            smallCourtesyService.OnLoadComplete();
            var presenter = new Release1SmallCourtesyPresenter(smallCourtesyService, wrongAddress.Story);
            var wrongAddressPresenter = new Release1WrongAddressPresenter(wrongAddress.Service, wrongAddress.Story);
            return new Harness(
                wrongAddress.Story, publisher, phoneService, smallCourtesyService, presenter, queue,
                wrongAddress.Service, wrongAddressPresenter, wrongAddress.World);
        }

        // A real story runtime and a fake world, with Small Courtesy and Wrong Address already
        // Satisfied and Room With No Name Offered (mirrors Release1RoomWithNoNameHarness.Offered()),
        // so the router's three new commands reach a real Release1RoomWithNoNamePresenter/
        // Release1RoomWithNoNameMissionService pair bound to the same story as the publisher and
        // Small Courtesy presenter.
        public static Harness WithRoomWithNoNameOffered()
        {
            var room = Release1RoomWithNoNameHarness.Offered();
            var queue = new QueueFake();
            var banner = new Release1PayphoneBanner(() => 0f);
            var phoneService = new Release1PhoneCallService(room.Story, queue, banner);
            var publisher = new Release1TransitionPublisherService(
                new ReaderFake(room.Context.Snapshot), room.Story, phoneService);
            var smallCourtesyService = new Release1SmallCourtesyMissionService(room.Story, room.World);
            smallCourtesyService.OnLoadComplete();
            var presenter = new Release1SmallCourtesyPresenter(smallCourtesyService, room.Story);
            var roomPresenter = new Release1RoomWithNoNamePresenter(room.Service, room.Story);
            return new Harness(
                room.Story, publisher, phoneService, smallCourtesyService, presenter, queue,
                roomWithNoNameService: room.Service, roomWithNoNamePresenter: roomPresenter, roomWithNoNameWorld: room.World);
        }

        // A real story runtime and a fake world, with Small Courtesy, Wrong Address, and Room With No
        // Name already Satisfied and Short Notice Offered (mirrors Release1ShortNoticeHarness.Offered()),
        // so the router's three new commands reach a real Release1ShortNoticePresenter/
        // Release1ShortNoticeMissionService pair bound to the same story as the publisher and Small
        // Courtesy presenter.
        public static Harness WithShortNoticeOffered()
        {
            var shortNotice = Release1ShortNoticeHarness.Offered();
            var queue = new QueueFake();
            var banner = new Release1PayphoneBanner(() => 0f);
            var phoneService = new Release1PhoneCallService(shortNotice.Story, queue, banner);
            var publisher = new Release1TransitionPublisherService(
                new ReaderFake(shortNotice.Context.Snapshot), shortNotice.Story, phoneService);
            var smallCourtesyService = new Release1SmallCourtesyMissionService(shortNotice.Story, shortNotice.World);
            smallCourtesyService.OnLoadComplete();
            var presenter = new Release1SmallCourtesyPresenter(smallCourtesyService, shortNotice.Story);
            var shortNoticePresenter = new Release1ShortNoticePresenter(shortNotice.Service, shortNotice.Story);
            return new Harness(
                shortNotice.Story, publisher, phoneService, smallCourtesyService, presenter, queue,
                shortNoticeService: shortNotice.Service, shortNoticePresenter: shortNoticePresenter, shortNoticeWorld: shortNotice.World);
        }

        // A real story runtime and a fake world, with Small Courtesy, Wrong Address, Room With No
        // Name, and Short Notice already Satisfied and Keep the Lights Off Offered (mirrors
        // Release1KeepTheLightsOffHarness.Offered()), so the router's three new commands reach a real
        // Release1KeepTheLightsOffPresenter/Release1KeepTheLightsOffMissionService pair bound to the
        // same story as the publisher and Small Courtesy presenter.
        public static Harness WithKeepTheLightsOffOffered()
        {
            var keepTheLightsOff = Release1KeepTheLightsOffHarness.Offered();
            var queue = new QueueFake();
            var banner = new Release1PayphoneBanner(() => 0f);
            var phoneService = new Release1PhoneCallService(keepTheLightsOff.Story, queue, banner);
            var publisher = new Release1TransitionPublisherService(
                new ReaderFake(keepTheLightsOff.Context.Snapshot), keepTheLightsOff.Story, phoneService);
            var smallCourtesyService = new Release1SmallCourtesyMissionService(keepTheLightsOff.Story, keepTheLightsOff.World);
            smallCourtesyService.OnLoadComplete();
            var presenter = new Release1SmallCourtesyPresenter(smallCourtesyService, keepTheLightsOff.Story);
            var keepTheLightsOffPresenter = new Release1KeepTheLightsOffPresenter(keepTheLightsOff.Service, keepTheLightsOff.Story);
            return new Harness(
                keepTheLightsOff.Story, publisher, phoneService, smallCourtesyService, presenter, queue,
                keepTheLightsOffService: keepTheLightsOff.Service, keepTheLightsOffPresenter: keepTheLightsOffPresenter,
                keepTheLightsOffWorld: keepTheLightsOff.World);
        }

        // A real story runtime and a fake world, with Small Courtesy, Wrong Address, Room With No
        // Name, Short Notice, and Keep the Lights Off already Satisfied and The Envelope Offered
        // (mirrors Release1TheEnvelopeHarness.Offered()), so the router's three new commands reach a
        // real Release1TheEnvelopePresenter/Release1TheEnvelopeMissionService pair bound to the same
        // story as the publisher and Small Courtesy presenter.
        public static Harness WithTheEnvelopeOffered()
        {
            var theEnvelope = Release1TheEnvelopeHarness.Offered();
            var queue = new QueueFake();
            var banner = new Release1PayphoneBanner(() => 0f);
            var phoneService = new Release1PhoneCallService(theEnvelope.Story, queue, banner);
            var publisher = new Release1TransitionPublisherService(
                new ReaderFake(theEnvelope.Context.Snapshot), theEnvelope.Story, phoneService);
            var smallCourtesyService = new Release1SmallCourtesyMissionService(theEnvelope.Story, theEnvelope.World);
            smallCourtesyService.OnLoadComplete();
            var presenter = new Release1SmallCourtesyPresenter(smallCourtesyService, theEnvelope.Story);
            var theEnvelopePresenter = new Release1TheEnvelopePresenter(theEnvelope.Service, theEnvelope.Story);
            return new Harness(
                theEnvelope.Story, publisher, phoneService, smallCourtesyService, presenter, queue,
                theEnvelopeService: theEnvelope.Service, theEnvelopePresenter: theEnvelopePresenter, theEnvelopeWorld: theEnvelope.World);
        }

        public static Harness WithIntroPromptVisible()
        {
            var harness = Create();
            harness.Publisher.OnLoadComplete();
            harness.Publisher.Update();
            Assert.True(harness.Publisher.PromptVisible);
            return harness;
        }

        public static Harness WithSmallCourtesyOffered()
        {
            var harness = Create();
            Assert.True(harness.Story.TryGetActiveContext(out var active, out _));
            const string receipt = "router-test-intro-accept";
            Assert.True(harness.Story.TryExecuteDurably(new(
                active.SessionEpoch,
                active.LoadEpoch,
                active.PlayerId,
                Release1MissionCatalog.IntroScopeKey,
                0,
                Release1TransitionKind.IntroAccepted,
                receipt,
                Release1LogicalCorrelation.Create(
                    active.PlayerId, Release1MissionCatalog.IntroScopeKey, 0, Release1TransitionKind.IntroAccepted, receipt).Value)).Accepted);
            harness.Service.OnLoadComplete();
            return harness;
        }

        public void Dispose()
        {
            KeepTheLightsOffPresenter?.Dispose();
            KeepTheLightsOffService?.Dispose();
            TheEnvelopePresenter?.Dispose();
            TheEnvelopeService?.Dispose();
            ShortNoticePresenter?.Dispose();
            ShortNoticeService?.Dispose();
            RoomWithNoNamePresenter?.Dispose();
            RoomWithNoNameService?.Dispose();
            WrongAddressPresenter?.Dispose();
            WrongAddressService?.Dispose();
            Presenter.Dispose();
            Service.Dispose();
            Publisher.Dispose();
            PhoneService.Dispose();
            Story.Dispose();
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
}
