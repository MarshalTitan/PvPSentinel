using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;

namespace PvPSentinel.Models;

internal sealed record StatusSnapshot(uint Id, string Name, float RemainingSeconds);

internal sealed record PlayerSnapshot(
    ulong GameObjectId,
    uint EntityId,
    string Name,
    uint JobId,
    string JobAbbreviation,
    Vector3 Position,
    uint CurrentHp,
    uint MaxHp,
    uint CurrentMp,
    uint MaxMp,
    byte ShieldPercent,
    ulong TargetObjectId,
    PlayerClassification Classification,
    StatusFlags StatusFlags,
    bool PartyMemberFlag,
    bool AllianceMemberFlag,
    bool HostileFlag,
    bool IsRosterMember,
    bool IsDead,
    bool IsTargetable,
    IReadOnlyList<StatusSnapshot> Statuses)
{
    public float HpPercent => MaxHp == 0 ? 0f : CurrentHp * 100f / MaxHp;
    public bool IsFriendly => Classification == PlayerClassification.Friendly;
    public bool IsEnemy => Classification == PlayerClassification.Enemy;
    public bool HasStatus(string name) => Statuses.Any(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    public bool HasStatusExact(string name) => Statuses.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    public bool HasStatus(uint id) => Statuses.Any(s => s.Id == id);
    public string BattleHigh => Statuses.FirstOrDefault(s => s.Name.Contains("Battle High", StringComparison.OrdinalIgnoreCase))?.Name ?? "None";
}
