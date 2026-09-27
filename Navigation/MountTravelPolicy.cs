namespace PvPSentinel.Navigation;

internal static class MountTravelPolicy
{
    public static bool ShouldDismount(
        bool isMounted,
        float remainingRouteDistance,
        float dismountDistance,
        bool combatOwnsMovement) =>
        isMounted &&
        (combatOwnsMovement || remainingRouteDistance <= Math.Max(0f, dismountDistance));
}
