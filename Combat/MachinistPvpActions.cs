namespace PvPSentinel.Combat;

internal static class MachinistPvpActions
{
    // Verified against the current BSD-3-Clause WrathCombo MCH PvP implementation
    // on 2026-09-07. Every ID is also checked against local Lumina action data
    // before PvPSentinel will execute it.
    public const uint BlastCharge = 29402;
    public const uint BlazingShot = 41468;
    public const uint Scattergun = 29404;
    public const uint Drill = 29405;
    public const uint BioBlaster = 29406;
    public const uint AirAnchor = 29407;
    public const uint ChainSaw = 29408;
    public const uint Wildfire = 29409;
    public const uint BishopTurret = 29412;
    public const uint AetherMortar = 29413;
    public const uint Analysis = 29414;
    public const uint MarksmanSpite = 29415;
    public const uint FullMetalField = 41469;

    public static readonly IReadOnlyDictionary<uint, string> ExpectedNames = new Dictionary<uint, string>
    {
        [BlastCharge] = "Blast Charge",
        [BlazingShot] = "Blazing Shot",
        [Scattergun] = "Scattergun",
        [Drill] = "Drill",
        [BioBlaster] = "Bioblaster",
        [AirAnchor] = "Air Anchor",
        [ChainSaw] = "Chain Saw",
        [Wildfire] = "Wildfire",
        [BishopTurret] = "Bishop Autoturret",
        [AetherMortar] = "Aether Mortar",
        [Analysis] = "Analysis",
        [MarksmanSpite] = "Marksman's Spite",
        [FullMetalField] = "Full Metal Field",
    };
}

