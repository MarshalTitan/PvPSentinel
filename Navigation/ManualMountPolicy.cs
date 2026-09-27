using PvPSentinel.Models;

namespace PvPSentinel.Navigation;

internal static class ManualMountPolicy
{
    // Manual M2 must wait while a mount transition is actually happening, but
    // a failed/blocked mount request must not recreate the old problem where a
    // stale game-combat flag could prevent an otherwise valid manual route.
    public static bool ShouldWaitBeforeMovement(
        MountState state,
        bool isMounted,
        bool isMounting,
        bool controllerRequestsWait) =>
        isMounting ||
        state is MountState.MountRequested or MountState.DismountRequested ||
        (isMounted && controllerRequestsWait);
}
