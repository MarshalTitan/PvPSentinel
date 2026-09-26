namespace PvPSentinel.Combat.Native.Jobs.Machinist;

internal sealed record ExecuteEvaluation(
    NativeActionCandidate? Candidate,
    string Explanation);

internal sealed class MachinistExecuteController
{
    public ExecuteEvaluation Evaluate(NativeCombatContext context)
    {
        var target = context.Target;
        if (target is null)
            return new ExecuteEvaluation(null, "Marksman's Spite rejected: no native target.");
        if (!context.MarksmanReady)
            return new ExecuteEvaluation(null, "Marksman's Spite rejected: limit break/action is not ready.");
        if (target.IsGuarding)
            return new ExecuteEvaluation(null, "Marksman's Spite rejected: target is Guarding.");
        if (target.Distance > 50f)
            return new ExecuteEvaluation(null, $"Marksman's Spite rejected: target range {target.Distance:F1}y exceeds 50y.");
        if (target.IsOverextended)
            return new ExecuteEvaluation(null, "Marksman's Spite rejected: reaching/holding this target is an unreasonable overextension.");

        var config = context.Configuration;
        var chainSawVulnerability = target.Player.HasStatus(3154);
        var damageMultiplier = chainSawVulnerability ? 1.2f : 1f;
        var expectedSoloDamage = (uint)MathF.Round(config.NativeMarksmanBaseDamage * damageMultiplier);
        var allowance = NativeCombatPolicy.MarksmanEffectiveHpAllowance(
            config.NativeMarksmanBaseDamage,
            target.AlliedFocus,
            config.NativeMarksmanFocusAllowance,
            config.NativeMarksmanMaximumEffectiveHp,
            damageMultiplier,
            config.NativeMarksmanSoloConfidence,
            config.NativeMarksmanUncreditedFocus);
        var highHpConfidence = NativeCombatPolicy.HasMarksmanHighHpConfidence(
            target.EffectiveHp,
            config.NativeMarksmanBaseDamage,
            damageMultiplier,
            target.AlliedFocus,
            config.NativeMarksmanHighHpMinimumFocus);
        if (target.EffectiveHp > allowance)
        {
            return new ExecuteEvaluation(null,
                $"Marksman's Spite confidence rejected: effective HP {target.EffectiveHp:N0} exceeds conservative allowance {allowance:N0} (expected solo damage {expectedSoloDamage:N0}, confidence {config.NativeMarksmanSoloConfidence:P0}, allied focus {target.AlliedFocus}, first {config.NativeMarksmanUncreditedFocus} focus uncredited, {config.NativeMarksmanFocusAllowance:N0} per additional focus, cap {config.NativeMarksmanMaximumEffectiveHp:N0}).");
        }

        if (!highHpConfidence)
        {
            return new ExecuteEvaluation(null,
                $"Marksman's Spite confidence rejected: effective HP {target.EffectiveHp:N0} is above expected solo damage {expectedSoloDamage:N0}; allied focus {target.AlliedFocus} is below the configured high-HP evidence requirement {config.NativeMarksmanHighHpMinimumFocus}.");
        }

        var reason =
            $"Contextual execute confidence passed: effective HP {target.EffectiveHp:N0} <= conservative allowance {allowance:N0}; expected solo damage {expectedSoloDamage:N0}, Guard absent, allied focus {target.AlliedFocus}, high-HP evidence={highHpConfidence}, range {target.Distance:F1}y, Chain Saw vulnerability={chainSawVulnerability}.";
        return new ExecuteEvaluation(
            new NativeActionCandidate(
                NativeActionLayer.Execute,
                MachinistActions.MarksmansSpite,
                "Marksman's Spite",
                NativeActionTarget.Enemy,
                target.Player.GameObjectId,
                target.Player.EntityId,
                reason),
            reason);
    }
}
