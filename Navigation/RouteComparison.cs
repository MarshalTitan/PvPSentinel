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
        var firstSamples = SampleOpening(first, sampleDistance);
        var secondSamples = SampleOpening(second, sampleDistance);
        if (firstSamples.Count != secondSamples.Count)
            return false;
        return firstSamples.Zip(secondSamples).All(pair => HorizontalDistance(pair.First, pair.Second) <= tolerance);
    }

    public static IReadOnlyList<Vector3> AlternateAnchors(Vector3 center, float radius = 8f) =>
    [
        center + new Vector3(radius, 0f, 0f),
        center + new Vector3(-radius, 0f, 0f),
        center + new Vector3(0f, 0f, radius),
        center + new Vector3(0f, 0f, -radius),
    ];

    private static IReadOnlyList<Vector3> SampleOpening(IReadOnlyList<Vector3> route, float distance)
    {
        var result = new List<Vector3> { route[0] };
        var walked = 0f;
        for (var index = 1; index < route.Count && walked < distance; index++)
        {
            walked += HorizontalDistance(route[index - 1], route[index]);
            result.Add(route[index]);
        }
        return result;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
