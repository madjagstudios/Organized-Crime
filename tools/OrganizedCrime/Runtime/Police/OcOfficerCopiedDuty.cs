namespace OrganizedCrime.Runtime.Police;

// Engine-free seam for a clone bind that can precede NPCBehaviour.Start.
internal static class OcOfficerCopiedDuty
{
    internal static bool Disable(
        Func<bool> isEnabled, Action disable, Action removeFromStack, Func<bool> isQueued)
    {
        var wasEnabled = isEnabled();
        if (wasEnabled)
            disable();
        if (isEnabled())
            throw new InvalidOperationException("Copied duty is still enabled after disable.");
        // Start installs the onDisable listener. A bind before Start must
        // remove the copied enabled-list entry itself, even if Enabled is false.
        removeFromStack();
        if (isQueued())
            throw new InvalidOperationException("Copied duty is still in the enabled stack.");
        return wasEnabled;
    }
}
