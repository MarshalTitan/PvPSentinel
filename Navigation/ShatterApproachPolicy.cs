using System.Numerics;

namespace PvPSentinel.Navigation;

/// <summary>Keep Shatter route endpoints outside the physical ice instead of at the marker center.</summary>
internal static class ShatterApproachPolicy
{
    public static float Radius(string kind) => kind == "LARGE" ? 12f : 9f;
    public static float MinimumClearance(string kind) => kind == "LARGE" ? 8f : 6f;

    public static IReadOnlyList<Vector3> Anchors(Vector3 center, Vector3 origin, string kind)
    {
        var towardOrigin = new Vector2(origin.X - center.X, origin.Z - center.Z);
        var direction = towardOrigin.LengthSquared() > 1f
            ? Vector2.Normalize(towardOrigin) : Vector2.UnitX;
        var side = new Vector2(-direction.Y, direction.X);
        var radius = Radius(kind);
        // Try the side from which travel begins, then either flank and the far side.
        // Every candidate is a vnavmesh destination, not a direct movement command.
        return [
            Offset(direction), Offset(Vector2.Normalize(direction + side)),
            Offset(Vector2.Normalize(direction - side)), Offset(side), Offset(-side),
            Offset(-direction),
        ];

        Vector3 Offset(Vector2 vector) => center + new Vector3(vector.X * radius, 0f, vector.Y * radius);
    }
}
