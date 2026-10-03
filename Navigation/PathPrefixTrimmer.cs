using System.Numerics;

namespace PvPSentinel.Navigation;

internal sealed record PathPrefixTrimResult(IReadOnlyList<Vector3> Route, bool RepathFromCurrentPosition, int RemovedPoints);

internal static class PathPrefixTrimmer
{
    public static PathPrefixTrimResult Reconcile(IReadOnlyList<Vector3> route, Vector3 requestOrigin,
        Vector3 current, TimeSpan requestAge)
    {
        if (route.Count < 2 || Vector3.Distance(requestOrigin, current) <= 3f)
            return new PathPrefixTrimResult(route, false, 0);

        var protectedPoints = RouteExecutionPlan.Build(route).Waypoints;
        var permittedTravel = Math.Clamp((float)requestAge.TotalSeconds * 10f + 3f, 3f, 45f);
        var travelled = 0f;
        var bestIndex = -1;
        var bestProgress = -1f;
        for (var index = 0; index < route.Count - 1; index++)
        {
            if (index > 0 && protectedPoints.Any(point => point.Protected &&
                    Vector3.Distance(point.Position, route[index]) < 0.15f))
                break;
            var start = route[index];
            var end = route[index + 1];
            var segment = end - start;
            var lengthSquared = segment.LengthSquared();
            if (lengthSquared < 0.01f)
                continue;
            var t = Math.Clamp(Vector3.Dot(current - start, segment) / lengthSquared, 0f, 1f);
            var progress = travelled + MathF.Sqrt(lengthSquared) * t;
            if (progress <= permittedTravel && Vector3.Distance(current, start + segment * t) <= 2.25f &&
                progress > bestProgress)
            {
                bestIndex = index;
                bestProgress = progress;
            }
            travelled += MathF.Sqrt(lengthSquared);
            if (travelled > permittedTravel + 25f)
                break;
        }
        if (bestIndex < 0 || bestProgress < 2f)
            return new PathPrefixTrimResult(route, true, 0);

        var trimmed = new List<Vector3> { current };
        for (var index = bestIndex + 1; index < route.Count; index++)
            if (Vector3.Distance(trimmed[^1], route[index]) > 0.15f)
                trimmed.Add(route[index]);
        return trimmed.Count < 2
            ? new PathPrefixTrimResult(route, true, 0)
            : new PathPrefixTrimResult(trimmed, false, bestIndex + 1);
    }
}
