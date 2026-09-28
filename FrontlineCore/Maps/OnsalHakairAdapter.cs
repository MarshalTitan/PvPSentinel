using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore.Maps;

internal sealed class OnsalHakairAdapter() : DiscoveryFrontlineAdapter(new DiscoveryMapProfile(
    FrontlineMap.OnsalHakair,
    888,
    701,
    "Onsal Hakair (discovery)",
    "ONS",
    "onsal"));
