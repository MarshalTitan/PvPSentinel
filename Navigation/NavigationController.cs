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
    private Vector3 pendingOrigin;
    private readonly PathRequestGate strategicPathGate = new();
    private long strategicPathTicket;
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
    private List<Vector3> manualDepartureCandidates = [];
    private int manualDepartureCandidateIndex;
    private ManualRecoveryPhase manualRecoveryPhase;
    private Vector3? manualSnapped;
    private Task<IReadOnlyList<Vector3>>? manualPendingPath;
    private Task<IReadOnlyList<Vector3>>? dynamicReplacementPath;
    private ManualNavigationRequest? dynamicReplacementRequest;
    private Vector3? dynamicReplacementSnap;
    private Vector3 dynamicReplacementOrigin;
    private DateTime dynamicReplacementRequestedUtc = DateTime.MinValue;
    private string dynamicReplacementRouteId = "NONE";
    private readonly PathRequestGate dynamicReplacementGate = new();
    private long dynamicReplacementTicket;
    private Vector3 manualPathOrigin;
    private readonly PathRequestGate manualPathGate = new();
    private long manualPathTicket;
    private IReadOnlyList<Vector3> manualRoute = [];
    private IReadOnlyList<Vector3> failedManualCorridor = [];
    private RouteExecutionPlan? manualPlan;
    private int manualRouteCursor;
    private int manualStageEnd;
    private int manualRouteAttempt;
    private string manualRouteId = "NONE";
    private bool manualReplacementAttempt;
    private int manualStuckCount;
    private int manualPathFailureCount;
    private DateTime manualPathRequestedUtc = DateTime.MinValue;
    private DateTime manualPathStartedUtc = DateTime.MinValue;
    private DateTime manualLastProgressUtc = DateTime.MinValue;
    private Vector3? manualLastProgressPosition;
    private int lastReportedManualWaypointCount = -1;
    private string manualMountSignature = string.Empty;
    private bool manualYieldedToCombat;
    private bool manualCombatTravelReported;
    private ManualNavigationSnapshot manualSnapshot = ManualNavigationSnapshot.Disarmed;

    public ManualNavigationSnapshot ManualSnapshot => manualSnapshot;
    public string? CurrentManualDestinationId => manualRequest?.DestinationId ?? activeManual?.DestinationId;
    public bool IsMountTransitionPending(DateTime now) => mount.IsTransitionPending(now);

    public void SetManualNavigationArmed(bool armed)
    {
        if (manualArmed == armed)
            return;
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
        IReadOnlyList<Vector3>? validatedApproachAnchors = null,
        bool includeReferencePosition = true,
        float minimumApproachClearance = 0f)
    {
        if (!manualArmed)
            return false;
        ClearDynamicReplacement();
        manualPathGate.Invalidate();
        var replacement = new ManualNavigationRequest(
            destinationId,
            destinationName,
            referencePosition,
            validatedApproachAnchors ?? [],
            DateTime.UtcNow,
            includeReferencePosition,
            minimumApproachClearance);
        manualCombatTravelReported = false;

        if (manualYieldedToCombat && activeManual is { } interrupted)
        {
            activeManual = replacement;
            manualRequest = null;
            manualCandidates = [];
            manualCandidateIndex = 0;
            ResetManualRecovery();
            manualSnapped = null;
            manualPendingPath = null;
            manualPathGate.Invalidate();
            manualRoute = [];
            failedManualCorridor = [];
            manualPlan = null;
            manualRouteCursor = 0;
            manualStageEnd = 0;
            manualRouteAttempt = 0;
            manualRouteId = "NONE";
            manualReplacementAttempt = false;
            consecutiveFailures = 0;
            manualStuckCount = 0;
            manualPathFailureCount = 0;
            manualMountSignature = string.Empty;
            Emit(
                "navigation_destination_replaced",
                $"previous={interrupted.DestinationId}; destination={destinationId}; combat_pause_preserved=true; action=repath_after_clearance");
        }
        else
        {
            manualRequest = replacement;
        }
        Emit("navigation_request", $"destination={destinationId}; reference={FormatVector(referencePosition)}; approaches={replacement.ApproachAnchors.Count}; center_allowed={includeReferencePosition}; clearance={minimumApproachClearance:F1}");
        return true;
    }

    /// <summary>Generate a moving-group replacement while the old validated route keeps running.</summary>
    public bool RequestDynamicDestination(string destinationId, string destinationName,
        Vector3 referencePosition, Vector3 currentPosition, DateTime now)
    {
        if (!manualArmed)
            return false;
        if (activeManual?.DestinationId != destinationId || !ownsPath || !vnav.IsPathRunning ||
            !vnav.IsReady || manualYieldedToCombat)
            return RequestManualDestination(destinationId, destinationName, referencePosition);

        var snapped = vnav.FindNearestReachable(referencePosition, 12f, 8f);
        if (snapped is null)
        {
            Emit("navigation_dynamic_retarget_rejected",
                $"destination={destinationId}; reason=no-reachable-mesh-point; old_route_retained=true");
            return false;
        }
        ClearDynamicReplacement();
        dynamicReplacementRequest = new ManualNavigationRequest(destinationId, destinationName,
            referencePosition, [], now);
        dynamicReplacementSnap = snapped;
        dynamicReplacementOrigin = currentPosition;
        dynamicReplacementRequestedUtc = now;
        dynamicReplacementRouteId = manualRouteId;
        dynamicReplacementTicket = dynamicReplacementGate.Begin();
        dynamicReplacementPath = vnav.FindPathAsync(currentPosition, snapped.Value, 2.5f);
        Emit("navigation_dynamic_retarget_request",
            $"destination={destinationId}; endpoint={FormatVector(snapped.Value)}; old_route_retained=true");
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
        if (battlefield.Match.Lifecycle == FrontlineMatchLifecycle.Results)
        {
            if (activeManual is not null || manualRequest is not null || manualPendingPath is not null ||
                manualYieldedToCombat)
                CancelManual("Match results ended manual navigation; a new match requires a fresh request.",
                    ManualRouteState.Cancelled, "navigation_cancelled");
            StopOwnedPath(clearDestination: true);
            return Decision(false, null, NavigationPathState.Paused, MountState.Disabled,
                "Match results are visible; manual and autonomous navigation are stopped.");
        }

        if (game.LocalPlayer?.IsDead == true)
        {
            if (activeManual is not null || manualRequest is not null || ownsPath)
                CancelManual("Death cancelled the route; a fresh manual request is required after respawn.", ManualRouteState.Dead, "navigation_cancelled");
            manualSnapshot = BuildManualSnapshot(MovementOwner.DeathRecovery, ManualRouteState.Dead,
                "Player is dead; movement ownership is released and the destination will not resume.", game.LocalPlayer.Position);
            return Decision(false, null, NavigationPathState.Paused, MountState.Disabled, manualSnapshot.Explanation);
        }

        var hasManualDestination = manualArmed &&
            (manualRequest is not null || activeManual is not null || manualPendingPath is not null || manualYieldedToCombat);
        var continueManualWithReborn = config.Enabled && config.NavigationEnabled &&
            combat.Provider == CombatProvider.RotationSolverReborn && combat.ControllerActive &&
            game.IsFrontline && !game.IsBetweenAreas && game.IsClassificationReliable &&
            battlefield.Match.Lifecycle == FrontlineMatchLifecycle.MatchActive &&
            game.LocalPlayer is { IsDead: false };
        var combatEvidence = battlefield.Combat.BlocksMovement || combat.YieldNavigation;
        var combatTravelActive = hasManualDestination && continueManualWithReborn && combatEvidence;
        if (combatTravelActive && !manualCombatTravelReported)
        {
            manualCombatTravelReported = true;
            Emit("navigation_continuing_during_combat",
                $"destination={activeManual?.DestinationId ?? manualRequest?.DestinationId}; provider=RotationSolverReborn; movement=vnavmesh-on-foot; combat_actions=external");
        }
        else if (!combatEvidence || !hasManualDestination || !continueManualWithReborn)
        {
            manualCombatTravelReported = false;
        }
        var manualCombatPause = ManualCombatYieldPolicy.ShouldPause(
            battlefield.Combat.BlocksMovement,
            combat.YieldNavigation,
            continueManualWithReborn);
        if (hasManualDestination && manualCombatPause)
        {
            return PauseManualForCombat(game, battlefield, combat, config);
        }

        if (ManualCombatYieldPolicy.ShouldResume(
                manualYieldedToCombat,
                battlefield.Combat.BlocksMovement,
                combat.YieldNavigation,
                continueManualWithReborn))
        {
            ResumeManualAfterCombat(game.LocalPlayer?.Position);
        }

        if (manualArmed && (manualRequest is not null || activeManual is not null || manualPendingPath is not null || ownsPath))
            return UpdateManual(game, battlefield, config, combatTravelActive);

        if (battlefield.Combat.BlocksMovement)
        {
            StopOwnedPath(clearDestination: false);
            var localPosition = game.LocalPlayer?.Position ?? Vector3.Zero;
            var nearbyThreats = game.LocalPlayer is null
                ? 0
                : CountNear(game.Enemies, localPosition, config.MountEnemySafetyRadius);
            var dismount = mount.Update(game, false, true, nearbyThreats, config);
            return Decision(false, committedDestination, NavigationPathState.YieldingToCombat, dismount.State,
                $"Confirmed enemy-player combat owns movement. {battlefield.Combat.Evidence} {dismount.Explanation}");
        }

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
            var dismount = mount.Update(game, false, true, nearbyMountThreats, config);
            return Decision(false, destination, NavigationPathState.Idle, dismount.State,
                $"Holding strategic position; destination is {distance:F1}y away. {dismount.Explanation}");
        }

        var longDistance = distance >= config.MountDistance;
        var shouldDismount = MountTravelPolicy.ShouldDismount(
            game.IsMounted,
            distance,
            config.DismountDistance,
            combatOwnsMovement: false);
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
        pendingOrigin = game.LocalPlayer.Position;
        pathRequestedAtUtc = now;
        strategicPathTicket = strategicPathGate.Begin();
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

    private NavigationDecision UpdateManual(
        GameStateSnapshot game, BattlefieldState battlefield, Configuration config, bool combatTravelActive)
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
            manualCandidates = request.Candidates().ToList();
            manualCandidateIndex = 0;
            ResetManualRecovery();
            manualSnapped = null;
            manualPendingPath = null;
            manualRoute = [];
            failedManualCorridor = [];
            manualPlan = null;
            manualRouteCursor = 0;
            manualStageEnd = 0;
            manualRouteAttempt = 0;
            manualRouteId = "NONE";
            manualReplacementAttempt = false;
            consecutiveFailures = 0;
            manualStuckCount = 0;
            manualPathFailureCount = 0;
            manualLastProgressPosition = local.Position;
            manualLastProgressUtc = now;
            manualMountSignature = string.Empty;
        }

        if (activeManual is null)
        {
            manualSnapshot = BuildManualSnapshot(MovementOwner.None, ManualRouteState.Idle,
                "Manual navigation is armed and waiting for a destination.", local.Position);
            return Decision(false, null, NavigationPathState.Idle, MountState.Disabled, manualSnapshot.Explanation);
        }

        CompleteDynamicReplacement(local.Position, now, config);

        var distance = manualSnapped is { } target
            ? HorizontalDistance(local.Position, target)
            : HorizontalDistance(local.Position, activeManual.ReferencePosition);
        var arrivalDistance = manualSnapped is { } arrivalTarget
            ? Vector3.Distance(local.Position, arrivalTarget)
            : float.MaxValue;
        var nearbyMountThreats = CountNear(game.Enemies, local.Position, config.MountEnemySafetyRadius);
        var remainingRouteDistance = manualPlan is null
            ? distance
            : manualPlan.RemainingLength(local.Position, manualRouteCursor);
        var dynamicFieldLeg = activeManual.DestinationId.StartsWith("FIELD-FOLLOW-", StringComparison.Ordinal);
        // A nearby group across a wall can have a long generated detour. Avoid
        // mounting for that short local leg, then dismounting one second later.
        var longDistance = MountTravelPolicy.ShouldMountForLeg(combatTravelActive, dynamicFieldLeg,
            remainingRouteDistance, distance, config.MountDistance);
        var shouldDismount = MountTravelPolicy.ShouldDismount(
            game.IsMounted,
            remainingRouteDistance,
            MountTravelPolicy.DismountDistanceForLeg(dynamicFieldLeg, config.DismountDistance),
            combatOwnsMovement: combatTravelActive) &&
            (combatTravelActive || (manualPlan is not null &&
                                    manualRecoveryPhase != ManualRecoveryPhase.DepartureStage));
        var mountDecision = mount.Update(game, longDistance, shouldDismount, nearbyMountThreats, config);
        ReportManualMountDecision(mountDecision, remainingRouteDistance, nearbyMountThreats, game);
        if (ManualMountPolicy.ShouldWaitBeforeMovement(
                mountDecision.State == MountState.MountRequested,
                mountDecision.State == MountState.DismountRequested,
                game.IsMounted,
                game.IsMounting,
                mountDecision.WaitBeforeMovement))
        {
            if (ownsPath)
                vnav.Stop();
            ownsPath = false;
            manualLastProgressPosition = local.Position;
            manualLastProgressUtc = now;
            manualSnapshot = BuildManualSnapshot(
                MovementOwner.SentinelTestNav,
                ManualRouteState.WaitingToMount,
                $"Manual route is waiting for the mount transition. {mountDecision.Explanation}",
                local.Position);
            return Decision(
                false,
                manualSnapped ?? activeManual.ReferencePosition,
                NavigationPathState.WaitingToMount,
                mountDecision.State,
                manualSnapshot.Explanation);
        }

        if (manualSnapped is not null && ManualNavigationPolicy.HasReachedGeneratedPoint(
                local.Position, manualSnapped.Value, 3f, 3f, game.IsMounted))
        {
            if (manualRecoveryPhase == ManualRecoveryPhase.DepartureStage)
            {
                if (ownsPath)
                    vnav.Stop();
                ownsPath = false;
                manualRecoveryPhase = ManualRecoveryPhase.DestinationRetry;
                manualCandidateIndex = 0;
                manualSnapped = null;
                manualPendingPath = null;
                manualRoute = [];
                manualPlan = null;
                manualRouteCursor = 0;
                manualStageEnd = 0;
                manualReplacementAttempt = true;
                manualLastProgressPosition = local.Position;
                manualLastProgressUtc = now;
                Emit(
                    "navigation_recovery_stage_arrived",
                    $"destination={activeManual.DestinationId}; departure_candidate={manualDepartureCandidateIndex + 1}; position={FormatVector(local.Position)}; action=repath_destination_from_new_origin");
                manualSnapshot = BuildManualSnapshot(
                    MovementOwner.SentinelTestNav,
                    ManualRouteState.RequestingPath,
                    "Reached a local recovery stage; requesting a materially different route to the original destination.",
                    local.Position);
                return Decision(false, activeManual.ReferencePosition, NavigationPathState.RequestingPath,
                    mountDecision.State, manualSnapshot.Explanation);
            }

            var arrivedRequest = activeManual;
            if (ownsPath)
                vnav.Stop();
            ownsPath = false;
            activeManual = null;
            manualPendingPath = null;
            manualSnapshot = new ManualNavigationSnapshot(
                manualArmed, MovementOwner.None, ManualRouteState.Arrived,
                arrivedRequest.DestinationId, arrivedRequest.DestinationName,
                arrivedRequest.ReferencePosition, manualSnapped, manualPlan?.Waypoints.Count ?? manualRoute.Count,
                manualRouteCursor,
                WaypointAt(manualRouteCursor - 1), WaypointAt(manualRouteCursor), WaypointAt(manualRouteCursor + 1),
                manualStageEnd, manualRouteId, manualRouteAttempt, manualPlan?.Length ?? 0f,
                arrivalDistance, 0f, manualStuckCount, manualPathFailureCount,
                $"ARRIVED within {arrivalDistance:F1}y (3D) of the validated approach point.");
            Emit("navigation_arrived", $"destination={arrivedRequest.DestinationId}; approach={FormatVector(manualSnapped.Value)}");
            return Decision(false, manualSnapped, NavigationPathState.Idle, mountDecision.State, manualSnapshot.Explanation);
        }

        if (ownsPath && manualPlan is not null && manualStageEnd > manualRouteCursor &&
            StageReached(local.Position, manualPlan.Waypoints[manualStageEnd].Position,
                manualPlan.Waypoints[manualStageEnd].Protected, game.IsMounted))
        {
            vnav.Stop();
            ownsPath = false;
            manualRouteCursor = manualStageEnd;
            manualLastProgressPosition = local.Position;
            manualLastProgressUtc = now;
            Emit("navigation_stage_completed",
                $"destination={activeManual.DestinationId}; route={manualRouteId}; waypoint={manualRouteCursor}; position={FormatVector(manualPlan.Waypoints[manualRouteCursor].Position)}; reason={manualPlan.Waypoints[manualRouteCursor].Reason}");
        }

        if (ownsPath && vnav.IsPathRunning)
        {
            if (vnav.WaypointCount != lastReportedManualWaypointCount)
            {
                lastReportedManualWaypointCount = vnav.WaypointCount;
                Emit("navigation_waypoint_changed",
                    $"destination={activeManual.DestinationId}; route={manualRouteId}; route_cursor={manualRouteCursor}; stage_end={manualStageEnd}; vnav_remaining={vnav.WaypointCount}; previous={FormatOptional(WaypointAt(manualRouteCursor - 1))}; current={FormatOptional(WaypointAt(manualRouteCursor))}; next={FormatOptional(WaypointAt(manualRouteCursor + 1))}");
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
                failedManualCorridor = RouteComparison.FailureCorridor(
                    manualRoute, local.Position, minimumWaypointIndex: Math.Min(manualRouteCursor + 1, Math.Max(0, manualRoute.Count - 1)));
                consecutiveFailures++;
                manualStuckCount++;
                Emit("navigation_stuck",
                    $"destination={activeManual.DestinationId}; route={manualRouteId}; remaining={distance:F1}; attempt={consecutiveFailures}; player={FormatVector(local.Position)}; previous={FormatOptional(WaypointAt(manualRouteCursor - 1))}; current={FormatOptional(WaypointAt(manualRouteCursor))}; next={FormatOptional(WaypointAt(manualRouteCursor + 1))}; failed_corridor={FormatRoute(failedManualCorridor)}; nearby_nonplayer={NearbyNonPlayerEvidence(game, local.Position)}");
                vnav.Stop();
                ownsPath = false;
                manualReplacementAttempt = true;
                manualPendingPath = null;
                manualLastProgressUtc = now;
                manualPlan = null;
                manualRoute = [];
                manualRouteCursor = 0;
                manualStageEnd = 0;
                BeginManualRecovery(local.Position, battlefield.Map);
            }
            else
            {
                manualSnapshot = BuildManualSnapshot(MovementOwner.SentinelTestNav, ManualRouteState.Following,
                    $"Following the generated vnavmesh route; {distance:F1}y remain.", local.Position);
                return Decision(true, manualSnapped, NavigationPathState.FollowingPath, mountDecision.State, manualSnapshot.Explanation);
            }
        }
        else if (ownsPath && now - manualPathStartedUtc > TimeSpan.FromSeconds(1.5))
        {
            consecutiveFailures++;
            manualStuckCount++;
            failedManualCorridor = RouteComparison.FailureCorridor(
                manualRoute, local.Position, minimumWaypointIndex: Math.Min(manualRouteCursor + 1, Math.Max(0, manualRoute.Count - 1)));
            Emit("navigation_stuck",
                $"destination={activeManual.DestinationId}; route={manualRouteId}; path stopped before stage arrival; attempt={consecutiveFailures}; player={FormatVector(local.Position)}; stage_target={FormatOptional(WaypointAt(manualStageEnd))}; failed_corridor={FormatRoute(failedManualCorridor)}; nearby_nonplayer={NearbyNonPlayerEvidence(game, local.Position)}");
            ownsPath = false;
            manualReplacementAttempt = true;
            manualPlan = null;
            manualRoute = [];
            manualRouteCursor = 0;
            manualStageEnd = 0;
            BeginManualRecovery(local.Position, battlefield.Map);
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
                manualPathFailureCount++;
                manualPendingPath = null;
                AdvanceManualCandidate("Path generation timed out.", manualReplacementAttempt);
            }
            else if (manualPendingPath.IsCompleted)
            {
                CompleteManualPath(local.Position, now);
            }
            if (manualPendingPath is not null)
            {
                manualSnapshot = BuildManualSnapshot(MovementOwner.SentinelTestNav, ManualRouteState.RequestingPath,
                    "Waiting for vnavmesh path generation; the player is not being steered directly.", local.Position);
                return Decision(false, manualSnapped, NavigationPathState.RequestingPath, mountDecision.State, manualSnapshot.Explanation);
            }
            if (ownsPath)
                return Decision(true, manualSnapped, NavigationPathState.PathValidated, mountDecision.State,
                    "Validated generated route submitted to vnavmesh.");
        }

        if (!ownsPath && manualPlan is not null && manualRouteCursor < manualPlan.LastIndex)
        {
            if (!StartManualStage(local.Position, now))
            {
                consecutiveFailures++;
                manualPathFailureCount++;
                failedManualCorridor = RouteComparison.FailureCorridor(
                    manualRoute, local.Position, minimumWaypointIndex: Math.Min(manualRouteCursor + 1, Math.Max(0, manualRoute.Count - 1)));
                manualReplacementAttempt = true;
                manualPlan = null;
                manualRoute = [];
                manualRouteCursor = 0;
                manualStageEnd = 0;
                AdvanceManualCandidate("vnavmesh rejected a protected route stage.", true);
            }
            else
            {
                manualSnapshot = BuildManualSnapshot(MovementOwner.SentinelTestNav, ManualRouteState.Following,
                    $"Following protected stage to waypoint {manualStageEnd}; Sentinel retains the generated route cursor.", local.Position);
                return Decision(true, manualSnapped, NavigationPathState.FollowingPath, mountDecision.State,
                    manualSnapshot.Explanation);
            }
        }

        if (!ownsPath)
        {
            if (!EnsureManualCandidateAvailable())
            {
                CancelManual("No reachable local departure stage or non-repeating approach route was found.", ManualRouteState.Failed, "navigation_failed");
                return Decision(false, null, NavigationPathState.Failed, MountState.Disabled, manualSnapshot.Explanation);
            }
            var candidate = CurrentManualCandidate();
            manualSnapped = vnav.FindNearestReachable(candidate, 12f, 8f);
            if (manualSnapped is null)
            {
                manualPathFailureCount++;
                AdvanceManualCandidate($"{CurrentCandidateLabel()} had no reachable mesh point.");
                return UpdateManual(game, battlefield, config, combatTravelActive);
            }
            if (activeManual is { MinimumApproachClearance: > 0f } guarded &&
                HorizontalDistance(manualSnapped.Value, guarded.ReferencePosition) < guarded.MinimumApproachClearance)
            {
                manualPathFailureCount++;
                AdvanceManualCandidate($"{CurrentCandidateLabel()} snapped inside the objective clearance.");
                return UpdateManual(game, battlefield, config, combatTravelActive);
            }
            if (manualRecoveryPhase == ManualRecoveryPhase.DepartureStage &&
                HorizontalDistance(local.Position, manualSnapped.Value) < 5f)
            {
                manualPathFailureCount++;
                AdvanceManualCandidate($"{CurrentCandidateLabel()} snapped less than 5y from the failed origin.", true);
                return UpdateManual(game, battlefield, config, combatTravelActive);
            }
            manualPathRequestedUtc = now;
            manualPathOrigin = local.Position;
            manualPathTicket = manualPathGate.Begin();
            manualPendingPath = vnav.FindPathAsync(local.Position, manualSnapped.Value, 2.5f);
            manualSnapshot = BuildManualSnapshot(MovementOwner.SentinelTestNav, ManualRouteState.RequestingPath,
                $"Snapped approach to {FormatVector(manualSnapped.Value)} and requested a generated route.", local.Position);
            return Decision(false, manualSnapped, NavigationPathState.RequestingPath, mountDecision.State, manualSnapshot.Explanation);
        }

        manualSnapshot = BuildManualSnapshot(MovementOwner.SentinelTestNav, ManualRouteState.Following,
            $"Following the generated vnavmesh route; {distance:F1}y remain.", local.Position);
        return Decision(true, manualSnapped, NavigationPathState.FollowingPath, mountDecision.State, manualSnapshot.Explanation);
    }

    private void CompleteManualPath(Vector3 origin, DateTime now)
    {
        var task = manualPendingPath!;
        manualPendingPath = null;
        if (!manualPathGate.IsCurrent(manualPathTicket) || activeManual is null)
            return;
        IReadOnlyList<Vector3> route;
        try { route = task.GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            consecutiveFailures++;
            manualPathFailureCount++;
            AdvanceManualCandidate($"Path generation faulted: {ex.GetType().Name}.", manualReplacementAttempt);
            return;
        }
        var validation = PathValidator.Validate(route, manualPathOrigin, manualSnapped!.Value, 2.5f);
        if (!validation.IsValid)
        {
            consecutiveFailures++;
            manualPathFailureCount++;
            AdvanceManualCandidate($"Generated path rejected: {validation.Explanation}", manualReplacementAttempt);
            return;
        }
        if (manualReplacementAttempt && RouteComparison.RepeatsFailedCorridor(failedManualCorridor, route))
        {
            manualPathFailureCount++;
            Emit("navigation_route_rejected",
                $"destination={activeManual?.DestinationId}; candidate={CurrentCandidateLabel()}; reason=repeated failed corridor; failed={FormatRoute(failedManualCorridor)}; replacement={FormatRoute(route)}");
            AdvanceManualCandidate("Replacement path repeated the failed corridor geometry.", true);
            return;
        }

        var rawRoute = route;
        var reconciled = PathPrefixTrimmer.Reconcile(route, manualPathOrigin, origin,
            now - manualPathRequestedUtc);
        if (reconciled.RepathFromCurrentPosition)
        {
            Emit("navigation_path_stale_origin",
                $"destination={activeManual.DestinationId}; reason=player-moved-off-safe-prefix; action=repath-from-current");
            return;
        }
        route = reconciled.Route;
        if (!PathValidator.Validate(route, origin, manualSnapped.Value, 2.5f).IsValid)
        {
            Emit("navigation_path_stale_origin",
                $"destination={activeManual.DestinationId}; reason=trimmed-route-invalid; action=repath-from-current");
            return;
        }

        manualPlan = RouteExecutionPlan.Build(route);
        if (manualPlan.Waypoints.Count < 2)
        {
            consecutiveFailures++;
            manualPathFailureCount++;
            manualPlan = null;
            AdvanceManualCandidate("Generated route collapsed below two distinct waypoints.");
            return;
        }
        manualRoute = route;
        manualRouteCursor = 0;
        manualStageEnd = 0;
        manualRouteAttempt++;
        manualRouteId = $"{activeManual?.DestinationId ?? "ROUTE"}-R{manualRouteAttempt:00}";
        var acceptedRecoveryPhase = manualRecoveryPhase;
        var recoveryResult = acceptedRecoveryPhase switch
        {
            ManualRecoveryPhase.DepartureStage => "local-departure-stage",
            ManualRecoveryPhase.DestinationRetry => "destination-from-recovery-stage",
            _ when manualReplacementAttempt => "replacement-materially-different",
            _ => "initial",
        };
        manualReplacementAttempt = false;
        if (acceptedRecoveryPhase == ManualRecoveryPhase.DestinationRetry)
            ResetManualRecovery();
        lastReportedManualWaypointCount = -1;
        Emit("navigation_path_ready",
            $"destination={activeManual?.DestinationId}; route={manualRouteId}; attempt={manualRouteAttempt}; candidate={CurrentCandidateLabel(acceptedRecoveryPhase)}; recovery={recoveryResult}; origin={FormatVector(origin)}; endpoint={FormatVector(manualSnapped!.Value)}; raw_waypoints={rawRoute.Count}; trimmed_prefix={reconciled.RemovedPoints}; execution_waypoints={manualPlan.Waypoints.Count}; length={manualPlan.Length:F1}; raw_geometry={FormatRoute(rawRoute, 128)}; execution_geometry={manualPlan.FormatGeometry()}");
    }

    private void CompleteDynamicReplacement(Vector3 current, DateTime now, Configuration config)
    {
        if (dynamicReplacementPath is null)
            return;
        if (now - dynamicReplacementRequestedUtc >
            TimeSpan.FromSeconds(Math.Clamp(config.PathRequestTimeoutSeconds, 2f, 30f)))
        {
            ClearDynamicReplacement();
            Emit("navigation_dynamic_retarget_rejected", "reason=timeout; old_route_retained=true");
            return;
        }
        if (!dynamicReplacementPath.IsCompleted)
            return;
        var task = dynamicReplacementPath;
        var request = dynamicReplacementRequest;
        var snapped = dynamicReplacementSnap;
        var origin = dynamicReplacementOrigin;
        var requestedAt = dynamicReplacementRequestedUtc;
        var oldRouteId = dynamicReplacementRouteId;
        var validTicket = dynamicReplacementGate.IsCurrent(dynamicReplacementTicket);
        ClearDynamicReplacement();
        if (request is null || snapped is null ||
            !DynamicRouteHandoffPolicy.MayReplace(validTicket, ownsPath,
                manualReplacementAttempt || manualRecoveryPhase != ManualRecoveryPhase.None,
                oldRouteId, manualRouteId, request.DestinationId, activeManual?.DestinationId))
            return;
        IReadOnlyList<Vector3> generated;
        try { generated = task.GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            Emit("navigation_dynamic_retarget_rejected",
                $"reason={ex.GetType().Name}; old_route_retained=true");
            return;
        }
        if (!PathValidator.Validate(generated, origin, snapped.Value, 2.5f).IsValid)
        {
            Emit("navigation_dynamic_retarget_rejected", "reason=invalid-generated-route; old_route_retained=true");
            return;
        }
        var reconciled = PathPrefixTrimmer.Reconcile(generated, origin, current, now - requestedAt);
        if (reconciled.RepathFromCurrentPosition ||
            !PathValidator.Validate(reconciled.Route, current, snapped.Value, 2.5f).IsValid)
        {
            Emit("navigation_dynamic_retarget_rejected", "reason=stale-origin; old_route_retained=true");
            return;
        }
        var plan = RouteExecutionPlan.Build(reconciled.Route);
        if (plan.Waypoints.Count < 2)
        {
            Emit("navigation_dynamic_retarget_rejected", "reason=short-route; old_route_retained=true");
            return;
        }

        // Only now release the old route. Stage execution below uses exactly
        // the same protected geometry and bounded stuck recovery as acquisition.
        if (ownsPath)
            vnav.Stop();
        ownsPath = false;
        manualPathGate.Invalidate();
        manualPendingPath = null;
        activeManual = request;
        manualCandidates = request.Candidates().ToList();
        manualCandidateIndex = 0;
        ResetManualRecovery();
        manualSnapped = snapped;
        manualPlan = plan;
        manualRoute = reconciled.Route;
        manualRouteCursor = 0;
        manualStageEnd = 0;
        manualReplacementAttempt = false;
        manualRouteAttempt++;
        manualRouteId = $"{request.DestinationId}-R{manualRouteAttempt:00}";
        manualLastProgressPosition = current;
        manualLastProgressUtc = now;
        Emit("navigation_dynamic_retarget_ready",
            $"destination={request.DestinationId}; route={manualRouteId}; length={plan.Length:F1}; trimmed_prefix={reconciled.RemovedPoints}; old_route_replaced=true");
    }

    private void ClearDynamicReplacement()
    {
        dynamicReplacementGate.Invalidate();
        dynamicReplacementPath = null;
        dynamicReplacementRequest = null;
        dynamicReplacementSnap = null;
        dynamicReplacementRouteId = "NONE";
    }

    private bool StartManualStage(Vector3 currentPosition, DateTime now)
    {
        if (manualPlan is null || manualRouteCursor >= manualPlan.LastIndex)
            return false;
        manualStageEnd = manualPlan.FindStageEnd(manualRouteCursor);
        var stage = manualPlan.BuildStage(currentPosition, manualRouteCursor, manualStageEnd);
        if (stage.Count < 2 || !vnav.StartPath(stage, manualPlan.Waypoints[manualStageEnd].Protected ? 0.75f : 1.5f))
            return false;

        ownsPath = true;
        manualPathStartedUtc = now;
        manualLastProgressUtc = now;
        manualLastProgressPosition = currentPosition;
        lastReportedManualWaypointCount = -1;
        Emit("navigation_stage_started",
            $"destination={activeManual?.DestinationId}; route={manualRouteId}; cursor={manualRouteCursor}; stage_end={manualStageEnd}; target={FormatVector(manualPlan.Waypoints[manualStageEnd].Position)}; protected={manualPlan.Waypoints[manualStageEnd].Protected}; reason={manualPlan.Waypoints[manualStageEnd].Reason}; submitted={FormatRoute(stage)}");
        return true;
    }

    private void AdvanceManualCandidate(string reason, bool preserveFailureComparison = false)
    {
        manualPathGate.Invalidate();
        Emit("navigation_path_failed",
            $"destination={activeManual?.DestinationId ?? manualSnapshot.DestinationId}; candidate={CurrentCandidateLabel()}; reason={reason}");
        if (manualRecoveryPhase == ManualRecoveryPhase.DepartureStage)
            manualDepartureCandidateIndex++;
        else
            manualCandidateIndex++;
        manualSnapped = null;
        lastReportedManualWaypointCount = -1;
        manualPendingPath = null;
        manualReplacementAttempt = preserveFailureComparison;
        developmentLog.Changed("manual-nav-candidate", $"{manualCandidateIndex}|{reason}", reason);
    }

    private void BeginManualRecovery(Vector3 currentPosition, FrontlineMap map)
    {
        manualDepartureCandidates = RouteComparison.DepartureAnchors(currentPosition, failedManualCorridor).ToList();
        var observedExit = WorqorCentralRecovery.PreferredDeparture(map, currentPosition, failedManualCorridor);
        if (observedExit is { } exit)
            manualDepartureCandidates.Insert(0, exit);
        manualDepartureCandidateIndex = 0;
        manualCandidateIndex = 0;
        manualRecoveryPhase = ManualRecoveryPhase.DepartureStage;
        manualSnapped = null;
        manualPendingPath = null;
        manualReplacementAttempt = true;
        Emit(
            "navigation_recovery_started",
            $"destination={activeManual?.DestinationId}; origin={FormatVector(currentPosition)}; strategy={(observedExit is null ? "generic" : "observed-worqor-east-exit")}; candidates={FormatRoute(manualDepartureCandidates)}; failed_corridor={FormatRoute(failedManualCorridor)}");
    }

    private bool EnsureManualCandidateAvailable()
    {
        if (manualRecoveryPhase == ManualRecoveryPhase.DepartureStage)
            return manualDepartureCandidateIndex < manualDepartureCandidates.Count;

        if (manualCandidateIndex < manualCandidates.Count)
            return true;

        if (manualRecoveryPhase != ManualRecoveryPhase.DestinationRetry ||
            manualDepartureCandidateIndex + 1 >= manualDepartureCandidates.Count)
            return false;

        manualDepartureCandidateIndex++;
        manualCandidateIndex = 0;
        manualRecoveryPhase = ManualRecoveryPhase.DepartureStage;
        manualSnapped = null;
        manualReplacementAttempt = true;
        Emit(
            "navigation_recovery_stage_advanced",
            $"destination={activeManual?.DestinationId}; departure_candidate={manualDepartureCandidateIndex + 1}; reason=all destination approaches repeated or failed");
        return true;
    }

    private Vector3 CurrentManualCandidate() => manualRecoveryPhase == ManualRecoveryPhase.DepartureStage
        ? manualDepartureCandidates[manualDepartureCandidateIndex]
        : manualCandidates[manualCandidateIndex];

    private string CurrentCandidateLabel(ManualRecoveryPhase? phaseOverride = null)
    {
        var phase = phaseOverride ?? manualRecoveryPhase;
        return phase == ManualRecoveryPhase.DepartureStage
            ? $"departure-{manualDepartureCandidateIndex + 1}"
            : $"approach-{manualCandidateIndex + 1}";
    }

    private void ResetManualRecovery()
    {
        manualDepartureCandidates = [];
        manualDepartureCandidateIndex = 0;
        manualRecoveryPhase = ManualRecoveryPhase.None;
    }

    private void CancelManual(string reason, ManualRouteState state, string eventName)
    {
        ClearDynamicReplacement();
        manualPathGate.Invalidate();
        var id = activeManual?.DestinationId ?? manualRequest?.DestinationId ?? manualSnapshot.DestinationId;
        if (ownsPath)
            vnav.Stop();
        ownsPath = false;
        manualRequest = null;
        activeManual = null;
        manualPendingPath = null;
        manualCandidates.Clear();
        ResetManualRecovery();
        manualRoute = [];
        failedManualCorridor = [];
        manualPlan = null;
        manualRouteCursor = 0;
        manualStageEnd = 0;
        manualRouteId = "NONE";
        manualSnapped = null;
        manualYieldedToCombat = false;
        manualCombatTravelReported = false;
        manualSnapshot = new ManualNavigationSnapshot(
            manualArmed, MovementOwner.None, state, id, "None", null, null,
            0, 0, null, null, null, 0, "NONE", manualRouteAttempt, 0f,
            0f, 0f, manualStuckCount, manualPathFailureCount, reason);
        Emit(eventName, $"destination={id}; reason={reason}");
    }

    private ManualNavigationSnapshot BuildManualSnapshot(
        MovementOwner owner,
        ManualRouteState state,
        string explanation,
        Vector3? playerPosition)
    {
        var request = activeManual ?? manualRequest;
        var waypointCount = manualPlan?.Waypoints.Count ?? manualRoute.Count;
        var next = WaypointAt(manualRouteCursor + 1);
        var remaining = playerPosition is not null && manualPlan is not null
            ? manualPlan.RemainingLength(playerPosition.Value, manualRouteCursor)
            : playerPosition is not null && manualSnapped is not null
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
            waypointCount,
            manualRouteCursor,
            WaypointAt(manualRouteCursor - 1),
            WaypointAt(manualRouteCursor),
            next,
            manualStageEnd,
            manualRouteId,
            manualRouteAttempt,
            manualPlan?.Length ?? 0f,
            remaining,
            progressAge,
            manualStuckCount,
            manualPathFailureCount,
            explanation);
    }

    private void Emit(string name, string detail)
    {
        manualEvents.Enqueue(new ManualNavigationEvent(name, detail, DateTime.UtcNow));
        developmentLog.Changed($"manual-{name}", detail, $"{name}: {detail}");
    }

    private void ReportManualMountDecision(
        MountDecision decision,
        float distance,
        int nearbyEnemies,
        GameStateSnapshot game)
    {
        var signature = $"{decision.State}|{game.IsMounted}|{game.IsMounting}";
        if (signature == manualMountSignature)
            return;

        manualMountSignature = signature;
        Emit(
            "navigation_mount_state_changed",
            $"destination={activeManual?.DestinationId ?? manualRequest?.DestinationId}; state={decision.State}; mounted={game.IsMounted}; mounting={game.IsMounting}; route_remaining={distance:F1}; nearby_enemies={nearbyEnemies}; reason={decision.Explanation}");
    }

    private NavigationDecision PauseManualForCombat(
        GameStateSnapshot game,
        BattlefieldState battlefield,
        CombatDecision combat,
        Configuration config)
    {
        var localPosition = game.LocalPlayer?.Position;
        var nearbyThreats = localPosition is null
            ? 0
            : CountNear(game.Enemies, localPosition.Value, config.MountEnemySafetyRadius);
        if (!manualYieldedToCombat)
        {
            if (ownsPath)
                vnav.Stop();
            ownsPath = false;
            manualPendingPath = null;
            manualPathGate.Invalidate();
            manualSnapped = null;
            manualRoute = [];
            failedManualCorridor = [];
            manualPlan = null;
            manualRouteCursor = 0;
            manualStageEnd = 0;
            manualCandidateIndex = 0;
            ResetManualRecovery();
            manualReplacementAttempt = false;
            consecutiveFailures = 0;
            lastReportedManualWaypointCount = -1;
            manualYieldedToCombat = true;
            Emit(
                "navigation_yielded_external_combat",
                $"destination={activeManual?.DestinationId ?? manualRequest?.DestinationId}; route_preserved=true; battlefield_block={battlefield.Combat.BlocksMovement}; provider_yield={combat.YieldNavigation}; reason={battlefield.Combat.Evidence} {combat.Explanation}");
        }

        var dismount = mount.Update(game, false, true, nearbyThreats, config);
        ReportManualMountDecision(dismount, 0f, nearbyThreats, game);
        manualSnapshot = BuildManualSnapshot(
            MovementOwner.ExternalCombat,
            ManualRouteState.YieldedExternalCombat,
            $"Combat owns movement; destination {activeManual?.DestinationId ?? manualRequest?.DestinationId} is preserved and will be repathed after Reborn's targeting-threat quiet period. {combat.Explanation}",
            localPosition);
        return Decision(
            false,
            activeManual?.ReferencePosition ?? manualRequest?.ReferencePosition,
            NavigationPathState.YieldingToCombat,
            dismount.State,
            manualSnapshot.Explanation);
    }

    private void ResumeManualAfterCombat(Vector3? localPosition)
    {
        manualPathGate.Invalidate();
        manualYieldedToCombat = false;
        var request = manualRequest ?? activeManual;
        if (request is not null)
        {
            manualCandidates = request.Candidates().ToList();
        }
        manualCandidateIndex = 0;
        ResetManualRecovery();
        manualSnapped = null;
        manualPendingPath = null;
        manualRoute = [];
        failedManualCorridor = [];
        manualPlan = null;
        manualRouteCursor = 0;
        manualStageEnd = 0;
        manualReplacementAttempt = false;
        consecutiveFailures = 0;
        manualLastProgressPosition = localPosition;
        manualLastProgressUtc = DateTime.UtcNow;
        manualMountSignature = string.Empty;
        Emit(
            "navigation_resumed_after_combat",
            $"destination={request?.DestinationId}; previous_route_discarded=true; action=repath_from_current_position");
    }

    private void CompletePathRequest(Vector3 origin, Vector3 destination, DateTime now)
    {
        var task = pendingPath!;
        var requested = pendingDestination;
        pendingPath = null;
        pendingDestination = null;

        if (!strategicPathGate.IsCurrent(strategicPathTicket) ||
            requested is null || HorizontalDistance(requested.Value, destination) > 1f)
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

        var validation = PathValidator.Validate(path, pendingOrigin, destination, 2.5f);
        if (!validation.IsValid)
        {
            RegisterFailure(now, $"Generated path rejected: {validation.Explanation}");
            return;
        }

        var reconciled = PathPrefixTrimmer.Reconcile(path, pendingOrigin, origin, now - pathRequestedAtUtc);
        if (reconciled.RepathFromCurrentPosition ||
            !PathValidator.Validate(reconciled.Route, origin, destination, 2.5f).IsValid)
        {
            developmentLog.Changed("nav-path-state", "stale-origin",
                "Player moved beyond the safe generated prefix; requesting a route from the current position.");
            return;
        }
        path = reconciled.Route;

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
        strategicPathGate.Invalidate();
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
        players.Count(player =>
            !player.IsDead &&
            player.CurrentHp > 0 &&
            player.IsTargetable &&
            HorizontalDistance(player.Position, origin) <= radius);

    private static string NearbyNonPlayerEvidence(GameStateSnapshot game, Vector3 origin)
    {
        var observations = game.ObjectiveObservations
            .Where(item => HorizontalDistance(item.Position, origin) <= 15f &&
                           Math.Abs(item.Position.Y - origin.Y) <= 6f)
            .OrderBy(item => HorizontalDistance(item.Position, origin))
            .Take(6)
            .Select(item => $"{item.ObjectKind}/{item.BaseId}@{FormatVector(item.Position)}:targetable={item.IsTargetable}");
        return $"[{string.Join(',', observations)}]";
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    private Vector3? WaypointAt(int index) => manualPlan is not null && index >= 0 && index < manualPlan.Waypoints.Count
        ? manualPlan.Waypoints[index].Position
        : null;

    private static bool StageReached(
        Vector3 player,
        Vector3 target,
        bool protectedWaypoint,
        bool isMounted)
    {
        var horizontalTolerance = protectedWaypoint ? 0.9f : 1.5f;
        var verticalTolerance = protectedWaypoint ? 1.25f : 2f;
        return ManualNavigationPolicy.HasReachedGeneratedPoint(
            player, target, horizontalTolerance, verticalTolerance, isMounted);
    }

    private static string FormatOptional(Vector3? value) => value is { } point ? FormatVector(point) : "NONE";

    private static string FormatRoute(IReadOnlyList<Vector3> route, int maximumPoints = 64)
    {
        var points = route.Take(maximumPoints).Select((point, index) => $"{index}:{FormatVector(point)}");
        var suffix = route.Count > maximumPoints ? $",...+{route.Count - maximumPoints}" : string.Empty;
        return $"[{string.Join(',', points)}{suffix}]";
    }

    private static string FormatVector(Vector3 value) => string.Create(
        CultureInfo.InvariantCulture, $"({value.X:F1}, {value.Y:F1}, {value.Z:F1})");

    private enum ManualRecoveryPhase
    {
        None,
        DepartureStage,
        DestinationRetry,
    }
}
