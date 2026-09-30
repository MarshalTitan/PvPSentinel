using System.Numerics;
using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore.Maps;

internal interface IFrontlineMapAdapter
{
    FrontlineMap Map { get; }
    string Name { get; }
    IReadOnlyList<MapObjectiveState> Objectives { get; }
    IReadOnlyList<FrontlineResearchObject> ResearchObjects { get; }
    IReadOnlyList<string> ResearchNotes { get; }
    void Reset(DateTime now);
    IReadOnlyList<ObjectiveChange> Update(
        DateTime now,
        IReadOnlyList<FrontlineMapMarkerObservation> markers,
        IReadOnlyList<ObjectiveObservation> physicalObservations,
        IReadOnlyList<TrackedPlayer> players);
    bool RecordArrived(string logicalId, Vector3 approachPoint, DateTime now);
}

internal abstract class FrontlineMapAdapterBase : IFrontlineMapAdapter
{
    protected readonly Dictionary<string, MutableObjective> Records = new(StringComparer.OrdinalIgnoreCase);

    public abstract FrontlineMap Map { get; }
    public abstract string Name { get; }

    public IReadOnlyList<MapObjectiveState> Objectives => Records.Values
        .OrderBy(record => record.SortOrder)
        .ThenBy(record => record.LogicalId, StringComparer.Ordinal)
        .Select(record => record.Snapshot())
        .ToArray();
    public virtual IReadOnlyList<FrontlineResearchObject> ResearchObjects => [];
    public virtual IReadOnlyList<string> ResearchNotes => [];

    public abstract void Reset(DateTime now);
    public abstract IReadOnlyList<ObjectiveChange> Update(
        DateTime now,
        IReadOnlyList<FrontlineMapMarkerObservation> markers,
        IReadOnlyList<ObjectiveObservation> physicalObservations,
        IReadOnlyList<TrackedPlayer> players);

    public bool RecordArrived(string logicalId, Vector3 approachPoint, DateTime now)
    {
        if (!Records.TryGetValue(logicalId, out var record))
            return false;

        var existing = record.ApproachAnchors.FindIndex(anchor =>
            Vector3.Distance(anchor.Position, approachPoint) <= 2f);
        var validated = new ObjectiveApproachAnchor(approachPoint, true, now, "ARRIVED");
        if (existing >= 0)
            record.ApproachAnchors[existing] = validated;
        else
            record.ApproachAnchors.Add(validated);
        while (record.ApproachAnchors.Count > 5)
            record.ApproachAnchors.RemoveAt(0);
        return true;
    }

    protected static (int Allies, int Enemies) CountNearby(
        Vector3? position,
        IReadOnlyList<TrackedPlayer> players,
        float radius = 30f)
    {
        if (position is null)
            return (0, 0);
        var allies = 0;
        var enemies = 0;
        foreach (var player in players.Where(player => player.IsFresh && !player.IsDead))
        {
            if (HorizontalDistance(player.Position, position.Value) > radius)
                continue;
            if (player.Relationship == BattlefieldRelationship.AllyConfirmed)
                allies++;
            else if (player.Relationship == BattlefieldRelationship.EnemyConfirmed)
                enemies++;
        }
        return (allies, enemies);
    }

    protected static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    protected sealed class MutableObjective(
        string logicalId,
        string displayName,
        string kind,
        int sortOrder)
    {
        public string LogicalId { get; } = logicalId;
        public string DisplayName { get; set; } = displayName;
        public string Kind { get; } = kind;
        public int SortOrder { get; } = sortOrder;
        public ObjectiveLifecycle State { get; set; } = ObjectiveLifecycle.Unknown;
        public string Rank { get; set; } = "UNRESOLVED";
        public ObjectiveOwner Owner { get; set; } = ObjectiveOwner.Unresolved;
        public string ObservedGrandCompany { get; set; } = "UNRESOLVED";
        public Vector3? ReferencePosition { get; set; }
        public List<ObjectiveApproachAnchor> ApproachAnchors { get; } = [];
        public int? ActivationEtaSeconds { get; set; }
        public int? StrengthPercent { get; set; }
        public uint StateId { get; set; }
        public string SensorSource { get; set; } = "UNRESOLVED";
        public SensorConfidence Confidence { get; set; } = SensorConfidence.Unresolved;
        public DateTime? FirstSeenUtc { get; set; }
        public DateTime? LastSeenUtc { get; set; }
        public ObjectivePhysicalConfirmation? PhysicalConfirmation { get; set; }
        public int NearbyAllies { get; set; }
        public int NearbyEnemies { get; set; }
        public string Evidence { get; set; } = "UNRESOLVED";

        public MapObjectiveState Snapshot() => new(
            LogicalId,
            DisplayName,
            Kind,
            State,
            Rank,
            Owner,
            ObservedGrandCompany,
            ReferencePosition,
            ApproachAnchors.Where(anchor => anchor.Validated).ToArray(),
            ActivationEtaSeconds,
            StrengthPercent,
            StateId,
            SensorSource,
            Confidence,
            FirstSeenUtc,
            LastSeenUtc,
            PhysicalConfirmation,
            NearbyAllies,
            NearbyEnemies,
            Evidence);
    }
}
