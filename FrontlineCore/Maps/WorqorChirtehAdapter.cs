using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore.Maps;

internal sealed class WorqorChirtehAdapter() : DiscoveryFrontlineAdapter(new DiscoveryMapProfile(
    FrontlineMap.WorqorChirteh,
    1313,
    1080,
    "Worqor Chirteh (discovery)",
    "WOR",
    "worqor"))
{
    private const uint ObservedMovingMarkerSentinel = 0xFF000000;

    public override IReadOnlyList<string> ResearchNotes =>
    [
        "Named Triumph marker text now identifies activating, unclaimed, and claimed states plus observed rank. Claimed faction 4/5/6 is retained as evidence; its mapping to our team remains UNRESOLVED.",
        ..base.ResearchNotes.Skip(1),
    ];

    protected override void UpdateKnownLocation(
        MutableObjective record, DiscoveryMarkerAggregate aggregate, List<ObjectiveChange> changes)
    {
        foreach (var marker in aggregate.Observations)
        {
            var signal = WorqorTriumphSignals.Parse(marker.Tooltip);
            if (signal is null)
                continue;
            var prior = record.DisplayName;
            var phaseName = signal.Phase switch
            {
                WorqorTriumphPhase.Activating => "activating",
                WorqorTriumphPhase.Unclaimed => "unclaimed",
                WorqorTriumphPhase.Claimed => "claimed (owner unresolved)",
                _ => "unresolved",
            };
            record.DisplayName = $"Triumph {signal.Number} — {phaseName}";
            record.State = signal.Phase == WorqorTriumphPhase.Activating
                ? ObjectiveLifecycle.Preactivating : ObjectiveLifecycle.Active;
            record.Owner = signal.Phase == WorqorTriumphPhase.Claimed
                ? ObjectiveOwner.Unresolved : ObjectiveOwner.Neutral;
            record.Rank = signal.Rank;
            record.ActivationEtaSeconds = signal.ActivationEtaSeconds;
            record.StateId = marker.IconId;
            record.SensorSource = $"{marker.Source} (Triumph tooltip)";
            if (!string.Equals(prior, record.DisplayName, StringComparison.Ordinal))
                changes.Add(new ObjectiveChange("worqor_triumph_state", record.LogicalId,
                    $"phase={signal.Phase}; rank={signal.Rank}; marker_faction={(signal.MarkerFaction?.ToString() ?? "UNRESOLVED")}; position={record.ReferencePosition}; ownership_mapping=UNRESOLVED"));
            break;
        }
    }

    protected override void OnLocationUnobserved(MutableObjective record)
    {
        record.State = ObjectiveLifecycle.Unknown;
        record.Owner = ObjectiveOwner.Unresolved;
        record.ActivationEtaSeconds = null;
    }

    // Live Worqor traces exposed the same non-objective player/Levemete markers
    // seen on Secure, 60359-60361 as moving player-like markers, and two
    // landing/base icon families. An EventObj at a base does not turn its map
    // marker into a Triumph destination. Keep the exact raw identities visible.
    protected override bool ForceRawObservation(DiscoveryMarkerAggregate aggregate) =>
        aggregate.Observations.Count > 0 &&
        aggregate.Observations.All(marker =>
            marker.DataId == 0 &&
            ((marker.IconId == 71121 && marker.ObjectiveId == 721223) ||
             (marker.IconId == 71041 && marker.ObjectiveId == 393222) ||
             (marker.EventState <= 0 && string.IsNullOrWhiteSpace(marker.Tooltip) &&
              ((marker.IconId is >= 60359 and <= 60361 && marker.ObjectiveId == ObservedMovingMarkerSentinel) ||
               (marker.IconId is 60573 or 60574 &&
                (marker.ObjectiveId == 0 || marker.ObjectiveId == ObservedMovingMarkerSentinel)) ||
               (marker.IconId is >= 60597 and <= 60599 &&
                (marker.ObjectiveId is 0 or 26 or 62 or 181 || marker.ObjectiveId == ObservedMovingMarkerSentinel ||
                 marker.IconId == 60599 && marker.ObjectiveId == 210))))));
}
