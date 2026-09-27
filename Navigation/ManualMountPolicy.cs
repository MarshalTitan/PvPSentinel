namespace PvPSentinel.Navigation;

internal static class ManualMountPolicy
{
    // Manual M2 must wait while a mount transition is actually happening, but
    // a failed/blocked mount request must not recreate the old problem where a
    // stale game-combat flag could prevent an otherwise valid manual route.
    public static bool ShouldWaitBeforeMovement(
        bool mountRequested,
        bool dismountRequested,
        bool isMounted,
        bool isMounting,
        bool controllerRequestsWait) =>
        isMounting ||
        mountRequested ||
        dismountRequested ||
        (isMounted && controllerRequestsWait);
}
