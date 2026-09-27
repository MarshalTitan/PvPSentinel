using System.Numerics;
using PvPSentinel.Diagnostics;
using PvPSentinel.Models;

namespace PvPSentinel.Navigation;

internal sealed class NavigationController(
    IVNavmeshAdapter vnav,
    IMountController mount,
    DevelopmentLogger developmentLog)
{
    private Vector3? committedDestination;
    private DateTime committedAtUtc = DateTime.MinValue;
    private Task<IReadOnlyList<Vector3>>? pendingPath;
    private Vector3? pendingDestination;
    private DateTime pathRequestedAtUtc = DateTime.MinValue;
    private DateTime retryAfterUtc = DateTime.MinValue;
    private Vector3? lastProgressPosition;
    private DateTime lastProgressUtc = DateTime.MinValue;
    private DateTime pathStartedAtUtc = DateTime.MinValue;
    private bool ownsPath;
    private int consecutiveFailures;

    public NavigationDecision Update(
        GameStateSnapshot game,
        BehaviorState behavior,
        FriendlyCluster? mainCluster,
        ObjectiveDecision? objective,
        CombatDecision combat,
        Configuration config)
    {
        var allowedState = behavior is BehaviorState.Regroup or BehaviorState.FollowGroup or
            BehaviorState.RespawnRegroup or BehaviorState.Retreat or BehaviorState.Engage or
            BehaviorState.FinishKill or BehaviorState.Travel;
        if (!config.Enabled || !config.NavigationEnabled || !game.IsFrontline ||
            !game.IsClassificationReliable || game.LocalPlayer is null || game.LocalPlayer.IsDead || !allowedState)
        {
            StopOwnedMovement();
            return Decision(false, null, NavigationPathState.Paused, MountState.Disabled,
                "Navigation is stopped by the Frontline, classification, player, or configuration safety gate.");
        }

        if (!vnav.IsReady)
        {
            StopOwnedMovement();
            return Decision(false, null, NavigationPathState.Paused, MountState.Disabled,
                "vnavmesh is unavailable; strategic movement is paused.");
        }

        if (combat.Provider == CombatProvider.RotationSolverReborn && !combat.ControllerActive)
        {
            StopOwnedPath(clearDestination: false);
            return Decision(false, committedDestination, NavigationPathState.Paused, MountState.Blocked,
                $"Strategic movement is fail-closed because RotationSolverReborn is not confirmed active. {combat.Explanation}");
        }

        var nearbyMountThreats = CountNear(game.Enemies, game.LocalPlayer.Position, config.MountEnemySafetyRadius);
        if (combat.YieldNavigation)
        {
            StopOwnedPath(clearDestination: false);
            var dismount = mount.Update(game, false, true, nearbyMountThreats, config);
            return Decision(false, committedDestination, NavigationPathState.YieldingToCombat, dismount.State,
                $"PvPSentinel yielded its owned navigation to {combat.Provider}. {combat.Explanation} {dismount.Explanation}");
        }

        var usingObjective = behavior == BehaviorState.Travel && objective?.IsActionable == true;
        if (!usingObjective && mainCluster is null)
        {
            StopOwnedMovement();
            return Decision(false, null, NavigationPathState.Paused, MountState.Disabled,
                "No reliable main cluster or validated objective destination is available.");
        }

        var proposed = usingObjective
            ? objective!.Objective.Position
            : BuildRangedFollowPoint(game.LocalPlayer.Position, mainCluster!, config.RangedPositionOffset);
        var now = game.CapturedAtUtc;
        var commitmentActive = now - committedAtUtc < TimeSpan.FromSeconds(config.MinimumDestinationCommitmentSeconds);
        var change = committedDestination is null ? float.MaxValue : HorizontalDistance(committedDestination.Value, proposed);

        if (committedDestination is null || (!commitmentActive && change >= config.DestinationSwitchDistance))
        {
            StopOwnedPath(clearDestination: false);
            committedDestination = proposed;
            committedAtUtc = now;
            consecutiveFailures = 0;
            retryAfterUtc = DateTime.MinValue;
            developmentLog.Changed(
                "nav-destination",
                $"{proposed.X:F1}|{proposed.Y:F1}|{proposed.Z:F1}",
                $"Committed {(usingObjective ? "objective" : "group")} destination {FormatVector(proposed)}; previous change {change:F1}y, commitment active={commitmentActive}.");
        }

        var destination = committedDestination.Value;
        var distance = HorizontalDistance(game.LocalPlayer.Position, destination);
        var centerDistance = mainCluster is null
            ? distance
            : HorizontalDistance(game.LocalPlayer.Position, mainCluster.Center);
        var shouldMove = usingObjective || behavior is BehaviorState.Regroup or BehaviorState.RespawnRegroup or BehaviorState.Retreat ||
                         centerDistance > config.MainGroupFollowRadius;

        if (!shouldMove || distance <= 3f)
        {
            StopOwnedPath(clearDestination: false);
            var dismount = mount.Update(game, false, distance <= config.DismountDistance, nearbyMountThreats, config);
            return Decision(false, destination, NavigationPathState.Idle, dismount.State,
                $"Holding strategic position; destination is {distance:F1}y away. {dismount.Explanation}");
        }

        var longDistance = distance >= config.MountDistance;
        var shouldDismount = distance <= config.DismountDistance || nearbyMountThreats > 0 || game.IsInCombat || game.IsCasting;
        var mountDecision = mount.Update(game, longDistance, shouldDismount, nearbyMountThreats, config);
        if (mountDecision.WaitBeforeMovement)
        {
            StopOwnedPath(clearDestination: false);
            return Decision(false, destination, NavigationPathState.WaitingToMount, mountDecision.State,
                $"Strategic movement is waiting on mount state. {mountDecision.Explanation}");
        }

        if (ownsPath && vnav.IsPathRunning)
        {
            TrackProgress(game.LocalPlayer.Position, destination, now, config);
            if (ownsPath && vnav.IsPathRunning)
            {
                return Decision(true, destination, NavigationPathState.FollowingPath, mountDecision.State,
                    $"Following a validated vnavmesh path; {distance:F1}y remain, {vnav.WaypointCount} waypoint(s) queued.");
            }
        }
        else if (ownsPath)
        {
            if (now - pathStartedAtUtc < TimeSpan.FromSeconds(1.5))
            {
                return Decision(true, destination, NavigationPathState.PathValidated, mountDecision.State,
                    "Validated path was submitted; waiting for vnavmesh to report active waypoints.");
            }

            ownsPath = false;
            RegisterFailure(now, "Path.MoveTo was accepted but vnavmesh did not report a running path.");
        }

        if (consecutiveFailures >= Math.Clamp(config.MaximumPathFailures, 1, 10))
        {
            return Decision(false, destination, NavigationPathState.Failed, mountDecision.State,
                $"Navigation paused after {consecutiveFailures} consecutive path/stuck failures. A materially new destination is required before retrying.");
        }

        if (pendingPath is not null)
        {
            if (now - pathRequestedAtUtc > TimeSpan.FromSeconds(Math.Clamp(config.PathRequestTimeoutSeconds, 2f, 30f)))
            {
                pendingPath = null;
                pendingDestination = null;
                RegisterFailure(now, "Path generation timed out.");
            }
            else if (pendingPath.IsCompleted)
            {
                CompletePathRequest(game.LocalPlayer.Position, destination, now);
            }

            if (pendingPath is not null)
            {
                return Decision(false, destination, NavigationPathState.RequestingPath, mountDecision.State,
                    $"Waiting for vnavmesh to generate a path to {FormatVector(destination)}. No movement has started.");
            }

            if (ownsPath && vnav.IsPathRunning)
            {
                return Decision(true, destination, NavigationPathState.FollowingPath, mountDecision.State,
                    $"Validated path started with {vnav.WaypointCount} waypoint(s); {distance:F1}y remain.");
            }
            if (ownsPath)
            {
                return Decision(true, destination, NavigationPathState.PathValidated, mountDecision.State,
                    "Validated path was submitted; waiting for vnavmesh to report active waypoints.");
            }
        }

        if (now < retryAfterUtc)
        {
            return Decision(false, destination, NavigationPathState.RepathBackoff, mountDecision.State,
                $"Repath backoff is active for {(retryAfterUtc - now).TotalSeconds:F1}s after path failure {consecutiveFailures}.");
        }

        pendingDestination = destination;
        pathRequestedAtUtc = now;
        pendingPath = vnav.FindPathAsync(game.LocalPlayer.Position, destination, 2.5f);
        developmentLog.Changed("nav-path-state", "requesting",
            $"Requested a generated ground path from {FormatVector(game.LocalPlayer.Position)} to {FormatVector(destination)}. Movement will not start until validation succeeds.");
        return Decision(false, destination, NavigationPathState.RequestingPath, mountDecision.State,
            $"Requested a vnavmesh path to {FormatVector(destination)}; waiting for validation before movement.");
    }

    public void StopOwnedMovement()
    {
        StopOwnedPath(clearDestination: true);
        consecutiveFailures = 0;
        retryAfterUtc = DateTime.MinValue;
    }

    private void CompletePathRequest(Vector3 origin, Vector3 destination, DateTime now)
    {
        var task = pendingPath!;
        var requested = pendingDestination;
        pendingPath = null;
        pendingDestination = null;

        if (requested is null || HorizontalDistance(requested.Value, destination) > 1f)
        {
            developmentLog.Changed("nav-path-state", "stale", "Discarded a completed path because the committed destination changed.");
            return;
        }

        IReadOnlyList<Vector3> path;
        try
        {
            path = task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            RegisterFailure(now, $"Path generation faulted: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var validation = PathValidator.Validate(path, origin, destination, 2.5f);
        if (!validation.IsValid)
        {
            RegisterFailure(now, $"Generated path rejected: {validation.Explanation}");
            return;
        }

        if (!vnav.StartPath(path, 2.5f))
        {
            RegisterFailure(now, "vnavmesh rejected Path.MoveTo after validation.");
            return;
        }

        ownsPath = true;
        pathStartedAtUtc = now;
        consecutiveFailures = 0;
        lastProgressPosition = origin;
        lastProgressUtc = now;
        developmentLog.Changed("nav-path-state", "following",
            $"Validated and started path with {path.Count} points. {validation.Explanation}");
    }

    private void TrackProgress(Vector3 currentPosition, Vector3 destination, DateTime now, Configuration config)
    {
        if (lastProgressPosition is null || HorizontalDistance(lastProgressPosition.Value, currentPosition) >= 1.5f)
        {
            lastProgressPosition = currentPosition;
            lastProgressUtc = now;
            return;
        }

        if (lastProgressUtc == DateTime.MinValue ||
            now - lastProgressUtc < TimeSpan.FromSeconds(Math.Clamp(config.StuckTimeoutSeconds, 2f, 15f)))
            return;

        var distance = HorizontalDistance(currentPosition, destination);
        var stalledSeconds = (now - lastProgressUtc).TotalSeconds;
        StopOwnedPath(clearDestination: false);
        RegisterFailure(now, $"No meaningful progress for {stalledSeconds:F1}s while {distance:F1}y from the destination.");
    }

    private void RegisterFailure(DateTime now, string reason)
    {
        consecutiveFailures++;
        retryAfterUtc = now.AddSeconds(Math.Min(8, 1 << Math.Min(consecutiveFailures, 3)));
        developmentLog.Changed("nav-path-state", $"failure-{consecutiveFailures}-{reason}",
            $"Path failure {consecutiveFailures}: {reason} Repath after {(retryAfterUtc - now).TotalSeconds:F0}s unless the failure limit is reached.");
    }

    private void StopOwnedPath(bool clearDestination)
    {
        pendingPath = null;
        pendingDestination = null;
        pathRequestedAtUtc = DateTime.MinValue;
        if (ownsPath)
            vnav.Stop();
        ownsPath = false;
        pathStartedAtUtc = DateTime.MinValue;
        lastProgressPosition = null;
        lastProgressUtc = DateTime.MinValue;
        if (clearDestination)
        {
            committedDestination = null;
            committedAtUtc = DateTime.MinValue;
        }
    }

    private NavigationDecision Decision(
        bool shouldMove,
        Vector3? destination,
        NavigationPathState state,
        MountState mountState,
        string explanation) =>
        new(shouldMove, destination, state, vnav.WaypointCount, consecutiveFailures, mountState, explanation);

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

    private static int CountNear(IEnumerable<PlayerSnapshot> players, Vector3 origin, float radius) =>
        players.Count(player => HorizontalDistance(player.Position, origin) <= radius);

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    private static string FormatVector(Vector3 value) => $"({value.X:F1}, {value.Y:F1}, {value.Z:F1})";
}
