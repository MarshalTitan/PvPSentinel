namespace PvPSentinel.Combat;

internal enum ExternalEngagementState
{
    Unavailable,
    Transit,
    Engaged,
    Dead,
    Regrouping,
}

internal sealed record RotationSolverEngagementDecision(
    ExternalEngagementState State,
    bool ShouldYield,
    int NearbyEnemies,
    int EnemiesTargetingPlayer,
    string Explanation);

/// <summary>
/// Converts observable local combat state into a stable ownership decision.
/// RotationSolverReborn has no supported PvP combat-permission IPC, so once an
/// engagement starts PvPSentinel deliberately yields its path until the player
/// is out of combat, observed hard-target pressure clears, and a short quiet
/// period elapses. Passive enemy proximity remains diagnostic and cannot starve
/// a preserved route indefinitely.
/// </summary>
internal sealed class RotationSolverEngagementTracker
{
    private ExternalEngagementState state = ExternalEngagementState.Unavailable;
    private DateTime quietSinceUtc = DateTime.MinValue;
    private bool waitingForRespawn;

    public RotationSolverEngagementDecision Update(
        DateTime now,
        bool providerConnected,
        bool isDead,
        bool isMounted,
        bool isMounting,
        bool sentinelMountTransitionPending,
        bool isInCombat,
        bool isCasting,
        bool isActionQueued,
        int nearbyEnemies,
        int enemiesTargetingPlayer,
        bool isRespawnRegroup,
        float quietSeconds)
    {
        nearbyEnemies = Math.Max(0, nearbyEnemies);
        enemiesTargetingPlayer = Math.Max(0, enemiesTargetingPlayer);
        quietSeconds = Math.Clamp(quietSeconds, 1f, 15f);

        if (!providerConnected)
        {
            Reset();
            return new RotationSolverEngagementDecision(
                ExternalEngagementState.Unavailable,
                false,
                nearbyEnemies,
                enemiesTargetingPlayer,
                "RotationSolverReborn is not connected; PvPSentinel cannot coordinate external combat travel.");
        }

        if (isDead)
        {
            state = ExternalEngagementState.Dead;
            quietSinceUtc = DateTime.MinValue;
            waitingForRespawn = true;
            return new RotationSolverEngagementDecision(
                state,
                false,
                nearbyEnemies,
                enemiesTargetingPlayer,
                "The player is dead. Combat yield is released so the normal respawn/regroup lifecycle can proceed.");
        }

        if (waitingForRespawn)
        {
            waitingForRespawn = false;
            state = ExternalEngagementState.Regrouping;
            quietSinceUtc = DateTime.MinValue;
        }

        // Mount casts can surface before the client sets its mounting condition.
        // The Sentinel mount owner therefore supplies its short request latch as
        // well as the public mounted/mounting flags so our own cast cannot be
        // mistaken for Reborn beginning an engagement.
        var strategicMountTransition = isMounting || sentinelMountTransitionPending;
        var hasCombatEvidence = isInCombat ||
                                (!strategicMountTransition && !isMounted && nearbyEnemies > 0 && (isCasting || isActionQueued));
        if (hasCombatEvidence)
        {
            state = ExternalEngagementState.Engaged;
            quietSinceUtc = DateTime.MinValue;
            var evidence = isInCombat
                ? "the client reports the local player in combat"
                : isCasting
                    ? "the local player is casting"
                    : "the client reports a queued local action";
            return new RotationSolverEngagementDecision(
                state,
                true,
                nearbyEnemies,
                enemiesTargetingPlayer,
                $"Engagement latched because {evidence}. Strategic travel remains paused until combat and observed hard-target pressure clear; Reborn action execution is not observable.");
        }

        if (state == ExternalEngagementState.Engaged)
        {
            if (enemiesTargetingPlayer > 0)
            {
                quietSinceUtc = DateTime.MinValue;
                return new RotationSolverEngagementDecision(
                    state,
                    true,
                    nearbyEnemies,
                    enemiesTargetingPlayer,
                    $"The combat flag cleared, but {enemiesTargetingPlayer} observed enemy player(s) inside the configured radius still hard-target the local player. Passive nearby enemies={nearbyEnemies}.");
            }

            if (quietSinceUtc == DateTime.MinValue)
                quietSinceUtc = now;

            var elapsed = now - quietSinceUtc;
            if (elapsed < TimeSpan.FromSeconds(quietSeconds))
            {
                return new RotationSolverEngagementDecision(
                    state,
                    true,
                    nearbyEnemies,
                    enemiesTargetingPlayer,
                    $"Combat and observed hard-target pressure cleared; waiting {Math.Max(0, quietSeconds - elapsed.TotalSeconds):F1}s of quiet time before resuming strategic travel. Passive nearby enemies={nearbyEnemies} do not reset this timer.");
            }

            state = isRespawnRegroup ? ExternalEngagementState.Regrouping : ExternalEngagementState.Transit;
            quietSinceUtc = DateTime.MinValue;
            return new RotationSolverEngagementDecision(
                state,
                false,
                nearbyEnemies,
                enemiesTargetingPlayer,
                $"Combat, observed hard-target pressure, and the post-combat quiet period are clear; strategic travel may resume. Passive nearby enemies={nearbyEnemies}.");
        }

        if (isRespawnRegroup || state == ExternalEngagementState.Regrouping)
        {
            state = isRespawnRegroup ? ExternalEngagementState.Regrouping : ExternalEngagementState.Transit;
            return new RotationSolverEngagementDecision(
                state,
                false,
                nearbyEnemies,
                enemiesTargetingPlayer,
                state == ExternalEngagementState.Regrouping
                    ? "Post-respawn regrouping is active; Reborn remains enabled while PvPSentinel owns strategic travel."
                    : "Post-respawn regrouping completed; normal strategic transit may continue.");
        }

        state = ExternalEngagementState.Transit;
        return new RotationSolverEngagementDecision(
            state,
            false,
            nearbyEnemies,
            enemiesTargetingPlayer,
            isMounted
                ? "Mounted strategic transit is active; Reborn should remain action-idle while mounted."
                : "No combat engagement is latched; PvPSentinel owns strategic transit.");
    }

    public void Reset()
    {
        state = ExternalEngagementState.Unavailable;
        quietSinceUtc = DateTime.MinValue;
        waitingForRespawn = false;
    }
}
