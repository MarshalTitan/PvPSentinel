namespace PvPSentinel.Combat.Native.Targeting;

// A single tuning surface keeps target policy auditable and makes live-test
// changes independent of the action rotation.
internal sealed record PvPTargetWeights(
    float RangeWeight,
    float HpPercentWeight,
    float AbsoluteHpWeight,
    float MaximumHpWeight,
    float AlliedFocusPerPlayer,
    float AlliedFocusCap,
    float ExecuteBonus,
    float CurrentTargetBonus,
    float GuardPenalty,
    float OverextensionPenalty,
    float UnsupportedPenalty)
{
    public static PvPTargetWeights From(Configuration config) => new(
        config.NativeTargetRangeWeight,
        config.NativeTargetHpPercentWeight,
        config.NativeTargetAbsoluteHpWeight,
        config.NativeTargetMaximumHpWeight,
        config.NativeTargetAlliedFocusPerPlayer,
        config.NativeTargetAlliedFocusCap,
        config.NativeTargetExecuteBonus,
        config.NativeTargetStickinessBonus,
        config.NativeTargetGuardPenalty,
        config.NativeTargetOverextensionPenalty,
        config.NativeTargetUnsupportedPenalty);
}
