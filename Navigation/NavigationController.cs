using System.Numerics;
using Dalamud.Plugin.Services;
using PvPSentinel.Models;

namespace PvPSentinel.Navigation;

internal sealed class NavigationController(IVNavmeshAdapter vnav, IPluginLog log)
{
    private Vector3? committedDestination;
    private DateTime committedAtUtc = DateTime.MinValue;
    private DateTime lastMoveRequestUtc = DateTime.MinValue;

    public NavigationDecision Update(
        GameStateSnapshot game,
        BehaviorState behavior,
        FriendlyCluster? mainCluster,
        Configuration config)
    {
        var allowedState = behavior is BehaviorState.Regroup or BehaviorState.FollowGroup or
            BehaviorState.RespawnRegroup or BehaviorState.Retreat or BehaviorState.Engage or BehaviorState.FinishKill;
        if (!config.Enabled || !config.NavigationEnabled || !game.IsFrontline || game.LocalPlayer is null || game.LocalPlayer.IsDead || !allowedState)
        {
            StopOwnedMovement();
            return new NavigationDecision(false, null, "Navigation is stopped by the safety gate.");
        }

        if (!vnav.IsReady)
        {
            StopOwnedMovement();
            return new NavigationDecision(false, null, "vnavmesh is unavailable; automation is paused.");
        }

        if (mainCluster is null)
        {
            StopOwnedMovement();
            return new NavigationDecision(false, null, "No reliable main cluster destination is available.");
        }

        var proposed = BuildRangedFollowPoint(game.LocalPlayer.Position, mainCluster, config.RangedPositionOffset);
        var now = game.CapturedAtUtc;
        var commitmentActive = now - committedAtUtc < TimeSpan.FromSeconds(config.MinimumDestinationCommitmentSeconds);
        var change = committedDestination is null ? float.MaxValue : HorizontalDistance(committedDestination.Value, proposed);

        if (committedDestination is null || (!commitmentActive && change >= config.DestinationSwitchDistance))
        {
            committedDestination = proposed;
            committedAtUtc = now;
        }

        var destination = committedDestination.Value;
        var distance = HorizontalDistance(game.LocalPlayer.Position, destination);
        var centerDistance = HorizontalDistance(game.LocalPlayer.Position, mainCluster.Center);
        var shouldMove = behavior is BehaviorState.Regroup or BehaviorState.RespawnRegroup or BehaviorState.Retreat ||
                         centerDistance > config.MainGroupFollowRadius;

        if (!shouldMove || distance <= 3f)
        {
            if (vnav.IsPathRunning)
                vnav.Stop();
            return new NavigationDecision(false, destination, $"Holding ranged position; main force center is {centerDistance:F1}y away.");
        }

        if (!vnav.IsPathRunning && !vnav.IsPathfindInProgress && now - lastMoveRequestUtc >= TimeSpan.FromSeconds(1))
        {
            lastMoveRequestUtc = now;
            var accepted = vnav.MoveCloseTo(destination, 2.5f);
            if (config.VerboseLogging)
                log.Debug("vnavmesh move request {Result}: {Destination}", accepted ? "accepted" : "rejected", destination);
        }

        return new NavigationDecision(true, destination, commitmentActive
            ? $"Following committed destination; {distance:F1}y remaining."
            : $"Following main force from a ranged offset; {distance:F1}y remaining.");
    }

    public void StopOwnedMovement()
    {
        if (vnav.IsPathRunning || vnav.IsPathfindInProgress)
            vnav.Stop();
        committedDestination = null;
        committedAtUtc = DateTime.MinValue;
    }

    private static Vector3 BuildRangedFollowPoint(Vector3 playerPosition, FriendlyCluster cluster, float offset)
    {
        var movement = new Vector3(cluster.MovementTrend.X, 0f, cluster.MovementTrend.Z);
        Vector3 direction;
        if (movement.LengthSquared() >= 0.25f)
        {
            direction = -Vector3.Normalize(movement);
        }
        else
        {
            var fromCenterToPlayer = new Vector3(playerPosition.X - cluster.Center.X, 0f, playerPosition.Z - cluster.Center.Z);
            direction = fromCenterToPlayer.LengthSquared() < 0.01f ? Vector3.UnitZ : Vector3.Normalize(fromCenterToPlayer);
        }

        return cluster.Center + (direction * Math.Clamp(offset, 4f, 30f));
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}

