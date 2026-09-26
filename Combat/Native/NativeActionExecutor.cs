using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace PvPSentinel.Combat.Native;

internal sealed class NativeActionExecutor(IObjectTable objects, ITargetManager targets, IPluginLog log)
{
    private DateTime lastAttemptUtc = DateTime.MinValue;

    public unsafe ActionAvailability Check(NativeActionCandidate action)
    {
        var manager = ActionManager.Instance();
        if (manager is null || action.ActionId == 0)
            return new ActionAvailability(false, "action manager unavailable");

        var adjusted = manager->GetAdjustedActionId(action.ActionId);
        var actionId = adjusted == 0 ? action.ActionId : adjusted;
        var status = manager->GetActionStatus(ActionType.Action, actionId, action.TargetObjectId);
        return status == 0
            ? new ActionAvailability(true, "ready")
            : new ActionAvailability(false, $"client action status {status}");
    }

    public unsafe bool TryExecute(NativeActionCandidate action)
    {
        if (DateTime.UtcNow - lastAttemptUtc < TimeSpan.FromMilliseconds(150))
            return false;

        var manager = ActionManager.Instance();
        if (manager is null)
            return false;

        ICharacter? enemyTarget = null;
        if (action.TargetType == NativeActionTarget.Enemy)
        {
            enemyTarget = objects.SearchByEntityId(action.TargetEntityId) as ICharacter;
            if (enemyTarget is null || enemyTarget.IsDead || !enemyTarget.IsTargetable)
                return false;
        }

        var adjusted = manager->GetAdjustedActionId(action.ActionId);
        var actionId = adjusted == 0 ? action.ActionId : adjusted;
        if (manager->GetActionStatus(ActionType.Action, actionId, action.TargetObjectId) != 0)
            return false;

        lastAttemptUtc = DateTime.UtcNow;
        if (enemyTarget is not null)
            targets.Target = enemyTarget;

        var accepted = manager->UseAction(ActionType.Action, actionId, action.TargetObjectId);
        if (accepted)
        {
            log.Information(
                "PvPSentinel native combat used {ActionName} ({ActionId}) on 0x{TargetId:X16}.",
                action.ActionName,
                actionId,
                action.TargetObjectId);
        }

        return accepted;
    }
}
