using System.Numerics;
using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore;

internal enum BattlefieldRelationship
{
    Self,
    AllyConfirmed,
    EnemyConfirmed,
    Unknown,
}

internal enum RelationshipConfidence
{
    Unresolved,
    BattalionTeam,
    LocalEntity,
}

internal enum FrontlineMatchLifecycle
{
    Outside,
    PreMatch,
    MatchActive,
    Results,
}

internal enum FrontlineCombatContext
{
    None,
    PvPEnemy,
    ObjectiveCombat,
    StaleGameCombat,
}

internal enum DeathRespawnState
{
    Unavailable,
    Alive,
    Dead,
    Respawned,
}

internal enum ObjectiveLifecycle
{
    Unknown,
    Inactive,
    Preactivating,
    Active,
    Deactivated,
}

internal enum ObjectiveOwner
{
    Neutral,
    OurTeam,
    EnemyTeamA,
    EnemyTeamB,
    Unresolved,
}

internal enum SensorConfidence
{
    Unresolved,
    RuntimeDiscovery,
    PublicStructure,
    LiveVerifiedMapping,
    PhysicalConfirmation,
}

internal enum SensorStatus
{
    Unavailable,
    Healthy,
    Degraded,
    Failed,
}

internal sealed record SelfState(
    uint EntityId,
    byte PvPTeam,
    Vector3 Position,
    bool IsDead,
    uint CurrentHp,
    uint MaxHp);

internal sealed record TrackedPlayer(
    uint EntityId,
    ulong GameObjectId,
    uint JobId,
    string JobAbbreviation,
    byte PvPTeam,
    BattlefieldRelationship Relationship,
    RelationshipConfidence Confidence,
    Vector3 Position,
    uint CurrentHp,
    uint MaxHp,
    bool IsDead,
    bool IsInCombat,
    ulong TargetObjectId,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    float StalenessSeconds)
{
    public bool IsFresh => StalenessSeconds <= PlayerTrackingPolicy.FreshSeconds;
}

internal sealed record BattlefieldCluster(
    int Id,
    BattlefieldRelationship Relationship,
    Vector3 Centroid,
    int MemberCount,
    float AverageStalenessSeconds,
    float MaximumStalenessSeconds,
    int CombatActiveMembers,
    float DistanceFromLocalPlayer,
    int NearbyAllies,
    int NearbyEnemies,
    string NearbyObjective,
    IReadOnlyList<uint> MemberEntityIds);

internal sealed record ObjectivePhysicalConfirmation(
    ulong GameObjectId,
    uint EntityId,
    uint ContentId,
    Vector3 Position,
    uint CurrentHp,
    uint MaxHp,
    bool IsTargetable,
    DateTime LastSeenUtc);

internal sealed record ObjectiveApproachAnchor(
    Vector3 Position,
    bool Validated,
    DateTime LastAttemptUtc,
    string Result);

internal sealed record MapObjectiveState(
    string LogicalId,
    string DisplayName,
    string Kind,
    ObjectiveLifecycle State,
    string Rank,
    ObjectiveOwner Owner,
    string ObservedGrandCompany,
    Vector3? ReferencePosition,
    IReadOnlyList<ObjectiveApproachAnchor> ValidatedApproachAnchors,
    int? ActivationEtaSeconds,
    int? StrengthPercent,
    uint StateId,
    string SensorSource,
    SensorConfidence Confidence,
    DateTime? FirstSeenUtc,
    DateTime? LastSeenUtc,
    ObjectivePhysicalConfirmation? PhysicalConfirmation,
    int NearbyAllies,
    int NearbyEnemies,
    string Evidence);

internal sealed record FrontlineResearchObject(
    string ResearchId,
    string Name,
    string ObjectKind,
    ulong GameObjectId,
    uint EntityId,
    uint BaseId,
    Vector3 Position,
    bool IsTargetable,
    bool IsDead,
    uint CurrentHp,
    uint MaxHp,
    bool CurrentlyObserved,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    string Evidence);

internal sealed record FrontlineMatchState(
    FrontlineMatchLifecycle Lifecycle,
    TimeSpan? TimeRemaining,
    int? ScoreCap,
    IReadOnlyList<string> GrandCompanies,
    bool ResultsDetected,
    string TeamScores,
    string Evidence)
{
    public bool DutyStarted { get; init; }
    public bool HeaderVisible { get; init; }
    public string ActiveCorroboration { get; init; } = "None";
}

internal sealed record DeathRespawnSnapshot(
    DeathRespawnState State,
    int Deaths,
    int Respawns,
    DateTime? LastTransitionUtc);

internal sealed record CombatContextSnapshot(
    FrontlineCombatContext Context,
    bool BlocksMovement,
    int EnemiesTargetingUs,
    int NearbyEnemies,
    string Evidence);

internal sealed record SensorHealthEntry(
    string Sensor,
    SensorStatus Status,
    DateTime? LastSuccessUtc,
    int ErrorCount,
    string Detail);

internal sealed record RelationshipCounts(int Self, int Allies, int Enemies, int Unknown);

internal sealed record BattlefieldState(
    DateTime CapturedAtUtc,
    FrontlineMap Map,
    uint TerritoryId,
    string MapName,
    string ActiveAdapter,
    SelfState? Self,
    byte LocalPvPTeam,
    IReadOnlyList<TrackedPlayer> TrackedPlayers,
    RelationshipCounts Counts,
    IReadOnlyList<BattlefieldCluster> AlliedClusters,
    IReadOnlyList<BattlefieldCluster> EnemyClusters,
    IReadOnlyList<MapObjectiveState> Objectives,
    IReadOnlyList<FrontlineResearchObject> ResearchObjects,
    IReadOnlyList<string> ResearchNotes,
    FrontlineMatchState Match,
    CombatContextSnapshot Combat,
    DeathRespawnSnapshot DeathRespawn,
    IReadOnlyList<SensorHealthEntry> SensorHealth,
    RetainedMatchSummary? LastMatchSummary,
    string Explanation)
{
    public static BattlefieldState Unavailable(DateTime now, string explanation) => new(
        now,
        FrontlineMap.Unknown,
        0,
        "UNRESOLVED",
        "NONE",
        null,
        0,
        [],
        new RelationshipCounts(0, 0, 0, 0),
        [],
        [],
        [],
        [],
        [],
        new FrontlineMatchState(FrontlineMatchLifecycle.Outside, null, null, [], false,
            "UNAVAILABLE / OPTIONAL", "No active Frontline duty."),
        new CombatContextSnapshot(FrontlineCombatContext.None, false, 0, 0, "No active Frontline duty."),
        new DeathRespawnSnapshot(DeathRespawnState.Unavailable, 0, 0, null),
        [],
        null,
        explanation);
}

internal sealed record ObjectiveResearchSummary(
    string LogicalId,
    Vector3? ReferencePosition,
    string State,
    string Owner,
    string Kind,
    string Evidence,
    DateTime? FirstSeenUtc,
    DateTime? LastSeenUtc);

internal sealed record ResearchObjectSummary(
    string ResearchId,
    string Name,
    string ObjectKind,
    ulong GameObjectId,
    uint EntityId,
    uint BaseId,
    Vector3 Position,
    bool IsTargetable,
    bool IsDead,
    bool CurrentlyObserved,
    uint CurrentHp,
    uint MaxHp,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    string Evidence);

internal sealed record ObjectiveTransitionSummary(
    DateTime TimestampUtc,
    string EventName,
    string LogicalId,
    string Evidence);

internal sealed record ObjectiveChange(string EventName, string LogicalId, string Detail);

internal sealed record RetainedMatchSummary(
    FrontlineMap PrimaryTestMap,
    uint TerritoryId,
    uint ContentFinderConditionId,
    DateTime MapEntryTimeUtc,
    DateTime? MatchStartTimeUtc,
    DateTime? MatchEndTimeUtc,
    DateTime MapExitTimeUtc,
    byte LocalPvPTeam,
    IReadOnlyList<string> LogicalObjectivesSeen,
    int ObjectiveCount,
    int PeakSelf,
    int PeakAllies,
    int PeakEnemies,
    int PeakUnknown,
    int Deaths,
    int Respawns,
    FrontlineMatchLifecycle FinalMatchState,
    bool ResultsDetected,
    int SensorErrors,
    int NavigationRequests,
    int NavigationArrivals,
    int NavigationFailures,
    int NavigationPathFailures,
    int NavigationStops,
    int NavigationStuckEvents,
    int NavigationRouteRejections,
    IReadOnlyList<ObjectiveResearchSummary> ObjectiveResearch,
    IReadOnlyList<ResearchObjectSummary> ResearchObjects,
    IReadOnlyList<ObjectiveTransitionSummary> ObjectiveTransitions,
    IReadOnlyList<string> UnresolvedObservations);

internal sealed record FrontlineMapMarkerObservation(
    uint IconId,
    uint DataId,
    uint ObjectiveId,
    Vector3 Position,
    string Tooltip,
    int EndTimestamp,
    sbyte EventState,
    string Source);

internal sealed record FrontlineUiObservation(
    bool HeaderVisible,
    bool ResultsVisible,
    TimeSpan? TimeRemaining,
    int? ScoreCap,
    IReadOnlyList<string> GrandCompanies,
    string WideTextAnnouncement,
    string Evidence);
