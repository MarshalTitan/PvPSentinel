using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using PvPSentinel.Models;
using System.Numerics;

namespace PvPSentinel.UI;

internal sealed class TargetCounterWindow : Window
{
    private readonly Configuration config;
    private readonly Func<TacticalSnapshot> snapshot;
    private readonly IFontHandle numberFont;

    public TargetCounterWindow(
        Configuration config,
        Func<TacticalSnapshot> snapshot,
        IFontHandle numberFont)
        : base(
            "###PvPSentinelTargetCounter",
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBackground)
    {
        this.config = config;
        this.snapshot = snapshot;
        this.numberFont = numberFont;
        IsOpen = true;
        RespectCloseHotkey = false;
        Position = new Vector2(640, 320);
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    public override bool DrawConditions()
    {
        if (!config.TargetCounterEnabled)
            return false;

        var state = snapshot();
        if (config.TargetCounterOnlyInPvp && !state.Game.IsPvP)
            return false;
        if (!state.Threat.IsReliable)
            return false;
        return !config.TargetCounterHideAtZero || state.Threat.TargeterCount > 0;
    }

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse |
                ImGuiWindowFlags.NoBackground;
        if (config.TargetCounterLocked)
            Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs;
    }

    public override void Draw()
    {
        var threat = snapshot().Threat;
        // The ordinary Dalamud font is built near normal UI size. Enlarging it
        // to 64px+ magnifies its atlas texture and produces visibly soft digits.
        // This dedicated 128px digit atlas is always downsampled instead, keeping
        // the counter sharp across the full configurable size range.
        using (numberFont.Push())
            DrawCentered(threat.TargeterCount.ToString(), config.TargetCounterNumberSize);

        if (config.TargetCounterShowJobs && threat.Targeters.Count > 0)
        {
            ImGui.Spacing();
            DrawCentered(
                string.Join("  ", threat.Targeters.Select(targeter => targeter.JobAbbreviation)),
                config.TargetCounterJobSize);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Currently observed enemies whose hard target is you. This does not detect soft targets, queued attacks, or future intent.");
    }

    private static void DrawCentered(string text, float requestedSize)
    {
        var baseSize = Math.Max(1f, ImGui.GetFontSize());
        ImGui.SetWindowFontScale(Math.Clamp(requestedSize / baseSize, 0.5f, 8f));
        var width = ImGui.CalcTextSize(text).X;
        var available = ImGui.GetContentRegionAvail().X;
        if (available > width)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (available - width) / 2f);
        ImGui.TextUnformatted(text);
        ImGui.SetWindowFontScale(1f);
    }
}
