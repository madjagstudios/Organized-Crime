namespace OrganizedCrime.Runtime;

/// <summary>
/// Composes the Room With No Name mission service and presenter over the shared Small Courtesy
/// world boundary and the shared phone-call service. Mirrors <see cref="Release1WrongAddressComposition"/>
/// exactly, including its lifecycle shape; it does not own or dispose the shared world, and it does
/// not own or dispose the shared phone-call service, both of which are constructed and disposed by
/// <see cref="Release1ProductionComposition"/> once, above both mission compositions.
/// </summary>
public sealed class Release1RoomWithNoNameComposition : IDisposable
{
    private bool _disposed;

    public Release1RoomWithNoNameComposition(
        Release1StoryRuntimeService story,
        IRelease1SmallCourtesyWorld world,
        Release1PhoneCallService phone,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(phone);

        Service = new Release1RoomWithNoNameMissionService(story, world, phone, log);
        Presenter = new Release1RoomWithNoNamePresenter(Service, story, log);
    }

    public Release1RoomWithNoNameMissionService Service { get; }
    public Release1RoomWithNoNamePresenter Presenter { get; }

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
