namespace PvPSentinel.FrontlineCore;

internal sealed class MatchLifecycleTracker
{
    private FrontlineMatchLifecycle lifecycle = FrontlineMatchLifecycle.Outside;

    public FrontlineMatchState Update(
        bool isFrontline,
        bool dutyStarted,
        FrontlineUiObservation ui,
        bool objectiveClaimCorroborated = false)
    {
        if (!isFrontline)
        {
            lifecycle = FrontlineMatchLifecycle.Outside;
            return new FrontlineMatchState(lifecycle, null, null, [], false,
                "UNAVAILABLE / OPTIONAL", "Outside a recognized Frontline duty.");
        }

        // A reconnect can miss the duty-start edge. Once active, a transient
        // unreadable header must not put the match back into PreMatch.
        if (lifecycle == FrontlineMatchLifecycle.Results || ui.ResultsVisible)
            lifecycle = FrontlineMatchLifecycle.Results;
        else if (lifecycle == FrontlineMatchLifecycle.MatchActive || dutyStarted ||
                 ui.TimeRemaining is { TotalMinutes: > 2 } ||
                 (ui.HeaderVisible && objectiveClaimCorroborated))
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
            $"DutyStarted={dutyStarted}; HeaderVisible={ui.HeaderVisible}; " +
            $"ParsedTimer={(ui.TimeRemaining is { } time ? time.ToString(@"mm\:ss") : "UNRESOLVED")}; " +
            $"Results={ui.ResultsVisible}; Active corroboration={(objectiveClaimCorroborated ? "current claimed Triumph" : "none")}; {ui.Evidence}")
        {
            DutyStarted = dutyStarted,
            HeaderVisible = ui.HeaderVisible,
            ActiveCorroboration = objectiveClaimCorroborated ? "Current claimed Triumph" : "None",
        };
    }

    public void Reset() => lifecycle = FrontlineMatchLifecycle.Outside;
}
