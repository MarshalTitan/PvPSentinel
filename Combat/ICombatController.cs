using PvPSentinel.Models;
using PvPSentinel.Combat.Threat;

namespace PvPSentinel.Combat;

internal interface ICombatController
{
    string LastAction { get; }
    CombatDecision Update(
        GameStateSnapshot game,
        BehaviorState behavior,
        TargetDecision? target,
        Configuration config,
        PvPThreatSnapshot threat);
}
