using System.Globalization;
using System.Numerics;

namespace PvPSentinel.Navigation;

internal sealed record RouteWaypointDescriptor(
    int Index,
    Vector3 Position,
    bool Protected,
    string Reason);

/// <summary>
/// Keeps vnavmesh's generated geometry intact while dividing execution at turns,
/// elevation changes, and bounded straight-line intervals. Path.MoveTo therefore
/// cannot silently collapse an entire stair/corner route into endpoint steering.
/// </summary>
internal sealed class RouteExecutionPlan
{
    private const float DuplicateDistance = 0.15f;
    private const float ProtectedTurnDegrees = 30f;
    private const float ProtectedElevationDelta = 0.6f;
    private const float MaximumUnprotectedStageLength = 18f;

    private RouteExecutionPlan(IReadOnlyList<RouteWaypointDescriptor> waypoints, float length)
    {
        Waypoints = waypoints;
        Length = length;
    }

    public IReadOnlyList<RouteWaypointDescriptor> Waypoints { get; }
    public float Length { get; }
    public int LastIndex => Waypoints.Count - 1;

    public static RouteExecutionPlan Build(IReadOnlyList<Vector3> route)
    {
        var cleaned = new List<Vector3>();
        foreach (var point in route)
        {
            if (cleaned.Count == 0 || Vector3.Distance(cleaned[^1], point) >= DuplicateDistance)
                cleaned.Add(point);
        }

        var descriptors = new List<RouteWaypointDescriptor>(cleaned.Count);
        var length = 0f;
        for (var index = 0; index < cleaned.Count; index++)
        {
            if (index > 0)
                length += Vector3.Distance(cleaned[index - 1], cleaned[index]);
            var reasons = new List<string>();
            if (index == 0)
                reasons.Add("origin");
            if (index == cleaned.Count - 1)
                reasons.Add("destination");
            if (index > 0 && Math.Abs(cleaned[index].Y - cleaned[index - 1].Y) >= ProtectedElevationDelta)
                reasons.Add("elevation-entry");
            if (index + 1 < cleaned.Count && Math.Abs(cleaned[index + 1].Y - cleaned[index].Y) >= ProtectedElevationDelta)
                reasons.Add("elevation-exit");
            if (index > 0 && index + 1 < cleaned.Count && TurnDegrees(cleaned[index - 1], cleaned[index], cleaned[index + 1]) >= ProtectedTurnDegrees)
                reasons.Add("turn");
            var structurallyProtected = reasons.Any(reason => reason is "elevation-entry" or "elevation-exit" or "turn");
            descriptors.Add(new RouteWaypointDescriptor(
                index,
                cleaned[index],
                structurallyProtected,
                reasons.Count == 0 ? "route" : string.Join('+', reasons)));
        }

        return new RouteExecutionPlan(descriptors, length);
    }

    public int FindStageEnd(int cursor)
    {
        if (cursor >= LastIndex)
            return LastIndex;

        var walked = 0f;
        for (var index = cursor + 1; index <= LastIndex; index++)
        {
            walked += Vector3.Distance(Waypoints[index - 1].Position, Waypoints[index].Position);
            if (Waypoints[index].Protected || walked >= MaximumUnprotectedStageLength)
                return index;
        }
        return LastIndex;
    }

    public IReadOnlyList<Vector3> BuildStage(Vector3 currentPosition, int cursor, int stageEnd)
    {
        var result = new List<Vector3> { currentPosition };
        for (var index = Math.Max(1, cursor + 1); index <= stageEnd && index < Waypoints.Count; index++)
        {
            if (Vector3.Distance(result[^1], Waypoints[index].Position) >= DuplicateDistance)
                result.Add(Waypoints[index].Position);
        }
        return result;
    }

    public float RemainingLength(Vector3 currentPosition, int cursor)
    {
        if (cursor >= LastIndex)
            return 0f;
        var remaining = Vector3.Distance(currentPosition, Waypoints[cursor + 1].Position);
        for (var index = cursor + 2; index < Waypoints.Count; index++)
            remaining += Vector3.Distance(Waypoints[index - 1].Position, Waypoints[index].Position);
        return remaining;
    }

    public string FormatGeometry(int maximumPoints = 128)
    {
        var rows = Waypoints.Take(maximumPoints).Select(waypoint => string.Create(
            CultureInfo.InvariantCulture,
            $"{waypoint.Index}:{Format(waypoint.Position)}:{waypoint.Reason}"));
        var suffix = Waypoints.Count > maximumPoints ? $",...+{Waypoints.Count - maximumPoints}" : string.Empty;
        return $"[{string.Join(',', rows)}{suffix}]";
    }

    private static float TurnDegrees(Vector3 previous, Vector3 current, Vector3 next)
    {
        var incoming = new Vector2(current.X - previous.X, current.Z - previous.Z);
        var outgoing = new Vector2(next.X - current.X, next.Z - current.Z);
        if (incoming.LengthSquared() < 0.01f || outgoing.LengthSquared() < 0.01f)
            return 0f;
        var dot = Math.Clamp(Vector2.Dot(Vector2.Normalize(incoming), Vector2.Normalize(outgoing)), -1f, 1f);
        return MathF.Acos(dot) * 180f / MathF.PI;
    }

    private static string Format(Vector3 value) => string.Create(
        CultureInfo.InvariantCulture, $"({value.X:F2},{value.Y:F2},{value.Z:F2})");
}
