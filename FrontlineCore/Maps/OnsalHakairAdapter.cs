using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore.Maps;

internal sealed class OnsalHakairAdapter() : DiscoveryFrontlineAdapter(new DiscoveryMapProfile(
    FrontlineMap.OnsalHakair,
    888,
    701,
    "Onsal Hakair (discovery)",
    "ONS",
    "onsal"))
{
    private const uint ObservedMovingMarkerSentinel = 0xFF000000;

    // The first complete live session showed the same Ovoo marker alternating
    // between 446 and 448 with no semantic/text change. Preserve the raw IDs in
    // research evidence, but treat the pair as one identity for stability and
    // transition comparisons so it cannot generate 4 Hz lifecycle noise.
    protected override uint NormalizeObjectiveIdForStability(uint objectiveId) => objectiveId switch
    {
        446 or 448 => 446,
        _ => base.NormalizeObjectiveIdForStability(objectiveId),
    };

    // Live evidence identified 60359/60360 + 4278190080 as moving player-like
    // markers and 60599 + 62/4278190080 as landing/base evidence. Their temporary
    // proximity to an EventObj must never promote them into ONS navigation buttons.
    protected override bool ForceRawObservation(DiscoveryMarkerAggregate aggregate) =>
        aggregate.Observations.Count > 0 &&
        aggregate.Observations.All(marker =>
            marker.DataId == 0 &&
            marker.EventState <= 0 &&
            string.IsNullOrWhiteSpace(marker.Tooltip) &&
            ((marker.IconId is 60359 or 60360 && marker.ObjectiveId == ObservedMovingMarkerSentinel) ||
             (marker.IconId == 60599 &&
              (marker.ObjectiveId == 62 || marker.ObjectiveId == ObservedMovingMarkerSentinel))));
}
