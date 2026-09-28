using System.Globalization;
using System.Numerics;

namespace PvPSentinel.FrontlineCore.Maps;

internal sealed record DiscoveryMarkerAggregate(
    string PositionKey,
    Vector3 Position,
    int EvidenceCount,
    string MarkerFamilyFingerprint,
    string TransitionFingerprint,
    string EvidenceSummary,
    IReadOnlyList<FrontlineMapMarkerObservation> Observations);

internal enum DiscoveryMarkerPromotionClass
{
    RawObservationOnly,
    DurableStationaryEvidence,
    ObjectiveSignal,
    PhysicalCorroboration,
}

/// <summary>
/// Map-independent, discovery-only marker grouping. It deliberately answers only
/// whether a coordinate has become stable enough to expose as a manual M2 test
/// destination; it never assigns objective state, ownership, rank, or strategy.
/// </summary>
internal static class DiscoveryMarkerAggregator
{
    internal const uint ObservedTransientObjectiveSentinel = 1115742468;
    internal const int ObjectiveSignalStableScans = 12;
    internal static readonly TimeSpan ObjectiveSignalStableAge = TimeSpan.FromSeconds(3);
    internal const int DurableEvidenceStableScans = 32;
    internal static readonly TimeSpan DurableEvidenceStableAge = TimeSpan.FromSeconds(8);
    internal const int PhysicalEvidenceStableScans = 8;
    internal static readonly TimeSpan PhysicalEvidenceStableAge = TimeSpan.FromSeconds(2);
    internal const int MaximumPromotedLocations = 32;

    public static IReadOnlyList<DiscoveryMarkerAggregate> Aggregate(
        IEnumerable<FrontlineMapMarkerObservation> markers) => markers
        .Where(IsObservable)
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
            var families = observations
                .Select(marker => $"{marker.IconId}/{marker.DataId}/{NormalizeFlickeringObjectiveId(marker.ObjectiveId)}")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var identities = observations
                .Select(marker => $"{marker.IconId}/{marker.DataId}/{marker.ObjectiveId}/e{marker.EventState}/t{marker.EndTimestamp}")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var tooltips = observations
                .Select(marker => Compact(marker.Tooltip))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return new DiscoveryMarkerAggregate(
                group.Key,
                position,
                observations.Length,
                string.Join('|', families),
                string.Join('|', observations.Select(TransitionRow).Distinct(StringComparer.Ordinal)),
                $"ids=[{string.Join(',', identities)}]; text=[{(tooltips.Length == 0 ? "<none>" : string.Join(" | ", tooltips))}]",
                observations);
        })
        .OrderBy(item => item.Position.X)
        .ThenBy(item => item.Position.Z)
        .ThenBy(item => item.Position.Y)
        .ToArray();

    public static DiscoveryMarkerPromotionClass ClassifyPromotionEvidence(
        DiscoveryMarkerAggregate aggregate,
        bool hasObjectiveLikePhysicalCorroboration)
    {
        if (hasObjectiveLikePhysicalCorroboration)
            return DiscoveryMarkerPromotionClass.PhysicalCorroboration;

        if (aggregate.Observations.Any(marker =>
                marker.DataId != 0 ||
                (NormalizeFlickeringObjectiveId(marker.ObjectiveId) != 0 &&
                 !string.IsNullOrWhiteSpace(marker.Tooltip)) ||
                marker.EventState > 0))
            return DiscoveryMarkerPromotionClass.ObjectiveSignal;

        if (aggregate.Observations.Any(marker =>
                marker.IconId != 0 && !string.IsNullOrWhiteSpace(marker.Tooltip)))
            return DiscoveryMarkerPromotionClass.DurableStationaryEvidence;

        return DiscoveryMarkerPromotionClass.RawObservationOnly;
    }

    public static bool IsStable(
        DateTime firstSeenUtc,
        DateTime lastSeenUtc,
        int scanCount,
        DiscoveryMarkerPromotionClass promotionClass) => promotionClass switch
        {
            DiscoveryMarkerPromotionClass.PhysicalCorroboration =>
                scanCount >= PhysicalEvidenceStableScans && lastSeenUtc - firstSeenUtc >= PhysicalEvidenceStableAge,
            DiscoveryMarkerPromotionClass.ObjectiveSignal =>
                scanCount >= ObjectiveSignalStableScans && lastSeenUtc - firstSeenUtc >= ObjectiveSignalStableAge,
            DiscoveryMarkerPromotionClass.DurableStationaryEvidence =>
                scanCount >= DurableEvidenceStableScans && lastSeenUtc - firstSeenUtc >= DurableEvidenceStableAge,
            _ => false,
        };

    public static string FamilyKey(FrontlineMapMarkerObservation marker) =>
        $"{marker.IconId}/{marker.DataId}/{marker.ObjectiveId}/e{marker.EventState}";

    public static string PositionKey(Vector3 position) =>
        $"{Quantize(position.X)}:{Quantize(position.Y)}:{Quantize(position.Z)}";

    internal static uint NormalizeFlickeringObjectiveId(uint objectiveId) =>
        objectiveId is 0 or 480 or 486 ? 0u : objectiveId;

    private static bool IsObservable(FrontlineMapMarkerObservation marker) =>
        float.IsFinite(marker.Position.X) &&
        float.IsFinite(marker.Position.Y) &&
        float.IsFinite(marker.Position.Z) &&
        (marker.IconId != 0 || marker.DataId != 0 || marker.ObjectiveId != 0 ||
         !string.IsNullOrWhiteSpace(marker.Tooltip));

    private static string TransitionRow(FrontlineMapMarkerObservation marker) =>
        string.Create(CultureInfo.InvariantCulture,
            $"icon={marker.IconId},data={marker.DataId},objective={NormalizeFlickeringObjectiveId(marker.ObjectiveId)},event={marker.EventState},text={NormalizeVolatileNumbers(Compact(marker.Tooltip))}");

    private static string Compact(string value) => value.Trim().Replace('\r', ' ').Replace('\n', ' ');

    private static string NormalizeVolatileNumbers(string value)
    {
        var result = new char[value.Length];
        var length = 0;
        var inDigits = false;
        foreach (var character in value)
        {
            if (char.IsDigit(character))
            {
                if (!inDigits)
                    result[length++] = '#';
                inDigits = true;
                continue;
            }

            inDigits = false;
            result[length++] = character;
        }
        return new string(result, 0, length);
    }

    private static int Quantize(float value) =>
        (int)MathF.Round(value * 2f, MidpointRounding.AwayFromZero);
}
