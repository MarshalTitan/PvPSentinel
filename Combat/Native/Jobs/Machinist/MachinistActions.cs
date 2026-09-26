namespace PvPSentinel.Combat.Native.Jobs.Machinist;

internal static class MachinistActions
{
    public const uint BlastCharge = 29402;
    public const uint BlazingShot = 41468;
    public const uint Scattergun = 29404;
    public const uint Drill = 29405;
    public const uint Bioblaster = 29406;
    public const uint AirAnchor = 29407;
    public const uint ChainSaw = 29408;
    public const uint Wildfire = 29409;
    public const uint BishopAutoturret = 29412;
    public const uint AetherMortar = 29413;
    public const uint Analysis = 29414;
    public const uint MarksmansSpite = 29415;
    public const uint FullMetalField = 41469;

    public const uint Dervish = 43249;
    public const uint Bravery = 43250;
    public const uint EagleEyeShot = 43251;

    public static readonly IReadOnlyDictionary<uint, string> ExpectedNames = new Dictionary<uint, string>
    {
        [BlastCharge] = "Blast Charge",
        [BlazingShot] = "Blazing Shot",
        [Scattergun] = "Scattergun",
        [Drill] = "Drill",
        [Bioblaster] = "Bioblaster",
        [AirAnchor] = "Air Anchor",
        [ChainSaw] = "Chain Saw",
        [Wildfire] = "Wildfire",
        [BishopAutoturret] = "Bishop Autoturret",
        [AetherMortar] = "Aether Mortar",
        [Analysis] = "Analysis",
        [MarksmansSpite] = "Marksman's Spite",
        [FullMetalField] = "Full Metal Field",
        [Dervish] = "Dervish",
    };
}
