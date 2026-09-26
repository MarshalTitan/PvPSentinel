using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PvPSentinel.Behavior;
using PvPSentinel.Combat;
using PvPSentinel.Combat.Native;
using PvPSentinel.Combat.Native.Jobs.Machinist;
using PvPSentinel.Diagnostics;
using PvPSentinel.GameState;
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
    private readonly TargetSelector targetSelector;
    private readonly ObjectiveStrategyService objectiveStrategy;
    private readonly BehaviorEngine behaviorEngine = new();
    private readonly VNavmeshAdapter vnav;
    private readonly NavigationController navigation;
    private readonly CombatProviderCoordinator combat;
    private readonly QueueLifecycleController queueLifecycle;
    private readonly WrathAdapter wrath = new();
    private readonly DiagnosticWindow diagnostics;
    private readonly ConfigurationWindow configurationWindow;

    private DateTime nextUpdateUtc = DateTime.MinValue;
    private BehaviorState lastLoggedBehavior = BehaviorState.Idle;
    private TacticalSnapshot current = EmptySnapshot();

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IClientState clientState,
        ICondition condition,
        IObjectTable objectTable,
        IPartyList partyList,
        IDataManager dataManager,
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

        developmentLog = new DevelopmentLogger(log, () => config.VerboseLogging);
        clusterAnalyzer = new FriendlyClusterAnalyzer(developmentLog);
        mainGroupTracker = new MainGroupTracker(developmentLog);
        targetSelector = new TargetSelector(developmentLog);
        objectiveStrategy = new ObjectiveStrategyService(developmentLog);

        gameState = new GameStateService(clientState, condition, objectTable, partyList, dataManager, log, developmentLog);
        vnav = new VNavmeshAdapter(pi, developmentLog);
        var mount = new NativeMountController(developmentLog);
        navigation = new NavigationController(vnav, mount, developmentLog);
        var executor = new NativeActionExecutor(objectTable, targetManager, log);
        IPvpJobCombatModule[] jobModules = [new MachinistCombatModule()];
        var nativeCombat = new NativeCombatProvider(dataManager, executor, jobModules, developmentLog, log);
        combat = new CombatProviderCoordinator(nativeCombat, developmentLog);
        var queueAdapter = new FrontlineQueueAdapter(gameGui, dataManager, developmentLog);
        queueLifecycle = new QueueLifecycleController(queueAdapter, dutyState, developmentLog);

        diagnostics = new DiagnosticWindow(() => current, vnav, wrath, () => combat.LastAction, OnDiagnosticsClosed)
        {
            IsOpen = config.ShowDiagnostics,
        };
        configurationWindow = new ConfigurationWindow(
            config,
            SetDiagnosticsVisibility,
            OnVerboseLoggingChanged,
            EmergencyStop,
            ClearEmergencyStop,
            queueLifecycle.ResetMatchCounter,
            () => queueLifecycle.EmergencyStopLatched);
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
        queueLifecycle.Dispose();
        framework.Update -= OnFrameworkUpdate;
        pi.UiBuilder.Draw -= DrawUi;
        pi.UiBuilder.OpenMainUi -= OpenDiagnostics;
        pi.UiBuilder.OpenConfigUi -= OpenConfiguration;
        commands.RemoveHandler(Command);
        windows.RemoveAllWindows();
    }

    private void OnCommand(string _, string arguments)
    {
        if (arguments.Trim().Equals("config", StringComparison.OrdinalIgnoreCase))
            configurationWindow.IsOpen = true;
        else
            diagnostics.IsOpen = true;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (DateTime.UtcNow < nextUpdateUtc)
            return;
        nextUpdateUtc = DateTime.UtcNow.AddMilliseconds(250);

        var game = gameState.Capture();
        var clusters = clusterAnalyzer.Analyze(game.Friendlies, config.FriendlyClusterLinkRadius, game.CapturedAtUtc);
        var mainCluster = mainGroupTracker.Select(clusters, game.CapturedAtUtc);
        var target = targetSelector.Select(game, mainCluster, config);
        var objective = objectiveStrategy.Select(game, config);
        var (behavior, behaviorSince, reason) = behaviorEngine.Evaluate(game, mainCluster, target, objective, config);
        var combatDecision = combat.Update(game, behavior, target, config);
        var navDecision = navigation.Update(game, behavior, mainCluster, objective, combatDecision, config);
        var queueDecision = queueLifecycle.Update(game, config);
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
            CountNear(game.Friendlies, localPosition, 20f),
            CountNear(game.Enemies, localPosition, 20f),
            CountNear(game.UnknownPlayers, localPosition, 20f),
            CountNear(game.Friendlies, localPosition, 40f),
            CountNear(game.Enemies, localPosition, 40f),
            CountNear(game.UnknownPlayers, localPosition, 40f),
            reason);

        if (behavior != lastLoggedBehavior)
        {
            log.Information("PvPSentinel behavior: {Previous} -> {Current}. {Reason}", lastLoggedBehavior, behavior, reason);
            lastLoggedBehavior = behavior;
        }
    }

    private void DrawUi() => windows.Draw();
    private void OpenDiagnostics() => SetDiagnosticsVisibility(true);
    private void OpenConfiguration() => configurationWindow.IsOpen = true;

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

    private void EmergencyStop()
    {
        navigation.StopOwnedMovement();
        queueLifecycle.EmergencyStop();
        config.Enabled = false;
        config.NavigationEnabled = false;
        config.MountingEnabled = false;
        config.ObjectiveNavigationEnabled = false;
        config.QueueAutomationEnabled = false;
        config.CombatProvider = CombatProvider.Off;
        config.Save();
        log.Warning("PvPSentinel emergency stop latched. Owned movement/queue were stopped and every action-capable automation switch was disabled.");
    }

    private void ClearEmergencyStop()
    {
        queueLifecycle.ClearEmergencyStop();
        log.Information("PvPSentinel emergency-stop latch cleared. Automation switches remain off until explicitly enabled.");
    }

    private static int CountNear(IEnumerable<PlayerSnapshot> players, Vector3 origin, float radius) =>
        players.Count(player => Vector2.Distance(new Vector2(player.Position.X, player.Position.Z), new Vector2(origin.X, origin.Z)) <= radius);

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
            0, 0, 0, 0, 0, 0,
            "Waiting for first update.");
    }
}
