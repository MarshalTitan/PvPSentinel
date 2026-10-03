namespace PvPSentinel.FrontlineCore;

// Only stable, logged-in territory frames with a local player may reach
// Frontline native research sensors. Restart after every zoning/loading edge.
internal sealed class FrontlineEntryGate
{
    private uint territory;
    private DateTime stableSinceUtc;

    public bool MayScan(bool isFrontline, uint currentTerritory, bool loggedIn,
        bool betweenAreas, bool localPlayerAvailable, DateTime now)
    {
        if (!isFrontline || !loggedIn || betweenAreas || !localPlayerAvailable || currentTerritory == 0)
        {
            territory = 0;
            stableSinceUtc = default;
            return false;
        }
        if (territory != currentTerritory)
        {
            territory = currentTerritory;
            stableSinceUtc = now;
            return false;
        }
        return now - stableSinceUtc >= TimeSpan.FromSeconds(1.25);
    }
}
