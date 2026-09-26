using System.Numerics;
using PvPSentinel.Models;

namespace PvPSentinel.Combat.Native.Targeting;

internal sealed class PvPTargetEvaluator
{
    private ulong selectedTargetId;
    private DateTime selectedAtUtc = DateTime.MinValue;

    public TargetEvaluation Evaluate(GameStateSnapshot game, Configuration config, ulong committedTargetId = 0)
    {
        var local = game.LocalPlayer;
        if (local is null)
            return new TargetEvaluation(null, null, null, false, "Local player unavailable.", []);

        var candidates = game.Enemies
            .Where(enemy => enemy.IsEnemy && !enemy.IsDead && enemy.IsTargetable && enemy.CurrentHp > 0)
            .Select(enemy => BuildCandidate(game, local, enemy, config))
            .Where(candidate => candidate.Distance <= config.NativeTargetMaximumRange)
            .OrderByDescending(candidate => candidate.Score.Total)
            .ToArray();

        if (candidates.Length == 0)
        {
            selectedTargetId = 0;
            selectedAtUtc = DateTime.MinValue;
            return new TargetEvaluation(null, null, null, false, "No alive, targetable, classified PvP enemy is in the configured evaluation range.", candidates);
        }

        var acceptableCandidates = candidates
            .Where(candidate => NativeCombatPolicy.IsTargetAcceptable(
                candidate.SafetyScore,
                config.NativeTargetMinimumScore,
                candidate.IsOverextended))
            .ToArray();
        if (acceptableCandidates.Length == 0)
        {
            var bestRejected = candidates.OrderByDescending(candidate => candidate.SafetyScore).First();
            selectedTargetId = 0;
            selectedAtUtc = DateTime.MinValue;
            var safetyReason = bestRejected.IsOverextended
                ? $"is categorically rejected for severe unsafe pursuit/overextension even though its safety score is {bestRejected.SafetyScore:F1}"
                : $"has safety score {bestRejected.SafetyScore:F1}, below the configured {config.NativeTargetMinimumScore:F1} floor because of insufficient contextual value or soft pursuit penalties";
            return new TargetEvaluation(
                null,
                null,
                bestRejected,
                false,
                $"No suitable combat target: best rejected candidate {bestRejected.Player.Name} ({bestRejected.Player.JobAbbreviation}) {safetyReason}. Ranking score {bestRejected.Score.Total:F1}; stickiness is excluded from the safety floor. {bestRejected.Score.Summary}.",
                candidates);
        }

        var best = acceptableCandidates[0];
        var current = candidates.FirstOrDefault(candidate => candidate.Player.GameObjectId == selectedTargetId);
        var currentRejectedForOverextension = current?.IsOverextended == true;
        var currentRejectedBySafety = current is not null &&
                                      !NativeCombatPolicy.IsTargetAcceptable(
                                          current.SafetyScore,
                                          config.NativeTargetMinimumScore,
                                          current.IsOverextended);
        if (currentRejectedBySafety)
            current = null;
        var switched = false;
        string reason;

        var committed = candidates.FirstOrDefault(candidate =>
            candidate.Player.GameObjectId == committedTargetId &&
            !candidate.IsOverextended &&
            NativeCombatPolicy.IsTargetScoreAcceptable(candidate.SafetyScore, config.NativeTargetMinimumScore));
        if (committed is not null)
        {
            switched = selectedTargetId != 0 && selectedTargetId != committed.Player.GameObjectId;
            best = committed;
            reason = $"Retained the job module's committed burst target while it remains valid and tactically supportable: {best.Score.Summary}.";
            if (selectedTargetId != best.Player.GameObjectId)
                Commit(best, game.CapturedAtUtc);

            var committedRunnerUp = acceptableCandidates.FirstOrDefault(candidate => candidate.Player.GameObjectId != best.Player.GameObjectId);
            return new TargetEvaluation(best, committedRunnerUp, null, switched, reason, candidates);
        }

        if (current is null)
        {
            switched = selectedTargetId != 0;
            reason = currentRejectedBySafety
                ? currentRejectedForOverextension
                    ? $"Safety overrode current-target stickiness because the prior target became severely overextended; selected the highest acceptable target: {best.Score.Summary}."
                    : $"Safety overrode current-target stickiness because the prior target fell below the {config.NativeTargetMinimumScore:F1} score floor; selected the highest acceptable target: {best.Score.Summary}."
                : selectedTargetId == 0
                ? $"Selected highest contextual score: {best.Score.Summary}."
                : $"Previous native target became invalid; selected highest valid score: {best.Score.Summary}.";
            Commit(best, game.CapturedAtUtc);
        }
        else if (current.Player.GameObjectId == best.Player.GameObjectId)
        {
            best = current;
            reason = $"Retained current native target; it remains the highest score: {best.Score.Summary}.";
        }
        else
        {
            var committedSeconds = (game.CapturedAtUtc - selectedAtUtc).TotalSeconds;
            var advantage = best.Score.Total - current.Score.Total;
            if (committedSeconds < config.NativeTargetMinimumCommitmentSeconds)
            {
                best = current;
                reason = $"Retained current native target for commitment window ({committedSeconds:F1}/{config.NativeTargetMinimumCommitmentSeconds:F1}s); challenger advantage {advantage:F1}.";
            }
            else if (!NativeCombatPolicy.ShouldSwitchTarget(
                         committedSeconds,
                         config.NativeTargetMinimumCommitmentSeconds,
                         advantage,
                         config.NativeTargetSwitchAdvantage))
            {
                best = current;
                reason = $"Retained current native target; challenger improvement {advantage:F1} is below the {config.NativeTargetSwitchAdvantage:F1} switch threshold.";
            }
            else
            {
                switched = true;
                reason = $"Switched native target for a meaningful score improvement of {advantage:F1}: {best.Score.Summary}.";
                Commit(best, game.CapturedAtUtc);
            }
        }

        var runnerUp = acceptableCandidates.FirstOrDefault(candidate => candidate.Player.GameObjectId != best.Player.GameObjectId);
        return new TargetEvaluation(best, runnerUp, null, switched, reason, candidates);
    }

    public void Reset()
    {
        selectedTargetId = 0;
        selectedAtUtc = DateTime.MinValue;
    }

    private TargetCandidate BuildCandidate(
        GameStateSnapshot game,
        PlayerSnapshot local,
        PlayerSnapshot enemy,
        Configuration config)
    {
        var weights = PvPTargetWeights.From(config);
        var distance = HorizontalDistance(local.Position, enemy.Position);
        var alliedFocus = game.Friendlies.Count(ally =>
            !ally.IsDead && ally.TargetObjectId == enemy.GameObjectId &&
            HorizontalDistance(ally.Position, enemy.Position) <= 30f);
        var friendlySupport = game.Friendlies.Count(ally =>
            !ally.IsDead && HorizontalDistance(ally.Position, enemy.Position) <= 25f);
        var nearbyEnemies = game.Enemies.Count(other =>
            !other.IsDead && HorizontalDistance(other.Position, enemy.Position) <= 20f);
        var guarding = enemy.HasStatus(3054) || enemy.HasStatusExact("Guard");
        var overextended = distance > config.NativeTargetOverextensionRange &&
                           (friendlySupport < 2 || nearbyEnemies > friendlySupport + 1);
        var effectiveHp = enemy.CurrentHp + enemy.MaxHp * enemy.ShieldPercent / 100f;
        var executeThreshold = NativeCombatPolicy.MarksmanEffectiveHpAllowance(
            config.NativeMarksmanBaseDamage,
            alliedFocus,
            config.NativeMarksmanFocusAllowance,
            config.NativeMarksmanMaximumEffectiveHp,
            enemy.HasStatus(3154) ? 1.2f : 1f,
            config.NativeMarksmanSoloConfidence,
            config.NativeMarksmanUncreditedFocus);
        var executeConfidence = NativeCombatPolicy.HasMarksmanHighHpConfidence(
            (uint)effectiveHp,
            config.NativeMarksmanBaseDamage,
            enemy.HasStatus(3154) ? 1.2f : 1f,
            alliedFocus,
            config.NativeMarksmanHighHpMinimumFocus);
        var executeOverkillFloor = NativeCombatPolicy.MarksmanOverkillFloor(
            config.NativeMarksmanOverkillMinimumHp,
            alliedFocus,
            config.NativeMarksmanOverkillFocusHpPerPlayer,
            config.NativeMarksmanUncreditedFocus,
            config.NativeMarksmanOverkillMaximumMinimumHp);
        var executeOverkillRisk = NativeCombatPolicy.IsMarksmanOverkillRisk(
            (uint)effectiveHp,
            distance,
            25f,
            executeOverkillFloor);
        var execute = !guarding && distance <= 50f && effectiveHp <= executeThreshold && executeConfidence && !executeOverkillRisk;

        var rangeScore = Math.Clamp(1f - distance / Math.Max(1f, config.NativeTargetMaximumRange), 0f, 1f) * weights.RangeWeight;
        var healthScore = Math.Clamp(1f - enemy.HpPercent / 100f, 0f, 1f) * weights.HpPercentWeight;
        var absoluteScore = Math.Clamp(1f - effectiveHp / 100000f, 0f, 1f) * weights.AbsoluteHpWeight;
        var maximumHpScore = Math.Clamp(1f - enemy.MaxHp / 100000f, 0f, 1f) * weights.MaximumHpWeight;
        var focusScore = Math.Min(alliedFocus * weights.AlliedFocusPerPlayer, weights.AlliedFocusCap);
        var executeScore = execute ? weights.ExecuteBonus : 0f;
        var stickyTargetId = selectedTargetId != 0 ? selectedTargetId : local.TargetObjectId;
        var stickyScore = enemy.GameObjectId == stickyTargetId ? weights.CurrentTargetBonus : 0f;
        var guardScore = guarding ? -weights.GuardPenalty : 0f;
        var extensionScore = overextended ? -weights.OverextensionPenalty : 0f;
        if (friendlySupport == 0 && distance > 25f)
            extensionScore -= weights.UnsupportedPenalty;

        var score = new TargetScore(
            rangeScore + healthScore + absoluteScore + maximumHpScore + focusScore + executeScore + stickyScore + guardScore + extensionScore,
            rangeScore,
            healthScore,
            absoluteScore,
            maximumHpScore,
            focusScore,
            executeScore,
            stickyScore,
            guardScore,
            extensionScore);

        return new TargetCandidate(
            enemy, distance, alliedFocus, friendlySupport, nearbyEnemies,
            guarding, execute, overextended, score);
    }

    private void Commit(TargetCandidate candidate, DateTime now)
    {
        selectedTargetId = candidate.Player.GameObjectId;
        selectedAtUtc = now;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
