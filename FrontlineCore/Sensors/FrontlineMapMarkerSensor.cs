using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.STD;
using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore.Sensors;

internal sealed record MarkerCapture(
    IReadOnlyList<FrontlineMapMarkerObservation> Markers,
    string Source,
    string BootstrapAction);

internal sealed unsafe class FrontlineMapMarkerSensor
{
    private DateTime firstActiveUtc = DateTime.MinValue;
    private DateTime lastDirectorProbeUtc = DateTime.MinValue;
    private DateTime bootstrapOpenedUtc = DateTime.MinValue;
    private bool bootstrapAttempted;
    private bool bootstrapOwned;
    private IReadOnlyList<FrontlineMapMarkerObservation> cachedDirectorMarkers = [];

    public MarkerCapture Capture(GameStateSnapshot game, FrontlineMatchLifecycle lifecycle)
    {
        var agent = AgentMap.Instance();
        var mapMarkers = ReadAgentMarkers(agent);
        if (game.FrontlineMap != FrontlineMap.WorqorChirteh)
            return new MarkerCapture(mapMarkers, "AgentMap.EventMarkers", string.Empty);

        if (lifecycle == FrontlineMatchLifecycle.MatchActive)
        {
            if (firstActiveUtc == DateTime.MinValue)
                firstActiveUtc = game.CapturedAtUtc;
        }
        else
        {
            firstActiveUtc = DateTime.MinValue;
        }

        // The fifth Worqor trace had no Triumph markers until the map was
        // opened. Query the event director without presenting the map first.
        if (!HasNamedTriumph(mapMarkers) &&
            game.CapturedAtUtc - lastDirectorProbeUtc >= TimeSpan.FromSeconds(1))
        {
            lastDirectorProbeUtc = game.CapturedAtUtc;
            cachedDirectorMarkers = ReadDirectorMarkers(game.TerritoryId);
        }

        var action = string.Empty;
        if (bootstrapOwned && game.CapturedAtUtc - bootstrapOpenedUtc >= TimeSpan.FromSeconds(1.25))
        {
            if (agent is not null)
                agent->Hide();
            bootstrapOwned = false;
            action = "closed-owned-map-after-marker-bootstrap";
        }

        var markers = HasNamedTriumph(mapMarkers) ? mapMarkers
            : HasNamedTriumph(cachedDirectorMarkers) ? cachedDirectorMarkers
            : mapMarkers.Count > 0 ? mapMarkers : cachedDirectorMarkers;
        if (!HasNamedTriumph(markers) && !bootstrapAttempted &&
            firstActiveUtc != DateTime.MinValue &&
            game.CapturedAtUtc - firstActiveUtc >= TimeSpan.FromSeconds(6) &&
            game.MapId != 0 && agent is not null &&
            !game.IsInCombat && !game.IsBetweenAreas && game.LocalPlayer is { IsDead: false } &&
            !agent->IsAddonShown())
        {
            bootstrapAttempted = true;
            agent->OpenMap(game.MapId, game.TerritoryId);
            bootstrapOwned = true;
            bootstrapOpenedUtc = game.CapturedAtUtc;
            action = "opened-map-once-to-initialize-worqor-markers";
        }

        return new MarkerCapture(markers,
            ReferenceEquals(markers, cachedDirectorMarkers) ? "EventFramework.GetEventMapMarkers" : "AgentMap.EventMarkers",
            action);
    }

    public void Reset()
    {
        if (bootstrapOwned)
        {
            var agent = AgentMap.Instance();
            if (agent is not null)
                agent->Hide();
        }
        bootstrapOwned = false;
        bootstrapAttempted = false;
        firstActiveUtc = DateTime.MinValue;
        lastDirectorProbeUtc = DateTime.MinValue;
        bootstrapOpenedUtc = DateTime.MinValue;
        cachedDirectorMarkers = [];
    }

    private static bool HasNamedTriumph(IReadOnlyList<FrontlineMapMarkerObservation> markers) =>
        markers.Any(marker => marker.Tooltip.StartsWith("Triumph ", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<FrontlineMapMarkerObservation> ReadAgentMarkers(AgentMap* agent)
    {
        if (agent is null)
            return [];
        var result = new List<FrontlineMapMarkerObservation>();
        var count = Math.Min(agent->EventMarkersPtrs.Count, 512);
        for (var index = 0; index < count; index++)
        {
            var marker = agent->EventMarkersPtrs[index].Value;
            if (marker is not null)
                result.Add(Copy(marker, "AgentMap.EventMarkers"));
        }
        return result;
    }

    private static IReadOnlyList<FrontlineMapMarkerObservation> ReadDirectorMarkers(uint territoryId)
    {
        if (territoryId == 0 || territoryId > ushort.MaxValue)
            return [];
        var framework = EventFramework.Instance();
        if (framework is null)
            return [];

        StdVector<MapMarkerData> native = default;
        try
        {
            framework->GetEventMapMarkers((ushort)territoryId, &native);
            var result = new List<FrontlineMapMarkerObservation>();
            for (var index = 0; index < Math.Min(native.Count, 512); index++)
                result.Add(Copy(native.First + index, "EventFramework.GetEventMapMarkers"));
            return result;
        }
        finally
        {
            native.Dispose();
        }
    }

    private static FrontlineMapMarkerObservation Copy(MapMarkerData* marker, string source) => new(
        marker->IconId,
        marker->DataId,
        marker->ObjectiveId,
        marker->Position,
        marker->TooltipString is null ? string.Empty : marker->TooltipString->ToString(),
        marker->EndTimestamp,
        marker->EventState,
        source);
}
