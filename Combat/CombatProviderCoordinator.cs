using PvPSentinel.Diagnostics;
using PvPSentinel.Integrations;
using PvPSentinel.Models;
using PvPSentinel.Combat.Threat;
using System.Numerics;

namespace PvPSentinel.Combat;

internal sealed class CombatProviderCoordinator(
    ICombatController nativeController,
    RotationSolverRebornAdapter rotationSolverReborn,
    DevelopmentLogger developmentLog)
{
    private readonly ExternalCombatYieldTracker externalYield = new();
    private readonly RotationSolverEngagementTracker rebornEngagement = new();

    public string LastAction => nativeController.LastAction;

    public CombatDecision Update(
        GameStateSnapshot game,
        BehaviorState behavior,
        TargetDecision? target,
        Configuration config,
        PvPThreatSnapshot threat,
        bool sentinelMountTransitionPending)
    {
        if (!config.Enabled)
        {
            externalYield.Reset();
            rebornEngagement.Reset();
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
        if (config.CombatProvider != CombatProvider.RotationSolverReborn)
            rebornEngagement.Reset();

        var decision = config.CombatProvider switch
        {
            CombatProvider.ExternalAcr => UpdateExternal(game, config),
            CombatProvider.RotationSolverReborn => UpdateRotationSolverReborn(
                game,
                behavior,
                config,
                threat,
                sentinelMountTransitionPending),
            CombatProvider.NativePvPSentinel => nativeController.Update(game, behavior, target, config, threat),
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
            $"{decision.Provider}|{decision.ControllerActive}|{decision.YieldNavigation}|{decision.External?.EngagementState}|{decision.External?.NearbyEnemies}|{decision.External?.TargetingPlayer}",
            $"Provider {decision.Provider}; active={decision.ControllerActive}; yield navigation={decision.YieldNavigation}. {decision.Explanation}");
        return decision;
    }

    private CombatDecision UpdateRotationSolverReborn(
        GameStateSnapshot game,
        BehaviorState behavior,
        Configuration config,
        PvPThreatSnapshot threat,
        bool sentinelMountTransitionPending)
    {
        var status = rotationSolverReborn.GetStatus(game.CapturedAtUtc);
        var providerActive = status.Loaded && status.IpcAvailable && status.AutorotationActive;
        var nearbyEnemies = game.LocalPlayer is null
            ? 0
            : CountNear(game.Enemies, game.LocalPlayer.Position, config.RotationSolverEnemyClearanceRadius);
        var enemiesTargetingPlayer = threat.IsReliable
            ? threat.Targeters.Count(targeter => targeter.Distance <= Math.Clamp(config.RotationSolverEnemyClearanceRadius, 10f, 60f))
            : 0;
        var engagement = rebornEngagement.Update(
            game.CapturedAtUtc,
            providerActive && game.IsFrontline && game.IsClassificationReliable && game.LocalPlayer is not null,
            game.LocalPlayer?.IsDead == true,
            game.IsMounted,
            game.IsMounting,
            sentinelMountTransitionPending,
            game.IsInCombat,
            game.IsCasting,
            game.IsActionQueued,
            nearbyEnemies,
            enemiesTargetingPlayer,
            behavior == BehaviorState.RespawnRegroup,
            config.RotationSolverQuietSeconds);

        var limitReady = game.IsMachinist && game.LimitBreakPercent >= 99f;
        var desiredAction = limitReady
            ? "LB READY - manual Marksman's Spite"
            : status.NextActionId == 0
                ? "No Reborn action announced"
                : $"{status.NextAction} ({status.NextActionId})";
        var external = new ExternalCombatDiagnostics(
            "RotationSolverReborn",
            engagement.State.ToString(),
            status.Installed,
            status.Loaded,
            status.Version,
            status.IpcAvailable,
            status.AutorotationActive,
            status.NextActionId,
            status.NextAction,
            status.NextGcdActionId,
            status.NextGcdAction,
            engagement.NearbyEnemies,
            engagement.EnemiesTargetingPlayer,
            limitReady,
            status.Explanation);

        var safety = !game.IsFrontline
            ? "PvPSentinel external-combat coordination is idle outside Frontline."
            : !game.IsClassificationReliable
                ? $"External-combat navigation coordination is paused because classification is unreliable: {game.ClassificationReliabilityExplanation}"
                : game.LocalPlayer is null
                    ? "External-combat navigation coordination is paused because the local player is unavailable."
                    : string.Empty;
        var explanation = string.IsNullOrEmpty(safety)
            ? $"{status.Explanation} {engagement.Explanation} PvPSentinel does not set Reborn targets or issue combat actions."
            : $"{status.Explanation} {safety}";
        if (limitReady)
            explanation += " Marksman's Spite is ready; Reborn's current MCH rotation does not automate it, so LB remains manual.";

        return new CombatDecision(
            CombatProvider.RotationSolverReborn,
            providerActive,
            engagement.ShouldYield,
            status.NextActionId,
            desiredAction,
            explanation,
            External: external);
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

    private static int CountNear(IEnumerable<PlayerSnapshot> players, Vector3 origin, float radius) =>
        players.Count(player =>
            !player.IsDead &&
            player.CurrentHp > 0 &&
            player.IsTargetable &&
            Vector2.Distance(
                new Vector2(player.Position.X, player.Position.Z),
                new Vector2(origin.X, origin.Z)) <= Math.Clamp(radius, 10f, 60f));
}
