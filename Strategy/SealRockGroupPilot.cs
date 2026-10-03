using System.Numerics;
using PvPSentinel.FrontlineCore;
using PvPSentinel.Models;
using PvPSentinel.Navigation;

namespace PvPSentinel.Strategy;

internal sealed record SealRockGroupPlan(string DestinationId, string DestinationName,
    Vector3 Position, IReadOnlyList<Vector3> ApproachAnchors);

/// <summary>
/// Supervised, opt-in Seal Rock pilot. It commits to one fresh neutral tomelith
/// with allied support, or to a visible field group. The manual route engine
/// retains ownership until arrival, death, STOP, or a bounded failure.
/// </summary>
internal sealed class SealRockGroupPilot
{
    private readonly Queue<ManualNavigationEvent> events = [];
    private string? committedId;
    private string? lastArrivedId;
    private DateTime lastArrivalUtc = DateTime.MinValue;
    private DateTime selectAfterUtc = DateTime.MinValue;
    private bool manualOverride;
    private bool pausedAfterFailure;
    private DateTime? invalidSinceUtc;
    private string lastWaitingReason = string.Empty;
    private DateTime lastWaitingEventUtc = DateTime.MinValue;

    public string Status { get; private set; } = "Off";
    public string? CommittedDestinationId => committedId;
    public string? CancelDestinationId { get; private set; }
    public bool HoldingAfterArrival(DateTime now) => lastArrivedId is not null && now < selectAfterUtc;

    public IReadOnlyList<ManualNavigationEvent> DrainEvents()
    {
        var result = events.ToArray();
        events.Clear();
        return result;
    }

    public SealRockGroupPlan? Update(GameStateSnapshot game, BattlefieldState battlefield,
        IReadOnlyList<FriendlyCluster> clusters, ManualNavigationSnapshot route,
        bool enabled, bool rebornReady, bool meshReady, FieldGroupChoice? trackedFieldGroup = null,
        bool allowGroupFallback = true)
    {
        var now = game.CapturedAtUtc;
        CancelDestinationId = null;
        if (game.FrontlineMap != FrontlineMap.SealRock ||
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
            manualOverride = false;
            selectAfterUtc = now.AddSeconds(4);
            Status = "Dead; will choose a supported tomelith or field group after respawn";
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
        if (!rebornReady || !meshReady)
        {
            WaitFor(!rebornReady ? "Waiting for combat provider readiness" : "Waiting for vnavmesh", now);
            return null;
        }

        if (committedId is { } objectiveId && objectiveId != "SR-REGROUP" &&
            route.DestinationId == objectiveId && RouteInProgress(route))
        {
            var selected = battlefield.Objectives.FirstOrDefault(item => item.LogicalId == objectiveId);
            var supported = selected?.ReferencePosition is { } position &&
                clusters.Any(cluster => cluster.PlayerCount >= 3 && Distance(cluster.Center, position) <= 65f);
            var valid = selected?.LastSeenUtc is { } seen && now - seen <= TimeSpan.FromSeconds(3) &&
                selected.State == ObjectiveLifecycle.Active && selected.Owner == ObjectiveOwner.Neutral && supported;
            invalidSinceUtc = valid ? null : invalidSinceUtc ?? now;
            if (invalidSinceUtc is { } invalidSince && now - invalidSince >= TimeSpan.FromSeconds(3))
            {
                CancelDestinationId = objectiveId;
                committedId = null;
                lastArrivedId = objectiveId;
                lastArrivalUtc = now;
                invalidSinceUtc = null;
                Event("seal_rock_objective_retired", $"destination={objectiveId}; reason=stale-or-unsupported", now);
                Status = "Tomelith no longer supported; returning to field group";
                return null;
            }
        }
        else invalidSinceUtc = null;

        if (committedId is { } current)
        {
            if (route.DestinationId != current && RouteInProgress(route))
            {
                committedId = null;
                manualOverride = true;
                Status = "Manual destination has priority";
                return null;
            }
            if (route.DestinationId == current && route.State == ManualRouteState.Arrived)
            {
                committedId = null;
                lastArrivedId = current;
                lastArrivalUtc = now;
                selectAfterUtc = now.AddSeconds(8);
                Event("seal_rock_group_arrived", $"destination={current}; hold=8s", now);
            }
            else if (route.DestinationId == current &&
                     route.State is ManualRouteState.Failed or ManualRouteState.Cancelled)
            {
                committedId = null;
                pausedAfterFailure = true;
                Event("seal_rock_group_paused", $"destination={current}; reason=route-ended; restart=next-match-or-toggle", now);
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
                objective.State == ObjectiveLifecycle.Active &&
                objective.Owner == ObjectiveOwner.Neutral &&
                objective.LastSeenUtc is { } seen && now - seen <= TimeSpan.FromSeconds(3) &&
                (objective.LogicalId != lastArrivedId || now - lastArrivalUtc >= TimeSpan.FromSeconds(60)) &&
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
                    group.PlayerCount * 25f + objective.NearbyAllies * 8f - objective.NearbyEnemies * 24f -
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
            Event("seal_rock_group_selected",
                $"destination={committedId}; state=active-neutral; group={supported.Group!.PlayerCount}; allies={objective.NearbyAllies}; enemies={objective.NearbyEnemies}; commitment=arrival-or-death", now);
            Status = $"Committed to {committedId} until arrival or death";
            return new SealRockGroupPlan(committedId, objective.DisplayName,
                objective.ReferencePosition!.Value,
                objective.ValidatedApproachAnchors.Select(anchor => anchor.Position).ToArray());
        }

        if (!allowGroupFallback)
        {
            WaitFor("No supported tomelith; shared dynamic field-group follower may run", now);
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
            WaitFor("Waiting for a supported neutral tomelith or visible field group", now);
            return null;
        }
        committedId = "SR-REGROUP";
        var groupPosition = trackedFieldGroup?.Destination ?? fieldGroup.Center;
        Event("seal_rock_group_regroup_selected",
            $"destination={committedId}; group={fieldGroup.PlayerCount}; distance={Distance(localPosition, groupPosition):F1}; commitment=arrival-or-death", now);
        Status = "Committed to allied field group until arrival or death";
        return new SealRockGroupPlan(committedId, "Allied field group", groupPosition, []);
    }

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
        selectAfterUtc = DateTime.MinValue;
        manualOverride = false;
        pausedAfterFailure = false;
        invalidSinceUtc = null;
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
        Event("seal_rock_group_waiting", reason, now);
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
