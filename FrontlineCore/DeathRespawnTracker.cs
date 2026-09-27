namespace PvPSentinel.FrontlineCore;

internal sealed class DeathRespawnTracker
{
    private bool? wasDead;
    private int deaths;
    private int respawns;
    private DateTime? lastTransitionUtc;
    private bool reportRespawned;

    public DeathRespawnSnapshot Update(bool playerAvailable, bool isDead, DateTime now)
    {
        if (!playerAvailable)
            return new DeathRespawnSnapshot(DeathRespawnState.Unavailable, deaths, respawns, lastTransitionUtc);

        reportRespawned = false;
        if (wasDead == false && isDead)
        {
            deaths++;
            lastTransitionUtc = now;
        }
        else if (wasDead == true && !isDead)
        {
            respawns++;
            lastTransitionUtc = now;
            reportRespawned = true;
        }
        wasDead = isDead;
        return new DeathRespawnSnapshot(
            isDead ? DeathRespawnState.Dead : reportRespawned ? DeathRespawnState.Respawned : DeathRespawnState.Alive,
            deaths,
            respawns,
            lastTransitionUtc);
    }

    public void Reset()
    {
        wasDead = null;
        deaths = 0;
        respawns = 0;
        lastTransitionUtc = null;
        reportRespawned = false;
    }
}
