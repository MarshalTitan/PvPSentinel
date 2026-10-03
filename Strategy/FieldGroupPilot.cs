using System.Numerics;
using PvPSentinel.FrontlineCore;
using PvPSentinel.Models;
using PvPSentinel.Navigation;

namespace PvPSentinel.Strategy;

internal sealed record FieldGroupPlan(string DestinationId, string DestinationName, Vector3 Position);

/// <summary>
/// Conservative group-only pilot for maps whose objective states are not verified.
/// It never interprets discovery locations as capturable objectives.
/// </summary>
internal sealed class FieldGroupPilot(FrontlineMap map, string eventPrefix, string destinationId)
{
    private readonly Queue<ManualNavigationEvent> events = [];
    private string? committedId;
    private Vector3? spawnPosition;
    private DateTime selectAfterUtc = DateTime.MinValue;
    private DateTime lastWaitingEventUtc = DateTime.MinValue;
    private DateTime lastEvaluationUtc = DateTime.MinValue;
    private DateTime lastCombatEventUtc = DateTime.MinValue;
    private string lastWaitingReason = string.Empty;
    private bool enabledLastFrame;
    private bool deadLastFrame;
    private bool respawnPending;
    private bool manualOverride;
    private bool pausedAfterFailure;

    public string Status { get; private set; } = "Off";
    public string? CommittedDestinationId => committedId;
    public string? CancelDestinationId { get; private set; }

    public IReadOnlyList<ManualNavigationEvent> DrainEvents()
    {
        var result = events.ToArray();
        events.Clear();
        return result;
    }

    public FieldGroupPlan? Update(GameStateSnapshot game, BattlefieldState battlefield,
        IReadOnlyList<FriendlyCluster> clusters, ManualNavigationSnapshot route,
        bool enabled, bool rebornReady, bool meshReady, bool combatActive = false,
        string? currentManualDestinationId = null, FieldGroupChoice? trackedFieldGroup = null)
    {
        var now = game.CapturedAtUtc;
        CancelDestinationId = null;
        if (game.FrontlineMap != map ||
            battlefield.Match.Lifecycle is FrontlineMatchLifecycle.Outside or FrontlineMatchLifecycle.Results)
        {
            Reset(now, game.FrontlineMap == map ? "results" : "map-changed", route);
            return null;
        }
        if (!enabled)
        {
            Reset(now, "toggle-off", route);
            return null;
        }
        if (!enabledLastFrame)
        {
            enabledLastFrame = true;
            Event("enabled", "mode=allied-field-group-only; objectives=unresolved", now);
        }
        // An opt-in can be enabled mid-match; that position is not evidence of the base.
        if (spawnPosition is null && game.LocalPlayer is { IsDead: false } atSpawn &&
            battlefield.Match.Lifecycle == FrontlineMatchLifecycle.PreMatch)
            spawnPosition = atSpawn.Position;

        if (game.LocalPlayer?.IsDead == true)
        {
            if (!deadLastFrame)
            {
                Event("death_clear", $"destination={committedId ?? "NONE"}", now);
                selectAfterUtc = now.AddSeconds(4);
            }
            deadLastFrame = true;
            respawnPending = true;
            committedId = null;
            manualOverride = false;
            Status = "Dead; will choose an allied field group after respawn";
            return null;
        }
        if (deadLastFrame)
        {
            deadLastFrame = false;
            spawnPosition = game.LocalPlayer?.Position ?? spawnPosition;
            selectAfterUtc = now.AddSeconds(4);
        }
        if (battlefield.Match.Lifecycle != FrontlineMatchLifecycle.MatchActive)
        {
            WaitFor("Waiting for active match", now);
            return null;
        }
        if (game.LocalPlayer is null || !game.IsClassificationReliable || !rebornReady || !meshReady)
        {
            var reason = game.LocalPlayer is null || !game.IsClassificationReliable
                ? $"Waiting for reliable team: {game.ClassificationReliabilityExplanation}"
                : !rebornReady ? "Reborn autorotation inactive; enable it to resume group travel"
                : "Waiting for vnavmesh";
            if (committedId is { } unsafeRoute)
            {
                CancelDestinationId = unsafeRoute;
                committedId = null;
                selectAfterUtc = now.AddSeconds(4);
                Event("safety_cancel", $"destination={unsafeRoute}; reason={reason}", now);
            }
            WaitFor(reason, now);
            return null;
        }
        // A newly clicked manual destination can still be pending while the
        // previous route snapshot says Idle. Respect that request immediately.
        if (currentManualDestinationId is { } manualId && manualId != destinationId)
        {
            committedId = null;
            manualOverride = true;
            Status = "Manual destination has priority";
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
                selectAfterUtc = now.AddSeconds(8);
                Event("arrived", $"destination={current}; hold=8s", now);
            }
            else if (route.DestinationId == current &&
                     route.State is ManualRouteState.Failed or ManualRouteState.Cancelled)
            {
                committedId = null;
                pausedAfterFailure = true;
                Event("route_failure", $"destination={current}; state={route.State}; restart=toggle-or-next-match", now);
            }
            else
            {
                Status = $"Committed to {current} until arrival or death";
                if (combatActive && RouteInProgress(route) &&
                    now - lastCombatEventUtc >= TimeSpan.FromSeconds(30))
                {
                    lastCombatEventUtc = now;
                    Event("retained_during_combat", $"destination={current}; route={route.State}", now);
                }
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
            Status = "Holding before choosing the next allied field group";
            return null;
        }

        if (respawnPending)
        {
            respawnPending = false;
            Event("respawn_reselection", "evaluating visible allied field groups", now);
        }
        var local = game.LocalPlayer.Position;
        IReadOnlyList<FriendlyCluster> candidateGroups = trackedFieldGroup.HasValue
            ? trackedFieldGroup.Value.Cluster is { } tracked ? new[] { tracked } : Array.Empty<FriendlyCluster>()
            : clusters;
        var eligible = candidateGroups
            .Select(group => new { Group = group, Distance = Distance(local, group.Center) })
            .Where(item => item.Group.PlayerCount >= 3 && item.Distance is >= 35f and <= 400f &&
                           (trackedFieldGroup.HasValue || spawnPosition is null ||
                            Distance(spawnPosition.Value, item.Group.Center) >= 45f))
            .OrderByDescending(item => item.Group.PlayerCount * 8f - item.Distance * 0.15f)
            .ThenBy(item => item.Distance)
            .ToArray();
        if (now - lastEvaluationUtc >= TimeSpan.FromSeconds(20))
        {
            lastEvaluationUtc = now;
            Event("candidate_evaluation",
                $"unresolved_objectives={battlefield.Objectives.Count}; clusters={clusters.Count}; eligible_field_groups={eligible.Length}; spawn_known={spawnPosition is not null}", now);
            if (map == FrontlineMap.OnsalHakair && battlefield.Objectives.Count > 0)
                Event("objective_rejected",
                    $"count={battlefield.Objectives.Count}; reason=Ovoo-state-and-ownership-unresolved; discovery-is-manual-only", now);
        }
        if (eligible.Length == 0)
        {
            WaitFor("Waiting for a visible allied group away from spawn", now);
            return null;
        }
        var chosen = eligible[0];
        var groupPosition = trackedFieldGroup?.Destination ?? chosen.Group.Center;
        committedId = destinationId;
        Event("allied_cluster_fallback",
            $"group={chosen.Group.PlayerCount}; distance={chosen.Distance:F1}; objective_selection=disabled", now);
        Event("destination_committed",
            $"destination={committedId}; group={chosen.Group.PlayerCount}; position=({groupPosition.X:F1},{groupPosition.Y:F1},{groupPosition.Z:F1}); commitment=arrival-or-death", now);
        Status = "Committed to allied field group until arrival or death";
        return new FieldGroupPlan(destinationId, "Allied field group", groupPosition);
    }

    private static bool RouteInProgress(ManualNavigationSnapshot route) =>
        route.DestinationId != "NONE" && route.State is
            ManualRouteState.Snapping or ManualRouteState.WaitingToMount or
            ManualRouteState.RequestingPath or ManualRouteState.Following or
            ManualRouteState.YieldedExternalCombat;

    private void Reset(DateTime now, string reason, ManualNavigationSnapshot route)
    {
        if (committedId is { } current &&
            (route.DestinationId == current || route.DestinationId == "NONE"))
            CancelDestinationId = current;
        if (enabledLastFrame)
            Event("disabled", $"reason={reason}; destination={committedId ?? "NONE"}", now);
        enabledLastFrame = false;
        committedId = null;
        spawnPosition = null;
        selectAfterUtc = DateTime.MinValue;
        lastWaitingEventUtc = DateTime.MinValue;
        lastEvaluationUtc = DateTime.MinValue;
        lastCombatEventUtc = DateTime.MinValue;
        lastWaitingReason = string.Empty;
        deadLastFrame = false;
        respawnPending = false;
        manualOverride = false;
        pausedAfterFailure = false;
        Status = "Off";
    }

    private void Event(string suffix, string detail, DateTime now) =>
        events.Enqueue(new ManualNavigationEvent($"{eventPrefix}_group_{suffix}", detail, now));

    private void WaitFor(string reason, DateTime now)
    {
        Status = reason;
        if (lastWaitingReason == reason && now - lastWaitingEventUtc < TimeSpan.FromSeconds(30))
            return;
        lastWaitingReason = reason;
        lastWaitingEventUtc = now;
        Event("waiting", reason, now);
    }

    private static float Distance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
