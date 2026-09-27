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
    string Explanation);

/// <summary>
/// Converts observable local combat state into a stable ownership decision.
/// RotationSolverReborn has no supported PvP combat-permission IPC, so once an
/// engagement starts PvPSentinel deliberately yields its path until the player
/// is out of combat, the clearance area is empty, and the quiet period elapses.
/// </summary>
internal sealed class RotationSolverEngagementTracker
{
    private ExternalEngagementState state = ExternalEngagementState.Unavailable;
    private DateTime quietSinceUtc = DateTime.MinValue;
    private bool waitingForRespawn;

    public RotationSolverEngagementDecision Update(
        DateTime now,
        bool providerAvailableAndActive,
        bool isDead,
        bool isMounted,
        bool isMounting,
        bool sentinelMountTransitionPending,
        bool isInCombat,
        bool isCasting,
        bool isActionQueued,
        int nearbyEnemies,
        bool isRespawnRegroup,
        float quietSeconds)
    {
        nearbyEnemies = Math.Max(0, nearbyEnemies);
        quietSeconds = Math.Clamp(quietSeconds, 1f, 15f);

        if (!providerAvailableAndActive)
        {
            Reset();
            return new RotationSolverEngagementDecision(
                ExternalEngagementState.Unavailable,
                false,
                nearbyEnemies,
                "RotationSolverReborn is not both loaded and active; PvPSentinel will not claim that external combat is being handled.");
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
                $"Engagement latched because {evidence}. Reborn owns local combat; strategic travel remains paused until combat and nearby threats clear.");
        }

        if (state == ExternalEngagementState.Engaged)
        {
            if (nearbyEnemies > 0)
            {
                quietSinceUtc = DateTime.MinValue;
                return new RotationSolverEngagementDecision(
                    state,
                    true,
                    nearbyEnemies,
                    $"The combat flag cleared, but {nearbyEnemies} observed enemy player(s) remain inside the engagement-clearance radius.");
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
                    $"Combat and nearby enemies cleared; waiting {Math.Max(0, quietSeconds - elapsed.TotalSeconds):F1}s of quiet time before resuming strategic travel.");
            }

            state = isRespawnRegroup ? ExternalEngagementState.Regrouping : ExternalEngagementState.Transit;
            quietSinceUtc = DateTime.MinValue;
            return new RotationSolverEngagementDecision(
                state,
                false,
                nearbyEnemies,
                "Combat, nearby enemies, and the post-combat quiet period are clear; strategic travel may resume.");
        }

        if (isRespawnRegroup || state == ExternalEngagementState.Regrouping)
        {
            state = isRespawnRegroup ? ExternalEngagementState.Regrouping : ExternalEngagementState.Transit;
            return new RotationSolverEngagementDecision(
                state,
                false,
                nearbyEnemies,
                state == ExternalEngagementState.Regrouping
                    ? "Post-respawn regrouping is active; Reborn remains enabled while PvPSentinel owns strategic travel."
                    : "Post-respawn regrouping completed; normal strategic transit may continue.");
        }

        state = ExternalEngagementState.Transit;
        return new RotationSolverEngagementDecision(
            state,
            false,
            nearbyEnemies,
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
