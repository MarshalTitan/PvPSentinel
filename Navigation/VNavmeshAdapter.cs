using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using PvPSentinel.Diagnostics;

namespace PvPSentinel.Navigation;

internal sealed class VNavmeshAdapter : IVNavmeshAdapter
{
    private readonly ICallGateSubscriber<bool> navReady;
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> moveCloseTo;
    private readonly ICallGateSubscriber<bool> pathRunning;
    private readonly ICallGateSubscriber<bool> pathfindRunning;
    private readonly ICallGateSubscriber<object> stop;
    private readonly DevelopmentLogger developmentLog;

    public VNavmeshAdapter(IDalamudPluginInterface pi, DevelopmentLogger developmentLog)
    {
        this.developmentLog = developmentLog;
        navReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        moveCloseTo = pi.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        pathRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        pathfindRunning = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        stop = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    }

    public bool IsReady => SafeInvoke("nav-ready-ipc", navReady, false);
    public bool IsPathRunning => SafeInvoke("nav-running-ipc", pathRunning, false);
    public bool IsPathfindInProgress => SafeInvoke("nav-pathfind-ipc", pathfindRunning, false);

    public bool MoveCloseTo(Vector3 destination, float tolerance)
    {
        try
        {
            return moveCloseTo.InvokeFunc(destination, false, tolerance);
        }
        catch (Exception ex)
        {
            developmentLog.Throttled("nav-move-ipc-failure", $"vnavmesh move IPC threw {ex.GetType().Name}: {ex.Message}");
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
