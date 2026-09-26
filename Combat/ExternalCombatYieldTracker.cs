namespace PvPSentinel.Combat;

internal sealed record ExternalCombatYieldDecision(
    bool HasActiveEvidence,
    bool ShouldYield,
    string Explanation);

internal sealed class ExternalCombatYieldTracker
{
    private DateTime activityUntilUtc = DateTime.MinValue;

    public ExternalCombatYieldDecision Update(
        DateTime now,
        bool isInCombat,
        bool isCasting,
        bool isActionQueued,
        float graceSeconds)
    {
        var active = isInCombat || isCasting || isActionQueued;
        if (active)
            activityUntilUtc = now.AddSeconds(Math.Clamp(graceSeconds, 0.5f, 10f));

        var yielding = active || now < activityUntilUtc;
        var reason = isCasting
            ? "Local player is casting."
            : isActionQueued
                ? "The client reports a queued local action."
                : isInCombat
                    ? "The client reports that the local player is in combat."
                    : yielding
                        ? "External-combat navigation yield grace period is active."
                        : "No local evidence of active external combat; strategic movement may resume.";

        return new ExternalCombatYieldDecision(active, yielding, reason);
    }

    public void Reset() => activityUntilUtc = DateTime.MinValue;
}
