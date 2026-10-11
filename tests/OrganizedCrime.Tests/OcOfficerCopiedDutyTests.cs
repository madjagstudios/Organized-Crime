using OrganizedCrime.Runtime.Police;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class OcOfficerCopiedDutyTests
{
    [Fact]
    public void OC142_disabled_mode_is_removed_even_before_Start_registers_listeners()
    {
        var enabled = true;
        var queued = true;
        var disableCalls = 0;
        Assert.True(OcOfficerCopiedDuty.Disable(
            () => enabled, () => { disableCalls++; enabled = false; },
            () => queued = false, () => queued));
        Assert.False(enabled);
        Assert.False(queued);
        Assert.Equal(1, disableCalls);
    }

    [Fact]
    public void OC142_already_disabled_mode_with_stale_stack_entry_is_removed()
    {
        var queued = true;
        Assert.False(OcOfficerCopiedDuty.Disable(
            () => false, () => throw new InvalidOperationException("must not disable twice"),
            () => queued = false, () => queued));
        Assert.False(queued);
    }

    [Fact]
    public void OC142_rebind_does_not_disable_a_mode_twice()
    {
        var enabled = true;
        var queued = true;
        var calls = 0;
        bool Apply() => OcOfficerCopiedDuty.Disable(
            () => enabled, () => { calls++; enabled = false; },
            () => queued = false, () => queued);
        Assert.True(Apply());
        Assert.False(Apply());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void OC142_no_op_native_disable_cannot_report_success()
    {
        Assert.Throws<InvalidOperationException>(() => OcOfficerCopiedDuty.Disable(
            () => true, () => { }, () => { }, () => false));
    }

    [Fact]
    public void OC142_stale_stack_removal_cannot_report_success()
    {
        Assert.Throws<InvalidOperationException>(() => OcOfficerCopiedDuty.Disable(
            () => false, () => { }, () => { }, () => true));
    }

    [Fact]
    public void OC142_disable_failure_aborts_before_removing_an_enabled_mode()
    {
        var removed = false;
        Assert.Throws<ArgumentException>(() => OcOfficerCopiedDuty.Disable(
            () => true, () => throw new ArgumentException(),
            () => removed = true, () => false));
        Assert.False(removed);
    }
}
