using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace PvPSentinel.Navigation;

internal sealed class VNavmeshAdapter : IVNavmeshAdapter
{
    private readonly ICallGateSubscriber<bool> navReady;
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> moveCloseTo;
    private readonly ICallGateSubscriber<bool> pathRunning;
    private readonly ICallGateSubscriber<bool> pathfindRunning;
    private readonly ICallGateSubscriber<object> stop;
    private readonly IPluginLog log;

    public VNavmeshAdapter(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.log = log;
        navReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        moveCloseTo = pi.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        pathRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        pathfindRunning = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        stop = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    }

    public bool IsReady => SafeInvoke(navReady, false);
    public bool IsPathRunning => SafeInvoke(pathRunning, false);
    public bool IsPathfindInProgress => SafeInvoke(pathfindRunning, false);

    public bool MoveCloseTo(Vector3 destination, float tolerance)
    {
        try
        {
            return moveCloseTo.InvokeFunc(destination, false, tolerance);
        }
        catch (Exception ex)
        {
            log.Debug(ex, "vnavmesh rejected a move request.");
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            stop.InvokeAction();
        }
        catch
        {
            // Missing/unloaded vnavmesh is an expected fail-safe condition.
        }
    }

    private static T SafeInvoke<T>(ICallGateSubscriber<T> subscriber, T fallback)
    {
        try { return subscriber.InvokeFunc(); }
        catch { return fallback; }
    }
}

