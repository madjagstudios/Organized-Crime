using OrganizedCrime.PropertyProbe.Model;
using OrganizedCrime.PropertyProbe.Reporting;
using Xunit;

namespace OrganizedCrime.PropertyProbe.Tests;

public sealed class PropertyDirectAttachmentTests
{
    [Fact]
    public void Direct_attachment_report_records_parent_identity_without_spawn_or_ownership()
    {
        var snapshot = PropertyDirectAttachmentSnapshot.Test(
            attachmentPassed: true,
            propertyNetworkObjectResolved: true,
            propertySpawned: true,
            cleanupPassed: true);

        var text = PropertyDirectAttachmentFormatter.FormatText(snapshot);

        Assert.Contains("ATTACHMENT_PASSED: True", text);
        Assert.Contains("PROPERTY_NETWORK_OBJECT_RESOLVED: True", text);
        Assert.Contains("PROPERTY_SPAWNED: True", text);
        Assert.Contains("SPAWN_ATTEMPTED: False", text);
        Assert.Contains("OWNERSHIP_ATTEMPTED: False", text);
        Assert.Contains("PERSISTENCE_ATTEMPTED: False", text);
        Assert.Contains("CLEANUP_PASSED: True", text);
    }
}
