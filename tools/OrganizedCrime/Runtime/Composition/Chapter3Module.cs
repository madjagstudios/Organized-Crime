using OrganizedCrime.Runtime.Release1.World;
// OC-151: the Chapter 3 setup module. Builds the day clock, the runtime service, the
// Arthur adapters, the officer roster, the overnight line, the wallet, the
// presentation projector, the HQ presence/exit links, the demand world, the visit
// engine and the checkpoints, in construction order: the service is constructed
// and QA-published before Arthur, and each collaborator is assigned via its setter
// around the patch call in declaration order. Deferred lambdas read the
// composition root at fire time, matching the Mod.cs field reads
// they replace. The log/warn parameters are the raw sinks; service delegates prefix
// "[Organized Crime] " like Mod.cs.
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using UnityEngine;
using OrganizedCrime.Runtime.Chapter3;

namespace OrganizedCrime.Runtime.Composition;

internal sealed record Chapter3CoreOutputs(
    IChapter3DayClock DayClock,
    IChapter3ArthurWorld Arthur,
    Chapter3RuntimeService Service,
    OcOfficerRosterService Roster,
    IDisposable OvernightLine);

internal static class Chapter3Module
{
    public static Chapter3CoreOutputs BuildCore(
        OcCompositionRoot root,
        IOcEngineAdapters engine,
        SyndicateHqHostContextAdapter hqContext,
        Release1StoryRuntimeService story,
        Action<string> warn,
        Action<string> log)
    {
        Action<string> serviceWarn = message => warn($"[Organized Crime] {message}");
        Action<string> serviceLog = message => log($"[Organized Crime] {message}");
        var dayClock = engine.CreateDayClock(serviceWarn);
        root.DayClock = dayClock;
        var service = new Chapter3RuntimeService(
            hqContext,
            OcCompositionRoot.CreateChapter3StateRepository,
            new Chapter3StoryGate(() => root.Story?.State),
            dayClock,
            serviceLog,
            new Chapter3WarehouseOwnership(() => root.WarehouseRuntime?.OwnershipUnlocked ?? false));
        root.Chapter3 = service;
        engine.CompositionTrace?.Add("step:Chapter3.Service");
        Chapter3QaAccess.Service = service;
        engine.CompositionTrace?.Add("step:Chapter3.QaAccess");
        // OC-130: Arthur's proof visit is host only; the visit pump idles without a world.
        var arthur = engine.CreateArthurRuntime(serviceWarn, engine.IsHost);
        root.Arthur = arthur;
        service.ArthurWorld = arthur;
        // OC-135: Arthur takes hits for the beating, never as a native NPC.
        if (!engine.ApplyArthurResponse(serviceWarn))
        {
            warn("[Organized Crime] Arthur's native reaction suppression is incomplete; he may react like a native NPC.");
        }
        service.ArthurClock = () => Time.realtimeSinceStartup;
        var probe = engine.CreateBeatProbeWorld(serviceWarn, engine.IsHost);
        service.BeatProbeWorld = probe;
        // OC-135: Arthur's beating reads and writes the player through the
        // S1API adapter; unwired, no beating starts.
        var beating = engine.CreateBeatingWorld(serviceWarn, engine.IsHost);
        service.BeatingWorld = beating;
        // OC-140: the shared OC officer roster, host only; the Chapter 3 QA
        // officers subcommands drive it through the runtime service link.
        var officerWorld = engine.CreateOfficerWorld(serviceWarn);
        var roster = new OcOfficerRosterService(officerWorld, log: serviceLog);
        engine.CompositionTrace?.Add("step:Chapter3.Roster");
        root.OfficerRoster = roster;
        service.OfficerRoster = roster;
        var overnight = engine.CreateOvernightLine(service, serviceLog);
        root.OvernightLine = overnight;
        return new Chapter3CoreOutputs(dayClock, arthur, service, roster, overnight);
    }

    public static void BuildWallet(IOcEngineAdapters engine, Chapter3RuntimeService service, IChapter3World world)
    {
        service.Wallet = new Chapter3Wallet(world);
        service.DeadDropWorld = world;
        if (world is IProductionActivityWorld activityWorld) service.WireShutdownWorld(activityWorld);
        engine.CompositionTrace?.Add("step:Chapter3.Wallet");
    }

    public static Chapter3PresentationProjector? BuildProjector(
        OcCompositionRoot root,
        IOcEngineAdapters engine,
        IRelease1NativePresentation? native,
        Chapter3RuntimeService chapter3Service,
        Action<string> log)
    {
        if (native is not null)
        {
            Action<string> serviceLog = message => log($"[Organized Crime] {message}");
            var projector = new Chapter3PresentationProjector(
                chapter3Service,
                () => chapter3Service.GetPresentationPlan(
                    root.Production?.Projector?.LastPlan?.Decision is not null),
                native,
                new Chapter3PresentationCommandRouter(chapter3Service),
                serviceLog,
                decisionOwnedElsewhere: () => root.Production?.Projector?.LastPlan?.Decision is not null);
            engine.CompositionTrace?.Add("step:Chapter3.Projector");
            return projector;
        }
        else
        {
            log("[Organized Crime] Chapter 3 has no fallback presenter; Nell's messages stay pending while the presentation mode is ImguiFallback.");
            return null;
        }
    }

    public static void BuildPresence(IOcEngineAdapters engine, OcCompositionRoot root, Chapter3RuntimeService service, SyndicateHqRuntimeService hqRuntime)
    {
        // OC-132: Arthur's HQ presence reads the Chapter 3 phase and the
        // HQ service's own inside state; the pump parks without it.
        service.HqPresence = new Chapter3SyndicateHqPresence(
            () => root.Chapter3!.Phase == Chapter3RuntimePhase.Active,
            () => hqRuntime.IsInside);
        // OC-135: a beating that starts inside the HQ leaves through
        // the HQ service's own exit.
        engine.CompositionTrace?.Add("step:Chapter3.HqPresence");
        service.BeatingHq = new Chapter3SyndicateHqExit(
            () => hqRuntime.IsInside,
            () => hqRuntime.TryExit());
        engine.CompositionTrace?.Add("step:Chapter3.BeatingHq");
    }

    public static void BuildDemand(
        OcCompositionRoot root,
        IOcEngineAdapters engine,
        Chapter3RuntimeService service,
        ISyndicateHqStorageRuntime holdRoomStorage,
        Func<SyndicateHqDoorObserver?> getObserver,
        IRelease1PayphoneCue? banner,
        Action<string> warn)
    {
        Action<string> serviceWarn = message => warn($"[Organized Crime] {message}");
        service.WireStorageRoom(
            () => holdRoomStorage.ReadHoldRoom(),
            () =>
            {
                var fingerprint = getObserver()?.CurrentFingerprint;
                return fingerprint is null || !fingerprint.Position.IsFinite ? null :
                    new Release1WorldPoint(fingerprint.Position.X, fingerprint.Position.Y, fingerprint.Position.Z);
            });
        service.WireCashWorld(new Chapter3CashStorageWorld(holdRoomStorage));
        // OC-136: the weekly production demand reads warehouse stations,
        // products, and the family locker through the S1API adapter; the
        // HQ door marker reuses the door observer's fingerprint.
        service.DemandWorld = engine.CreateDemandWorld(
            holdRoomStorage,
            hqDoor: () =>
            {
                var fingerprint = getObserver()?.CurrentFingerprint;
                if (fingerprint is null || !fingerprint.Position.IsFinite) return null;
                return new Release1WorldPoint(fingerprint.Position.X, fingerprint.Position.Y, fingerprint.Position.Z);
            },
            engine.IsHost,
            serviceWarn);
        // OC-134: Arthur's visit engine reads the gated player conditions
        // and rings through the Release 1 phone mechanism; unwired, the
        // engine idles and missed evenings only accrue.
        service.VisitConditions = engine.CreateVisitConditions(serviceWarn);
        service.ArthurPhone = engine.CreateArthurPhone(banner, engine.IsHost, serviceWarn);
    }

    public static void BuildChiefLinks(IOcEngineAdapters engine, Chapter3RuntimeService service, Release1ChiefService chiefService)
    {
        // OC-118: Campbell's protection retainer. The quest line reads the Chief's snapshot, the
        // QA commands drive his service, and the law gates consult his cover read; all three are
        // read-only from this side, and every gate fails open to vanilla behaviour.
        service.ProtectionSnapshot = () => chiefService.ReadProtectionSnapshot();
        service.ChiefProtectionQa = chiefService;
        service.ChiefForcedWantedQa = chiefService;
        engine.CompositionTrace?.Add("step:Chapter3.ChiefLinks");
    }

    public static Chapter3CheckpointService BuildCheckpoints(
        OcCompositionRoot root,
        Chapter3RuntimeService service,
        Release1ChiefService chiefService,
        IProductionActivityMissionWorld world,
        OcOfficerRosterService roster,
        IOcEngineAdapters engine,
        Action<string> warn,
        Action<string> log)
    {
        Action<string> serviceWarn = message => warn($"[Organized Crime] {message}");
        Action<string> serviceLog = message => log($"[Organized Crime] {message}");
        // OC-141: police checkpoints near the busiest property, staffed by the
        // shared officer roster. The checkpoint QA drives it through the runtime link.
        var checkpointWorld = engine.CreateCheckpointWorld(serviceWarn, serviceLog);
        var checkpoints = new Chapter3CheckpointService(
            service,
            chiefService,
            world,
            checkpointWorld,
            roster,
            log: serviceLog);
        root.Checkpoints = checkpoints;
        service.Checkpoints = checkpoints;
        engine.CompositionTrace?.Add("step:Chapter3.Checkpoints");
        service.WireSweeps(() =>
        {
            if (!engine.IsHost()) return new(false, null, null, null);
            var minutes = world.TryReadCanonicalTotalMinutes(out var now) == Release1SmallCourtesyWorldReadStatus.Ready
                ? (double?)now : null;
            return new(true, minutes, chiefService.ReadTier(), chiefService.ReadSweepProtection());
        }, root.Chief?.SweepPresentation);
        var exposure = new S1ApiChapter3PlayerExposureReader(serviceWarn);
        service.WireRaids(() =>
        {
            if (!engine.IsHost()) return new(false, null, null, null, null);
            var minutes = world.TryReadCanonicalTotalMinutes(out var now) == Release1SmallCourtesyWorldReadStatus.Ready ? (double?)now : null;
            bool? arrest = exposure.TryRead(out var player) && player.Arrested is { } arrested && player.PursuitLevel is { } pursuit
                ? arrested || pursuit == (int)S1API.Law.PursuitLevel.Arresting : null;
            return new(true, minutes, chiefService.ReadTier(), chiefService.ReadSweepProtection(), arrest);
        }, id => root.LocalPressure?.Service.TryApplyRaidCooling(id) == true,
        id => root.LocalPressure?.Service is { Phase: LocalPressureRuntimePhase.Active } pressure &&
            service.State is { } chapter && pressure.TryGetState(chapter.PlayerId, out var state)
                ? state!.LastAppliedRaidCoolingId == id : (bool?)null);
        var patrols = new Chapter3PatrolService(roster,
            () => ReadPatrolInput(service, chiefService, world, checkpointWorld, engine), serviceLog, serviceWarn);
        root.Patrols = patrols;
        service.WirePressureStatus(() => root.LocalPressure?.Service.QaStatus() ?? new[] { "heat runtime: not wired." });
        service.WirePatrols(patrols,
            tier => root.LocalPressure?.Service.QaSetTier(tier) ?? new(false, "heat refused: runtime not wired."),
            chiefService.QaSetLockdown);
        return checkpoints;
    }

    private static Chapter3PatrolInput ReadPatrolInput(Chapter3RuntimeService service,
        Release1ChiefService chief, IProductionActivityMissionWorld world,
        IChapter3CheckpointWorld positions, IOcEngineAdapters engine)
    {
        if (!engine.IsHost()) return new(false, false, null, null, false, false, null, 0);
        var ready = service.Phase == Chapter3RuntimePhase.Active && service.State is not null;
        var minutes = world.TryReadCanonicalTotalMinutes(out var now) == Release1SmallCourtesyWorldReadStatus.Ready
            ? (double?)now : null;
        var tier = chief.ReadTier();
        var covered = chief.IsProtected();
        var lockdown = chief.ReadLockdownEngaged();
        var checkpointDemand = covered ? 0 : tier switch
        {
            LocalPressureTier.Watched => Chapter3PatrolRules.WatchedCheckpoints,
            LocalPressureTier.Critical => Chapter3PatrolRules.CriticalCheckpoints,
            _ => 0
        };
        OcOfficerPosition? target = null;
        // D3 owns the daily pick and its fallback; an unavailable target blocks spawn.
        var state = service.State;
        var surge = Chapter3SweepFeature.Profile(state, minutes, chief.ReadSweepProtection());
        var code = surge is null ? service.ReadCheckpointTarget().PropertyCode : state?.Sweep?.TargetCode;
        if (code is not null && positions.TryReadOwnedPropertyPositions(out var owned) == Chapter3CheckpointWorldStatus.Ok)
        {
            var match = owned.FirstOrDefault(p => p is not null && string.Equals(p.PropertyCode, code, StringComparison.Ordinal));
            if (match is not null) target = new(match.X, match.Y, match.Z);
        }
        return new(true, ready, minutes, tier, covered, lockdown, target, checkpointDemand, surge);
    }

    public static void ApplyCheckpointSwapPatch(IOcEngineAdapters engine, Action<string> warn)
    {
        Action<string> serviceWarn = message => warn($"[Organized Crime] {message}");
        if (!engine.ApplyCheckpointSwap(serviceWarn))
        {
            warn("[Organized Crime] OC checkpoint staffing patch is incomplete; cloned officers cannot take checkpoint duty.");
        }
    }
}
