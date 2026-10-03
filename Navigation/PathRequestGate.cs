namespace PvPSentinel.Navigation;

// Pathfind tasks cannot be cancelled through the vnavmesh IPC. Only the active
// generation may submit a completed result to Path.MoveTo.
internal sealed class PathRequestGate
{
    private long generation;

    public long Begin() => ++generation;
    public void Invalidate() => ++generation;
    public bool IsCurrent(long ticket) => ticket == generation;
}
