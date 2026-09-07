using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using PvPSentinel.Models;

namespace PvPSentinel.GameState;

internal sealed class GameStateService(
    IClientState clientState,
    ICondition condition,
    IObjectTable objects,
    IDataManager data,
    IPluginLog log)
{
    private readonly Dictionary<uint, string> statusNames = new();

    public GameStateSnapshot Capture()
    {
        try
        {
            var localObject = objects.LocalPlayer;
            var territory = ResolveTerritory(clientState.TerritoryType);
            var territoryName = territory.RowId == 0 ? $"Territory {clientState.TerritoryType}" : territory.PlaceName.Value.Name.ToString();
            var isPvP = clientState.IsPvPExcludingDen;
            var isBoundByDuty = condition[ConditionFlag.BoundByDuty] ||
                                condition[ConditionFlag.BoundByDuty56] ||
                                condition[ConditionFlag.BoundByDuty95];
            var isFrontline = FrontlineDetector.IsFrontline(isPvP, isBoundByDuty, territory);

            if (!clientState.IsLoggedIn || localObject is null)
            {
                return new GameStateSnapshot(
                    DateTime.UtcNow,
                    clientState.IsLoggedIn,
                    isPvP,
                    isBoundByDuty,
                    isFrontline,
                    clientState.TerritoryType,
                    clientState.MapId,
                    territoryName,
                    null,
                    [],
                    [],
                    "Local player is unavailable.");
            }

            var local = Convert(localObject);
            var friendlies = new List<PlayerSnapshot>();
            var enemies = new List<PlayerSnapshot>();

            foreach (var player in objects.PlayerObjects)
            {
                if (player.EntityId == local.EntityId || player.EntityId == 0)
                    continue;

                var snapshot = Convert(player);
                if (snapshot.IsHostile)
                    enemies.Add(snapshot);
                else
                    friendlies.Add(snapshot);
            }

            // Including ourselves gives clustering a stable anchor and makes a lone
            // visible ally a meaningful two-person group.
            friendlies.Add(local);

            return new GameStateSnapshot(
                DateTime.UtcNow,
                clientState.IsLoggedIn,
                isPvP,
                isBoundByDuty,
                isFrontline,
                clientState.TerritoryType,
                clientState.MapId,
                territoryName,
                local,
                friendlies,
                enemies,
                string.Empty);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "PvPSentinel paused because game state could not be captured safely.");
            return GameStateSnapshot.Unavailable(ex.Message);
        }
    }

    private PlayerSnapshot Convert(IBattleChara player)
    {
        var flags = player.StatusFlags;
        var statuses = player.StatusList
            .Select(status => new StatusSnapshot(
                status.StatusId,
                ResolveStatusName(status.StatusId),
                status.RemainingTime))
            .ToArray();

        return new PlayerSnapshot(
            player.GameObjectId,
            player.EntityId,
            player.Name.TextValue,
            player.ClassJob.RowId,
            ResolveJobAbbreviation(player.ClassJob.RowId),
            player.Position,
            player.CurrentHp,
            player.MaxHp,
            player.ShieldPercentage,
            flags.HasFlag(StatusFlags.Hostile),
            flags.HasFlag(StatusFlags.PartyMember) || flags.HasFlag(StatusFlags.AllianceMember),
            player.IsDead || player.CurrentHp == 0,
            player.IsTargetable,
            statuses);
    }

    private string ResolveStatusName(uint id)
    {
        if (statusNames.TryGetValue(id, out var cached))
            return cached;

        var row = data.GetExcelSheet<Status>().FirstOrDefault(status => status.RowId == id);
        var name = row.RowId == 0 ? $"Status {id}" : row.Name.ToString();
        statusNames[id] = name;
        return name;
    }

    private string ResolveJobAbbreviation(uint id)
    {
        var row = data.GetExcelSheet<ClassJob>().FirstOrDefault(job => job.RowId == id);
        return row.RowId == 0 ? $"Job {id}" : row.Abbreviation.ToString();
    }

    private TerritoryType ResolveTerritory(uint id) =>
        data.GetExcelSheet<TerritoryType>().FirstOrDefault(territory => territory.RowId == id);
}
