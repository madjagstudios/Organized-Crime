// OC-151: the lifecycle fan-out. Each game phase runs an explicit, ordered step table
// (built from the composition root); the tables pin today's per-phase participant order
// and every timing name. SaveComplete is host-gated as a whole, LoadComplete gates its
// HQ step on the prepare result and its warehouse step on host, PreLoad begins the
// storage cycle untimed first, and the OC-52 staging tail runs untimed after the
// load-complete block. Update keeps today's participants, timers, host section and
// QA-last order verbatim. A throwing step aborts the rest of its phase (after the
// timing receipt reports it), exactly like the Mod.cs handlers it replaces.
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;

namespace OrganizedCrime.Runtime.Composition;

internal enum LifecycleStepKind
{
    Timed,
    Untimed,
}

internal sealed record LifecycleStep(string Name, Action Run, LifecycleStepKind Kind = LifecycleStepKind.Timed);

internal sealed record LifecycleGateStep(string Name, Func<bool> Run);

internal sealed class LifecycleSlots
{
    public Action? HqUpdate { get; set; }
    public Action? ProductionUpdate { get; set; }
    public Action? ProjectorReconcile { get; set; }
    public Action? ArthurPoll { get; set; }
    public Action? EmergencyPoll { get; set; }
    public Action? ChiefUpdate { get; set; }
    public Action? ReadinessPump { get; set; }
    public Action? WarehouseUpdate { get; set; }
    public Action? ReplayUpdate { get; set; }
    public Action? WarehouseBuildPoll { get; set; }
    public Action? DemandPoll { get; set; }
    public Action? PatrolPoll { get; set; }
    public Action? RosterUpdate { get; set; }
    public Action? CheckpointsUpdate { get; set; }
    public Action? EnsureUi { get; set; }
    public Action? QaPoll { get; set; }
    public Action? HqGui { get; set; }
    public Action? ProductionGui { get; set; }
}

internal sealed class LifecycleTables
{
    public IReadOnlyList<LifecycleStep> SaveComplete { get; init; } = Array.Empty<LifecycleStep>();
    public IReadOnlyList<LifecycleStep> SaveStart { get; init; } = Array.Empty<LifecycleStep>();
    public IReadOnlyList<LifecycleStep> PreLoad { get; init; } = Array.Empty<LifecycleStep>();
    public LifecycleGateStep? LoadCompletePrepare { get; init; }
    public IReadOnlyList<LifecycleStep> LoadCompleteMain { get; init; } = Array.Empty<LifecycleStep>();
    public LifecycleStep? LoadCompleteHq { get; init; }
    public LifecycleStep? LoadCompleteFish { get; init; }
    public Action? LoadCompleteTail { get; init; }
    public IReadOnlyList<LifecycleStep> PreSceneChange { get; init; } = Array.Empty<LifecycleStep>();
    public LifecycleSlots Update { get; init; } = new();

    public static LifecycleTables ForRoot(OcCompositionRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var engine = root.Engine;
        var log = root.Log;
        var warn = root.Warn;
        return new LifecycleTables
        {
            SaveComplete = new[]
            {
                new LifecycleStep("save-complete/story", () => Release1StoryLifecycleLogging.LogIfRejected("OnSaveComplete", root.Story?.OnSaveComplete())),
                new LifecycleStep("save-complete/chapter3", () => Chapter3LifecycleLogging.LogIfRejected("OnSaveComplete", root.Chapter3?.OnSaveComplete())),
                new LifecycleStep("save-complete/chapter3-projector", () => root.Chapter3Projector?.OnSaveComplete()),
                new LifecycleStep("save-complete/officers", () => root.OfficerRoster?.OnSaveComplete()),
                new LifecycleStep("save-complete/checkpoints", () => { root.Checkpoints?.OnSaveComplete(); root.Patrols?.OnSaveComplete(); }),
                new LifecycleStep("save-complete/production", () => root.Production?.OnSaveComplete()),
                new LifecycleStep("save-complete/chief", () => root.Chief?.OnSaveComplete()),
                new LifecycleStep("save-complete/hq", () => root.Hq?.OnSaveComplete()),
                new LifecycleStep("save-complete/fish-warehouse", () => root.WarehousePersistence?.HandleSaveComplete()),
            },
            SaveStart = new[]
            {
                new LifecycleStep("save-start/story", () => Release1StoryLifecycleLogging.LogIfRejected("OnSaveStart", root.Story?.OnSaveStart())),
                new LifecycleStep("save-start/chapter3", () => Chapter3LifecycleLogging.LogIfRejected("OnSaveStart", root.Chapter3?.OnSaveStart())),
                new LifecycleStep("save-start/chapter3-projector", () => root.Chapter3Projector?.OnSaveStart()),
                new LifecycleStep("save-start/officers", () => { root.Patrols?.OnSaveStart(); root.OfficerRoster?.OnSaveStart(); }),
                new LifecycleStep("save-start/checkpoints", () => root.Checkpoints?.OnSaveStart()),
                new LifecycleStep("save-start/production", () => root.Production?.OnSaveStart()),
                new LifecycleStep("save-start/chief", () => root.Chief?.OnSaveStart()),
                new LifecycleStep("save-start/hq", () => root.Hq?.OnSaveStart()),
            },
            PreLoad = new[]
            {
                new LifecycleStep("pre-load/hq-storage-cycle", engine.BeginHqLoadCycle, LifecycleStepKind.Untimed),
                new LifecycleStep("pre-load/hq", () => root.Hq?.OnPreLoad()),
                new LifecycleStep("pre-load/production", () => root.Production?.OnPreLoad()),
                new LifecycleStep("pre-load/chief", () => root.Chief?.OnPreLoad()),
                new LifecycleStep("pre-load/story", () => Release1StoryLifecycleLogging.LogIfRejected("OnPreLoad", root.Story?.OnPreLoad())),
                new LifecycleStep("pre-load/chapter3", () => Chapter3LifecycleLogging.LogIfRejected("OnPreLoad", root.Chapter3?.OnPreLoad())),
                new LifecycleStep("pre-load/chapter3-projector", () => root.Chapter3Projector?.OnPreLoad()),
                new LifecycleStep("pre-load/officers", () => { root.Patrols?.OnPreLoad(); root.OfficerRoster?.OnPreLoad(); }),
                new LifecycleStep("pre-load/checkpoints", () => root.Checkpoints?.OnPreLoad()),
                // The warehouse load reset is deliberately not host-gated: a rejoining client
                // must forget the previous generation's replay schedule even though it never
                // persists one.
                new LifecycleStep("pre-load/fish-warehouse", () => root.WarehouseReplay?.PrepareForLoad()),
            },
            LoadCompletePrepare = new LifecycleGateStep("load-complete/hq-prepare", () => root.Hq?.PrepareLoadComplete() ?? true),
            LoadCompleteMain = new[]
            {
                new LifecycleStep("load-complete/story", () => Release1StoryLifecycleLogging.LogIfRejected("OnLoadComplete", root.Story?.OnLoadComplete())),
                new LifecycleStep("load-complete/chapter3", () => Chapter3LifecycleLogging.LogIfRejected("OnLoadComplete", root.Chapter3?.OnLoadComplete())),
                new LifecycleStep("load-complete/chapter3-projector", () => root.Chapter3Projector?.OnLoadComplete()),
                new LifecycleStep("load-complete/officers", () => root.OfficerRoster?.OnLoadComplete()),
                new LifecycleStep("load-complete/checkpoints", () => { root.Checkpoints?.OnLoadComplete(); root.Patrols?.OnLoadComplete(); }),
                new LifecycleStep("load-complete/production", () => root.Production?.OnLoadComplete()),
                new LifecycleStep("load-complete/chief", () => root.Chief?.OnLoadComplete()),
                new LifecycleStep("load-complete/hq-storage", engine.CompleteHqLoad),
            },
            LoadCompleteHq = new LifecycleStep("load-complete/hq", () => root.Hq?.OnLoadComplete()),
            LoadCompleteFish = new LifecycleStep("load-complete/fish-warehouse", () => root.WarehouseReplay?.HandleLoadComplete()),
            LoadCompleteTail = () =>
            {
                var world = root.CourtesyWorld;
                if (root.OwnerQaKeysEnabled && world is not null)
                {
                    try
                    {
                        // OC-52 staging proof at load: dump the staged dead drops so a glance at
                        // the log shows whether the QA handoff survived the vanilla load.
                        var result = Release1WrongAddressStagingHarness.TryDump(world);
                        foreach (var line in result.Lines)
                        {
                            if (line.StartsWith("dead drops read: ", StringComparison.Ordinal) ||
                                line.Contains(" quantity ", StringComparison.Ordinal))
                                OrganizedCrimeLog.Diagnostic($"[Organized Crime] OC-52 staging proof at load: {line}", log);
                        }
                    }
                    catch (Exception ex)
                    {
                        warn($"[Organized Crime] OC-52 staging proof at load failed: {ex.Message}");
                    }
                }
            },
            PreSceneChange = new[]
            {
                new LifecycleStep("pre-scene-change/production", () => root.Production?.OnPreSceneChange()),
                new LifecycleStep("pre-scene-change/hq", () => root.Hq?.OnPreSceneChange()),
                new LifecycleStep("pre-scene-change/chapter3-projector", () => { root.Chapter3?.SuspendSweeps(); root.Patrols?.OnPreLoad(); root.Chapter3Projector?.OnPreSceneChange(); }),
            },
            Update = new LifecycleSlots
            {
                HqUpdate = () => root.Hq?.Update(),
                ProductionUpdate = () => root.Production?.Update(),
                ProjectorReconcile = () => root.Chapter3Projector?.Reconcile(),
                ArthurPoll = () => root.Chapter3?.PollArthurVisit(),
                EmergencyPoll = () => root.Chapter3?.PollEmergencyExpiry(),
                ChiefUpdate = () => root.Chief?.Update(),
                ReadinessPump = () => root.LocalPressure?.Service.PumpReadiness(),
                WarehouseUpdate = () => root.WarehouseRuntime?.Update(),
                ReplayUpdate = () => root.WarehouseReplay?.Update(),
                WarehouseBuildPoll = () => root.Chapter3?.PollPendingWarehouseBuild(() => engine.ReadSleepBuildPhase()),
                DemandPoll = () => root.Chapter3?.PollProductionDemand(),
                PatrolPoll = () => root.Patrols?.Poll(),
                RosterUpdate = () => root.OfficerRoster?.Update(),
                CheckpointsUpdate = () => root.Checkpoints?.Update(),
                EnsureUi = () => root.PropertyHost?.EnsureManagementWorldspaceUiContainerForOpenCanvas(),
                QaPoll = () => root.QaKeys.Poll(),
                HqGui = () => root.Hq?.OnGUI(),
                ProductionGui = () => root.Production?.OnGUI(),
            },
        };
    }
}

internal sealed class LifecycleFanout
{
    private readonly LifecycleTables _tables;
    private readonly IOcEngineAdapters _engine;
    private readonly Func<OrganizedCrimeTimingReceipts> _timing;
    private readonly OcCompositionLifetime? _lifetime;
    private float _chapter3EmergencyPollSeconds;
    private float _chapter3DemandPollSeconds;

    public LifecycleFanout(OcCompositionRoot root)
        : this(LifecycleTables.ForRoot(root), root.Engine, () => root.Timing, root.Lifetime)
    {
    }

    public LifecycleFanout(LifecycleTables tables, IOcEngineAdapters engine, Func<OrganizedCrimeTimingReceipts> timing, OcCompositionLifetime? lifetime = null)
    {
        _tables = tables ?? throw new ArgumentNullException(nameof(tables));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _timing = timing ?? throw new ArgumentNullException(nameof(timing));
        _lifetime = lifetime;
    }

    public void HandleSaveComplete()
    {
        var timing = _timing();
        timing.Measure("save-complete", () => FishWarehousePersistenceReplayService.RunIfAuthoritativeHost(_engine.IsHost(), () =>
        {
            foreach (var step in _tables.SaveComplete)
                timing.Measure(step.Name, step.Run);
        }));
    }

    public void HandleSaveStart()
    {
        OrganizedCrimeLog.FlushWarningSummaries();
        var timing = _timing();
        timing.Measure("save-start", () =>
        {
            foreach (var step in _tables.SaveStart)
                timing.Measure(step.Name, step.Run);
        });
    }

    public void HandlePreLoad()
    {
        var timing = _timing();
        timing.Measure("pre-load", () =>
        {
            foreach (var step in _tables.PreLoad)
            {
                if (step.Kind == LifecycleStepKind.Untimed)
                    step.Run();
                else
                    timing.Measure(step.Name, step.Run);
            }
        });
    }

    public void HandleLoadComplete()
    {
        var timing = _timing();
        timing.Measure("load-complete", () =>
        {
            var hqLoadPrepared = true;
            var prepare = _tables.LoadCompletePrepare;
            if (prepare is not null)
                timing.Measure(prepare.Name, () => { hqLoadPrepared = prepare.Run(); });
            foreach (var step in _tables.LoadCompleteMain)
                timing.Measure(step.Name, step.Run);
            var hq = _tables.LoadCompleteHq;
            if (hqLoadPrepared && hq is not null)
                timing.Measure(hq.Name, hq.Run);
            var fish = _tables.LoadCompleteFish;
            if (fish is not null)
                FishWarehousePersistenceReplayService.RunIfAuthoritativeHost(_engine.IsHost(), () => timing.Measure(fish.Name, fish.Run));
        });
        _tables.LoadCompleteTail?.Invoke();
    }

    public void HandlePreSceneChange()
    {
        var timing = _timing();
        timing.Measure("pre-scene-change", () =>
        {
            foreach (var step in _tables.PreSceneChange)
                timing.Measure(step.Name, step.Run);
        });
    }

    public void Update()
    {
        var slots = _tables.Update;
        slots.HqUpdate?.Invoke();
        // Chapter 3 reconciles after Release 1 every frame: Nell's decision toasts must
        // defer to vanilla or Release 1 ownership decided this same frame.
        slots.ProductionUpdate?.Invoke();
        slots.ProjectorReconcile?.Invoke();
        slots.ArthurPoll?.Invoke();
        if (_lifetime is not null)
        {
            _lifetime.EmergencyPollSeconds += _engine.DeltaTime();
            if (_lifetime.EmergencyPollSeconds >= 1f)
            {
                _lifetime.EmergencyPollSeconds = 0f;
                slots.EmergencyPoll?.Invoke();
            }
        }
        else
        {
            _chapter3EmergencyPollSeconds += _engine.DeltaTime();
            if (_chapter3EmergencyPollSeconds >= 1f)
            {
                _chapter3EmergencyPollSeconds = 0f;
                slots.EmergencyPoll?.Invoke();
            }
        }
        slots.ChiefUpdate?.Invoke();
        slots.ReadinessPump?.Invoke();
        if (_engine.IsHost())
        {
            slots.WarehouseUpdate?.Invoke();
            slots.ReplayUpdate?.Invoke();
            slots.WarehouseBuildPoll?.Invoke();
            if (_lifetime is not null)
            {
                _lifetime.DemandPollSeconds += _engine.DeltaTime();
                if (_lifetime.DemandPollSeconds >= 1f)
                {
                    _lifetime.DemandPollSeconds = 0f;
                    slots.DemandPoll?.Invoke();
                    slots.PatrolPoll?.Invoke();
                }
            }
            else
            {
                _chapter3DemandPollSeconds += _engine.DeltaTime();
                if (_chapter3DemandPollSeconds >= 1f)
                {
                    _chapter3DemandPollSeconds = 0f;
                    slots.DemandPoll?.Invoke();
                    slots.PatrolPoll?.Invoke();
                }
            }
            // The roster's update binds checkpoint clones, so it runs before the
            // checkpoint service's update every host pass.
            slots.RosterUpdate?.Invoke();
            slots.CheckpointsUpdate?.Invoke();
        }
        slots.EnsureUi?.Invoke();
        slots.QaPoll?.Invoke();
    }

    public void OnGUI()
    {
        _tables.Update.HqGui?.Invoke();
        _tables.Update.ProductionGui?.Invoke();
    }
}
