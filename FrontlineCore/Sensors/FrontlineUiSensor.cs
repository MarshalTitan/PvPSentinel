using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace PvPSentinel.FrontlineCore.Sensors;

internal sealed partial class FrontlineUiSensor(IGameGui gameGui)
{
    private static readonly string[] KnownGrandCompanies = ["Maelstrom", "Twin Adder", "Immortal Flames"];

    public unsafe FrontlineUiObservation Capture()
    {
        var header = gameGui.GetAddonByName<AtkUnitBase>("PvPFrontlineHeader");
        var results = gameGui.GetAddonByName<AtkUnitBase>("FrontlineRecord");
        var wideText = gameGui.GetAddonByName<AtkUnitBase>("_WideText");
        var headerVisible = header is not null && header->IsReady && header->IsVisible;
        var resultsVisible = results is not null && results->IsReady && results->IsVisible;
        var strings = headerVisible ? ReadStrings(header!, 256) : [];
        var time = strings.Select(ParseTimer).FirstOrDefault(value => value is not null);
        var scoreCap = strings.Select(ParseScoreCap).FirstOrDefault(value => value is not null);
        var companies = KnownGrandCompanies.Where(company => strings.Any(text =>
            text.Contains(company, StringComparison.OrdinalIgnoreCase))).ToArray();
        var announcement = wideText is not null && wideText->IsReady && wideText->IsVisible
            ? ReadStrings(wideText, 64).FirstOrDefault(IsObjectiveAnnouncement) ?? string.Empty
            : string.Empty;
        var evidence = resultsVisible
            ? "FrontlineRecord is visible."
            : headerVisible
                ? $"PvPFrontlineHeader is visible; timer={(time is null ? "UNRESOLVED" : time.Value.ToString("mm\\:ss"))}."
                : "Frontline header/results addons are not visible.";
        if (!string.IsNullOrEmpty(announcement))
            evidence += " _WideText contains a Frontline objective lifecycle announcement.";
        return new FrontlineUiObservation(
            headerVisible,
            resultsVisible,
            time,
            scoreCap,
            companies,
            announcement,
            evidence);
    }

    private static unsafe IReadOnlyList<string> ReadStrings(AtkUnitBase* addon, int maximum)
    {
        if (addon is null || addon->AtkValues is null)
            return [];
        var result = new List<string>();
        var count = Math.Min((int)addon->AtkValuesCount, maximum);
        for (var index = 0; index < count; index++)
        {
            var value = addon->AtkValues[index];
            if (value.Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString))
                continue;
            var text = value.GetValueAsString();
            if (!string.IsNullOrWhiteSpace(text))
                result.Add(text);
        }
        return result;
    }

    private static TimeSpan? ParseTimer(string text)
    {
        var match = TimerRegex().Match(text);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var minutes) ||
            !int.TryParse(match.Groups[2].Value, out var seconds) || seconds > 59)
            return null;
        return TimeSpan.FromSeconds((minutes * 60) + seconds);
    }

    private static int? ParseScoreCap(string text)
    {
        var match = ScoreCapRegex().Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var cap) && cap is >= 100 and <= 5000
            ? cap
            : null;
    }

    private static bool IsObjectiveAnnouncement(string text) =>
        text.Contains("tomelith", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("ice", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("captur", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("deactiv", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?<!\d)(\d{1,2}):(\d{2})(?!\d)")]
    private static partial Regex TimerRegex();

    [GeneratedRegex(@"(?:score|victory|cap)[^\d]{0,20}(\d{3,4})", RegexOptions.IgnoreCase)]
    private static partial Regex ScoreCapRegex();
}
