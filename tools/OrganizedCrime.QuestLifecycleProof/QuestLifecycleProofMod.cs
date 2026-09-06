using MelonLoader;

[assembly: MelonInfo(
    typeof(OrganizedCrime.QuestLifecycleProof.QuestLifecycleProofMod),
    "Organized Crime OC-44 Typed Quest Lifecycle Proof",
    "0.1.0",
    "MadJag Studios")]

namespace OrganizedCrime.QuestLifecycleProof;

public sealed class QuestLifecycleProofMod : MelonMod
{
    private QuestLifecycleProofRuntime? _runtime;

    public override void OnInitializeMelon()
    {
        _runtime = new QuestLifecycleProofRuntime(message => MelonLogger.Msg($"[OC-44] {message}"));
        _runtime.Initialize();
    }

    public override void OnUpdate() => _runtime?.Update();

    public override void OnDeinitializeMelon()
    {
        _runtime?.Dispose();
        _runtime = null;
    }
}
