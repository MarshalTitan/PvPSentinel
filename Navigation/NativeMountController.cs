using FFXIVClientStructs.FFXIV.Client.Game;
using PvPSentinel.Diagnostics;
using PvPSentinel.Models;

namespace PvPSentinel.Navigation;

internal sealed class NativeMountController(
    PreferredMountCatalog mountCatalog,
    DevelopmentLogger developmentLog) : IMountController
{
    // General Action 9 is retained only as the normal mounted-state dismount
    // toggle. It is never called while on foot and can never select a random mount.
    private const uint DismountGeneralAction = 9;
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

            var accepted = TryDismount(game.CapturedAtUtc);
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

        var preferred = mountCatalog.Resolve(config.PreferredMountId, config.PreferredMountName);
        if (!preferred.IsAvailable)
        {
            developmentLog.Throttled("preferred-mount-unavailable", preferred.Explanation, TimeSpan.FromSeconds(5));
            return new MountDecision(MountState.Blocked, false,
                $"{preferred.Explanation} No Mount Roulette fallback was attempted; travel may continue on foot.");
        }

        var mounted = TrySummonMount(game.CapturedAtUtc, preferred.RowId, out var actionStatus);
        if (mounted)
            mountRequestPendingUntilUtc = game.CapturedAtUtc + MountRequestGrace;
        developmentLog.Throttled("mount-request", mounted
            ? $"Requested preferred mount '{preferred.Name}' (Mount row {preferred.RowId}) for long-distance Frontline travel."
            : $"Preferred mount '{preferred.Name}' is not currently summonable (action status {actionStatus}).", TimeSpan.FromSeconds(2));
        return new MountDecision(mounted ? MountState.MountRequested : MountState.Blocked, true,
            mounted
                ? $"Preferred mount '{preferred.Name}' requested; path movement will wait for the mounted state."
                : $"Preferred mount '{preferred.Name}' is not currently summonable (action status {actionStatus}); no random fallback was attempted.");
    }

    private unsafe bool TryDismount(DateTime now)
    {
        if (now - lastAttemptUtc < TimeSpan.FromSeconds(2))
            return false;

        var manager = ActionManager.Instance();
        if (manager is null || manager->GetActionStatus(ActionType.GeneralAction, DismountGeneralAction) != 0)
            return false;

        lastAttemptUtc = now;
        return manager->UseAction(ActionType.GeneralAction, DismountGeneralAction);
    }

    private unsafe bool TrySummonMount(DateTime now, uint mountRowId, out uint actionStatus)
    {
        actionStatus = uint.MaxValue;
        if (now - lastAttemptUtc < TimeSpan.FromSeconds(2))
            return false;

        var manager = ActionManager.Instance();
        if (manager is null)
            return false;

        actionStatus = manager->GetActionStatus(ActionType.Mount, mountRowId);
        if (actionStatus != 0)
            return false;

        lastAttemptUtc = now;
        return manager->UseAction(ActionType.Mount, mountRowId);
    }
}
