using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using PvPSentinel.FrontlineCore.Maps;
using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore.Sensors;

internal sealed record MarkerCapture(
    IReadOnlyList<FrontlineMapMarkerObservation> Markers,
    string Source,
    string BootstrapAction);

internal sealed unsafe class FrontlineMapMarkerSensor
{
    private DateTime firstCaptureUtc = DateTime.MinValue;
    private DateTime bootstrapOpenedUtc = DateTime.MinValue;
    private bool bootstrapAttempted;
    private bool bootstrapOwned;
    public MarkerCapture Capture(GameStateSnapshot game, FrontlineMatchLifecycle lifecycle)
    {
        var agent = AgentMap.Instance();
        var mapMarkers = ReadAgentMarkers(agent);
        if (game.FrontlineMap is not (FrontlineMap.WorqorChirteh or FrontlineMap.FieldsOfGlory))
            return new MarkerCapture(mapMarkers, "AgentMap.EventMarkers", string.Empty);

        if (firstCaptureUtc == DateTime.MinValue)
            firstCaptureUtc = game.CapturedAtUtc;

        var action = string.Empty;
        if (bootstrapOwned && game.CapturedAtUtc - bootstrapOpenedUtc >= TimeSpan.FromSeconds(1.25))
        {
            if (agent is not null)
                agent->Hide();
            bootstrapOwned = false;
            action = "closed-owned-map-after-marker-bootstrap";
        }

        // The direct event-framework probe returned no named Worqor markers.
        // Initialize the map agent once when no usable objective marker has
        // arrived; Shatter uses the same read-only AgentMap marker source.
        var hasObjectiveMarkers = game.FrontlineMap == FrontlineMap.WorqorChirteh
            ? HasNamedTriumph(mapMarkers)
            : HasShatterIce(mapMarkers);
        if (!hasObjectiveMarkers && !bootstrapAttempted &&
            (lifecycle is FrontlineMatchLifecycle.PreMatch or FrontlineMatchLifecycle.MatchActive) &&
            game.CapturedAtUtc - firstCaptureUtc >= TimeSpan.FromSeconds(5) &&
            game.MapId != 0 && agent is not null &&
            !game.IsInCombat && !game.IsBetweenAreas && game.LocalPlayer is { IsDead: false } &&
            !agent->IsAddonShown())
        {
            bootstrapAttempted = true;
            agent->OpenMap(game.MapId, game.TerritoryId);
            bootstrapOwned = true;
            bootstrapOpenedUtc = game.CapturedAtUtc;
            action = game.FrontlineMap == FrontlineMap.WorqorChirteh
                ? "opened-map-once-to-initialize-worqor-markers"
                : "opened-map-once-to-initialize-shatter-markers";
        }

        return new MarkerCapture(mapMarkers, "AgentMap.EventMarkers", action);
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
        firstCaptureUtc = DateTime.MinValue;
        bootstrapOpenedUtc = DateTime.MinValue;
    }

    private static bool HasNamedTriumph(IReadOnlyList<FrontlineMapMarkerObservation> markers) =>
        markers.Any(marker => marker.Tooltip.StartsWith("Triumph ", StringComparison.OrdinalIgnoreCase));

    private static bool HasShatterIce(IReadOnlyList<FrontlineMapMarkerObservation> markers) =>
        markers.Any(marker => ShatterObjectivePolicy.Resolve(marker) is not null &&
            ShatterObjectivePolicy.ParseLogicalId(marker.Tooltip) is not null);

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
