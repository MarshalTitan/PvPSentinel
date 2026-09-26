using PvPSentinel.Models;

namespace PvPSentinel.Combat.Native.Jobs.Machinist;

internal sealed record MachinistState(
    bool HasAnalysis,
    bool Overheated,
    bool DrillPrimed,
    bool BioblasterPrimed,
    bool AirAnchorPrimed,
    bool ChainSawPrimed)
{
    public string PrimedTool => DrillPrimed
        ? "Drill"
        : BioblasterPrimed
            ? "Bioblaster"
            : AirAnchorPrimed
                ? "Air Anchor"
                : ChainSawPrimed
                    ? "Chain Saw"
                    : "None";

    public string Summary => $"primed={PrimedTool}; Analysis={HasAnalysis}; Overheated={Overheated}";

    public static MachinistState Capture(PlayerSnapshot local) => new(
        local.HasStatus(3158) || local.HasStatusExact("Analysis"),
        local.HasStatus(3149) || local.HasStatusExact("Overheated"),
        local.HasStatus(3150) || local.HasStatusExact("Drill Primed"),
        local.HasStatus(3151) || local.HasStatusExact("Bioblaster Primed"),
        local.HasStatus(3152) || local.HasStatusExact("Air Anchor Primed"),
        local.HasStatus(3153) || local.HasStatusExact("Chain Saw Primed"));
}
