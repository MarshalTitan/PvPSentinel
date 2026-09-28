using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore;

internal sealed class RetainedMatchSummaryBuilder(
    FrontlineMap map,
    uint territoryId,
    uint contentFinderConditionId,
    DateTime enteredAtUtc)
{
    internal const int MaximumRetainedObjectiveTransitions = 512;
    internal const int MaximumRetainedTransitionsPerSignal = 12;
    private readonly HashSet<string> objectives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ObjectiveResearchSummary> objectiveResearch = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResearchObjectSummary> researchObjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ObjectiveTransitionSummary> objectiveTransitions = [];
    private IReadOnlyList<string> unresolvedObservations = [];
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
        // Battalion is zero-based in the live Dalamud source. Preserve team 0
        // exactly like teams 1 and 2, while ignoring unavailable sentinels.
        if (TeamClassifier.IsValidFrontlineBattalion(state.LocalPvPTeam))
            LocalPvPTeam = state.LocalPvPTeam;
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
        // Research notes contain live counters as well as durable caveats. Keeping
        // every distinct counter value across a match made Secure summaries grow
        // past a megabyte. The final sensor snapshot is sufficient for retained
        // diagnostics; objective transitions preserve the event history separately.
        unresolvedObservations = state.ResearchNotes
            .Select(Diagnostics.PrivacySanitizer.Sanitize)
            .Distinct(StringComparer.Ordinal)
            .Take(32)
            .ToArray();
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
        var sanitized = Diagnostics.PrivacySanitizer.Sanitize(change.Detail);
        var lastMatching = objectiveTransitions.LastOrDefault(item =>
            item.EventName == change.EventName &&
            item.LogicalId == change.LogicalId);
        if (lastMatching is not null && lastMatching.Evidence == sanitized)
            return;

        var matchingIndices = objectiveTransitions
            .Select((item, index) => (item, index))
            .Where(value => value.item.EventName == change.EventName &&
                            value.item.LogicalId == change.LogicalId)
            .Select(value => value.index)
            .ToArray();
        if (matchingIndices.Length >= MaximumRetainedTransitionsPerSignal)
            objectiveTransitions.RemoveAt(matchingIndices[0]);

        objectiveTransitions.Add(new ObjectiveTransitionSummary(
            now,
            change.EventName,
            change.LogicalId,
            sanitized));
        if (objectiveTransitions.Count > MaximumRetainedObjectiveTransitions)
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
