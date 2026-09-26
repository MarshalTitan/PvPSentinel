using System.Numerics;

namespace PvPSentinel.Combat.Native.Jobs.Machinist;

internal sealed class MachinistCombatModule : IPvpJobCombatModule
{
    private readonly MachinistAnalysisController analysis = new();
    private readonly MachinistExecuteController execute = new();
    private readonly MachinistBurstController burst = new();

    public uint JobId => 31;
    public string JobName => "Machinist";
    public ulong CommittedTargetId => burst.TargetId;

    public JobCombatEvaluation Evaluate(NativeCombatContext context)
    {
        var state = MachinistState.Capture(context.Local);
        var candidates = new List<NativeActionCandidate>();
        var rejections = new List<string>();
        var burstEvaluation = burst.Evaluate(context);

        if (!context.OffensiveCombatPermitted || context.Target is null)
        {
            rejections.Add("Offense rejected: behavior/combat permission gate did not authorize an enemy action.");
            rejections.AddRange(burstEvaluation.Rejections);
            return new JobCombatEvaluation(
                "Defensive/hold",
                state.Summary,
                burstEvaluation.State,
                state.Overheated,
                candidates,
                rejections);
        }

        var executeEvaluation = execute.Evaluate(context);
        if (executeEvaluation.Candidate is not null)
            candidates.Add(executeEvaluation.Candidate);
        else
            rejections.Add(executeEvaluation.Explanation);

        candidates.AddRange(burstEvaluation.Continuation);
        candidates.AddRange(burstEvaluation.Initiation.Select(action => action with { Layer = NativeActionLayer.BurstInitiation }));
        rejections.AddRange(burstEvaluation.Rejections);

        var analysisEvaluation = analysis.Evaluate(context, state);
        candidates.AddRange(analysisEvaluation.Candidates);
        rejections.AddRange(analysisEvaluation.Rejections);

        AddUtilityCandidates(context, candidates, rejections);
        AddPressureCandidates(context, state, candidates, rejections);

        return new JobCombatEvaluation(
            burst.IsActive ? "Wildfire burst" : "Engaged pressure",
            state.Summary,
            burstEvaluation.State,
            state.Overheated,
            candidates.OrderBy(candidate => candidate.Layer).ToArray(),
            rejections);
    }

    public void NotifyActionAccepted(NativeActionCandidate action, DateTime acceptedAtUtc, bool hypothetical) =>
        burst.NotifyActionAccepted(action, acceptedAtUtc, hypothetical);

    public void Reset() => burst.Reset();

    private static void AddUtilityCandidates(
        NativeCombatContext context,
        ICollection<NativeActionCandidate> candidates,
        ICollection<string> rejections)
    {
        var target = context.Target!;
        var nearbyParty = context.Game.Friendlies.Count(ally =>
            !ally.IsDead &&
            (ally.PartyMemberFlag || ally.EntityId == context.Local.EntityId) &&
            HorizontalDistance(ally.Position, context.Local.Position) <= 15f);
        var usefulDervishContext = context.Game.IsInCombat && nearbyParty >= 2 &&
                                   (target.AlliedFocus > 0 || target.NearbyEnemies >= 2);
        if (usefulDervishContext)
        {
            candidates.Add(Self(context, MachinistActions.Dervish, "Dervish", NativeActionLayer.Utility,
                $"Useful Frontline role-action context: {nearbyParty} nearby party members and active allied/enemy engagement."));
        }
        else
        {
            rejections.Add($"Dervish rejected: combat={context.Game.IsInCombat}, nearby party={nearbyParty}, allied focus={target.AlliedFocus}, nearby enemies={target.NearbyEnemies}.");
        }

        var bishopContext = target.Distance <= 25f && context.Local.HpPercent >= 45f &&
                            (target.AlliedFocus > 0 || target.NearbyEnemies >= 2);
        if (bishopContext)
        {
            candidates.Add(Enemy(context, MachinistActions.BishopAutoturret, "Bishop Autoturret", NativeActionLayer.Utility,
                $"Proactive combat utility at {target.Distance:F1}y with allied focus {target.AlliedFocus} and {target.NearbyEnemies} nearby enemies; local HP is healthy ({context.Local.HpPercent:F1}%)."));
        }
        else
        {
            rejections.Add("Bishop Autoturret rejected: requires a <=25y active engagement with allied focus or nearby combat and local HP >=45%.");
        }
    }

    private static void AddPressureCandidates(
        NativeCombatContext context,
        MachinistState state,
        ICollection<NativeActionCandidate> candidates,
        ICollection<string> rejections)
    {
        var target = context.Target!;
        if (state.Overheated && target.Distance <= 25f)
            candidates.Add(Enemy(context, MachinistActions.BlazingShot, "Blazing Shot", NativeActionLayer.NormalPressure, "Overheated; apply mobile single-target pressure."));

        candidates.Add(Enemy(context, MachinistActions.FullMetalField, "Full Metal Field", NativeActionLayer.NormalPressure,
            "Use high-value Full Metal Field outside a committed Wildfire sequence when the client reports it available."));

        if (target.Distance <= 12f)
        {
            var scattergunReason = target.NearbyEnemies <= 1
                ? $"Close single-target pressure at {target.Distance:F1}y; Scattergun strikes twice when it hits only one target and applies knockback control."
                : $"Close clustered pressure at {target.Distance:F1}y with {target.NearbyEnemies} enemies observed around the target.";
            candidates.Add(Enemy(context, MachinistActions.Scattergun, "Scattergun", NativeActionLayer.NormalPressure,
                scattergunReason));
        }
        else
        {
            rejections.Add($"Scattergun rejected: selected target at {target.Distance:F1}y exceeds its 12y cone range.");
        }

        if (target.Distance <= 25f)
            candidates.Add(Enemy(context, MachinistActions.BlastCharge, "Blast Charge", NativeActionLayer.NormalPressure, "Baseline ranged pressure; higher-priority defense, execute, burst, tools, and utility were unavailable."));
        else
            rejections.Add($"Normal MCH pressure rejected: selected target at {target.Distance:F1}y exceeds 25y weapon-skill range.");
    }

    private static NativeActionCandidate Self(
        NativeCombatContext context,
        uint id,
        string name,
        NativeActionLayer layer,
        string reason) =>
        new(layer, id, name, NativeActionTarget.Self,
            context.Local.GameObjectId, context.Local.EntityId, reason);

    private static NativeActionCandidate Enemy(
        NativeCombatContext context,
        uint id,
        string name,
        NativeActionLayer layer,
        string reason) =>
        new(layer, id, name, NativeActionTarget.Enemy,
            context.Target!.Player.GameObjectId, context.Target.Player.EntityId, reason);

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
