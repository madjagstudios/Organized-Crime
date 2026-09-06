namespace OrganizedCrime.Runtime;

/// <summary>
/// Composes the Short Notice mission service and presenter over the shared Small Courtesy world
/// boundary and the shared phone-call service. Mirrors <see cref="Release1RoomWithNoNameComposition"/>
/// exactly, including its lifecycle shape; it does not own or dispose the shared world, and it does
/// not own or dispose the shared phone-call service, both of which are constructed and disposed by
/// <see cref="Release1ProductionComposition"/> once, above every mission composition.
/// </summary>
public sealed class Release1ShortNoticeComposition : IDisposable
{
    private bool _disposed;

    public Release1ShortNoticeComposition(
        Release1StoryRuntimeService story,
        IRelease1SmallCourtesyWorld world,
        Release1PhoneCallService phone,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(phone);

        Service = new Release1ShortNoticeMissionService(story, world, phone, log);
        Presenter = new Release1ShortNoticePresenter(Service, story, log);
    }

    public Release1ShortNoticeMissionService Service { get; }
    public Release1ShortNoticePresenter Presenter { get; }

    public void OnLoadComplete()
    {
        if (_disposed) return;
        Presenter.ClearFeedback();
        Service.OnLoadComplete();
    }

    public void OnPreLoad()
    {
        if (_disposed) return;
        Presenter.ClearFeedback();
        Service.OnPreLoad();
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
    }

    public void Update()
    {
        if (_disposed) return;
        Service.Update();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Presenter.Dispose();
        Service.Dispose();
    }
}
