using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PvPSentinel.Behavior;
using PvPSentinel.Combat;
using PvPSentinel.Combat.Native;
using PvPSentinel.Combat.Native.Jobs.Machinist;
using PvPSentinel.Combat.Threat;
using PvPSentinel.Diagnostics;
using PvPSentinel.GameState;
using PvPSentinel.FrontlineCore;
using PvPSentinel.FrontlineCore.Diagnostics;
using PvPSentinel.Integrations;
using PvPSentinel.Intelligence;
using PvPSentinel.Models;
using PvPSentinel.Navigation;
using PvPSentinel.Queue;
using PvPSentinel.Strategy;
using PvPSentinel.UI;
using System.Numerics;

namespace PvPSentinel;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/pvpsentinel";

    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly WindowSystem windows = new("PvPSentinel");
    private readonly GameStateService gameState;
    private readonly DevelopmentLogger developmentLog;
    private readonly FriendlyClusterAnalyzer clusterAnalyzer;
    private readonly MainGroupTracker mainGroupTracker;
    private readonly FieldGroupTracker fieldGroupTracker = new();
    private readonly FrontlineDynamicFollowController dynamicFollow = new();
    private readonly TargetSelector targetSelector;
    private readonly ObjectiveStrategyService objectiveStrategy;
    private readonly WorqorGroupPilot worqorGroupPilot = new();
    private readonly SealRockGroupPilot sealRockGroupPilot = new();
    private readonly ShatterGroupPilot shatterGroupPilot = new();
    private readonly BehaviorEngine behaviorEngine = new();
    private readonly VNavmeshAdapter vnav;
    private readonly NavigationController navigation;
    private readonly CombatProviderCoordinator combat;
    private readonly RotationSolverRebornAdapter rotationSolverReborn;
    private readonly PvPThreatTracker threatTracker;
    private readonly QueueLifecycleController queueLifecycle;
    private readonly BattlefieldService battlefield;
    private readonly FrontlineEntryTrace entryTrace;
    private readonly WrathAdapter wrath = new();
    private readonly DiagnosticWindow diagnostics;
    private readonly ConfigurationWindow configurationWindow;

    private DateTime nextUpdateUtc = DateTime.MinValue;
    private BehaviorState lastLoggedBehavior = BehaviorState.Idle;
    private TacticalSnapshot current = EmptySnapshot();
    private FrontlinePilotReadiness? pilotReadiness;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IClientState clientState,
        ICondition condition,
        IObjectTable objectTable,
        IPartyList partyList,
        IDataManager dataManager,
        IUnlockState unlockState,
        ITargetManager targetManager,
        IGameGui gameGui,
        IDutyState dutyState,
        IFramework framework,
        IPluginLog pluginLog)
    {
        pi = pluginInterface;
        commands = commandManager;
        this.framework = framework;
        log = pluginLog;

        config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        config.Initialize(pi);
        if (config.AutonomousStrategyEnabled || config.ObjectiveNavigationEnabled || config.QueueAutomationEnabled)
        {
            config.AutonomousStrategyEnabled = false;
            config.ObjectiveNavigationEnabled = false;
            config.QueueAutomationEnabled = false;
            config.Save();
        }

        developmentLog = new DevelopmentLogger(log, () => config.VerboseLogging);
        clusterAnalyzer = new FriendlyClusterAnalyzer(developmentLog);
        mainGroupTracker = new MainGroupTracker(developmentLog);
        targetSelector = new TargetSelector(developmentLog);
        objectiveStrategy = new ObjectiveStrategyService(developmentLog);

        entryTrace = new FrontlineEntryTrace(pi.ConfigDirectory.FullName, log);
        gameState = new GameStateService(clientState, condition, objectTable, partyList, dataManager, log, developmentLog, entryTrace);
        vnav = new VNavmeshAdapter(pi, developmentLog);
        var mountCatalog = new PreferredMountCatalog(dataManager, unlockState);
        var mount = new NativeMountController(mountCatalog, developmentLog);
        navigation = new NavigationController(vnav, mount, developmentLog);
        var executor = new NativeActionExecutor(objectTable, targetManager, log);
        IPvpJobCombatModule[] jobModules = [new MachinistCombatModule()];
        var nativeCombat = new NativeCombatProvider(dataManager, executor, jobModules, developmentLog, log);
        rotationSolverReborn = new RotationSolverRebornAdapter(pi, dataManager, developmentLog);
        combat = new CombatProviderCoordinator(nativeCombat, rotationSolverReborn, developmentLog);
        threatTracker = new PvPThreatTracker(developmentLog);
        battlefield = new BattlefieldService(gameGui, dutyState, pi.ConfigDirectory.FullName, developmentLog, log, entryTrace);
        var queueAdapter = new FrontlineQueueAdapter(gameGui, dataManager, developmentLog);
        queueLifecycle = new QueueLifecycleController(queueAdapter, dutyState, developmentLog);

        diagnostics = new DiagnosticWindow(config, () => current, vnav, navigation, battlefield, wrath,
            () => combat.LastAction, () => $"Policy: {current.Game.FrontlineMap}; dynamic: {dynamicFollow.Status}; slot: {dynamicFollow.Slot}; destination: {dynamicFollow.Destination?.ToString() ?? "none"}; Worqor: {worqorGroupPilot.Status}; Seal Rock: {sealRockGroupPilot.Status}; Shatter: {shatterGroupPilot.Status}",
            () => pilotReadiness,
            StopNavigationFromUi, OnDiagnosticsClosed)
        {
            IsOpen = config.ShowDiagnostics,
        };
        configurationWindow = new ConfigurationWindow(
            pi,
            config,
            SetDiagnosticsVisibility,
            OnVerboseLoggingChanged,
            EmergencyStop,
            ClearEmergencyStop,
            queueLifecycle.ResetMatchCounter,
            () => queueLifecycle.EmergencyStopLatched,
            () => rotationSolverReborn.GetStatus(DateTime.UtcNow),
            () => pilotReadiness, () => current.Game.FrontlineMap,
            mountCatalog);
        windows.AddWindow(diagnostics);
        windows.AddWindow(configurationWindow);

        commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open PvP Sentinel development diagnostics. Use '/pvpsentinel config' for settings.",
        });

        framework.Update += OnFrameworkUpdate;
        pi.UiBuilder.Draw += DrawUi;
        pi.UiBuilder.OpenMainUi += OpenDiagnostics;
        pi.UiBuilder.OpenConfigUi += OpenConfiguration;
        log.Information("PvP Sentinel development build loaded. Master enable: {Enabled}; combat provider: {CombatProvider}; queue automation: {Queue}.",
            config.Enabled, config.CombatProvider, config.QueueAutomationEnabled);
    }

    public void Dispose()
    {
        navigation.StopOwnedMovement();
        rotationSolverReborn.Dispose();
        queueLifecycle.Dispose();
        framework.Update -= OnFrameworkUpdate;
        pi.UiBuilder.Draw -= DrawUi;
        pi.UiBuilder.OpenMainUi -= OpenDiagnostics;
        pi.UiBuilder.OpenConfigUi -= OpenConfiguration;
        commands.RemoveHandler(Command);
        windows.RemoveAllWindows();
        configurationWindow.Dispose();
    }

    private void OnCommand(string _, string arguments)
    {
        if (arguments.Trim().Equals("config", StringComparison.OrdinalIgnoreCase))
            configurationWindow.OpenAndExpand();
        else
            diagnostics.IsOpen = true;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (DateTime.UtcNow < nextUpdateUtc)
            return;
        nextUpdateUtc = DateTime.UtcNow.AddMilliseconds(250);

        var game = gameState.Capture();
        var threat = threatTracker.Evaluate(game);
        var battlefieldState = battlefield.Update(game, threat);
        var clusters = clusterAnalyzer.Analyze(game.Friendlies, config.FriendlyClusterLinkRadius, game.CapturedAtUtc);
        var trackedFieldGroup = fieldGroupTracker.Update(game.FrontlineMap,
            battlefieldState.Match.Lifecycle == FrontlineMatchLifecycle.MatchActive,
            battlefieldState.Match.Lifecycle == FrontlineMatchLifecycle.PreMatch,
            game.IsClassificationReliable, game.LocalPlayer?.Position,
            game.LocalPlayer?.IsDead == true, clusters, game.CapturedAtUtc);
        var mainCluster = mainGroupTracker.Select(clusters, game.CapturedAtUtc);
        var target = targetSelector.Select(game, mainCluster, config);
        var objective = objectiveStrategy.Select(game, config);
        var (behavior, behaviorSince, reason) = behaviorEngine.Evaluate(game, mainCluster, target, objective, config);
        var combatDecision = combat.Update(
            game,
            behavior,
            target,
            config,
            threat,
            navigation.IsMountTransitionPending(game.CapturedAtUtc));
        var reborn = rotationSolverReborn.GetStatus(game.CapturedAtUtc);
        var supportedMap = game.FrontlineMap is FrontlineMap.WorqorChirteh or FrontlineMap.SealRock or
            FrontlineMap.FieldsOfGlory or FrontlineMap.OnsalHakair or FrontlineMap.BorderlandRuins;
        var pilotEnabled = config.Enabled && config.NavigationEnabled && supportedMap;
        pilotReadiness = new FrontlinePilotReadiness(
            pilotEnabled, battlefieldState.Match.Lifecycle, game.IsClassificationReliable, vnav.IsReady,
            config.CombatProvider,
            config.CombatProvider == CombatProvider.RotationSolverReborn && reborn.Installed,
            config.CombatProvider == CombatProvider.RotationSolverReborn && reborn.Loaded,
            config.CombatProvider == CombatProvider.RotationSolverReborn && reborn.IpcAvailable,
            config.CombatProvider == CombatProvider.RotationSolverReborn && reborn.AutorotationActive);
        // External ACR currently has no trustworthy activity IPC, and Native
        // Shadow mode cannot own travel into combat. Both remain fail closed.
        developmentLog.Changed("pilot-readiness", string.Join('|', pilotReadiness.Lines),
            $"{game.FrontlineMap} pilot {(pilotReadiness.CanTravel ? "READY" : "BLOCKED")}: {string.Join("; ", pilotReadiness.Lines)}");

        if (!pilotReadiness.CanTravel && navigation.CurrentManualDestinationId is { } automated &&
            (dynamicFollow.Owns(game.FrontlineMap, automated) ||
             automated == worqorGroupPilot.CommittedDestinationId ||
             automated == sealRockGroupPilot.CommittedDestinationId ||
             automated == shatterGroupPilot.CommittedDestinationId))
            navigation.StopManualNavigation("Automatic route paused: lifecycle, team, mesh, or provider not ready.");

        var route = navigation.ManualSnapshot;
        var dynamicRoute = dynamicFollow.Owns(game.FrontlineMap, navigation.CurrentManualDestinationId) ||
            dynamicFollow.Owns(game.FrontlineMap, route.DestinationId);
        // The objective selectors should evaluate while the shared follower is
        // moving. Other routes, including user requests, keep their priority.
        var objectiveRoute = dynamicRoute
            ? route with { DestinationId = "NONE", State = ManualRouteState.Idle }
            : route;
        var worqorPlan = worqorGroupPilot.Update(game, battlefieldState, clusters, objectiveRoute,
            pilotEnabled && game.FrontlineMap == FrontlineMap.WorqorChirteh,
            pilotReadiness.CanTravel, trackedFieldGroup, allowGroupFallback: false);
        if (worqorGroupPilot.CancelDestinationId is { } unsafeWorqor &&
            navigation.CurrentManualDestinationId == unsafeWorqor)
            navigation.StopManualNavigation("Worqor pilot safety gate became unavailable.");
        foreach (var pilotEvent in worqorGroupPilot.DrainEvents())
            battlefield.RecordNavigationEvent(pilotEvent);

        var sealRockPlan = sealRockGroupPilot.Update(game, battlefieldState, clusters, objectiveRoute,
            pilotEnabled && game.FrontlineMap == FrontlineMap.SealRock,
            pilotReadiness.CanTravel, vnav.IsReady, trackedFieldGroup, allowGroupFallback: false);
        if (sealRockGroupPilot.CancelDestinationId is { } unsafeSealRock &&
            navigation.CurrentManualDestinationId == unsafeSealRock)
            navigation.StopManualNavigation("Seal Rock objective is stale or unsupported.");
        foreach (var pilotEvent in sealRockGroupPilot.DrainEvents())
            battlefield.RecordNavigationEvent(pilotEvent);

        var shatterPlan = shatterGroupPilot.Update(game, battlefieldState, clusters, objectiveRoute,
            pilotEnabled && game.FrontlineMap == FrontlineMap.FieldsOfGlory,
            pilotReadiness.CanTravel, vnav.IsReady, trackedFieldGroup, allowGroupFallback: false);
        if (shatterGroupPilot.RetiredDestinationId is { } retiredId &&
            navigation.CurrentManualDestinationId == retiredId)
            navigation.StopManualNavigation("Shatter ice became inactive or depleted; selecting another destination.");
        foreach (var pilotEvent in shatterGroupPilot.DrainEvents())
            battlefield.RecordNavigationEvent(pilotEvent);

        var objectiveSelected = worqorPlan is not null || sealRockPlan is not null || shatterPlan is not null;
        if (objectiveSelected)
        {
            dynamicFollow.SuspendForStatic();
            navigation.SetManualNavigationArmed(true);
            if (worqorPlan is not null)
                navigation.RequestManualDestination(worqorPlan.DestinationId, worqorPlan.DestinationName,
                    worqorPlan.Position, worqorPlan.ApproachAnchors);
            if (sealRockPlan is not null)
                navigation.RequestManualDestination(sealRockPlan.DestinationId, sealRockPlan.DestinationName,
                    sealRockPlan.Position, sealRockPlan.ApproachAnchors);
            if (shatterPlan is not null)
                navigation.RequestManualDestination(shatterPlan.DestinationId, shatterPlan.DestinationName,
                    shatterPlan.Position, shatterPlan.ApproachAnchors,
                    shatterPlan.IncludeReferencePosition, shatterPlan.MinimumApproachClearance);
        }
        var staticBusy = objectiveSelected || (game.FrontlineMap switch
        {
            FrontlineMap.WorqorChirteh => worqorGroupPilot.CommittedDestinationId is not null ||
                worqorGroupPilot.HoldingAfterArrival(game.CapturedAtUtc),
            FrontlineMap.SealRock => sealRockGroupPilot.CommittedDestinationId is not null ||
                sealRockGroupPilot.HoldingAfterArrival(game.CapturedAtUtc),
            FrontlineMap.FieldsOfGlory => shatterGroupPilot.CommittedDestinationId is not null ||
                shatterGroupPilot.HoldingAfterArrival(game.CapturedAtUtc),
            _ => false,
        });
        var follow = dynamicFollow.Update(game.FrontlineMap, game.CapturedAtUtc, pilotEnabled,
            pilotReadiness.CanTravel, game.LocalPlayer?.IsDead == true,
            battlefieldState.Match.Lifecycle == FrontlineMatchLifecycle.Results,
            staticBusy, navigation.ManualSnapshot, navigation.CurrentManualDestinationId,
            game.LocalPlayer?.Position, trackedFieldGroup);
        if (follow.CancelOwned && dynamicFollow.Owns(game.FrontlineMap, navigation.CurrentManualDestinationId))
        {
            navigation.StopManualNavigation(follow.Status);
            battlefield.RecordNavigationEvent(new ManualNavigationEvent("dynamic_follow_cancelled", follow.Status, game.CapturedAtUtc));
        }
        if (follow.Plan is { } dynamicPlan)
        {
            navigation.SetManualNavigationArmed(true);
            if (navigation.RequestDynamicDestination(dynamicPlan.DestinationId, "Allied field group formation",
                dynamicPlan.Position, game.LocalPlayer!.Position, game.CapturedAtUtc))
                battlefield.RecordNavigationEvent(new ManualNavigationEvent("dynamic_follow_destination",
                    $"map={game.FrontlineMap}; reason={dynamicPlan.Reason}; destination={dynamicPlan.Position}; slot={dynamicFollow.Slot}", game.CapturedAtUtc));
        }
        var navDecision = navigation.Update(game, behavior, mainCluster, objective, combatDecision, battlefieldState, config);
        foreach (var navigationEvent in navigation.DrainManualEvents())
            battlefield.RecordNavigationEvent(navigationEvent);
        var queueDecision = new QueueDecision(QueueLifecycleState.Disabled, FrontlineMap.Unknown,
            0, config.MatchLimit, queueLifecycle.EmergencyStopLatched,
            "Native queue/requeue is disabled during manual M2 navigation validation.");
        developmentLog.Throttled("behavior-evaluation", $"State {behavior}; committed {(game.CapturedAtUtc - behaviorSince).TotalSeconds:F1}s; {reason}");
        developmentLog.Throttled("combat-decision", $"Active {combatDecision.ControllerActive}; desired {combatDecision.DesiredAction} ({combatDecision.ActionId}); {combatDecision.Explanation}");
        var localPosition = game.LocalPlayer?.Position ?? Vector3.Zero;

        current = new TacticalSnapshot(
            game,
            behavior,
            behaviorSince,
            mainCluster,
            clusters,
            target,
            objective,
            navDecision,
            combatDecision,
            queueDecision,
            threat,
            CountNear(game.Friendlies, localPosition, 20f),
            CountNear(game.Enemies, localPosition, 20f),
            CountNear(game.UnknownPlayers, localPosition, 20f),
            CountNear(game.Friendlies, localPosition, 40f),
            CountNear(game.Enemies, localPosition, 40f),
            CountNear(game.UnknownPlayers, localPosition, 40f),
            reason,
            battlefieldState);

        if (behavior != lastLoggedBehavior)
        {
            log.Information("PvPSentinel behavior: {Previous} -> {Current}. {Reason}", lastLoggedBehavior, behavior, reason);
            lastLoggedBehavior = behavior;
        }
    }

    private void DrawUi() => windows.Draw();
    private void OpenDiagnostics() => SetDiagnosticsVisibility(true);
    private void OpenConfiguration() => configurationWindow.OpenAndExpand();

    private void SetDiagnosticsVisibility(bool isOpen)
    {
        diagnostics.IsOpen = isOpen;
        if (config.ShowDiagnostics == isOpen)
            return;

        config.ShowDiagnostics = isOpen;
        config.Save();
    }

    private void OnDiagnosticsClosed()
    {
        if (!config.ShowDiagnostics)
            return;

        config.ShowDiagnostics = false;
        config.Save();
    }

    private void OnVerboseLoggingChanged(bool enabled)
    {
        developmentLog.Reset();
        log.Information("PvPSentinel verbose logging {State}.", enabled ? "enabled" : "disabled");
    }

    private void StopNavigationFromUi()
    {
        config.NavigationEnabled = false;
        config.Save();
        navigation.StopManualNavigation("STOP disabled navigation until explicitly enabled again.");
    }

    private void EmergencyStop()
    {
        navigation.StopOwnedMovement();
        queueLifecycle.EmergencyStop();
        config.Enabled = false;
        config.NavigationEnabled = false;
        config.ObjectiveNavigationEnabled = false;
        config.QueueAutomationEnabled = false;
        config.CombatProvider = CombatProvider.Off;
        config.Save();
        log.Warning("PvPSentinel emergency stop latched. PvPSentinel-owned movement, actions, and queue automation were disabled. External combat plugins remain independent and must be stopped through their own controls.");
    }

    private void ClearEmergencyStop()
    {
        queueLifecycle.ClearEmergencyStop();
        log.Information("PvPSentinel emergency-stop latch cleared. Automation switches remain off until explicitly enabled.");
    }

    private static int CountNear(IEnumerable<PlayerSnapshot> players, Vector3 origin, float radius) =>
        players.Count(player =>
            !player.IsDead &&
            player.CurrentHp > 0 &&
            Vector2.Distance(new Vector2(player.Position.X, player.Position.Z), new Vector2(origin.X, origin.Z)) <= radius);

    private static TacticalSnapshot EmptySnapshot()
    {
        var game = GameStateSnapshot.Unavailable("Waiting for first game-state update.");
        return new TacticalSnapshot(
            game,
            BehaviorState.Idle,
            DateTime.UtcNow,
            null,
            [],
            null,
            null,
            new NavigationDecision(false, null, NavigationPathState.Idle, 0, 0, MountState.Disabled, "Waiting."),
            new CombatDecision(CombatProvider.Off, false, false, 0, "None", "Waiting."),
            new QueueDecision(QueueLifecycleState.Disabled, FrontlineMap.Unknown, 0, 1, false, "Waiting."),
            PvPThreatSnapshot.Unavailable(game.CapturedAtUtc, "Waiting for first threat observation."),
            0, 0, 0, 0, 0, 0,
            "Waiting for first update.",
            BattlefieldState.Unavailable(game.CapturedAtUtc, "Waiting for first battlefield scan."));
    }
}
