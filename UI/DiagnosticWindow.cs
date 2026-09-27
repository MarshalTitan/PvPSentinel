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
    private readonly Action closed;

    public DiagnosticWindow(
        Func<TacticalSnapshot> snapshot,
        IVNavmeshAdapter vnav,
        WrathAdapter wrath,
        Func<string> lastAction,
        Action closed)
        : base("PvP Sentinel - Development###PvPSentinelDiagnostics")
    {
        this.snapshot = snapshot;
        this.vnav = vnav;
        this.wrath = wrath;
        this.lastAction = lastAction;
        this.closed = closed;
        Size = new Vector2(680, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnClose() => closed();

    public override void Draw()
    {
        var state = snapshot();
        var game = state.Game;
        var local = game.LocalPlayer;

        KeyValue("PvP", YesNo(game.IsPvP));
        KeyValue("Bound by duty", YesNo(game.IsBoundByDuty));
        KeyValue("Mode", game.IsFrontline ? "Frontline" : "Unsupported / none");
        KeyValue("Map", $"{game.MapName} ({game.TerritoryId}/{game.MapId})");
        KeyValue("Frontline campaign", $"{game.FrontlineMap.DisplayName()} (CF {game.ContentFinderConditionId})");
        KeyValue("Job", local is null ? "Unavailable" : $"{local.JobAbbreviation} ({local.JobId})");
        KeyValue("Combat / casting / queued action", $"{YesNo(game.IsInCombat)} / {YesNo(game.IsCasting)} / {YesNo(game.IsActionQueued)}");
        KeyValue("Animation lock / mounted", $"{game.AnimationLockSeconds:F2}s / {YesNo(game.IsMounted)}");
        if (!string.IsNullOrEmpty(game.ReadError))
            ColoredText(new Vector4(1f, 0.45f, 0.3f, 1f), game.ReadError);

        Section("Frontline Classification");
        KeyValue("Alliance roster", YesNo(game.TeamStatus.IsAlliance));
        KeyValue("Declared / resolved members", $"{game.TeamStatus.DeclaredMemberCount} / {game.TeamStatus.ResolvedMemberCount}");
        KeyValue("Non-member rule available", YesNo(game.TeamStatus.CanClassifyNonMembers));
        KeyValue("Classification reliable", YesNo(game.IsClassificationReliable));
        ImGui.TextWrapped(game.ClassificationReliabilityExplanation);

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
        KeyValue("Friendly / enemy / unknown within 20y", $"{state.Friendly20} / {state.Enemy20} / {state.Unknown20}");
        KeyValue("Friendly / enemy / unknown within 40y", $"{state.Friendly40} / {state.Enemy40} / {state.Unknown40}");

        Section("Observed Threat");
        KeyValue("Threat observation reliable", YesNo(state.Threat.IsReliable));
        KeyValue("Targeting me", state.Threat.TargeterCount.ToString());
        KeyValue("Combat-relevant targeters (within 30y)", state.Threat.CombatRelevantTargeterCount.ToString());
        KeyValue("Nearby enemies / allies (within 18y)", $"{state.Threat.NearbyEnemyCount} / {state.Threat.NearbyFriendlyCount}");
        KeyValue("Threat level", state.Threat.Level.ToString());
        ImGui.TextWrapped(state.Threat.Explanation);
        ImGui.TextDisabled("Hard-target observations only; soft targeting, queued attacks, and future intent are not observable.");
        if (ImGui.TreeNode($"Enemies currently targeting me ({state.Threat.TargeterCount})"))
        {
            foreach (var targeter in state.Threat.Targeters)
                ImGui.BulletText($"{targeter.JobAbbreviation} - {targeter.Name}, {targeter.Distance:F1}y");
            ImGui.TreePop();
        }

        var nearbyPlayers = local is null
            ? []
            : game.ObservedPlayers
                .Select(player => (Player: player, Distance: HorizontalDistance(local.Position, player.Position)))
                .Where(item => item.Distance <= 40f)
                .OrderBy(item => item.Distance)
                .ToArray();
        if (ImGui.TreeNode($"Nearby PCs within 40y ({nearbyPlayers.Length})"))
        {
            foreach (var item in nearbyPlayers)
            {
                var player = item.Player;
                if (!ImGui.TreeNode($"{player.Classification}: {player.JobAbbreviation}, {item.Distance:F1}y###pc-{player.GameObjectId:X}"))
                    continue;

                KeyValue("Entity / object ID", $"0x{player.EntityId:X8} / 0x{player.GameObjectId:X16}");
                KeyValue("Job", $"{player.JobAbbreviation} ({player.JobId})");
                KeyValue("Final classification", player.Classification.ToString());
                KeyValue("StatusFlags", $"{player.StatusFlags} (0x{(uint)player.StatusFlags:X8})");
                KeyValue("PartyMember flag", YesNo(player.PartyMemberFlag));
                KeyValue("AllianceMember flag", YesNo(player.AllianceMemberFlag));
                KeyValue("Hostile flag", YesNo(player.HostileFlag));
                KeyValue("Roster member", YesNo(player.IsRosterMember));
                KeyValue("Targetable", YesNo(player.IsTargetable));
                KeyValue("Distance", $"{item.Distance:F1}y");
                ImGui.TreePop();
            }
            ImGui.TreePop();
        }

        Section("Strategic Target Recommendation");
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

        Section("Map Strategy / Objectives");
        if (state.Objective is null)
        {
            ImGui.TextDisabled("No recognized objective candidate. Research observations remain available below.");
        }
        else
        {
            KeyValue("Strategy", state.Objective.Strategy);
            KeyValue("Objective", $"{state.Objective.Objective.Name} (base {state.Objective.Objective.BaseId})");
            KeyValue("Score / confidence", $"{state.Objective.Score:F1} / {state.Objective.Confidence:F2}");
            KeyValue("Actionable", YesNo(state.Objective.IsActionable));
            KeyValue("Position", FormatVector(state.Objective.Objective.Position));
            ImGui.TextWrapped(state.Objective.Explanation);
        }

        if (ImGui.TreeNode($"Objective research objects ({game.ObjectiveObservations.Count})"))
        {
            foreach (var observation in game.ObjectiveObservations.OrderBy(item => local is null ? 0f : HorizontalDistance(local.Position, item.Position)).Take(100))
            {
                var distance = local is null ? 0f : HorizontalDistance(local.Position, observation.Position);
                ImGui.BulletText($"{observation.ObjectKind} '{observation.Name}' base={observation.BaseId} targetable={observation.IsTargetable} hp={observation.CurrentHp}/{observation.MaxHp} distance={distance:F1}y pos={FormatVector(observation.Position)}");
            }
            ImGui.TreePop();
        }

        Section("Navigation");
        KeyValue("vnavmesh available", YesNo(vnav.IsReady));
        KeyValue("Pathing / pathfinding", $"{YesNo(vnav.IsPathRunning)} / {YesNo(vnav.IsPathfindInProgress)}");
        KeyValue("Owned path state", state.Navigation.PathState.ToString());
        KeyValue("Waypoints / failures", $"{state.Navigation.WaypointCount} / {state.Navigation.ConsecutiveFailures}");
        KeyValue("Mount state", state.Navigation.MountState.ToString());
        KeyValue("Destination", state.Navigation.Destination is { } destination ? FormatVector(destination) : "None");
        ImGui.TextWrapped(state.Navigation.Explanation);

        Section("Combat");
        KeyValue("Provider", state.Combat.Provider.ToString());
        KeyValue("Controller active", YesNo(state.Combat.ControllerActive));
        KeyValue("Yield owned navigation", YesNo(state.Combat.YieldNavigation));
        KeyValue("Last accepted action", lastAction());
        KeyValue("Next desired action", state.Combat.DesiredAction);
        ImGui.TextWrapped(state.Combat.Explanation);
        if (state.Combat.Native is { } native)
        {
            KeyValue("Native mode / state", $"{native.Mode} / {native.CombatState}");
            KeyValue("Native selected target", native.SelectedTarget);
            KeyValue("Target score / competitor", $"{native.SelectedTargetScore:F1} / {native.CompetingTarget}");
            KeyValue("Target switch reason", native.TargetSwitchReason);
            KeyValue("Player HP / MP", native.PlayerResources);
            KeyValue("Target HP / Guard / allied focus", $"{native.TargetHealth} / {YesNo(native.TargetGuarding)} / {native.AlliedFocus}");
            KeyValue("Analysis / tool state", native.ToolState);
            KeyValue("Wildfire state", native.WildfireState);
            KeyValue("Overheated", YesNo(native.Overheated));
            KeyValue("Limit gauge", native.LimitBreakState);
            KeyValue("Defense state", native.DefenseState);
            KeyValue("Offense suppression", native.OffenseSuppression);
            KeyValue("Action resolution", native.ActionResolution);
            if (ImGui.TreeNode($"Important native rejections ({native.Rejections.Count})"))
            {
                foreach (var rejection in native.Rejections)
                    ImGui.BulletText(rejection);
                ImGui.TreePop();
            }
        }
        if (state.Combat.External is { } external)
        {
            KeyValue("External provider", external.ProviderName);
            KeyValue("External engagement state", external.EngagementState);
            KeyValue("Installed / loaded / version", $"{YesNo(external.Installed)} / {YesNo(external.Loaded)} / {external.Version}");
            KeyValue("Status IPC / autorotation active", $"{YesNo(external.IpcAvailable)} / {YesNo(external.AutorotationActive)}");
            KeyValue("Reborn next action", $"{external.NextAction} ({external.NextActionId})");
            KeyValue("Reborn next GCD", $"{external.NextGcdAction} ({external.NextGcdActionId})");
            KeyValue("Enemies in clearance radius", external.NearbyEnemies.ToString());
            KeyValue("Limit Break ready / unmanaged", YesNo(external.LimitBreakReadyUnmanaged));
            ImGui.TextWrapped(external.Status);
            ImGui.TextDisabled("Reborn action events announce decisions only; they do not confirm that an action executed.");
        }
        KeyValue("Wrath", wrath.Status);

        Section("Frontline Lifecycle");
        KeyValue("Queue state", state.Queue.State.ToString());
        KeyValue("Detected daily campaign", state.Queue.DailyCampaign.DisplayName());
        KeyValue("Completed / limit", $"{state.Queue.CompletedMatches} / {state.Queue.MatchLimit}");
        KeyValue("Emergency stop latched", YesNo(state.Queue.EmergencyStopLatched));
        ImGui.TextWrapped(state.Queue.Explanation);
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
