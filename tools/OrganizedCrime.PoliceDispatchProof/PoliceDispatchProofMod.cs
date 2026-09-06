using MelonLoader;

[assembly: MelonInfo(
    typeof(OrganizedCrime.PoliceDispatchProof.PoliceDispatchProofMod),
    "Organized Crime OC-40 Police Dispatch Proof",
    "0.1.0",
    "MadJag Studios")]

namespace OrganizedCrime.PoliceDispatchProof;

public sealed class PoliceDispatchProofMod : MelonMod
{
    private PoliceDispatchProofRuntime? _runtime;

    public override void OnInitializeMelon()
    {
        _runtime = new PoliceDispatchProofRuntime(message => MelonLogger.Msg($"[OC-40] {message}"));
        _runtime.Initialize();
    }

    public override void OnUpdate() => _runtime?.Update();

    public override void OnDeinitializeMelon()
    {
        _runtime?.Dispose();
        _runtime = null;
    }
}
