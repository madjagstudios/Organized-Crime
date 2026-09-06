using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(
    typeof(OrganizedCrime.PhoneCallProof.PhoneCallProofMod),
    "Organized Crime OC-43 Phone Call Proof",
    "0.1.0",
    "MadJag Studios")]

namespace OrganizedCrime.PhoneCallProof;

public sealed class PhoneCallProofMod : MelonMod
{
    private PhoneCallProofRuntime? _runtime;

    public override void OnInitializeMelon()
    {
        _runtime = new PhoneCallProofRuntime(
            PhoneProofHostGate.ReadContext,
            new S1ApiPhoneCallQueue(),
            key => key switch
            {
                PhoneProofKey.Nell => Input.GetKeyDown(KeyCode.F9),
                PhoneProofKey.Arthur => Input.GetKeyDown(KeyCode.F10),
                PhoneProofKey.Classify => Input.GetKeyDown(KeyCode.F11),
                _ => false
            },
            message => MelonLogger.Msg($"[OC-43] {message}"));
        _runtime.Initialize();
        MelonLogger.Msg("[OC-43] initialized; owner keys F9/F10/F11 are required; no automatic call is issued.");
    }

    public override void OnUpdate() => _runtime?.Update();

    public override void OnDeinitializeMelon()
    {
        _runtime?.Dispose();
        _runtime = null;
    }
}
