namespace PvPSentinel.Models;

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
