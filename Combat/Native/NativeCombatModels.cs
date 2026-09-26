using PvPSentinel.Combat.Native.Targeting;
using PvPSentinel.Models;

namespace PvPSentinel.Combat.Native;

internal enum NativeActionLayer
{
    EmergencyDefense,
    Execute,
    BurstContinuation,
    BurstInitiation,
    AnalysisTool,
    Utility,
    NormalPressure,
}

internal enum NativeActionTarget
{
    Self,
    Enemy,
}

internal sealed record NativeActionCandidate(
    NativeActionLayer Layer,
    uint ActionId,
    string ActionName,
    NativeActionTarget TargetType,
    ulong TargetObjectId,
    uint TargetEntityId,
    string Reason);

internal sealed record NativeCombatContext(
    GameStateSnapshot Game,
    PlayerSnapshot Local,
    BehaviorState Behavior,
    TargetCandidate? Target,
    Configuration Configuration,
    bool OffensiveCombatPermitted,
    bool MarksmanReady);

internal sealed record JobCombatEvaluation(
    string CombatState,
    string ToolState,
    string WildfireState,
    bool Overheated,
    IReadOnlyList<NativeActionCandidate> Candidates,
    IReadOnlyList<string> Rejections);

internal sealed record ActionAvailability(bool IsReady, string Explanation);
