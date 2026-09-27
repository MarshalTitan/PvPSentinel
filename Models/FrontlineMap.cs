namespace PvPSentinel.Models;

public enum FrontlineMap
{
    Unknown,
    BorderlandRuins,
    SealRock,
    FieldsOfGlory,
    OnsalHakair,
    WorqorChirteh,
}

internal static class FrontlineMapCatalog
{
    // ContentFinderCondition and current territory IDs come from local Lumina
    // game data. Keeping both identifiers makes map detection language-neutral.
    private static readonly IReadOnlyDictionary<uint, FrontlineMap> ByContentFinderId =
        new Dictionary<uint, FrontlineMap>
        {
            [127] = FrontlineMap.BorderlandRuins,
            [130] = FrontlineMap.SealRock,
            [180] = FrontlineMap.FieldsOfGlory,
            [701] = FrontlineMap.OnsalHakair,
            [1080] = FrontlineMap.WorqorChirteh,
        };

    private static readonly IReadOnlyDictionary<uint, FrontlineMap> ByTerritoryId =
        new Dictionary<uint, FrontlineMap>
        {
            [1273] = FrontlineMap.BorderlandRuins,
            [431] = FrontlineMap.SealRock,
            [554] = FrontlineMap.FieldsOfGlory,
            [888] = FrontlineMap.OnsalHakair,
            [1313] = FrontlineMap.WorqorChirteh,
        };

    public static FrontlineMap Identify(uint contentFinderConditionId, uint territoryId)
    {
        if (ByContentFinderId.TryGetValue(contentFinderConditionId, out var contentMap))
            return contentMap;
        return ByTerritoryId.GetValueOrDefault(territoryId, FrontlineMap.Unknown);
    }

    public static uint ContentFinderConditionId(this FrontlineMap map) => map switch
    {
        FrontlineMap.BorderlandRuins => 127,
        FrontlineMap.SealRock => 130,
        FrontlineMap.FieldsOfGlory => 180,
        FrontlineMap.OnsalHakair => 701,
        FrontlineMap.WorqorChirteh => 1080,
        _ => 0,
    };

    public static uint TerritoryId(this FrontlineMap map) => map switch
    {
        FrontlineMap.BorderlandRuins => 1273,
        FrontlineMap.SealRock => 431,
        FrontlineMap.FieldsOfGlory => 554,
        FrontlineMap.OnsalHakair => 888,
        FrontlineMap.WorqorChirteh => 1313,
        _ => 0,
    };

    public static bool HasExactIdentity(this FrontlineMap map, uint contentFinderConditionId, uint territoryId) =>
        map != FrontlineMap.Unknown &&
        map.ContentFinderConditionId() == contentFinderConditionId &&
        map.TerritoryId() == territoryId;

    public static string DescribeIdentity(
        FrontlineMap map,
        uint contentFinderConditionId,
        uint territoryId) => map.HasExactIdentity(contentFinderConditionId, territoryId)
        ? $"VERIFIED runtime Lumina pair: territory {territoryId}, duty {contentFinderConditionId}"
        : $"MISMATCH / UNRESOLVED: observed territory {territoryId}, duty {contentFinderConditionId}; expected {map.TerritoryId()}/{map.ContentFinderConditionId()}";

    public static FrontlineMap IdentifyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return FrontlineMap.Unknown;

        if (text.Contains("Borderland Ruins", StringComparison.OrdinalIgnoreCase))
            return FrontlineMap.BorderlandRuins;
        if (text.Contains("Seal Rock", StringComparison.OrdinalIgnoreCase))
            return FrontlineMap.SealRock;
        if (text.Contains("Fields of Glory", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Shatter", StringComparison.OrdinalIgnoreCase))
            return FrontlineMap.FieldsOfGlory;
        if (text.Contains("Onsal Hakair", StringComparison.OrdinalIgnoreCase))
            return FrontlineMap.OnsalHakair;
        if (text.Contains("Worqor Chirteh", StringComparison.OrdinalIgnoreCase))
            return FrontlineMap.WorqorChirteh;
        return FrontlineMap.Unknown;
    }

    public static string DisplayName(this FrontlineMap map) => map switch
    {
        FrontlineMap.BorderlandRuins => "The Borderland Ruins (Secure)",
        FrontlineMap.SealRock => "Seal Rock (Seize)",
        FrontlineMap.FieldsOfGlory => "The Fields of Glory (Shatter)",
        FrontlineMap.OnsalHakair => "Onsal Hakair (Danshig Naadam)",
        FrontlineMap.WorqorChirteh => "Worqor Chirteh (Triumph)",
        _ => "Unknown Frontline campaign",
    };
}
