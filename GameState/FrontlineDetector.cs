using Lumina.Excel.Sheets;

namespace PvPSentinel.GameState;

internal static class FrontlineDetector
{
    public static bool IsFrontline(bool isPvPExcludingDen, bool isBoundByDuty, TerritoryType territory)
    {
        if (!isPvPExcludingDen || !isBoundByDuty || territory.RowId == 0 || !territory.IsPvpZone)
            return false;

        var content = territory.ContentFinderCondition.Value;
        return content.RowId != 0 && content.PvP && content.DailyFrontlineChallenge;
    }
}
