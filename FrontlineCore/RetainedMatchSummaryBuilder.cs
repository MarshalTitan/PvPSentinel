using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore;

internal sealed class RetainedMatchSummaryBuilder(
    FrontlineMap map,
    uint territoryId,
    uint contentFinderConditionId,
    DateTime enteredAtUtc)
{
    private readonly HashSet<string> objectives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ObjectiveResearchSummary> objectiveResearch = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResearchObjectSummary> researchObjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ObjectiveTransitionSummary> objectiveTransitions = [];
    private readonly HashSet<string> unresolvedObservations = new(StringComparer.Ordinal);
    private int navigationRequests;
    private int navigationArrivals;
    private int navigationFailures;
    private int navigationPathFailures;
    private int navigationStops;
    private int navigationStuckEvents;
    private int navigationRouteRejections;

    public FrontlineMap Map { get; } = map;
    public uint TerritoryId { get; } = territoryId;
    public uint ContentFinderConditionId { get; } = contentFinderConditionId;
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
        {
            objectives.Add(objective.LogicalId);
            objectiveResearch[objective.LogicalId] = new ObjectiveResearchSummary(
                objective.LogicalId,
                objective.ReferencePosition,
                objective.State.ToString(),
                objective.Owner.ToString(),
                objective.Kind,
                Diagnostics.PrivacySanitizer.Sanitize(objective.Evidence),
                objective.FirstSeenUtc,
                objective.LastSeenUtc);
        }
        foreach (var item in state.ResearchObjects)
        {
            researchObjects[item.ResearchId] = new ResearchObjectSummary(
                item.ResearchId,
                Diagnostics.PrivacySanitizer.Sanitize(item.Name),
                item.ObjectKind,
                item.GameObjectId,
                item.EntityId,
                item.BaseId,
                item.Position,
                item.IsTargetable,
                item.IsDead,
                item.CurrentlyObserved,
                item.CurrentHp,
                item.MaxHp,
                item.FirstSeenUtc,
                item.LastSeenUtc,
                Diagnostics.PrivacySanitizer.Sanitize(item.Evidence));
        }
        foreach (var note in state.ResearchNotes)
            unresolvedObservations.Add(note);
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
        else if (eventName == "navigation_path_failed") navigationPathFailures++;
        else if (eventName == "navigation_cancelled") navigationStops++;
        else if (eventName == "navigation_stuck") navigationStuckEvents++;
        else if (eventName == "navigation_route_rejected") navigationRouteRejections++;
    }

    public void RecordObjectiveChange(ObjectiveChange change, DateTime now)
    {
        objectiveTransitions.Add(new ObjectiveTransitionSummary(
            now,
            change.EventName,
            change.LogicalId,
            Diagnostics.PrivacySanitizer.Sanitize(change.Detail)));
        if (objectiveTransitions.Count > 2048)
            objectiveTransitions.RemoveAt(0);
    }

    public RetainedMatchSummary Build(DateTime exitedAtUtc) => new(
        Map, TerritoryId, ContentFinderConditionId,
        EnteredAtUtc, MatchStartUtc, MatchEndUtc, exitedAtUtc, LocalPvPTeam,
        objectives.Order(StringComparer.OrdinalIgnoreCase).ToArray(), objectives.Count,
        PeakSelf, PeakAllies, PeakEnemies, PeakUnknown, Deaths, Respawns,
        FinalLifecycle, ResultsDetected, SensorErrors,
        navigationRequests, navigationArrivals, navigationFailures, navigationPathFailures,
        navigationStops, navigationStuckEvents, navigationRouteRejections,
        objectiveResearch.Values.OrderBy(item => item.LogicalId, StringComparer.OrdinalIgnoreCase).ToArray(),
        researchObjects.Values.OrderBy(item => item.ResearchId, StringComparer.OrdinalIgnoreCase).ToArray(),
        objectiveTransitions.ToArray(),
        unresolvedObservations.Order(StringComparer.Ordinal).ToArray());
}
