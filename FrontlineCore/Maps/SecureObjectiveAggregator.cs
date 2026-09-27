using System.Globalization;
using System.Numerics;

namespace PvPSentinel.FrontlineCore.Maps;

internal sealed record SecureMarkerAggregate(
    string PositionKey,
    Vector3 Position,
    int EvidenceCount,
    string MarkerFamilyFingerprint,
    string EvidenceFingerprint,
    string EvidenceSummary,
    IReadOnlyList<FrontlineMapMarkerObservation> Observations);

internal enum SecureMarkerPromotionClass
{
    RawObservationOnly,
    DurableStationaryEvidence,
    ObjectiveSignal,
    PhysicalCorroboration,
}

/// <summary>
/// Discovery-only grouping for Secure. No marker ID is assigned tactical meaning here.
/// </summary>
internal static class SecureObjectiveAggregator
{
    // The first Secure field session proved that 60360/60361 are moving/transient
    // marker families. They produced 3,135 of 3,163 false SEC promotions while
    // briefly occupying the same half-yard cell. They remain observable research
    // evidence, but are never navigation destinations.
    private static readonly HashSet<uint> LiveVerifiedTransientIcons = [60360, 60361];
    private const uint ObservedTransientObjectiveSentinel = 1115742468;

    public const int ObjectiveSignalStableScans = 8;
    public static readonly TimeSpan ObjectiveSignalStableAge = TimeSpan.FromSeconds(2);
    public const int DurableEvidenceStableScans = 32;
    public static readonly TimeSpan DurableEvidenceStableAge = TimeSpan.FromSeconds(8);
    public const int PhysicalEvidenceStableScans = 4;
    public static readonly TimeSpan PhysicalEvidenceStableAge = TimeSpan.FromSeconds(1);
    public const int MaximumPromotedLocations = 32;

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
                var families = observations
                    .Select(marker => $"{marker.IconId}/{marker.DataId}/{marker.ObjectiveId}")
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
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
                    string.Join('|', families),
                    string.Join('|', rows),
                    $"ids=[{string.Join(',', identities)}]; text=[{(tooltips.Length == 0 ? "<none>" : string.Join(" | ", tooltips))}]",
                    observations);
            })
            .OrderBy(item => item.Position.X)
            .ThenBy(item => item.Position.Z)
            .ThenBy(item => item.Position.Y)
            .ToArray();
    }

    public static SecureMarkerPromotionClass ClassifyPromotionEvidence(
        SecureMarkerAggregate aggregate,
        bool hasPhysicalCorroboration)
    {
        if (aggregate.Observations.Any(IsLiveVerifiedTransientMarker))
            return SecureMarkerPromotionClass.RawObservationOnly;

        if (hasPhysicalCorroboration)
            return SecureMarkerPromotionClass.PhysicalCorroboration;

        // ObjectiveId 486 was shared by the small stable marker population in the
        // first Secure capture (persistent locations and central timed objects).
        // This does not assign tactical meaning; it only supplies stronger evidence
        // that a coordinate is objective-like rather than a moving player marker.
        if (aggregate.Observations.Any(marker =>
                marker.ObjectiveId != 0 && marker.ObjectiveId != ObservedTransientObjectiveSentinel))
            return SecureMarkerPromotionClass.ObjectiveSignal;

        // Unknown families need durable, stationary evidence and some identity or
        // text beyond the transient sentinel before they can become clickable.
        if (aggregate.Observations.Any(marker =>
                marker.DataId != 0 ||
                !string.IsNullOrWhiteSpace(marker.Tooltip) ||
                (marker.ObjectiveId != 0 && marker.ObjectiveId != ObservedTransientObjectiveSentinel)))
            return SecureMarkerPromotionClass.DurableStationaryEvidence;

        return SecureMarkerPromotionClass.RawObservationOnly;
    }

    public static bool IsStable(
        DateTime firstSeenUtc,
        DateTime lastSeenUtc,
        int scanCount,
        SecureMarkerPromotionClass promotionClass) => promotionClass switch
        {
            SecureMarkerPromotionClass.PhysicalCorroboration =>
                scanCount >= PhysicalEvidenceStableScans && lastSeenUtc - firstSeenUtc >= PhysicalEvidenceStableAge,
            SecureMarkerPromotionClass.ObjectiveSignal =>
                scanCount >= ObjectiveSignalStableScans && lastSeenUtc - firstSeenUtc >= ObjectiveSignalStableAge,
            SecureMarkerPromotionClass.DurableStationaryEvidence =>
                scanCount >= DurableEvidenceStableScans && lastSeenUtc - firstSeenUtc >= DurableEvidenceStableAge,
            _ => false,
        };

    public static bool IsLiveVerifiedTransientMarker(FrontlineMapMarkerObservation marker) =>
        LiveVerifiedTransientIcons.Contains(marker.IconId) &&
        marker.DataId == 0 &&
        marker.ObjectiveId == ObservedTransientObjectiveSentinel;

    public static string FamilyKey(FrontlineMapMarkerObservation marker) =>
        $"{marker.IconId}/{marker.DataId}/{marker.ObjectiveId}/e{marker.EventState}";

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
