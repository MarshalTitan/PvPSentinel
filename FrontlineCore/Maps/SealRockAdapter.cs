using System.Numerics;
using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore.Maps;

internal sealed class SealRockAdapter : FrontlineMapAdapterBase
{
    public const uint TerritoryId = 431;
    private readonly Dictionary<string, string> logicalIdByPosition = new(StringComparer.Ordinal);
    private int nextLogicalIndex = 1;

    public override FrontlineMap Map => FrontlineMap.SealRock;
    public override string Name => "Seal Rock";

    public override void Reset(DateTime now)
    {
        Records.Clear();
        logicalIdByPosition.Clear();
        nextLogicalIndex = 1;
    }

    public override IReadOnlyList<ObjectiveChange> Update(
        DateTime now,
        IReadOnlyList<FrontlineMapMarkerObservation> markers,
        IReadOnlyList<ObjectiveObservation> physicalObservations,
        IReadOnlyList<TrackedPlayer> players)
    {
        var changes = new List<ObjectiveChange>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var aggregate in SealRockObjectiveAggregator.Aggregate(markers))
        {
            if (!logicalIdByPosition.TryGetValue(aggregate.PositionKey, out var logicalId))
            {
                logicalId = $"SR-{nextLogicalIndex:00}";
                nextLogicalIndex++;
                logicalIdByPosition[aggregate.PositionKey] = logicalId;
                Records[logicalId] = new MutableObjective(logicalId, $"Allagan Tomelith {logicalId}", "TOMELITH", nextLogicalIndex);
                changes.Add(new ObjectiveChange("objective_discovered", logicalId,
                    $"position={aggregate.PositionKey}; paired_evidence={aggregate.EvidenceCount}"));
            }

            var record = Records[logicalId];
            seen.Add(logicalId);
            var beforeState = record.State;
            var beforeRank = record.Rank;
            var beforeGc = record.ObservedGrandCompany;
            record.State = aggregate.State;
            record.Rank = aggregate.Rank;
            record.ObservedGrandCompany = aggregate.GrandCompany;
            record.Owner = aggregate.DirectOwner;
            record.StateId = aggregate.StateId;
            record.ReferencePosition = aggregate.Position;
            record.SensorSource = aggregate.Source;
            record.Confidence = aggregate.Confidence;
            record.FirstSeenUtc ??= now;
            record.LastSeenUtc = now;

            if (beforeState != record.State && beforeState != ObjectiveLifecycle.Unknown)
                changes.Add(new ObjectiveChange("objective_state_changed", logicalId, $"{beforeState} -> {record.State}"));
            if (beforeRank != record.Rank && beforeRank != "UNRESOLVED")
                changes.Add(new ObjectiveChange("objective_rank_changed", logicalId, $"{beforeRank} -> {record.Rank}"));
            if (beforeGc != record.ObservedGrandCompany && beforeGc != "UNRESOLVED")
                changes.Add(new ObjectiveChange("objective_owner_changed", logicalId,
                    $"{beforeGc} -> {record.ObservedGrandCompany}; normalized={record.Owner}"));
        }

        foreach (var record in Records.Values)
        {
            if (seen.Contains(record.LogicalId) || record.LastSeenUtc is null ||
                now - record.LastSeenUtc.Value < TimeSpan.FromSeconds(3) ||
                record.State is ObjectiveLifecycle.Deactivated or ObjectiveLifecycle.Inactive)
                continue;
            var before = record.State;
            record.State = ObjectiveLifecycle.Deactivated;
            record.Owner = ObjectiveOwner.Unresolved;
            record.SensorSource = "AgentMap marker disappearance";
            changes.Add(new ObjectiveChange("objective_state_changed", record.LogicalId,
                $"{before} -> {record.State}; objective marker no longer present"));
        }

        foreach (var record in Records.Values)
        {
            var nearby = CountNearby(record.ReferencePosition, players);
            record.NearbyAllies = nearby.Allies;
            record.NearbyEnemies = nearby.Enemies;
        }
        return changes;
    }
}
