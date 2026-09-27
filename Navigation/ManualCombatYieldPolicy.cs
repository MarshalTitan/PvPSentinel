namespace PvPSentinel.Navigation;

internal static class ManualCombatYieldPolicy
{
    public static bool ShouldPause(bool battlefieldBlocksMovement, bool combatProviderYieldsNavigation) =>
        battlefieldBlocksMovement || combatProviderYieldsNavigation;

    public static bool ShouldResume(
        bool routeWasPaused,
        bool battlefieldBlocksMovement,
        bool combatProviderYieldsNavigation) =>
        routeWasPaused && !ShouldPause(battlefieldBlocksMovement, combatProviderYieldsNavigation);
}
