namespace PvPSentinel.FrontlineCore;

internal static class PlayerTrackingPolicy
{
    public const float FreshSeconds = 1.5f;
    public const float EvictionSeconds = 10f;

    public static bool IsFresh(DateTime lastSeenUtc, DateTime now) =>
        now - lastSeenUtc <= TimeSpan.FromSeconds(FreshSeconds);

    public static bool IsExpired(DateTime lastSeenUtc, DateTime now) =>
        now - lastSeenUtc > TimeSpan.FromSeconds(EvictionSeconds);
}
