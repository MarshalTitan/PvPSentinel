using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace PvPSentinel.Combat;

internal sealed class NativeActionExecutor(IObjectTable objects, ITargetManager targets, IPluginLog log)
{
    private DateTime lastAttemptUtc = DateTime.MinValue;

    public unsafe bool IsReady(uint actionId, ulong targetId)
    {
        var manager = ActionManager.Instance();
        if (manager is null || actionId == 0)
            return false;

        var adjusted = manager->GetAdjustedActionId(actionId);
        if (adjusted != 0)
            actionId = adjusted;

        return manager->GetActionStatus(ActionType.Action, actionId, targetId) == 0;
    }

    public unsafe bool TryExecute(uint actionId, uint targetEntityId)
    {
        if (DateTime.UtcNow - lastAttemptUtc < TimeSpan.FromMilliseconds(150))
            return false;

        var target = objects.SearchByEntityId(targetEntityId) as ICharacter;
        var manager = ActionManager.Instance();
        if (target is null || manager is null || target.IsDead || !target.IsTargetable)
            return false;

        var adjusted = manager->GetAdjustedActionId(actionId);
        if (adjusted != 0)
            actionId = adjusted;

        if (manager->GetActionStatus(ActionType.Action, actionId, target.GameObjectId) != 0)
            return false;

        lastAttemptUtc = DateTime.UtcNow;
        targets.Target = target;
        var accepted = manager->UseAction(ActionType.Action, actionId, target.GameObjectId);
        if (accepted)
            log.Information("PvPSentinel executed PvP action {ActionId} on entity {EntityId}.", actionId, targetEntityId);
        return accepted;
    }
}

