using PvPSentinel.Diagnostics;
using PvPSentinel.Models;

namespace PvPSentinel.Combat;

internal sealed class CombatProviderCoordinator(
    ICombatController nativeController,
    DevelopmentLogger developmentLog)
{
    private readonly ExternalCombatYieldTracker externalYield = new();

    public string LastAction => nativeController.LastAction;

    public CombatDecision Update(
        GameStateSnapshot game,
        BehaviorState behavior,
        TargetDecision? target,
        Configuration config)
    {
        if (!config.Enabled)
        {
            externalYield.Reset();
            return new CombatDecision(
                config.CombatProvider,
                false,
                false,
                0,
                "None",
                "Master enable is off; the selected combat provider is inactive.");
        }

        if (config.CombatProvider != CombatProvider.ExternalAcr)
            externalYield.Reset();

        var decision = config.CombatProvider switch
        {
            CombatProvider.ExternalAcr => UpdateExternal(game, config),
            CombatProvider.NativePvPSentinel => nativeController.Update(game, behavior, target, config),
            _ => new CombatDecision(
                CombatProvider.Off,
                false,
                false,
                0,
                "None",
                "Combat provider is Off; PvPSentinel will not select targets for an external system or execute actions."),
        };

        developmentLog.Changed(
            "combat-provider",
            $"{decision.Provider}|{decision.ControllerActive}|{decision.YieldNavigation}",
            $"Provider {decision.Provider}; active={decision.ControllerActive}; yield navigation={decision.YieldNavigation}. {decision.Explanation}");
        return decision;
    }

    private CombatDecision UpdateExternal(
        GameStateSnapshot game,
        Configuration config)
    {
        if (!game.IsFrontline || !game.IsClassificationReliable || game.LocalPlayer is null || game.LocalPlayer.IsDead)
        {
            externalYield.Reset();
            return new CombatDecision(
                CombatProvider.ExternalAcr,
                false,
                false,
                0,
                "External ACR idle",
                "External combat coordination is behind the Frontline/classification safety gate.");
        }

        // This deliberately uses only local game state. There is no direct or
        // undocumented MMOMinion/Champion IPC dependency. Combat, casting, and
        // a queued local action are sufficient evidence to yield owned movement;
        // a short grace period prevents movement oscillation between actions.
        var yield = externalYield.Update(
            game.CapturedAtUtc,
            game.IsInCombat,
            game.IsCasting,
            game.IsActionQueued,
            config.ExternalCombatYieldSeconds);

        return new CombatDecision(
            CombatProvider.ExternalAcr,
            true,
            yield.ShouldYield,
            0,
            yield.ShouldYield ? "Yield to external ACR" : "External ACR available",
            yield.Explanation + " PvPSentinel does not issue targets or actions in External Combat / ACR mode.");
    }
}
