using System.Numerics;
using System.Globalization;
using PvPSentinel.Diagnostics;
using PvPSentinel.FrontlineCore;
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
    private bool manualArmed;
    private ManualNavigationRequest? manualRequest;
    private ManualNavigationRequest? activeManual;
    private readonly Queue<ManualNavigationEvent> manualEvents = [];
    private List<Vector3> manualCandidates = [];
    private int manualCandidateIndex;
    private Vector3? manualSnapped;
    private Task<IReadOnlyList<Vector3>>? manualPendingPath;
    private IReadOnlyList<Vector3> manualRoute = [];
    private IReadOnlyList<Vector3> failedManualRoute = [];
    private bool manualReplacementAttempt;
    private DateTime manualPathRequestedUtc = DateTime.MinValue;
    private DateTime manualPathStartedUtc = DateTime.MinValue;
    private DateTime manualLastProgressUtc = DateTime.MinValue;
    private Vector3? manualLastProgressPosition;
    private int lastReportedManualWaypointCount = -1;
    private ManualNavigationSnapshot manualSnapshot = ManualNavigationSnapshot.Disarmed;

    public ManualNavigationSnapshot ManualSnapshot => manualSnapshot;

    public void SetManualNavigationArmed(bool armed)
    {
        manualArmed = armed;
        if (!armed)
        {
            CancelManual("Manual navigation was disarmed.", ManualRouteState.Disarmed, "navigation_cancelled");
            manualSnapshot = ManualNavigationSnapshot.Disarmed;
        }
        else
        {
            manualSnapshot = manualSnapshot with
            {
                Armed = true,
                Owner = MovementOwner.None,
                State = ManualRouteState.Idle,
                Explanation = "Manual navigation is armed; select a discovered objective or allied cluster.",
            };
        }
    }

    public bool RequestManualDestination(
        string destinationId,
        string destinationName,
        Vector3 referencePosition,
        IReadOnlyList<Vector3>? validatedApproachAnchors = null)
    {
        if (!manualArmed)
            return false;
        manualRequest = new ManualNavigationRequest(
            destinationId,
            destinationName,
            referencePosition,
            validatedApproachAnchors ?? [],
            DateTime.UtcNow);
        Emit("navigation_request", $"destination={destinationId}; reference={FormatVector(referencePosition)}");
        return true;
    }

    public void StopManualNavigation(string reason = "Immediate STOP requested.") =>
        CancelManual(reason, manualArmed ? ManualRouteState.Cancelled : ManualRouteState.Disarmed, "navigation_cancelled");

    public IReadOnlyList<ManualNavigationEvent> DrainManualEvents()
    {
        var result = manualEvents.ToArray();
        manualEvents.Clear();
        return result;
    }

    public NavigationDecision Update(
        GameStateSnapshot game,
        BehaviorState behavior,
        FriendlyCluster? mainCluster,
        ObjectiveDecision? objective,
        CombatDecision combat,
        BattlefieldState battlefield,
        Configuration config)
    {
        if (game.LocalPlayer?.IsDead == true)
        {
            if (activeManual is not null || manualRequest is not null || ownsPath)
                CancelManual("Death cancelled the route; a fresh manual request is required after respawn.", ManualRouteState.Dead, "navigation_cancelled");
            manualSnapshot = BuildManualSnapshot(MovementOwner.DeathRecovery, ManualRouteState.Dead,
                "Player is dead; movement ownership is released and the destination will not resume.", game.LocalPlayer.Position);
            return Decision(false, null, NavigationPathState.Paused, MountState.Disabled, manualSnapshot.Explanation);
        }

        if (battlefield.Combat.BlocksMovement)
        {
            if (activeManual is not null || manualRequest is not null || ownsPath)
                CancelManual("Confirmed enemy-player combat yielded and cancelled the manual route; select a fresh destination after combat.",
                    ManualRouteState.YieldedExternalCombat, "navigation_yielded_external_combat");
            manualSnapshot = BuildManualSnapshot(MovementOwner.ExternalCombat, ManualRouteState.YieldedExternalCombat,
                "Confirmed enemy-player combat owns movement; the prior route will not auto-resume.", game.LocalPlayer?.Position);
            return Decision(false, null, NavigationPathState.YieldingToCombat, MountState.Blocked, manualSnapshot.Explanation);
        }

        if (manualArmed && (manualRequest is not null || activeManual is not null || manualPendingPath is not null || ownsPath))
            return UpdateManual(game, battlefield, config);

        if (!config.AutonomousStrategyEnabled)
        {
            StopOwnedPath(clearDestination: true);
            if (manualArmed)
                manualSnapshot = BuildManualSnapshot(MovementOwner.None, ManualRouteState.Idle,
                    "Manual M2 is armed and idle; autonomous destination selection is disabled.", game.LocalPlayer?.Position);
            return Decision(false, null, NavigationPathState.Idle, MountState.Disabled,
                "Autonomous strategy is disabled for M2. Select a manual destination in diagnostics.");
        }

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

        if (!ManualNavigationPolicy.CanRetry(consecutiveFailures, config.MaximumPathFailures))
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
        CancelManual("PvPSentinel navigation stopped.", manualArmed ? ManualRouteState.Cancelled : ManualRouteState.Disarmed,
            "navigation_cancelled");
        StopOwnedPath(clearDestination: true);
        consecutiveFailures = 0;
        retryAfterUtc = DateTime.MinValue;
    }

    private NavigationDecision UpdateManual(GameStateSnapshot game, BattlefieldState battlefield, Configuration config)
    {
        var now = game.CapturedAtUtc;
        var local = game.LocalPlayer;
        if (!config.Enabled || !config.NavigationEnabled || !game.IsFrontline || local is null)
        {
            CancelManual("Manual navigation is stopped by the master, navigation, Frontline, or player safety gate.",
                ManualRouteState.Failed, "navigation_failed");
            return Decision(false, null, NavigationPathState.Paused, MountState.Disabled, manualSnapshot.Explanation);
        }
        if (!vnav.IsReady)
        {
            CancelManual("vnavmesh is not ready; no direct-movement fallback is permitted.", ManualRouteState.Failed, "navigation_failed");
            return Decision(false, null, NavigationPathState.Paused, MountState.Disabled, manualSnapshot.Explanation);
        }

        if (manualRequest is { } request)
        {
            StopOwnedPath(clearDestination: true);
            activeManual = request;
            manualRequest = null;
            manualCandidates = request.ApproachAnchors
                .Concat([request.ReferencePosition])
                .Concat(RouteComparison.AlternateAnchors(request.ReferencePosition))
                .DistinctBy(point => $"{point.X:F1}|{point.Y:F1}|{point.Z:F1}")
                .ToList();
            manualCandidateIndex = 0;
            manualSnapped = null;
            manualPendingPath = null;
            manualRoute = [];
            failedManualRoute = [];
            manualReplacementAttempt = false;
            consecutiveFailures = 0;
            manualLastProgressPosition = local.Position;
            manualLastProgressUtc = now;
        }

        if (activeManual is null)
        {
            manualSnapshot = BuildManualSnapshot(MovementOwner.None, ManualRouteState.Idle,
                "Manual navigation is armed and waiting for a destination.", local.Position);
            return Decision(false, null, NavigationPathState.Idle, MountState.Disabled, manualSnapshot.Explanation);
        }

        var distance = manualSnapped is { } target
            ? HorizontalDistance(local.Position, target)
            : HorizontalDistance(local.Position, activeManual.ReferencePosition);
        if (manualSnapped is not null && distance <= 3f)
        {
            var arrivedRequest = activeManual;
            if (ownsPath)
                vnav.Stop();
            ownsPath = false;
            activeManual = null;
            manualPendingPath = null;
            manualSnapshot = new ManualNavigationSnapshot(
                manualArmed, MovementOwner.None, ManualRouteState.Arrived,
                arrivedRequest.DestinationId, arrivedRequest.DestinationName,
                arrivedRequest.ReferencePosition, manualSnapped, vnav.WaypointCount,
                Math.Max(0, manualRoute.Count - vnav.WaypointCount), vnav.Waypoints.FirstOrDefault(),
                distance, 0f, consecutiveFailures, $"ARRIVED within {distance:F1}y of the validated approach point.");
            Emit("navigation_arrived", $"destination={arrivedRequest.DestinationId}; approach={FormatVector(manualSnapped.Value)}");
            return Decision(false, manualSnapped, NavigationPathState.Idle, MountState.OnFoot, manualSnapshot.Explanation);
        }

        if (ownsPath && vnav.IsPathRunning)
        {
            if (vnav.WaypointCount != lastReportedManualWaypointCount)
            {
                lastReportedManualWaypointCount = vnav.WaypointCount;
                Emit("navigation_waypoint_changed", $"destination={activeManual.DestinationId}; remaining_waypoints={vnav.WaypointCount}");
            }
            var moved = manualLastProgressPosition is null ? 0f : HorizontalDistance(manualLastProgressPosition.Value, local.Position);
            if (moved >= 1.5f)
            {
                manualLastProgressPosition = local.Position;
                manualLastProgressUtc = now;
                Emit("navigation_progress", $"destination={activeManual.DestinationId}; remaining={distance:F1}");
            }
            else if (now - manualLastProgressUtc >= TimeSpan.FromSeconds(Math.Clamp(config.StuckTimeoutSeconds, 2f, 15f)))
            {
                failedManualRoute = manualRoute;
                consecutiveFailures++;
                Emit("navigation_stuck", $"destination={activeManual.DestinationId}; remaining={distance:F1}; attempt={consecutiveFailures}");
                vnav.Stop();
                ownsPath = false;
                manualReplacementAttempt = true;
                manualPendingPath = null;
                manualLastProgressUtc = now;
            }
            else
            {
                manualSnapshot = BuildManualSnapshot(MovementOwner.SentinelTestNav, ManualRouteState.Following,
                    $"Following the generated vnavmesh route; {distance:F1}y remain.", local.Position);
                return Decision(true, manualSnapped, NavigationPathState.FollowingPath, MountState.OnFoot, manualSnapshot.Explanation);
            }
        }
        else if (ownsPath && now - manualPathStartedUtc > TimeSpan.FromSeconds(1.5))
        {
            consecutiveFailures++;
            Emit("navigation_stuck", $"destination={activeManual.DestinationId}; path stopped before arrival; attempt={consecutiveFailures}");
            failedManualRoute = manualRoute;
            ownsPath = false;
            manualReplacementAttempt = true;
        }

        if (!ManualNavigationPolicy.CanRetry(consecutiveFailures, config.MaximumPathFailures))
        {
            CancelManual($"Navigation failed after {consecutiveFailures} bounded recovery attempts.", ManualRouteState.Failed, "navigation_failed");
            return Decision(false, null, NavigationPathState.Failed, MountState.Disabled, manualSnapshot.Explanation);
        }

        if (manualPendingPath is not null)
        {
            if (now - manualPathRequestedUtc > TimeSpan.FromSeconds(Math.Clamp(config.PathRequestTimeoutSeconds, 2f, 30f)))
            {
                consecutiveFailures++;
                manualPendingPath = null;
                AdvanceManualCandidate("Path generation timed out.");
            }
            else if (manualPendingPath.IsCompleted)
            {
                CompleteManualPath(local.Position, now);
            }
            if (manualPendingPath is not null)
            {
                manualSnapshot = BuildManualSnapshot(MovementOwner.SentinelTestNav, ManualRouteState.RequestingPath,
                    "Waiting for vnavmesh path generation; the player is not being steered directly.", local.Position);
                return Decision(false, manualSnapped, NavigationPathState.RequestingPath, MountState.OnFoot, manualSnapshot.Explanation);
            }
            if (ownsPath)
                return Decision(true, manualSnapped, NavigationPathState.PathValidated, MountState.OnFoot,
                    "Validated generated route submitted to vnavmesh.");
        }

        if (!ownsPath)
        {
            if (manualCandidateIndex >= manualCandidates.Count)
            {
                CancelManual("No reachable, non-repeating approach route was found.", ManualRouteState.Failed, "navigation_failed");
                return Decision(false, null, NavigationPathState.Failed, MountState.Disabled, manualSnapshot.Explanation);
            }
            var candidate = manualCandidates[manualCandidateIndex];
            manualSnapped = vnav.FindNearestReachable(candidate, 12f, 8f);
            if (manualSnapped is null)
            {
                AdvanceManualCandidate($"Approach candidate {manualCandidateIndex + 1} had no reachable mesh point.");
                return UpdateManual(game, battlefield, config);
            }
            manualPathRequestedUtc = now;
            manualPendingPath = vnav.FindPathAsync(local.Position, manualSnapped.Value, 2.5f);
            manualSnapshot = BuildManualSnapshot(MovementOwner.SentinelTestNav, ManualRouteState.RequestingPath,
                $"Snapped approach to {FormatVector(manualSnapped.Value)} and requested a generated route.", local.Position);
            return Decision(false, manualSnapped, NavigationPathState.RequestingPath, MountState.OnFoot, manualSnapshot.Explanation);
        }

        manualSnapshot = BuildManualSnapshot(MovementOwner.SentinelTestNav, ManualRouteState.Following,
            $"Following the generated vnavmesh route; {distance:F1}y remain.", local.Position);
        return Decision(true, manualSnapped, NavigationPathState.FollowingPath, MountState.OnFoot, manualSnapshot.Explanation);
    }

    private void CompleteManualPath(Vector3 origin, DateTime now)
    {
        var task = manualPendingPath!;
        manualPendingPath = null;
        IReadOnlyList<Vector3> route;
        try { route = task.GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            consecutiveFailures++;
            AdvanceManualCandidate($"Path generation faulted: {ex.GetType().Name}.");
            return;
        }
        var validation = PathValidator.Validate(route, origin, manualSnapped!.Value, 2.5f);
        if (!validation.IsValid)
        {
            consecutiveFailures++;
            AdvanceManualCandidate($"Generated path rejected: {validation.Explanation}");
            return;
        }
        if (manualReplacementAttempt && RouteComparison.MateriallyIdentical(failedManualRoute, route))
        {
            Emit("navigation_route_rejected", $"destination={activeManual?.DestinationId}; materially identical opening geometry");
            manualReplacementAttempt = false;
            AdvanceManualCandidate("Replacement path repeated the failed opening geometry.");
            return;
        }
        if (!vnav.StartPath(route, 2.5f))
        {
            consecutiveFailures++;
            AdvanceManualCandidate("vnavmesh rejected Path.MoveTo.");
            return;
        }
        manualRoute = route;
        ownsPath = true;
        manualPathStartedUtc = now;
        manualLastProgressUtc = now;
        manualLastProgressPosition = origin;
        manualReplacementAttempt = false;
        lastReportedManualWaypointCount = route.Count;
        Emit("navigation_path_ready", $"destination={activeManual?.DestinationId}; waypoints={route.Count}");
    }

    private void AdvanceManualCandidate(string reason)
    {
        manualCandidateIndex++;
        manualSnapped = null;
        lastReportedManualWaypointCount = -1;
        manualPendingPath = null;
        manualReplacementAttempt = false;
        developmentLog.Changed("manual-nav-candidate", $"{manualCandidateIndex}|{reason}", reason);
    }

    private void CancelManual(string reason, ManualRouteState state, string eventName)
    {
        var id = activeManual?.DestinationId ?? manualRequest?.DestinationId ?? manualSnapshot.DestinationId;
        if (ownsPath)
            vnav.Stop();
        ownsPath = false;
        manualRequest = null;
        activeManual = null;
        manualPendingPath = null;
        manualCandidates.Clear();
        manualRoute = [];
        failedManualRoute = [];
        manualSnapped = null;
        manualSnapshot = new ManualNavigationSnapshot(
            manualArmed, MovementOwner.None, state, id, "None", null, null, 0, 0, null, 0f, 0f,
            consecutiveFailures, reason);
        Emit(eventName, $"destination={id}; reason={reason}");
    }

    private ManualNavigationSnapshot BuildManualSnapshot(
        MovementOwner owner,
        ManualRouteState state,
        string explanation,
        Vector3? playerPosition)
    {
        var request = activeManual ?? manualRequest;
        var waypoints = vnav.Waypoints;
        var next = waypoints.Count > 0 ? waypoints[0] : (Vector3?)null;
        var remaining = playerPosition is not null && manualSnapped is not null
            ? HorizontalDistance(playerPosition.Value, manualSnapped.Value)
            : 0f;
        var progressAge = manualLastProgressUtc == DateTime.MinValue
            ? 0f
            : Math.Max(0f, (float)(DateTime.UtcNow - manualLastProgressUtc).TotalSeconds);
        return new ManualNavigationSnapshot(
            manualArmed, owner, state,
            request?.DestinationId ?? manualSnapshot.DestinationId,
            request?.DestinationName ?? manualSnapshot.DestinationName,
            request?.ReferencePosition ?? manualSnapshot.ReferencePosition,
            manualSnapped,
            waypoints.Count,
            Math.Max(0, manualRoute.Count - waypoints.Count),
            next,
            remaining,
            progressAge,
            consecutiveFailures,
            explanation);
    }

    private void Emit(string name, string detail)
    {
        manualEvents.Enqueue(new ManualNavigationEvent(name, detail, DateTime.UtcNow));
        developmentLog.Changed($"manual-{name}", detail, $"{name}: {detail}");
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

    private static string FormatVector(Vector3 value) => string.Create(
        CultureInfo.InvariantCulture, $"({value.X:F1}, {value.Y:F1}, {value.Z:F1})");
}
