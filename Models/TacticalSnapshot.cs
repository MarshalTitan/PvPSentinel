using System.Numerics;

namespace PvPSentinel.Models;

internal sealed record FriendlyCluster(
    int Id,
    IReadOnlyList<PlayerSnapshot> Members,
    Vector3 Center,
    Vector3 MovementTrend,
    float Density,
    float Confidence)
{
    public int PlayerCount => Members.Count;
}

internal sealed record TargetDecision(
    PlayerSnapshot Target,
    float Score,
    bool IsFinishOpportunity,
    string Explanation);

internal sealed record ObjectiveDecision(
    FrontlineMap Map,
    ObjectiveObservation Objective,
    float Score,
    float Confidence,
    bool IsActionable,
    string Strategy,
    string Explanation);

internal enum NavigationPathState
{
    Idle,
    WaitingToMount,
    RequestingPath,
    PathValidated,
    FollowingPath,
    YieldingToCombat,
    RepathBackoff,
    Failed,
    Paused,
}

internal enum MountState
{
    Disabled,
    OnFoot,
    MountRequested,
    Mounted,
    DismountRequested,
    Blocked,
}

internal sealed record NavigationDecision(
    bool ShouldMove,
    Vector3? Destination,
    NavigationPathState PathState,
    int WaypointCount,
    int ConsecutiveFailures,
    MountState MountState,
    string Explanation);

internal sealed record CombatDecision(
    CombatProvider Provider,
    bool ControllerActive,
    bool YieldNavigation,
    uint ActionId,
    string DesiredAction,
    string Explanation,
    NativeCombatDiagnostics? Native = null);

internal sealed record NativeCombatDiagnostics(
    NativeCombatMode Mode,
    string CombatState,
    ulong SelectedTargetId,
    string SelectedTarget,
    float SelectedTargetScore,
    string CompetingTarget,
    string TargetSwitchReason,
    string PlayerResources,
    string TargetHealth,
    bool TargetGuarding,
    int AlliedFocus,
    string ToolState,
    string WildfireState,
    bool Overheated,
    string LimitBreakState,
    string DefenseState,
    string OffenseSuppression,
    string ActionResolution,
    IReadOnlyList<string> Rejections);

internal enum QueueLifecycleState
{
    Disabled,
    EmergencyStopped,
    InspectingDailyCampaign,
    DailyCampaignUnknown,
    DailyCampaignBlocked,
    ReadyToQueue,
    JoiningQueue,
    Queued,
    DutyReady,
    AcceptingDuty,
    Loading,
    InMatch,
    LeavingMatch,
    MatchLimitReached,
    Error,
}

internal sealed record QueueDecision(
    QueueLifecycleState State,
    FrontlineMap DailyCampaign,
    int CompletedMatches,
    int MatchLimit,
    bool EmergencyStopLatched,
    string Explanation);

internal sealed record TacticalSnapshot(
    GameStateSnapshot Game,
    BehaviorState Behavior,
    DateTime BehaviorSinceUtc,
    FriendlyCluster? MainCluster,
    IReadOnlyList<FriendlyCluster> Clusters,
    TargetDecision? Target,
    ObjectiveDecision? Objective,
    NavigationDecision Navigation,
    CombatDecision Combat,
    QueueDecision Queue,
    int Friendly20,
    int Enemy20,
    int Unknown20,
    int Friendly40,
    int Enemy40,
    int Unknown40,
    string DecisionReason);
