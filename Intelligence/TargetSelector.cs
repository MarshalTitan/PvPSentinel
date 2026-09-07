using System.Numerics;
using PvPSentinel.Diagnostics;
using PvPSentinel.Models;

namespace PvPSentinel.Intelligence;

internal sealed class TargetSelector(DevelopmentLogger developmentLog)
{
    private uint selectedEntityId;
    private DateTime selectedAtUtc = DateTime.MinValue;

    public TargetDecision? Select(
        GameStateSnapshot game,
        FriendlyCluster? mainCluster,
        Configuration config)
    {
        if (!config.TargetSelectionEnabled || game.LocalPlayer is null || mainCluster is null || !game.IsClassificationReliable)
        {
            Clear("Target selection gate is closed.");
            return null;
        }

        var scored = game.Enemies
            .Where(enemy => enemy.IsTargetable && !enemy.IsDead && enemy.CurrentHp > 0)
            .Select(enemy => Score(enemy, game, mainCluster, config))
            .Where(decision => decision is not null)
            .Cast<TargetDecision>()
            .Where(decision => decision.Score > 0f)
            .OrderByDescending(decision => decision.Score)
            .ToArray();

        if (scored.Length == 0)
        {
            Clear("No target has a positive score inside the engagement radius.");
            return null;
        }

        developmentLog.Throttled(
            "target-scores",
            string.Join(" | ", scored.Take(8).Select(decision =>
                $"0x{decision.Target.EntityId:X8} {decision.Target.JobAbbreviation} score={decision.Score:F1} finish={decision.IsFinishOpportunity}: {decision.Explanation}")),
            TimeSpan.FromSeconds(4));

        var best = scored[0];
        var incumbent = scored.FirstOrDefault(decision => decision.Target.EntityId == selectedEntityId);
        if (incumbent is not null && incumbent.Target.EntityId != best.Target.EntityId)
        {
            var committed = game.CapturedAtUtc - selectedAtUtc < TimeSpan.FromSeconds(config.MinimumTargetCommitmentSeconds);
            var finishOverride = best.IsFinishOpportunity && !incumbent.IsFinishOpportunity;
            if (!finishOverride && (committed || best.Score < incumbent.Score + config.TargetSwitchScoreAdvantage))
            {
                developmentLog.Throttled(
                    "target-switch-retained",
                    $"Retained 0x{incumbent.Target.EntityId:X8} ({incumbent.Score:F1}) over 0x{best.Target.EntityId:X8} ({best.Score:F1}); committed={committed}, required advantage={config.TargetSwitchScoreAdvantage:F1}.",
                    TimeSpan.FromSeconds(3));
                best = incumbent;
            }
        }

        if (selectedEntityId != best.Target.EntityId)
        {
            selectedEntityId = best.Target.EntityId;
            selectedAtUtc = game.CapturedAtUtc;
            developmentLog.Changed(
                "target-selection",
                selectedEntityId.ToString(),
                $"Selected 0x{best.Target.EntityId:X8} {best.Target.JobAbbreviation}, score {best.Score:F1}, finish={best.IsFinishOpportunity}. {best.Explanation}");
        }

        return best;
    }

    private static TargetDecision? Score(
        PlayerSnapshot enemy,
        GameStateSnapshot game,
        FriendlyCluster mainCluster,
        Configuration config)
    {
        var local = game.LocalPlayer!;
        var distance = HorizontalDistance(local.Position, enemy.Position);
        if (distance > config.EnemyEngagementRadius)
            return null;

        var leashDistance = HorizontalDistance(mainCluster.Center, enemy.Position);
        var guarding = enemy.HasStatusExact("Guard");
        var friendsNear = game.Friendlies.Count(friend => HorizontalDistance(friend.Position, enemy.Position) <= 15f);
        var enemiesNear = game.Enemies.Count(other => HorizontalDistance(other.Position, enemy.Position) <= 15f);
        var isolated = enemiesNear <= 2;
        var finish = config.FinishKoPriorityEnabled &&
                     enemy.HpPercent <= config.FinishTargetHpPercent &&
                     distance <= config.FinishTargetMaxChaseDistance &&
                     leashDistance <= config.MainGroupFollowRadius + config.FinishTargetMaxChaseDistance &&
                     !guarding &&
                     friendsNear >= enemiesNear;

        var distanceScore = 32f * (1f - Math.Clamp(distance / config.EnemyEngagementRadius, 0f, 1f));
        var hpScore = 28f * (1f - Math.Clamp(enemy.HpPercent / 100f, 0f, 1f));
        var shieldPenalty = enemy.ShieldPercent * 0.20f;
        var guardPenalty = guarding ? 45f : 0f;
        var numbersScore = Math.Clamp((friendsNear - enemiesNear) * 4f, -20f, 20f);
        var isolationScore = isolated ? 8f : 0f;
        var battleHighScore = enemy.BattleHigh == "None" ? 0f : 5f;
        var leashPenalty = Math.Max(0f, leashDistance - config.MainGroupFollowRadius) * 1.25f;
        var finishBonus = finish ? 55f : 0f;
        var score = distanceScore + hpScore + numbersScore + isolationScore + battleHighScore + finishBonus - shieldPenalty - guardPenalty - leashPenalty;

        var explanation = finish
            ? $"Finish opportunity: {enemy.HpPercent:F0}% HP, {distance:F1}y, local numbers {friendsNear}:{enemiesNear}."
            : $"Distance {distance:F1}y; HP {enemy.HpPercent:F0}%; shield {enemy.ShieldPercent}%; local numbers {friendsNear}:{enemiesNear}; leash {leashDistance:F1}y.";

        return new TargetDecision(enemy, score, finish, explanation);
    }

    private void Clear(string reason)
    {
        if (selectedEntityId != 0)
            developmentLog.Changed("target-selection", "none", $"Cleared target 0x{selectedEntityId:X8}. {reason}");
        selectedEntityId = 0;
        selectedAtUtc = DateTime.MinValue;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
