using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore;

internal sealed class CombatContextTracker
{
    private DateTime pvpEvidenceUntilUtc = DateTime.MinValue;
    private DateTime objectiveEvidenceUntilUtc = DateTime.MinValue;

    public CombatContextSnapshot Update(
        GameStateSnapshot game,
        IReadOnlyList<TrackedPlayer> trackedPlayers,
        IReadOnlyList<MapObjectiveState> objectives,
        int enemiesTargetingUs,
        int nearbyEnemies)
    {
        var now = game.CapturedAtUtc;
        var targetId = game.LocalPlayer?.TargetObjectId ?? 0;
        var targetIsEnemy = targetId != 0 && trackedPlayers.Any(player =>
            player.Relationship == BattlefieldRelationship.EnemyConfirmed &&
            (player.GameObjectId == targetId || player.EntityId == targetId));
        var targetedByEnemy = enemiesTargetingUs > 0;
        var targetIsObjective = targetId != 0 && objectives.Any(objective =>
            objective.PhysicalConfirmation is { } physical &&
            (physical.GameObjectId == targetId || physical.EntityId == targetId));

        if (game.IsInCombat && (targetIsEnemy || targetedByEnemy))
            pvpEvidenceUntilUtc = now.AddSeconds(3);
        if (game.IsInCombat && targetIsObjective)
            objectiveEvidenceUntilUtc = now.AddSeconds(1.5);

        var context = CombatContextPolicy.Resolve(
            game.IsInCombat,
            now < pvpEvidenceUntilUtc,
            now < objectiveEvidenceUntilUtc);
        if (context == FrontlineCombatContext.PvPEnemy)
        {
            return new CombatContextSnapshot(
                FrontlineCombatContext.PvPEnemy,
                true,
                enemiesTargetingUs,
                nearbyEnemies,
                $"Confirmed enemy-player evidence: hard_target={targetIsEnemy}; targeting_us={enemiesTargetingUs}; hold={(pvpEvidenceUntilUtc - now).TotalSeconds:F1}s.");
        }
        if (context == FrontlineCombatContext.ObjectiveCombat)
        {
            return new CombatContextSnapshot(
                FrontlineCombatContext.ObjectiveCombat,
                false,
                enemiesTargetingUs,
                nearbyEnemies,
                $"Confirmed Frontline objective target; hold={(objectiveEvidenceUntilUtc - now).TotalSeconds:F1}s. Manual navigation remains available.");
        }
        if (context == FrontlineCombatContext.StaleGameCombat)
        {
            return new CombatContextSnapshot(
                FrontlineCombatContext.StaleGameCombat,
                false,
                enemiesTargetingUs,
                nearbyEnemies,
                "The generic combat flag has no current enemy-player or objective target evidence; it does not block a fresh manual request.");
        }
        return new CombatContextSnapshot(
            FrontlineCombatContext.None,
            false,
            enemiesTargetingUs,
            nearbyEnemies,
            "No current combat evidence.");
    }

    public void Reset()
    {
        pvpEvidenceUntilUtc = DateTime.MinValue;
        objectiveEvidenceUntilUtc = DateTime.MinValue;
    }
}
