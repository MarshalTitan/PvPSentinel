using System.Text.RegularExpressions;

namespace PvPSentinel.FrontlineCore.Diagnostics;

internal static partial class PrivacySanitizer
{
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var compact = ControlCharacters().Replace(value, " ");
        compact = PlayerLikeName().Replace(compact, "[PLAYER]");
        return compact.Length <= 320 ? compact : compact[..320] + "…";
    }

    [GeneratedRegex(@"[\u0000-\u001F\u007F]+")]
    private static partial Regex ControlCharacters();

    [GeneratedRegex(@"\b[A-Z][a-z]{2,15}\s+[A-Z][a-z]{2,15}\b")]
    private static partial Regex PlayerLikeName();
}

