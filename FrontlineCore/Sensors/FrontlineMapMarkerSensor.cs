using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace PvPSentinel.FrontlineCore.Sensors;

internal sealed unsafe class FrontlineMapMarkerSensor
{
    public IReadOnlyList<FrontlineMapMarkerObservation> Capture()
    {
        var agent = AgentMap.Instance();
        if (agent is null)
            return [];

        var result = new List<FrontlineMapMarkerObservation>();
        var count = Math.Min(agent->EventMarkersPtrs.Count, 512);
        for (var index = 0; index < count; index++)
        {
            var marker = agent->EventMarkersPtrs[index].Value;
            if (marker is null)
                continue;

            var tooltip = marker->TooltipString is null ? string.Empty : marker->TooltipString->ToString();
            result.Add(new FrontlineMapMarkerObservation(
                marker->IconId,
                marker->DataId,
                marker->ObjectiveId,
                marker->Position,
                tooltip,
                marker->EndTimestamp,
                marker->EventState,
                "AgentMap.EventMarkers"));
        }
        return result;
    }
}
