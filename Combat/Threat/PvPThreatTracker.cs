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
    float Distance,
    PvPThreatObservationSource Source);

internal sealed record PvPThreatSnapshot(
    DateTime CapturedAtUtc,
    bool IsReliable,
    IReadOnlyList<ObservedEnemyTargeter> Targeters,
    int NearbyEnemyCount,
    int NearbyFriendlyCount,
    PvPThreatLevel Level,
    PvPThreatObservationSource Source,
    string Explanation)
{
    public int TargeterCount => Targeters.Count;

    // Guard tuning in v0.2.0.1 was live-validated against targeters within 30y.
    // Defense consumes this distance-filtered view from the same shared
    // observation used by engagement coordination and diagnostics.
    public int CombatRelevantTargeterCount => Targeters.Count(targeter => targeter.Distance <= 30f);

    public static PvPThreatSnapshot Unavailable(DateTime capturedAtUtc, string explanation) =>
        new(capturedAtUtc, false, [], 0, 0, PvPThreatLevel.None,
            PvPThreatObservationSource.Unavailable, explanation);
}

internal sealed class PvPThreatTracker(DevelopmentLogger developmentLog)
{
    private const float NearbyRadius = 18f;

    public PvPThreatSnapshot Evaluate(GameStateSnapshot game)
    {
        var local = game.LocalPlayer;
        if (!game.IsFrontline || local is null)
            return PvPThreatSnapshot.Unavailable(game.CapturedAtUtc, "Threat observations require a live local player in Frontline.");

        var source = game.IsClassificationReliable
            ? PvPThreatObservationSource.BattalionTeam
            : PvPThreatObservationSource.NativeHostileFlagFallback;
        var enemies = game.IsClassificationReliable
            ? game.Enemies
            : game.ObservedPlayers.Where(player => PvPThreatPolicy.IsFallbackHostile(
                player.GameObjectId == local.GameObjectId || player.EntityId == local.EntityId,
                player.HostileFlag,
                player.PartyMemberFlag,
                player.AllianceMemberFlag,
                player.IsRosterMember)).ToArray();
        var friendlies = game.IsClassificationReliable
            ? game.Friendlies
            : game.ObservedPlayers.Where(player =>
                player.IsRosterMember || player.PartyMemberFlag || player.AllianceMemberFlag).ToArray();

        var targeters = enemies
            .Where(enemy => !enemy.IsDead && enemy.IsTargetable && enemy.TargetObjectId == local.GameObjectId)
            .Select(enemy => new ObservedEnemyTargeter(
                enemy.GameObjectId,
                enemy.EntityId,
                enemy.Name,
                enemy.JobId,
                enemy.JobAbbreviation,
                HorizontalDistance(enemy.Position, local.Position),
                source))
            .OrderBy(targeter => targeter.Distance)
            .ToArray();
        var nearbyEnemies = enemies.Count(enemy =>
            !enemy.IsDead && enemy.IsTargetable && HorizontalDistance(enemy.Position, local.Position) <= NearbyRadius);
        var nearbyFriendlies = friendlies.Count(ally =>
            !ally.IsDead && HorizontalDistance(ally.Position, local.Position) <= NearbyRadius);
        var level = PvPThreatPolicy.EvaluateLevel(targeters.Length, nearbyEnemies);
        var snapshot = new PvPThreatSnapshot(
            game.CapturedAtUtc,
            true,
            targeters,
            nearbyEnemies,
            nearbyFriendlies,
            level,
            source,
            $"Observation source={source}; currently observed enemy hard targets={targeters.Length}; nearby enemies/allies within {NearbyRadius:F0}y={nearbyEnemies}/{nearbyFriendlies}; threat={level}." +
            (source == PvPThreatObservationSource.NativeHostileFlagFallback
                ? " Full PvP-team classification remains unresolved and is still fail-closed for strategy; only Dalamud's native Hostile flag, roster exclusions, targetability, and observed hard target are used here."
                : string.Empty));

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
