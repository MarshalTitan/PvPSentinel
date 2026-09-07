using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace PvPSentinel.UI;

internal sealed class ConfigurationWindow : Window
{
    private readonly Configuration config;

    public ConfigurationWindow(Configuration config)
        : base("PvP Sentinel - Configuration###PvPSentinelConfiguration")
    {
        this.config = config;
        Size = new Vector2(520, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.TextWrapped("Development controls. The master switch and combat execution are off by default. PvPSentinel also refuses to automate outside a recognized Frontline territory.");
        ImGui.Separator();

        DrawCheckbox("Enable PvPSentinel", config.Enabled, value => config.Enabled = value);
        ImGui.Indent();
        DrawCheckbox("Enable navigation", config.NavigationEnabled, value => config.NavigationEnabled = value);
        DrawCheckbox("Enable combat execution", config.CombatEnabled, value => config.CombatEnabled = value);
        DrawCheckbox("Enable target selection", config.TargetSelectionEnabled, value => config.TargetSelectionEnabled = value);
        DrawCheckbox("Prioritize safe finish-KO opportunities", config.FinishKoPriorityEnabled, value => config.FinishKoPriorityEnabled = value);
        ImGui.Unindent();

        ImGui.Spacing();
        DrawCheckbox("Show diagnostic window", config.ShowDiagnostics, value => config.ShowDiagnostics = value);
        DrawCheckbox("Verbose logging", config.VerboseLogging, value => config.VerboseLogging = value);

        Section("Movement and clustering");
        DrawFloat("Friendly cluster link radius", config.FriendlyClusterLinkRadius, 6f, 30f, value => config.FriendlyClusterLinkRadius = value, "%.1f y");
        DrawFloat("Main-group follow radius", config.MainGroupFollowRadius, 8f, 40f, value => config.MainGroupFollowRadius = value, "%.1f y");
        DrawFloat("Regroup distance", config.MainGroupRegroupDistance, 20f, 80f, value => config.MainGroupRegroupDistance = value, "%.1f y");
        DrawFloat("MCH ranged position offset", config.RangedPositionOffset, 4f, 30f, value => config.RangedPositionOffset = value, "%.1f y");
        DrawFloat("Destination switch threshold", config.DestinationSwitchDistance, 3f, 25f, value => config.DestinationSwitchDistance = value, "%.1f y");
        DrawFloat("Minimum destination commitment", config.MinimumDestinationCommitmentSeconds, 1f, 15f, value => config.MinimumDestinationCommitmentSeconds = value, "%.1f s");

        Section("Targets");
        DrawFloat("Enemy engagement radius", config.EnemyEngagementRadius, 10f, 40f, value => config.EnemyEngagementRadius = value, "%.1f y");
        DrawFloat("Finish target HP threshold", config.FinishTargetHpPercent, 5f, 50f, value => config.FinishTargetHpPercent = value, "%.0f %%");
        DrawFloat("Finish target max chase", config.FinishTargetMaxChaseDistance, 10f, 40f, value => config.FinishTargetMaxChaseDistance = value, "%.1f y");
        DrawFloat("Target switch score advantage", config.TargetSwitchScoreAdvantage, 0f, 40f, value => config.TargetSwitchScoreAdvantage = value, "%.0f");
        DrawFloat("Minimum target commitment", config.MinimumTargetCommitmentSeconds, 0f, 8f, value => config.MinimumTargetCommitmentSeconds = value, "%.1f s");

        ImGui.Spacing();
        if (ImGui.Button("Save configuration"))
            config.Save();
        ImGui.SameLine();
        ImGui.TextDisabled("Changes are also saved as they are made.");
    }

    private void DrawCheckbox(string label, bool current, Action<bool> setter)
    {
        var value = current;
        if (!ImGui.Checkbox(label, ref value))
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

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text(title);
    }
}
