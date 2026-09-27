using PvPSentinel.Models;

namespace PvPSentinel.Navigation;

internal sealed record MountDecision(MountState State, bool WaitBeforeMovement, string Explanation);

internal interface IMountController
{
    bool IsTransitionPending(DateTime now);

    MountDecision Update(
        GameStateSnapshot game,
        bool longDistanceTravel,
        bool forceDismount,
        int nearbyEnemies,
        Configuration config);
}
