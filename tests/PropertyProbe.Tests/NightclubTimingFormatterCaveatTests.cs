using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class NightclubTimingFormatterCaveatTests
{
    [Fact]
    public void Receipt_text_preserves_identity_observation_menu_and_overwrite_caveats()
    {
        var text = NightclubTimingFormatter.FormatText(
            NightclubProbeEvidence.Test(),
            NightclubProbeEvaluator.Evaluate(NightclubProbeEvidence.Test()));

        Assert.Contains("Unity InstanceIds are session-local", text);
        Assert.Contains("NotObserved is not proof of absence", text);
        Assert.Contains("scene-global and not proven door-caused", text);
        Assert.Contains("overwrite on a re-run; copy them before", text);
    }
}
