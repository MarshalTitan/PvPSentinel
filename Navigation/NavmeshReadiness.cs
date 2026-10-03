namespace PvPSentinel.Navigation;

internal static class NavmeshReadiness
{
    // An old territory mesh can remain visible while vnavmesh builds the new one.
    // The IPC reports negative progress only when no build is underway.
    public static bool CanRequestPath(bool navReportedReady, float buildProgress) =>
        navReportedReady && float.IsFinite(buildProgress) && buildProgress < 0f;
}
