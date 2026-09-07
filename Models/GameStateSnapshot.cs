namespace PvPSentinel.Models;

internal sealed record GameStateSnapshot(
    DateTime CapturedAtUtc,
    bool IsLoggedIn,
    bool IsPvP,
    bool IsFrontline,
    uint TerritoryId,
    uint MapId,
    string MapName,
    PlayerSnapshot? LocalPlayer,
    IReadOnlyList<PlayerSnapshot> Friendlies,
    IReadOnlyList<PlayerSnapshot> Enemies,
    string ReadError)
{
    public static GameStateSnapshot Unavailable(string error) => new(
        DateTime.UtcNow, false, false, false, 0, 0, "Unknown", null, [], [], error);

    public bool IsMachinist => LocalPlayer?.JobId == 31;
}

