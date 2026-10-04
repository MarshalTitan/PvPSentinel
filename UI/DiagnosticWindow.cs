using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Utility;
using SentinelCore.UI;
using PvPSentinel.Integrations;
using PvPSentinel.FrontlineCore;
using PvPSentinel.Models;
using PvPSentinel.Navigation;
using PvPSentinel.Strategy;
using System.Numerics;

namespace PvPSentinel.UI;

internal sealed class DiagnosticWindow : Window
{
    private readonly Configuration config;
    private readonly SentinelModernStyleScope modernStyle = new();
    private readonly Func<TacticalSnapshot> snapshot;
    private readonly IVNavmeshAdapter vnav;
    private readonly NavigationController navigation;
    private readonly BattlefieldService battlefieldService;
    private readonly WrathAdapter wrath;
    private readonly Func<string> lastAction;
    private readonly Func<string> groupPilotStatus;
    private readonly Func<FrontlinePilotReadiness?> pilotReadiness;
    private readonly Action stopNavigation;
    private readonly Action closed;

    public DiagnosticWindow(
        Configuration config,
        Func<TacticalSnapshot> snapshot,
        IVNavmeshAdapter vnav,
        NavigationController navigation,
        BattlefieldService battlefieldService,
        WrathAdapter wrath,
        Func<string> lastAction,
        Func<string> groupPilotStatus,
        Func<FrontlinePilotReadiness?> pilotReadiness,
        Action stopNavigation,
        Action closed)
        : base("PvP Sentinel - Development###PvPSentinelDiagnostics")
    {
        this.config = config;
        this.snapshot = snapshot;
        this.vnav = vnav;
        this.navigation = navigation;
        this.battlefieldService = battlefieldService;
        this.wrath = wrath;
        this.lastAction = lastAction;
        this.groupPilotStatus = groupPilotStatus;
        this.pilotReadiness = pilotReadiness;
        this.stopNavigation = stopNavigation;
        this.closed = closed;
        Size = new Vector2(680, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnClose() => closed();

    public override void PreDraw()
    {
        if (config.ConfigurationTheme == (int)SentinelThemeKind.Modern)
            modernStyle.Push(ImGuiHelpers.GlobalScale);
    }

    public override void PostDraw() => modernStyle.Pop();

    public override void Draw()
    {
        if (config.ConfigurationTheme == (int)SentinelThemeKind.Modern)
            SentinelModernAmbient.DrawRings(ImGuiHelpers.GlobalScale, 0.45f);
        var state = snapshot();
        var game = state.Game;
        var local = game.LocalPlayer;

        if (BeginSection("Testing Controls", true))
            DrawTestingControls(state);

        if (BeginSection("Frontline / Team Classification"))
        {
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
        KeyValue("Local PvP team", game.TeamStatus.UsesBattalionPvPTeam ? game.TeamStatus.LocalPvPTeam.ToString() : "UNRESOLVED");
        KeyValue("Raw local Battalion", local is null ? "UNAVAILABLE" : local.PvPTeam.ToString());
        KeyValue("Local StatusFlags", local is null ? "UNAVAILABLE" : $"{local.StatusFlags} (0x{(uint)local.StatusFlags:X8})");
        KeyValue("Battalion PvP-team classification", YesNo(game.TeamStatus.UsesBattalionPvPTeam));
        KeyValue("Alliance roster", YesNo(game.TeamStatus.IsAlliance));
        KeyValue("Declared / resolved members", $"{game.TeamStatus.DeclaredMemberCount} / {game.TeamStatus.ResolvedMemberCount}");
        KeyValue("Classification reliable", YesNo(game.IsClassificationReliable));
        ImGui.TextWrapped(game.ClassificationReliabilityExplanation);
        }

        if (BeginSection("Battlefield / Sensors"))
        {
            DrawBattlefield(state);

        Subheading("Behavior");
        ColoredText(BehaviorColor(state.Behavior), state.Behavior.ToString().ToUpperInvariant());
        ImGui.TextWrapped(state.DecisionReason);
        KeyValue("Committed for", $"{(game.CapturedAtUtc - state.BehaviorSinceUtc).TotalSeconds:F1}s");

        Subheading("Main Friendly Cluster");
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

        Subheading("Nearby");
        KeyValue("Friendly / enemy / unknown within 20y", $"{state.Friendly20} / {state.Enemy20} / {state.Unknown20}");
        KeyValue("Friendly / enemy / unknown within 40y", $"{state.Friendly40} / {state.Enemy40} / {state.Unknown40}");
        }

        if (BeginSection("Threat"))
        {
        KeyValue("Threat observation reliable", YesNo(state.Threat.IsReliable));
        KeyValue("Threat observation source", state.Threat.Source.ToString());
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
        }

        if (BeginSection("Objectives / Map Research"))
        {
        Subheading("Strategic Target Recommendation");
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

        Subheading("Map Strategy / Objectives");
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
        DrawObjectiveResearch(state);
        }

        if (BeginSection("M2 Navigation", true))
        {
        DrawManualNavigationDiagnostics(state);

        Subheading("Navigation (legacy/autonomous disabled)");
        KeyValue("vnavmesh available", YesNo(vnav.IsReady));
        KeyValue("Mesh build progress", vnav.BuildProgress < 0f ? "No build in progress / UNRESOLVED" : $"{vnav.BuildProgress:P0}");
        KeyValue("Pathing / pathfinding", $"{YesNo(vnav.IsPathRunning)} / {YesNo(vnav.IsPathfindInProgress)}");
        KeyValue("Owned path state", state.Navigation.PathState.ToString());
        KeyValue("Waypoints / failures", $"{state.Navigation.WaypointCount} / {state.Navigation.ConsecutiveFailures}");
        KeyValue("Mount state", state.Navigation.MountState.ToString());
        KeyValue("Destination", state.Navigation.Destination is { } destination ? FormatVector(destination) : "None");
        ImGui.TextWrapped(state.Navigation.Explanation);
        }

        if (BeginSection("Combat"))
        {
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
            KeyValue("Nearby enemies / targeting me", $"{external.NearbyEnemies} / {external.TargetingPlayer}");
            KeyValue("Limit Break ready / unmanaged", YesNo(external.LimitBreakReadyUnmanaged));
            ImGui.TextWrapped(external.Status);
            ImGui.TextDisabled("Reborn action events announce decisions only; they do not confirm that an action executed.");
        }
        KeyValue("Wrath", wrath.Status);
        }

        if (BeginSection("Frontline Lifecycle"))
        {
        KeyValue("Queue state", state.Queue.State.ToString());
        KeyValue("Detected daily campaign", state.Queue.DailyCampaign.DisplayName());
        KeyValue("Completed / limit", $"{state.Queue.CompletedMatches} / {state.Queue.MatchLimit}");
        KeyValue("Emergency stop latched", YesNo(state.Queue.EmergencyStopLatched));
        ImGui.TextWrapped(state.Queue.Explanation);
        }
    }

    private void DrawBattlefield(TacticalSnapshot state)
    {
        var battlefield = state.Battlefield;
        KeyValue("Adapter", battlefield.ActiveAdapter);
        KeyValue("Map", $"{battlefield.Map.DisplayName()} ({battlefield.TerritoryId})");
        KeyValue("Lifecycle / timer", $"{battlefield.Match.Lifecycle} / {(battlefield.Match.TimeRemaining is { } timer ? timer.ToString("mm\\:ss") : "UNRESOLVED")}");
        KeyValue("DutyStarted", YesNo(battlefield.Match.DutyStarted));
        KeyValue("FrontlineHeader visible", YesNo(battlefield.Match.HeaderVisible));
        KeyValue("Active corroboration", battlefield.Match.ActiveCorroboration);
        ImGui.TextWrapped(battlefield.Match.Evidence);
        KeyValue("Results terminal", YesNo(battlefield.Match.ResultsDetected));
        KeyValue("Team scores", battlefield.Match.TeamScores);
        KeyValue("Local PvP team", state.Game.TeamStatus.UsesBattalionPvPTeam ? battlefield.LocalPvPTeam.ToString() : "UNRESOLVED");
        KeyValue("SELF / ALLY / ENEMY / UNKNOWN",
            $"{battlefield.Counts.Self} / {battlefield.Counts.Allies} / {battlefield.Counts.Enemies} / {battlefield.Counts.Unknown}");
        KeyValue("Allied / enemy clusters", $"{battlefield.AlliedClusters.Count} / {battlefield.EnemyClusters.Count}");
        KeyValue("Combat context", $"{battlefield.Combat.Context} (blocks movement: {YesNo(battlefield.Combat.BlocksMovement)})");
        KeyValue("Death / respawn", $"{battlefield.DeathRespawn.State}; {battlefield.DeathRespawn.Deaths} / {battlefield.DeathRespawn.Respawns}");
        KeyValue("M2 log directory", battlefieldService.CurrentLogDirectory);
        ImGui.TextWrapped(battlefield.Explanation);

        if (ImGui.TreeNode($"Sensor health ({battlefield.SensorHealth.Count})"))
        {
            foreach (var sensor in battlefield.SensorHealth)
                ImGui.BulletText($"{sensor.Sensor}: {sensor.Status}; errors={sensor.ErrorCount}; {sensor.Detail}");
            ImGui.TreePop();
        }
        if (battlefield.LastMatchSummary is { } summary && ImGui.TreeNode("Retained last-match summary"))
        {
            KeyValue("Map / territory / duty", $"{summary.PrimaryTestMap.DisplayName()} / {summary.TerritoryId} / {summary.ContentFinderConditionId}");
            KeyValue("Final state", summary.FinalMatchState.ToString());
            KeyValue("Peaks SELF / ALLY / ENEMY / UNKNOWN", $"{summary.PeakSelf} / {summary.PeakAllies} / {summary.PeakEnemies} / {summary.PeakUnknown}");
            KeyValue("Objectives / deaths / respawns", $"{summary.ObjectiveCount} / {summary.Deaths} / {summary.Respawns}");
            KeyValue("Navigation requests / arrivals / failures", $"{summary.NavigationRequests} / {summary.NavigationArrivals} / {summary.NavigationFailures}");
            KeyValue("Path failures", summary.NavigationPathFailures.ToString());
            KeyValue("Stops / stuck / rejected routes", $"{summary.NavigationStops} / {summary.NavigationStuckEvents} / {summary.NavigationRouteRejections}");
            KeyValue("Research locations / objects / transitions", $"{summary.ObjectiveResearch.Count} / {summary.ResearchObjects.Count} / {summary.ObjectiveTransitions.Count}");
            KeyValue("Results / sensor errors", $"{YesNo(summary.ResultsDetected)} / {summary.SensorErrors}");
            ImGui.TreePop();
        }
    }

    private void DrawTestingControls(TacticalSnapshot state)
    {
        var manual = navigation.ManualSnapshot;
        KeyValue("Current Frontline", state.Battlefield.Map.DisplayName());
        KeyValue("Active adapter", state.Battlefield.ActiveAdapter);
        if (ImGui.Button(manual.Armed ? "Disarm Manual Navigation" : "Arm Manual Navigation", new Vector2(210, 34)))
        {
            if (manual.Armed)
            {
                stopNavigation();
                navigation.SetManualNavigationArmed(false);
            }
            else
                navigation.SetManualNavigationArmed(true);
        }
        ImGui.SameLine();
        if (ImGui.Button("STOP NAVIGATION", new Vector2(180, 34)))
            stopNavigation();

        KeyValue("Selected destination", $"{manual.DestinationId} — {manual.DestinationName}");
        KeyValue("Route state", manual.State.ToString());
        KeyValue("Manual navigation armed", YesNo(manual.Armed));
        KeyValue("Map policy / dynamic follower", groupPilotStatus());
        if (pilotReadiness() is { } readiness)
        {
            ImGui.TextUnformatted($"Current pilot {(readiness.CanTravel ? "READY" : "BLOCKED")}");
            foreach (var line in readiness.Lines)
                ImGui.BulletText(line);
        }

        var objectives = state.Battlefield.Objectives.ToArray();
        if (objectives.Length > 0)
        {
            ImGui.Text(state.Battlefield.Map switch
            {
                FrontlineMap.BorderlandRuins => "Secure destinations (SEC-xx is session-scoped; coordinates are authoritative):",
                FrontlineMap.FieldsOfGlory => "Shatter objectives (UNRESOLVED buttons remain disabled):",
                FrontlineMap.SealRock => "Seal Rock destinations (SR-xx):",
                FrontlineMap.OnsalHakair => "Onsal stable discovery destinations (ONS-xx; coordinates are authoritative):",
                FrontlineMap.WorqorChirteh => "Worqor stable discovery destinations (WOR-xx; coordinates are authoritative):",
                _ => "Discovered objective destinations:",
            });
            ImGui.BeginDisabled(!manual.Armed);
            for (var index = 0; index < objectives.Length; index++)
            {
                var objective = objectives[index];
                ImGui.BeginDisabled(objective.ReferencePosition is null);
                var buttonLabel = UsesSessionScopedDiscoveryLabels(state.Battlefield.Map) &&
                                  objective.ReferencePosition is { } discoveryPosition
                    ? $"{objective.LogicalId} [{discoveryPosition.X:F0},{discoveryPosition.Z:F0}]"
                    : objective.LogicalId;
                if (ImGui.Button($"{buttonLabel}##m2-{objective.LogicalId}"))
                {
                    var shatterIce = state.Battlefield.Map == FrontlineMap.FieldsOfGlory;
                    navigation.RequestManualDestination(
                        objective.LogicalId,
                        objective.DisplayName,
                        objective.ReferencePosition!.Value,
                        shatterIce
                            ? ShatterApproachPolicy.Anchors(objective.ReferencePosition.Value,
                                state.Game.LocalPlayer?.Position ?? objective.ReferencePosition.Value, objective.Kind)
                            : objective.ValidatedApproachAnchors.Select(anchor => anchor.Position).ToArray(),
                        includeReferencePosition: !shatterIce,
                        minimumApproachClearance: shatterIce ? ShatterApproachPolicy.MinimumClearance(objective.Kind) : 0f);
                }
                ImGui.EndDisabled();
                if ((index + 1) % 6 != 0 && index + 1 < objectives.Length)
                    ImGui.SameLine();
            }
            ImGui.EndDisabled();
        }
        else
        {
            ImGui.TextDisabled(state.Battlefield.Map switch
            {
                FrontlineMap.BorderlandRuins => "Waiting for stable Secure marker coordinates to create SEC-xx destinations.",
                FrontlineMap.OnsalHakair => "Waiting for strongly stable Onsal evidence to promote ONS-xx destinations; raw observations remain research-only.",
                FrontlineMap.WorqorChirteh => "Waiting for strongly stable Worqor evidence to promote WOR-xx destinations; raw observations remain research-only.",
                _ => "No logical objective has a resolved reference position yet.",
            });
        }

        var nearestCluster = state.Battlefield.AlliedClusters
            .Where(cluster => cluster.MemberCount >= 2)
            .OrderBy(cluster => cluster.DistanceFromLocalPlayer)
            .FirstOrDefault();
        ImGui.BeginDisabled(!manual.Armed || nearestCluster is null);
        if (ImGui.Button("Nearest Allied Cluster", new Vector2(210, 0)) && nearestCluster is not null)
            navigation.RequestManualDestination(
                $"ALLY-CLUSTER-{nearestCluster.Id}",
                $"Allied cluster #{nearestCluster.Id}",
                nearestCluster.Centroid);
        ImGui.EndDisabled();
        if (!manual.Armed)
            ImGui.TextDisabled("Arm manual navigation before selecting any destination.");
    }

    private void DrawObjectiveResearch(TacticalSnapshot state)
    {
        var battlefield = state.Battlefield;
        var researchName = battlefield.Map switch
        {
            FrontlineMap.BorderlandRuins => "Secure Research",
            FrontlineMap.SealRock => "Seal Rock Research",
            FrontlineMap.FieldsOfGlory => "Shatter Research",
            FrontlineMap.OnsalHakair => "Onsal Research",
            FrontlineMap.WorqorChirteh => "Worqor Research",
            _ => "Map Research",
        };
        if (ImGui.TreeNode($"{researchName}: promoted logical locations ({battlefield.Objectives.Count})"))
        {
            foreach (var objective in battlefield.Objectives)
            {
                var position = objective.ReferencePosition is { } reference ? FormatVector(reference) : "UNRESOLVED";
                ImGui.BulletText($"{objective.LogicalId} {objective.Kind}: {objective.State}; rank={objective.Rank}; owner={objective.Owner}; GC={objective.ObservedGrandCompany}; nearby A/E={objective.NearbyAllies}/{objective.NearbyEnemies}; pos={position}; physical={YesNo(objective.PhysicalConfirmation is not null)}; source={objective.SensorSource}; confidence={objective.Confidence}");
                ImGui.Indent();
                ImGui.TextWrapped($"Evidence: {objective.Evidence}");
                ImGui.Unindent();
            }
            ImGui.TreePop();
        }
        if (ImGui.TreeNode($"{researchName}: physical research objects ({battlefield.ResearchObjects.Count})"))
        {
            foreach (var item in battlefield.ResearchObjects.Take(200))
            {
                ImGui.BulletText($"{item.ResearchId}: {item.ObjectKind} '{item.Name}', base={item.BaseId}, entity=0x{item.EntityId:X8}, object=0x{item.GameObjectId:X16}, observed={YesNo(item.CurrentlyObserved)}, targetable={YesNo(item.IsTargetable)}, hp={item.CurrentHp}/{item.MaxHp}, pos={FormatVector(item.Position)}");
            }
            ImGui.TreePop();
        }
        if (ImGui.TreeNode($"Research notes / unresolved ({battlefield.ResearchNotes.Count})"))
        {
            foreach (var note in battlefield.ResearchNotes)
                ImGui.BulletText(note);
            ImGui.TreePop();
        }
    }

    private void DrawManualNavigationDiagnostics(TacticalSnapshot state)
    {
        var manual = navigation.ManualSnapshot;
        KeyValue("Current Map", $"{state.Battlefield.Map.DisplayName()} ({state.Battlefield.TerritoryId})");
        KeyValue("Navmesh Ready", YesNo(vnav.IsReady));
        KeyValue("Build Progress", vnav.BuildProgress < 0f ? "No build / UNRESOLVED" : $"{vnav.BuildProgress:P0}");
        KeyValue("Destination", $"{manual.DestinationId} — {manual.DestinationName}");
        KeyValue("Movement Owner", manual.Owner.ToString());
        KeyValue("Combat Context", state.Battlefield.Combat.Context.ToString());
        KeyValue("Movement Blocked", YesNo(state.Battlefield.Combat.BlocksMovement));
        KeyValue("Route State", manual.State.ToString());
        KeyValue("Route / attempt", $"{manual.RouteId} / {manual.RouteAttempt}");
        KeyValue("Waypoint Count / route length", $"{manual.WaypointCount} / {manual.RouteLength:F1}y");
        KeyValue("Current / stage-end waypoint", $"{manual.CurrentWaypoint} / {manual.StageEndWaypoint}");
        KeyValue("Previous Waypoint", manual.PreviousWaypoint is { } previous ? FormatVector(previous) : "NONE");
        KeyValue("Current Waypoint Position", manual.CurrentWaypointPosition is { } current ? FormatVector(current) : "NONE");
        KeyValue("Next Waypoint", manual.NextWaypoint is { } next ? FormatVector(next) : "NONE");
        KeyValue("Distance to Next Waypoint",
            manual.NextWaypoint is { } nextPoint && state.Game.LocalPlayer is { } local
                ? $"{Vector3.Distance(local.Position, nextPoint):F1}y (3D)"
                : "UNRESOLVED");
        KeyValue("Distance Remaining", $"{manual.DistanceRemaining:F1}y");
        KeyValue("Progress Age", $"{manual.ProgressAgeSeconds:F1}s");
        KeyValue("Stuck Count", manual.StuckCount.ToString());
        KeyValue("Path Failures", manual.PathFailureCount.ToString());
        ImGui.TextWrapped(manual.Explanation);
    }

    private static void KeyValue(string key, string value)
    {
        ImGui.TextDisabled(key + ":");
        ImGui.SameLine();
        ImGui.TextWrapped(value);
    }

    private static void ColoredText(Vector4 color, string text) => ImGui.TextColored(color, text);
    private bool BeginSection(string title, bool defaultOpen = false)
    {
        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        return config.ConfigurationTheme == (int)SentinelThemeKind.Modern
            ? SentinelModernControls.CollapsingSection(title, flags)
            : ImGui.CollapsingHeader(title, flags);
    }
    private static void Subheading(string title)
    {
        ImGui.Spacing();
        ImGui.Text(title);
        ImGui.Separator();
    }
    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text(title);
    }
    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static bool UsesSessionScopedDiscoveryLabels(FrontlineMap map) =>
        map is FrontlineMap.BorderlandRuins or FrontlineMap.OnsalHakair or FrontlineMap.WorqorChirteh;
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
