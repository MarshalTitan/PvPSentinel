namespace PvPSentinel.Integrations;

internal sealed class WrathAdapter
{
    // WrathCombo's documented IPC explicitly excludes supported control of PvP
    // combos/options. Keep this boundary in place for a later replacement-action
    // compatibility experiment, without making Wrath a dependency today.
    public bool IsEnabled => false;
    public string Status => "Optional integration inactive (Wrath PvP IPC is unsupported).";
}

