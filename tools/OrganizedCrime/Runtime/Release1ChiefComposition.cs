using OrganizedCrime.Runtime.Release1.World;
using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// Composes Chief Campbell's convergence service and, in Native mode, his own projector over his own
/// plan builder (<see cref="Release1ChiefPresentation.BuildPlan"/>), mirroring
/// <see cref="Release1TheEnvelopeComposition"/>'s lifecycle shape exactly. Owns neither the shared
/// story runtime nor the shared Small Courtesy world boundary, both constructed and disposed once,
/// above every composition, by the mod shell; owns neither the shared Local Pressure runtime, whose
/// reader, wiper and epoch delegates are handed in already bound. In ImguiFallback mode this
/// constructs no projector and logs <see cref="Release1ChiefCampbellCopy.FallbackLogText"/> exactly
/// once per load, the same OC-52 rule every fallback-less mission in
/// <see cref="Release1ProductionComposition"/> already follows; the Chief is never offered through a
/// GUI, so there is no decision prompt host to build either way.
/// </summary>
public sealed class Release1ChiefComposition : IDisposable
{
    private readonly Release1PresentationMode _mode;
    private readonly Action<string> _log;
    private bool _disposed;

    public Release1ChiefComposition(
        Release1StoryRuntimeService story,
        IChiefWorld world,
        Release1ChiefTierObserver observer,
        Release1ChiefLedgerReader readLedger,
        Release1ChiefLedgerWiper wipeLedger,
        Release1ChiefEpochReader epochs,
        Release1PresentationMode mode = Release1PresentationMode.ImguiFallback,
        IRelease1NativePresentation? native = null,
        Action<string>? log = null,
        Func<int>? chapter3SuccessfulJobs = null,
        IRelease1ProtectionCheckpointWorld? checkpointWorld = null,
        Func<int, int?>? playerDay = null,
        Func<bool>? isAuthoritativeHost = null,
        IChapter3PlayerExposureReader? exposureReader = null,
        IChapter3CurfewReader? curfewReader = null,
        IChapter3WantedWriter? wantedWriter = null,
        IChapter3ForcedWantedDispatch? forcedWantedDispatch = null,
        IChapter3PursuitHold? pursuitHold = null)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(readLedger);
        ArgumentNullException.ThrowIfNull(wipeLedger);
        ArgumentNullException.ThrowIfNull(epochs);
        if (mode == Release1PresentationMode.Native && native is null)
            throw new ArgumentException("A native presentation implementation is required in Native mode.", nameof(native));

        SweepPresentation = mode == Release1PresentationMode.Native ? native : null;
        _mode = mode;
        _log = log ?? (_ => { });

        Service = new Release1ChiefService(story, world, observer, readLedger, wipeLedger, epochs, LocalPressureProfile.Moderate, chapter3SuccessfulJobs, checkpointWorld,
            isAuthoritativeHost, exposureReader, curfewReader, wantedWriter, forcedWantedDispatch, pursuitHold);

        if (mode == Release1PresentationMode.Native)
        {
            Projector = new Release1PresentationProjector(
                story,
                // OC-118: today drives Buy versus Extend and the banking-cap hide; playerDay shows
                // the cover end day in the chapter days Nell and the quest line use.
                () => Release1ChiefPresentation.BuildPlan(story.State, Service.ReadToday(), playerDay),
                native!,
                new ChiefCommandSink(Service),
                log,
                // The Chief adds no quest and must never sweep the six shipped mission quests that
                // live in the same global S1API quest registry; see the projector's own doc comment.
                reconcileQuests: false);
        }
    }

    internal IRelease1NativePresentation? SweepPresentation { get; }
    public Release1ChiefService Service { get; }
    public Release1PresentationProjector? Projector { get; }

    public void OnLoadComplete()
    {
        if (_disposed) return;
        Service.OnLoadComplete();
        if (_mode == Release1PresentationMode.ImguiFallback) _log(Release1ChiefCampbellCopy.FallbackLogText);
        if (Projector is not null) Projector.OnLoadComplete();
    }

    public void OnPreLoad()
    {
        if (_disposed) return;
        if (Projector is not null) Projector.OnPreLoad();
        Service.OnPreLoad();
    }

    public void OnSaveStart()
    {
        if (_disposed) return;
        Service.OnSaveStart();
        if (Projector is not null) Projector.OnSaveStart();
    }

    public void OnSaveComplete()
    {
        if (_disposed) return;
        Service.OnSaveComplete();
        if (Projector is not null) Projector.OnSaveComplete();
    }

    public void Update()
    {
        if (_disposed) return;
        Service.Update();
        Projector?.Reconcile();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Projector?.Dispose();
        Service.Dispose();
    }

    /// <summary>
    /// Routes the Chief's own two decision commands to his own service, and refuses everything else.
    /// A parallel, narrower sibling of <see cref="Release1PresentationCommandRouter"/>, which itself
    /// refuses both <see cref="Release1PresentationCommand.ChiefCampbellPay"/> and
    /// <see cref="Release1PresentationCommand.ChiefCampbellDecline"/> (spec decision 6): a Chief
    /// command can never reach a mission presenter, and a mission command can never reach the Chief.
    /// </summary>
    private sealed class ChiefCommandSink : IRelease1PresentationCommandSink
    {
        private readonly Release1ChiefService _service;

        public ChiefCommandSink(Release1ChiefService service) => _service = service;

        public bool TryInvoke(Release1PresentationCommand command) => command switch
        {
            Release1PresentationCommand.ChiefCampbellPay => _service.TryPay(),
            Release1PresentationCommand.ChiefCampbellDecline => _service.TryDecline(),
            Release1PresentationCommand.ChiefCampbellBuyProtection => _service.TryBuyProtection(),
            _ => false
        };
    }
}
