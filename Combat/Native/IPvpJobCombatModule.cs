namespace PvPSentinel.Combat.Native;

internal interface IPvpJobCombatModule
{
    uint JobId { get; }
    string JobName { get; }
    ulong CommittedTargetId { get; }
    JobCombatEvaluation Evaluate(NativeCombatContext context);
    void NotifyActionAccepted(NativeActionCandidate action, DateTime acceptedAtUtc, bool hypothetical);
    void Reset();
}
