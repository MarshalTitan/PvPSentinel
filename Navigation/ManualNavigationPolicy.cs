using System.Numerics;

namespace PvPSentinel.Navigation;

internal static class ManualNavigationPolicy
{
    public static MovementOwner ResolveOwner(bool isDead, bool pvpCombatBlocks, bool routeActive) =>
        isDead ? MovementOwner.DeathRecovery :
        pvpCombatBlocks ? MovementOwner.ExternalCombat :
        routeActive ? MovementOwner.SentinelTestNav : MovementOwner.None;

    public static bool CanRetry(int failures, int maximumFailures) =>
        failures < Math.Clamp(maximumFailures, 1, 10);

    public static bool HasReachedGeneratedPoint(
        Vector3 player,
        Vector3 target,
        float horizontalTolerance,
        float verticalTolerance,
        bool isMounted)
    {
        var horizontal = Vector2.Distance(new Vector2(player.X, player.Z), new Vector2(target.X, target.Z));
        // Dalamud reports the mounted actor several yalms above the ground-mesh
        // point even after vnavmesh has completed the path. The allowance is
        // bounded and applies only after following a generated route; a genuinely
        // different floor still fails the vertical check.
        var effectiveVerticalTolerance = verticalTolerance + (isMounted ? 5f : 0f);
        return horizontal <= horizontalTolerance &&
               Math.Abs(player.Y - target.Y) <= effectiveVerticalTolerance;
    }
}
