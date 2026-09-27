using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using PvPSentinel.Diagnostics;

namespace PvPSentinel.Navigation;

internal sealed class VNavmeshAdapter : IVNavmeshAdapter
{
    private readonly ICallGateSubscriber<bool> navReady;
    private readonly ICallGateSubscriber<float> buildProgress;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestReachable;
    private readonly ICallGateSubscriber<Vector3, Vector3, bool, float, Task<List<Vector3>>> pathfind;
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> moveTo;
    private readonly ICallGateSubscriber<float, object> setTolerance;
    private readonly ICallGateSubscriber<bool> pathRunning;
    private readonly ICallGateSubscriber<bool> pathfindRunning;
    private readonly ICallGateSubscriber<int> waypointCount;
    private readonly ICallGateSubscriber<List<Vector3>> listWaypoints;
    private readonly ICallGateSubscriber<object> stop;
    private readonly DevelopmentLogger developmentLog;

    public VNavmeshAdapter(IDalamudPluginInterface pi, DevelopmentLogger developmentLog)
    {
        this.developmentLog = developmentLog;
        navReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        buildProgress = pi.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
        nearestReachable = pi.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");
        pathfind = pi.GetIpcSubscriber<Vector3, Vector3, bool, float, Task<List<Vector3>>>("vnavmesh.Nav.PathfindWithTolerance");
        moveTo = pi.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        setTolerance = pi.GetIpcSubscriber<float, object>("vnavmesh.Path.SetTolerance");
        pathRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        pathfindRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        waypointCount = pi.GetIpcSubscriber<int>("vnavmesh.Path.NumWaypoints");
        listWaypoints = pi.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");
        stop = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    }

    public bool IsReady => SafeInvoke("nav-ready-ipc", navReady, false);
    public float BuildProgress => SafeInvoke("nav-build-progress-ipc", buildProgress, -1f);
    public bool IsPathRunning => SafeInvoke("nav-running-ipc", pathRunning, false);
    public bool IsPathfindInProgress => SafeInvoke("nav-pathfind-ipc", pathfindRunning, false);
    public int WaypointCount => SafeInvoke("nav-waypoint-count-ipc", waypointCount, 0);
    public IReadOnlyList<Vector3> Waypoints => SafeInvoke("nav-waypoint-list-ipc", listWaypoints, []).ToArray();

    public Vector3? FindNearestReachable(Vector3 point, float horizontalRadius, float verticalRadius)
    {
        try { return nearestReachable.InvokeFunc(point, horizontalRadius, verticalRadius); }
        catch (Exception ex)
        {
            developmentLog.Throttled("nav-nearest-ipc-failure", $"vnavmesh nearest-point IPC threw {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public async Task<IReadOnlyList<Vector3>> FindPathAsync(Vector3 origin, Vector3 destination, float tolerance)
    {
        try
        {
            var task = pathfind.InvokeFunc(origin, destination, false, tolerance);
            if (task is null)
                return [];
            return await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            developmentLog.Throttled("nav-pathfind-ipc-failure", $"vnavmesh pathfind IPC threw {ex.GetType().Name}: {ex.Message}");
            return [];
        }
    }

    public bool StartPath(IReadOnlyList<Vector3> waypoints, float tolerance)
    {
        if (waypoints.Count < 2)
            return false;

        try
        {
            setTolerance.InvokeAction(tolerance);
            moveTo.InvokeAction(waypoints.ToList(), false);
            return true;
        }
        catch (Exception ex)
        {
            developmentLog.Throttled("nav-start-ipc-failure", $"vnavmesh Path.MoveTo IPC threw {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            stop.InvokeAction();
        }
        catch (Exception ex)
        {
            // Missing/unloaded vnavmesh is an expected fail-safe condition.
            developmentLog.Throttled("nav-stop-ipc-failure", $"vnavmesh stop IPC threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private T SafeInvoke<T>(string key, ICallGateSubscriber<T> subscriber, T fallback)
    {
        try { return subscriber.InvokeFunc(); }
        catch (Exception ex)
        {
            developmentLog.Throttled(key, $"vnavmesh status IPC threw {ex.GetType().Name}: {ex.Message}");
            return fallback;
        }
    }
}
