// OC-151: the Syndicate HQ setup module. Builds the native storage boundary and
// runtime, applies the storage load patch, and builds the door observer, interior,
// runtime service and composition, in today's order. The observer and runtime stay
// method locals captured by deferred lambdas, as in Mod.cs. The log/warn parameters
// are the raw sinks; service delegates prefix "[Organized Crime] " like Mod.cs.
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;

namespace OrganizedCrime.Runtime.Composition;

internal sealed record HqRuntimeOutputs(
    SyndicateHqDoorObserver Observer,
    SyndicateHqRuntimeService Runtime,
    SyndicateHqRuntimeComposition Composition);

internal static class HqModule
{
    public static SyndicateHqNativeStorageRuntime BuildStorage(
        OcCompositionRoot root,
        IOcEngineAdapters engine,
        Action<string> warn)
    {
        Action<string> serviceWarn = message => warn($"[Organized Crime] {message}");
        var hqStorageBoundary = new SyndicateHqNativeStorageBoundary { Timing = root.Timing };
        engine.CompositionTrace?.Add("step:Hq.Boundary");
        var runtime = new SyndicateHqNativeStorageRuntime(
            hqStorageBoundary,
            serviceWarn,
            receiptLog: OrganizedCrimeLog.Receipt);
        engine.CompositionTrace?.Add("step:Hq.Storage");
        return runtime;
    }

    public static void ApplyStorageLoadPatch(
        IOcEngineAdapters engine,
        ISyndicateHqStorageRuntime holdRoomStorage,
        Action<string> warn)
    {
        Action<string> serviceWarn = message => warn($"[Organized Crime] {message}");
        if (!engine.ApplyHqStorageLoad(
                holdRoomStorage,
                new SyndicateHqStorageLoadContextAdapter(),
                serviceWarn,
                receiptLog: OrganizedCrimeLog.Receipt))
        {
            warn("[Organized Crime] Native Syndicate HQ storage will remain inert because its exact vanilla load prefix was unavailable.");
        }
    }

    public static HqRuntimeOutputs BuildRuntime(
        OcCompositionRoot root,
        IOcEngineAdapters engine,
        IRelease1StoryHostContext hqContext,
        Release1StoryRuntimeService story,
        ISyndicateHqStorageRuntime holdRoomStorage,
        Action<string> warn,
        Action<SyndicateHqDoorObserver>? publishObserver = null)
    {
        Action<string> serviceWarn = message => warn($"[Organized Crime] {message}");
        SyndicateHqRuntimeService? hqRuntime = null;
        var hqObserver = new SyndicateHqDoorObserver(
            (fingerprint, generation) => hqRuntime?.HandleDoorInteraction(fingerprint, generation),
            serviceWarn)
        {
            Timing = root.Timing
        };
        engine.CompositionTrace?.Add("step:Hq.Observer");
        publishObserver?.Invoke(hqObserver);
        var hqInterior = new SyndicateHqInteriorRuntime(
            serviceWarn,
            holdRoomStorage)
        {
            Timing = root.Timing
        };
        engine.CompositionTrace?.Add("step:Hq.Interior");
        hqRuntime = new SyndicateHqRuntimeService(
            hqContext,
            new Release1StorySyndicateHqUnlockAuthority(hqContext, story),
            hqObserver,
            hqInterior,
            new SyndicateHqNativePlayerTransport(serviceWarn),
            serviceWarn);
        engine.CompositionTrace?.Add("step:Hq.Runtime");
        var composition = new SyndicateHqRuntimeComposition(hqRuntime, () => root.HqContext?.BeginLoad());
        engine.CompositionTrace?.Add("step:Hq.Composition");
        return new HqRuntimeOutputs(hqObserver, hqRuntime, composition);
    }
}
