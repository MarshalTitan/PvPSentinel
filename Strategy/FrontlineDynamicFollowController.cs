using System.Numerics;
using System.Security.Cryptography;
using PvPSentinel.Models;
using PvPSentinel.Navigation;

namespace PvPSentinel.Strategy;

internal sealed record DynamicFollowPlan(string DestinationId, Vector3 Position, string Reason);
internal sealed record DynamicFollowDecision(DynamicFollowPlan? Plan, bool CancelOwned, string Status);

/// <summary>Shared live field-group route policy. Path acquisition and recovery stay in NavigationController.</summary>
internal sealed class FrontlineDynamicFollowController(Func<int>? newSeed = null)
{
    private readonly Func<int> newSeed = newSeed ?? (() => RandomNumberGenerator.GetInt32(int.MaxValue));
    private FrontlineMap map = FrontlineMap.Unknown;
    private int? slotSeed;
    private Vector3? lastDestination;
    private Vector3 lastForward;
    private DateTime lastRequestUtc = DateTime.MinValue;
    private DateTime lastValidGroupUtc = DateTime.MinValue;
    private bool wasDead;
    private bool pausedAfterFailure;
    private bool manualOverride;

    public string Status { get; private set; } = "Idle";
    public Vector3? Destination => lastDestination;
    public string Slot => slotSeed is { } seed ? $"ranged-behind/lateral #{seed & 0xff:X2}" : "unassigned";
    public static string Id(FrontlineMap map) => $"FIELD-FOLLOW-{map}";
    public bool Owns(FrontlineMap currentMap, string? destinationId) => destinationId == Id(currentMap);

    public DynamicFollowDecision Update(FrontlineMap currentMap, DateTime now,
        bool enabled, bool ready, bool dead, bool results, bool staticBusy,
        ManualNavigationSnapshot route, string? pendingDestinationId,
        Vector3? localPosition, FieldGroupChoice? group)
    {
        var owns = Owns(currentMap, pendingDestinationId) || Owns(currentMap, route.DestinationId);
        if (currentMap != map)
            Reset(currentMap);
        if (!enabled || results)
        {
            Reset(currentMap);
            return Decision(null, owns, results ? "Results: movement stopped" : "Navigation disabled or map changed");
        }
        if (dead)
        {
            wasDead = true;
            lastDestination = null;
            return Decision(null, owns, "Dead; clearing dynamic route");
        }
        if (wasDead)
        {
            wasDead = false;
            slotSeed = null;
            lastDestination = null;
            lastRequestUtc = DateTime.MinValue;
        }
        if (!ready || localPosition is null)
        {
            lastDestination = null;
            lastRequestUtc = DateTime.MinValue;
            return Decision(null, owns, "Waiting for lifecycle, team, mesh and combat-provider readiness");
        }
        if (staticBusy)
        {
            lastDestination = null;
            return Decision(null, false, "Static objective route has priority");
        }
        if (pendingDestinationId is { } active && active != Id(currentMap) && active != "NONE")
        {
            manualOverride = true;
            return Decision(null, false, "Manual destination has priority");
        }
        if (manualOverride)
        {
            if (route.State is ManualRouteState.Following or ManualRouteState.RequestingPath or
                ManualRouteState.Snapping or ManualRouteState.WaitingToMount or ManualRouteState.YieldedExternalCombat)
                return Decision(null, false, "Manual destination has priority");
            manualOverride = false;
        }
        if (pausedAfterFailure)
            return Decision(null, false, "Paused after bounded route failure; restart navigation to retry");
        if (owns && route.DestinationId == Id(currentMap) &&
            (route.State == ManualRouteState.Failed ||
             route.State == ManualRouteState.Cancelled &&
             !route.Explanation.StartsWith("Automatic route paused:", StringComparison.Ordinal)))
        {
            pausedAfterFailure = true;
            return Decision(null, false, "Paused after bounded route failure; restart navigation to retry");
        }
        if (group?.Cluster is not { PlayerCount: >= 3 } || localPosition is null)
        {
            if (owns && now - lastValidGroupUtc <= TimeSpan.FromSeconds(5))
                return Decision(null, false, "Allied group temporarily unobserved; retaining current route briefly");
            lastDestination = null;
            return Decision(null, owns, "Waiting for a valid allied field group");
        }
        lastValidGroupUtc = now;

        if (group.Value.GroupChanged)
        {
            slotSeed = null;
            lastDestination = null;
            lastRequestUtc = DateTime.MinValue;
        }
        slotSeed ??= newSeed();
        var destination = FormationDestination(group.Value, localPosition.Value, slotSeed.Value);
        var distance = Distance(localPosition.Value, destination);
        var moved = lastDestination is { } old ? Distance(old, destination) : float.MaxValue;
        var age = now - lastRequestUtc;
        // Stay within support range without repeatedly routing to the centroid.
        if (distance <= 14f)
            return Decision(null, false, $"With allied group; retaining committed formation leg {Slot}");
        var inFlight = route.State is ManualRouteState.Snapping or ManualRouteState.RequestingPath;
        var refresh = lastDestination is null || !owns ||
            route.State == ManualRouteState.Arrived && age >= TimeSpan.FromSeconds(2) ||
            // A moving group can outrun an old endpoint, but a routine
            // centroid shift must not tear down a protected vnavmesh route.
            age >= TimeSpan.FromSeconds(15) && moved >= 25f;
        // Let the existing request finish or fail under the navigation
        // controller's bounded recovery before submitting another generation.
        if (inFlight)
            refresh = false;
        if (!refresh)
            return Decision(null, false, $"Following allied group; formation {Slot}; endpoint shift {moved:F1}y");

        var reason = lastDestination is null ? "acquire" : !owns ? "reacquire" :
            route.State == ManualRouteState.Arrived ? "group-moved-after-arrival" :
            route.State == ManualRouteState.Arrived ? "group-moved-after-arrival" :
            moved >= 25f ? "group-moved-materially" : "group-moved-after-hold";
        lastDestination = destination;
        lastRequestUtc = now;
        return Decision(new DynamicFollowPlan(Id(currentMap), destination, reason), false,
            $"Dynamic follow {reason}; formation {Slot}; distance {distance:F1}y");
    }

    public void SuspendForStatic()
    {
        lastDestination = null;
        lastRequestUtc = DateTime.MinValue;
        lastValidGroupUtc = DateTime.MinValue;
        Status = "Static objective route has priority";
    }

    private Vector3 FormationDestination(FieldGroupChoice group, Vector3 local, int seed)
    {
        var center = group.SmoothedCenter == default ? group.Destination : group.SmoothedCenter;
        var forward = new Vector3(group.Velocity.X, 0f, group.Velocity.Z);
        if (forward.Length() < 0.8f)
            forward = lastForward;
        if (forward.Length() < 0.8f)
            forward = new Vector3(center.X - local.X, 0f, center.Z - local.Z);
        if (forward.Length() < 0.8f)
            forward = Vector3.UnitZ;
        forward = Vector3.Normalize(forward);
        lastForward = forward;
        var side = new Vector3(-forward.Z, 0f, forward.X);
        var value = (uint)seed;
        var behind = 7f + value % 6u;
        var lateral = (5f + (value >> 4) % 8u) * ((value & 0x100u) == 0 ? 1f : -1f);
        var point = group.Destination - forward * behind + side * lateral;
        point.Y = center.Y;
        return point;
    }

    private static float Distance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    private void Reset(FrontlineMap currentMap)
    {
        map = currentMap;
        slotSeed = null;
        lastDestination = null;
        lastRequestUtc = DateTime.MinValue;
        lastForward = default;
        wasDead = false;
        pausedAfterFailure = false;
        manualOverride = false;
    }

    private DynamicFollowDecision Decision(DynamicFollowPlan? plan, bool cancel, string status)
    {
        Status = status;
        return new DynamicFollowDecision(plan, cancel, status);
    }
}
