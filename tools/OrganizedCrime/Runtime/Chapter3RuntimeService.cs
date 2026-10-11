using OrganizedCrime.Runtime.Release1.World;
using OrganizedCrime.Core.Diagnostics;
using OrganizedCrime.Core.Lifecycle;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime.Chapter3;
using OrganizedCrime.Runtime.Police;

namespace OrganizedCrime.Runtime;

// OC-152: the Chapter 3 coordinator. Owns the single gate, the shared
// context, the save lifecycle and the day/week ticks; the feature classes
// and the QA facade hold the moved bodies and run only under this gate.
public sealed class Chapter3RuntimeService : IDisposable, IChapter3QaTarget, IChapter3CheckpointHost, IChapter3PatrolQaTarget, IChapter3SweepQaTarget, IChapter3StorageQaTarget, IChapter3CashQaTarget, IChapter3ShutdownQaTarget, IChapter3RaidQaTarget, IChapter3RaidSeizureQaTarget, IChapter3PressureQaTarget
{
    private Func<IReadOnlyList<string>>? _pressureQaStatus;
    internal void WirePressureStatus(Func<IReadOnlyList<string>> read) => _pressureQaStatus = read;
    IReadOnlyList<string> IChapter3PressureQaTarget.PressureStatus() =>
        _pressureQaStatus?.Invoke() ?? Array.Empty<string>();
    private Chapter3RaidFeature? _raid;
    private IChapter3RaidWorld? _raidWorld;
    internal void WireRaids(Func<Chapter3RaidInput> input, Func<string, bool> cooling, Func<string, bool?>? coolingApplied = null)
    {
        if (_raidWorld is IChapter3RaidSeizureWorld seizure)
            _raid ??= new(_ctx, _raidWorld, seizure, input, cooling, coolingApplied);
    }
    Chapter3QaResult IChapter3RaidSeizureQaTarget.RaidNow(string? code, bool forceAway) => _raid?.Queue(code, forceAway) ?? new(false, "raid: not wired.");
    Chapter3QaResult IChapter3RaidSeizureQaTarget.RaidSkipCap() => _raid?.SkipCap() ?? new(false, "raid: not wired.");
    IReadOnlyList<string> IChapter3RaidSeizureQaTarget.RaidStatus() => _raid?.Status() ?? new[] { "raid: not wired." };
    private Chapter3RaidPreview? _raidPreview;
    internal void WireRaidWorld(IChapter3RaidWorld world)
    {
        _raidWorld = world;
        _raidPreview ??= new(world,
            () => _ctx.TryReadCanonicalMinutesLocked(out var minutes) ? minutes : null, () => _ctx.State);
    }
    IReadOnlyList<string> IChapter3RaidQaTarget.RaidPreview(string? code) =>
        _ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null
            ? new[] { "raid preview refused: Chapter 3 is not active." }
            : _raidPreview?.Preview(code) ?? new[] { "raid preview: not wired." };
    Chapter3QaResult IChapter3StorageQaTarget.StorageCommand(string command) => _ctx.Storage.Queue(command);
    IReadOnlyList<string> IChapter3StorageQaTarget.StorageStatus() => _ctx.Storage.Status();
    Chapter3QaResult IChapter3CashQaTarget.CashCommand(string command) => _ctx.Cash.Queue(command);
    IReadOnlyList<string> IChapter3CashQaTarget.CashStatus() => _ctx.Cash.Status();
    internal void WireCashWorld(IHoldRoomWorld world) => _ctx.CashWorld = world;
    internal void WireShutdownWorld(IProductionActivityWorld world) => _ctx.ShutdownWorld = world;
    Chapter3QaResult IChapter3ShutdownQaTarget.ShutdownCommand(string command) => _ctx.Shutdown.Queue(command);
    IReadOnlyList<string> IChapter3ShutdownQaTarget.ShutdownStatus() => _ctx.Shutdown.Status();

    internal void WireStorageRoom(Func<Release1HoldRoomSnapshot?> reader, Func<Release1WorldPoint?> marker)
    {
        _ctx.StorageRoomReader = reader;
        _ctx.StorageRoomMarker = marker;
    }

    internal void WirePatrols(Chapter3PatrolService patrols, Func<LocalPressureTier, Chapter3QaResult> heat, Func<bool, Chapter3QaResult> lockdown)
    {
        _qaFacade.Patrols = patrols;
        _qaFacade.PatrolHeat = heat;
        _qaFacade.PatrolLockdown = lockdown;
    }
    Chapter3QaResult IChapter3PatrolQaTarget.PatrolAuto(bool enabled) => _qaFacade.SetPatrolAuto(enabled);
    Chapter3QaResult IChapter3PatrolQaTarget.PatrolHeat(LocalPressureTier tier) => _qaFacade.SetPatrolHeat(tier);
    Chapter3QaResult IChapter3PatrolQaTarget.PatrolLockdown(bool enabled) => _qaFacade.SetPatrolLockdown(enabled);

    // Internal explicit QA capability preserves OC-152's public surface and lock pins.
    Chapter3QaResult IChapter3PatrolQaTarget.PatrolHere(OcPatrolDuty duty)
    {
        // No gate here: console, roster Update and lifecycle run on the main thread.
        if (_ctx.Disposed) return new(false, "patrol refused: Chapter 3 service disposed.");
        if (_ctx.OfficerRoster is null) return new(false, "patrol refused: roster not wired.");
        try { return _ctx.OfficerRoster.QaPatrolHere(duty); }
        catch (Exception ex) { return new(false, $"patrol refused: the roster threw ({ex.Message})."); }
    }

    Chapter3QaResult IChapter3PatrolQaTarget.PatrolStop()
    {
        // No gate here: console, roster Update and lifecycle run on the main thread.
        if (_ctx.Disposed) return new(false, "patrol refused: Chapter 3 service disposed.");
        if (_ctx.OfficerRoster is null) return new(false, "patrol refused: roster not wired.");
        try { return _ctx.OfficerRoster.QaPatrolStop(); }
        catch (Exception ex) { return new(false, $"patrol stop refused: the roster threw ({ex.Message})."); }
    }

    IReadOnlyList<string> IChapter3PatrolQaTarget.PatrolStatus()
    {
        // No gate here: console, roster Update and lifecycle run on the main thread.
        if (_ctx.Disposed) return new[] { "patrol: Chapter 3 service disposed." };
        if (_ctx.OfficerRoster is null) return new[] { "patrol: roster not wired." };
        try { return (_qaFacade.Patrols?.Status() ?? Array.Empty<string>()).Concat(_ctx.OfficerRoster.QaPatrolStatus()).ToArray(); }
        catch (Exception ex) { return new[] { $"patrol status: the roster threw ({ex.Message})." }; }
    }

    internal void WireSweeps(Func<Chapter3SweepInput> read, IRelease1NativePresentation? presentation) =>
        _sweep.Wire(read, presentation);
    internal void SuspendSweeps() { _sweep.Suspend(); _raid?.Suspend(); _raidPreview?.Reset(); }
    Chapter3QaResult IChapter3SweepQaTarget.SweepCommand(string command) => _sweep.Queue(command);
    IReadOnlyList<string> IChapter3SweepQaTarget.SweepStatus() => _sweep.Status(State)
        .Concat(((IChapter3PatrolQaTarget)this).PatrolStatus()).ToArray();

    private readonly Chapter3SweepFeature _sweep;
    private readonly object _gate = new();
    private readonly Chapter3FeatureContext _ctx;
    private readonly Chapter3OffersFeature _offers;
    private readonly Chapter3DeliveryFeature _delivery;
    private readonly Chapter3DemandFeature _demand;
    private readonly Chapter3PaymentsFeature _payments;
    private readonly Chapter3WarehouseFeature _warehouse;
    private readonly Chapter3ArthurFeature _arthur;
    private readonly SaveLifecycle<Chapter3RuntimePhase, Chapter3RuntimeLifecycleResult, Chapter3SaveEnvelope> _saveLifecycle;
    // OC-151: the QA facade; it runs the QA bodies under this gate. OC-152
    // moved the remaining QA members there.
    private readonly Chapter3QaFacade _qaFacade;

    public Chapter3RuntimeService(
        IRelease1StoryHostContext context,
        IChapter3Repository repository,
        IChapter3StoryGate storyGate,
        IChapter3DayClock clock,
        Action<string>? log = null,
        IChapter3WarehouseOwnership? ownership = null)
        : this(context, storyGate, clock, log, ownership)
    {
        _ctx.Repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Chapter3RuntimeService(
        IRelease1StoryHostContext context,
        Func<string, IChapter3Repository> repositoryFactory,
        IChapter3StoryGate storyGate,
        IChapter3DayClock clock,
        Action<string>? log = null,
        IChapter3WarehouseOwnership? ownership = null)
        : this(context, storyGate, clock, log, ownership)
    {
        // The repository is bound per save folder at load complete, like the Release 1 story.
        _ctx.RepositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
    }

    private Chapter3RuntimeService(
        IRelease1StoryHostContext context,
        IChapter3StoryGate storyGate,
        IChapter3DayClock clock,
        Action<string>? log,
        IChapter3WarehouseOwnership? ownership)
    {
        _ctx = new Chapter3FeatureContext();
        _ctx.Context = context ?? throw new ArgumentNullException(nameof(context));
        _ctx.StoryGate = storyGate ?? throw new ArgumentNullException(nameof(storyGate));
        _ctx.Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _ctx.Ownership = ownership;
        _ctx.Log = log ?? (_ => { });
        _ctx.LoggedDeliveryHolds = new OnceLog(_ctx.Log);
        _ctx.Repository = null!;
        _ctx.Clock.DayPassed += OnDayPassed;
        _ctx.Clock.WeekPassed += OnWeekPassed;
        _ctx.ArthurPump = new Chapter3ArthurVisitPump(() => _ctx.ArthurClock());
        _saveLifecycle = CreateSaveLifecycle();
        _delivery = new Chapter3DeliveryFeature(_ctx, OnDeadDropClosed);
        _ctx.Storage = new Chapter3StorageFeature(_ctx, _delivery);
        _ctx.Cash = new Chapter3CashFeature(_ctx, _delivery);
        _ctx.Shutdown = new Chapter3ShutdownFeature(_ctx, _delivery);
        _demand = new Chapter3DemandFeature(_ctx);
        _sweep = new Chapter3SweepFeature(_ctx);
        _payments = new Chapter3PaymentsFeature(_ctx);
        _warehouse = new Chapter3WarehouseFeature(_ctx);
        _arthur = new Chapter3ArthurFeature(_ctx);
        _offers = new Chapter3OffersFeature(_ctx, _delivery);
        _qaFacade = new Chapter3QaFacade(_gate, () => _ctx.Disposed, _ctx, _offers, _delivery, _demand, _payments, _arthur);
    }

    public Chapter3RuntimePhase Phase { get { lock (_gate) return _ctx.Phase; } }
    public Chapter3State? State { get { lock (_gate) return _ctx.State; } }

    /// <summary>
    /// The wallet reply-button payments debit. Null until Mod.cs wires the
    /// native adapter; payments with no wallet report Unavailable and record
    /// nothing.
    /// </summary>
    public IChapter3Wallet? Wallet
    {
        get { lock (_gate) return _ctx.Wallet; }
        set { lock (_gate) _ctx.Wallet = value; }
    }

    /// <summary>
    /// The dead-drop world the job runner freezes and delivers through. Null
    /// until Mod.cs wires the native adapter; without it a DeadDrop accept
    /// stays unfrozen and deliveries never run.
    /// </summary>
    public IChapter3DeliveryWorld? DeadDropWorld
    {
        get { lock (_gate) return _ctx.DeadDropWorld; }
        set
        {
            lock (_gate)
            {
                _ctx.DeadDropWorld = value;
                _ctx.StorageRoomReader = value is IHoldRoomWorld roomWorld
                    ? () => roomWorld.TryReadHoldRoom(out var room) == Release1SmallCourtesyWorldReadStatus.Ready ? room : null
                    : null;
            }
        }
    }

    /// <summary>
    /// The warehouse the story purchase acquires. Null until Mod.cs wires the
    /// adapter over the fish warehouse runtime service; without it the offer
    /// never opens and a buy tap reports Unavailable.
    /// </summary>
    public IChapter3WarehouseAcquisition? WarehouseAcquisition
    {
        get { lock (_gate) return _ctx.WarehouseAcquisition; }
        set { lock (_gate) _ctx.WarehouseAcquisition = value; }
    }

    /// <summary>
    /// OC-117: the warehouse the capacity purchase applies to. Null until
    /// Mod.cs wires the adapter over the runtime property host; without it
    /// a buy tap reports Unavailable and the tier still applies at attach.
    /// </summary>
    public IChapter3WarehouseCapacity? WarehouseCapacity
    {
        get { lock (_gate) return _ctx.WarehouseCapacity; }
        set { lock (_gate) _ctx.WarehouseCapacity = value; }
    }

    /// <summary>
    /// OC-138: the top-screen banner sender the owner QA notice show
    /// and hide post through. Null until Mod.cs wires the HUD target.
    /// </summary>
    public IFishWarehouseStaffReturnBannerSender? StaffReturnBannerSender
    {
        get { lock (_gate) return _ctx.StaffReturnBannerSender; }
        set { lock (_gate) _ctx.StaffReturnBannerSender = value; }
    }

    /// <summary>
    /// OC-118: the read-only protection view the quest planner presents. Null until Mod.cs wires
    /// the Chief service's own snapshot read; without it (or when it throws) the quest carries no
    /// protection line. A read-only signal: Chapter 3 never writes the Chief's record through it.
    /// </summary>
    public Func<Release1ChiefProtectionSnapshot?>? ProtectionSnapshot
    {
        get { lock (_gate) return _ctx.ProtectionSnapshot; }
        set { lock (_gate) _ctx.ProtectionSnapshot = value; }
    }

    /// <summary>
    /// Owner QA only (OwnerQaKeys): the Chief service the forced-Wanted QA subcommands drive.
    /// Null until Mod.cs wires the Chief composition's own service.
    /// </summary>
    public IRelease1ChiefForcedWantedQa? ChiefForcedWantedQa
    {
        get { lock (_gate) return _ctx.ChiefForcedWantedQa; }
        set { lock (_gate) _ctx.ChiefForcedWantedQa = value; }
    }

    public IRelease1ChiefProtectionQa? ChiefProtectionQa
    {
        get { lock (_gate) return _ctx.ChiefProtectionQa; }
        set { lock (_gate) _ctx.ChiefProtectionQa = value; }
    }

    /// <summary>
    /// OC-140: the shared OC officer roster the officers QA subcommands drive.
    /// Null until Mod.cs wires the roster service; without it every officers
    /// subcommand refuses. The roster is host-session infrastructure, so these
    /// refuse only while unwired or disposed, never on the Chapter 3 phase.
    /// </summary>
    public OcOfficerRosterService? OfficerRoster
    {
        get { lock (_gate) return _ctx.OfficerRoster; }
        set { lock (_gate) _ctx.OfficerRoster = value; }
    }

    /// <summary>
    /// OC-141. The checkpoint service Mod.cs wires; the checkpoint QA
    /// subcommands drive it through this link and refuse while unwired.
    /// </summary>
    public Chapter3CheckpointService? Checkpoints
    {
        get => _qaFacade.Checkpoints;
        set => _qaFacade.Checkpoints = value;
    }

    /// <summary>
    /// OC-136: the production demand world the poll counts and hands
    /// over through. Null until Mod.cs wires the S1API adapter; without
    /// it demands never issue and the hand-over never runs.
    /// </summary>
    public IChapter3ProductionDemandWorld? DemandWorld
    {
        get { lock (_gate) return _ctx.DemandWorld; }
        set { lock (_gate) _ctx.DemandWorld = value; }
    }

    /// <summary>
    /// OC-130: Arthur's proof-visit world. Null until Mod.cs wires the
    /// S1API runtime; without it the visit pump idles and every arthur
    /// QA command refuses with "not wired up".
    /// </summary>
    public IChapter3ArthurWorld? ArthurWorld
    {
        get { lock (_gate) return _ctx.ArthurWorld; }
        set { lock (_gate) _ctx.ArthurWorld = value; }
    }

    /// <summary>
    /// OC-130: the visit pump's wall clock. Production sets the
    /// engine's realtime-since-startup; the default is a tick-count
    /// clock so the pump still bounds itself unwired, and tests set
    /// a fake.
    /// </summary>
    public Func<float> ArthurClock
    {
        get { lock (_gate) return _ctx.ArthurClock; }
        set { lock (_gate) _ctx.ArthurClock = value ?? throw new ArgumentNullException(nameof(value)); }
    }

    /// <summary>
    /// OC-132: Arthur's HQ presence input. Null until Mod.cs wires the
    /// adapter over the Chapter 3 phase and the HQ service's own
    /// inside state; without it the pump parks exactly like OC-130.
    /// </summary>
    public IChapter3HqPresence? HqPresence
    {
        get { lock (_gate) return _ctx.HqPresence; }
        set { lock (_gate) _ctx.HqPresence = value; }
    }

    /// <summary>
    /// OC-134: the visit-conditions world. Null until Mod.cs wires the
    /// S1API adapter; without it the engine never starts a visit.
    /// </summary>
    public IChapter3ArthurVisitConditions? VisitConditions
    {
        get { lock (_gate) return _ctx.VisitConditions; }
        set { lock (_gate) _ctx.VisitConditions = value; }
    }

    /// <summary>
    /// OC-134: Arthur's Chapter 3 phone fallback. Null until Mod.cs
    /// wires the S1API adapter; without it missed evenings accrue but
    /// the call never fires.
    /// </summary>
    public IChapter3ArthurPhone? ArthurPhone
    {
        get { lock (_gate) return _ctx.ArthurPhone; }
        set { lock (_gate) _ctx.ArthurPhone = value; }
    }

    /// Owner QA only (OwnerQaKeys): the beat-probe world. Null until Mod.cs wires the S1API
    /// adapter; without it every beat probe refuses with "not wired up". Wiring it also arms
    /// the invincible on/off memory, which is QA-only runtime memory, not Chapter 3 state.
    public IChapter3BeatProbeWorld? BeatProbeWorld
    {
        get { lock (_gate) return _ctx.BeatProbeWorld; }
        set { lock (_gate) { _ctx.BeatProbeWorld = value; _ctx.BeatInvincible = value is null ? null : new Chapter3BeatInvincibleProbe(value); } }
    }

    /// OC-135: the beating's player-side world. Null until Mod.cs
    /// wires the S1API adapter; without it no beating starts.
    public IChapter3BeatingWorld? BeatingWorld
    {
        get { lock (_gate) return _ctx.BeatingWorld; }
        set { lock (_gate) _ctx.BeatingWorld = value; }
    }

    /// OC-135: the beating's HQ door. Null until Mod.cs wires the
    /// adapter over the HQ service; without it no beating starts.
    public IChapter3BeatingHq? BeatingHq
    {
        get { lock (_gate) return _ctx.BeatingHq; }
        set { lock (_gate) _ctx.BeatingHq = value; }
    }

    // OC-108: deliberately not host-gated. Loads come from the main menu, so
    // PreLoad fires before the FishNet server starts; the reset only drops
    // previous-scene local state, so it is safe on any peer.
    public Chapter3RuntimeLifecycleResult OnPreLoad()
    {
        lock (_gate)
        {
            return _saveLifecycle.OnPreLoad();
        }
    }
    public Chapter3RuntimeLifecycleResult OnLoadComplete()
    {
        lock (_gate)
        {
            return _saveLifecycle.OnLoadComplete();
        }
    }
    public Chapter3RuntimeLifecycleResult OnSaveStart()
    {
        lock (_gate)
        {
            _raid?.Suspend();
            _raidPreview?.Reset();
            return _saveLifecycle.OnSaveStart();
        }
    }
    public Chapter3RuntimeLifecycleResult OnSaveComplete()
    {
        lock (_gate)
        {
            var result = _saveLifecycle.OnSaveComplete();
            if (_ctx.Phase == Chapter3RuntimePhase.Active) _raid?.Resume();
            return result;
        }
    }

    private SaveLifecycle<Chapter3RuntimePhase, Chapter3RuntimeLifecycleResult, Chapter3SaveEnvelope> CreateSaveLifecycle() =>
        new(new SaveLifecycleHooks<Chapter3RuntimePhase, Chapter3RuntimeLifecycleResult, Chapter3SaveEnvelope>
        {
            AwaitingLoadPhase = Chapter3RuntimePhase.AwaitingLoad,
            ActivePhase = Chapter3RuntimePhase.Active,
            SavingPhase = Chapter3RuntimePhase.Saving,
            QuarantinedPhase = Chapter3RuntimePhase.Quarantined,
            GetPhase = () => _ctx.Phase,
            SetPhase = phase => _ctx.Phase = phase,
            IsDisposed = () => _ctx.Disposed,
            BuildResult = BuildLifecycleResult,
            MapContextStatus = MapContextStatusForLifecycle,
            BeforeLoadGuard = ArmArthurForLifecycle,
            ReadContext = ReadContextForLifecycle,
            CreateRepository = CreateRepositoryForLifecycle,
            GetBoundSaveFolder = GetBoundSaveFolderForLifecycle,
            SameSaveFolder = SameSaveFolder,
            Load = LoadForLifecycle,
            Activate = ActivateForLifecycle,
            AfterLoad = AfterLoadForLifecycle,
            BeforeClear = AbortBeatingForLifecycle,
            ClearForLoad = ClearForLifecycle,
            PreserveQuarantined = PreserveQuarantinedSidecar,
            CaptureSaveSnapshot = () => _ctx.SaveSnapshot = _ctx.State,
            ClearSaveSnapshot = () => _ctx.SaveSnapshot = null,
            SaveUpdate = SaveUpdateForLifecycle,
        });

    private Chapter3RuntimeLifecycleResult BuildLifecycleResult(SaveLifecycleOutcome outcome, string? message) => outcome switch
    {
        SaveLifecycleOutcome.Disposed => Lifecycle(Chapter3RuntimeRejectReason.Disposed, "Service was disposed."),
        SaveLifecycleOutcome.PreLoadRepeat => Lifecycle(Chapter3RuntimeRejectReason.None, "PreLoad was already handled."),
        SaveLifecycleOutcome.LoadRepeat => Lifecycle(Chapter3RuntimeRejectReason.None, "LoadComplete was already handled."),
        SaveLifecycleOutcome.SaveStartRepeat => Lifecycle(Chapter3RuntimeRejectReason.None, "SaveStart was already handled."),
        SaveLifecycleOutcome.SaveCompleteNoop => Lifecycle(Chapter3RuntimeRejectReason.None, "SaveComplete was a lifecycle no-op."),
        SaveLifecycleOutcome.ContextPending => Lifecycle(Chapter3RuntimeRejectReason.ContextPending, "Host context is pending."),
        SaveLifecycleOutcome.NotAuthoritative => Lifecycle(Chapter3RuntimeRejectReason.NotAuthoritative, "Host authority is still settling during load."),
        SaveLifecycleOutcome.RepositoryUnbound => Lifecycle(Chapter3RuntimeRejectReason.RepositoryUnbound, "Repository was not bound to a validated save folder."),
        SaveLifecycleOutcome.PathMismatch => Lifecycle(Chapter3RuntimeRejectReason.RepositoryPathMismatch, "Repository save folder did not match host context."),
        SaveLifecycleOutcome.IdentityMismatch => Lifecycle(Chapter3RuntimeRejectReason.IdentityMismatch, "Chapter 3 sidecar identity did not match host context."),
        SaveLifecycleOutcome.SaveInactive => Lifecycle(Chapter3RuntimeRejectReason.Inactive, "Only an active Chapter 3 can save."),
        SaveLifecycleOutcome.PreLoadOk => Lifecycle(Chapter3RuntimeRejectReason.None, "Chapter 3 state was cleared for load."),
        SaveLifecycleOutcome.LoadOk => Lifecycle(Chapter3RuntimeRejectReason.None, "Chapter 3 runtime is active."),
        SaveLifecycleOutcome.SaveStartOk => Lifecycle(Chapter3RuntimeRejectReason.None, "Chapter 3 save snapshot captured."),
        SaveLifecycleOutcome.SaveCompleteOk => Lifecycle(Chapter3RuntimeRejectReason.None, "Chapter 3 save snapshot persisted."),
        SaveLifecycleOutcome.ContextFaulted => Lifecycle(Chapter3RuntimeRejectReason.ContextFaulted, message!),
        SaveLifecycleOutcome.FactoryFaulted => Lifecycle(Chapter3RuntimeRejectReason.RepositoryFaulted, message!),
        SaveLifecycleOutcome.LoadFaulted => Lifecycle(Chapter3RuntimeRejectReason.RepositoryFaulted, message!),
        SaveLifecycleOutcome.LoadFailed => Lifecycle(Chapter3RuntimeRejectReason.SidecarLoadFailed, message!),
        SaveLifecycleOutcome.SaveFaulted => Lifecycle(Chapter3RuntimeRejectReason.RepositoryFaulted, message!),
        SaveLifecycleOutcome.SaveFailed => Lifecycle(Chapter3RuntimeRejectReason.SidecarSaveFailed, message!),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown save lifecycle outcome."),
    };

    private Chapter3RuntimeLifecycleResult MapContextStatusForLifecycle(Release1StoryHostContextReadStatus status) =>
        Lifecycle(Map(status), "Host context was not safe for Chapter 3 activation.");

    private void ArmArthurForLifecycle()
    {
        _raid?.Suspend();
        _raidPreview?.Reset();
        // OC-130: unconditional, ahead of every phase check. S1API's own
        // load-time sweep constructs Arthur whether or not Chapter 3 is
        // active, so a reload always resolves him to parked. The arm is
        // unconditional; consuming the reconcile waits for authority.
        _ctx.ArthurPump.BeginAfterLoad();
    }

    private SaveLifecycleContextRead ReadContextForLifecycle()
    {
        var status = _ctx.Context.TryRead(out var snapshot);
        return new SaveLifecycleContextRead(status, snapshot);
    }

    private void CreateRepositoryForLifecycle(string activeSaveFolder)
    {
        if (_ctx.RepositoryFactory is not null)
            _ctx.Repository = _ctx.RepositoryFactory(activeSaveFolder) ?? throw new InvalidOperationException("Repository factory returned null.");
    }

    private SaveLifecycleBoundFolder GetBoundSaveFolderForLifecycle() =>
        _ctx.Repository is not IChapter3SaveFolderBoundRepository bound
            ? new SaveLifecycleBoundFolder(false, null)
            : new SaveLifecycleBoundFolder(true, bound.BoundSaveFolder);

    private SaveLifecycleLoadAttempt<Chapter3SaveEnvelope> LoadForLifecycle()
    {
        var load = _ctx.Repository.Load();
        if (!load.Succeeded)
        {
            _ctx.QuarantinedRawContents = load.RawContents;
            return SaveLifecycleLoadAttempt<Chapter3SaveEnvelope>.Failed(load.Message);
        }
        return SaveLifecycleLoadAttempt<Chapter3SaveEnvelope>.Loaded(load.Envelope?.State is not null, load.Envelope?.State?.PlayerId, load.Envelope);
    }

    private void ActivateForLifecycle(Chapter3SaveEnvelope? envelope, Release1StoryHostContextSnapshot snapshot)
    {
        _ctx.State = envelope?.State ?? Chapter3State.CreateInitial(snapshot.PlayerId);
    }

    private Chapter3RuntimeLifecycleResult? AfterLoadForLifecycle()
    {
        _raid?.ResetTransient();
        // An Emergency Supply window can lapse while unloaded; fail it now
        // rather than leaving a dead job on the board.
        var loadedDay = _ctx.State!.ChapterDayAt(_ctx.State!.LastProcessedElapsedDay);
        if (loadedDay.HasValue)
            _delivery.CheckEmergencyExpiryLocked(loadedDay.Value);
        _delivery.RefreshDropSubscriptionsLocked();
        // The load window is only for a build already pending at load:
        // arming it unconditionally would let a purchase made later in
        // play build on the next frame, the freeze OC-126 removes.
        _ctx.LoadBuildArmed = _warehouse.HasPendingWarehouseBuildLocked();
        return null;
    }

    private void AbortBeatingForLifecycle()
    {
        // OC-135: a reload mid-beating unlocks here, records nothing,
        // and comes back with Arthur parked or at the HQ. A reload
        // after the move but before Record replays the beating later.
        _arthur.AbortBeatingForUnloadLocked("the scene is unloading.");
    }

    private void ClearForLifecycle()
    {
        if (_raidWorld is IChapter3RaidLoadWorld loadWorld) loadWorld.OnPreLoad();
        _raidPreview?.Reset();
        _sweep.ResetTransient();
        _ctx.State = null; _ctx.SaveSnapshot = null; _ctx.QuarantinedRawContents = null; _ctx.LastOvernightLineElapsedDay = null; _ctx.Phase = Chapter3RuntimePhase.AwaitingLoad;
        _ctx.LoadBuildArmed = false;
        _ctx.UnsettledBuildNotified = false;
        _delivery.DisposeDropSubscriptionsLocked();
        _delivery.ReleaseSlotLockLocked();
        _demand.ReleaseDemandSlotLockLocked();
        _ctx.LoggedDeliveryHolds.ClearAll();
        _ctx.PendingRemoval = null;
        _ctx.DemandStationBaseline = null;
        _ctx.DemandLockerNameReceiptLogged = false;
        _demand.ResetReceiptDelayLocked();
        _ctx.DemandHoldKey = null;
        _ctx.ForceNextEmergencyOffer = false;
        _ctx.Storage.ClearQaForLoad();
        _ctx.Cash.ClearQaForLoad();
        _ctx.Shutdown.ClearQaForLoad();
        _ctx.VisitProgress = null;
        _ctx.LastVisitTickAt = float.NegativeInfinity;
        _ctx.LastVisitStartFailure = null;
    }

    private SaveLifecycleSaveAttempt SaveUpdateForLifecycle()
    {
        var result = _ctx.Repository.Update(_ctx.SaveSnapshot);
        return result.Succeeded
            ? SaveLifecycleSaveAttempt.Ok()
            : SaveLifecycleSaveAttempt.Failed(result.Message);
    }

    public void OnDayPassed()
    {
        lock (_gate)
        {
            if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return;
            if (!_ctx.Clock.TryReadElapsedDays(out var elapsedDays))
            {
                OrganizedCrimeLog.Warning("Chapter 3 day tick skipped: the host clock was unreadable.");
                return;
            }
            // A fulfilled delivery completes before the tick can fail it at
            // its deadline; the refresh drops the binding when the tick
            // failed the job instead. A fulfilled recovery handoff completes
            // the same way, and a fulfilled emergency delivery completes
            // before its window can fail it.
            var chapterDay = _ctx.State.ChapterDayAt(elapsedDays);
            if (chapterDay.HasValue)
            {
                _delivery.CheckDeadDropDeliveryLocked(chapterDay.Value);
                _delivery.CheckRecoveryLocked(chapterDay.Value);
                _ctx.Storage.Poll(chapterDay.Value);
                _ctx.Cash.Poll(chapterDay.Value);
                _ctx.Shutdown.Poll(chapterDay.Value);
                _delivery.CheckEmergencyExpiryLocked(chapterDay.Value);
            }
            ProcessTickLocked(elapsedDays, ReadGate());
            _delivery.RefreshDropSubscriptionsLocked();
        }
    }

    public void OnWeekPassed()
    {
        lock (_gate)
        {
            if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return;
            if (!_ctx.Clock.TryReadElapsedDays(out var elapsedDays))
            {
                OrganizedCrimeLog.Warning("Chapter 3 week tick skipped: the host clock was unreadable.");
                return;
            }
            var chapterDay = _ctx.State.ChapterDayAt(elapsedDays);
            if (!chapterDay.HasValue)
                return;
            _payments.ProcessWeekPassLocked(Chapter3FeatureContext.WeekIndexAt(elapsedDays), chapterDay.Value);
        }
    }

    /// <summary>
    /// One Emergency Supply expiry pass. Mod.cs calls this at 1 Hz while the
    /// game runs; the day tick and load complete call the same check, so the
    /// window closes even when the poll never runs. A lapse fails the job like
    /// any failure, through the existing failed-job path.
    /// </summary>
    public void PollEmergencyExpiry()
    {
        lock (_gate)
        {
            if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return;
            // The poll lands between ticks, like a reply-button tap, so it
            // reads the last processed chapter day.
            var chapterDay = _ctx.State.ChapterDayAt(_ctx.State.LastProcessedElapsedDay);
            if (!chapterDay.HasValue)
                return;
            _delivery.CheckEmergencyExpiryLocked(chapterDay.Value);
            _ctx.Storage.Poll(chapterDay.Value);
            _ctx.Cash.Poll(chapterDay.Value);
            _ctx.Shutdown.Poll(chapterDay.Value);
        }
    }

    /// <summary>
    /// OC-136: one production demand pass. Mod.cs calls this at 1 Hz on
    /// the host while the game runs; the phase check inside keeps it to
    /// an active Chapter 3. Counts fresh warehouse output (which also
    /// grows the eligibility set with no demand open) and takes one
    /// family-locker hand-over step. Unwired, the pass idles.
    /// </summary>
    public void PollProductionDemand()
    {
        lock (_gate)
        {
            if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return;
            _warehouse.ReconcileWarehouseCapacityLocked();
            // The poll lands between ticks, like a reply-button tap, so it
            // reads the last processed chapter day.
            var chapterDay = _ctx.State.ChapterDayAt(_ctx.State.LastProcessedElapsedDay);
            if (!chapterDay.HasValue)
                return;
            _sweep.PollLocked();
            _ctx.OfficerRoster?.PollDependencies();
            _raidPreview?.Poll();
            _raid?.PollLocked();
            _demand.PollDemandLocked();
        }
    }

    /// <summary>
    /// OC-130: one Arthur visit-pump pass. Mod.cs calls this every
    /// update, ungated: the pump no-ops unless its load reconcile is
    /// armed or a visit is mid-flight, and neither depends on the
    /// QA keys or the Chapter 3 phase. OC-132 feeds the HQ presence
    /// here and logs attack steps verbatim: they carry their own
    /// exact sentence.
    /// </summary>
    public void PollArthurVisit()
    {
        lock (_gate)
        {
            if (_ctx.Disposed)
                return;
            if (_ctx.ArthurWorld is null || !_ctx.ArthurWorld.HasAuthority)
                return;
            _ctx.ArthurPump.HqPresence = _ctx.HqPresence;
            var step = _ctx.ArthurPump.Pump(_ctx.ArthurWorld);
            if (step is not null)
            {
                foreach (var line in step.Lines)
                    _ctx.Log(step.Outcome == Chapter3ArthurVisitOutcome.Attacked
                        ? line
                        : $"Chapter 3 Arthur visit {step.Outcome}: {line}");
                if (step.Outcome == Chapter3ArthurVisitOutcome.Parked && _ctx.ArthurPump.State == Chapter3ArthurVisitState.Parked)
                    OrganizedCrimeLog.Diagnostic("Chapter 3 Arthur is parked.", _ctx.Log);
                // OC-135: an attack starts the beating right there instead
                // of the old log-only behaviour; a running beating
                // ignores further hits.
                if (step.Outcome == Chapter3ArthurVisitOutcome.Attacked)
                    _arthur.StartAttackBeatingLocked();
            }
            _arthur.TickBeatingLocked();
            _arthur.TickArthurVisitLocked();
        }
    }

    public bool TryQaStartNow()
    {
        lock (_gate)
        {
            if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
            {
                _ctx.Log("Chapter 3 QA start refused: Chapter 3 is not active.");
                return false;
            }
            if (!_ctx.Clock.TryReadElapsedDays(out var elapsedDays))
            {
                _ctx.Log("Chapter 3 QA start refused: the host clock was unreadable.");
                return false;
            }
            ProcessTickLocked(elapsedDays, envelopeSatisfied: true);
            return true;
        }
    }

    public Chapter3QaResult QaAcceptOpenOffer() => _qaFacade.QaAcceptOpenOffer();

    public Chapter3QaResult QaFailActiveJob() => _qaFacade.QaFailActiveJob();

    public Chapter3QaResult QaPayMakeGood(int id) => _qaFacade.QaPayMakeGood(id);

    public Chapter3QaResult QaRecordWarning(int id) => _qaFacade.QaRecordWarning(id);

    public Chapter3QaResult QaSimulateWeekPass() => _qaFacade.QaSimulateWeekPass();

    public Chapter3QaResult QaDeliverActiveJob() => _qaFacade.QaDeliverActiveJob();

    public Chapter3QaResult QaOfferEmergency() => _qaFacade.QaOfferEmergency();

    public int? QaEmergencyMinutesLeft() => _qaFacade.QaEmergencyMinutesLeft();

    public Chapter3QaResult QaOfferWarehouse() => _qaFacade.QaOfferWarehouse();

    public bool QaWarehouseOwned() => _qaFacade.QaWarehouseOwned();

    public IReadOnlyList<string> QaCapacityStatus() => _qaFacade.QaCapacityStatus();

    public Chapter3QaResult QaCapacityUnlock() => _qaFacade.QaCapacityUnlock();

    public Chapter3QaResult QaCapacitySet(int tier) => _qaFacade.QaCapacitySet(tier);

    public Chapter3QaResult QaCapacityHere() => _qaFacade.QaCapacityHere();

    public IReadOnlyList<string> QaWarehouseSpawns() => _qaFacade.QaWarehouseSpawns();

    public Chapter3QaResult QaWarehouseSpawnHere(bool exterior) => _qaFacade.QaWarehouseSpawnHere(exterior);

    public Chapter3QaResult QaProtectionOffer() => _qaFacade.QaProtectionOffer();

    public Chapter3QaResult QaProtectionLapse() => _qaFacade.QaProtectionLapse();

    public Release1ChiefProtectionSnapshot? QaProtectionSnapshot() => _qaFacade.QaProtectionSnapshot();

    public IReadOnlyList<string> QaOfficersStatus() => _qaFacade.QaOfficersStatus();

    public Chapter3QaResult QaOfficersSpawn(int count) => _qaFacade.QaOfficersSpawn(count);

    public Chapter3QaResult QaOfficersClear() => _qaFacade.QaOfficersClear();

    // Drives the checkpoint service through the wired link; without it every subcommand
    // refuses.
    public IReadOnlyList<string> QaCheckpointStatus() => _qaFacade.QaCheckpointStatus();

    public Chapter3QaResult QaCheckpointSpotHere(string label) => _qaFacade.QaCheckpointSpotHere(label);

    public Chapter3QaResult QaCheckpointTest(string label) => _qaFacade.QaCheckpointTest(label);

    public Chapter3QaResult QaCheckpointClear() => _qaFacade.QaCheckpointClear();

    public Chapter3QaResult QaCheckpointPick() => _qaFacade.QaCheckpointPick();

    public Chapter3QaResult QaLockdownAge(double hours) => _qaFacade.QaLockdownAge(hours);

    public Release1ChiefForcedWantedSnapshot? QaForcedWantedSnapshot() => _qaFacade.QaForcedWantedSnapshot();

    public Chapter3QaResult QaNoticeShow() => _qaFacade.QaNoticeShow();

    public Chapter3QaResult QaNoticeHide() => _qaFacade.QaNoticeHide();

    public Chapter3QaResult QaPayCut(int weekIndex) => _qaFacade.QaPayCut(weekIndex);

    public Chapter3QaResult QaDemandNow() => _qaFacade.QaDemandNow();

    public IReadOnlyList<string> QaDemandEligible() => _qaFacade.QaDemandEligible();

    public IReadOnlyList<string> QaDemandFresh() => _qaFacade.QaDemandFresh();

    public Chapter3QaResult QaDemandFreshAdd(int units) => _qaFacade.QaDemandFreshAdd(units);

    public Chapter3QaResult QaDemandEligibleAdd(string productId) => _qaFacade.QaDemandEligibleAdd(productId);

    public Chapter3QaResult QaDemandExpire() => _qaFacade.QaDemandExpire();

    /// <summary>
    /// Nell's pending messages plus her one reply decision, or empty when
    /// Chapter 3 is not active. The projector rebuilds this on every pass; the
    /// flag carries Release 1's live decision ownership so only one chapter
    /// wants Nell's reply buttons at a time.
    /// </summary>
    public Chapter3PresentationPlan GetPresentationPlan(bool release1HasNellDecision)
    {
        lock (_gate)
        {
            if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return Chapter3PresentationPlan.Empty;
            ReportUnfrozenQuestOnceLocked(_ctx.State.JobBoard.ActiveJob);
            var owned = _ctx.WarehouseAcquisition is not null && _ctx.WarehouseAcquisition.IsOwned();
            return _ctx.Cash.Project(_ctx.Storage.Project(Chapter3PresentationPlanner.Build(_ctx.State, release1HasNellDecision, owned, ReadProtectionSnapshotLocked(), _demand.ReceiptReadyLocked())));
        }
    }

    // OC-118. Reads the protection snapshot for the quest line. An unwired or throwing provider
    // reads as no snapshot, so the quest simply carries no protection line.
    private Release1ChiefProtectionSnapshot? ReadProtectionSnapshotLocked()
    {
        var provider = _ctx.ProtectionSnapshot;
        if (provider is null) return null;
        try { return provider(); }
        catch { return null; }
    }

    /// <summary>
    /// A job with no mappable drop position (a schema 5 or older dead-drop job
    /// loaded unfrozen, or any accept that ran without a world) gets no quest;
    /// say so once per job so the missing journal entry is never a silent
    /// mystery.
    /// </summary>
    private void ReportUnfrozenQuestOnceLocked(Chapter3ActiveJob? job)
    {
        if (job is null)
            return;
        var mappable = job.JobType switch
        {
            Chapter3JobType.PackageRecovery => Chapter3QuestCopy.HasMappablePosition(job.Recovery),
            Chapter3JobType.DeadDrop => Chapter3QuestCopy.HasMappablePosition(job.Delivery),
            Chapter3JobType.EmergencySupply => Chapter3QuestCopy.HasMappablePosition(job.Delivery),
            _ => true
        };
        if (mappable)
            return;
        if (_ctx.UnfrozenQuestLoggedJobId == job.JobId)
            return;
        _ctx.UnfrozenQuestLoggedJobId = job.JobId;
        _ctx.Log($"Chapter 3 job {job.JobId} has no frozen drop position; no quest projected.");
    }

    public Chapter3OfferResult TryAcceptOffer()
    {
        lock (_gate)
        {
            if (!_ctx.PresentationCommandReady(out var chapterDay))
                return new(false, null);
            var offer = _ctx.State!.JobBoard.OpenOffer;
            if (offer is null)
                return new(false, null);
            return _offers.AcceptOfferLocked(offer, chapterDay);
        }
    }

    private void OnDeadDropClosed(string deadDropGuid)
    {
        lock (_gate)
        {
            _delivery.OnDeadDropClosedLocked(deadDropGuid);
        }
    }

    public Chapter3DeclineResult TryDeclineOffer()
    {
        lock (_gate)
        {
            return _offers.TryDeclineOfferLocked();
        }
    }

    /// <summary>
    /// Pays a make-good from a reply-button tap: debits the wallet first, then
    /// records. A short wallet records a shortfall and keeps the debt; an
    /// unreadable wallet, a missing or paid entry, and an amount past the
    /// chapter bound record nothing. A zero-amount debt records paid without
    /// touching the wallet, so it can never wedge the reply buttons.
    /// </summary>
    public Chapter3MoneyResult TryPayMakeGood(int id)
    {
        lock (_gate)
        {
            return _payments.TryPayMakeGoodLocked(id);
        }
    }

    /// <summary>
    /// Pays a weekly cut from a reply-button tap. Same debit-then-record order
    /// as <see cref="TryPayMakeGood"/>.
    /// </summary>
    public Chapter3MoneyResult TryPayCut(int weekIndex)
    {
        lock (_gate)
        {
            return _payments.TryPayCutLocked(weekIndex);
        }
    }

    /// <summary>
    /// Buys the active job's deadline extension from a reply-button tap. Same
    /// debit-then-record order as <see cref="TryPayMakeGood"/>.
    /// </summary>
    public Chapter3MoneyResult TryBuyTime()
    {
        lock (_gate)
        {
            return _payments.TryBuyTimeLocked();
        }
    }

    /// <summary>
    /// Buys the Fish Warehouse from a reply-button tap, in strict order: the
    /// warehouse must be acquirable (or already owned, which the purchase then
    /// adopts without charging); the wallet must cover the price; the debit
    /// lands; the purchase is recorded. The tap never spawns: the build runs
    /// behind the next sleep or load screen via
    /// <see cref="PollPendingWarehouseBuild"/>. A short wallet sends Nell's
    /// short line and keeps the button. A failed build after the debit refunds
    /// through <see cref="Chapter3Wallet.TryCredit(int, int)"/> with delta
    /// verification, logs a warning, reopens the offer, and sends Nell's
    /// fell-through line instead.
    /// </summary>
    public Chapter3MoneyResult TryBuyWarehouse()
    {
        lock (_gate)
        {
            return _warehouse.TryBuyWarehouseLocked();
        }
    }

    /// <summary>
    /// OC-117: buys the open warehouse expansion tier from a reply-button
    /// tap, in the purchase's strict order: a tier must be openly offered
    /// (derived from the story purchase, the current tier, and the
    /// lifetime completed-demand count); an open make-good refuses
    /// (NotPayable); the warehouse must be owned and attached; the debit
    /// lands through the bounded form; the tier is recorded; the raise
    /// applies live. A short wallet sends Nell's short line and keeps the
    /// button. A raise that cannot apply (a detached race) still records:
    /// the tier applies at the next attach.
    /// </summary>
    public Chapter3MoneyResult TryBuyWarehouseCapacity()
    {
        lock (_gate)
        {
            return _warehouse.TryBuyWarehouseCapacityLocked();
        }
    }

    /// <summary>
    /// OC-117: the tier capacity the warehouse attach reads through the
    /// provider seam Mod.cs wires. Tier 1 whenever Chapter 3 is not
    /// Active, so any owned warehouse holds 12.
    /// </summary>
    public int ReadWarehouseTierCapacity()
    {
        lock (_gate)
        {
            return _warehouse.ReadWarehouseTierCapacityLocked();
        }
    }

    /// <summary>
    /// A bought warehouse that the world does not own yet. Derived, not
    /// saved: a crash or a lost sidecar between purchase and build still
    /// needs the build, and a v9 save that says purchased but not owned
    /// becomes pending automatically, with no migration. Not-owned means
    /// known-unowned: while warehouse persistence is unsettled the build
    /// waits instead of building over an unknown warehouse.
    /// </summary>
    public bool HasPendingWarehouseBuild
    {
        get
        {
            lock (_gate)
            {
                return _warehouse.ReadHasPendingWarehouseBuildLocked();
            }
        }
    }

    /// <summary>
    /// Builds a pending warehouse behind cover, from the sleep poll or the
    /// load poll. False with no pending build; a failed build refunds,
    /// reopens the offer, and counts a fell-through. The build places no
    /// employee locker: the player places their own, as in any native property.
    /// </summary>
    public bool TryBuildPendingWarehouse()
    {
        lock (_gate)
        {
            if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return false;
            return _warehouse.TryBuildPendingWarehouseLocked();
        }
    }

    /// <summary>
    /// Builds a pending warehouse behind cover. The sleep poll passes true
    /// while the sleep events run and always builds. The load window fires
    /// at most once, on the first poll after the load: a build pending then
    /// runs if the warehouse is acquirable, otherwise the window disarms
    /// and the build waits for the next sleep. Anything bought after the
    /// load never sees an armed window, so it never builds mid-play.
    /// </summary>
    public void PollPendingWarehouseBuild(Func<bool> sleepEventsRunning)
    {
        lock (_gate)
        {
            _warehouse.PollPendingWarehouseBuildLocked(sleepEventsRunning);
        }
    }

    /// <summary>
    /// The sleep owns the pending build from here: a load window that never
    /// built (the warehouse stayed unacquirable all through it) must not
    /// fire later mid-play.
    /// </summary>
    public void NotifySleepStarted()
    {
        lock (_gate)
        {
            _warehouse.NotifySleepStartedLocked();
        }
    }

    /// <summary>
    /// OC-141. The daily checkpoint target the D3b pick wrote: the winning
    /// property code plus the elapsed day it was picked. (null, null) until the
    /// first pick; a null code with a day means the pick ran and found no owned
    /// property. Fails closed when Chapter 3 is not active.
    /// </summary>
    public Chapter3CheckpointTarget ReadCheckpointTarget()
    {
        lock (_gate)
        {
            if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return new Chapter3CheckpointTarget(null, null);
            return new Chapter3CheckpointTarget(_ctx.State.CheckpointTargetCode, _ctx.State.CheckpointTargetDay);
        }
    }

    /// <summary>
    /// OC-141. Records the daily checkpoint target pick. True when recorded;
    /// false when Chapter 3 is not active. Bumps the revision like every other
    /// state write. A null code with a day is a valid no-property pick.
    /// </summary>
    public bool TryWriteCheckpointTarget(Chapter3CheckpointTarget target)
    {
        lock (_gate)
        {
            if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return false;
            if (target is null) return false;
            _ctx.State = _ctx.State with
            {
                CheckpointTargetCode = target.PropertyCode,
                CheckpointTargetDay = target.PickDay,
                Revision = _ctx.State.Revision + 1
            };
            return true;
        }
    }
    /// <summary>
    /// Records that a Chapter 3 message reached Nell's conversation once. True
    /// when recorded or already present; false on a foreign or malformed id,
    /// or when Chapter 3 is not active. Mirrors
    /// <see cref="Release1StoryRuntimeService.TryRecordPresentationReceipt"/>.
    /// </summary>
    public bool TryRecordPresentationReceipt(string correlationId)
    {
        lock (_gate)
        {
            if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return false;
            if (!Chapter3PresentationReceipt.TryParse(correlationId, out var playerId, out _) || playerId != _ctx.State.PlayerId)
                return false;
            if (_ctx.State.PresentationReceipts.Any(receipt => receipt.CorrelationId == correlationId))
                return true;
            var receipt = new Chapter3PresentationReceipt(correlationId, _ctx.State.Revision + 1);
            _ctx.State = _ctx.State with
            {
                PresentationReceipts = _ctx.State.PresentationReceipts.Append(receipt).ToArray(),
                Revision = _ctx.State.Revision + 1
            };
            if (_ctx.State.Raid is { } raid && correlationId == raid.NoticeCorrelation(_ctx.State.PlayerId))
                _ctx.State = _ctx.State with { Raid = raid with { NoticeReceipt = correlationId } };
            return true;
        }
    }

    /// The overnight line for this sleep, or null when no line applies. Serves at
    /// most one line per elapsed day: a second sleep start for the same night gets
    /// nothing. The guard is in-memory only and resets at PreLoad.
    /// </summary>
    public string? GetOvernightLineForSleep()
    {
        lock (_gate)
        {
            if (_ctx.Disposed || _ctx.Phase != Chapter3RuntimePhase.Active || _ctx.State is null)
                return null;
            if (!_ctx.Clock.TryReadElapsedDays(out var elapsedDays))
                return null;
            if (elapsedDays == _ctx.LastOvernightLineElapsedDay)
                return null;
            var chapterDay = _ctx.State.ChapterDayAt(elapsedDays);
            if (!chapterDay.HasValue)
                return null;
            var line = Chapter3OvernightLine.Select(_ctx.State, chapterDay.Value);
            if (line is null)
                return null;
            _ctx.LastOvernightLineElapsedDay = elapsedDays;
            return line;
        }
    }

    public Chapter3QaResult QaArthurCome() => _qaFacade.QaArthurCome();

    public Chapter3QaResult QaArthurSay(string text) => _qaFacade.QaArthurSay(text);

    public Chapter3QaResult QaArthurLeave() => _qaFacade.QaArthurLeave();

    public IReadOnlyList<string> QaArthurStatus() => _qaFacade.QaArthurStatus();

    public Chapter3QaResult QaArthurHq(string mode) => _qaFacade.QaArthurHq(mode);

    public Chapter3QaResult QaArthurHqHere() => _qaFacade.QaArthurHqHere();

    public IReadOnlyList<string> QaBeatSnapshot() => _qaFacade.QaBeatSnapshot();

    public IReadOnlyList<string> QaBeatInvincible(bool on) => _qaFacade.QaBeatInvincible(on);

    public IReadOnlyList<string> QaBeatAnim(string? trigger) => _qaFacade.QaBeatAnim(trigger);

    public void Dispose()
    {
        lock (_gate)
        {
            _raid?.Suspend();
            if (_ctx.Disposed)
                return;
            _raidPreview?.Reset();
            _ctx.Disposed = true;
            _ctx.Phase = Chapter3RuntimePhase.Disposed;
            _arthur.AbortBeatingForUnloadLocked("the service is disposed.");
            _delivery.DisposeDropSubscriptionsLocked();
            _delivery.ReleaseSlotLockLocked();
            _demand.ReleaseDemandSlotLockLocked();
            _ctx.LoggedDeliveryHolds.ClearAll();
            _ctx.PendingRemoval = null;
            _ctx.DemandStationBaseline = null;
            _ctx.DemandHoldKey = null;
        }
        try { _ctx.Clock.DayPassed -= OnDayPassed; }
        catch { }
        try { _ctx.Clock.WeekPassed -= OnWeekPassed; }
        catch { }
    }

    private void ProcessTickLocked(int elapsedDays, bool envelopeSatisfied)
    {
        var activeBefore = _ctx.State!.JobBoard.ActiveJob;
        var boardBeforeTick = _ctx.State.JobBoard;
        var tick = Chapter3State.ProcessDayTick(_ctx.State!, elapsedDays, envelopeSatisfied);
        if (tick.State == _ctx.State)
            return;
        var board = _offers.FreezeFreshEmergencyOfferLocked(boardBeforeTick, tick.State.JobBoard);
        board = _offers.FreezeFreshStorageOfferLocked(boardBeforeTick, board);
        board = _offers.FreezeFreshCashOfferLocked(boardBeforeTick, board);
        board = _offers.FreezeFreshShutdownOfferLocked(boardBeforeTick, board);
        var effects = tick.Effects;
        if (!ReferenceEquals(board, tick.State.JobBoard))
        {
            var open = board.OpenOffer;
            effects = tick.Effects
                .Select(effect => effect is Chapter3OfferCreatedEffect created &&
                    open is not null && created.Offer.OfferId == open.OfferId
                    ? created with { Offer = open }
                    : effect)
                .ToArray();
        }
        _ctx.State = tick.State with { JobBoard = board };
        var (cutEffects, missedLedgerEffects) = ApplyCutsTickLocked(tick.ChapterDay);
        var ledgerEffects = new List<Chapter3MakeGoodEffect>(missedLedgerEffects);
        ledgerEffects.AddRange(_delivery.ApplyLedgerTickLocked(effects, tick.ChapterDay));
        _warehouse.UpdateWarehouseOfferLocked(tick.ChapterDay);
        _demand.ApplyDemandTickLocked(tick.ChapterDay);
        if (activeBefore?.Recovery is not null && activeBefore.JobType == Chapter3JobType.PackageRecovery &&
            effects.OfType<Chapter3JobCompletedEffect>().Any(effect => effect.JobId == activeBefore.JobId && !effect.Succeeded))
            _delivery.CleanupRecoveryLocked(activeBefore.JobId, activeBefore.Recovery);
        if (activeBefore?.StorageRun is { } storage &&
            effects.OfType<Chapter3JobCompletedEffect>().Any(effect => effect.JobId == activeBefore.JobId && !effect.Succeeded))
            _delivery.CleanupStorageLocked(activeBefore.JobId, storage.Route);
        if (tick.Started)
            _ctx.Log($"Chapter 3 started; day 0 is elapsed day {elapsedDays}");
        if (tick.ChapterDay.HasValue)
            OrganizedCrimeLog.Diagnostic($"Chapter 3 day {tick.ChapterDay.Value}", _ctx.Log);
        foreach (var effect in effects)
            _ctx.Log(Chapter3FeatureContext.FormatEffect(effect));
        foreach (var cutEffect in cutEffects)
            _ctx.Log(Chapter3FeatureContext.FormatCutEffect(cutEffect));
        foreach (var ledgerEffect in ledgerEffects)
            _ctx.Log(Chapter3FeatureContext.FormatLedgerEffect(ledgerEffect));
    }

    /// <summary>
    /// Runs the weekly cuts' day tick and opens a make-good for each cut missed
    /// today, before the ledger's own tick. The caller logs both effect lists.
    /// </summary>
    private (List<Chapter3WeeklyCutEffect> CutEffects, List<Chapter3MakeGoodEffect> LedgerEffects) ApplyCutsTickLocked(int? chapterDay)
    {
        var cutEffects = new List<Chapter3WeeklyCutEffect>();
        var ledgerEffects = new List<Chapter3MakeGoodEffect>();
        if (!chapterDay.HasValue)
            return (cutEffects, ledgerEffects);
        var ticked = Chapter3WeeklyCutTransitions.OnDayTick(_ctx.State!.Cuts, chapterDay.Value);
        _ctx.State = _ctx.State with { Cuts = ticked.Cuts };
        cutEffects.AddRange(ticked.Effects);
        var ledger = _ctx.State.Ledger;
        foreach (var missed in ticked.Effects.OfType<Chapter3WeeklyCutMissedEffect>())
        {
            var opened = Chapter3MakeGoods.OpenForMissedCut(ledger, missed.Cut.WeekIndex, missed.Cut.Amount, chapterDay.Value);
            ledger = opened.Ledger;
            ledgerEffects.AddRange(opened.Effects);
        }
        _ctx.State = _ctx.State with { Ledger = ledger };
        return (cutEffects, ledgerEffects);
    }

    private bool ReadGate()
    {
        try { return _ctx.StoryGate.IsEnvelopeSatisfied(); }
        catch (Exception ex)
        {
            OrganizedCrimeLog.Warning($"Chapter 3 envelope gate faulted; treating The Envelope as unsatisfied: {ex.Message}");
            return false;
        }
    }

    private Chapter3RuntimeLifecycleResult PreserveQuarantinedSidecar()
    {
        if (_ctx.QuarantinedRawContents is null)
            return Lifecycle(Chapter3RuntimeRejectReason.Quarantined, "Quarantined Chapter 3 has no sidecar bytes to preserve.");
        if (_ctx.Repository is not IChapter3RawContentPreservingRepository preserving)
            return Lifecycle(Chapter3RuntimeRejectReason.Quarantined, "Quarantined Chapter 3 repository cannot preserve raw bytes.");
        Chapter3StoreUpdateResult result;
        try { result = preserving.PreserveRawContents(_ctx.QuarantinedRawContents); }
        catch (Exception ex) { return Lifecycle(Chapter3RuntimeRejectReason.RepositoryFaulted, ex.Message); }
        if (!result.Succeeded)
            return Lifecycle(Chapter3RuntimeRejectReason.SidecarSaveFailed, result.Message);
        return Lifecycle(Chapter3RuntimeRejectReason.Quarantined, "Quarantined Chapter 3 sidecar bytes were preserved.");
    }

    private Chapter3RuntimeLifecycleResult Lifecycle(Chapter3RuntimeRejectReason reason, string message) =>
        new(_ctx.Phase, reason, message);

    private static bool SameSaveFolder(string bound, string active)
    {
        try { return string.Equals(Path.GetFullPath(bound), Path.GetFullPath(active), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(bound, active, StringComparison.OrdinalIgnoreCase); }
    }

    public Chapter3QaResult QaArthurVisit(string kind) => _qaFacade.QaArthurVisit(kind);

    public Chapter3QaResult QaArthurBeat() => _qaFacade.QaArthurBeat();

    private static Chapter3RuntimeRejectReason Map(Release1StoryHostContextReadStatus status) => status switch
    {
        Release1StoryHostContextReadStatus.UnsupportedMultiplayer => Chapter3RuntimeRejectReason.UnsupportedMultiplayer,
        Release1StoryHostContextReadStatus.AmbiguousIdentity => Chapter3RuntimeRejectReason.AmbiguousIdentity,
        Release1StoryHostContextReadStatus.Faulted => Chapter3RuntimeRejectReason.ContextFaulted,
        _ => Chapter3RuntimeRejectReason.ContextFaulted,
    };
}
