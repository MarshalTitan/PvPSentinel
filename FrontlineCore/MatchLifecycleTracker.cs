namespace PvPSentinel.FrontlineCore;

internal sealed class MatchLifecycleTracker
{
    private FrontlineMatchLifecycle lifecycle = FrontlineMatchLifecycle.Outside;

    public FrontlineMatchState Update(
        bool isFrontline,
        bool dutyStarted,
        FrontlineUiObservation ui)
    {
        if (!isFrontline)
        {
            lifecycle = FrontlineMatchLifecycle.Outside;
            return new FrontlineMatchState(lifecycle, null, null, [], false,
                "UNAVAILABLE / OPTIONAL", "Outside a recognized Frontline duty.");
        }

        if (lifecycle == FrontlineMatchLifecycle.Results || ui.ResultsVisible)
            lifecycle = FrontlineMatchLifecycle.Results;
        else if (dutyStarted || ui.TimeRemaining is { TotalMinutes: > 2 })
            lifecycle = FrontlineMatchLifecycle.MatchActive;
        else
            lifecycle = FrontlineMatchLifecycle.PreMatch;

        return new FrontlineMatchState(
            lifecycle,
            ui.TimeRemaining,
            ui.ScoreCap,
            ui.GrandCompanies,
            lifecycle == FrontlineMatchLifecycle.Results,
            "UNAVAILABLE / OPTIONAL",
            ui.Evidence);
    }

    public void Reset() => lifecycle = FrontlineMatchLifecycle.Outside;
}
