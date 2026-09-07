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

internal sealed record NavigationDecision(
    bool ShouldMove,
    Vector3? Destination,
    string Explanation);

internal sealed record CombatDecision(
    bool ControllerActive,
    uint ActionId,
    string DesiredAction,
    string Explanation);

internal sealed record TacticalSnapshot(
    GameStateSnapshot Game,
    BehaviorState Behavior,
    DateTime BehaviorSinceUtc,
    FriendlyCluster? MainCluster,
    IReadOnlyList<FriendlyCluster> Clusters,
    TargetDecision? Target,
    NavigationDecision Navigation,
    CombatDecision Combat,
    int Friendly20,
    int Enemy20,
    int Friendly40,
    int Enemy40,
    string DecisionReason);

