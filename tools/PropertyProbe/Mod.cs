using MelonLoader;

[assembly: MelonInfo(
    typeof(OrganizedCrime.PropertyProbe.PropertyProbeMod),
    "Organized Crime Property Probe",
    "0.1.0",
    "MadJag Studios")]

namespace OrganizedCrime.PropertyProbe;

public sealed class PropertyProbeMod : MelonMod
{
    public override void OnInitializeMelon()
    {
        ProbeLog.Initialize();
        ProbeLog.Info("Read-only property runtime probe loaded.");
        ProbeCoordinator.Initialize();
    }

    public override void OnUpdate()
    {
        ProbeCoordinator.Update();
    }

    public override void OnApplicationQuit()
    {
        ProbeCoordinator.Dispose();
    }
}
