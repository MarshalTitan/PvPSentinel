using System.Text.RegularExpressions;

namespace PvPSentinel.FrontlineCore.Maps;

internal static partial class ShatterObjectivePolicy
{
    public static (string Kind, ObjectiveLifecycle State)? Resolve(uint stateId) => stateId switch
    {
        60901 => ("LARGE", ObjectiveLifecycle.Inactive),
        60989 => ("LARGE", ObjectiveLifecycle.Preactivating),
        60902 => ("LARGE", ObjectiveLifecycle.Active),
        60903 => ("SMALL", ObjectiveLifecycle.Inactive),
        60990 => ("SMALL", ObjectiveLifecycle.Preactivating),
        60904 => ("SMALL", ObjectiveLifecycle.Active),
        _ => null,
    };

    public static (uint StateId, string Kind, ObjectiveLifecycle State)? Resolve(
        FrontlineMapMarkerObservation marker)
    {
        foreach (var candidate in new[] { marker.IconId, marker.DataId, marker.ObjectiveId })
        {
            if (Resolve(candidate) is { } resolved)
                return (candidate, resolved.Kind, resolved.State);
        }
        return null;
    }

    public static string? ParseLogicalId(string? text)
    {
        var match = LogicalIdRegex().Match(text ?? string.Empty);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    public static int? ParseActivationEtaSeconds(string? text)
    {
        var match = EtaRegex().Match(text ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out var minutes) &&
               int.TryParse(match.Groups[2].Value, out var seconds)
            ? (minutes * 60) + seconds
            : null;
    }

    public static int? ParseStrengthPercent(string? text)
    {
        var match = StrengthRegex().Match(text ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out var strength) ? strength : null;
    }

    [GeneratedRegex(@"Icebound\s+Tomelith\s+([AB]\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex LogicalIdRegex();
    [GeneratedRegex(@"Activation\s+in:\s*(\d+):(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex EtaRegex();
    [GeneratedRegex(@"Ice\s+Strength:\s*(\d+)%", RegexOptions.IgnoreCase)]
    private static partial Regex StrengthRegex();
}
