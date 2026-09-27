using System.Numerics;

namespace PvPSentinel.Navigation;

internal sealed record ManualRoutePlan(Vector3? SnappedDestination, IReadOnlyList<Vector3> Route, bool IsValid, string Explanation);

internal static class ManualRoutePlanner
{
    public static async Task<ManualRoutePlan> BuildAsync(
        IVNavmeshAdapter vnav,
        Vector3 origin,
        Vector3 requestedDestination,
        float tolerance)
    {
        var snapped = vnav.FindNearestReachable(requestedDestination, 12f, 8f);
        if (snapped is null)
            return new ManualRoutePlan(null, [], false, "No reachable mesh point was found.");
        var route = await vnav.FindPathAsync(origin, snapped.Value, tolerance).ConfigureAwait(false);
        var validation = PathValidator.Validate(route, origin, snapped.Value, tolerance);
        return new ManualRoutePlan(snapped, route, validation.IsValid, validation.Explanation);
    }
}
