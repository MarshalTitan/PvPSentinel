using PvPSentinel.Models;

namespace PvPSentinel.Combat;

internal interface ICombatController
{
    string LastAction { get; }
    CombatDecision Update(GameStateSnapshot game, BehaviorState behavior, TargetDecision? target, Configuration config);
}

