using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using PvPSentinel.Integrations;
using PvPSentinel.Models;
using PvPSentinel.Navigation;
using System.Numerics;

namespace PvPSentinel.UI;

internal sealed class DiagnosticWindow : Window
{
    private readonly Func<TacticalSnapshot> snapshot;
    private readonly IVNavmeshAdapter vnav;
    private readonly WrathAdapter wrath;
    private readonly Func<string> lastAction;

    public DiagnosticWindow(
        Func<TacticalSnapshot> snapshot,
        IVNavmeshAdapter vnav,
        WrathAdapter wrath,
        Func<string> lastAction)
        : base("PvP Sentinel - Development###PvPSentinelDiagnostics")
    {
        this.snapshot = snapshot;
        this.vnav = vnav;
        this.wrath = wrath;
        this.lastAction = lastAction;
        Size = new Vector2(570, 690);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var state = snapshot();
        var game = state.Game;
        var local = game.LocalPlayer;

        KeyValue("PvP", YesNo(game.IsPvP));
        KeyValue("Bound by duty", YesNo(game.IsBoundByDuty));
        KeyValue("Mode", game.IsFrontline ? "Frontline" : "Unsupported / none");
        KeyValue("Map", $"{game.MapName} ({game.TerritoryId}/{game.MapId})");
        KeyValue("Job", local is null ? "Unavailable" : $"{local.JobAbbreviation} ({local.JobId})");
        if (!string.IsNullOrEmpty(game.ReadError))
            ColoredText(new Vector4(1f, 0.45f, 0.3f, 1f), game.ReadError);

        Section("Behavior");
        ColoredText(BehaviorColor(state.Behavior), state.Behavior.ToString().ToUpperInvariant());
        ImGui.TextWrapped(state.DecisionReason);
        KeyValue("Committed for", $"{(game.CapturedAtUtc - state.BehaviorSinceUtc).TotalSeconds:F1}s");

        Section("Main Friendly Cluster");
        if (state.MainCluster is null)
        {
            ImGui.TextDisabled("No reliable cluster");
        }
        else
        {
            KeyValue("Players", state.MainCluster.PlayerCount.ToString());
            KeyValue("Distance", local is null ? "n/a" : $"{HorizontalDistance(local.Position, state.MainCluster.Center):F1}y");
            KeyValue("Confidence", state.MainCluster.Confidence.ToString("F2"));
            KeyValue("Density", state.MainCluster.Density.ToString("F2"));
            KeyValue("Center", FormatVector(state.MainCluster.Center));
            KeyValue("Movement trend", FormatVector(state.MainCluster.MovementTrend));
        }

        if (ImGui.TreeNode($"All friendly clusters ({state.Clusters.Count})"))
        {
            foreach (var cluster in state.Clusters)
                ImGui.BulletText($"#{cluster.Id}: {cluster.PlayerCount} players, confidence {cluster.Confidence:F2}, center {FormatVector(cluster.Center)}");
            ImGui.TreePop();
        }

        Section("Nearby");
        KeyValue("Friendlies / enemies within 20y", $"{state.Friendly20} / {state.Enemy20}");
        KeyValue("Friendlies / enemies within 40y", $"{state.Friendly40} / {state.Enemy40}");

        Section("Current Target");
        if (state.Target is null)
        {
            ImGui.TextDisabled("None");
        }
        else
        {
            var target = state.Target.Target;
            KeyValue("Target", $"{target.JobAbbreviation} - {target.Name}");
            KeyValue("HP / shield", $"{target.HpPercent:F1}% / {target.ShieldPercent}%");
            KeyValue("Distance", local is null ? "n/a" : $"{HorizontalDistance(local.Position, target.Position):F1}y");
            KeyValue("Battle High", target.BattleHigh);
            KeyValue("Target score", state.Target.Score.ToString("F1"));
            KeyValue("Finish opportunity", YesNo(state.Target.IsFinishOpportunity));
            ImGui.TextWrapped(state.Target.Explanation);
        }

        Section("Navigation");
        KeyValue("vnavmesh available", YesNo(vnav.IsReady));
        KeyValue("Pathing / pathfinding", $"{YesNo(vnav.IsPathRunning)} / {YesNo(vnav.IsPathfindInProgress)}");
        KeyValue("Destination", state.Navigation.Destination is { } destination ? FormatVector(destination) : "None");
        ImGui.TextWrapped(state.Navigation.Explanation);

        Section("Combat");
        KeyValue("MCH controller active", YesNo(state.Combat.ControllerActive));
        KeyValue("Last accepted action", lastAction());
        KeyValue("Next desired action", state.Combat.DesiredAction);
        ImGui.TextWrapped(state.Combat.Explanation);
        KeyValue("Wrath", wrath.Status);
    }

    private static void KeyValue(string key, string value)
    {
        ImGui.TextDisabled(key + ":");
        ImGui.SameLine();
        ImGui.TextWrapped(value);
    }

    private static void ColoredText(Vector4 color, string text) => ImGui.TextColored(color, text);
    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text(title);
    }
    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static string FormatVector(Vector3 value) => $"{value.X:F1}, {value.Y:F1}, {value.Z:F1}";
    private static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    private static Vector4 BehaviorColor(BehaviorState state) => state switch
    {
        BehaviorState.Disabled or BehaviorState.Idle => new Vector4(0.7f, 0.7f, 0.7f, 1f),
        BehaviorState.Paused or BehaviorState.Dead => new Vector4(1f, 0.4f, 0.3f, 1f),
        BehaviorState.Retreat => new Vector4(1f, 0.65f, 0.2f, 1f),
        BehaviorState.FinishKill => new Vector4(1f, 0.3f, 0.55f, 1f),
        BehaviorState.Engage => new Vector4(1f, 0.8f, 0.2f, 1f),
        _ => new Vector4(0.35f, 0.85f, 0.55f, 1f),
    };
}
