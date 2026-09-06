using System.Reflection;
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class NativeLawResponseWiringTests
{
    private const string CanonicalPlayerId = "76561190000000001";

    [Fact]
    public void Production_composition_uses_one_shared_host_adapter_for_runtime_and_native_dispatch()
    {
        var host = new FakeHostAdapter();
        using var composition = CreateComposition(host);

        var controllerAdapter = ReadPrivateField<INativeLawResponseAdapter>(composition.Controller, "_adapter");
        var nativeDispatchHost = ReadPrivateField<ILocalPressureRuntimeHostAdapter>(controllerAdapter, "_runtimeHost");
        var serviceSink = ReadPrivateField<ILocalPressureTierTransitionSink?>(composition.LocalPressureRuntime.Service, "_tierTransitionSink");

        Assert.IsType<NativePoliceDispatchAdapter>(controllerAdapter);
        Assert.Same(host, composition.LocalPressureRuntime.Adapter);
        Assert.Same(host, nativeDispatchHost);
        Assert.Same(composition.LocalPressureRuntime.Adapter, nativeDispatchHost);
        Assert.Same(composition.TierTransitionSink, serviceSink);
    }

    [Fact]
    public void Production_composition_fans_tier_transitions_out_to_the_controller_and_a_fresh_chief_observer()
    {
        var host = new FakeHostAdapter();
        using var composition = CreateComposition(host);

        Assert.IsType<LocalPressureTierTransitionFanOut>(composition.TierTransitionSink);
        Assert.NotNull(composition.ChiefObserver);
        Assert.Equal(0, composition.ChiefObserver.QueueDepth);
    }

    [Fact]
    public void Production_composition_constructs_controller_before_runtime_and_disposes_runtime_before_controller()
    {
        var order = new List<string>();

        var host = new FakeHostAdapter();
        using var composition = CreateComposition(
            host,
            controllerFactory: (gate, adapter, profile, log) =>
            {
                order.Add("controller");
                return new NativeLawResponseController(gate, adapter, profile, log);
            },
            runtimeFactory: (adapter, repositoryFactory, log, sink) =>
            {
                order.Add("runtime");
                return new LocalPressureRuntimeComposition(adapter, repositoryFactory, log, tierTransitionSink: sink);
            },
            runtimeLifetimeFactory: runtime => new CallbackDisposable(() =>
            {
                order.Add("runtime-dispose");
                runtime.Dispose();
            }),
            controllerLifetimeFactory: controller => new CallbackDisposable(() =>
            {
                order.Add("controller-dispose");
                controller.Dispose();
            }));

        Assert.Equal(new[] { "controller", "runtime" }, order);

        composition.Dispose();

        Assert.Equal(
            new[] { "controller", "runtime", "runtime-dispose", "controller-dispose" },
            order);
    }

    [Fact]
    public void Production_composition_disposal_leaves_controller_disposed_and_runtime_unable_to_publish()
    {
        var host = new FakeHostAdapter();
        var composition = CreateComposition(host);

        Activate(composition.LocalPressureRuntime.Service);
        var first = ApplyCustody(composition.LocalPressureRuntime.Service, "2");

        Assert.True(first.Accepted);
        var resultBeforeDispose = Assert.IsType<NativeLawResponseResult>(composition.Controller.LastResult);

        composition.Dispose();

        var publishAfterDispose = Record.Exception(() => composition.Controller.Publish(
            new LocalPressureTierTransitionNotification(
                composition.LocalPressureRuntime.Service.SessionEpoch,
                composition.LocalPressureRuntime.Service.LoadEpoch,
                CanonicalPlayerId,
                new object(),
                CorrelationId(composition.LocalPressureRuntime.Service, "3"),
                LocalPressureTier.Noticed,
                LocalPressureTier.Watched,
                "north",
                "safehouse")));

        var applyAfterDispose = composition.LocalPressureRuntime.Service.TryApplyCustodyEvidence(
            Evidence(composition.LocalPressureRuntime.Service, "4"),
            composition.LocalPressureRuntime.Service.SessionEpoch,
            composition.LocalPressureRuntime.Service.LoadEpoch);

        Assert.Null(publishAfterDispose);
        Assert.Same(resultBeforeDispose, composition.Controller.LastResult);
        Assert.False(applyAfterDispose.Accepted);
        Assert.Equal(LocalPressureEvidenceWriteRejectReason.Disposed, applyAfterDispose.RejectReason);
    }

    private static NativeLawResponseModComposition CreateComposition(
        FakeHostAdapter host,
        Func<NativeLawResponseAdmissionGate, INativeLawResponseAdapter, NativeLawResponseProfile, Action<string>, NativeLawResponseController>? controllerFactory = null,
        Func<ILocalPressureRuntimeHostAdapter, Func<string?, ILocalPressureStateRepository?>, Action<string>, ILocalPressureTierTransitionSink, LocalPressureRuntimeComposition>? runtimeFactory = null,
        Func<LocalPressureRuntimeComposition, IDisposable>? runtimeLifetimeFactory = null,
        Func<NativeLawResponseController, IDisposable>? controllerLifetimeFactory = null) =>
        new(
            host,
            _ => new FakeRepository(new LocalPressureState(30, false, null, null, null, CanonicalPlayerId, "north", "safehouse", 0)),
            _ => { },
            _ => { },
            controllerFactory: controllerFactory,
            runtimeFactory: runtimeFactory,
            runtimeLifetimeFactory: runtimeLifetimeFactory,
            controllerLifetimeFactory: controllerLifetimeFactory);

    private static void Activate(LocalPressureRuntimeService service)
    {
        service.OnPreLoad();
        service.OnLoadComplete();
        service.OnClockBoundary(LocalPressureClockBoundary.HostReady);
    }

    private static LocalPressureEvidenceWriteResult ApplyCustody(LocalPressureRuntimeService service, string episode) =>
        service.TryApplyCustodyEvidence(
            Evidence(service, episode),
            service.SessionEpoch,
            service.LoadEpoch);

    private static CustodyEntryEvidence Evidence(LocalPressureRuntimeService service, string episode) =>
        new(
            CanonicalPlayerId,
            CorrelationId(service, episode),
            "north",
            "safehouse");

    private static string CorrelationId(LocalPressureRuntimeService service, string episode) =>
        $"custody/v1/{service.SessionEpoch:D}/{service.LoadEpoch}/{CanonicalPlayerId}/{episode}";

    private static T ReadPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsAssignableFrom<T>(field!.GetValue(instance));
    }

    private sealed class CallbackDisposable : IDisposable
    {
        private readonly Action _callback;
        private bool _disposed;

        public CallbackDisposable(Action callback) => _callback = callback;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _callback();
        }
    }

    #pragma warning disable CS0067
    private sealed class FakeHostAdapter : ILocalPressureRuntimeHostAdapter
    {
        public string? ActiveSaveFolder { get; set; } = "C:\\Saves\\76561190000000001\\SaveGame_slot";
        public string? CanonicalHostIdentity { get; set; } = CanonicalPlayerId;
        public IReadOnlyList<LocalPressurePlayerSample> Players { get; set; } = new[] { SinglePlayer(CanonicalPlayerId) };
        public LocalPressureClockSample Clock { get; set; } = CreateClock(600);

        public event Action? PreLoad;
        public event Action? LoadComplete;
        public event Action? SaveStart;
        public event Action? SaveComplete;
        public event Action<LocalPressureClockBoundary>? ClockBoundary;

        public LocalPressureClockBoundaryBindingStatus EnsureClockBoundarySubscriptions() =>
            LocalPressureClockBoundaryBindingStatus.Ready;

        public LocalPressureHostAuthorityReadStatus ReadHostAuthority() =>
            LocalPressureHostAuthorityReadStatus.Ready;

        public LocalPressureClockReadStatus TryReadHostClock(out LocalPressureClockSample sample)
        {
            sample = Clock;
            return LocalPressureClockReadStatus.Ready;
        }

        public LocalPressurePlayerReadStatus TryReadSupportedPlayers(out IReadOnlyList<LocalPressurePlayerSample> players)
        {
            players = Players;
            return LocalPressurePlayerReadStatus.Ready;
        }

        public void Dispose() { }

        public static LocalPressurePlayerSample SinglePlayer(string playerCode) =>
            new("0", true, true, playerCode, "north", "safehouse", new object());

        public static LocalPressureClockSample CreateClock(long totalGameMinutes) =>
            new(totalGameMinutes, (int)(totalGameMinutes / 1440), (int)(totalGameMinutes % 1440), null, LocalPressureClockBoundary.HostReady, DateTime.UtcNow);
    }
    #pragma warning restore CS0067

    private sealed class FakeRepository : ILocalPressureStateRepository
    {
        private LocalPressureSaveEnvelope _envelope;

        public FakeRepository(LocalPressureState? initialState = null)
        {
            _envelope = initialState is null
                ? LocalPressureSaveEnvelope.CreateEmpty()
                : new LocalPressureSaveEnvelope(1, new[] { LocalPressurePlayerRecord.FromState(initialState) });
        }

        public LocalPressureStoreLoadResult Load() => new(
            true,
            _envelope.Players.Count == 0 ? LocalPressureStoreLoadStatus.Empty : LocalPressureStoreLoadStatus.Loaded,
            _envelope,
            LocalPressureStoreFailureReason.None,
            "ok");

        public LocalPressureStoreUpdateResult Update(LocalPressurePlayerRecord record) => new(
            true,
            LocalPressureStoreUpdateStatus.Updated,
            _envelope = new LocalPressureSaveEnvelope(1, new[] { record }),
            LocalPressureStoreFailureReason.None,
            "ok");
    }
}
