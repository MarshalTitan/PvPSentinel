using System.Numerics;
using PvPSentinel.Diagnostics;
using PvPSentinel.Models;

namespace PvPSentinel.Strategy;

internal sealed class ObjectiveStrategyService(DevelopmentLogger developmentLog)
{
    public ObjectiveDecision? Select(GameStateSnapshot game, Configuration config)
    {
        if (!game.IsFrontline || game.LocalPlayer is null || game.FrontlineMap == FrontlineMap.Unknown)
            return null;

        var strategy = PolicyFor(game.FrontlineMap);
        var candidates = game.ObjectiveObservations
            .Select(observation => Score(game, observation, strategy))
            .Where(decision => decision is not null)
            .Cast<ObjectiveDecision>()
            .OrderByDescending(decision => decision.Score)
            .ToArray();

        developmentLog.Throttled(
            "objective-observation",
            $"{game.FrontlineMap.DisplayName()}: {game.ObjectiveObservations.Count} research objects, {candidates.Length} recognized objective candidates.");

        if (candidates.Length == 0)
            return null;

        var selected = candidates[0];
        developmentLog.Changed(
            "objective-selection",
            $"{selected.Objective.GameObjectId:X}|{selected.IsActionable}|{selected.Score:F0}",
            $"{selected.Map.DisplayName()}: selected '{selected.Objective.Name}' ({selected.Objective.BaseId}) score {selected.Score:F1}, confidence {selected.Confidence:F2}, actionable={selected.IsActionable}. {selected.Explanation}");

        // Objective navigation remains separately opt-in. The decision is still
        // returned while disabled so field diagnostics can validate object IDs,
        // names and state before the strategy is allowed to steer movement.
        return selected with
        {
            IsActionable = selected.IsActionable && config.ObjectiveNavigationEnabled,
            Explanation = config.ObjectiveNavigationEnabled
                ? selected.Explanation
                : selected.Explanation + " Objective navigation is disabled; observation only.",
        };
    }

    private static ObjectiveDecision? Score(
        GameStateSnapshot game,
        ObjectiveObservation observation,
        ObjectivePolicy policy)
    {
        if (!policy.Keywords.Any(keyword => observation.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            return null;

        var local = game.LocalPlayer!;
        var distance = HorizontalDistance(local.Position, observation.Position);
        var friendlyNear = game.Friendlies.Count(player => HorizontalDistance(player.Position, observation.Position) <= 30f);
        var enemyNear = game.Enemies.Count(player => HorizontalDistance(player.Position, observation.Position) <= 30f);
        var hasPositiveState = observation.IsTargetable || observation.MaxHp > 0;
        var actionable = policy.PassiveCaptureObject || hasPositiveState;

        // A recognized name is useful research evidence. Actual navigation needs
        // positive active-state evidence unless a future field-validated policy
        // explicitly marks its passive capture object as authoritative.
        var confidence = Math.Clamp(0.55f + (hasPositiveState ? 0.30f : 0f) +
                                    (observation.BaseId != 0 ? 0.10f : 0f), 0f, 1f);
        var rankBonus = RankBonus(observation.Name);
        var support = Math.Clamp((friendlyNear - enemyNear) * 4f, -32f, 32f);
        var distancePenalty = Math.Min(distance, 250f) * policy.DistanceWeight;
        var score = policy.BasePriority + rankBonus + support - distancePenalty;
        if (enemyNear > friendlyNear + 4)
        {
            actionable = false;
            score -= 50f;
        }

        var explanation =
            $"{policy.Explanation} Distance {distance:F1}y; nearby support {friendlyNear}:{enemyNear}; active evidence={(hasPositiveState ? "yes" : "no")}.";
        return new ObjectiveDecision(game.FrontlineMap, observation, score, confidence, actionable, policy.Name, explanation);
    }

    private static ObjectivePolicy PolicyFor(FrontlineMap map) => map switch
    {
        FrontlineMap.SealRock => new("Seize active tomeliths", ["tomelith"], 125f, 0.22f, false,
            "Prefer safely supported active Allagan tomeliths; do not chase through a large enemy advantage."),
        FrontlineMap.FieldsOfGlory => new("Shatter icebound tomeliths", ["icebound tomelith", "tomelith"], 120f, 0.20f, false,
            "Prefer attackable icebound tomeliths while maintaining friendly support."),
        FrontlineMap.OnsalHakair => new("Danshig Naadam ovoos", ["ovoo"], 130f, 0.22f, false,
            "Prefer safely supported active ovoos and retain the main-force leash."),
        FrontlineMap.BorderlandRuins => new("Secure key locations", ["key location", "allagan", "interceptor", "drone"], 105f, 0.16f, false,
            "Research key-location and interceptor state while retaining the main-force strategy; field state is not yet authoritative."),
        FrontlineMap.WorqorChirteh => new("Triumph fields", ["triumph"], 135f, 0.24f, false,
            "Research inactive, activating, unclaimed, and claimed triumph-field state before authorizing movement."),
        _ => new("Unknown", [], 0f, 1f, false, "No map-specific objective policy is available."),
    };

    private static float RankBonus(string name)
    {
        if (name.Contains("S Rank", StringComparison.OrdinalIgnoreCase))
            return 45f;
        if (name.Contains("A Rank", StringComparison.OrdinalIgnoreCase))
            return 25f;
        if (name.Contains("B Rank", StringComparison.OrdinalIgnoreCase))
            return 10f;
        return 0f;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    private sealed record ObjectivePolicy(
        string Name,
        IReadOnlyList<string> Keywords,
        float BasePriority,
        float DistanceWeight,
        bool PassiveCaptureObject,
        string Explanation);
}
