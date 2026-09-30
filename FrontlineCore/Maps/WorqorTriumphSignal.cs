using System.Text.RegularExpressions;

namespace PvPSentinel.FrontlineCore.Maps;

internal enum WorqorTriumphPhase
{
    Unknown,
    Activating,
    Unclaimed,
    Claimed,
}

internal sealed record WorqorTriumphSignal(
    int Number,
    string Rank,
    WorqorTriumphPhase Phase,
    int? ActivationEtaSeconds,
    int? MarkerFaction);

internal static partial class WorqorTriumphSignals
{
    // These are the English marker tooltips observed across five Worqor matches.
    // The trailing 4/5/6 follows the claimed icon family but has not been
    // mapped authoritatively to the local zero-based Battalion team.
    [GeneratedRegex(@"^Triumph\s+(?<number>\d{1,2})\s+(?:Rank\s+(?<rank>[SAB])\s+)?(?:(?<activating>Activating in:\s*(?<minutes>\d+):(?<seconds>\d+))|(?<unclaimed>Unclaimed)|(?<claimed>Claimed)(?:\s+(?<faction>[456]))?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TooltipPattern();

    public static WorqorTriumphSignal? Parse(string tooltip)
    {
        var match = TooltipPattern().Match(tooltip.Trim());
        if (!match.Success || !int.TryParse(match.Groups["number"].Value, out var number) ||
            number is < 1 or > 12)
            return null;

        var rank = match.Groups["rank"].Success ? match.Groups["rank"].Value.ToUpperInvariant() : "UNRESOLVED";
        var phase = match.Groups["activating"].Success ? WorqorTriumphPhase.Activating
            : match.Groups["unclaimed"].Success ? WorqorTriumphPhase.Unclaimed
            : match.Groups["claimed"].Success ? WorqorTriumphPhase.Claimed
            : WorqorTriumphPhase.Unknown;
        int? eta = null;
        if (phase == WorqorTriumphPhase.Activating &&
            int.TryParse(match.Groups["minutes"].Value, out var minutes) &&
            int.TryParse(match.Groups["seconds"].Value, out var seconds) && seconds <= 59)
            eta = Math.Min(3600, minutes * 60 + seconds);
        int? faction = int.TryParse(match.Groups["faction"].Value, out var parsedFaction) ? parsedFaction : null;
        return new WorqorTriumphSignal(number, rank, phase, eta, faction);
    }
}
