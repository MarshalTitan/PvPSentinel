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
    IPartyList partyList,
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
                    [],
                    [],
                    new FrontlineTeamStatus(false, 0, 0, false, "Local player is unavailable."),
                    "Local player is unavailable.");
            }

            var roster = CaptureTeamRoster(localObject, isFrontline);
            var local = Convert(localObject, PlayerClassification.Friendly, isRosterMember: true);
            var friendlies = new List<PlayerSnapshot>();
            var enemies = new List<PlayerSnapshot>();
            var unknownPlayers = new List<PlayerSnapshot>();
            var observedPlayers = new List<PlayerSnapshot>();

            foreach (var player in objects.PlayerObjects)
            {
                if (player.EntityId == local.EntityId || player.EntityId == 0)
                    continue;

                var isRosterMember = roster.EntityIds.Contains(player.EntityId) ||
                                     roster.ObjectIds.Contains(player.GameObjectId);
                var classification = FrontlinePlayerResolver.Classify(
                    isLocalPlayer: false,
                    isFrontline,
                    roster.Status.CanClassifyNonMembers,
                    isRosterMember,
                    player.StatusFlags);
                var snapshot = Convert(player, classification, isRosterMember);
                observedPlayers.Add(snapshot);

                switch (classification)
                {
                    case PlayerClassification.Friendly:
                        friendlies.Add(snapshot);
                        break;
                    case PlayerClassification.Enemy:
                        enemies.Add(snapshot);
                        break;
                    default:
                        unknownPlayers.Add(snapshot);
                        break;
                }
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
                unknownPlayers,
                observedPlayers,
                roster.Status,
                string.Empty);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "PvPSentinel paused because game state could not be captured safely.");
            return GameStateSnapshot.Unavailable(ex.Message);
        }
    }

    private PlayerSnapshot Convert(
        IBattleChara player,
        PlayerClassification classification,
        bool isRosterMember)
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
            classification,
            flags,
            flags.HasFlag(StatusFlags.PartyMember),
            flags.HasFlag(StatusFlags.AllianceMember),
            flags.HasFlag(StatusFlags.Hostile),
            isRosterMember,
            player.IsDead || player.CurrentHp == 0,
            player.IsTargetable,
            statuses);
    }

    private TeamRoster CaptureTeamRoster(IBattleChara localPlayer, bool isFrontline)
    {
        var entityIds = new HashSet<uint> { localPlayer.EntityId };
        var objectIds = new HashSet<ulong> { localPlayer.GameObjectId };
        var isAlliance = false;
        var declaredCount = 0;

        try
        {
            isAlliance = partyList.IsAlliance;
            declaredCount = partyList.Length;
            foreach (var member in partyList)
            {
                if (member.EntityId != 0)
                    entityIds.Add(member.EntityId);
                if (member.GameObject is { } gameObject)
                    objectIds.Add(gameObject.GameObjectId);
            }
        }
        catch (Exception ex)
        {
            return new TeamRoster(
                entityIds,
                objectIds,
                new FrontlineTeamStatus(false, 0, entityIds.Count, false,
                    $"Alliance roster read failed; non-member PCs remain Unknown ({ex.GetType().Name})."));
        }

        var canClassifyNonMembers = isFrontline && isAlliance && declaredCount >= 8 && entityIds.Count >= 2;
        var explanation = !isFrontline
            ? "Not in a recognized Frontline duty; non-member classification is disabled."
            : canClassifyNonMembers
                ? "Alliance roster is available; positive membership is Friendly and other loaded PCs are Enemy candidates."
                : "Frontline alliance roster is not authoritative yet; non-member PCs remain Unknown.";

        return new TeamRoster(
            entityIds,
            objectIds,
            new FrontlineTeamStatus(isAlliance, declaredCount, entityIds.Count, canClassifyNonMembers, explanation));
    }

    private sealed record TeamRoster(
        HashSet<uint> EntityIds,
        HashSet<ulong> ObjectIds,
        FrontlineTeamStatus Status);

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
