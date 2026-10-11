// OC-151: the law setup module. Builds the native law response composition (through
// its internal constructor with the engine-provided host adapter; the production
// engine supplies the same adapter the public constructor news up), the custody
// bridge, and the custody patch, in today's order. The log/warn parameters are the
// raw sinks; service delegates prefix "[Organized Crime] " like Mod.cs.
using OrganizedCrime.Model;
using OrganizedCrime.Persistence;
using OrganizedCrime.Runtime;

namespace OrganizedCrime.Runtime.Composition;

internal static class LawModule
{
    public static NativeLawResponseModComposition BuildLaw(
        OcCompositionRoot root,
        IOcEngineAdapters engine,
        Action<string> warn,
        Action<string> log)
    {
        Action<string> serviceWarn = message => warn($"[Organized Crime] {message}");
        Action<string> serviceLog = message => log($"[Organized Crime] {message}");
        var composition = new NativeLawResponseModComposition(
            engine.CreateLawHostAdapter(serviceWarn),
            OcCompositionRoot.CreateLocalPressureRepository,
            serviceWarn,
            serviceLog,
            receiptLog: OrganizedCrimeLog.Receipt);
        root.Law = composition;
        if (composition.LawResponseAdapter is NativePoliceDispatchAdapter dispatch)
            dispatch.ConfigureOfficerPools(root.OfficerRoster?.OfficerPools);
        engine.CompositionTrace?.Add("step:Law.Composition");
        root.LawController = composition.Controller;
        root.LocalPressure = composition.LocalPressureRuntime;
        return composition;
    }

    public static PoliceCustodyEvidenceBridge BuildCustodyBridge(
        IOcEngineAdapters engine,
        LocalPressureRuntimeComposition localPressure,
        Action<string> warn)
    {
        Action<string> serviceWarn = message => warn($"[Organized Crime] {message}");
        var bridge = new PoliceCustodyEvidenceBridge(
            new CustodyEntryGate(),
            localPressure.Service,
            serviceWarn);
        engine.CompositionTrace?.Add("step:Law.Bridge");
        return bridge;
    }

    public static void ApplyCustodyPatch(
        IOcEngineAdapters engine,
        PoliceCustodyEvidenceBridge bridge,
        Action<string> warn)
    {
        Action<string> serviceWarn = message => warn($"[Organized Crime] {message}");
        engine.ApplyCustody(bridge, serviceWarn);
    }
}
