using System.Numerics;
using PvPSentinel.Diagnostics;
using PvPSentinel.Models;

namespace PvPSentinel.Combat.Threat;

internal sealed record ObservedEnemyTargeter(
    ulong GameObjectId,
    uint EntityId,
    string Name,
    uint JobId,
    string JobAbbreviation,
    float Distance);

internal sealed record PvPThreatSnapshot(
    DateTime CapturedAtUtc,
    bool IsReliable,
    IReadOnlyList<ObservedEnemyTargeter> Targeters,
    int NearbyEnemyCount,
    int NearbyFriendlyCount,
    PvPThreatLevel Level,
    string Explanation)
{
    public int TargeterCount => Targeters.Count;

    // Guard tuning in v0.2.0.1 was live-validated against targeters within 30y.
    // The HUD still reports every currently observed hard target, while defense
    // consumes this distance-filtered view from the same shared observation.
    public int CombatRelevantTargeterCount => Targeters.Count(targeter => targeter.Distance <= 30f);

    public static PvPThreatSnapshot Unavailable(DateTime capturedAtUtc, string explanation) =>
        new(capturedAtUtc, false, [], 0, 0, PvPThreatLevel.None, explanation);
}

internal sealed class PvPThreatTracker(DevelopmentLogger developmentLog)
{
    private const float NearbyRadius = 18f;

    public PvPThreatSnapshot Evaluate(GameStateSnapshot game)
    {
        var local = game.LocalPlayer;
        if (!game.IsFrontline || local is null)
            return PvPThreatSnapshot.Unavailable(game.CapturedAtUtc, "Threat observations require a live local player in Frontline.");
        if (!game.IsClassificationReliable)
            return PvPThreatSnapshot.Unavailable(game.CapturedAtUtc, "Enemy classification is not reliable; threat observations are suppressed.");

        var targeters = game.Enemies
            .Where(enemy => !enemy.IsDead && enemy.IsTargetable && enemy.TargetObjectId == local.GameObjectId)
            .Select(enemy => new ObservedEnemyTargeter(
                enemy.GameObjectId,
                enemy.EntityId,
                enemy.Name,
                enemy.JobId,
                enemy.JobAbbreviation,
                HorizontalDistance(enemy.Position, local.Position)))
            .OrderBy(targeter => targeter.Distance)
            .ToArray();
        var nearbyEnemies = game.Enemies.Count(enemy =>
            !enemy.IsDead && enemy.IsTargetable && HorizontalDistance(enemy.Position, local.Position) <= NearbyRadius);
        var nearbyFriendlies = game.Friendlies.Count(ally =>
            !ally.IsDead && HorizontalDistance(ally.Position, local.Position) <= NearbyRadius);
        var level = PvPThreatPolicy.EvaluateLevel(targeters.Length, nearbyEnemies);
        var snapshot = new PvPThreatSnapshot(
            game.CapturedAtUtc,
            true,
            targeters,
            nearbyEnemies,
            nearbyFriendlies,
            level,
            $"Currently observed enemy hard targets={targeters.Length}; nearby enemies/allies within {NearbyRadius:F0}y={nearbyEnemies}/{nearbyFriendlies}; threat={level}.");

        developmentLog.Throttled(
            "pvp-threat",
            snapshot.Explanation + FormatTargeters(targeters),
            TimeSpan.FromSeconds(2));
        return snapshot;
    }

    private static string FormatTargeters(IReadOnlyList<ObservedEnemyTargeter> targeters) => targeters.Count == 0
        ? string.Empty
        : " Targeters: " + string.Join(", ", targeters.Select(targeter =>
            $"{targeter.JobAbbreviation} {targeter.Name} {targeter.Distance:F1}y"));

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
