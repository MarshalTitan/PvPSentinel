namespace PvPSentinel.Navigation;

internal static class MountTravelPolicy
{
    public static bool ShouldMountForLeg(bool combatTravelActive, bool dynamicFieldLeg,
        float routeDistance, float directDistance, float mountDistance) =>
        !combatTravelActive && routeDistance >= mountDistance &&
        (!dynamicFieldLeg || directDistance >= mountDistance);

    public static float DismountDistanceForLeg(bool dynamicFieldLeg, float configuredDistance) =>
        dynamicFieldLeg ? Math.Min(configuredDistance, 14f) : configuredDistance;

    public static bool ShouldDismount(
        bool isMounted,
        float remainingRouteDistance,
        float dismountDistance,
        bool combatOwnsMovement) =>
        isMounted &&
        (combatOwnsMovement || remainingRouteDistance <= Math.Max(0f, dismountDistance));
}
