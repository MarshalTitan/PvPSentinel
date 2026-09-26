namespace PvPSentinel.Combat.Native.Defense;

internal static class PvPCommonActions
{
    public const uint Guard = 29054;
    public const uint StandardIssueElixir = 29055;
    public const uint Purify = 29056;
    public const uint Recuperate = 29711;

    public static readonly IReadOnlyDictionary<uint, string> ExpectedNames = new Dictionary<uint, string>
    {
        [Guard] = "Guard",
        [StandardIssueElixir] = "Standard-issue Elixir",
        [Purify] = "Purify",
        [Recuperate] = "Recuperate",
    };
}
