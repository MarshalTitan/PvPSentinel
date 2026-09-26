namespace PvPSentinel.Models;

internal sealed record FrontlineTeamStatus(
    bool IsAlliance,
    int DeclaredMemberCount,
    int ResolvedMemberCount,
    bool CanClassifyNonMembers,
    string Explanation);

internal sealed record ObjectiveObservation(
    ulong GameObjectId,
    uint EntityId,
    uint BaseId,
    string Name,
    string ObjectKind,
    System.Numerics.Vector3 Position,
    bool IsTargetable,
    bool IsDead,
    uint CurrentHp,
    uint MaxHp);

internal sealed record GameStateSnapshot(
    DateTime CapturedAtUtc,
    bool IsLoggedIn,
    bool IsPvP,
    bool IsBoundByDuty,
    bool IsFrontline,
    bool IsInCombat,
    bool IsCasting,
    bool IsActionQueued,
    float AnimationLockSeconds,
    bool IsMounted,
    bool IsMounting,
    bool IsBetweenAreas,
    ushort LimitBreakCurrentUnits,
    ushort LimitBreakBarUnits,
    byte LimitBreakBarCount,
    uint TerritoryId,
    uint MapId,
    string MapName,
    uint ContentFinderConditionId,
    string ContentName,
    FrontlineMap FrontlineMap,
    PlayerSnapshot? LocalPlayer,
    IReadOnlyList<PlayerSnapshot> Friendlies,
    IReadOnlyList<PlayerSnapshot> Enemies,
    IReadOnlyList<PlayerSnapshot> UnknownPlayers,
    IReadOnlyList<PlayerSnapshot> ObservedPlayers,
    IReadOnlyList<ObjectiveObservation> ObjectiveObservations,
    FrontlineTeamStatus TeamStatus,
    string ReadError)
{
    public static GameStateSnapshot Unavailable(string error) => new(
        DateTime.UtcNow, false, false, false, false, false, false, false, 0f, false, false, false, 0, 0, 0,
        0, 0, "Unknown", 0, "Unknown", FrontlineMap.Unknown, null, [], [], [], [], [],
        new FrontlineTeamStatus(false, 0, 0, false, "Team roster is unavailable."), error);

    public bool IsMachinist => LocalPlayer?.JobId == 31;
    public float LimitBreakPercent => LimitBreakBarUnits == 0
        ? 0f
        : Math.Clamp(LimitBreakCurrentUnits * 100f / LimitBreakBarUnits, 0f, 100f);
    public bool IsClassificationReliable =>
        TeamStatus.CanClassifyNonMembers &&
        UnknownPlayers.Count == 0 &&
        Friendlies.Count is >= 1 and <= 24 &&
        Friendlies.Select(player => player.EntityId).Distinct().Count() == Friendlies.Count;

    public string ClassificationReliabilityExplanation => !TeamStatus.CanClassifyNonMembers
        ? TeamStatus.Explanation
        : UnknownPlayers.Count > 0
            ? $"{UnknownPlayers.Count} observed player(s) remain Unknown."
            : Friendlies.Count > 24
                ? $"Observed friendly count {Friendlies.Count} exceeds the 24-player Frontline team maximum."
                : Friendlies.Select(player => player.EntityId).Distinct().Count() != Friendlies.Count
                    ? "Duplicate friendly entity IDs were observed."
                    : "Positive team membership is authoritative; all other observed PCs are classified as enemies.";
}
