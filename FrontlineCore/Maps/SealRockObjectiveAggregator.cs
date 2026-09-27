using System.Numerics;

namespace PvPSentinel.FrontlineCore.Maps;

internal sealed record SealRockAggregate(
    string PositionKey,
    Vector3 Position,
    ObjectiveLifecycle State,
    string Rank,
    string GrandCompany,
    ObjectiveOwner DirectOwner,
    uint StateId,
    SensorConfidence Confidence,
    int EvidenceCount,
    string Source);

internal static class SealRockObjectiveAggregator
{
    private static readonly IReadOnlyDictionary<uint, (string Rank, string Gc)> LiveStates =
        new Dictionary<uint, (string, string)>
        {
            [60585] = ("B", "NEUTRAL"),
            [60586] = ("B", "MAELSTROM"),
            [60587] = ("B", "ADDERS"),
            [60588] = ("B", "FLAMES"),
            [60589] = ("A", "NEUTRAL"),
            [60590] = ("A", "MAELSTROM"),
            [60591] = ("A", "ADDERS"),
            [60592] = ("A", "FLAMES"),
        };

    private static readonly IReadOnlyDictionary<uint, string> OwnerOverlays =
        new Dictionary<uint, string>
        {
            [60484] = "MAELSTROM",
            [60485] = "ADDERS",
            [60486] = "FLAMES",
        };

    public static IReadOnlyList<SealRockAggregate> Aggregate(
        IEnumerable<FrontlineMapMarkerObservation> markers)
    {
        return markers
            .Where(IsRelevant)
            .GroupBy(marker => PositionKey(marker.Position), StringComparer.Ordinal)
            .Select(Resolve)
            .OrderBy(item => item.Position.X)
            .ThenBy(item => item.Position.Z)
            .ToArray();
    }

    public static string PositionKey(Vector3 position) =>
        $"{Quantize(position.X)}:{Quantize(position.Y)}:{Quantize(position.Z)}";

    private static SealRockAggregate Resolve(IGrouping<string, FrontlineMapMarkerObservation> group)
    {
        var samples = group.ToArray();
        var primary = samples
            .Select(marker => (Marker: marker, Id: Candidates(marker).FirstOrDefault(LiveStates.ContainsKey)))
            .Where(item => item.Id != 0)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault();
        var overlay = samples
            .Select(marker => (Marker: marker, Id: Candidates(marker).FirstOrDefault(OwnerOverlays.ContainsKey)))
            .FirstOrDefault(item => item.Id != 0);
        var state = ObjectiveLifecycle.Unknown;
        var rank = "UNRESOLVED";
        var gc = "UNRESOLVED";
        var stateId = 0u;
        var confidence = SensorConfidence.RuntimeDiscovery;

        if (primary.Marker is not null && LiveStates.TryGetValue(primary.Id, out var live))
        {
            state = ObjectiveLifecycle.Active;
            rank = live.Rank;
            gc = live.Gc;
            stateId = primary.Id;
            confidence = SensorConfidence.LiveVerifiedMapping;
        }
        if (overlay.Marker is not null && OwnerOverlays.TryGetValue(overlay.Id, out var overlayGc))
        {
            gc = overlayGc;
            if (state == ObjectiveLifecycle.Unknown)
                state = ObjectiveLifecycle.Active;
            stateId = stateId == 0 ? overlay.Id : stateId;
            confidence = SensorConfidence.LiveVerifiedMapping;
        }

        if (state == ObjectiveLifecycle.Unknown)
        {
            var combined = string.Join(' ', samples.Select(sample => sample.Tooltip));
            if (combined.Contains("deactiv", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("exhaust", StringComparison.OrdinalIgnoreCase))
                state = ObjectiveLifecycle.Deactivated;
            else if (combined.Contains("inactive", StringComparison.OrdinalIgnoreCase))
                state = ObjectiveLifecycle.Inactive;
            else if (combined.Contains("active", StringComparison.OrdinalIgnoreCase) ||
                     combined.Contains("tomelith", StringComparison.OrdinalIgnoreCase))
                state = ObjectiveLifecycle.Active;
        }

        var directOwner = gc == "NEUTRAL" ? ObjectiveOwner.Neutral : ObjectiveOwner.Unresolved;
        return new SealRockAggregate(
            group.Key,
            samples[0].Position,
            state,
            rank,
            gc,
            directOwner,
            stateId,
            confidence,
            samples.Length,
            "AgentMap.EventMarkers");
    }

    private static bool IsRelevant(FrontlineMapMarkerObservation marker) =>
        Candidates(marker).Any(candidate => LiveStates.ContainsKey(candidate) || OwnerOverlays.ContainsKey(candidate)) ||
        marker.Tooltip.Contains("tomelith", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<uint> Candidates(FrontlineMapMarkerObservation marker)
    {
        yield return marker.IconId;
        yield return marker.DataId;
        yield return marker.ObjectiveId;
    }

    private static int Quantize(float value) => (int)MathF.Round(value * 2f, MidpointRounding.AwayFromZero);
}
