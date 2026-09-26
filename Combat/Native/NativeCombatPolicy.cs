namespace PvPSentinel.Combat.Native;

internal static class NativeCombatPolicy
{
    public static bool RecuperateEligible(float hpPercent, float threshold, uint currentMp) =>
        hpPercent < threshold && currentMp >= 2000;

    public static bool ShouldSwitchTarget(
        double committedSeconds,
        float minimumCommitmentSeconds,
        float challengerAdvantage,
        float requiredAdvantage) =>
        committedSeconds >= minimumCommitmentSeconds && challengerAdvantage >= requiredAdvantage;

    public static uint MarksmanEffectiveHpAllowance(
        uint baseDamage,
        int alliedFocus,
        uint focusAllowance,
        uint maximumEffectiveHp,
        float damageMultiplier = 1f)
    {
        var modifiedBase = (uint)MathF.Round(baseDamage * Math.Max(1f, damageMultiplier));
        var focus = (ulong)Math.Max(0, alliedFocus) * focusAllowance;
        return (uint)Math.Min(maximumEffectiveHp, (ulong)modifiedBase + focus);
    }
}
