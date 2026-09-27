using System.Numerics;

namespace PvPSentinel.Navigation;

internal static class RouteComparison
{
    public static bool MateriallyIdentical(
        IReadOnlyList<Vector3> first,
        IReadOnlyList<Vector3> second,
        float sampleDistance = 20f,
        float tolerance = 3f)
    {
        if (first.Count < 2 || second.Count < 2)
            return false;
        var comparisonDistance = Math.Min(sampleDistance, Math.Min(Length(first), Length(second)));
        if (comparisonDistance < 5f)
            return false;
        var firstSamples = SampleOpening(first, comparisonDistance);
        var secondSamples = SampleOpening(second, comparisonDistance);
        if (firstSamples.Count != secondSamples.Count)
            return false;
        return firstSamples.Zip(secondSamples).All(pair => SpatialDistance(pair.First, pair.Second) <= tolerance);
    }

    public static IReadOnlyList<Vector3> FailureCorridor(
        IReadOnlyList<Vector3> route,
        Vector3 currentPosition,
        float lookAheadDistance = 25f,
        int minimumWaypointIndex = 0)
    {
        if (route.Count == 0)
            return [];
        minimumWaypointIndex = Math.Clamp(minimumWaypointIndex, 0, route.Count - 1);
        var nearest = Enumerable.Range(minimumWaypointIndex, route.Count - minimumWaypointIndex)
            .OrderBy(index => SpatialDistance(route[index], currentPosition))
            .First();
        var result = new List<Vector3> { currentPosition };
        var walked = 0f;
        for (var index = nearest; index < route.Count && walked < lookAheadDistance; index++)
        {
            if (SpatialDistance(result[^1], route[index]) < 0.15f)
                continue;
            walked += SpatialDistance(result[^1], route[index]);
            result.Add(route[index]);
        }
        return result;
    }

    public static bool RepeatsFailedCorridor(
        IReadOnlyList<Vector3> failedCorridor,
        IReadOnlyList<Vector3> replacement,
        float sampleDistance = 20f,
        float tolerance = 3f)
    {
        if (failedCorridor.Count < 2 || replacement.Count < 2)
            return false;
        if (MateriallyIdentical(failedCorridor, replacement, sampleDistance, tolerance))
            return true;

        // A regenerated path can add a tiny opening detour and then merge back
        // into the exact corridor that just failed. Opening-only comparison called
        // that "different" in the SEC-12 stair trace. Require a continuous aligned
        // overlap so an incidental crossing is still allowed, but a rejoined bad
        // corridor advances to the next approach anchor immediately.
        const float sampleInterval = 2.5f;
        const float maximumFailedPrefix = 10f;
        const float maximumReplacementPrefix = 15f;
        const float requiredAlignedOverlap = 12.5f;
        var failedSamples = SampleEvery(failedCorridor, sampleInterval);
        var replacementSamples = SampleEvery(replacement, sampleInterval);
        var failedPrefixSamples = (int)MathF.Ceiling(maximumFailedPrefix / sampleInterval);
        var replacementPrefixSamples = (int)MathF.Ceiling(maximumReplacementPrefix / sampleInterval);
        var requiredSamples = (int)MathF.Ceiling(requiredAlignedOverlap / sampleInterval) + 1;

        for (var first = 0; first < Math.Min(failedSamples.Count, failedPrefixSamples + 1); first++)
        for (var second = 0; second < Math.Min(replacementSamples.Count, replacementPrefixSamples + 1); second++)
        {
            if (SpatialDistance(failedSamples[first], replacementSamples[second]) > tolerance)
                continue;
            var available = Math.Min(failedSamples.Count - first, replacementSamples.Count - second);
            if (available < requiredSamples)
                continue;
            var aligned = true;
            for (var offset = 1; offset < requiredSamples; offset++)
            {
                if (SpatialDistance(failedSamples[first + offset], replacementSamples[second + offset]) <= tolerance)
                    continue;
                aligned = false;
                break;
            }
            if (aligned)
                return true;
        }

        return false;
    }

    public static IReadOnlyList<Vector3> AlternateAnchors(Vector3 center, float radius = 8f) =>
    [
        center + new Vector3(radius, 0f, 0f),
        center + new Vector3(-radius, 0f, 0f),
        center + new Vector3(0f, 0f, radius),
        center + new Vector3(0f, 0f, -radius),
    ];

    private static IReadOnlyList<Vector3> SampleOpening(
        IReadOnlyList<Vector3> route,
        float distance,
        float interval = 5f)
    {
        var result = new List<Vector3> { route[0] };
        var targetDistance = interval;
        var walked = 0f;
        for (var index = 1; index < route.Count && targetDistance <= distance; index++)
        {
            var segment = SpatialDistance(route[index - 1], route[index]);
            if (segment < 0.001f)
                continue;
            while (walked + segment >= targetDistance && targetDistance <= distance)
            {
                var ratio = (targetDistance - walked) / segment;
                result.Add(Vector3.Lerp(route[index - 1], route[index], ratio));
                targetDistance += interval;
            }
            walked += segment;
        }
        return result;
    }

    private static float SpatialDistance(Vector3 a, Vector3 b) => Vector3.Distance(a, b);

    private static IReadOnlyList<Vector3> SampleEvery(IReadOnlyList<Vector3> route, float interval)
    {
        var length = Length(route);
        if (length < 0.001f)
            return [route[0]];
        var result = SampleOpening(route, length, interval).ToList();
        if (SpatialDistance(result[^1], route[^1]) > 0.15f)
            result.Add(route[^1]);
        return result;
    }

    private static float Length(IReadOnlyList<Vector3> route)
    {
        var length = 0f;
        for (var index = 1; index < route.Count; index++)
            length += SpatialDistance(route[index - 1], route[index]);
        return length;
    }
}
