using System.Numerics;
using PvPSentinel.Models;

namespace PvPSentinel.Navigation;

internal static class WorqorCentralRecovery
{
    // The 2026-09-30 video shows the central snowman blocking a northwest
    // departure from this lower platform. In the same clip, a generated route
    // from (5.7,-16.8,4.8) reached Triumph 2 via (7.2,-16.8,35.5), then the
    // east exit at (52.2,-17.0,34.1). Use only that observed exit as a candidate;
    // vnavmesh must still generate and follow the entire stage, and the normal
    // bounded failure policy applies if the terrain/obstacle has changed.
    internal static readonly Vector3 ObservedEastExit = new(52.2f, -17f, 34.1f);

    public static Vector3? PreferredDeparture(
        FrontlineMap map, Vector3 player, IReadOnlyList<Vector3> failedCorridor)
    {
        if (map != FrontlineMap.WorqorChirteh ||
            player.X is < -12f or > 12f ||
            player.Z is < 0f or > 15f ||
            MathF.Abs(player.Y + 17f) > 2.5f ||
            failedCorridor.Count < 2)
            return null;

        // Only intervene when the just-failed corridor runs northwest into the
        // filmed obstruction. Other routes through the middle retain the usual
        // map-independent recovery candidates.
        var next = failedCorridor[1];
        return next.X < player.X - 20f && next.Z < player.Z - 25f
            ? ObservedEastExit
            : null;
    }
}
