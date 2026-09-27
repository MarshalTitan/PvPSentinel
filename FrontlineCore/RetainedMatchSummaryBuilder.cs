using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore;

internal sealed class RetainedMatchSummaryBuilder(FrontlineMap map, DateTime enteredAtUtc)
{
    private readonly HashSet<string> objectives = new(StringComparer.OrdinalIgnoreCase);
    private int navigationRequests;
    private int navigationArrivals;
    private int navigationFailures;

    public FrontlineMap Map { get; } = map;
    public DateTime EnteredAtUtc { get; } = enteredAtUtc;
    public DateTime? MatchStartUtc { get; private set; }
    public DateTime? MatchEndUtc { get; private set; }
    public byte LocalPvPTeam { get; private set; }
    public int PeakSelf { get; private set; }
    public int PeakAllies { get; private set; }
    public int PeakEnemies { get; private set; }
    public int PeakUnknown { get; private set; }
    public FrontlineMatchLifecycle FinalLifecycle { get; private set; }
    public bool ResultsDetected { get; private set; }
    public int SensorErrors { get; private set; }
    public int Deaths { get; private set; }
    public int Respawns { get; private set; }

    public void Observe(BattlefieldState state)
    {
        LocalPvPTeam = state.LocalPvPTeam > 0 ? state.LocalPvPTeam : LocalPvPTeam;
        PeakSelf = Math.Max(PeakSelf, state.Counts.Self);
        PeakAllies = Math.Max(PeakAllies, state.Counts.Allies);
        PeakEnemies = Math.Max(PeakEnemies, state.Counts.Enemies);
        PeakUnknown = Math.Max(PeakUnknown, state.Counts.Unknown);
        Deaths = Math.Max(Deaths, state.DeathRespawn.Deaths);
        Respawns = Math.Max(Respawns, state.DeathRespawn.Respawns);
        FinalLifecycle = state.Match.Lifecycle;
        ResultsDetected |= state.Match.ResultsDetected;
        SensorErrors = Math.Max(SensorErrors, state.SensorHealth.Sum(sensor => sensor.ErrorCount));
        foreach (var objective in state.Objectives.Where(item => item.FirstSeenUtc is not null))
            objectives.Add(objective.LogicalId);
        if (state.Match.Lifecycle == FrontlineMatchLifecycle.MatchActive)
            MatchStartUtc ??= state.CapturedAtUtc;
        if (state.Match.Lifecycle == FrontlineMatchLifecycle.Results)
            MatchEndUtc ??= state.CapturedAtUtc;
    }

    public void RecordNavigation(string eventName)
    {
        if (eventName == "navigation_request") navigationRequests++;
        else if (eventName == "navigation_arrived") navigationArrivals++;
        else if (eventName == "navigation_failed") navigationFailures++;
    }

    public RetainedMatchSummary Build(DateTime exitedAtUtc) => new(
        Map, EnteredAtUtc, MatchStartUtc, MatchEndUtc, exitedAtUtc, LocalPvPTeam,
        objectives.Order(StringComparer.OrdinalIgnoreCase).ToArray(), objectives.Count,
        PeakSelf, PeakAllies, PeakEnemies, PeakUnknown, Deaths, Respawns,
        FinalLifecycle, ResultsDetected, SensorErrors,
        navigationRequests, navigationArrivals, navigationFailures);
}
