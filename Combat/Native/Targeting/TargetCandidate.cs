using PvPSentinel.Models;

namespace PvPSentinel.Combat.Native.Targeting;

internal sealed record TargetCandidate(
    PlayerSnapshot Player,
    float Distance,
    int AlliedFocus,
    int FriendlySupport,
    int NearbyEnemies,
    bool IsGuarding,
    bool IsExecuteOpportunity,
    bool IsOverextended,
    TargetScore Score)
{
    public uint EffectiveHp => Player.CurrentHp + (uint)(Player.MaxHp * Player.ShieldPercent / 100f);
}

internal sealed record TargetEvaluation(
    TargetCandidate? Selected,
    TargetCandidate? RunnerUp,
    bool Switched,
    string SelectionReason,
    IReadOnlyList<TargetCandidate> Candidates);
