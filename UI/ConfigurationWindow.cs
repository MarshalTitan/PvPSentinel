using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Utility;
using SentinelCore.UI;
using PvPSentinel.Models;
using PvPSentinel.Integrations;
using PvPSentinel.Navigation;
using PvPSentinel.Strategy;
using System.Numerics;

namespace PvPSentinel.UI;

internal sealed class ConfigurationWindow : Window
{
    private enum ConfigurationPage { Core, Movement, Combat, NativeCombat, Diagnostics, Frontline, Appearance }
    private readonly SentinelModernStyleScope modernStyle = new();
    private readonly Action drawModernNavigation;
    private readonly Action drawModernContent;
    private ConfigurationPage selectedPage;
    private bool IsModern => SentinelThemeState<ConfigurationPage>.NormalizeTheme(config.ConfigurationTheme)
        == SentinelThemeKind.Modern;

    private readonly Configuration config;
    private readonly Action<bool> diagnosticsVisibilityChanged;
    private readonly Action<bool> verboseLoggingChanged;
    private readonly Action emergencyStop;
    private readonly Action clearEmergencyStop;
    private readonly Action resetMatchCounter;
    private readonly Func<bool> isEmergencyStopped;
    private readonly Func<RotationSolverRebornStatus> rebornStatus;
    private readonly PreferredMountCatalog mountCatalog;
    private readonly Func<FrontlinePilotReadiness?> readiness;
    private readonly Func<FrontlineMap> currentMap;

    public ConfigurationWindow(
        Configuration config,
        Action<bool> diagnosticsVisibilityChanged,
        Action<bool> verboseLoggingChanged,
        Action emergencyStop,
        Action clearEmergencyStop,
        Action resetMatchCounter,
        Func<bool> isEmergencyStopped,
        Func<RotationSolverRebornStatus> rebornStatus,
        Func<FrontlinePilotReadiness?> readiness, Func<FrontlineMap> currentMap,
        PreferredMountCatalog mountCatalog)
        : base("PvP Sentinel - Configuration###PvPSentinelConfiguration")
    {
        this.config = config;
        this.diagnosticsVisibilityChanged = diagnosticsVisibilityChanged;
        this.verboseLoggingChanged = verboseLoggingChanged;
        this.emergencyStop = emergencyStop;
        this.clearEmergencyStop = clearEmergencyStop;
        this.resetMatchCounter = resetMatchCounter;
        this.isEmergencyStopped = isEmergencyStopped;
        this.rebornStatus = rebornStatus;
        this.readiness = readiness;
        this.currentMap = currentMap;
        this.mountCatalog = mountCatalog;
        drawModernNavigation = DrawModernNavigation;
        drawModernContent = DrawModernContent;
        Size = new Vector2(550, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw()
    {
        if (IsModern)
            modernStyle.Push(ImGuiHelpers.GlobalScale);
    }

    public override void PostDraw() => modernStyle.Pop();

    public override void Draw()
    {
        if (IsModern)
            DrawModern();
        else
            DrawClassic();
    }

    private void DrawClassic()
    {
        if (BeginSection("Core", true))
            DrawCore();

        if (BeginSection("Diagnostics and testing display", true))
            DrawDiagnostics();

        if (BeginSection("Movement and clustering"))
            DrawMovement();

        if (BeginSection("Advanced combat policy"))
            DrawCombat();

        if (BeginSection("Native PvP combat (experimental)"))
            DrawNativeCombat();

        if (BeginSection("Frontline lifecycle and allowed maps"))
            DrawFrontline();

        if (BeginSection("Appearance"))
            DrawAppearance();

        ImGui.Spacing();
        if (ImGui.Button("Save configuration"))
            config.Save();
        ImGui.SameLine();
        ImGui.TextDisabled("Changes are also saved as they are made.");
    }

    private void DrawModern()
    {
        var pilot = readiness();
        var status = isEmergencyStopped() ? new SentinelModernStatus("STOPPED", SentinelModernStatusTone.Warning)
            : pilot?.CanTravel == true ? new SentinelModernStatus("NAV READY", SentinelModernStatusTone.Success)
            : new SentinelModernStatus("STANDBY", SentinelModernStatusTone.Neutral);
        var options = new SentinelModernShellOptions(
            "PvPSentinel-Modern", "MARSHALTITAN  /  SENTINEL", "PVP SENTINEL",
            "Frontline strategy and supervised navigation")
        {
            Scale = ImGuiHelpers.GlobalScale,
            ContextLabel = "Modern",
            Status = status,
        };
        SentinelModernConfigurationShell.Draw(options, drawModernNavigation, drawModernContent);
    }

    private void DrawModernNavigation()
    {
        var scale = ImGuiHelpers.GlobalScale;
        SentinelModernNavigation.GroupLabel("NAVIGATION");
        NavItem(ConfigurationPage.Core, "Core", scale);
        NavItem(ConfigurationPage.Movement, "Movement", scale);
        ImGui.Spacing();
        SentinelModernNavigation.GroupLabel("COMBAT");
        NavItem(ConfigurationPage.Combat, "Combat policy", scale);
        NavItem(ConfigurationPage.NativeCombat, "Native combat", scale);
        ImGui.Spacing();
        SentinelModernNavigation.GroupLabel("TOOLS");
        NavItem(ConfigurationPage.Diagnostics, "Diagnostics", scale);
        NavItem(ConfigurationPage.Frontline, "Frontline lifecycle", scale);
        NavItem(ConfigurationPage.Appearance, "Appearance", scale);
    }

    private void NavItem(ConfigurationPage page, string title, float scale)
    {
        if (SentinelModernNavigation.Item(page.ToString(), title, selectedPage == page, scale))
            selectedPage = page;
    }

    private void DrawModernContent()
    {
        var (title, description) = selectedPage switch
        {
            ConfigurationPage.Core => ("Core", "Enable travel, select a combat provider, and see readiness."),
            ConfigurationPage.Diagnostics => ("Diagnostics", "Visibility and event logging for supervised tests."),
            ConfigurationPage.Movement => ("Movement", "Clustering, mounting, and bounded route recovery."),
            ConfigurationPage.Combat => ("Combat policy", "Target scoring and provider coordination."),
            ConfigurationPage.NativeCombat => ("Native combat", "Experimental action and defensive thresholds."),
            ConfigurationPage.Frontline => ("Frontline lifecycle", "Future queue preferences; queueing remains disabled."),
            _ => ("Appearance", "Choose the presentation for PvPSentinel windows."),
        };
        SentinelModernUi.PageHeading(title, description);
        ImGui.Spacing();
        using var card = SentinelModernCard.Begin("PvPSentinel-ModernPageCard");
        if (!card.IsVisible)
            return;
        switch (selectedPage)
        {
            case ConfigurationPage.Core: DrawCore(); break;
            case ConfigurationPage.Diagnostics: DrawDiagnostics(); break;
            case ConfigurationPage.Movement: DrawMovement(); break;
            case ConfigurationPage.Combat: DrawCombat(); break;
            case ConfigurationPage.NativeCombat: DrawNativeCombat(); break;
            case ConfigurationPage.Frontline: DrawFrontline(); break;
            case ConfigurationPage.Appearance: DrawAppearance(); break;
        }
    }

    private void DrawCore()
    {
            DrawCheckbox("Enable PvPSentinel", config.Enabled, value => config.Enabled = value);
            DrawCheckbox("Enable navigation", config.NavigationEnabled, value => config.NavigationEnabled = value);
            DrawCombatProvider();
            var pilot = readiness();
            ImGui.Separator();
            ImGui.Text("Status");
            ImGui.Text($"Frontline: {currentMap().DisplayName()}");
            ImGui.Text($"Navigation: {(pilot?.CanTravel == true ? "Active" : config.NavigationEnabled ? "Blocked" : "Off")}");
            ImGui.Text($"Combat provider: {(pilot is { ProviderLoaded: true, ProviderActivityObservable: true } ? "Connected" : "Not connected")}");
            ImGui.Text($"vnavmesh: {(pilot?.MeshReady == true ? "Ready" : "Unavailable/loading")}");
            if (IsModern)
            {
                var scale = ImGuiHelpers.GlobalScale;
                SentinelModernUi.StatusChip(pilot?.CanTravel == true ? "NAV READY" : "NAV BLOCKED",
                    pilot?.CanTravel == true ? SentinelModernStatusTone.Success : SentinelModernStatusTone.Warning, scale);
                SentinelModernUi.StatusChip(pilot?.MeshReady == true ? "MESH READY" : "MESH LOADING",
                    pilot?.MeshReady == true ? SentinelModernStatusTone.Success : SentinelModernStatusTone.Neutral, scale);
            }
            if (ImGui.Button("EMERGENCY STOP", new Vector2(180, 34)))
                emergencyStop();
            if (isEmergencyStopped() && ImGui.Button("Clear emergency-stop latch"))
                clearEmergencyStop();
    }

    private void DrawDiagnostics()
    {
            DrawCheckbox("Show diagnostic window", config.ShowDiagnostics, value =>
            {
                config.ShowDiagnostics = value;
                diagnosticsVisibilityChanged(value);
            });
            DrawCheckbox("Verbose logging", config.VerboseLogging, value =>
            {
                config.VerboseLogging = value;
                verboseLoggingChanged(value);
            });
    }

    private void DrawMovement()
    {
            DrawFloat("Friendly cluster link radius", config.FriendlyClusterLinkRadius, 6f, 30f, value => config.FriendlyClusterLinkRadius = value, "%.1f y");
            DrawFloat("Main-group follow radius", config.MainGroupFollowRadius, 8f, 40f, value => config.MainGroupFollowRadius = value, "%.1f y");
            DrawFloat("Regroup distance", config.MainGroupRegroupDistance, 20f, 80f, value => config.MainGroupRegroupDistance = value, "%.1f y");
            DrawFloat("MCH ranged position offset", config.RangedPositionOffset, 4f, 30f, value => config.RangedPositionOffset = value, "%.1f y");
            DrawFloat("Destination switch threshold", config.DestinationSwitchDistance, 3f, 25f, value => config.DestinationSwitchDistance = value, "%.1f y");
            DrawFloat("Minimum destination commitment", config.MinimumDestinationCommitmentSeconds, 1f, 15f, value => config.MinimumDestinationCommitmentSeconds = value, "%.1f s");
            DrawFloat("Path request timeout", config.PathRequestTimeoutSeconds, 2f, 20f, value => config.PathRequestTimeoutSeconds = value, "%.1f s");
            DrawFloat("Stuck timeout", config.StuckTimeoutSeconds, 2f, 12f, value => config.StuckTimeoutSeconds = value, "%.1f s");
            DrawInt("Maximum consecutive path failures", config.MaximumPathFailures, 1, 8, value => config.MaximumPathFailures = value);
            DrawFloat("Mount distance", config.MountDistance, 30f, 120f, value => config.MountDistance = value, "%.1f y");
            DrawFloat("Dismount distance", config.DismountDistance, 10f, 45f, value => config.DismountDistance = value, "%.1f y");
            DrawFloat("Mount enemy safety radius", config.MountEnemySafetyRadius, 15f, 50f, value => config.MountEnemySafetyRadius = value, "%.1f y");
            DrawPreferredMount();
    }

    private void DrawCombat()
    {
            DrawCheckbox("Enable strategic target scoring", config.TargetSelectionEnabled, value => config.TargetSelectionEnabled = value);
            DrawCheckbox("Prioritize safe finish-KO opportunities", config.FinishKoPriorityEnabled, value => config.FinishKoPriorityEnabled = value);
            DrawFloat("Enemy engagement radius", config.EnemyEngagementRadius, 10f, 40f, value => config.EnemyEngagementRadius = value, "%.1f y");
            DrawFloat("Finish target HP threshold", config.FinishTargetHpPercent, 5f, 50f, value => config.FinishTargetHpPercent = value, "%.0f %%");
            DrawFloat("Finish target max chase", config.FinishTargetMaxChaseDistance, 10f, 40f, value => config.FinishTargetMaxChaseDistance = value, "%.1f y");
            DrawFloat("Target switch score advantage", config.TargetSwitchScoreAdvantage, 0f, 40f, value => config.TargetSwitchScoreAdvantage = value, "%.0f");
            DrawFloat("Minimum target commitment", config.MinimumTargetCommitmentSeconds, 0f, 8f, value => config.MinimumTargetCommitmentSeconds = value, "%.1f s");
            DrawFloat("Generic External ACR yield grace", config.ExternalCombatYieldSeconds, 0.5f, 10f, value => config.ExternalCombatYieldSeconds = value, "%.1f s");
            if (config.CombatProvider == CombatProvider.RotationSolverReborn)
            {
                DrawFloat("Reborn post-combat quiet period", config.RotationSolverQuietSeconds, 1f, 15f, value => config.RotationSolverQuietSeconds = value, "%.1f s");
                DrawFloat("Reborn targeting-threat radius", config.RotationSolverEnemyClearanceRadius, 10f, 60f, value => config.RotationSolverEnemyClearanceRadius = value, "%.1f y");
            }
    }

    private void DrawNativeCombat()
    {
            ImGui.TextWrapped("Shadow / Observe is the safe default: the native combat subsystem evaluates and logs targets/actions but cannot target, act, or move. Active permits verified native actions; strategic navigation remains a separate controller.");
        DrawFloat("Recuperate below HP", config.NativeRecuperateHpPercent, 25f, 95f, value => config.NativeRecuperateHpPercent = value, "%.0f %%");
        DrawFloat("Guard base danger HP", config.NativeGuardHpPercent, 10f, 60f, value => config.NativeGuardHpPercent = value, "%.0f %%");
        DrawFloat("Guard maximum danger HP", config.NativeGuardMaximumHpPercent, 35f, 90f, value => config.NativeGuardMaximumHpPercent = value, "%.0f %%");
        DrawInt("Guard minimum threats", config.NativeGuardMinimumThreats, 1, 6, value => config.NativeGuardMinimumThreats = value);
        DrawFloat("Native target evaluation range", config.NativeTargetMaximumRange, 25f, 60f, value => config.NativeTargetMaximumRange = value, "%.0f y");
        DrawFloat("Native overextension range", config.NativeTargetOverextensionRange, 20f, 50f, value => config.NativeTargetOverextensionRange = value, "%.0f y");
        DrawFloat("Native minimum target safety score", config.NativeTargetMinimumScore, -20f, 80f, value => config.NativeTargetMinimumScore = value, "%.0f");
        DrawFloat("Native switch score advantage", config.NativeTargetSwitchAdvantage, 0f, 60f, value => config.NativeTargetSwitchAdvantage = value, "%.0f");
        DrawFloat("Native target commitment", config.NativeTargetMinimumCommitmentSeconds, 0f, 8f, value => config.NativeTargetMinimumCommitmentSeconds = value, "%.1f s");
        DrawInt("Marksman's Spite base damage", (int)config.NativeMarksmanBaseDamage, 20000, 60000, value => config.NativeMarksmanBaseDamage = (uint)value);
        DrawFloat("Marksman's Spite solo confidence", config.NativeMarksmanSoloConfidence, 0.5f, 1f, value => config.NativeMarksmanSoloConfidence = value, "%.2f");
        DrawInt("Marksman's Spite uncredited focus", config.NativeMarksmanUncreditedFocus, 0, 4, value => config.NativeMarksmanUncreditedFocus = value);
        DrawInt("Marksman's Spite high-HP minimum focus", config.NativeMarksmanHighHpMinimumFocus, 0, 8, value => config.NativeMarksmanHighHpMinimumFocus = value);
        DrawInt("Marksman's Spite focus allowance", (int)config.NativeMarksmanFocusAllowance, 0, 20000, value => config.NativeMarksmanFocusAllowance = (uint)value);
        DrawInt("Marksman's Spite max effective HP", (int)config.NativeMarksmanMaximumEffectiveHp, 40000, 120000, value => config.NativeMarksmanMaximumEffectiveHp = (uint)value);
        DrawInt("Marksman's Spite anti-overkill HP", (int)config.NativeMarksmanOverkillMinimumHp, 0, 30000, value => config.NativeMarksmanOverkillMinimumHp = (uint)value);
        DrawInt("Marksman's Spite anti-overkill HP per focused ally", (int)config.NativeMarksmanOverkillFocusHpPerPlayer, 0, 10000, value => config.NativeMarksmanOverkillFocusHpPerPlayer = (uint)value);
        DrawInt("Marksman's Spite anti-overkill cap", (int)config.NativeMarksmanOverkillMaximumMinimumHp, 0, 50000, value => config.NativeMarksmanOverkillMaximumMinimumHp = (uint)value);
        DrawInt("Wildfire minimum effective HP", (int)config.NativeWildfireMinimumEffectiveHp, 18000, 60000, value => config.NativeWildfireMinimumEffectiveHp = (uint)value);
        DrawInt("Wildfire HP per focused ally", (int)config.NativeWildfireFocusHpPerPlayer, 0, 10000, value => config.NativeWildfireFocusHpPerPlayer = (uint)value);
        DrawInt("Wildfire uncredited focus", config.NativeWildfireUncreditedFocus, 0, 4, value => config.NativeWildfireUncreditedFocus = value);
        DrawInt("Wildfire maximum survival floor", (int)config.NativeWildfireMaximumMinimumHp, 18000, 80000, value => config.NativeWildfireMaximumMinimumHp = (uint)value);
            if (ImGui.TreeNode("Advanced native Guard threat weights"))
        {
            DrawFloat("HP bonus per active targeter", config.NativeGuardTargeterHpBonus, 0f, 12f, value => config.NativeGuardTargeterHpBonus = value, "%.1f");
            DrawFloat("HP bonus per nearby threat", config.NativeGuardNearbyThreatHpBonus, 0f, 6f, value => config.NativeGuardNearbyThreatHpBonus = value, "%.1f");
            DrawInt("Nearby-threat bonus baseline", config.NativeGuardNearbyThreatBaseline, 0, 8, value => config.NativeGuardNearbyThreatBaseline = value);
            DrawFloat("HP bonus per numerical disadvantage", config.NativeGuardDisadvantageHpBonus, 0f, 6f, value => config.NativeGuardDisadvantageHpBonus = value, "%.1f");
            DrawFloat("Rapid HP-loss threshold", config.NativeGuardRapidLossPercentPerSecond, 2f, 40f, value => config.NativeGuardRapidLossPercentPerSecond = value, "%.1f %%/s");
            DrawFloat("Rapid HP-loss threshold bonus", config.NativeGuardRapidLossHpBonus, 0f, 25f, value => config.NativeGuardRapidLossHpBonus = value, "%.1f");
            ImGui.TreePop();
        }
            if (ImGui.TreeNode("Advanced native target-score weights"))
        {
            DrawFloat("Range weight", config.NativeTargetRangeWeight, 0f, 80f, value => config.NativeTargetRangeWeight = value, "%.0f");
            DrawFloat("HP-percent weight", config.NativeTargetHpPercentWeight, 0f, 80f, value => config.NativeTargetHpPercentWeight = value, "%.0f");
            DrawFloat("Absolute-HP weight", config.NativeTargetAbsoluteHpWeight, 0f, 80f, value => config.NativeTargetAbsoluteHpWeight = value, "%.0f");
            DrawFloat("Maximum-HP weight", config.NativeTargetMaximumHpWeight, 0f, 80f, value => config.NativeTargetMaximumHpWeight = value, "%.0f");
            DrawFloat("Allied-focus per player", config.NativeTargetAlliedFocusPerPlayer, 0f, 30f, value => config.NativeTargetAlliedFocusPerPlayer = value, "%.0f");
            DrawFloat("Allied-focus cap", config.NativeTargetAlliedFocusCap, 0f, 100f, value => config.NativeTargetAlliedFocusCap = value, "%.0f");
            DrawFloat("Execute score bonus", config.NativeTargetExecuteBonus, 0f, 80f, value => config.NativeTargetExecuteBonus = value, "%.0f");
            DrawFloat("Current-target stickiness", config.NativeTargetStickinessBonus, 0f, 60f, value => config.NativeTargetStickinessBonus = value, "%.0f");
            DrawFloat("Guard penalty", config.NativeTargetGuardPenalty, 0f, 100f, value => config.NativeTargetGuardPenalty = value, "%.0f");
            DrawFloat("Overextension penalty", config.NativeTargetOverextensionPenalty, 0f, 120f, value => config.NativeTargetOverextensionPenalty = value, "%.0f");
            DrawFloat("Unsupported-target penalty", config.NativeTargetUnsupportedPenalty, 0f, 80f, value => config.NativeTargetUnsupportedPenalty = value, "%.0f");
            ImGui.TreePop();
        }

    }

    private void DrawFrontline()
    {
            ImGui.TextDisabled("Automatic queue / accept / requeue: disabled for manual M2 validation");
            ImGui.TextWrapped("These preferences apply only to future queue work, not navigation within a match.");
            DrawCheckbox("The Borderland Ruins (Secure)", config.AllowBorderlandRuins, value => config.AllowBorderlandRuins = value);
            DrawCheckbox("Seal Rock (Seize)", config.AllowSealRock, value => config.AllowSealRock = value);
            DrawCheckbox("The Fields of Glory (Shatter)", config.AllowFieldsOfGlory, value => config.AllowFieldsOfGlory = value);
            DrawCheckbox("Onsal Hakair (Danshig Naadam)", config.AllowOnsalHakair, value => config.AllowOnsalHakair = value);
            DrawCheckbox("Worqor Chirteh (Triumph)", config.AllowWorqorChirteh, value => config.AllowWorqorChirteh = value);
            DrawInt("Match limit (this session)", config.MatchLimit, 1, 100, value => config.MatchLimit = value);
            if (ImGui.Button("Reset session match counter"))
                resetMatchCounter();
    }

    private void DrawAppearance()
    {
        var modern = IsModern;
        if (ImGui.BeginCombo("Configuration theme", modern ? "Sentinel Modern" : "Classic"))
        {
            if (ImGui.Selectable("Classic", !modern))
                SetTheme(SentinelThemeKind.Classic);
            if (ImGui.Selectable("Sentinel Modern", modern))
                SetTheme(SentinelThemeKind.Modern);
            ImGui.EndCombo();
        }
        ImGui.TextDisabled("Existing configurations remain Classic until changed here.");
        if (ImGui.Button("Save configuration"))
            config.Save();
    }

    private void SetTheme(SentinelThemeKind theme)
    {
        if (config.ConfigurationTheme == (int)theme)
            return;
        config.ConfigurationTheme = (int)theme;
        config.Save();
    }

    private void DrawCheckbox(string label, bool current, Action<bool> setter)
    {
        var value = current;
        var changed = IsModern
            ? SentinelModernControls.Toggle(label, label, ref value, ImGuiHelpers.GlobalScale)
            : ImGui.Checkbox(label, ref value);
        // Core draws the switch; keyboard/gamepad activation is provided by
        // the underlying ImGui item when it did not receive a mouse click.
        if (IsModern && !changed && ImGui.IsItemActivated())
        {
            value = !value;
            changed = true;
        }
        if (!changed)
            return;
        setter(value);
        config.Save();
    }

    private void DrawFloat(string label, float current, float min, float max, Action<float> setter, string format)
    {
        var value = current;
        if (!ImGui.SliderFloat(label, ref value, min, max, format))
            return;
        setter(value);
        config.Save();
    }

    private void DrawInt(string label, int current, int min, int max, Action<int> setter)
    {
        var value = current;
        if (!ImGui.SliderInt(label, ref value, min, max))
            return;
        setter(value);
        config.Save();
    }

    private void DrawPreferredMount()
    {
        var resolution = mountCatalog.Resolve(config.PreferredMountId, config.PreferredMountName);
        var label = resolution.IsAvailable
            ? resolution.Name
            : $"{config.PreferredMountName} (unavailable)";
        if (ImGui.BeginCombo("Preferred Mount", label))
        {
            foreach (var mount in mountCatalog.GetUnlockedMounts())
            {
                var selected = resolution.IsAvailable && resolution.RowId == mount.RowId;
                if (ImGui.Selectable($"{mount.Name}##mount-{mount.RowId}", selected))
                {
                    config.PreferredMountId = mount.RowId;
                    config.PreferredMountName = mount.Name;
                    config.Save();
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (resolution.IsAvailable)
            ImGui.TextDisabled($"Resolved from current game data: {resolution.Name} (Mount row {resolution.RowId}).");
        else
            ImGui.TextWrapped(resolution.Explanation);
        if (!string.IsNullOrEmpty(mountCatalog.LastCatalogError))
            ImGui.TextWrapped(mountCatalog.LastCatalogError);
        if (ImGui.SmallButton("Refresh unlocked mounts"))
            mountCatalog.GetUnlockedMounts(true);
        ImGui.TextDisabled("No Mount Roulette fallback is used if the preferred mount is unavailable.");
    }

    private void DrawCombatProvider()
    {
        var label = config.CombatProvider switch
        {
            CombatProvider.ExternalAcr => "External Combat / ACR",
            CombatProvider.RotationSolverReborn => "RotationSolverReborn (External)",
            CombatProvider.NativePvPSentinel => "Native PvPSentinel (experimental)",
            _ => "Off",
        };

        if (ImGui.BeginCombo("Combat provider", label))
        {
            SelectProvider(CombatProvider.Off, "Off");
            SelectProvider(CombatProvider.ExternalAcr, "External Combat / ACR");
            SelectProvider(CombatProvider.RotationSolverReborn, "RotationSolverReborn (External)");
            SelectProvider(CombatProvider.NativePvPSentinel, "Native PvPSentinel (experimental)");
            ImGui.EndCombo();
        }

        if (config.CombatProvider == CombatProvider.RotationSolverReborn)
        {
            var status = rebornStatus();
            var color = status.Loaded && status.IpcAvailable
                ? new Vector4(0.35f, 0.9f, 0.55f, 1f)
                : new Vector4(1f, 0.55f, 0.25f, 1f);
            ImGui.TextColored(color,
                $"Reborn: {(status.Loaded ? "loaded" : "not loaded")}, mode {(status.AutorotationActive ? "active" : "Off")}, {status.Version}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Loaded and connected permits supervised travel even when RSR reports Off. Combat action execution cannot be verified through this IPC; see Development.");
        }
        else if (config.CombatProvider == CombatProvider.NativePvPSentinel)
            DrawNativeCombatMode();
    }

    private void SelectProvider(CombatProvider provider, string label)
    {
        var selected = config.CombatProvider == provider;
        if (ImGui.Selectable(label, selected))
        {
            config.CombatProvider = provider;
            config.Save();
        }
        if (selected)
            ImGui.SetItemDefaultFocus();
    }

    private void DrawNativeCombatMode()
    {
        var label = config.NativeCombatMode == NativeCombatMode.Active
            ? "Active (experimental)"
            : "Shadow / Observe (safe default)";
        if (!ImGui.BeginCombo("Native development mode", label))
            return;

        SelectNativeMode(NativeCombatMode.ShadowObserve, "Shadow / Observe (safe default)");
        SelectNativeMode(NativeCombatMode.Active, "Active (experimental)");
        ImGui.EndCombo();
    }

    private void SelectNativeMode(NativeCombatMode mode, string label)
    {
        var selected = config.NativeCombatMode == mode;
        if (ImGui.Selectable(label, selected))
        {
            config.NativeCombatMode = mode;
            config.Save();
        }
        if (selected)
            ImGui.SetItemDefaultFocus();
    }

    private static bool BeginSection(string title, bool defaultOpen = false) =>
        ImGui.CollapsingHeader(title, defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);

    private static string YesNo(bool value) => value ? "yes" : "no";
}
