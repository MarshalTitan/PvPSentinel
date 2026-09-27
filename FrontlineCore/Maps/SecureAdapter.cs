using System.Globalization;
using System.Numerics;
using PvPSentinel.FrontlineCore.Diagnostics;
using PvPSentinel.Models;

namespace PvPSentinel.FrontlineCore.Maps;

/// <summary>
/// Discovery-first Borderland Ruins adapter. It intentionally preserves marker and
/// physical-object evidence without assigning community-derived objective meaning.
/// </summary>
internal sealed class SecureAdapter : FrontlineMapAdapterBase
{
    public const uint TerritoryId = 1273;
    public const uint ContentFinderConditionId = 127;
    private static readonly TimeSpan MissingEvidenceDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CandidateRetention = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RawFamilyReportInterval = TimeSpan.FromSeconds(30);
    private const int MaximumTrackedPromotionCandidates = 128;
    private const int MaximumResearchObjects = 256;
    private readonly Dictionary<string, MarkerCandidate> markerCandidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RawMarkerFamily> rawMarkerFamilies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> logicalIdByPosition = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableResearchObject> researchObjects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> physicalResearchIdByLocation = new(StringComparer.OrdinalIgnoreCase);
    private int nextLocationIndex = 1;
    private int nextResearchIndex = 1;
    private int droppedResearchObjects;

    public override FrontlineMap Map => FrontlineMap.BorderlandRuins;
    public override string Name => "Borderland Ruins (Secure discovery)";
    public override IReadOnlyList<FrontlineResearchObject> ResearchObjects => researchObjects.Values
        .OrderBy(item => item.SortOrder)
        .Select(item => item.Snapshot())
        .ToArray();
    public override IReadOnlyList<string> ResearchNotes =>
    [
        "Secure objective names, types, ownership, active state, and tactical meaning remain UNRESOLVED.",
        $"Promotion tiers: objective signal {SecureObjectiveAggregator.ObjectiveSignalStableScans} scans/{SecureObjectiveAggregator.ObjectiveSignalStableAge.TotalSeconds:F0}s; durable unknown {SecureObjectiveAggregator.DurableEvidenceStableScans} scans/{SecureObjectiveAggregator.DurableEvidenceStableAge.TotalSeconds:F0}s; physical corroboration {SecureObjectiveAggregator.PhysicalEvidenceStableScans} scans/{SecureObjectiveAggregator.PhysicalEvidenceStableAge.TotalSeconds:F0}s.",
        $"Observed {markerCandidates.Count} promotion candidate(s) and {rawMarkerFamilies.Count} raw-only family/families; promoted {Records.Count}/{SecureObjectiveAggregator.MaximumPromotedLocations} bounded SEC location(s); retained {researchObjects.Count}/{MaximumResearchObjects} physical research object(s), dropped {droppedResearchObjects} after the bound.",
        RawFamilySummary(),
        "Marker/object disappearance means no longer observed; it is not interpreted as deactivation or destruction.",
    ];

    public override void Reset(DateTime now)
    {
        Records.Clear();
        markerCandidates.Clear();
        rawMarkerFamilies.Clear();
        logicalIdByPosition.Clear();
        researchObjects.Clear();
        physicalResearchIdByLocation.Clear();
        nextLocationIndex = 1;
        nextResearchIndex = 1;
        droppedResearchObjects = 0;
    }

    public override IReadOnlyList<ObjectiveChange> Update(
        DateTime now,
        IReadOnlyList<FrontlineMapMarkerObservation> markers,
        IReadOnlyList<ObjectiveObservation> physicalObservations,
        IReadOnlyList<TrackedPlayer> players)
    {
        var changes = new List<ObjectiveChange>();
        UpdateMarkers(now, markers, physicalObservations, changes);
        UpdateResearchObjects(now, physicalObservations, changes);
        AssociatePhysicalEvidence(now, changes);

        foreach (var record in Records.Values)
        {
            var nearby = CountNearby(record.ReferencePosition, players);
            record.NearbyAllies = nearby.Allies;
            record.NearbyEnemies = nearby.Enemies;
        }

        return changes;
    }

    private void UpdateMarkers(
        DateTime now,
        IReadOnlyList<FrontlineMapMarkerObservation> markers,
        IReadOnlyList<ObjectiveObservation> physicalObservations,
        List<ObjectiveChange> changes)
    {
        var aggregates = SecureObjectiveAggregator.Aggregate(markers);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var aggregate in aggregates)
        {
            var physicallyCorroborated = physicalObservations.Any(observation =>
                HorizontalDistance(observation.Position, aggregate.Position) <= 8f &&
                Math.Abs(observation.Position.Y - aggregate.Position.Y) <= 5f);
            var promotionClass = SecureObjectiveAggregator.ClassifyPromotionEvidence(
                aggregate,
                physicallyCorroborated);
            if (promotionClass == SecureMarkerPromotionClass.RawObservationOnly)
            {
                ObserveRawFamily(now, aggregate, changes);
                continue;
            }

            var candidateKey = ResolveCandidateKey(aggregate.Position, seenKeys) ?? aggregate.PositionKey;
            seenKeys.Add(candidateKey);
            var sanitizedEvidence = PrivacySanitizer.Sanitize(aggregate.EvidenceSummary);
            if (!markerCandidates.TryGetValue(candidateKey, out var candidate))
            {
                if (markerCandidates.Count >= MaximumTrackedPromotionCandidates)
                {
                    ObserveRawFamily(now, aggregate, changes, "promotion-candidate-cap");
                    continue;
                }
                candidate = new MarkerCandidate(
                    candidateKey,
                    aggregate.Position,
                    now,
                    now,
                    1,
                    aggregate.EvidenceFingerprint,
                    sanitizedEvidence,
                    true,
                    promotionClass,
                    aggregate.MarkerFamilyFingerprint);
                markerCandidates[candidateKey] = candidate;
                changes.Add(new ObjectiveChange(
                    "secure_marker_candidate_appeared",
                    $"UNRESOLVED@{candidateKey}",
                    $"position={FormatVector(aggregate.Position)}; class={promotionClass}; records={aggregate.EvidenceCount}; evidence={sanitizedEvidence}"));
            }
            else
            {
                if (!candidate.CurrentlyObserved)
                {
                    candidate.CurrentlyObserved = true;
                    candidate.StabilityStartedUtc = now;
                    candidate.ScanCount = 0;
                    changes.Add(new ObjectiveChange(
                        "secure_marker_candidate_reappeared",
                        LogicalOrCandidateId(candidate.PositionKey),
                        $"position={FormatVector(aggregate.Position)}; evidence={sanitizedEvidence}"));
                }
                var familyChanged = !string.Equals(
                    candidate.MarkerFamilyFingerprint,
                    aggregate.MarkerFamilyFingerprint,
                    StringComparison.Ordinal);
                var drifted = HorizontalDistance(candidate.AnchorPosition, aggregate.Position) > 1.25f;
                candidate.Position = aggregate.Position;
                candidate.LastSeenUtc = now;
                candidate.ScanCount++;
                if (promotionClass != candidate.PromotionClass || familyChanged || drifted)
                {
                    candidate.PromotionClass = promotionClass;
                    candidate.MarkerFamilyFingerprint = aggregate.MarkerFamilyFingerprint;
                    candidate.AnchorPosition = aggregate.Position;
                    candidate.StabilityStartedUtc = now;
                    candidate.ScanCount = 1;
                }
                if (!string.Equals(candidate.EvidenceFingerprint, aggregate.EvidenceFingerprint, StringComparison.Ordinal))
                {
                    var before = candidate.EvidenceSummary;
                    candidate.EvidenceFingerprint = aggregate.EvidenceFingerprint;
                    candidate.EvidenceSummary = sanitizedEvidence;
                    changes.Add(new ObjectiveChange(
                        "secure_observable_transition",
                        LogicalOrCandidateId(candidate.PositionKey),
                        $"before={before}; after={sanitizedEvidence}; position={FormatVector(aggregate.Position)}"));
                }
            }

            if (!logicalIdByPosition.TryGetValue(candidate.PositionKey, out var logicalId) &&
                Records.Count < SecureObjectiveAggregator.MaximumPromotedLocations &&
                SecureObjectiveAggregator.IsStable(
                    candidate.StabilityStartedUtc,
                    candidate.LastSeenUtc,
                    candidate.ScanCount,
                    candidate.PromotionClass))
            {
                logicalId = $"SEC-{nextLocationIndex:00}";
                logicalIdByPosition[candidate.PositionKey] = logicalId;
                Records[logicalId] = new MutableObjective(logicalId, "UNRESOLVED", "UNRESOLVED", nextLocationIndex)
                {
                    ReferencePosition = candidate.Position,
                    State = ObjectiveLifecycle.Unknown,
                    Rank = "UNRESOLVED",
                    Owner = ObjectiveOwner.Unresolved,
                    ObservedGrandCompany = "UNRESOLVED",
                    StateId = 0,
                    SensorSource = "AgentMap.EventMarkers (Secure discovery; meaning unresolved)",
                    Confidence = SensorConfidence.RuntimeDiscovery,
                    FirstSeenUtc = candidate.FirstSeenUtc,
                    LastSeenUtc = now,
                    Evidence = candidate.EvidenceSummary,
                };
                nextLocationIndex++;
                changes.Add(new ObjectiveChange(
                    "secure_location_discovered",
                    logicalId,
                    $"position={FormatVector(candidate.Position)}; promotion={candidate.PromotionClass}; stable_scans={candidate.ScanCount}; stable_age={(candidate.LastSeenUtc - candidate.StabilityStartedUtc).TotalSeconds:F1}s; evidence={candidate.EvidenceSummary}"));
            }
            else if (!logicalIdByPosition.ContainsKey(candidate.PositionKey) &&
                     Records.Count >= SecureObjectiveAggregator.MaximumPromotedLocations &&
                     !candidate.PromotionCeilingReported)
            {
                candidate.PromotionCeilingReported = true;
                changes.Add(new ObjectiveChange(
                    "secure_location_promotion_rejected",
                    $"UNRESOLVED@{candidate.PositionKey}",
                    $"reason=sanity ceiling {SecureObjectiveAggregator.MaximumPromotedLocations}; position={FormatVector(candidate.Position)}; class={candidate.PromotionClass}; evidence={candidate.EvidenceSummary}"));
            }

            if (logicalId is not null && Records.TryGetValue(logicalId, out var record))
            {
                record.ReferencePosition = candidate.Position;
                record.LastSeenUtc = now;
                record.Evidence = candidate.EvidenceSummary;
                record.SensorSource = "AgentMap.EventMarkers (Secure discovery; meaning unresolved)";
            }
        }

        foreach (var candidate in markerCandidates.Values)
        {
            if (seenKeys.Contains(candidate.PositionKey) || !candidate.CurrentlyObserved ||
                now - candidate.LastSeenUtc < MissingEvidenceDelay)
                continue;
            candidate.CurrentlyObserved = false;
            changes.Add(new ObjectiveChange(
                "secure_marker_candidate_unobserved",
                LogicalOrCandidateId(candidate.PositionKey),
                $"last_position={FormatVector(candidate.Position)}; last_evidence={candidate.EvidenceSummary}; no lifecycle meaning inferred"));
            if (logicalIdByPosition.TryGetValue(candidate.PositionKey, out var logicalId) &&
                Records.TryGetValue(logicalId, out var record))
            {
                record.SensorSource = "Secure marker currently unobserved (not interpreted)";
                record.Evidence = $"last observed: {candidate.EvidenceSummary}";
            }
        }

        foreach (var staleKey in markerCandidates.Values
                     .Where(candidate => !candidate.CurrentlyObserved &&
                                         !logicalIdByPosition.ContainsKey(candidate.PositionKey) &&
                                         now - candidate.LastSeenUtc >= CandidateRetention)
                     .Select(candidate => candidate.PositionKey)
                     .ToArray())
            markerCandidates.Remove(staleKey);
    }

    private void ObserveRawFamily(
        DateTime now,
        SecureMarkerAggregate aggregate,
        List<ObjectiveChange> changes,
        string reason = "raw-observation-only")
    {
        foreach (var familyGroup in aggregate.Observations.GroupBy(SecureObjectiveAggregator.FamilyKey, StringComparer.Ordinal))
        {
            var familyKey = familyGroup.Key;
            var sample = familyGroup.First();
            if (!rawMarkerFamilies.TryGetValue(familyKey, out var family))
            {
                family = new RawMarkerFamily(familyKey, now);
                rawMarkerFamilies[familyKey] = family;
            }

            family.ObservationCount += familyGroup.Count();
            family.ScanCount++;
            family.LastSeenUtc = now;
            family.LastPosition = aggregate.Position;
            family.Reason = reason;
            family.AddSample(aggregate.Position);

            if (family.LastReportedUtc != DateTime.MinValue &&
                now - family.LastReportedUtc < RawFamilyReportInterval)
                continue;

            family.LastReportedUtc = now;
            var sanitizedText = PrivacySanitizer.Sanitize(sample.Tooltip);
            changes.Add(new ObjectiveChange(
                "secure_raw_marker_family_observed",
                $"RAW:{familyKey}",
                $"reason={reason}; observations={family.ObservationCount}; scans={family.ScanCount}; last_position={FormatVector(aggregate.Position)}; samples=[{string.Join(',', family.SamplePositions.Select(FormatVector))}]; text={(string.IsNullOrWhiteSpace(sanitizedText) ? "<none>" : sanitizedText)}"));
        }
    }

    private string RawFamilySummary()
    {
        if (rawMarkerFamilies.Count == 0)
            return "Raw-only marker evidence: none observed.";
        return "Raw-only marker families (not clickable): " + string.Join("; ", rawMarkerFamilies.Values
            .OrderByDescending(family => family.ObservationCount)
            .Take(8)
            .Select(family => $"{family.FamilyKey}={family.ObservationCount}"));
    }

    private void UpdateResearchObjects(
        DateTime now,
        IReadOnlyList<ObjectiveObservation> observations,
        List<ObjectiveChange> changes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observation in observations.Take(512))
        {
            var key = PhysicalKey(observation);
            seen.Add(key);
            var sanitizedName = PrivacySanitizer.Sanitize(observation.Name);
            var signature = PhysicalSignature(observation, sanitizedName);
            var evidence = PhysicalEvidence(observation, sanitizedName);
            if (!researchObjects.TryGetValue(key, out var record))
            {
                if (researchObjects.Count >= MaximumResearchObjects)
                {
                    var eviction = researchObjects
                        .Where(pair => !pair.Value.CurrentlyObserved)
                        .OrderBy(pair => pair.Value.LastSeenUtc)
                        .Select(pair => pair.Key)
                        .FirstOrDefault();
                    if (eviction is not null)
                        researchObjects.Remove(eviction);
                }
                if (researchObjects.Count >= MaximumResearchObjects)
                {
                    droppedResearchObjects++;
                    continue;
                }
                record = new MutableResearchObject(
                    $"SEC-OBJ-{nextResearchIndex:000}",
                    nextResearchIndex,
                    sanitizedName,
                    observation,
                    now,
                    signature,
                    evidence);
                nextResearchIndex++;
                researchObjects[key] = record;
                changes.Add(new ObjectiveChange(
                    "secure_research_object_appeared",
                    record.ResearchId,
                    evidence));
                continue;
            }

            if (!record.CurrentlyObserved)
            {
                record.CurrentlyObserved = true;
                changes.Add(new ObjectiveChange(
                    "secure_research_object_reappeared",
                    record.ResearchId,
                    evidence));
            }
            if (!string.Equals(record.Signature, signature, StringComparison.Ordinal))
            {
                var before = record.Evidence;
                changes.Add(new ObjectiveChange(
                    "secure_research_object_changed",
                    record.ResearchId,
                    $"before={before}; after={evidence}"));
            }
            record.Update(sanitizedName, observation, now, signature, evidence);
        }

        foreach (var pair in researchObjects)
        {
            var record = pair.Value;
            if (seen.Contains(pair.Key) || !record.CurrentlyObserved || now - record.LastSeenUtc < MissingEvidenceDelay)
                continue;
            record.CurrentlyObserved = false;
            changes.Add(new ObjectiveChange(
                "secure_research_object_unobserved",
                record.ResearchId,
                $"last={record.Evidence}; no lifecycle meaning inferred"));
        }
    }

    private void AssociatePhysicalEvidence(DateTime now, List<ObjectiveChange> changes)
    {
        var observed = researchObjects.Values.Where(item => item.CurrentlyObserved).ToArray();
        foreach (var objective in Records.Values)
        {
            if (objective.ReferencePosition is not { } position)
                continue;
            var nearest = observed
                .Select(item => (Item: item, Distance: HorizontalDistance(position, item.Position)))
                .Where(item => item.Distance <= 15f)
                .OrderBy(item => item.Distance)
                .FirstOrDefault();
            if (nearest.Item is null)
            {
                objective.PhysicalConfirmation = null;
                var physicalSeparator = objective.Evidence.IndexOf(" | physical=", StringComparison.Ordinal);
                if (physicalSeparator >= 0)
                    objective.Evidence = objective.Evidence[..physicalSeparator];
                if (physicalResearchIdByLocation.Remove(objective.LogicalId, out var previous))
                {
                    changes.Add(new ObjectiveChange(
                        "secure_location_physical_unobserved",
                        objective.LogicalId,
                        $"research_object={previous}; proximity evidence disappeared; no lifecycle meaning inferred"));
                }
                continue;
            }

            var item = nearest.Item;
            objective.PhysicalConfirmation = new ObjectivePhysicalConfirmation(
                item.GameObjectId,
                item.EntityId,
                item.BaseId,
                item.Position,
                item.CurrentHp,
                item.MaxHp,
                item.IsTargetable,
                now);
            var separator = objective.Evidence.IndexOf(" | physical=", StringComparison.Ordinal);
            var markerEvidence = separator >= 0 ? objective.Evidence[..separator] : objective.Evidence;
            objective.Evidence = $"{markerEvidence} | physical={item.ResearchId}: {item.Evidence}";
            if (!physicalResearchIdByLocation.TryGetValue(objective.LogicalId, out var previousId) ||
                !string.Equals(previousId, item.ResearchId, StringComparison.Ordinal))
            {
                physicalResearchIdByLocation[objective.LogicalId] = item.ResearchId;
                changes.Add(new ObjectiveChange(
                    "secure_location_physical_evidence",
                    objective.LogicalId,
                    $"research_object={item.ResearchId}; distance={nearest.Distance:F1}; evidence={item.Evidence}"));
            }
        }
    }

    private string LogicalOrCandidateId(string positionKey) =>
        logicalIdByPosition.GetValueOrDefault(positionKey, $"UNRESOLVED@{positionKey}");

    private string? ResolveCandidateKey(Vector3 position, IReadOnlySet<string> alreadySeen) =>
        markerCandidates.Values
            .Where(candidate => !alreadySeen.Contains(candidate.PositionKey) &&
                                Math.Abs(candidate.Position.Y - position.Y) <= 3f)
            .Select(candidate => (candidate.PositionKey, Distance: HorizontalDistance(candidate.Position, position)))
            .Where(item => item.Distance <= 1.25f)
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.PositionKey, StringComparer.Ordinal)
            .Select(item => item.PositionKey)
            .FirstOrDefault();

    private static string PhysicalKey(ObjectiveObservation observation) => observation.GameObjectId != 0
        ? $"object:{observation.GameObjectId:X16}"
        : $"fallback:{observation.BaseId}:{SecureObjectiveAggregator.PositionKey(observation.Position)}";

    private static string PhysicalSignature(ObjectiveObservation observation, string sanitizedName) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{observation.EntityId:X8}|{observation.BaseId}|{sanitizedName}|{observation.ObjectKind}|{observation.Position.X:F1}|{observation.Position.Y:F1}|{observation.Position.Z:F1}|{observation.IsTargetable}|{observation.IsDead}|{observation.CurrentHp}|{observation.MaxHp}");

    private static string PhysicalEvidence(ObjectiveObservation observation, string sanitizedName) =>
        string.Create(CultureInfo.InvariantCulture,
            $"object=0x{observation.GameObjectId:X16}; entity=0x{observation.EntityId:X8}; base={observation.BaseId}; kind={observation.ObjectKind}; name={sanitizedName}; position={FormatVector(observation.Position)}; targetable={observation.IsTargetable}; dead={observation.IsDead}; hp={observation.CurrentHp}/{observation.MaxHp}");

    private static string FormatVector(Vector3 position) =>
        string.Create(CultureInfo.InvariantCulture, $"({position.X:F1},{position.Y:F1},{position.Z:F1})");

    private sealed class MarkerCandidate(
        string positionKey,
        Vector3 position,
        DateTime firstSeenUtc,
        DateTime lastSeenUtc,
        int scanCount,
        string evidenceFingerprint,
        string evidenceSummary,
        bool currentlyObserved,
        SecureMarkerPromotionClass promotionClass,
        string markerFamilyFingerprint)
    {
        public string PositionKey { get; } = positionKey;
        public Vector3 Position { get; set; } = position;
        public Vector3 AnchorPosition { get; set; } = position;
        public DateTime FirstSeenUtc { get; } = firstSeenUtc;
        public DateTime StabilityStartedUtc { get; set; } = firstSeenUtc;
        public DateTime LastSeenUtc { get; set; } = lastSeenUtc;
        public int ScanCount { get; set; } = scanCount;
        public string EvidenceFingerprint { get; set; } = evidenceFingerprint;
        public string EvidenceSummary { get; set; } = evidenceSummary;
        public bool CurrentlyObserved { get; set; } = currentlyObserved;
        public SecureMarkerPromotionClass PromotionClass { get; set; } = promotionClass;
        public string MarkerFamilyFingerprint { get; set; } = markerFamilyFingerprint;
        public bool PromotionCeilingReported { get; set; }
    }

    private sealed class RawMarkerFamily(string familyKey, DateTime firstSeenUtc)
    {
        private const int MaximumSamples = 8;
        public string FamilyKey { get; } = familyKey;
        public DateTime FirstSeenUtc { get; } = firstSeenUtc;
        public DateTime LastSeenUtc { get; set; } = firstSeenUtc;
        public DateTime LastReportedUtc { get; set; } = DateTime.MinValue;
        public int ObservationCount { get; set; }
        public int ScanCount { get; set; }
        public Vector3 LastPosition { get; set; }
        public string Reason { get; set; } = "raw-observation-only";
        public List<Vector3> SamplePositions { get; } = [];

        public void AddSample(Vector3 position)
        {
            if (SamplePositions.Any(sample => HorizontalDistance(sample, position) <= 2f))
                return;
            if (SamplePositions.Count >= MaximumSamples)
                SamplePositions.RemoveAt(0);
            SamplePositions.Add(position);
        }
    }

    private sealed class MutableResearchObject(
        string researchId,
        int sortOrder,
        string name,
        ObjectiveObservation observation,
        DateTime now,
        string signature,
        string evidence)
    {
        public string ResearchId { get; } = researchId;
        public int SortOrder { get; } = sortOrder;
        public string Name { get; private set; } = name;
        public string ObjectKind { get; private set; } = observation.ObjectKind;
        public ulong GameObjectId { get; private set; } = observation.GameObjectId;
        public uint EntityId { get; private set; } = observation.EntityId;
        public uint BaseId { get; private set; } = observation.BaseId;
        public Vector3 Position { get; private set; } = observation.Position;
        public bool IsTargetable { get; private set; } = observation.IsTargetable;
        public bool IsDead { get; private set; } = observation.IsDead;
        public uint CurrentHp { get; private set; } = observation.CurrentHp;
        public uint MaxHp { get; private set; } = observation.MaxHp;
        public bool CurrentlyObserved { get; set; } = true;
        public DateTime FirstSeenUtc { get; } = now;
        public DateTime LastSeenUtc { get; private set; } = now;
        public string Signature { get; private set; } = signature;
        public string Evidence { get; private set; } = evidence;

        public void Update(
            string name,
            ObjectiveObservation observation,
            DateTime now,
            string signature,
            string evidence)
        {
            Name = name;
            ObjectKind = observation.ObjectKind;
            GameObjectId = observation.GameObjectId;
            EntityId = observation.EntityId;
            BaseId = observation.BaseId;
            Position = observation.Position;
            IsTargetable = observation.IsTargetable;
            IsDead = observation.IsDead;
            CurrentHp = observation.CurrentHp;
            MaxHp = observation.MaxHp;
            LastSeenUtc = now;
            Signature = signature;
            Evidence = evidence;
            CurrentlyObserved = true;
        }

        public FrontlineResearchObject Snapshot() => new(
            ResearchId,
            Name,
            ObjectKind,
            GameObjectId,
            EntityId,
            BaseId,
            Position,
            IsTargetable,
            IsDead,
            CurrentHp,
            MaxHp,
            CurrentlyObserved,
            FirstSeenUtc,
            LastSeenUtc,
            Evidence);
    }
}
