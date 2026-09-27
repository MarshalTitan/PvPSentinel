using System.Numerics;

namespace PvPSentinel.Navigation;

internal enum MovementOwner
{
    None,
    SentinelTestNav,
    ExternalCombat,
    DeathRecovery,
}

internal enum ManualRouteState
{
    Disarmed,
    Idle,
    Snapping,
    WaitingToMount,
    RequestingPath,
    Following,
    Arrived,
    Cancelled,
    YieldedExternalCombat,
    Dead,
    Failed,
}

internal sealed record ManualNavigationRequest(
    string DestinationId,
    string DestinationName,
    Vector3 ReferencePosition,
    IReadOnlyList<Vector3> ApproachAnchors,
    DateTime RequestedAtUtc);

internal sealed record ManualNavigationSnapshot(
    bool Armed,
    MovementOwner Owner,
    ManualRouteState State,
    string DestinationId,
    string DestinationName,
    Vector3? ReferencePosition,
    Vector3? SnappedPosition,
    int WaypointCount,
    int CurrentWaypoint,
    Vector3? PreviousWaypoint,
    Vector3? CurrentWaypointPosition,
    Vector3? NextWaypoint,
    int StageEndWaypoint,
    string RouteId,
    int RouteAttempt,
    float RouteLength,
    float DistanceRemaining,
    float ProgressAgeSeconds,
    int StuckCount,
    int PathFailureCount,
    string Explanation)
{
    public static ManualNavigationSnapshot Disarmed => new(
        false, MovementOwner.None, ManualRouteState.Disarmed, "NONE", "None", null, null,
        0, 0, null, null, null, 0, "NONE", 0, 0f,
        0f, 0f, 0, 0, "Manual navigation is disarmed.");
}

internal sealed record ManualNavigationEvent(string Name, string Detail, DateTime AtUtc);
