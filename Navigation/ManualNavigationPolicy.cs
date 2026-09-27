namespace PvPSentinel.Navigation;

internal static class ManualNavigationPolicy
{
    public static MovementOwner ResolveOwner(bool isDead, bool pvpCombatBlocks, bool routeActive) =>
        isDead ? MovementOwner.DeathRecovery :
        pvpCombatBlocks ? MovementOwner.ExternalCombat :
        routeActive ? MovementOwner.SentinelTestNav : MovementOwner.None;

    public static bool CanRetry(int failures, int maximumFailures) =>
        failures < Math.Clamp(maximumFailures, 1, 10);
}
