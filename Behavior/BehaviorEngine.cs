using System.Numerics;
using PvPSentinel.Models;

namespace PvPSentinel.Behavior;

internal sealed class BehaviorEngine
{
    private BehaviorState current = BehaviorState.Idle;
    private DateTime changedAtUtc = DateTime.UtcNow;
    private bool wasDead;

    public (BehaviorState State, DateTime SinceUtc, string Reason) Evaluate(
        GameStateSnapshot game,
        FriendlyCluster? mainCluster,
        TargetDecision? target,
        Configuration config)
    {
        if (!config.Enabled)
            return Set(BehaviorState.Disabled, game.CapturedAtUtc, "Master enable is off.", immediate: true);

        if (!string.IsNullOrEmpty(game.ReadError) || game.LocalPlayer is null)
            return Set(BehaviorState.Paused, game.CapturedAtUtc, $"Game state unavailable: {game.ReadError}", immediate: true);

        if (!game.IsFrontline)
            return Set(BehaviorState.Idle, game.CapturedAtUtc, game.IsPvP ? "PvP territory is not a recognized Frontline map." : "Not inside Frontline.", immediate: true);

        if (!game.IsClassificationReliable)
            return Set(BehaviorState.Paused, game.CapturedAtUtc, $"Player classification is uncertain; automation is paused. {game.TeamStatus.Explanation}", immediate: true);

        if (game.LocalPlayer.IsDead)
        {
            wasDead = true;
            return Set(BehaviorState.Dead, game.CapturedAtUtc, "Player is dead; movement and combat stopped.", immediate: true);
        }

        if (wasDead)
        {
            wasDead = false;
            return Set(BehaviorState.RespawnRegroup, game.CapturedAtUtc, "Player respawned; reacquiring the friendly force.", immediate: true);
        }

        if (current == BehaviorState.RespawnRegroup && game.CapturedAtUtc - changedAtUtc < TimeSpan.FromSeconds(8))
            return (current, changedAtUtc, "Post-respawn regroup commitment is active.");

        if (mainCluster is null)
            return Set(BehaviorState.Paused, game.CapturedAtUtc, "No reliable friendly cluster is visible.", immediate: true);

        var local = game.LocalPlayer;
        var clusterDistance = HorizontalDistance(local.Position, mainCluster.Center);
        var friendly20 = CountNear(game.Friendlies, local.Position, 20f);
        var enemy20 = CountNear(game.Enemies, local.Position, 20f);

        if (local.HpPercent < 30f || enemy20 > friendly20 + 2)
            return Set(BehaviorState.Retreat, game.CapturedAtUtc, $"Threat is unfavorable ({friendly20} friendly / {enemy20} enemy within 20y, HP {local.HpPercent:F0}%).");

        if (clusterDistance > config.MainGroupRegroupDistance)
            return Set(BehaviorState.Regroup, game.CapturedAtUtc, $"Main friendly force is {clusterDistance:F1}y away.");

        if (target?.IsFinishOpportunity == true)
            return Set(BehaviorState.FinishKill, game.CapturedAtUtc, target.Explanation);

        if (target is not null)
            return Set(BehaviorState.Engage, game.CapturedAtUtc, target.Explanation);

        return Set(BehaviorState.FollowGroup, game.CapturedAtUtc, "Stay with the established main friendly force.");
    }

    private (BehaviorState State, DateTime SinceUtc, string Reason) Set(
        BehaviorState desired,
        DateTime now,
        string reason,
        bool immediate = false)
    {
        if (desired == current)
            return (current, changedAtUtc, reason);

        var safetyState = desired is BehaviorState.Disabled or BehaviorState.Idle or BehaviorState.Paused or BehaviorState.Dead or BehaviorState.Retreat;
        if (!immediate && !safetyState && now - changedAtUtc < TimeSpan.FromSeconds(1.5))
            return (current, changedAtUtc, $"State commitment retained; proposed {desired}: {reason}");

        current = desired;
        changedAtUtc = now;
        return (current, changedAtUtc, reason);
    }

    private static int CountNear(IEnumerable<PlayerSnapshot> players, Vector3 point, float radius) =>
        players.Count(player => HorizontalDistance(player.Position, point) <= radius);

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
