namespace OrganizedCrime.Runtime;

public enum RuntimePropertyLifecycleState
{
    Unavailable,
    Ready,
    Starting,
    Spawned,
    Unloading,
    Failed
}
