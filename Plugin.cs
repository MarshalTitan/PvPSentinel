using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PvPSentinel.Behavior;
using PvPSentinel.Combat;
using PvPSentinel.GameState;
using PvPSentinel.Integrations;
using PvPSentinel.Intelligence;
using PvPSentinel.Models;
using PvPSentinel.Navigation;
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
    private readonly FriendlyClusterAnalyzer clusterAnalyzer = new();
    private readonly MainGroupTracker mainGroupTracker = new();
    private readonly TargetSelector targetSelector = new();
    private readonly BehaviorEngine behaviorEngine = new();
    private readonly VNavmeshAdapter vnav;
    private readonly NavigationController navigation;
    private readonly MachinistPvpCombatController combat;
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
        IFramework framework,
        IPluginLog pluginLog)
    {
        pi = pluginInterface;
        commands = commandManager;
        this.framework = framework;
        log = pluginLog;

        config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        config.Initialize(pi);

        gameState = new GameStateService(clientState, condition, objectTable, partyList, dataManager, log);
        vnav = new VNavmeshAdapter(pi, log);
        navigation = new NavigationController(vnav, log);
        var executor = new NativeActionExecutor(objectTable, targetManager, log);
        combat = new MachinistPvpCombatController(dataManager, executor, log);

        diagnostics = new DiagnosticWindow(() => current, vnav, wrath, () => combat.LastAction)
        {
            IsOpen = config.ShowDiagnostics,
        };
        configurationWindow = new ConfigurationWindow(config);
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
        log.Information("PvP Sentinel development build loaded. Master enable: {Enabled}; combat execution: {Combat}.", config.Enabled, config.CombatEnabled);
    }

    public void Dispose()
    {
        navigation.StopOwnedMovement();
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
        var (behavior, behaviorSince, reason) = behaviorEngine.Evaluate(game, mainCluster, target, config);
        var navDecision = navigation.Update(game, behavior, mainCluster, config);
        var combatDecision = combat.Update(game, behavior, target, config);
        var localPosition = game.LocalPlayer?.Position ?? Vector3.Zero;

        current = new TacticalSnapshot(
            game,
            behavior,
            behaviorSince,
            mainCluster,
            clusters,
            target,
            navDecision,
            combatDecision,
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
    private void OpenDiagnostics() => diagnostics.IsOpen = true;
    private void OpenConfiguration() => configurationWindow.IsOpen = true;

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
            new NavigationDecision(false, null, "Waiting."),
            new CombatDecision(false, 0, "None", "Waiting."),
            0, 0, 0, 0, 0, 0,
            "Waiting for first update.");
    }
}
