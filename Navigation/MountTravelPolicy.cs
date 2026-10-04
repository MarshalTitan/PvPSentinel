namespace PvPSentinel.Navigation;

internal static class MountTravelPolicy
{
    public static bool ShouldMountForLeg(bool combatTravelActive, bool dynamicFieldLeg,
        float routeDistance, float directDistance, float mountDistance) =>
        !combatTravelActive && routeDistance >= mountDistance &&
        (!dynamicFieldLeg || directDistance >= mountDistance);

    public static bool ShouldDismount(
        bool isMounted,
        float remainingRouteDistance,
        float dismountDistance,
        bool combatOwnsMovement,
        bool dynamicFieldLeg = false) =>
        isMounted &&
        (combatOwnsMovement ||
         !dynamicFieldLeg && remainingRouteDistance <= Math.Max(0f, dismountDistance));
}
