namespace PvPSentinel.Models;

internal sealed record FrontlineTeamStatus(
    bool IsAlliance,
    int DeclaredMemberCount,
    int ResolvedMemberCount,
    bool CanClassifyNonMembers,
    string Explanation);

internal sealed record GameStateSnapshot(
    DateTime CapturedAtUtc,
    bool IsLoggedIn,
    bool IsPvP,
    bool IsBoundByDuty,
    bool IsFrontline,
    uint TerritoryId,
    uint MapId,
    string MapName,
    PlayerSnapshot? LocalPlayer,
    IReadOnlyList<PlayerSnapshot> Friendlies,
    IReadOnlyList<PlayerSnapshot> Enemies,
    IReadOnlyList<PlayerSnapshot> UnknownPlayers,
    IReadOnlyList<PlayerSnapshot> ObservedPlayers,
    FrontlineTeamStatus TeamStatus,
    string ReadError)
{
    public static GameStateSnapshot Unavailable(string error) => new(
        DateTime.UtcNow, false, false, false, false, 0, 0, "Unknown", null, [], [], [], [],
        new FrontlineTeamStatus(false, 0, 0, false, "Team roster is unavailable."), error);

    public bool IsMachinist => LocalPlayer?.JobId == 31;
    public bool IsClassificationReliable => TeamStatus.CanClassifyNonMembers && UnknownPlayers.Count == 0;
}
