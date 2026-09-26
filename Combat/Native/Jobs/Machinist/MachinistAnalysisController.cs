namespace PvPSentinel.Combat.Native.Jobs.Machinist;

internal sealed record AnalysisEvaluation(
    IReadOnlyList<NativeActionCandidate> Candidates,
    IReadOnlyList<string> Rejections);

internal sealed class MachinistAnalysisController
{
    public AnalysisEvaluation Evaluate(NativeCombatContext context, MachinistState state)
    {
        var target = context.Target;
        if (target is null)
            return new AnalysisEvaluation([], ["Analysis/tools rejected: no native target."]);

        var candidates = new List<NativeActionCandidate>();
        var rejections = new List<string>();
        var analysisPermitted = state.DrillPrimed || state.AirAnchorPrimed || state.ChainSawPrimed;

        if (!state.HasAnalysis && analysisPermitted)
        {
            candidates.Add(Self(
                context,
                MachinistActions.Analysis,
                "Analysis",
                $"{state.PrimedTool} is primed; native policy permits Analysis before Drill, Air Anchor, or Chain Saw."));
        }
        else if (!state.HasAnalysis && state.BioblasterPrimed)
        {
            rejections.Add("Analysis rejected: Bioblaster is primed, so the charge is preserved by default.");
        }
        else if (state.HasAnalysis)
        {
            rejections.Add($"Analysis rejected: Analysis is already active for {state.PrimedTool}.");
        }

        if (state.DrillPrimed && target.Distance <= 25f)
            candidates.Add(Enemy(context, MachinistActions.Drill, "Drill", "Use primed Drill; Analysis enhancement and Guard bypass are handled by the game state."));
        if (state.AirAnchorPrimed && target.Distance <= 25f)
            candidates.Add(Enemy(context, MachinistActions.AirAnchor, "Air Anchor", "Use primed Air Anchor for pressure/control; Analysis upgrades the control when active."));
        if (state.ChainSawPrimed && target.Distance <= 25f)
            candidates.Add(Enemy(context, MachinistActions.ChainSaw, "Chain Saw", "Use primed Chain Saw; Analysis adds the high-value target vulnerability when active."));
        if (state.BioblasterPrimed)
        {
            if (target.Distance <= 12f && target.NearbyEnemies >= 2)
                candidates.Add(Enemy(context, MachinistActions.Bioblaster, "Bioblaster", $"Use primed Bioblaster at {target.Distance:F1}y with {target.NearbyEnemies} enemies in the local engagement."));
            else
                rejections.Add($"Bioblaster rejected: requires <=12y and clustered combat; observed {target.Distance:F1}y/{target.NearbyEnemies} nearby enemies.");
        }

        return new AnalysisEvaluation(candidates, rejections);
    }

    private static NativeActionCandidate Self(NativeCombatContext context, uint id, string name, string reason) =>
        new(NativeActionLayer.AnalysisTool, id, name, NativeActionTarget.Self,
            context.Local.GameObjectId, context.Local.EntityId, reason);

    private static NativeActionCandidate Enemy(NativeCombatContext context, uint id, string name, string reason) =>
        new(NativeActionLayer.AnalysisTool, id, name, NativeActionTarget.Enemy,
            context.Target!.Player.GameObjectId, context.Target.Player.EntityId, reason);
}
