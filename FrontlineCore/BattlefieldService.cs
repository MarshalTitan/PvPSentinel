using Dalamud.Plugin.Services;
using System.Globalization;
using PvPSentinel.Combat.Threat;
using PvPSentinel.Diagnostics;
using PvPSentinel.FrontlineCore.Diagnostics;
using PvPSentinel.FrontlineCore.Maps;
using PvPSentinel.FrontlineCore.Sensors;
using PvPSentinel.Models;
using PvPSentinel.Navigation;

namespace PvPSentinel.FrontlineCore;

internal sealed class BattlefieldService
{
    private readonly IDutyState dutyState;
    private readonly IPluginLog log;
    private readonly DevelopmentLogger developmentLog;
    private readonly FrontlineMapMarkerSensor markerSensor = new();
    private readonly FrontlineUiSensor uiSensor;
    private readonly PlayerTracker playerTracker = new();
    private readonly MatchLifecycleTracker lifecycleTracker = new();
    private readonly DeathRespawnTracker deathTracker = new();
    private readonly CombatContextTracker combatTracker = new();
    private readonly Dictionary<FrontlineMap, IFrontlineMapAdapter> adapters;
    private readonly Dictionary<FrontlineMap, IFrontlineMapAdapter> passiveAdapters = [];
    private readonly Dictionary<string, MutableSensorHealth> sensorHealth = [];
    private readonly Dictionary<uint, BattlefieldRelationship> priorRelationships = [];
    private readonly EventDiffLimiter diffs = new(TimeSpan.FromSeconds(10));
    private readonly FrontlineEventRecorder recorder;
    private IFrontlineMapAdapter? activeAdapter;
    private RetainedMatchSummaryBuilder? summaryBuilder;
    private RetainedMatchSummary? retainedSummary;
    private FrontlineMatchLifecycle priorLifecycle;
    private DeathRespawnState priorDeathState = DeathRespawnState.Unavailable;
    private FrontlineCombatContext priorCombatContext;
    private IReadOnlyList<FrontlineMapMarkerObservation> lastSuccessfulMarkers = [];

    public BattlefieldService(
        IGameGui gameGui,
        IDutyState dutyState,
        string configDirectory,
        DevelopmentLogger developmentLog,
        IPluginLog log)
    {
        this.dutyState = dutyState;
        this.developmentLog = developmentLog;
        this.log = log;
        uiSensor = new FrontlineUiSensor(gameGui);
        recorder = new FrontlineEventRecorder(configDirectory, log);
        adapters = new Dictionary<FrontlineMap, IFrontlineMapAdapter>
        {
            [FrontlineMap.BorderlandRuins] = new SecureAdapter(),
            [FrontlineMap.FieldsOfGlory] = new ShatterAdapter(),
            [FrontlineMap.SealRock] = new SealRockAdapter(),
        };
        sensorHealth["PvP team/player tracker"] = new MutableSensorHealth("PvP team/player tracker");
        sensorHealth["AgentMap.EventMarkers"] = new MutableSensorHealth("AgentMap.EventMarkers");
        sensorHealth["Frontline addons"] = new MutableSensorHealth("Frontline addons");
        sensorHealth["Map objective adapter"] = new MutableSensorHealth("Map objective adapter");
    }

    public string CurrentLogDirectory => recorder.CurrentLogDirectory;
    public BattlefieldState Current { get; private set; } = BattlefieldState.Unavailable(DateTime.UtcNow, "Waiting for first battlefield scan.");

    public BattlefieldState Update(GameStateSnapshot game, PvPThreatSnapshot threat)
    {
        IFrontlineMapAdapter? desiredAdapter = null;
        if (game.IsFrontline)
        {
            if (!adapters.TryGetValue(game.FrontlineMap, out desiredAdapter))
            {
                if (!passiveAdapters.TryGetValue(game.FrontlineMap, out desiredAdapter))
                {
                    desiredAdapter = new PassiveFrontlineAdapter(game.FrontlineMap);
                    passiveAdapters[game.FrontlineMap] = desiredAdapter;
                }
            }
        }
        if (activeAdapter is not null && !ReferenceEquals(activeAdapter, desiredAdapter))
            ExitMap(game.CapturedAtUtc);
        if (desiredAdapter is null)
        {
            Current = BattlefieldState.Unavailable(game.CapturedAtUtc,
                "Outside a recognized Frontline duty.") with { LastMatchSummary = retainedSummary };
            return Current;
        }
        if (activeAdapter is null)
            EnterMap(desiredAdapter, game);
        var adapter = activeAdapter!;

        var allObservations = game.LocalPlayer is null
            ? game.ObservedPlayers
            : game.ObservedPlayers.Prepend(game.LocalPlayer).ToArray();
        IReadOnlyList<TrackedPlayer> players;
        try
        {
            players = playerTracker.Update(game.CapturedAtUtc,
                game.LocalPlayer?.EntityId ?? 0, game.TeamStatus.LocalPvPTeam, allObservations);
            Success("PvP team/player tracker", game.CapturedAtUtc,
                $"Tracked {players.Count} entity-keyed player records.");
        }
        catch (Exception ex)
        {
            Failure("PvP team/player tracker", ex);
            players = Current.TrackedPlayers;
        }
        EmitTeamProbe(game);

        var markers = CaptureMarkers(game.CapturedAtUtc);
        var ui = CaptureUi(game.CapturedAtUtc);
        if (!string.IsNullOrWhiteSpace(ui.WideTextAnnouncement) &&
            diffs.ShouldEmit("wide-text", ui.WideTextAnnouncement, game.CapturedAtUtc))
        {
            var announcement = PrivacySanitizer.Sanitize(ui.WideTextAnnouncement);
            Record("frontline_wide_text", game.CapturedAtUtc, new
            {
                text = announcement,
                source = "_WideText",
            });
            developmentLog.Changed("frontline-wide-text", announcement,
                $"_WideText Frontline evidence: {announcement}");
        }
        IReadOnlyList<ObjectiveChange> changes;
        try
        {
            changes = adapter.Update(game.CapturedAtUtc, markers, game.ObjectiveObservations, players);
            Success("Map objective adapter", game.CapturedAtUtc,
                $"{adapter.Name} updated {adapter.Objectives.Count} logical objective records.");
        }
        catch (Exception ex)
        {
            Failure("Map objective adapter", ex);
            changes = [];
        }
        foreach (var change in changes)
        {
            summaryBuilder?.RecordObjectiveChange(change, game.CapturedAtUtc);
            var sanitized = PrivacySanitizer.Sanitize(change.Detail);
            Record(change.EventName, game.CapturedAtUtc, new { objective = change.LogicalId, detail = sanitized });
            developmentLog.Changed(
                $"frontline-{change.EventName}-{change.LogicalId}",
                sanitized,
                $"{change.EventName}: {change.LogicalId}; {sanitized}");
        }

        var objectives = adapter.Objectives;
        var localPosition = game.LocalPlayer?.Position ?? System.Numerics.Vector3.Zero;
        var alliedClusters = BattlefieldClusterer.Build(players, BattlefieldRelationship.AllyConfirmed,
            localPosition, 20f, objectives);
        var enemyClusters = BattlefieldClusterer.Build(players, BattlefieldRelationship.EnemyConfirmed,
            localPosition, 20f, objectives);
        var match = lifecycleTracker.Update(game.IsFrontline, dutyState.IsDutyStarted, ui);
        var death = deathTracker.Update(game.LocalPlayer is not null, game.LocalPlayer?.IsDead == true, game.CapturedAtUtc);
        var combat = combatTracker.Update(game, players, objectives, threat.TargeterCount, threat.NearbyEnemyCount);
        var fresh = players.Where(player => player.IsFresh).ToArray();
        var counts = new RelationshipCounts(
            fresh.Count(player => player.Relationship == BattlefieldRelationship.Self),
            fresh.Count(player => player.Relationship == BattlefieldRelationship.AllyConfirmed),
            fresh.Count(player => player.Relationship == BattlefieldRelationship.EnemyConfirmed),
            fresh.Count(player => player.Relationship == BattlefieldRelationship.Unknown));
        var self = players.FirstOrDefault(player => player.Relationship == BattlefieldRelationship.Self);

        Current = new BattlefieldState(
            game.CapturedAtUtc,
            game.FrontlineMap,
            game.TerritoryId,
            game.MapName,
            adapter.Name,
            self is null ? null : new SelfState(self.EntityId, self.PvPTeam, self.Position, self.IsDead, self.CurrentHp, self.MaxHp),
            game.TeamStatus.LocalPvPTeam,
            players,
            counts,
            alliedClusters,
            enemyClusters,
            objectives,
            adapter.ResearchObjects,
            adapter.ResearchNotes,
            match,
            combat,
            death,
            sensorHealth.Values.OrderBy(sensor => sensor.Name).Select(sensor => sensor.Snapshot()).ToArray(),
            retainedSummary,
            $"{adapter.Name} adapter active; independent sensors remain fail-isolated.");

        EmitRelationshipChanges(players, game.CapturedAtUtc);
        EmitStateChanges(Current);
        summaryBuilder?.Observe(Current);
        return Current;
    }

    public void RecordNavigationEvent(ManualNavigationEvent navigationEvent)
    {
        summaryBuilder?.RecordNavigation(navigationEvent.Name);
        Record(navigationEvent.Name, navigationEvent.AtUtc,
            new { detail = PrivacySanitizer.Sanitize(navigationEvent.Detail) });
        if (navigationEvent.Name == "navigation_arrived" && activeAdapter is not null)
        {
            var id = ParseField(navigationEvent.Detail, "destination");
            var approach = ParseVector(ParseField(navigationEvent.Detail, "approach"));
            if (id is not null && approach is not null)
                activeAdapter.RecordArrived(id, approach.Value, navigationEvent.AtUtc);
        }
    }

    private void EnterMap(IFrontlineMapAdapter adapter, GameStateSnapshot game)
    {
        var now = game.CapturedAtUtc;
        activeAdapter = adapter;
        adapter.Reset(now);
        playerTracker.Reset();
        lifecycleTracker.Reset();
        deathTracker.Reset();
        combatTracker.Reset();
        priorRelationships.Clear();
        lastSuccessfulMarkers = [];
        diffs.Reset();
        foreach (var sensor in sensorHealth.Values)
            sensor.Reset();
        summaryBuilder = new RetainedMatchSummaryBuilder(
            adapter.Map,
            game.TerritoryId,
            game.ContentFinderConditionId,
            now);
        recorder.Start(adapter.Map, now);
        Record("map_entered", now, new
        {
            map = adapter.Map.ToString(),
            adapter = adapter.Name,
            territory = game.TerritoryId,
            duty = game.ContentFinderConditionId,
            territory_name = PrivacySanitizer.Sanitize(game.MapName),
            duty_name = PrivacySanitizer.Sanitize(game.ContentName),
            identity = FrontlineMapCatalog.DescribeIdentity(adapter.Map, game.ContentFinderConditionId, game.TerritoryId),
        });
        log.Information("PvPSentinel FrontlineCore entered {Map} using the {Adapter} adapter.", adapter.Map, adapter.Name);
    }

    private void ExitMap(DateTime now)
    {
        if (activeAdapter is null)
            return;
        Record("map_exited", now, new { map = activeAdapter.Map.ToString() });
        if (summaryBuilder is not null)
        {
            retainedSummary = summaryBuilder.Build(now);
            recorder.Complete(retainedSummary);
        }
        activeAdapter = null;
        summaryBuilder = null;
        playerTracker.Reset();
        lifecycleTracker.Reset();
        deathTracker.Reset();
        combatTracker.Reset();
        priorRelationships.Clear();
        lastSuccessfulMarkers = [];
    }

    private IReadOnlyList<FrontlineMapMarkerObservation> CaptureMarkers(DateTime now)
    {
        try
        {
            var value = markerSensor.Capture();
            lastSuccessfulMarkers = value;
            var identities = string.Join(", ", value
                .Select(marker => $"{marker.IconId}/{marker.DataId}/{marker.ObjectiveId}")
                .Distinct(StringComparer.Ordinal)
                .Take(12));
            Success("AgentMap.EventMarkers", now,
                $"Observed {value.Count} global records; first distinct icon/data/objective IDs: {(identities.Length == 0 ? "NONE" : identities)}.");
            return value;
        }
        catch (Exception ex)
        {
            Failure("AgentMap.EventMarkers", ex);
            return lastSuccessfulMarkers;
        }
    }

    private FrontlineUiObservation CaptureUi(DateTime now)
    {
        try
        {
            var value = uiSensor.Capture();
            Success("Frontline addons", now, value.Evidence);
            return value;
        }
        catch (Exception ex)
        {
            Failure("Frontline addons", ex);
            return new FrontlineUiObservation(false, false, null, null, [], string.Empty,
                $"Frontline addon scan failed independently ({ex.GetType().Name}).");
        }
    }

    private void EmitRelationshipChanges(IReadOnlyList<TrackedPlayer> players, DateTime now)
    {
        foreach (var player in players.Where(player => player.IsFresh))
        {
            if (!priorRelationships.TryGetValue(player.EntityId, out var prior) || prior != player.Relationship)
            {
                Record("relationship_changed", now, new
                {
                    entity = $"0x{player.EntityId:X8}",
                    from = priorRelationships.ContainsKey(player.EntityId) ? prior.ToString() : "UNSEEN",
                    to = player.Relationship.ToString(),
                    pvp_team = player.PvPTeam,
                    confidence = player.Confidence.ToString(),
                });
                priorRelationships[player.EntityId] = player.Relationship;
            }
        }
    }

    private void EmitTeamProbe(GameStateSnapshot game)
    {
        var local = game.LocalPlayer;
        if (local is null)
            return;

        var teamHistogram = game.ObservedPlayers
            .GroupBy(player => player.PvPTeam)
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key.ToString(CultureInfo.InvariantCulture), group => group.Count());
        var rosterTeamHistogram = game.ObservedPlayers
            .Where(player => player.IsRosterMember)
            .GroupBy(player => player.PvPTeam)
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key.ToString(CultureInfo.InvariantCulture), group => group.Count());
        var statusHistogram = game.ObservedPlayers
            .GroupBy(player => (uint)player.StatusFlags)
            .OrderByDescending(group => group.Count())
            .Take(12)
            .ToDictionary(group => $"0x{group.Key:X8}", group => group.Count());
        var signature = string.Join('|', new[]
        {
            local.PvPTeam.ToString(CultureInfo.InvariantCulture),
            ((uint)local.StatusFlags).ToString("X8", CultureInfo.InvariantCulture),
            game.TeamStatus.IsAlliance.ToString(),
            game.TeamStatus.DeclaredMemberCount.ToString(CultureInfo.InvariantCulture),
            game.TeamStatus.ResolvedMemberCount.ToString(CultureInfo.InvariantCulture),
            string.Join(',', teamHistogram.Select(pair => $"{pair.Key}:{pair.Value}")),
            string.Join(',', rosterTeamHistogram.Select(pair => $"{pair.Key}:{pair.Value}")),
            game.ObservedPlayers.Count(player => player.HostileFlag).ToString(CultureInfo.InvariantCulture),
        });
        if (!diffs.ShouldEmit("pvp-team-probe", signature, game.CapturedAtUtc))
            return;

        Record("pvp_team_probe", game.CapturedAtUtc, new
        {
            local_entity = $"0x{local.EntityId:X8}",
            raw_local_battalion = local.PvPTeam,
            local_status_flags = $"0x{(uint)local.StatusFlags:X8}",
            local_party_flag = local.PartyMemberFlag,
            local_alliance_flag = local.AllianceMemberFlag,
            local_hostile_flag = local.HostileFlag,
            local_targetable = local.IsTargetable,
            alliance_roster = game.TeamStatus.IsAlliance,
            declared_roster_members = game.TeamStatus.DeclaredMemberCount,
            resolved_roster_members = game.TeamStatus.ResolvedMemberCount,
            observed_team_histogram = teamHistogram,
            roster_team_histogram = rosterTeamHistogram,
            observed_status_flag_histogram = statusHistogram,
            observed_hostile_flags = game.ObservedPlayers.Count(player => player.HostileFlag),
            observed_targetable = game.ObservedPlayers.Count(player => player.IsTargetable),
            classification_reliable = game.IsClassificationReliable,
            conclusion = game.IsClassificationReliable
                ? "positive local Battalion available"
                : "UNRESOLVED: raw local Battalion is not positive; no roster/team inference applied",
        });
    }

    private void EmitStateChanges(BattlefieldState state)
    {
        var now = state.CapturedAtUtc;
        if (priorLifecycle != state.Match.Lifecycle)
        {
            Record("match_lifecycle_changed", now, new { from = priorLifecycle.ToString(), to = state.Match.Lifecycle.ToString() });
            priorLifecycle = state.Match.Lifecycle;
        }
        if (priorDeathState != state.DeathRespawn.State)
        {
            if (state.DeathRespawn.State == DeathRespawnState.Dead) Record("death", now);
            if (state.DeathRespawn.State == DeathRespawnState.Respawned) Record("respawn", now);
            priorDeathState = state.DeathRespawn.State;
        }
        if (priorCombatContext != state.Combat.Context)
        {
            Record("combat_context_changed", now, new
            {
                from = priorCombatContext.ToString(),
                to = state.Combat.Context.ToString(),
                targeting_us = state.Combat.EnemiesTargetingUs,
                nearby_enemies = state.Combat.NearbyEnemies,
            });
            priorCombatContext = state.Combat.Context;
        }
        var clusterSignature = string.Join('|', state.AlliedClusters.Select(cluster =>
            $"A{cluster.Id}:{cluster.MemberCount}:{cluster.Centroid.X:F0}:{cluster.Centroid.Z:F0}").Concat(
            state.EnemyClusters.Select(cluster => $"E{cluster.Id}:{cluster.MemberCount}:{cluster.Centroid.X:F0}:{cluster.Centroid.Z:F0}")));
        if (diffs.ShouldEmit("clusters", clusterSignature, now))
            Record("cluster_changed", now, new { allied = state.AlliedClusters.Count, enemy = state.EnemyClusters.Count, signature = clusterSignature });
    }

    private void Success(string sensor, DateTime now, string detail)
    {
        var value = sensorHealth[sensor];
        value.Status = SensorStatus.Healthy;
        value.LastSuccessUtc = now;
        value.Detail = PrivacySanitizer.Sanitize(detail);
    }

    private void Failure(string sensor, Exception ex)
    {
        var value = sensorHealth[sensor];
        value.Status = value.LastSuccessUtc is null ? SensorStatus.Failed : SensorStatus.Degraded;
        value.ErrorCount++;
        value.Detail = $"{ex.GetType().Name}: {PrivacySanitizer.Sanitize(ex.Message)}";
        developmentLog.Throttled($"sensor-{sensor}", $"{sensor} failed independently: {value.Detail}");
    }

    private void Record(string eventName, DateTime now, object? data = null) => recorder.Record(eventName, now, data);

    private static string? ParseField(string text, string field) => text.Split(';')
        .Select(part => part.Trim())
        .FirstOrDefault(part => part.StartsWith(field + "=", StringComparison.OrdinalIgnoreCase))?
        .Split('=', 2)[1];

    private static System.Numerics.Vector3? ParseVector(string? value)
    {
        if (value is null)
            return null;
        var parts = value.Trim('(', ')').Split(',');
        return parts.Length == 3 &&
               float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
               float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
               float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
            ? new System.Numerics.Vector3(x, y, z)
            : null;
    }

    private sealed class MutableSensorHealth(string name)
    {
        public string Name { get; } = name;
        public SensorStatus Status { get; set; } = SensorStatus.Unavailable;
        public DateTime? LastSuccessUtc { get; set; }
        public int ErrorCount { get; set; }
        public string Detail { get; set; } = "UNRESOLVED";
        public SensorHealthEntry Snapshot() => new(Name, Status, LastSuccessUtc, ErrorCount, Detail);
        public void Reset()
        {
            Status = SensorStatus.Unavailable;
            LastSuccessUtc = null;
            ErrorCount = 0;
            Detail = "UNRESOLVED";
        }
    }
}
