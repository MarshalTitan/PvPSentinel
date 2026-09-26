namespace PvPSentinel.Combat.Native.Targeting;

internal sealed record TargetScore(
    float Total,
    float Range,
    float Health,
    float AbsoluteHealth,
    float MaximumHealth,
    float AlliedFocus,
    float Execute,
    float Stickiness,
    float Guard,
    float Overextension)
{
    public string Summary =>
        $"score {Total:F1} [range {Range:+0.0;-0.0;0.0}, hp% {Health:+0.0;-0.0;0.0}, absolute {AbsoluteHealth:+0.0;-0.0;0.0}, maxHP {MaximumHealth:+0.0;-0.0;0.0}, focus {AlliedFocus:+0.0;-0.0;0.0}, execute {Execute:+0.0;-0.0;0.0}, sticky {Stickiness:+0.0;-0.0;0.0}, guard {Guard:+0.0;-0.0;0.0}, extend {Overextension:+0.0;-0.0;0.0}]";
}
