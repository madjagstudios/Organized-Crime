using MelonLoader;
using Il2CppScheduleOne.Persistence;
using OrganizedCrime.Runtime;

[assembly: MelonInfo(
    typeof(OrganizedCrime.PostBenziesProof.PostBenziesProofMod),
    "Organized Crime OC-50 Post-Benzies Proof",
    "0.1.0",
    "MadJag Studios")]

namespace OrganizedCrime.PostBenziesProof;

public sealed class PostBenziesProofMod : MelonMod
{
    private PostBenziesProofRuntime? _runtime;

    public override void OnInitializeMelon()
    {
        if (_runtime is not null)
            return;

        var reader = new Release1PostBenziesUnlockReader(
            new SyndicateHqHostContextAdapter(),
            new S1CartelStatusSource());
        _runtime = new PostBenziesProofRuntime(
            reader,
            "SaveGame_5",
            receipt => MelonLogger.Msg($"[OC-50 Proof] {receipt}"),
            activeSaveFolderProvider: ReadActiveSaveFolder);
        _runtime.Initialize();
        MelonLogger.Msg("[OC-50 Proof] initialized read-only; waiting for canonical single-player SaveGame_5.");
    }

    public override void OnUpdate() => _runtime?.Update();

    public override void OnDeinitializeMelon()
    {
        _runtime?.Dispose();
        _runtime = null;
    }

    private static string? ReadActiveSaveFolder()
    {
        try
        {
            return LoadManager.Instance?.LoadedGameFolderPath;
        }
        catch
        {
            return null;
        }
    }
}
