namespace OrganizedCrime.Runtime;

public static class RuntimePropertyLifecycle
{
    public static bool CanTransition(
        RuntimePropertyLifecycleState from,
        RuntimePropertyLifecycleState to) =>
        (from, to) switch
        {
            (RuntimePropertyLifecycleState.Unavailable, RuntimePropertyLifecycleState.Ready) => true,
            (RuntimePropertyLifecycleState.Ready, RuntimePropertyLifecycleState.Starting) => true,
            (RuntimePropertyLifecycleState.Starting, RuntimePropertyLifecycleState.Spawned) => true,
            (RuntimePropertyLifecycleState.Starting, RuntimePropertyLifecycleState.Failed) => true,
            (RuntimePropertyLifecycleState.Spawned, RuntimePropertyLifecycleState.Unloading) => true,
            (RuntimePropertyLifecycleState.Unloading, RuntimePropertyLifecycleState.Spawned) => true,
            (RuntimePropertyLifecycleState.Unloading, RuntimePropertyLifecycleState.Unavailable) => true,
            (RuntimePropertyLifecycleState.Unloading, RuntimePropertyLifecycleState.Failed) => true,
            _ => false
        };
}
