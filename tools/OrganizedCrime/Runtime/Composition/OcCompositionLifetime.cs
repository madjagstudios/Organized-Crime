// OC-151: mod-instance lifetime state preserved across deinit/reinit. Base Mod.cs kept
// the two 1 Hz timer accumulators and the three QA toggle bools as MelonMod instance
// fields, so they survived OnDeinitializeMelon and a later OnInitializeMelon. The
// composition root is discarded on teardown, so the shell owns one lifetime object and
// passes it into each root; the fanout and QA keys read/write through it.
namespace OrganizedCrime.Runtime.Composition;

internal sealed class OcCompositionLifetime
{
    public float EmergencyPollSeconds { get; set; }
    public float DemandPollSeconds { get; set; }
    public bool TheEnvelopeCashGateDumpNext { get; set; } = true;
    public bool TheEnvelopeClosetCashGateDumpNext { get; set; } = true;
    public bool KeepTheLightsOffProductionGateDumpNext { get; set; } = true;
}
