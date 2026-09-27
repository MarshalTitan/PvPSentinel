using FFXIVClientStructs.FFXIV.Client.Game;
using PvPSentinel.Diagnostics;
using PvPSentinel.Models;

namespace PvPSentinel.Navigation;

internal sealed class NativeMountController(DevelopmentLogger developmentLog) : IMountController
{
    private const uint MountRouletteGeneralAction = 9;
    private static readonly TimeSpan MountRequestGrace = TimeSpan.FromSeconds(4);
    private DateTime lastAttemptUtc = DateTime.MinValue;
    private DateTime mountRequestPendingUntilUtc = DateTime.MinValue;

    public bool IsTransitionPending(DateTime now) => now < mountRequestPendingUntilUtc;

    public unsafe MountDecision Update(
        GameStateSnapshot game,
        bool longDistanceTravel,
        bool forceDismount,
        int nearbyEnemies,
        Configuration config)
    {
        if (!config.MountingEnabled)
        {
            mountRequestPendingUntilUtc = DateTime.MinValue;
            return new MountDecision(MountState.Disabled, false, "Automatic mounting is disabled.");
        }

        if (!game.IsFrontline || game.LocalPlayer is null || game.LocalPlayer.IsDead || game.IsBetweenAreas)
        {
            mountRequestPendingUntilUtc = DateTime.MinValue;
            return new MountDecision(MountState.Blocked, true, "Mounting is behind the Frontline/player safety gate.");
        }

        if (game.IsMounting)
            return new MountDecision(game.IsMounted ? MountState.DismountRequested : MountState.MountRequested, true,
                "A mount transition is already in progress.");

        if (game.IsMounted)
        {
            mountRequestPendingUntilUtc = DateTime.MinValue;
            if (!forceDismount)
                return new MountDecision(MountState.Mounted, false,
                    "Mounted travel remains stable until arrival or confirmed combat requires a dismount.");

            var accepted = TryToggleMount(game.CapturedAtUtc);
            developmentLog.Throttled("mount-dismount", accepted
                ? "Requested dismount before combat/arrival."
                : "Dismount request is waiting for the general action to become available.", TimeSpan.FromSeconds(2));
            return new MountDecision(accepted ? MountState.DismountRequested : MountState.Blocked, true,
                accepted ? "Dismount requested before combat or close-range arrival." : "Unable to dismount safely yet.");
        }

        if (forceDismount)
        {
            mountRequestPendingUntilUtc = DateTime.MinValue;
            return new MountDecision(MountState.OnFoot, false,
                "Already on foot while combat/arrival policy requests a dismounted state.");
        }

        if (game.CapturedAtUtc < mountRequestPendingUntilUtc)
        {
            return new MountDecision(MountState.MountRequested, true,
                $"Mount request was accepted; waiting up to {(mountRequestPendingUntilUtc - game.CapturedAtUtc).TotalSeconds:F1}s for the client transition.");
        }

        mountRequestPendingUntilUtc = DateTime.MinValue;
        if (!longDistanceTravel)
            return new MountDecision(MountState.OnFoot, false, "Travel distance does not require a mount.");

        if (game.IsInCombat || game.IsCasting || nearbyEnemies > 0)
            return new MountDecision(MountState.Blocked, true,
                $"Mount request blocked: combat={game.IsInCombat}, casting={game.IsCasting}, enemies in safety radius={nearbyEnemies}.");

        var mounted = TryToggleMount(game.CapturedAtUtc);
        if (mounted)
            mountRequestPendingUntilUtc = game.CapturedAtUtc + MountRequestGrace;
        developmentLog.Throttled("mount-request", mounted
            ? "Requested Mount Roulette for long-distance Frontline travel."
            : "Mount request is waiting for the general action to become available.", TimeSpan.FromSeconds(2));
        return new MountDecision(mounted ? MountState.MountRequested : MountState.Blocked, true,
            mounted ? "Mount requested; path movement will wait for the mounted state." : "Unable to mount safely yet.");
    }

    private unsafe bool TryToggleMount(DateTime now)
    {
        if (now - lastAttemptUtc < TimeSpan.FromSeconds(2))
            return false;

        var manager = ActionManager.Instance();
        if (manager is null || manager->GetActionStatus(ActionType.GeneralAction, MountRouletteGeneralAction) != 0)
            return false;

        lastAttemptUtc = now;
        return manager->UseAction(ActionType.GeneralAction, MountRouletteGeneralAction);
    }
}
