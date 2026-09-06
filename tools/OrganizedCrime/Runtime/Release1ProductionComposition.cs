using System.Linq;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class Release1ProductionComposition : IDisposable
{
    private const string WrongAddressFallbackLog =
        "Wrong Address has no fallback presenter; the mission stays offered while the presentation mode is ImguiFallback.";

    private const string RoomWithNoNameFallbackLog =
        "A Room With No Name has no fallback presenter; the mission stays offered while the presentation mode is ImguiFallback.";

    private const string ShortNoticeFallbackLog =
        "Short Notice has no fallback presenter; the mission stays offered while the presentation mode is ImguiFallback.";

    private const string KeepTheLightsOffFallbackLog =
        "Keep the Lights Off has no fallback presenter; the mission stays offered while the presentation mode is ImguiFallback.";

    private const string TheEnvelopeFallbackLog =
        "The Envelope has no fallback presenter; the mission stays offered while the presentation mode is ImguiFallback.";

    private readonly Release1StoryRuntimeService _story;
    private readonly Release1PresentationMode _mode;
    private readonly Action<string>? _log;
    private readonly Func<Release1WorldPoint?>? _holdRoomMarker;
    private bool _loggedMissingHoldMarker;
    private bool _disposed;

    public Release1ProductionComposition(
        Release1StoryRuntimeService story,
        IRelease1PostBenziesUnlockReader reader,
        IRelease1PhoneCallQueue queue,
        IRelease1SmallCourtesyWorld smallCourtesyWorld,
        Action<string>? log = null,
        Func<DateTime>? utcNow = null,
        Func<float>? bannerNow = null,
        IRelease1DecisionPromptPlatform? promptPlatform = null,
        Release1PresentationMode mode = Release1PresentationMode.ImguiFallback,
        IRelease1NativePresentation? native = null,
        Func<Release1WorldPoint?>? holdRoomMarker = null)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(smallCourtesyWorld);
        if (mode == Release1PresentationMode.Native && native is null)
            throw new ArgumentException("A native presentation implementation is required in Native mode.", nameof(native));

        _story = story;
        _mode = mode;
        _log = log;
        _holdRoomMarker = holdRoomMarker;
        Banner = new Release1PayphoneBanner(bannerNow);
        PhoneService = new Release1PhoneCallService(story, queue, Banner, log);
        Publisher = new Release1TransitionPublisherService(
            reader, story, PhoneService, log, utcNow,
            publishIntroCall: mode == Release1PresentationMode.ImguiFallback);
        PromptPresenter = new Release1IntroPromptPresenter(Publisher);
        SmallCourtesy = new Release1SmallCourtesyComposition(story, smallCourtesyWorld, log);
        WrongAddress = new Release1WrongAddressComposition(story, smallCourtesyWorld, PhoneService, log);
        RoomWithNoName = new Release1RoomWithNoNameComposition(story, smallCourtesyWorld, PhoneService, log);
        ShortNotice = new Release1ShortNoticeComposition(story, smallCourtesyWorld, PhoneService, log);
        KeepTheLightsOff = new Release1KeepTheLightsOffComposition(story, smallCourtesyWorld, PhoneService, log);
        TheEnvelope = new Release1TheEnvelopeComposition(story, smallCourtesyWorld, PhoneService, log);

        if (mode == Release1PresentationMode.Native)
        {
            var commands = new Release1PresentationCommandRouter(
                Publisher, SmallCourtesy.Presenter, WrongAddress.Presenter, RoomWithNoName.Presenter, ShortNotice.Presenter, KeepTheLightsOff.Presenter, TheEnvelope.Presenter);
            Projector = new Release1PresentationProjector(story, () => Release1PresentationPlanner.Build(BuildPresentationInputs()), native!, commands, log);
        }
        else
        {
            DecisionPromptHost = new Release1DecisionPromptHost(
                () => PromptPresenter.DecisionPrompt ?? SmallCourtesy.Presenter.DecisionPrompt,
                promptPlatform ?? new Release1DecisionPromptUnityPlatform(),
                log);
        }
    }

    public Release1TransitionPublisherService Publisher { get; }
    public Release1PhoneCallService PhoneService { get; }
    public Release1IntroPromptPresenter PromptPresenter { get; }
    public Release1PayphoneBanner Banner { get; }
    public Release1SmallCourtesyComposition SmallCourtesy { get; }
    public Release1WrongAddressComposition WrongAddress { get; }
    public Release1RoomWithNoNameComposition RoomWithNoName { get; }
    public Release1ShortNoticeComposition ShortNotice { get; }
    public Release1KeepTheLightsOffComposition KeepTheLightsOff { get; }
    public Release1TheEnvelopeComposition TheEnvelope { get; }
    public Release1PresentationProjector? Projector { get; }
    public Release1DecisionPromptHost? DecisionPromptHost { get; }

    /// <summary>
    /// The timing seam this composition reports its own per child phase receipts through. It is
    /// assigned by the mod shell after construction and defaults to the disabled seam, so every
    /// existing caller and every test keeps the exact behaviour it had before the seam existed.
    /// </summary>
    public OrganizedCrimeTimingReceipts Timing { get; set; } = OrganizedCrimeTimingReceipts.Disabled;

    public void OnLoadComplete()
    {
        if (_disposed) return;
        var timing = Timing;
        timing.Measure("load-complete/production/publisher", Publisher.OnLoadComplete);
        timing.Measure("load-complete/production/small-courtesy", SmallCourtesy.OnLoadComplete);
        timing.Measure("load-complete/production/wrong-address", WrongAddress.OnLoadComplete);
        timing.Measure("load-complete/production/room-with-no-name", RoomWithNoName.OnLoadComplete);
        timing.Measure("load-complete/production/short-notice", ShortNotice.OnLoadComplete);
        timing.Measure("load-complete/production/keep-the-lights-off", KeepTheLightsOff.OnLoadComplete);
        timing.Measure("load-complete/production/the-envelope", TheEnvelope.OnLoadComplete);
        if (_mode == Release1PresentationMode.ImguiFallback)
        {
            _log?.Invoke(WrongAddressFallbackLog);
            _log?.Invoke(RoomWithNoNameFallbackLog);
            _log?.Invoke(ShortNoticeFallbackLog);
            _log?.Invoke(KeepTheLightsOffFallbackLog);
            _log?.Invoke(TheEnvelopeFallbackLog);
        }
        if (Projector is not null) timing.Measure("load-complete/production/projector", Projector.OnLoadComplete);
    }

    public void OnPreLoad()
    {
        if (_disposed) return;
        var timing = Timing;
        DecisionPromptHost?.OnPreLoad();
        if (Projector is not null) timing.Measure("pre-load/production/projector", Projector.OnPreLoad);
        timing.Measure("pre-load/production/the-envelope", TheEnvelope.OnPreLoad);
        timing.Measure("pre-load/production/keep-the-lights-off", KeepTheLightsOff.OnPreLoad);
        timing.Measure("pre-load/production/short-notice", ShortNotice.OnPreLoad);
        timing.Measure("pre-load/production/room-with-no-name", RoomWithNoName.OnPreLoad);
        timing.Measure("pre-load/production/wrong-address", WrongAddress.OnPreLoad);
        timing.Measure("pre-load/production/small-courtesy", SmallCourtesy.OnPreLoad);
        timing.Measure("pre-load/production/publisher", Publisher.OnPreLoad);
        _loggedMissingHoldMarker = false;
    }

    public void OnPreSceneChange()
    {
        if (_disposed) return;
        DecisionPromptHost?.OnPreSceneChange();
    }

    public void OnSaveStart()
    {
        if (_disposed) return;
        var timing = Timing;
        timing.Measure("save-start/production/small-courtesy", SmallCourtesy.OnSaveStart);
        timing.Measure("save-start/production/wrong-address", WrongAddress.OnSaveStart);
        timing.Measure("save-start/production/room-with-no-name", RoomWithNoName.OnSaveStart);
        timing.Measure("save-start/production/short-notice", ShortNotice.OnSaveStart);
        timing.Measure("save-start/production/keep-the-lights-off", KeepTheLightsOff.OnSaveStart);
        timing.Measure("save-start/production/the-envelope", TheEnvelope.OnSaveStart);
        if (Projector is not null) timing.Measure("save-start/production/projector", Projector.OnSaveStart);
    }

    public void OnSaveComplete()
    {
        if (_disposed) return;
        var timing = Timing;
        timing.Measure("save-complete/production/small-courtesy", SmallCourtesy.OnSaveComplete);
        timing.Measure("save-complete/production/wrong-address", WrongAddress.OnSaveComplete);
        timing.Measure("save-complete/production/room-with-no-name", RoomWithNoName.OnSaveComplete);
        timing.Measure("save-complete/production/short-notice", ShortNotice.OnSaveComplete);
        timing.Measure("save-complete/production/keep-the-lights-off", KeepTheLightsOff.OnSaveComplete);
        timing.Measure("save-complete/production/the-envelope", TheEnvelope.OnSaveComplete);
        if (Projector is not null) timing.Measure("save-complete/production/projector", Projector.OnSaveComplete);
    }

    public void Update()
    {
        if (_disposed) return;
        Publisher.Update();
        SmallCourtesy.Update();
        WrongAddress.Update();
        RoomWithNoName.Update();
        ShortNotice.Update();
        KeepTheLightsOff.Update();
        TheEnvelope.Update();
        Projector?.Reconcile();
        DecisionPromptHost?.Update();
        Banner.Update();
    }

    public void OnGUI()
    {
        if (_disposed) return;
        DecisionPromptHost?.OnGUI();
        Banner.OnGUI();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Projector?.Dispose();
        DecisionPromptHost?.Dispose();
        TheEnvelope.Dispose();
        KeepTheLightsOff.Dispose();
        ShortNotice.Dispose();
        RoomWithNoName.Dispose();
        WrongAddress.Dispose();
        SmallCourtesy.Dispose();
        Publisher.Dispose();
        PhoneService.Dispose();
    }

    private Release1PresentationInputs BuildPresentationInputs()
    {
        var state = _story.State;
        Release1SmallCourtesyAssignment? smallCourtesyAssignment = null;
        Release1WrongAddressAssignment? wrongAddressAssignment = null;
        Release1WrongAddressProgress? wrongAddressProgress = null;
        Release1RoomWithNoNameAssignment? roomWithNoNameAssignment = null;
        Release1RoomWithNoNameProgress? roomWithNoNameProgress = null;
        Release1ShortNoticeAssignment? shortNoticeAssignment = null;
        Release1ShortNoticeProgress? shortNoticeProgress = null;
        Release1KeepTheLightsOffAssignment? keepTheLightsOffAssignment = null;
        Release1KeepTheLightsOffProgress? keepTheLightsOffProgress = null;
        Release1TheEnvelopeAssignment? theEnvelopeAssignment = null;
        Release1TheEnvelopeProgress? theEnvelopeProgress = null;
        if (state is not null)
        {
            var smallCourtesy = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.SmallCourtesy)];
            smallCourtesyAssignment = state.SmallCourtesyAssignments
                .Where(candidate => candidate.Attempt == smallCourtesy.Attempt)
                .OrderByDescending(candidate => candidate.Attempt)
                .FirstOrDefault();
            var wrongAddress = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.WrongAddress)];
            wrongAddressAssignment = state.WrongAddressAssignments
                .SingleOrDefault(candidate => candidate.Attempt == wrongAddress.Attempt);
            wrongAddressProgress = state.WrongAddressProgress
                .SingleOrDefault(candidate => candidate.Attempt == wrongAddress.Attempt);
            var room = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.RoomWithNoName)];
            roomWithNoNameAssignment = state.RoomWithNoNameAssignments
                .SingleOrDefault(candidate => candidate.Attempt == room.Attempt);
            roomWithNoNameProgress = state.RoomWithNoNameProgress
                .SingleOrDefault(candidate => candidate.Attempt == room.Attempt);
            var shortNotice = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.ShortNotice)];
            shortNoticeAssignment = state.ShortNoticeAssignments
                .SingleOrDefault(candidate => candidate.Attempt == shortNotice.Attempt);
            shortNoticeProgress = state.ShortNoticeProgress
                .SingleOrDefault(candidate => candidate.Attempt == shortNotice.Attempt);
            var keepTheLightsOff = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.KeepTheLightsOff)];
            keepTheLightsOffAssignment = state.KeepTheLightsOffAssignments
                .SingleOrDefault(candidate => candidate.Attempt == keepTheLightsOff.Attempt);
            keepTheLightsOffProgress = state.KeepTheLightsOffProgress
                .SingleOrDefault(candidate => candidate.Attempt == keepTheLightsOff.Attempt);
            var theEnvelope = state.Missions[Release1MissionCatalog.IndexOf(Release1MissionCatalog.TheEnvelope)];
            theEnvelopeAssignment = state.TheEnvelopeAssignments
                .SingleOrDefault(candidate => candidate.Attempt == theEnvelope.Attempt);
            theEnvelopeProgress = state.TheEnvelopeProgress
                .SingleOrDefault(candidate => candidate.Attempt == theEnvelope.Attempt);
        }
        return new Release1PresentationInputs(
            state,
            Publisher.PromptVisible,
            Publisher.EligibilityObserving,
            SmallCourtesy.Presenter.View,
            smallCourtesyAssignment,
            _mode == Release1PresentationMode.Native ? WrongAddress.Presenter.View : null,
            wrongAddressAssignment,
            wrongAddressProgress,
            _mode == Release1PresentationMode.Native ? RoomWithNoName.Presenter.View : null,
            roomWithNoNameAssignment,
            roomWithNoNameProgress,
            ReadHoldRoomMarker(),
            _mode == Release1PresentationMode.Native ? ShortNotice.Presenter.View : null,
            shortNoticeAssignment,
            shortNoticeProgress,
            _mode == Release1PresentationMode.Native ? KeepTheLightsOff.Presenter.View : null,
            keepTheLightsOffAssignment,
            keepTheLightsOffProgress,
            _mode == Release1PresentationMode.Native ? TheEnvelope.Presenter.View : null,
            theEnvelopeAssignment,
            theEnvelopeProgress);
    }

    /// <summary>
    /// The resolved HQ door position, shared by every mission whose active quest entry lives at the
    /// Syndicate HQ hold room (Room With No Name's hold entry and The Envelope's one entry), or null
    /// when the door observer has not resolved a door yet. Null is a first-class outcome, not an
    /// error: each entry is shown with no marker and this composition logs one line per load, not one
    /// per mission. No mission ever gates on HQ entry.
    /// </summary>
    private Release1WorldPoint? ReadHoldRoomMarker()
    {
        if (_holdRoomMarker is null) return null;
        try
        {
            var marker = _holdRoomMarker();
            if (marker is null)
            {
                if (!_loggedMissingHoldMarker)
                {
                    _loggedMissingHoldMarker = true;
                    _log?.Invoke("The Syndicate HQ door has not resolved yet; the hold objective is showing without a marker.");
                }
                return null;
            }
            return marker;
        }
        catch
        {
            return null;
        }
    }
}
