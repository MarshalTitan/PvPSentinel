using System.Globalization;
using System.Numerics;

namespace PvPSentinel.FrontlineCore.Maps;

internal sealed record SecureMarkerAggregate(
    string PositionKey,
    Vector3 Position,
    int EvidenceCount,
    string EvidenceFingerprint,
    string EvidenceSummary,
    IReadOnlyList<FrontlineMapMarkerObservation> Observations);

/// <summary>
/// Discovery-only grouping for Secure. No marker ID is assigned tactical meaning here.
/// </summary>
internal static class SecureObjectiveAggregator
{
    public const int RequiredStableScans = 3;
    public static readonly TimeSpan RequiredStableAge = TimeSpan.FromMilliseconds(750);

    public static IReadOnlyList<SecureMarkerAggregate> Aggregate(
        IEnumerable<FrontlineMapMarkerObservation> markers)
    {
        return markers
            .Where(IsObservableCandidate)
            .GroupBy(marker => PositionKey(marker.Position), StringComparer.Ordinal)
            .Select(group =>
            {
                var observations = group
                    .OrderBy(marker => marker.IconId)
                    .ThenBy(marker => marker.DataId)
                    .ThenBy(marker => marker.ObjectiveId)
                    .ThenBy(marker => marker.EventState)
                    .ToArray();
                var position = new Vector3(
                    observations.Average(marker => marker.Position.X),
                    observations.Average(marker => marker.Position.Y),
                    observations.Average(marker => marker.Position.Z));
                var rows = observations.Select(EvidenceRow).Distinct(StringComparer.Ordinal).ToArray();
                var identities = observations
                    .Select(marker => $"{marker.IconId}/{marker.DataId}/{marker.ObjectiveId}/e{marker.EventState}/t{marker.EndTimestamp}")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var tooltips = observations
                    .Select(marker => marker.Tooltip.Trim().Replace('\r', ' ').Replace('\n', ' '))
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return new SecureMarkerAggregate(
                    group.Key,
                    position,
                    observations.Length,
                    string.Join('|', rows),
                    $"ids=[{string.Join(',', identities)}]; text=[{(tooltips.Length == 0 ? "<none>" : string.Join(" | ", tooltips))}]",
                    observations);
            })
            .OrderBy(item => item.Position.X)
            .ThenBy(item => item.Position.Z)
            .ThenBy(item => item.Position.Y)
            .ToArray();
    }

    public static bool IsStable(DateTime firstSeenUtc, DateTime lastSeenUtc, int scanCount) =>
        scanCount >= RequiredStableScans && lastSeenUtc - firstSeenUtc >= RequiredStableAge;

    public static string PositionKey(Vector3 position) =>
        $"{Quantize(position.X)}:{Quantize(position.Y)}:{Quantize(position.Z)}";

    private static bool IsObservableCandidate(FrontlineMapMarkerObservation marker) =>
        float.IsFinite(marker.Position.X) &&
        float.IsFinite(marker.Position.Y) &&
        float.IsFinite(marker.Position.Z) &&
        (marker.IconId != 0 || marker.DataId != 0 || marker.ObjectiveId != 0 ||
         !string.IsNullOrWhiteSpace(marker.Tooltip));

    private static string EvidenceRow(FrontlineMapMarkerObservation marker)
    {
        var tooltip = string.IsNullOrWhiteSpace(marker.Tooltip)
            ? "<none>"
            : marker.Tooltip.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return string.Create(CultureInfo.InvariantCulture,
            $"icon={marker.IconId},data={marker.DataId},objective={marker.ObjectiveId},event={marker.EventState},end={marker.EndTimestamp},text={tooltip}");
    }

    private static int Quantize(float value) =>
        (int)MathF.Round(value * 2f, MidpointRounding.AwayFromZero);
}
