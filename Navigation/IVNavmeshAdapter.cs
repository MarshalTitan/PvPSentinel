using System.Numerics;

namespace PvPSentinel.Navigation;

internal interface IVNavmeshAdapter
{
    bool IsReady { get; }
    bool IsPathRunning { get; }
    bool IsPathfindInProgress { get; }
    int WaypointCount { get; }
    Task<IReadOnlyList<Vector3>> FindPathAsync(Vector3 origin, Vector3 destination, float tolerance);
    bool StartPath(IReadOnlyList<Vector3> waypoints, float tolerance);
    void Stop();
}
