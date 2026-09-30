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

    // Both live Worqor teams exposed 60359-60361 as moving player-like markers
    // and 60598/60599 as landing/base evidence (the adjacent 60597 is the
    // conservative third-team variant). Physical overlap with an EventObj
    // cannot make these otherwise anonymous markers Triumph destinations.
    protected override bool ForceRawObservation(DiscoveryMarkerAggregate aggregate) =>
        aggregate.Observations.Count > 0 &&
        aggregate.Observations.All(marker =>
            marker.DataId == 0 &&
            marker.EventState <= 0 &&
            string.IsNullOrWhiteSpace(marker.Tooltip) &&
            ((marker.IconId is >= 60359 and <= 60361 && marker.ObjectiveId == ObservedMovingMarkerSentinel) ||
             (marker.IconId is >= 60597 and <= 60599 &&
              (marker.ObjectiveId is 26 or 62 or 181 || marker.ObjectiveId == ObservedMovingMarkerSentinel))));
}
