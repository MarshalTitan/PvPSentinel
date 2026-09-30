namespace PvPSentinel.Navigation;

internal static class ManualCombatYieldPolicy
{
    public static bool ShouldPause(
        bool battlefieldBlocksMovement,
        bool combatProviderYieldsNavigation,
        bool continueManualRouteWithActiveReborn = false) =>
        !continueManualRouteWithActiveReborn &&
        (battlefieldBlocksMovement || combatProviderYieldsNavigation);

    public static bool ShouldResume(
        bool routeWasPaused,
        bool battlefieldBlocksMovement,
        bool combatProviderYieldsNavigation,
        bool continueManualRouteWithActiveReborn = false) =>
        routeWasPaused && !ShouldPause(
            battlefieldBlocksMovement, combatProviderYieldsNavigation, continueManualRouteWithActiveReborn);
}
