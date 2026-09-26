namespace PvPSentinel.Combat.Native;

internal static class NativeCombatPolicy
{
    private static readonly HashSet<uint> PurifyRemovableStatusIds =
    [
        1343, // Stun
        1344, // Heavy
        1345, // Bind
        1347, // Silence
        3219, // Deep Freeze
    ];

    private static readonly HashSet<string> PurifyRemovableStatusNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Stun",
        "Heavy",
        "Bind",
        "Silence",
        "Half-asleep",
        "Sleep",
        "Deep Freeze",
    };

    public static bool RecuperateEligible(float hpPercent, float threshold, uint currentMp) =>
        hpPercent < threshold && currentMp >= 2000;

    public static bool PurifyEligible(bool removableControlPresent, bool resilienceActive, uint currentMp) =>
        removableControlPresent && !resilienceActive && currentMp >= 2000;

    public static bool IsPurifyRemovableStatus(uint statusId, string statusName) =>
        PurifyRemovableStatusIds.Contains(statusId) || PurifyRemovableStatusNames.Contains(statusName);

    public static bool ShouldSwitchTarget(
        double committedSeconds,
        float minimumCommitmentSeconds,
        float challengerAdvantage,
        float requiredAdvantage) =>
        committedSeconds >= minimumCommitmentSeconds && challengerAdvantage >= requiredAdvantage;

    public static bool IsTargetScoreAcceptable(float score, float minimumScore) =>
        score >= minimumScore;

    public static bool IsTargetAcceptable(float score, float minimumScore, bool severeOverextension) =>
        !severeOverextension && IsTargetScoreAcceptable(score, minimumScore);

    public static string DefensePreemptionReason(
        bool purifyUrgent,
        bool guardNeeded,
        bool recuperateEligible) =>
        purifyUrgent
            ? "Purify priority"
            : guardNeeded
                ? "Guard priority"
                : recuperateEligible
                    ? "Recuperate priority"
                    : "None";

    public static GuardThresholdEvaluation CalculateGuardThreshold(
        float baseThreshold,
        float maximumThreshold,
        int incomingTargeters,
        int nearbyThreats,
        int nearbyFriendlies,
        float targeterBonus,
        float nearbyThreatBonus,
        int nearbyThreatBaseline,
        float disadvantageBonus,
        float hpLossPercentPerSecond,
        float rapidLossThreshold,
        float rapidLossBonus)
    {
        var targeterContribution = Math.Max(0, incomingTargeters) * Math.Max(0f, targeterBonus);
        var nearbyContribution = Math.Max(0, nearbyThreats - Math.Max(0, nearbyThreatBaseline)) *
                                 Math.Max(0f, nearbyThreatBonus);
        var numericalDisadvantage = Math.Max(0, nearbyThreats - Math.Max(0, nearbyFriendlies));
        var disadvantageContribution = numericalDisadvantage * Math.Max(0f, disadvantageBonus);
        var rapidLossContribution = hpLossPercentPerSecond >= Math.Max(0f, rapidLossThreshold)
            ? Math.Max(0f, rapidLossBonus)
            : 0f;
        var threshold = Math.Clamp(
            baseThreshold + targeterContribution + nearbyContribution + disadvantageContribution + rapidLossContribution,
            baseThreshold,
            Math.Max(baseThreshold, maximumThreshold));

        return new GuardThresholdEvaluation(
            threshold,
            targeterContribution,
            nearbyContribution,
            numericalDisadvantage,
            disadvantageContribution,
            hpLossPercentPerSecond,
            rapidLossContribution);
    }

    public static uint MarksmanEffectiveHpAllowance(
        uint baseDamage,
        int alliedFocus,
        uint focusAllowance,
        uint maximumEffectiveHp,
        float damageMultiplier = 1f,
        float soloConfidence = 0.9f,
        int uncreditedFocus = 1)
    {
        var modifiedBase = (uint)MathF.Round(baseDamage * Math.Max(1f, damageMultiplier));
        var reliableSoloDamage = (uint)MathF.Round(modifiedBase * Math.Clamp(soloConfidence, 0f, 1f));
        var creditedFocus = Math.Max(0, alliedFocus - Math.Max(0, uncreditedFocus));
        var focus = (ulong)creditedFocus * focusAllowance;
        return (uint)Math.Min(maximumEffectiveHp, (ulong)reliableSoloDamage + focus);
    }

    public static bool HasMarksmanHighHpConfidence(
        uint effectiveHp,
        uint baseDamage,
        float damageMultiplier,
        int alliedFocus,
        int minimumFocus) =>
        effectiveHp <= (uint)MathF.Round(baseDamage * Math.Max(1f, damageMultiplier)) ||
        alliedFocus >= Math.Max(0, minimumFocus);

    public static uint MarksmanOverkillFloor(
        uint minimumEffectiveHp,
        int alliedFocus,
        uint focusHpPerPlayer,
        int uncreditedFocus,
        uint maximumMinimumHp) =>
        FocusAdjustedMinimumHp(
            minimumEffectiveHp,
            alliedFocus,
            focusHpPerPlayer,
            uncreditedFocus,
            maximumMinimumHp);

    public static bool IsMarksmanOverkillRisk(
        uint effectiveHp,
        float distance,
        float normalPressureRange,
        uint overkillFloor) =>
        distance <= normalPressureRange && effectiveHp < overkillFloor;

    public static uint WildfireSurvivalFloor(
        uint minimumEffectiveHp,
        int alliedFocus,
        uint focusHpPerPlayer,
        int uncreditedFocus,
        uint maximumMinimumHp)
        => FocusAdjustedMinimumHp(
            minimumEffectiveHp,
            alliedFocus,
            focusHpPerPlayer,
            uncreditedFocus,
            maximumMinimumHp);

    private static uint FocusAdjustedMinimumHp(
        uint minimumEffectiveHp,
        int alliedFocus,
        uint focusHpPerPlayer,
        int uncreditedFocus,
        uint maximumMinimumHp)
    {
        var creditedFocus = Math.Max(0, alliedFocus - Math.Max(0, uncreditedFocus));
        var focusAdjustment = (ulong)creditedFocus * focusHpPerPlayer;
        var uncapped = (ulong)minimumEffectiveHp + focusAdjustment;
        return (uint)Math.Min(Math.Max(minimumEffectiveHp, maximumMinimumHp), uncapped);
    }
}

internal sealed record GuardThresholdEvaluation(
    float Threshold,
    float TargeterContribution,
    float NearbyContribution,
    int NumericalDisadvantage,
    float DisadvantageContribution,
    float HpLossPercentPerSecond,
    float RapidLossContribution);
