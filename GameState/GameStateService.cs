using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using PvPSentinel.Diagnostics;
using PvPSentinel.FrontlineCore;
using PvPSentinel.Models;
using ActionManager = FFXIVClientStructs.FFXIV.Client.Game.ActionManager;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using LimitBreakController = FFXIVClientStructs.FFXIV.Client.Game.UI.LimitBreakController;

namespace PvPSentinel.GameState;

internal sealed class GameStateService(
    IClientState clientState,
    ICondition condition,
    IObjectTable objects,
    IPartyList partyList,
    IDataManager data,
    IPluginLog log,
    DevelopmentLogger developmentLog,
    FrontlineCore.Diagnostics.FrontlineEntryTrace entryTrace)
{
    private readonly Dictionary<uint, string> statusNames = new();
    private readonly FrontlineEntryGate entryGate = new();
    private int completedStableScans;

    public unsafe GameStateSnapshot Capture()
    {
        try
        {
            var now = DateTime.UtcNow;
            var localObject = objects.LocalPlayer;
            var territory = ResolveTerritory(clientState.TerritoryType);
            var territoryName = territory.RowId == 0 ? $"Territory {clientState.TerritoryType}" : territory.PlaceName.Value.Name.ToString();
            var content = territory.RowId == 0 ? default : territory.ContentFinderCondition.Value;
            var contentName = content.RowId == 0 ? "Unknown" : content.Name.ToString();
            var isPvP = clientState.IsPvPExcludingDen;
            var isBoundByDuty = condition[ConditionFlag.BoundByDuty] ||
                                condition[ConditionFlag.BoundByDuty56] ||
                                condition[ConditionFlag.BoundByDuty95];
            var isFrontline = FrontlineDetector.IsFrontline(isPvP, isBoundByDuty, territory);
            if (isFrontline)
            {
                entryTrace.Begin(clientState.TerritoryType, now);
                entryTrace.Stage("game-state", "START", now);
            }
            else
                entryTrace.Reset();
            var frontlineMap = FrontlineMapCatalog.Identify(content.RowId, clientState.TerritoryType);
            var isInCombat = condition[ConditionFlag.InCombat];
            var isCasting = condition[ConditionFlag.Casting] || condition[ConditionFlag.Casting87] || localObject?.IsCasting == true;
            var isBetweenAreas = condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51];
            var mayScan = entryGate.MayScan(isFrontline, clientState.TerritoryType,
                clientState.IsLoggedIn, isBetweenAreas, localObject is not null, now);
            if (!mayScan)
                completedStableScans = 0;
            var safeNative = clientState.IsLoggedIn && !isBetweenAreas && localObject is not null &&
                (!isFrontline || mayScan);
            // These singleton pointers and all object/native research reads are
            // skipped while zoning or before a stable local player exists.
            var actionManager = safeNative ? ActionManager.Instance() : null;
            var isActionQueued = actionManager is not null && actionManager->ActionQueued;
            var animationLockSeconds = actionManager is null ? 0f : Math.Max(0f, actionManager->AnimationLock);
            var isMounted = condition[ConditionFlag.Mounted] || condition[ConditionFlag.RidingPillion];
            var isMounting = condition[ConditionFlag.Mounting] || condition[ConditionFlag.MountOrOrnamentTransition];
            var limitBreak = safeNative ? LimitBreakController.Instance() : null;
            var limitCurrent = limitBreak is null ? (ushort)0 : limitBreak->CurrentUnits;
            var limitBarUnits = limitBreak is null ? (ushort)0 : limitBreak->BarUnits;
            var limitBarCount = limitBreak is null ? (byte)0 : limitBreak->BarCount;

            if (!clientState.IsLoggedIn || localObject is null || (isFrontline && !mayScan))
            {
                if (isFrontline)
                    entryTrace.Stage("entry-gate", "HOLD", DateTime.UtcNow);
                return new GameStateSnapshot(
                    DateTime.UtcNow,
                    clientState.IsLoggedIn,
                    isPvP,
                    isBoundByDuty,
                    isFrontline,
                    isInCombat,
                    isCasting,
                    isActionQueued,
                    animationLockSeconds,
                    isMounted,
                    isMounting,
                    isBetweenAreas,
                    limitCurrent,
                    limitBarUnits,
                    limitBarCount,
                    clientState.TerritoryType,
                    clientState.MapId,
                    territoryName,
                    content.RowId,
                    contentName,
                    frontlineMap,
                    null,
                    [],
                    [],
                    [],
                    [],
                    [],
                    new FrontlineTeamStatus(false, 0, 0, 0, false, "Local player is unavailable."),
                    isFrontline && localObject is not null ? "Frontline territory is stabilizing." : "Local player is unavailable.");
            }

            if (isFrontline)
                entryTrace.Stage("entry-gate", "OK", DateTime.UtcNow);
            entryTrace.Stage("team-tracker", "START", DateTime.UtcNow);
            var localPvpTeam = ReadPvPTeam(localObject);
            var roster = CaptureTeamRoster(localObject, isFrontline, localPvpTeam);
            var local = Convert(localObject, PlayerClassification.Friendly, isRosterMember: true, localPvpTeam);
            entryTrace.Stage("team-tracker", "OK", DateTime.UtcNow);
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
                var pvpTeam = ReadPvPTeam(player);
                var relationship = TeamClassifier.Classify(
                    player.EntityId,
                    local.EntityId,
                    pvpTeam,
                    localPvpTeam,
                    roster.Status.UsesBattalionPvPTeam).Relationship;
                var classification = relationship switch
                {
                    BattlefieldRelationship.Self or BattlefieldRelationship.AllyConfirmed => PlayerClassification.Friendly,
                    BattlefieldRelationship.EnemyConfirmed => PlayerClassification.Enemy,
                    _ => PlayerClassification.Unknown,
                };
                var snapshot = Convert(player, classification, isRosterMember, pvpTeam);
                observedPlayers.Add(snapshot);
                developmentLog.Changed(
                    $"classification-{snapshot.EntityId:X8}",
                    $"{snapshot.Classification}|{snapshot.PvPTeam}|{localPvpTeam}",
                    $"0x{snapshot.EntityId:X8} {snapshot.JobAbbreviation} => {snapshot.Classification}; PvP team={snapshot.PvPTeam}, local team={localPvpTeam}, targetable={snapshot.IsTargetable}.");

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
            var teamStatus = AddTeamProbeExplanation(roster.Status, local, observedPlayers);
            entryTrace.Stage("object-research", "START", DateTime.UtcNow);
            var objectiveObservations = isFrontline && completedStableScans >= 2
                ? CaptureObjectiveObservations(local.Position)
                : [];
            if (mayScan)
                completedStableScans = Math.Min(2, completedStableScans + 1);
            entryTrace.Stage("object-research", "OK", DateTime.UtcNow);

            developmentLog.Changed(
                "classification-roster",
                $"{teamStatus.IsAlliance}|{teamStatus.DeclaredMemberCount}|{teamStatus.ResolvedMemberCount}|{teamStatus.LocalPvPTeam}",
                $"PvP team={teamStatus.LocalPvPTeam}; alliance={teamStatus.IsAlliance}; declared={teamStatus.DeclaredMemberCount}; resolved={teamStatus.ResolvedMemberCount}. {teamStatus.Explanation}");
            developmentLog.Throttled(
                "classification-summary",
                $"Observed {observedPlayers.Count}: {friendlies.Count} friendly (including self), {enemies.Count} enemy, {unknownPlayers.Count} unknown.");

            entryTrace.Stage("game-state", "OK", DateTime.UtcNow);
            return new GameStateSnapshot(
                DateTime.UtcNow,
                clientState.IsLoggedIn,
                isPvP,
                isBoundByDuty,
                isFrontline,
                isInCombat,
                isCasting,
                isActionQueued,
                animationLockSeconds,
                isMounted,
                isMounting,
                isBetweenAreas,
                limitCurrent,
                limitBarUnits,
                limitBarCount,
                clientState.TerritoryType,
                clientState.MapId,
                territoryName,
                content.RowId,
                contentName,
                frontlineMap,
                local,
                friendlies,
                enemies,
                unknownPlayers,
                observedPlayers,
                objectiveObservations,
                teamStatus,
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
        bool isRosterMember,
        byte pvpTeam)
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
            player.CurrentMp,
            player.MaxMp,
            player.ShieldPercentage,
            player.TargetObjectId,
            pvpTeam,
            classification,
            flags,
            flags.HasFlag(StatusFlags.PartyMember),
            flags.HasFlag(StatusFlags.AllianceMember),
            flags.HasFlag(StatusFlags.Hostile),
            isRosterMember,
            ReadNativeInCombat(player),
            player.IsDead || player.CurrentHp == 0,
            player.IsTargetable,
            statuses);
    }

    private TeamRoster CaptureTeamRoster(IBattleChara localPlayer, bool isFrontline, byte localPvpTeam)
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
                new FrontlineTeamStatus(
                    false,
                    0,
                    entityIds.Count,
                    localPvpTeam,
                    isFrontline && TeamClassifier.IsValidFrontlineBattalion(localPvpTeam),
                    $"Alliance roster read failed, but the Frontline Battalion value {(TeamClassifier.IsValidFrontlineBattalion(localPvpTeam) ? "remains available" : "is invalid")} ({ex.GetType().Name})."));
        }

        var usesBattalionPvpTeam = isFrontline && TeamClassifier.IsValidFrontlineBattalion(localPvpTeam);
        var explanation = !isFrontline
            ? "Not in a recognized Frontline duty; PvP-team classification is disabled."
            : usesBattalionPvpTeam
                ? $"Local zero-based Dalamud Battalion team {localPvpTeam} is authoritative; roster flags are diagnostic corroboration only."
                : $"Local Battalion value {localPvpTeam} is outside the live-confirmed Frontline range 0-2; other players remain Unknown.";

        return new TeamRoster(
            entityIds,
            objectIds,
            new FrontlineTeamStatus(isAlliance, declaredCount, entityIds.Count, localPvpTeam, usesBattalionPvpTeam, explanation));
    }

    private static unsafe byte ReadPvPTeam(IBattleChara player)
    {
        var native = (NativeCharacter*)player.Address;
        // 0 is a valid live Frontline team in Dalamud's zero-based Battalion
        // source, so a missing native pointer must use an out-of-range sentinel.
        return native is null ? byte.MaxValue : native->Battalion;
    }

    private static unsafe bool ReadNativeInCombat(IBattleChara player)
    {
        var native = (NativeCharacter*)player.Address;
        return native is not null && native->InCombat;
    }

    private static FrontlineTeamStatus AddTeamProbeExplanation(
        FrontlineTeamStatus status,
        PlayerSnapshot local,
        IReadOnlyList<PlayerSnapshot> observedPlayers)
    {
        if (!status.UsesBattalionPvPTeam)
        {
            var observedTeams = observedPlayers
                .GroupBy(player => player.PvPTeam)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}:{group.Count()}")
                .ToArray();
            var rosterTeams = observedPlayers
                .Where(player => player.IsRosterMember)
                .GroupBy(player => player.PvPTeam)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}:{group.Count()}")
                .ToArray();
            return status with
            {
                Explanation = status.Explanation +
                              $" Raw local Character.Battalion={local.PvPTeam}; local StatusFlags=0x{(uint)local.StatusFlags:X8}." +
                              $" Observed Battalion histogram=[{string.Join(',', observedTeams)}]; roster histogram=[{string.Join(',', rosterTeams)}]." +
                              " No team is inferred from these observations."
            };
        }

        return status;
    }

    private sealed record TeamRoster(
        HashSet<uint> EntityIds,
        HashSet<ulong> ObjectIds,
        FrontlineTeamStatus Status);

    private IReadOnlyList<ObjectiveObservation> CaptureObjectiveObservations(System.Numerics.Vector3 localPosition)
    {
        var observations = new List<ObjectiveObservation>();
        foreach (var gameObject in objects)
        {
            if (gameObject is null || gameObject.ObjectKind is not (ObjectKind.EventObj or ObjectKind.BattleNpc))
                continue;

            var delta = gameObject.Position - localPosition;
            if ((delta.X * delta.X) + (delta.Z * delta.Z) > 300f * 300f)
                continue;

            var battle = gameObject as IBattleChara;
            observations.Add(new ObjectiveObservation(
                gameObject.GameObjectId,
                gameObject.EntityId,
                gameObject.BaseId,
                gameObject.Name.TextValue,
                gameObject.ObjectKind.ToString(),
                gameObject.Position,
                gameObject.IsTargetable,
                gameObject.IsDead,
                battle?.CurrentHp ?? 0,
                battle?.MaxHp ?? 0));
        }

        return observations;
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
