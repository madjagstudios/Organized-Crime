namespace OrganizedCrime.Runtime;

// Engine-free boundary for the host's Unity registration and cleanup checks.
internal static class RuntimePropertyCleanup
{
    internal static bool IsRegistered<T>(T? property, Func<T?> find,
        Func<T?, bool> isAlive, Func<T?, T?, bool> same) where T : class
    {
        // A destroyed Unity wrapper can equal null after scene unload. Only
        // live objects can remain registered and make cleanup partial.
        if (!isAlive(property)) return false;
        var found = find();
        return isAlive(found) && same(found, property);
    }

    internal static bool Clean<T>(T? target, Func<T?, bool> isAlive,
        Func<T, bool> clean) where T : class => !isAlive(target) || clean(target!);
}
