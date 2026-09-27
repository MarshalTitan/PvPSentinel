using Dalamud.Configuration;
using Dalamud.Plugin;
using PvPSentinel.Models;

namespace PvPSentinel;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 10;
    public bool Enabled { get; set; }
    public bool NavigationEnabled { get; set; }
    public bool AutonomousStrategyEnabled { get; set; }
    public CombatProvider CombatProvider { get; set; } = CombatProvider.Off;
    public NativeCombatMode NativeCombatMode { get; set; } = NativeCombatMode.ShadowObserve;
    public bool TargetSelectionEnabled { get; set; } = true;
    public bool FinishKoPriorityEnabled { get; set; } = true;
    public bool MountingEnabled { get; set; }
    // Mount sheet row 1 is Company Chocobo. This is resolved and validated
    // against the live game-data row before use; it is not an action ID.
    public uint PreferredMountId { get; set; } = 1;
    public string PreferredMountName { get; set; } = "Company Chocobo";
    public bool ObjectiveNavigationEnabled { get; set; }
    public bool QueueAutomationEnabled { get; set; }
    public bool ShowDiagnostics { get; set; } = true;
    public bool VerboseLogging { get; set; }
    public bool TargetCounterEnabled { get; set; } = true;
    public bool TargetCounterOnlyInPvp { get; set; } = true;
    public bool TargetCounterHideAtZero { get; set; } = true;
    public bool TargetCounterShowJobs { get; set; }
    public bool TargetCounterLocked { get; set; }
    public float TargetCounterNumberSize { get; set; } = 64f;
    public float TargetCounterJobSize { get; set; } = 16f;

    public bool AllowBorderlandRuins { get; set; } = true;
    public bool AllowSealRock { get; set; } = true;
    public bool AllowFieldsOfGlory { get; set; } = true;
    public bool AllowOnsalHakair { get; set; } = true;
    public bool AllowWorqorChirteh { get; set; } = true;
    public int MatchLimit { get; set; } = 1;

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
    public float ExternalCombatYieldSeconds { get; set; } = 2.5f;
    public float RotationSolverQuietSeconds { get; set; } = 5f;
    public float RotationSolverEnemyClearanceRadius { get; set; } = 30f;
    public float NativeRecuperateHpPercent { get; set; } = 75f;
    public float NativeGuardHpPercent { get; set; } = 35f;
    public float NativeGuardMaximumHpPercent { get; set; } = 65f;
    public int NativeGuardMinimumThreats { get; set; } = 2;
    public float NativeGuardTargeterHpBonus { get; set; } = 4f;
    public float NativeGuardNearbyThreatHpBonus { get; set; } = 1.5f;
    public int NativeGuardNearbyThreatBaseline { get; set; } = 2;
    public float NativeGuardDisadvantageHpBonus { get; set; } = 2f;
    public float NativeGuardRapidLossPercentPerSecond { get; set; } = 12f;
    public float NativeGuardRapidLossHpBonus { get; set; } = 8f;
    public float NativeTargetMaximumRange { get; set; } = 50f;
    public float NativeTargetOverextensionRange { get; set; } = 35f;
    public float NativeTargetMinimumScore { get; set; } = 10f;
    public float NativeTargetSwitchAdvantage { get; set; } = 24f;
    public float NativeTargetMinimumCommitmentSeconds { get; set; } = 3.5f;
    public float NativeTargetRangeWeight { get; set; } = 28f;
    public float NativeTargetHpPercentWeight { get; set; } = 24f;
    public float NativeTargetAbsoluteHpWeight { get; set; } = 12f;
    public float NativeTargetMaximumHpWeight { get; set; } = 8f;
    public float NativeTargetAlliedFocusPerPlayer { get; set; } = 9f;
    public float NativeTargetAlliedFocusCap { get; set; } = 36f;
    public float NativeTargetExecuteBonus { get; set; } = 22f;
    public float NativeTargetStickinessBonus { get; set; } = 14f;
    public float NativeTargetGuardPenalty { get; set; } = 42f;
    public float NativeTargetOverextensionPenalty { get; set; } = 45f;
    public float NativeTargetUnsupportedPenalty { get; set; } = 16f;
    public uint NativeMarksmanBaseDamage { get; set; } = 40000;
    public float NativeMarksmanSoloConfidence { get; set; } = 0.9f;
    public int NativeMarksmanUncreditedFocus { get; set; } = 1;
    public int NativeMarksmanHighHpMinimumFocus { get; set; } = 3;
    public uint NativeMarksmanFocusAllowance { get; set; } = 3000;
    public uint NativeMarksmanMaximumEffectiveHp { get; set; } = 56000;
    public uint NativeMarksmanOverkillMinimumHp { get; set; } = 10000;
    public uint NativeMarksmanOverkillFocusHpPerPlayer { get; set; } = 2000;
    public uint NativeMarksmanOverkillMaximumMinimumHp { get; set; } = 20000;
    public uint NativeWildfireMinimumEffectiveHp { get; set; } = 28000;
    public uint NativeWildfireFocusHpPerPlayer { get; set; } = 2500;
    public int NativeWildfireUncreditedFocus { get; set; } = 1;
    public uint NativeWildfireMaximumMinimumHp { get; set; } = 45000;
    public float MountDistance { get; set; } = 55f;
    public float DismountDistance { get; set; } = 28f;
    public float MountEnemySafetyRadius { get; set; } = 30f;
    public float PathRequestTimeoutSeconds { get; set; } = 8f;
    public float StuckTimeoutSeconds { get; set; } = 5f;
    public int MaximumPathFailures { get; set; } = 3;

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface = pi;
        MigrateFailClosedDefaults();
    }

    public bool IsMapAllowed(FrontlineMap map) => map switch
    {
        FrontlineMap.BorderlandRuins => AllowBorderlandRuins,
        FrontlineMap.SealRock => AllowSealRock,
        FrontlineMap.FieldsOfGlory => AllowFieldsOfGlory,
        FrontlineMap.OnsalHakair => AllowOnsalHakair,
        FrontlineMap.WorqorChirteh => AllowWorqorChirteh,
        _ => false,
    };

    private void MigrateFailClosedDefaults()
    {
        if (Version >= 10)
            return;

        // Version 1 exposed independent booleans for navigation and native action
        // execution. Requiring an explicit re-enable prevents an old development
        // config from silently activating the new strategic/lifecycle systems.
        if (Version < 2)
        {
            Enabled = false;
            NavigationEnabled = false;
            CombatProvider = CombatProvider.Off;
            MountingEnabled = false;
            ObjectiveNavigationEnabled = false;
            QueueAutomationEnabled = false;
        }

        if (Version < 3)
        {
            // Native execution did not exist as an explicit development mode before
            // version 3. Existing installations always enter the read-only observer.
            NativeCombatMode = NativeCombatMode.ShadowObserve;
        }

        if (Version < 4)
        {
            // v0.2.0.1 deliberately narrows the observer's safety envelope. Apply
            // these safer defaults to existing v0.2.0.0 installations rather than
            // retaining the earlier, more speculative LB values.
            NativeTargetMinimumScore = 10f;
            NativeGuardMaximumHpPercent = 65f;
            NativeGuardTargeterHpBonus = 4f;
            NativeGuardNearbyThreatHpBonus = 1.5f;
            NativeGuardNearbyThreatBaseline = 2;
            NativeGuardDisadvantageHpBonus = 2f;
            NativeGuardRapidLossPercentPerSecond = 12f;
            NativeGuardRapidLossHpBonus = 8f;
            NativeMarksmanSoloConfidence = 0.9f;
            NativeMarksmanUncreditedFocus = 1;
            NativeMarksmanHighHpMinimumFocus = 3;
            NativeMarksmanFocusAllowance = 3000;
            NativeMarksmanMaximumEffectiveHp = 56000;
        }

        if (Version < 5)
        {
            // The targeting-me counter is observational only and does not widen
            // any combat or navigation permission. Existing users receive the
            // compact PvP-only display with zero-count hiding and no job row.
            TargetCounterEnabled = true;
            TargetCounterOnlyInPvp = true;
            TargetCounterHideAtZero = true;
            TargetCounterShowJobs = false;
            TargetCounterLocked = false;
            TargetCounterNumberSize = 64f;
            TargetCounterJobSize = 16f;
        }

        if (Version < 6)
        {
            // v0.2.0.5 adds an anti-overkill survival floor for deliberate
            // Wildfire commitments. Existing installations receive the same
            // conservative defaults as fresh configurations.
            NativeWildfireMinimumEffectiveHp = 28000;
            NativeWildfireFocusHpPerPlayer = 2500;
            NativeWildfireUncreditedFocus = 1;
            NativeWildfireMaximumMinimumHp = 45000;
        }

        if (Version < 7)
        {
            // The v0.2.0.5 validation pass showed that a barely-positive score
            // could still produce short-lived target churn, and that a full
            // limit gauge could be recommended into an already-collapsing,
            // heavily focused target. Prefer steadier commitment and conserve
            // Marksman's Spite when normal-range pressure should finish the KO.
            NativeTargetSwitchAdvantage = 24f;
            NativeTargetMinimumCommitmentSeconds = 3.5f;
            NativeMarksmanOverkillMinimumHp = 10000;
            NativeMarksmanOverkillFocusHpPerPlayer = 2000;
            NativeMarksmanOverkillMaximumMinimumHp = 20000;
        }

        if (Version < 8)
        {
            // RotationSolverReborn remains an explicit provider selection. These
            // values only govern when Sentinel resumes strategic travel after an
            // observed engagement; they do not activate Reborn or Native combat.
            RotationSolverQuietSeconds = 5f;
            RotationSolverEnemyClearanceRadius = 30f;
        }

        if (Version < 9)
        {
            // M2 is deliberately manual. Existing automation selections must not
            // silently issue strategic destinations or queue for another match.
            AutonomousStrategyEnabled = false;
            ObjectiveNavigationEnabled = false;
            QueueAutomationEnabled = false;
        }

        if (Version < 10)
        {
            // Mount Roulette was the only summon path before v0.3.0.6. Keep the
            // default semantic (rather than an action ID) so the live Mount sheet
            // resolves Company Chocobo in the current client data.
            PreferredMountId = 1;
            PreferredMountName = "Company Chocobo";
        }

        Version = 10;
        Save();
    }

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
