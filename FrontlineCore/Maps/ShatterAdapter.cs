using System.Numerics;
using PvPSentinel.FrontlineCore.Diagnostics;
using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore.Maps;

internal sealed class ShatterAdapter : FrontlineMapAdapterBase
{
    public const uint TerritoryId = 554;

    private static readonly IReadOnlyDictionary<uint, string> LargeContentIds =
        new Dictionary<uint, string>
        {
            [4822] = "A1",
            [4823] = "A2",
            [4824] = "A3",
            [4825] = "A4",
        };

    public override FrontlineMap Map => FrontlineMap.FieldsOfGlory;
    public override string Name => "Shatter";

    public override void Reset(DateTime now)
    {
        Records.Clear();
        for (var index = 1; index <= 4; index++)
            Records[$"A{index}"] = new MutableObjective($"A{index}", $"Icebound Tomelith A{index}", "LARGE", index);
        for (var index = 1; index <= 15; index++)
            Records[$"B{index}"] = new MutableObjective($"B{index}", $"Icebound Tomelith B{index}", "SMALL", 100 + index);
    }

    public override IReadOnlyList<ObjectiveChange> Update(
        DateTime now,
        IReadOnlyList<FrontlineMapMarkerObservation> markers,
        IReadOnlyList<ObjectiveObservation> physicalObservations,
        IReadOnlyList<TrackedPlayer> players)
    {
        var changes = new List<ObjectiveChange>();
        foreach (var marker in markers)
        {
            var mapped = ShatterObjectivePolicy.Resolve(marker);
            if (mapped is null)
                continue;
            var logicalId = ShatterObjectivePolicy.ParseLogicalId(marker.Tooltip);
            if (logicalId is null || !Records.TryGetValue(logicalId, out var record) ||
                !record.Kind.Equals(mapped.Value.Kind, StringComparison.Ordinal))
                continue;

            var wasDiscovered = record.FirstSeenUtc is not null;
            var beforeState = record.State;
            record.State = mapped.Value.State;
            record.StateId = mapped.Value.StateId;
            record.ReferencePosition = marker.Position;
            record.ActivationEtaSeconds = ShatterObjectivePolicy.ParseActivationEtaSeconds(marker.Tooltip);
            record.StrengthPercent = ShatterObjectivePolicy.ParseStrengthPercent(marker.Tooltip);
            record.SensorSource = marker.Source;
            record.Confidence = SensorConfidence.LiveVerifiedMapping;
            record.Evidence = PrivacySanitizer.Sanitize(
                $"icon={marker.IconId}; data={marker.DataId}; objective={marker.ObjectiveId}; state={mapped.Value.StateId}; text={marker.Tooltip}");
            record.FirstSeenUtc ??= now;
            record.LastSeenUtc = now;
            if (!wasDiscovered)
                changes.Add(new ObjectiveChange("objective_discovered", record.LogicalId,
                    $"kind={record.Kind}; state={record.State}; source={record.SensorSource}"));
            else if (beforeState != record.State)
                changes.Add(new ObjectiveChange("objective_state_changed", record.LogicalId,
                    $"{beforeState} -> {record.State}"));
        }

        UpdatePhysical(now, physicalObservations, changes);
        foreach (var record in Records.Values)
        {
            var nearby = CountNearby(record.ReferencePosition, players);
            record.NearbyAllies = nearby.Allies;
            record.NearbyEnemies = nearby.Enemies;
        }
        return changes;
    }

    private void UpdatePhysical(
        DateTime now,
        IReadOnlyList<ObjectiveObservation> observations,
        List<ObjectiveChange> changes)
    {
        var confirmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var observation in observations)
        {
            string? logicalId = LargeContentIds.GetValueOrDefault(observation.BaseId);
            if (logicalId is null && observation.MaxHp == 300_000)
            {
                logicalId = Records.Values
                    .Where(record => record.Kind == "SMALL" && record.State == ObjectiveLifecycle.Active && record.ReferencePosition is not null)
                    .Select(record => (record.LogicalId, Distance: HorizontalDistance(record.ReferencePosition!.Value, observation.Position)))
                    .Where(item => item.Distance <= 35f)
                    .OrderBy(item => item.Distance)
                    .Select(item => item.LogicalId)
                    .FirstOrDefault();
            }
            if (logicalId is null || !Records.TryGetValue(logicalId, out var record))
                continue;

            confirmed.Add(logicalId);
            var prior = record.PhysicalConfirmation?.GameObjectId;
            record.PhysicalConfirmation = new ObjectivePhysicalConfirmation(
                observation.GameObjectId,
                observation.EntityId,
                observation.BaseId,
                observation.Position,
                observation.CurrentHp,
                observation.MaxHp,
                observation.IsTargetable,
                now);
            if (record.ReferencePosition is null)
                record.ReferencePosition = observation.Position;
            if (record.Confidence < SensorConfidence.PhysicalConfirmation)
                record.Confidence = SensorConfidence.PhysicalConfirmation;
            if (prior != observation.GameObjectId)
                changes.Add(new ObjectiveChange("objective_physical_confirmation", logicalId,
                    $"content={observation.BaseId}; max_hp={observation.MaxHp}"));
        }

        foreach (var record in Records.Values)
        {
            if (record.PhysicalConfirmation is { } physical && !confirmed.Contains(record.LogicalId) &&
                now - physical.LastSeenUtc > TimeSpan.FromSeconds(2))
                record.PhysicalConfirmation = null;
        }
    }

}
