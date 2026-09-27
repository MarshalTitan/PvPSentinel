namespace PvPSentinel.FrontlineCore;

internal static class CombatContextPolicy
{
    public static FrontlineCombatContext Resolve(bool genericCombat, bool enemyPlayerEvidence, bool objectiveEvidence)
    {
        if (enemyPlayerEvidence)
            return FrontlineCombatContext.PvPEnemy;
        if (objectiveEvidence)
            return FrontlineCombatContext.ObjectiveCombat;
        return genericCombat ? FrontlineCombatContext.StaleGameCombat : FrontlineCombatContext.None;
    }

    public static bool BlocksMovement(FrontlineCombatContext context) => context == FrontlineCombatContext.PvPEnemy;
}
