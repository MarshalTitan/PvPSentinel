namespace PvPSentinel.Navigation;

internal static class DynamicRouteHandoffPolicy
{
    public static bool MayReplace(bool ticketCurrent, bool oldRouteOwned,
        bool recovering, string requestedRouteId, string currentRouteId,
        string requestedDestinationId, string? currentDestinationId) =>
        ticketCurrent && oldRouteOwned && !recovering &&
        requestedRouteId == currentRouteId &&
        requestedDestinationId == currentDestinationId;
}
