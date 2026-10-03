using System.Numerics;
using PvPSentinel.FrontlineCore;
using PvPSentinel.Models;
using PvPSentinel.Navigation;

namespace PvPSentinel.Strategy;

internal sealed record ShatterGroupPlan(string DestinationId, string DestinationName,
    Vector3 Position, IReadOnlyList<Vector3> ApproachAnchors,
    bool IncludeReferencePosition = true, float MinimumApproachClearance = 0f);

/// <summary>
/// Supervised, opt-in Shatter pilot. It commits to one fresh active or soon-activating ice
/// with allied support, or to a visible field group. The manual route engine
/// retains ownership until arrival, death, STOP, or a bounded failure.
/// </summary>
internal sealed class ShatterGroupPilot
{
    private readonly Queue<ManualNavigationEvent> events = [];
    private string? committedId;
    private string? lastArrivedId;
    private DateTime lastArrivalUtc = DateTime.MinValue;
    private string? lastRetiredId;
    private DateTime lastRetiredUtc = DateTime.MinValue;
    private DateTime? invalidSinceUtc;
    private DateTime selectAfterUtc = DateTime.MinValue;
    private bool manualOverride;
    private bool pausedAfterFailure;
    private string lastWaitingReason = string.Empty;
    private DateTime lastWaitingEventUtc = DateTime.MinValue;

    public string Status { get; private set; } = "Off";
    public string? CommittedDestinationId => committedId;
    public string? CancelDestinationId { get; private set; }
    public bool HoldingAfterArrival(DateTime now) => lastArrivedId is not null && now < selectAfterUtc;
    public string? RetiredDestinationId { get; private set; }

    public IReadOnlyList<ManualNavigationEvent> DrainEvents()
    {
        var result = events.ToArray();
        events.Clear();
        return result;
    }

    public ShatterGroupPlan? Update(GameStateSnapshot game, BattlefieldState battlefield,
        IReadOnlyList<FriendlyCluster> clusters, ManualNavigationSnapshot route,
        bool enabled, bool rebornReady, bool meshReady, FieldGroupChoice? trackedFieldGroup = null,
        bool allowGroupFallback = true)
    {
        var now = game.CapturedAtUtc;
        RetiredDestinationId = null;
        CancelDestinationId = null;
        if (game.FrontlineMap != FrontlineMap.FieldsOfGlory ||
            battlefield.Match.Lifecycle is FrontlineMatchLifecycle.Outside or FrontlineMatchLifecycle.Results)
        {
            Reset();
            return null;
        }
        if (!enabled)
        {
            Reset();
            return null;
        }
        // A death retires the commitment even if another readiness sensor is lost
        // on the same frame. The navigation controller cancels its route below.
        if (game.LocalPlayer?.IsDead == true)
        {
            committedId = null;
            invalidSinceUtc = null;
            manualOverride = false;
            selectAfterUtc = now.AddSeconds(4);
            Status = "Dead; will choose a supported icebound tomelith or field group after respawn";
            return null;
        }
        if (battlefield.Match.Lifecycle != FrontlineMatchLifecycle.MatchActive)
        {
            WaitFor("Waiting for active match", now);
            return null;
        }
        if (game.LocalPlayer is null || !game.IsClassificationReliable)
        {
            WaitFor($"Waiting for reliable team: {game.ClassificationReliabilityExplanation}", now);
            return null;
        }
        // A destroyed/expired ice is no longer a useful destination. Require a
        // stable marker transition so a single missing/flickering frame cannot
        // interrupt a committed route. A stale unseen marker needs a longer hold.
        if (committedId is { } iceId && iceId != "SHATTER-REGROUP" &&
            route.DestinationId == iceId && RouteInProgress(route))
        {
            var objective = battlefield.Objectives.FirstOrDefault(item => item.LogicalId == iceId);
            var fresh = objective?.LastSeenUtc is { } seen && now - seen <= TimeSpan.FromSeconds(3);
            var expired = fresh && (objective!.State is ObjectiveLifecycle.Inactive or ObjectiveLifecycle.Deactivated ||
                                    objective.StrengthPercent is 0 ||
                                    objective.PhysicalConfirmation is { CurrentHp: 0 });
            var stale = objective?.LastSeenUtc is { } lastSeen && now - lastSeen > TimeSpan.FromSeconds(8);
            if (expired || stale)
            {
                invalidSinceUtc ??= now;
                if (now - invalidSinceUtc.Value >= TimeSpan.FromSeconds(expired ? 2 : 3))
                {
                    RetiredDestinationId = iceId;
                    committedId = null;
                    invalidSinceUtc = null;
                    lastRetiredId = iceId;
                    lastRetiredUtc = now;
                    selectAfterUtc = now.AddSeconds(2);
                    Event("shatter_group_objective_retired",
                        $"destination={iceId}; reason={(expired ? "inactive-or-depleted" : "marker-stale")}; hold=2s", now);
                    Status = "Ice ended; choosing another supported destination shortly";
                    return null;
                }
            }
            else invalidSinceUtc = null;
        }
        else invalidSinceUtc = null;
        if (!rebornReady || !meshReady)
        {
            if (committedId is { } unsafeDestination)
            {
                CancelDestinationId = unsafeDestination;
                committedId = null;
                selectAfterUtc = now.AddSeconds(4);
            }
            WaitFor(!rebornReady ? "Waiting for combat provider readiness" : "Waiting for vnavmesh", now);
            return null;
        }

        if (committedId is { } current)
        {
            if (route.DestinationId != current && RouteInProgress(route))
            {
                committedId = null;
                invalidSinceUtc = null;
                manualOverride = true;
                Status = "Manual destination has priority";
                return null;
            }
            if (route.DestinationId == current && route.State == ManualRouteState.Arrived)
            {
                committedId = null;
                invalidSinceUtc = null;
                lastArrivedId = current;
                lastArrivalUtc = now;
                selectAfterUtc = now.AddSeconds(8);
                Event("shatter_group_arrived", $"destination={current}; hold=8s", now);
            }
            else if (route.DestinationId == current &&
                     route.State is ManualRouteState.Failed or ManualRouteState.Cancelled)
            {
                committedId = null;
                pausedAfterFailure = true;
                Event("shatter_group_paused", $"destination={current}; reason=route-ended; restart=next-match-or-toggle", now);
            }
            else
            {
                Status = $"Committed to {current} until arrival or death";
                return null;
            }
        }

        if (pausedAfterFailure)
        {
            Status = "Paused after route failure; toggle mode or wait for next match";
            return null;
        }
        if (RouteInProgress(route))
        {
            manualOverride = true;
            Status = "Manual destination has priority";
            return null;
        }
        if (manualOverride)
        {
            manualOverride = false;
            selectAfterUtc = now.AddSeconds(8);
        }
        if (now < selectAfterUtc)
        {
            Status = "Holding before choosing the next destination";
            return null;
        }

        var localPosition = game.LocalPlayer.Position;
        var supported = battlefield.Objectives
            .Where(objective => objective.ReferencePosition is not null &&
                ShatterIceCandidate(objective) &&
                objective.LastSeenUtc is { } seen && now - seen <= TimeSpan.FromSeconds(3) &&
                (objective.LogicalId != lastArrivedId || now - lastArrivalUtc >= TimeSpan.FromSeconds(60)) &&
                (objective.LogicalId != lastRetiredId || now - lastRetiredUtc >= TimeSpan.FromSeconds(20)) &&
                objective.NearbyEnemies <= objective.NearbyAllies + 2)
            .Select(objective =>
            {
                var position = objective.ReferencePosition!.Value;
                var group = clusters
                    .Where(cluster => cluster.PlayerCount >= 3 && Distance(cluster.Center, position) <= 65f)
                    .OrderByDescending(cluster => cluster.PlayerCount)
                    .FirstOrDefault();
                var groupDistance = group is null ? float.MaxValue : Distance(group.Center, position);
                var heading = group is null ? 0f : HeadingToward(group, position);
                var score = group is null ? float.NegativeInfinity :
                    group.PlayerCount * 25f + objective.NearbyAllies * 8f - objective.NearbyEnemies * 24f +
                    (objective.Kind == "LARGE" ? 20f : 0f) -
                    groupDistance * 0.3f + heading * 12f - Distance(localPosition, position) * 0.13f;
                return new { Objective = objective, Group = group, GroupDistance = groupDistance, Heading = heading, Score = score };
            })
            .Where(candidate => candidate.Group is not null &&
                (candidate.GroupDistance <= 35f || candidate.Heading >= 0.25f))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Objective.LogicalId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (supported is not null)
        {
            var objective = supported.Objective;
            committedId = objective.LogicalId;
            Event("shatter_group_selected",
                $"destination={committedId}; state={objective.State}; eta={objective.ActivationEtaSeconds?.ToString() ?? "n/a"}; group={supported.Group!.PlayerCount}; allies={objective.NearbyAllies}; enemies={objective.NearbyEnemies}; commitment=arrival-or-death", now);
            Status = $"Committed to {committedId} until arrival or death";
            return new ShatterGroupPlan(committedId, objective.DisplayName,
                objective.ReferencePosition!.Value,
                ShatterApproachPolicy.Anchors(objective.ReferencePosition.Value, localPosition, objective.Kind),
                false, ShatterApproachPolicy.MinimumClearance(objective.Kind));
        }

        if (!allowGroupFallback)
        {
            WaitFor("No supported ice; shared dynamic field-group follower may run", now);
            return null;
        }
        // Group travel is a bounded leg to its current position, not pursuit of
        // moving players. Wait until the group has left the spawn/local area.
        var fieldGroup = trackedFieldGroup.HasValue ? trackedFieldGroup.Value.Cluster : clusters
            .Select(group => new { Group = group, Distance = Distance(localPosition, group.Center) })
            .Where(item => item.Group.PlayerCount >= 3 && item.Distance is >= 35f and <= 400f)
            .OrderByDescending(item => item.Group.PlayerCount * 8f - item.Distance * 0.15f)
            .ThenBy(item => item.Distance)
            .FirstOrDefault()?.Group;
        if (fieldGroup is null)
        {
            WaitFor("Waiting for a supported icebound tomelith or visible field group", now);
            return null;
        }
        committedId = "SHATTER-REGROUP";
        var groupPosition = trackedFieldGroup?.Destination ?? fieldGroup.Center;
        Event("shatter_group_regroup_selected",
            $"destination={committedId}; group={fieldGroup.PlayerCount}; distance={Distance(localPosition, groupPosition):F1}; commitment=arrival-or-death", now);
        Status = "Committed to allied field group until arrival or death";
        return new ShatterGroupPlan(committedId, "Allied field group", groupPosition, []);
    }

    private static bool ShatterIceCandidate(MapObjectiveState objective) =>
        objective.Confidence >= SensorConfidence.LiveVerifiedMapping &&
        objective.StrengthPercent is null or > 0 &&
        (objective.PhysicalConfirmation is null or { CurrentHp: > 0 }) &&
        ((objective.State == ObjectiveLifecycle.Active &&
          objective.StateId is 60902 or 60904) ||
         (objective.State == ObjectiveLifecycle.Preactivating &&
          objective.StateId is 60989 or 60990 &&
          objective.ActivationEtaSeconds is >= 0 and <= 25));

    private static bool RouteInProgress(ManualNavigationSnapshot route) =>
        route.DestinationId != "NONE" && route.State is
            ManualRouteState.Snapping or ManualRouteState.WaitingToMount or
            ManualRouteState.RequestingPath or ManualRouteState.Following or
            ManualRouteState.YieldedExternalCombat;

    private void Reset()
    {
        committedId = null;
        lastArrivedId = null;
        lastArrivalUtc = DateTime.MinValue;
        lastRetiredId = null;
        lastRetiredUtc = DateTime.MinValue;
        invalidSinceUtc = null;
        RetiredDestinationId = null;
        selectAfterUtc = DateTime.MinValue;
        manualOverride = false;
        pausedAfterFailure = false;
        lastWaitingReason = string.Empty;
        lastWaitingEventUtc = DateTime.MinValue;
        Status = "Off";
    }

    private void Event(string name, string detail, DateTime now) =>
        events.Enqueue(new ManualNavigationEvent(name, detail, now));

    private void WaitFor(string reason, DateTime now)
    {
        Status = reason;
        if (lastWaitingReason == reason && now - lastWaitingEventUtc < TimeSpan.FromSeconds(30))
            return;
        lastWaitingReason = reason;
        lastWaitingEventUtc = now;
        Event("shatter_group_waiting", reason, now);
    }

    private static float Distance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    private static float HeadingToward(FriendlyCluster cluster, Vector3 destination)
    {
        var movement = new Vector2(cluster.MovementTrend.X, cluster.MovementTrend.Z);
        var target = new Vector2(destination.X - cluster.Center.X, destination.Z - cluster.Center.Z);
        return movement.Length() < 0.75f || target.Length() < 1f ? 0f :
            Vector2.Dot(Vector2.Normalize(movement), Vector2.Normalize(target));
    }
}
