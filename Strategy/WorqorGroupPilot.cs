using System.Numerics;
using PvPSentinel.FrontlineCore;
using PvPSentinel.Models;
using PvPSentinel.Navigation;

namespace PvPSentinel.Strategy;

internal sealed record WorqorGroupPlan(
    string DestinationId,
    string DestinationName,
    Vector3 Position,
    IReadOnlyList<Vector3> ApproachAnchors,
    string Reason);

/// <summary>
/// Opt-in Worqor destination selection. Once a supported Triumph is chosen,
/// the existing bounded manual route owns movement until arrival or death.
/// No timer, marker flicker, or new cluster can redirect an active route.
/// </summary>
internal sealed class WorqorGroupPilot
{
    private readonly Queue<ManualNavigationEvent> events = [];
    private Vector3? spawnPosition;
    private string? committedId;
    private string? lastArrivedId;
    private bool diedThisMatch;
    private bool postDeathRegroupPending;
    private DateTime lastArrivalUtc = DateTime.MinValue;
    private DateTime selectAfterUtc = DateTime.MinValue;
    private bool manualOverride;
    private bool pausedAfterFailure;
    private string lastWaitingReason = string.Empty;
    private DateTime lastWaitingEventUtc = DateTime.MinValue;

    public string Status { get; private set; } = "Off";
    public string? CommittedDestinationId => committedId;

    public IReadOnlyList<ManualNavigationEvent> DrainEvents()
    {
        var values = events.ToArray();
        events.Clear();
        return values;
    }

    public WorqorGroupPlan? Update(
        GameStateSnapshot game,
        BattlefieldState battlefield,
        IReadOnlyList<FriendlyCluster> clusters,
        ManualNavigationSnapshot route,
        bool enabled,
        bool rebornReady, FieldGroupChoice? trackedFieldGroup = null)
    {
        var now = game.CapturedAtUtc;
        if (game.FrontlineMap != FrontlineMap.WorqorChirteh ||
            battlefield.Match.Lifecycle == FrontlineMatchLifecycle.Results)
        {
            Reset(clearSpawn: true);
            return null;
        }

        if (spawnPosition is null && game.LocalPlayer is { IsDead: false } localAtSpawn &&
            battlefield.Match.Lifecycle is FrontlineMatchLifecycle.PreMatch or FrontlineMatchLifecycle.MatchActive)
            spawnPosition = localAtSpawn.Position;

        if (!enabled)
        {
            Reset(clearSpawn: false);
            return null;
        }
        // Death must retire a committed manual route even if the provider or
        // team sensor went unavailable on the same frame.
        if (game.LocalPlayer?.IsDead == true)
        {
            diedThisMatch = true;
            postDeathRegroupPending = true;
            committedId = null;
            manualOverride = false;
            selectAfterUtc = now.AddSeconds(4);
            Status = "Dead; will find a supported Triumph or allied group after respawn";
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
        if (!rebornReady)
        {
            WaitFor("Reborn autorotation is inactive; enable it after death or reconnect to resume group travel", now);
            return null;
        }

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
                if (current == "WOR-REGROUP")
                    postDeathRegroupPending = false;
                lastArrivedId = current;
                lastArrivalUtc = now;
                selectAfterUtc = now.AddSeconds(6);
                Event("worqor_group_arrived", $"destination={current}; hold=6s", now);
            }
            else if (route.DestinationId == current && route.State == ManualRouteState.Failed)
            {
                committedId = null;
                pausedAfterFailure = true;
                Event("worqor_group_paused", $"destination={current}; reason=bounded-route-failure; restart=next-match-or-toggle", now);
            }
            else if (route.DestinationId == current && route.State == ManualRouteState.Cancelled)
            {
                committedId = null;
                pausedAfterFailure = true;
                Event("worqor_group_paused", $"destination={current}; reason=route-cancelled", now);
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
            selectAfterUtc = now.AddSeconds(6);
        }
        if (now < selectAfterUtc)
        {
            Status = "Holding before choosing another group-supported Triumph";
            return null;
        }

        var firstLeg = !diedThisMatch && lastArrivedId is null && spawnPosition is not null;
        var viable = battlefield.Objectives
            .Where(objective => objective.ReferencePosition is not null &&
                objective.LastSeenUtc is { } seen && now - seen <= TimeSpan.FromSeconds(3) &&
                (objective.State == ObjectiveLifecycle.Active && objective.Owner == ObjectiveOwner.Neutral ||
                 objective.State == ObjectiveLifecycle.Preactivating && objective.ActivationEtaSeconds is >= 0 and <= 30))
            .Where(objective => objective.LogicalId != lastArrivedId ||
                now - lastArrivalUtc >= TimeSpan.FromSeconds(60))
            .Where(objective => !firstLeg || HorizontalDistance(spawnPosition!.Value, objective.ReferencePosition!.Value) <= 310f)
            .Select(objective =>
            {
                var position = objective.ReferencePosition!.Value;
                var support = clusters
                    .Where(cluster => cluster.PlayerCount >= 2 && HorizontalDistance(cluster.Center, position) <= 75f)
                    .OrderByDescending(cluster => cluster.PlayerCount)
                    .FirstOrDefault();
                var distance = HorizontalDistance(game.LocalPlayer.Position, position);
                var groupDistance = support is null ? float.MaxValue : HorizontalDistance(support.Center, position);
                var heading = support is null ? 0f : HeadingToward(support, position);
                var score = support is null ? float.NegativeInfinity :
                    support.PlayerCount * 25f + objective.NearbyAllies * 8f - objective.NearbyEnemies * 24f -
                    groupDistance * 0.28f + heading * 12f - distance * 0.12f -
                    (firstLeg ? HorizontalDistance(spawnPosition!.Value, position) * 0.08f : 0f);
                return new { Objective = objective, Support = support, GroupDistance = groupDistance, Heading = heading, Score = score };
            })
            .Where(candidate => candidate.Support is not null &&
                (candidate.GroupDistance <= 35f || candidate.Heading >= 0.25f) &&
                candidate.Objective.NearbyEnemies <= candidate.Objective.NearbyAllies + 2)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Objective.LogicalId, StringComparer.Ordinal)
            .ToArray();

        if (viable.Length == 0)
        {
            if (postDeathRegroupPending)
            {
                var regroup = trackedFieldGroup.HasValue
                    ? trackedFieldGroup.Value.Cluster is { } tracked
                        ? new WorqorRegroupCandidate(trackedFieldGroup.Value.Destination, tracked.PlayerCount)
                        : null
                    : WorqorRegroupPolicy.Choose(game.LocalPlayer.Position,
                        clusters.Select(cluster => new WorqorRegroupCandidate(
                            cluster.Center, cluster.PlayerCount)));
                if (regroup is not null)
                {
                    committedId = "WOR-REGROUP";
                    var regroupDetail = $"destination={committedId}; group={regroup.PlayerCount}; distance={HorizontalDistance(game.LocalPlayer.Position, regroup.Position):F1}; reason=no-supported-Triumph-after-death; commitment=arrival-or-death";
                    Event("worqor_group_regroup_selected", regroupDetail, now);
                    Status = "Regrouping with allied cluster after death until arrival";
                    return new WorqorGroupPlan(committedId, "Allied group after respawn",
                        regroup.Position, [], regroupDetail);
                }
            }
            WaitFor("Waiting for a fresh Triumph with allied support or a reachable allied group after death", now);
            return null;
        }

        var choice = viable[0];
        postDeathRegroupPending = false;
        committedId = choice.Objective.LogicalId;
        var detail = $"destination={committedId}; state={choice.Objective.State}; rank={choice.Objective.Rank}; allies={choice.Objective.NearbyAllies}; enemies={choice.Objective.NearbyEnemies}; group={choice.Support!.PlayerCount}; heading={choice.Heading:F2}; score={choice.Score:F1}; commitment=arrival-or-death";
        Event("worqor_group_selected", detail, now);
        Status = $"Committed to {committedId} until arrival or death";
        return new WorqorGroupPlan(committedId, choice.Objective.DisplayName,
            choice.Objective.ReferencePosition!.Value,
            choice.Objective.ValidatedApproachAnchors.Select(anchor => anchor.Position).ToArray(), detail);
    }

    private static bool RouteInProgress(ManualNavigationSnapshot route) =>
        route.DestinationId != "NONE" && route.State is
            ManualRouteState.Snapping or ManualRouteState.WaitingToMount or
            ManualRouteState.RequestingPath or ManualRouteState.Following or
            ManualRouteState.YieldedExternalCombat;

    private void Reset(bool clearSpawn)
    {
        committedId = null;
        lastArrivedId = null;
        lastArrivalUtc = DateTime.MinValue;
        selectAfterUtc = DateTime.MinValue;
        manualOverride = false;
        pausedAfterFailure = false;
        postDeathRegroupPending = false;
        lastWaitingReason = string.Empty;
        lastWaitingEventUtc = DateTime.MinValue;
        if (clearSpawn)
        {
            spawnPosition = null;
            diedThisMatch = false;
        }
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
        Event("worqor_group_waiting", reason, now);
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    private static float HeadingToward(FriendlyCluster cluster, Vector3 destination)
    {
        var movement = new Vector2(cluster.MovementTrend.X, cluster.MovementTrend.Z);
        var toDestination = new Vector2(destination.X - cluster.Center.X, destination.Z - cluster.Center.Z);
        return movement.Length() < 0.75f || toDestination.Length() < 1f
            ? 0f : Vector2.Dot(Vector2.Normalize(movement), Vector2.Normalize(toDestination));
    }
}
