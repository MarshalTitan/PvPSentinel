namespace PvPSentinel.Combat.Threat;

internal enum PvPThreatLevel
{
    None,
    Low,
    Moderate,
    High,
    Extreme,
}

internal static class PvPThreatPolicy
{
    public static PvPThreatLevel EvaluateLevel(int targeters, int nearbyEnemies)
    {
        targeters = Math.Max(0, targeters);
        nearbyEnemies = Math.Max(0, nearbyEnemies);

        if (targeters == 0 && nearbyEnemies == 0)
            return PvPThreatLevel.None;
        if (targeters >= 6 || nearbyEnemies >= 10)
            return PvPThreatLevel.Extreme;
        if (targeters >= 4 || (targeters >= 2 && nearbyEnemies >= 6) || nearbyEnemies >= 8)
            return PvPThreatLevel.High;
        if (targeters >= 2 || nearbyEnemies >= 4)
            return PvPThreatLevel.Moderate;
        return PvPThreatLevel.Low;
    }
}
