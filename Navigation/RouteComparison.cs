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
        return firstSamples.Zip(secondSamples).All(pair => HorizontalDistance(pair.First, pair.Second) <= tolerance);
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
            .OrderBy(index => HorizontalDistance(route[index], currentPosition))
            .First();
        var result = new List<Vector3> { currentPosition };
        var walked = 0f;
        for (var index = nearest; index < route.Count && walked < lookAheadDistance; index++)
        {
            if (HorizontalDistance(result[^1], route[index]) < 0.15f)
                continue;
            walked += HorizontalDistance(result[^1], route[index]);
            result.Add(route[index]);
        }
        return result;
    }

    public static bool RepeatsFailedCorridor(
        IReadOnlyList<Vector3> failedCorridor,
        IReadOnlyList<Vector3> replacement,
        float sampleDistance = 20f,
        float tolerance = 3f) =>
        failedCorridor.Count >= 2 &&
        replacement.Count >= 2 &&
        MateriallyIdentical(failedCorridor, replacement, sampleDistance, tolerance);

    public static IReadOnlyList<Vector3> AlternateAnchors(Vector3 center, float radius = 8f) =>
    [
        center + new Vector3(radius, 0f, 0f),
        center + new Vector3(-radius, 0f, 0f),
        center + new Vector3(0f, 0f, radius),
        center + new Vector3(0f, 0f, -radius),
    ];

    private static IReadOnlyList<Vector3> SampleOpening(IReadOnlyList<Vector3> route, float distance)
    {
        const float interval = 5f;
        var result = new List<Vector3> { route[0] };
        var targetDistance = interval;
        var walked = 0f;
        for (var index = 1; index < route.Count && targetDistance <= distance; index++)
        {
            var segment = HorizontalDistance(route[index - 1], route[index]);
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

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    private static float Length(IReadOnlyList<Vector3> route)
    {
        var length = 0f;
        for (var index = 1; index < route.Count; index++)
            length += HorizontalDistance(route[index - 1], route[index]);
        return length;
    }
}
