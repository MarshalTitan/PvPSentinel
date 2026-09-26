using System.Numerics;

namespace PvPSentinel.Navigation;

internal sealed record PathValidationResult(bool IsValid, string Explanation);

internal static class PathValidator
{
    public static PathValidationResult Validate(
        IReadOnlyList<Vector3>? path,
        Vector3 origin,
        Vector3 destination,
        float tolerance)
    {
        if (path is null || path.Count < 2)
            return new PathValidationResult(false, "Path contains fewer than two points.");

        if (path.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)))
            return new PathValidationResult(false, "Path contains a non-finite coordinate.");

        var startError = Vector3.Distance(path[0], origin);
        if (startError > 12f)
            return new PathValidationResult(false, $"First path point is {startError:F1}y from the player.");

        var endError = Vector3.Distance(path[^1], destination);
        if (endError > Math.Max(8f, tolerance + 5f))
            return new PathValidationResult(false, $"Final path point is {endError:F1}y from the requested destination.");

        var pathLength = 0f;
        for (var index = 1; index < path.Count; index++)
            pathLength += Vector3.Distance(path[index - 1], path[index]);

        var direct = Math.Max(1f, Vector3.Distance(origin, destination));
        var maximumReasonable = Math.Max(500f, (direct * 6f) + 100f);
        if (pathLength > maximumReasonable)
            return new PathValidationResult(false,
                $"Path length {pathLength:F1}y is implausible for a {direct:F1}y direct distance.");

        return new PathValidationResult(true,
            $"Start error {startError:F1}y, end error {endError:F1}y, path length {pathLength:F1}y.");
    }
}
