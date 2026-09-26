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
        var allowance = NativeCombatPolicy.MarksmanEffectiveHpAllowance(
            config.NativeMarksmanBaseDamage,
            target.AlliedFocus,
            config.NativeMarksmanFocusAllowance,
            config.NativeMarksmanMaximumEffectiveHp,
            damageMultiplier);
        if (target.EffectiveHp > allowance)
        {
            return new ExecuteEvaluation(null,
                $"Marksman's Spite rejected: effective HP {target.EffectiveHp:N0} exceeds contextual allowance {allowance:N0} (base {config.NativeMarksmanBaseDamage:N0} + focus {target.AlliedFocus}).");
        }

        var reason =
            $"Contextual execute: effective HP {target.EffectiveHp:N0} <= {allowance:N0}, Guard absent, {target.AlliedFocus} allied focus, range {target.Distance:F1}y, {config.NativeMarksmanBaseDamage:N0} base LB damage available, Chain Saw vulnerability={chainSawVulnerability}.";
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
