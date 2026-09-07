using System.Numerics;

namespace PvPSentinel.Navigation;

internal interface IVNavmeshAdapter
{
    bool IsReady { get; }
    bool IsPathRunning { get; }
    bool IsPathfindInProgress { get; }
    bool MoveCloseTo(Vector3 destination, float tolerance);
    void Stop();
}

