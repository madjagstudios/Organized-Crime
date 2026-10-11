using OrganizedCrime.Runtime.Release1.World;
// OC-151: the Release 1 setup module. Builds the story runtime, the presentation mode
// and native presenters, the small-courtesy world, the production composition, the
// chief composition and the protection gates, in today's order. Deferred lambdas read
// the composition root at fire time, matching the Mod.cs field reads they replace;
// build-time values arrive as parameters. The log/warn parameters are the raw sinks;
// service delegates prefix "[Organized Crime] " exactly like the Mod.cs lambdas.
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;

namespace OrganizedCrime.Runtime.Composition;

internal static class Release1Module
{
    public static Release1StoryRuntimeService BuildStory(IOcEngineAdapters engine, SyndicateHqHostContextAdapter hqContext)
    {
        var story = new Release1StoryRuntimeService(hqContext, OcCompositionRoot.CreateRelease1StoryRepository);
        engine.CompositionTrace?.Add("step:Release1.Story");
        return story;
    }

    public static Release1PresentationMode BuildPresentationMode(IOcEngineAdapters engine) =>
        engine.ReadPresentationMode();

    public static IRelease1NativePresentation? BuildRelease1Native(
        IOcEngineAdapters engine,
        Release1PresentationMode mode,
        Action<string> warn)
    {
        return mode == Release1PresentationMode.Native
            ? engine.CreateRelease1Native(engine.ReadQuestPolicy(), message => warn($"[Organized Crime] {message}"))
            : null;
    }

    public static IRelease1SmallCourtesyWorld BuildWorld(
        IOcEngineAdapters engine,
        IRelease1StoryHostContext hqContext,
        ISyndicateHqStorageRuntime? holdRoomStorage) =>
        engine.CreateSmallCourtesyWorld(hqContext, holdRoomStorage);

    public static Release1ProductionComposition BuildProduction(
        OcCompositionRoot root,
        IOcEngineAdapters engine,
        Release1StoryRuntimeService story,
        IRelease1StoryHostContext hqContext,
        IRelease1ProductionWorld world,
        Release1PresentationMode mode,
        IRelease1NativePresentation? native,
        Func<SyndicateHqDoorObserver?> getObserver,
        Action<string> warn)
    {
        Action<string> serviceWarn = message => warn($"[Organized Crime] {message}");
        var composition = new Release1ProductionComposition(
            story,
            new Release1PostBenziesUnlockReader(hqContext, engine.CreateCartelStatusSource()),
            engine.CreatePhoneCallQueue(),
            world,
            serviceWarn,
            mode: mode,
            native: native,
            holdRoomMarker: () =>
            {
                var fingerprint = getObserver()?.CurrentFingerprint;
                if (fingerprint is null || !fingerprint.Position.IsFinite) return null;
                return new Release1WorldPoint(fingerprint.Position.X, fingerprint.Position.Y, fingerprint.Position.Z);
            },
            nellDecisionOwnedElsewhere: () => root.Chapter3Projector?.LastPlan?.Decision is not null);
        root.Production = composition;
        engine.CompositionTrace?.Add("step:Release1.Production");
        story.Timing = root.Timing;
        composition.Timing = root.Timing;
        composition.KeepTheLightsOff.Service.Timing = root.Timing;
        composition.TheEnvelope.Service.Timing = root.Timing;
        return composition;
    }

    public static Release1ChiefComposition BuildChief(
        OcCompositionRoot root,
        IOcEngineAdapters engine,
        Release1StoryRuntimeService story,
        IChiefWorld world,
        NativeLawResponseModComposition law,
        LocalPressureRuntimeComposition localPressure,
        Release1PresentationMode mode,
        Action<string> warn)
    {
        Action<string> serviceWarn = message => warn($"[Organized Crime] {message}");
        // OC-73. The Chief needs the Local Pressure runtime, which does not exist until
        // the law composition is built above, so he is composed here, as a sibling of
        // the production composition rather than a child of it, which also keeps that
        // file free of the word Chief (see Release1ProductionCompositionTests).
        IRelease1NativePresentation? chiefNative = mode == Release1PresentationMode.Native
            ? engine.CreateChiefNative(engine.ReadQuestPolicy(), serviceWarn)
            : null;
        var localPressureService = localPressure.Service;
        var composition = new Release1ChiefComposition(
            story,
            world,
            law.ChiefObserver,
            playerId => localPressureService.TryGetState(playerId, out var state) ? state : null,
            playerId => localPressureService.TryApplyRecordWipe(playerId, localPressureService.SessionEpoch, localPressureService.LoadEpoch),
            (out Guid sessionEpoch, out long loadEpoch) =>
            {
                sessionEpoch = localPressureService.SessionEpoch;
                loadEpoch = localPressureService.LoadEpoch;
                return localPressureService.Phase == LocalPressureRuntimePhase.Active;
            },
            mode,
            chiefNative,
            serviceWarn,
            chapter3SuccessfulJobs: () => root.Chapter3?.State?.JobBoard.SuccessfulJobs ?? 0,
            checkpointWorld: engine.CreateProtectionCheckpointWorld(serviceWarn),
            playerDay: absoluteDay => root.Chapter3?.State?.ChapterDayAt(absoluteDay),
            isAuthoritativeHost: engine.IsHost,
            exposureReader: engine.CreateExposureReader(serviceWarn),
            curfewReader: engine.CreateCurfewReader(serviceWarn),
            wantedWriter: engine.CreateWantedWriter(serviceWarn),
            forcedWantedDispatch: new Chapter3ForcedWantedDispatch(
                law.HostAdapter,
                law.LawResponseAdapter,
                serviceWarn),
            pursuitHold: engine.CreatePursuitHold(serviceWarn));
        engine.CompositionTrace?.Add("step:Release1.Chief");
        return composition;
    }

    public static void BuildProtectionGates(
        IOcEngineAdapters engine,
        IGameTimeWorld world,
        Release1ChiefService chiefService,
        Action<string> warn)
    {
        Func<bool> isCovered = () =>
        {
            try { return chiefService.IsProtected(); }
            catch { return false; }
        };
        Func<bool> isHost = () =>
        {
            try { return engine.IsHost(); }
            catch { return false; }
        };
        Func<int?> today = () =>
        {
            try
            {
                if (world.TryReadCanonicalTotalMinutes(out var read) != Release1SmallCourtesyWorldReadStatus.Ready)
                    return null;
                var day = (int)Math.Floor(read / 1440d);
                return day < 0 ? null : day;
            }
            catch { return null; }
        };
        engine.ConfigureProtectionGate(isCovered, isHost, today);
        Action<string> serviceWarn = message => warn($"[Organized Crime] {message}");
        if (!engine.ApplyProtectionGate(serviceWarn))
        {
            warn("[Organized Crime] Chief Campbell's protection gates are incomplete; some native policing stays ungated while covered.");
        }
    }
}
