using Dalamud.Configuration;
using Dalamud.Plugin;

namespace PvPSentinel;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; }
    public bool NavigationEnabled { get; set; } = true;
    public bool CombatEnabled { get; set; }
    public bool TargetSelectionEnabled { get; set; } = true;
    public bool FinishKoPriorityEnabled { get; set; } = true;
    public bool ShowDiagnostics { get; set; } = true;
    public bool VerboseLogging { get; set; }

    public float FriendlyClusterLinkRadius { get; set; } = 14f;
    public float MainGroupFollowRadius { get; set; } = 18f;
    public float MainGroupRegroupDistance { get; set; } = 38f;
    public float RangedPositionOffset { get; set; } = 12f;
    public float EnemyEngagementRadius { get; set; } = 25f;
    public float FinishTargetHpPercent { get; set; } = 20f;
    public float FinishTargetMaxChaseDistance { get; set; } = 25f;
    public float DestinationSwitchDistance { get; set; } = 8f;
    public float MinimumDestinationCommitmentSeconds { get; set; } = 5f;
    public float TargetSwitchScoreAdvantage { get; set; } = 15f;
    public float MinimumTargetCommitmentSeconds { get; set; } = 2f;

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    public void Save() => pluginInterface?.SavePluginConfig(this);
}

