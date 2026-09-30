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

    // The first live Worqor session identified these as moving player-like
    // markers and landing/base evidence. Physical overlap with an EventObj
    // cannot make them stable Triumph destinations.
    protected override bool ForceRawObservation(DiscoveryMarkerAggregate aggregate) =>
        aggregate.Observations.Count > 0 &&
        aggregate.Observations.All(marker =>
            marker.DataId == 0 &&
            marker.EventState <= 0 &&
            string.IsNullOrWhiteSpace(marker.Tooltip) &&
            ((marker.IconId is 60359 or 60360 && marker.ObjectiveId == ObservedMovingMarkerSentinel) ||
             (marker.IconId == 60599 &&
              (marker.ObjectiveId == 26 || marker.ObjectiveId == ObservedMovingMarkerSentinel))));
}
