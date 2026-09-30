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
                (marker.ObjectiveId is 0 or 26 or 62 or 181 || marker.ObjectiveId == ObservedMovingMarkerSentinel))))));
}
