using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

public sealed class Release1SmallCourtesyComposition : IDisposable
{
    private readonly IRelease1SmallCourtesyWorld _world;
    private readonly Release1StoryRuntimeService _story;
    private readonly Action<string>? _log;
    private readonly HashSet<string> _reportedAmbiguities = new(StringComparer.Ordinal);
    private bool _disposed;

    public Release1SmallCourtesyComposition(
        Release1StoryRuntimeService story,
        IRelease1SmallCourtesyWorld world,
        Action<string>? log = null)
    {
        _story = story ?? throw new ArgumentNullException(nameof(story));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
        Service = new Release1SmallCourtesyMissionService(story, world);
        Presenter = new Release1SmallCourtesyPresenter(Service, story, log);
    }

    public Release1SmallCourtesyMissionService Service { get; }
    public Release1SmallCourtesyPresenter Presenter { get; }

    public void OnLoadComplete()
    {
        if (_disposed) return;
        _reportedAmbiguities.Clear();
        Presenter.ClearFeedback();
        Service.OnLoadComplete();
        ReportAmbiguities();
    }

    public void OnPreLoad()
    {
        if (_disposed) return;
        Presenter.ClearFeedback();
        Service.OnPreLoad();
        _reportedAmbiguities.Clear();
    }

    public void OnSaveStart()
    {
        if (_disposed) return;
        Service.OnSaveStart();
    }

    public void OnSaveComplete()
    {
        if (_disposed) return;
        Service.OnSaveComplete();
        ReportAmbiguities();
    }

    public void Update()
    {
        if (_disposed) return;
        Service.Update();
        ReportAmbiguities();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Presenter.Dispose();
        Service.Dispose();
        if (_world is IDisposable disposable) disposable.Dispose();
        _reportedAmbiguities.Clear();
    }

    private void ReportAmbiguities()
    {
        var state = _story.State;
        if (state is null) return;
        foreach (var effect in state.NativeEffects.Where(effect =>
                     effect.MissionKey == Release1MissionCatalog.SmallCourtesy && effect.ExecutionBlocked))
        {
            if (_reportedAmbiguities.Add(effect.EffectId))
                _log?.Invoke($"Small Courtesy native effect stopped as ambiguous: {effect.EffectId}; no automatic retry will occur.");
        }
    }
}
